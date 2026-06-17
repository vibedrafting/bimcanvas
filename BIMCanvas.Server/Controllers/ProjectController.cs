using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BIMCanvas.Core.Algorithms.Spatial;
using BIMCanvas.Core.Converters.Json;
using BIMCanvas.Core.Models.Computed;
using BIMCanvas.Core.Models.Layout;
using BIMCanvas.Core.Models.Project;
using BIMCanvas.Core.Models.Revit;
using BIMCanvas.Core.Models.Semantic;
using BIMCanvas.Server.Dtos;
using BIMCanvas.Server.Models.Plugins;
using BIMCanvas.Server.Services;
using BIMCanvas.Server.Services.Plugins;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace BIMCanvas.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProjectController : ControllerBase
    {
        private readonly ILogger<ProjectController> _logger;
        private readonly ProjectContext _projectContext;
        private readonly ProjectService _projectService;
        private readonly RecentProjectsService _recentProjectsService;
        private readonly GitWorktreeService _gitService;
        private readonly AgentClientService _agentClientService;
        private readonly ProjectWatcherService _projectWatcherService;
        private readonly ModulesReaderService _modulesReader;
        private readonly ModulesWriterService _modulesWriter;
        private readonly ModuleFileTopologyService _moduleFileTopology;
        private readonly PluginLifecycleService _pluginLifecycle;
        private readonly ProjectFixedFilesBootstrapService _projectFixedFiles;
        private readonly JsonSerializerSettings _jsonSettings;

        public ProjectController(
            ILogger<ProjectController> logger,
            ProjectContext projectContext,
            ProjectService projectService,
            RecentProjectsService recentProjectsService,
            GitWorktreeService gitService,
            AgentClientService agentClientService,
            ProjectWatcherService projectWatcherService,
            ModulesReaderService modulesReader,
            ModulesWriterService modulesWriter,
            ModuleFileTopologyService moduleFileTopology,
            PluginLifecycleService pluginLifecycle,
            ProjectFixedFilesBootstrapService projectFixedFiles)
        {
            _logger = logger;
            _projectContext = projectContext;
            _projectService = projectService;
            _recentProjectsService = recentProjectsService;
            _gitService = gitService;
            _agentClientService = agentClientService;
            _projectWatcherService = projectWatcherService;
            _modulesReader = modulesReader;
            _modulesWriter = modulesWriter;
            _moduleFileTopology = moduleFileTopology;
            _pluginLifecycle = pluginLifecycle;
            _projectFixedFiles = projectFixedFiles;
            _jsonSettings = new JsonSerializerSettings
            {
                ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
                Converters = { new Polygon2DConverter(), new FacingConverter() }
            };
        }

        /// <summary>
        /// 获取当前项目数据（单项目模式：无需 path 参数）
        /// 支持多窗口 Worktree 隔离：优先从活跃窗口的 Worktree 读取数据
        /// </summary>
        /// <returns>聚合后的 ProjectData</returns>
        [HttpGet]
        public ActionResult<ProjectData> GetProjectData()
        {
            if (!_projectContext.IsLoaded)
            {
                return BadRequest(new { message = "没有加载的项目" });
            }

            if (ClearProjectContextIfMissing())
            {
                return NotFound(new { message = "当前项目目录不存在，已关闭项目" });
            }

            // 优先使用活跃窗口的 Worktree 路径，否则回退到主仓库路径
            var loadPath = _projectContext.GetActiveWorktreePath()
                           ?? _projectContext.CurrentProjectPath!;

            if (!Directory.Exists(loadPath))
            {
                return NotFound($"项目目录不存在: {loadPath}");
            }

            try
            {
                _logger.LogInformation("加载项目数据: {Path} (Window: {WindowId})",
                    loadPath, _projectContext.ActiveWindowId ?? "主窗口");
                var projectData = LoadProjectData(loadPath);
                return Ok(projectData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加载项目数据失败: {Path}", loadPath);
                return StatusCode(500, new { message = $"加载项目数据失败: {ex.Message}" });
            }
        }

        /// <summary>
        /// 获取当前项目状态
        /// </summary>
        [HttpGet("status")]
        public ActionResult GetProjectStatus()
        {
            var projectMissing = ClearProjectContextIfMissing();

            return Ok(new
            {
                isLoaded = _projectContext.IsLoaded,
                projectPath = _projectContext.CurrentProjectPath,
                sourceBcpPath = _projectContext.SourceBcpPath,
                projectMissing
            });
        }

        /// <summary>
        /// 扫描项目列表
        /// </summary>
        [HttpGet("list")]
        public ActionResult<List<ProjectSummary>> ListProjects()
        {
            try
            {
                var projects = _projectService.ScanProjects();
                return Ok(projects);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "扫描项目列表失败");
                return StatusCode(500, new { message = $"扫描项目列表失败: {ex.Message}" });
            }
        }

        /// <summary>
        /// 打开已有项目文件夹
        /// </summary>
        [HttpPost("open-folder")]
        public ActionResult<ProjectLoadResult> OpenFolder([FromBody] OpenFolderRequest request)
        {
            if (string.IsNullOrEmpty(request.FolderPath))
            {
                return BadRequest(new ProjectLoadResult
                {
                    Status = "Error",
                    Message = "文件夹路径不能为空"
                });
            }

            try
            {
                var previousProjectPath = _projectContext.CurrentProjectPath;
                var previousWindowIds = _projectContext.GetRegisteredWindowIds().ToList();

                var loadResult = _projectService.OpenFolder(request.FolderPath);

                // 短路：本进程未注册过任何项目/窗口时，跳过 release 内部 ~1s 的固定 Task.Delay
                var nothingToRelease = string.IsNullOrWhiteSpace(previousProjectPath)
                                     && previousWindowIds.Count == 0;

                if (!nothingToRelease)
                {
                    if (!string.IsNullOrWhiteSpace(previousProjectPath) &&
                        !IsSamePath(previousProjectPath, loadResult.ProjectPath))
                    {
                        ReleaseProjectRuntimeResources(
                            previousProjectPath,
                            previousWindowIds,
                            closeDefaultWindowFallback: true);
                    }

                    ReleaseProjectRuntimeResources(
                        loadResult.ProjectPath,
                        previousWindowIds,
                        closeDefaultWindowFallback: true);
                }

                _projectContext.Clear();
                _projectContext.SetProject(loadResult.ProjectPath);

                // 记录最近打开
                var projectName = Path.GetFileName(loadResult.ProjectPath);
                _recentProjectsService.RecordOpen(projectName, loadResult.ProjectPath);

                // 初始化对话日志
                BIMCanvas.Server.Logging.ConversationLogger.Initialize(loadResult.ProjectPath);

                // 清空 Worktree（切换项目后旧 Worktree 无效）
                _gitService.CleanupAllWorktrees(loadResult.ProjectPath);
                _projectWatcherService.RestartWatching(loadResult.ProjectPath);

                return Ok(CreateSuccessProjectLoadResult(loadResult.ProjectPath, loadResult.Warnings));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "打开项目文件夹失败: {Path}", request.FolderPath);
                return StatusCode(500, new ProjectLoadResult
                {
                    Status = "Error",
                    Message = ex.Message
                });
            }
        }

        /// <summary>
        /// 关闭当前项目
        /// </summary>
        [HttpPost("close")]
        public ActionResult CloseProject([FromBody] CloseProjectRequest? request)
        {
            if (!_projectContext.IsLoaded)
            {
                return Ok(new { message = "没有已加载的项目" });
            }

            var projectPath = _projectContext.CurrentProjectPath!;
            var registeredWindowIds = _projectContext.GetRegisteredWindowIds().ToList();

            // 检测未保存变更
            if (request?.Force != true)
            {
                try
                {
                    var hasChanges = _gitService.HasUncommittedChanges(projectPath);
                    if (hasChanges)
                    {
                        return Conflict(new
                        {
                            message = "项目有未保存的更改",
                            hasUncommittedChanges = true
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "检测未保存变更失败，继续关闭");
                }
            }

            ReleaseProjectRuntimeResources(
                projectPath,
                registeredWindowIds,
                closeDefaultWindowFallback: true);

            _projectContext.Clear();
            _logger.LogInformation("项目已关闭");

            return Ok(new { message = "项目已关闭" });
        }

        /// <summary>
        /// 删除项目
        /// </summary>
        [HttpDelete("{name}")]
        public ActionResult DeleteProject(string name)
        {
            // 禁止删除当前打开的项目
            if (_projectContext.IsLoaded)
            {
                var currentName = Path.GetFileName(_projectContext.CurrentProjectPath);
                if (string.Equals(currentName, name, StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new { message = "不能删除当前打开的项目，请先关闭" });
                }
            }

            try
            {
                var folderPath = Path.Combine(ProjectService.DefaultProjectsRoot, name);
                ReleaseProjectRuntimeResources(
                    folderPath,
                    windowIds: null,
                    closeDefaultWindowFallback: false);
                _projectService.DeleteProject(name);
                _recentProjectsService.Remove(folderPath);

                return Ok(new { message = $"项目 '{name}' 已删除" });
            }
            catch (DirectoryNotFoundException)
            {
                return NotFound(new { message = $"项目不存在: {name}" });
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "删除项目失败，目录仍被占用: {Name}", name);
                return Conflict(new
                {
                    message = $"删除项目失败: 项目目录仍被进程占用，请先关闭项目窗口或等待 Agent 任务结束后重试。原始错误: {ex.Message}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除项目失败: {Name}", name);
                return StatusCode(500, new { message = $"删除项目失败: {ex.Message}" });
            }
        }

        private void ReleaseProjectRuntimeResources(
            string projectPath,
            IEnumerable<string>? windowIds,
            bool closeDefaultWindowFallback)
        {
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                return;
            }

            var normalizedProjectPath = Path.GetFullPath(projectPath);
            _projectWatcherService.StopWatchingProject(normalizedProjectPath);

            var closedByProject = _agentClientService.CloseProjectAgentsSync(normalizedProjectPath, waitMs: 500);
            if (closedByProject)
            {
                return;
            }

            var fallbackWindowIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (closeDefaultWindowFallback)
            {
                fallbackWindowIds.Add("window-main");
                fallbackWindowIds.Add("primary");
            }

            if (windowIds != null)
            {
                foreach (var windowId in windowIds)
                {
                    if (!string.IsNullOrWhiteSpace(windowId))
                    {
                        fallbackWindowIds.Add(windowId);
                    }
                }
            }

            foreach (var windowId in fallbackWindowIds)
            {
                _agentClientService.CloseAgentSync(windowId, waitMs: 250);
            }
        }

        private static bool IsSamePath(string left, string right)
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }

        private bool ClearProjectContextIfMissing()
        {
            if (!_projectContext.IsLoaded || string.IsNullOrWhiteSpace(_projectContext.CurrentProjectPath))
            {
                return false;
            }

            var projectPath = _projectContext.CurrentProjectPath;
            if (Directory.Exists(projectPath))
            {
                return false;
            }

            var registeredWindowIds = _projectContext.GetRegisteredWindowIds().ToList();
            ReleaseProjectRuntimeResources(
                projectPath,
                registeredWindowIds,
                closeDefaultWindowFallback: true);
            _projectContext.Clear();
            _logger.LogWarning("当前项目目录不存在，已自动关闭项目: {ProjectPath}", projectPath);
            return true;
        }

        /// <summary>
        /// 获取最近打开的项目列表
        /// </summary>
        [HttpGet("recent")]
        public ActionResult<List<RecentProjectEntry>> GetRecentProjects()
        {
            try
            {
                var recent = _recentProjectsService.LoadWithExistsCheck();
                return Ok(recent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加载最近项目失败");
                return StatusCode(500, new { message = $"加载最近项目失败: {ex.Message}" });
            }
        }

        /// <summary>
        /// 上传并打开 BCP 文件（带冲突检测）
        /// </summary>
        [HttpPost("upload")]
        [RequestSizeLimit(100 * 1024 * 1024)] // 100MB 限制
        public async Task<ActionResult<ProjectLoadResult>> UploadProject(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new ProjectLoadResult
                {
                    Status = "Error",
                    Message = "请选择要上传的 BCP 文件"
                });
            }

            if (!file.FileName.EndsWith(".bcp", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new ProjectLoadResult
                {
                    Status = "Error",
                    Message = "只支持 .bcp 格式的文件"
                });
            }

            try
            {
                // 保存到临时目录
                var tempDir = Path.Combine(Path.GetTempPath(), "BIMCanvas", "uploads");
                Directory.CreateDirectory(tempDir);
                var tempFilePath = Path.Combine(tempDir, file.FileName);

                // 写入临时文件
                using (var stream = new FileStream(tempFilePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                _logger.LogInformation("BCP 文件已上传到临时目录: {Path}", tempFilePath);

                // 检测冲突
                var (hasConflict, existingPath) = _projectService.CheckProjectConflict(tempFilePath);
                if (hasConflict)
                {
                    return Conflict(new ProjectLoadResult
                    {
                        Status = "Conflict",
                        ExistingPath = existingPath,
                        ProjectName = Path.GetFileNameWithoutExtension(file.FileName),
                        Message = $"项目 '{Path.GetFileNameWithoutExtension(file.FileName)}' 已存在"
                    });
                }

                // 无冲突，直接加载
                var loadResult = _projectService.LoadProject(tempFilePath);
                _projectContext.SetProject(loadResult.ProjectPath, tempFilePath);

                // 记录最近打开
                _recentProjectsService.RecordOpen(Path.GetFileNameWithoutExtension(file.FileName), loadResult.ProjectPath);

                // 初始化对话日志
                BIMCanvas.Server.Logging.ConversationLogger.Initialize(loadResult.ProjectPath);

                // 清理临时文件
                try { System.IO.File.Delete(tempFilePath); } catch { }

                return Ok(CreateSuccessProjectLoadResult(loadResult.ProjectPath, loadResult.Warnings));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "上传并加载项目失败");
                return StatusCode(500, new ProjectLoadResult
                {
                    Status = "Error",
                    Message = ex.Message
                });
            }
        }

        /// <summary>
        /// 解决上传文件的冲突（覆盖或使用已存在）
        /// </summary>
        [HttpPost("upload-resolve")]
        [RequestSizeLimit(100 * 1024 * 1024)] // 100MB 限制
        public async Task<ActionResult<ProjectLoadResult>> UploadResolveConflict(
            IFormFile file,
            [FromQuery] string resolution)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new ProjectLoadResult
                {
                    Status = "Error",
                    Message = "请选择要上传的 BCP 文件"
                });
            }

            if (resolution != "Overwrite" && resolution != "UseExisting")
            {
                return BadRequest(new ProjectLoadResult
                {
                    Status = "Error",
                    Message = "无效的解决策略，必须是 Overwrite 或 UseExisting"
                });
            }

            try
            {
                string projectPath;
                List<string> warnings;

                if (resolution == "UseExisting")
                {
                    // 使用已存在的项目，不需要上传文件内容
                    var bcpFileName = Path.GetFileNameWithoutExtension(file.FileName);
                    projectPath = Path.Combine(ProjectService.DefaultProjectsRoot, bcpFileName);

                    if (!Directory.Exists(projectPath))
                    {
                        return NotFound(new ProjectLoadResult
                        {
                            Status = "Error",
                            Message = $"项目目录不存在: {projectPath}"
                        });
                    }

                    var openResult = _projectService.OpenFolder(projectPath);
                    projectPath = openResult.ProjectPath;
                    warnings = openResult.Warnings;

                    _projectContext.SetProject(projectPath, null);

                    // 初始化对话日志
                    BIMCanvas.Server.Logging.ConversationLogger.Initialize(projectPath);
                }
                else // Overwrite
                {
                    // 保存到临时目录
                    var tempDir = Path.Combine(Path.GetTempPath(), "BIMCanvas", "uploads");
                    Directory.CreateDirectory(tempDir);
                    var tempFilePath = Path.Combine(tempDir, file.FileName);

                    using (var stream = new FileStream(tempFilePath, FileMode.Create))
                    {
                        await file.CopyToAsync(stream);
                    }

                    // 覆盖加载
                    var loadResult = _projectService.LoadProject(tempFilePath, overwrite: true);
                    projectPath = loadResult.ProjectPath;
                    warnings = loadResult.Warnings;
                    _projectContext.SetProject(projectPath, tempFilePath);

                    // 初始化对话日志
                    BIMCanvas.Server.Logging.ConversationLogger.Initialize(projectPath);

                    // 清理临时文件
                    try { System.IO.File.Delete(tempFilePath); } catch { }
                }

                return Ok(CreateSuccessProjectLoadResult(projectPath, warnings));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "解决冲突失败");
                return StatusCode(500, new ProjectLoadResult
                {
                    Status = "Error",
                    Message = ex.Message
                });
            }
        }

        /// <summary>
        /// 创建空白项目（无 .bcp 输入）。与 /upload 对称：检测同名冲突 → 物化空 baseline → 走相同的派生初始化管线。
        /// 不绑定 plugin scene；scenes[] 由用户后续在 /scenes 端点显式添加（R10 不变量）。
        /// </summary>
        [HttpPost("create")]
        public ActionResult<ProjectLoadResult> CreateBlankProject([FromBody] CreateProjectRequest request)
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new ProjectLoadResult
                {
                    Status = "Error",
                    Message = "请提供项目名称"
                });
            }

            string trimmedName;
            try
            {
                ProjectService.ValidateProjectName(request.Name);
                trimmedName = request.Name.Trim();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new ProjectLoadResult
                {
                    Status = "Error",
                    Message = ex.Message
                });
            }

            try
            {
                var (hasConflict, existingPath) = _projectService.CheckEmptyProjectConflict(trimmedName);
                if (hasConflict)
                {
                    return Conflict(new ProjectLoadResult
                    {
                        Status = "Conflict",
                        ExistingPath = existingPath,
                        ProjectName = trimmedName,
                        Message = $"项目 '{trimmedName}' 已存在"
                    });
                }

                var loadResult = _projectService.CreateProject(trimmedName, overwrite: false);
                _projectContext.SetProject(loadResult.ProjectPath, null);

                _recentProjectsService.RecordOpen(trimmedName, loadResult.ProjectPath);
                BIMCanvas.Server.Logging.ConversationLogger.Initialize(loadResult.ProjectPath);

                return Ok(CreateSuccessProjectLoadResult(loadResult.ProjectPath, loadResult.Warnings));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建空白项目失败: {Name}", trimmedName);
                return StatusCode(500, new ProjectLoadResult
                {
                    Status = "Error",
                    Message = ex.Message
                });
            }
        }

        /// <summary>
        /// 解决「新建空项目」的同名冲突（覆盖或使用已存在）。镜像 /upload-resolve 的两路分支。
        /// </summary>
        [HttpPost("create-resolve")]
        public ActionResult<ProjectLoadResult> CreateResolveConflict(
            [FromBody] CreateProjectRequest request,
            [FromQuery] string resolution)
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new ProjectLoadResult
                {
                    Status = "Error",
                    Message = "请提供项目名称"
                });
            }

            if (resolution != "Overwrite" && resolution != "UseExisting")
            {
                return BadRequest(new ProjectLoadResult
                {
                    Status = "Error",
                    Message = "无效的解决策略，必须是 Overwrite 或 UseExisting"
                });
            }

            string trimmedName;
            try
            {
                ProjectService.ValidateProjectName(request.Name);
                trimmedName = request.Name.Trim();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new ProjectLoadResult
                {
                    Status = "Error",
                    Message = ex.Message
                });
            }

            try
            {
                string projectPath;
                List<string> warnings;

                if (resolution == "UseExisting")
                {
                    projectPath = Path.Combine(ProjectService.DefaultProjectsRoot, trimmedName);
                    if (!Directory.Exists(projectPath))
                    {
                        return NotFound(new ProjectLoadResult
                        {
                            Status = "Error",
                            Message = $"项目目录不存在: {projectPath}"
                        });
                    }

                    var openResult = _projectService.OpenFolder(projectPath);
                    projectPath = openResult.ProjectPath;
                    warnings = openResult.Warnings;
                    _projectContext.SetProject(projectPath, null);
                }
                else // Overwrite
                {
                    var createResult = _projectService.CreateProject(trimmedName, overwrite: true);
                    projectPath = createResult.ProjectPath;
                    warnings = createResult.Warnings;
                    _projectContext.SetProject(projectPath, null);
                }

                _recentProjectsService.RecordOpen(trimmedName, projectPath);
                BIMCanvas.Server.Logging.ConversationLogger.Initialize(projectPath);

                return Ok(CreateSuccessProjectLoadResult(projectPath, warnings));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "解决新建项目冲突失败: {Name}", trimmedName);
                return StatusCode(500, new ProjectLoadResult
                {
                    Status = "Error",
                    Message = ex.Message
                });
            }
        }

        /// <summary>
        /// 打开 BCP 文件（带冲突检测）- 基于路径（仅供服务端使用）
        /// </summary>
        [HttpPost("open")]
        public ActionResult<ProjectLoadResult> OpenProject([FromBody] OpenProjectRequest request)
        {
            if (string.IsNullOrEmpty(request.BcpFilePath))
            {
                return BadRequest(new ProjectLoadResult
                {
                    Status = "Error",
                    Message = "BCP 文件路径不能为空"
                });
            }

            if (!System.IO.File.Exists(request.BcpFilePath))
            {
                return NotFound(new ProjectLoadResult
                {
                    Status = "Error",
                    Message = $"BCP 文件不存在: {request.BcpFilePath}"
                });
            }

            // 检测冲突
            var (hasConflict, existingPath) = _projectService.CheckProjectConflict(request.BcpFilePath);
            if (hasConflict)
            {
                return Conflict(new ProjectLoadResult
                {
                    Status = "Conflict",
                    ExistingPath = existingPath,
                    ProjectName = Path.GetFileNameWithoutExtension(request.BcpFilePath),
                    Message = $"项目 '{Path.GetFileNameWithoutExtension(request.BcpFilePath)}' 已存在"
                });
            }

            // 无冲突，直接加载
            try
            {
                var loadResult = _projectService.LoadProject(request.BcpFilePath);
                return ApplyAndBuildLoadResult(loadResult.ProjectPath, request.BcpFilePath, loadResult.Warnings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加载项目失败");
                return StatusCode(500, new ProjectLoadResult
                {
                    Status = "Error",
                    Message = ex.Message
                });
            }
        }

        /// <summary>
        /// 解决冲突（覆盖或使用已存在）- 基于路径（仅供服务端使用）
        /// </summary>
        [HttpPost("resolve-conflict")]
        public ActionResult<ProjectLoadResult> ResolveConflict([FromBody] ConflictResolutionRequest request)
        {
            if (string.IsNullOrEmpty(request.BcpFilePath))
            {
                return BadRequest(new ProjectLoadResult
                {
                    Status = "Error",
                    Message = "BCP 文件路径不能为空"
                });
            }

            try
            {
                string projectPath;
                List<string> warnings;

                if (request.Resolution == "UseExisting")
                {
                    // 使用已存在的项目
                    var bcpFileName = Path.GetFileNameWithoutExtension(request.BcpFilePath);
                    projectPath = Path.Combine(ProjectService.DefaultProjectsRoot, bcpFileName);

                    if (!Directory.Exists(projectPath))
                    {
                        return NotFound(new ProjectLoadResult
                        {
                            Status = "Error",
                            Message = $"项目目录不存在: {projectPath}"
                        });
                    }

                    var openResult = _projectService.OpenFolder(projectPath);
                    projectPath = openResult.ProjectPath;
                    warnings = openResult.Warnings;
                }
                else // Overwrite
                {
                    // 覆盖：删除旧目录并重新解压
                    var loadResult = _projectService.LoadProject(request.BcpFilePath, overwrite: true);
                    projectPath = loadResult.ProjectPath;
                    warnings = loadResult.Warnings;
                }

                _projectContext.SetProject(projectPath, request.BcpFilePath);

                return Ok(CreateSuccessProjectLoadResult(projectPath, warnings));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "解决冲突失败");
                return StatusCode(500, new ProjectLoadResult
                {
                    Status = "Error",
                    Message = ex.Message
                });
            }
        }

        /// <summary>
        /// 导出当前项目为 BCP 文件
        /// </summary>
        [HttpGet("export")]
        public IActionResult ExportProject()
        {
            if (!_projectContext.IsLoaded)
            {
                return BadRequest(new { message = "没有加载的项目" });
            }

            var projectPath = _projectContext.CurrentProjectPath!;
            var projectName = Path.GetFileName(projectPath);

            // 生成临时文件路径
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var tempFileName = $"{projectName}_{timestamp}.bcp";
            var tempFilePath = Path.Combine(Path.GetTempPath(), "BIMCanvas", "exports", tempFileName);

            try
            {
                // 确保目录存在
                Directory.CreateDirectory(Path.GetDirectoryName(tempFilePath)!);

                // 调用 ProjectService 保存
                _projectService.SaveProject(projectPath, tempFilePath);

                // 读取文件并返回
                var fileBytes = System.IO.File.ReadAllBytes(tempFilePath);

                // 清理临时文件
                try { System.IO.File.Delete(tempFilePath); } catch { }

                return File(fileBytes, "application/octet-stream", tempFileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "导出项目失败");
                return StatusCode(500, new { message = $"导出项目失败: {ex.Message}" });
            }
        }

        /// <summary>
        /// 保存模块数据到文件系统
        /// v3.4: 移除 zoneId 依赖，Server 根据模块 bounds 位置自动计算分区
        /// </summary>
        [HttpPost("save")]
        public async System.Threading.Tasks.Task<ActionResult> SaveModules([FromBody] JObject requestBody)
        {
            if (!_projectContext.IsLoaded)
            {
                return BadRequest(new { message = "没有加载的项目" });
            }

            // R3 写入 gate (V12a / 主真理源 §3.11 / §4.7):pending 状态拒绝写入
            var writeGate = _projectContext.CheckWriteAllowed();
            if (!writeGate.Allowed)
                return StatusCode(403, new { code = writeGate.Code, message = writeGate.Message });

            // 优先使用活跃窗口的 Worktree 路径
            var projectPath = _projectContext.GetActiveWorktreePath()
                              ?? _projectContext.CurrentProjectPath!;
            var schemesPath = Path.Combine(projectPath, "schemes");

            try
            {
                var rawFacingErrors = ValidateFacingPayloadForProjectSave(requestBody);
                if (rawFacingErrors.Count > 0)
                {
                    return BadRequest(new
                    {
                        message = string.Join("；", rawFacingErrors)
                    });
                }

                var request = requestBody.ToObject<SaveModulesRequest>(JsonSerializer.Create(_jsonSettings));
                if (request == null)
                {
                    return BadRequest(new { message = "保存请求体无效" });
                }

                // 确保 schemes 目录存在
                if (!Directory.Exists(schemesPath))
                {
                    Directory.CreateDirectory(schemesPath);
                }

                var modules = request.Modules ?? new List<Module>();
                var facingErrors = ValidateFacingForProjectSave(modules);
                if (facingErrors.Count > 0)
                {
                    return BadRequest(new
                    {
                        message = string.Join("；", facingErrors)
                    });
                }

                // Step 1: 读取分区边界
                var computedData = LoadComputedData(projectPath);
                var roomZones = computedData.RoomZones ?? new List<Zone>();

                // 变体激活映射：designZoneId → variantSlug（设计区级；空/非法条目过滤）。
                // 设计区在变体编辑态时，模块按该变体自身 zones.json 的子分区落盘到
                // schemes/{dz}/{slug}/[{leaf}/]modules.json，canonical（adopted）不动；未在映射内的设计区照常写 canonical。
                // 旧契约（leaf→slug）会让带子分区变体的 leaf key（dz_1/dz_2）与按房间分组的 key（rz_*）对不上，
                // 静默回落 canonical（=adopted，常为 bootstrap 出的 main），导致变体编辑写错文件——改为设计区级索引根治。
                var variantByDesignZone = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (request.VariantSelection != null)
                {
                    foreach (var kvp in request.VariantSelection)
                    {
                        if (string.IsNullOrWhiteSpace(kvp.Key) || string.IsNullOrWhiteSpace(kvp.Value))
                            continue;
                        try
                        {
                            ModuleFileTopologyService.EnsureSafeVariantId(kvp.Value);
                            variantByDesignZone[kvp.Key] = kvp.Value;
                        }
                        catch (ArgumentException ex)
                        {
                            return BadRequest(new { message = $"variantSelection[{kvp.Key}] 非法: {ex.Message}" });
                        }
                    }
                }

                // 预载每个活跃变体设计区的子分区叶子（含边界），用于把模块从房间级细分到子分区级。
                // designZoneId → (slug, leaves)；leaves 为空 = 单叶子变体（设计区自身即叶子）。
                // 复用 BuildEffectiveZoneView（与 GetVariantZones 同一塑形源、走显式 slug，不经 adopted 中心拓扑），
                // 故未采纳变体的子分区叶子也能正确解析。
                var variantLeaves = new Dictionary<string, (string Slug, List<Zone> Leaves)>(StringComparer.OrdinalIgnoreCase);
                foreach (var pair in variantByDesignZone)
                {
                    var roots = ProjectService.BuildEffectiveZoneView(schemesPath, pair.Value, new[] { pair.Key });
                    var root = roots.FirstOrDefault(z => string.Equals(z.Id, pair.Key, StringComparison.OrdinalIgnoreCase));
                    variantLeaves[pair.Key] = (pair.Value, root?.SubZones ?? new List<Zone>());
                }

                // Step 2: 分组。先按房间（baseline rz_*）定位，再对活跃变体设计区下钻到具体子分区叶子。
                // leafTarget: leafZoneId → (designZoneId, variantId)，唯一确定落盘文件（variantId=null 即 canonical）。
                var grouped = new Dictionary<string, List<Module>>(StringComparer.OrdinalIgnoreCase);
                var leafTarget = new Dictionary<string, (string DesignZoneId, string? VariantId)>(StringComparer.OrdinalIgnoreCase);
                var orphanModules = new List<string>();

                foreach (var module in modules)
                {
                    var roomId = CalculateModuleZone(module, roomZones);

                    if (string.IsNullOrEmpty(roomId))
                    {
                        // 不回头看：删 _unzoned 假桶。孤儿模块（bounds 中心不落任何分区）不落盘到黑洞目录
                        // （递归模型下解析器不登记 _unzoned，写后不可读），改经响应 orphanModules 显式回传调用方。
                        orphanModules.Add(module.Id);
                        _logger.LogWarning("[SaveModules] 模块 {ModuleId} 不在任何分区内，未落盘（经 orphanModules 回传）", module.Id);
                        continue;
                    }

                    string leafZoneId;
                    string designZoneId;
                    string? variantId;

                    if (variantLeaves.TryGetValue(roomId, out var vl))
                    {
                        // 该设计区在变体编辑态
                        designZoneId = roomId;
                        variantId = vl.Slug;
                        if (vl.Leaves.Count > 0)
                        {
                            // 多子分区变体：按 bounds 中心落到具体子分区叶子
                            var sub = FindContainingLeaf(module, vl.Leaves);
                            if (string.IsNullOrEmpty(sub))
                            {
                                orphanModules.Add(module.Id);
                                _logger.LogWarning("[SaveModules] 模块 {ModuleId} 不在变体 {Slug} 的任何子分区内，未落盘", module.Id, vl.Slug);
                                continue;
                            }
                            leafZoneId = sub;
                        }
                        else
                        {
                            // 单叶子变体：设计区自身即叶子
                            leafZoneId = roomId;
                        }
                    }
                    else
                    {
                        // 无活跃变体：写 canonical（adopted）。房间即叶子。
                        designZoneId = ResolveDesignZoneIdForLeaf(schemesPath, roomId);
                        leafZoneId = roomId;
                        variantId = null;
                    }

                    if (!grouped.ContainsKey(leafZoneId))
                        grouped[leafZoneId] = new List<Module>();
                    grouped[leafZoneId].Add(module);
                    leafTarget[leafZoneId] = (designZoneId, variantId);
                }

                // 活跃变体的每个叶子即使被拖空也要写空数组（否则旧家具残留在变体文件里）。
                foreach (var pair in variantLeaves)
                {
                    var dz = pair.Key;
                    var vl = pair.Value;
                    if (vl.Leaves.Count > 0)
                    {
                        foreach (var leaf in vl.Leaves)
                        {
                            if (string.IsNullOrWhiteSpace(leaf.Id) || grouped.ContainsKey(leaf.Id))
                                continue;
                            grouped[leaf.Id] = new List<Module>();
                            leafTarget[leaf.Id] = (dz, vl.Slug);
                        }
                    }
                    else if (!grouped.ContainsKey(dz))
                    {
                        grouped[dz] = new List<Module>();
                        leafTarget[dz] = (dz, vl.Slug);
                    }
                }

                // Step 3: 清空旧文件
                //   canonical 设计区：清 canonical 叶子文件（防家具被拖出后残留）
                //   活跃变体设计区：清该变体所有叶子文件，canonical 不动
                var existingCanonicalFiles = ModuleFileTopologyService.FindExistingCanonicalModuleFiles(schemesPath);
                foreach (var entry in existingCanonicalFiles)
                {
                    var dzForLeaf = ResolveDesignZoneIdForLeaf(schemesPath, entry.ZoneId);
                    if (variantByDesignZone.ContainsKey(dzForLeaf))
                        continue;   // 该设计区在变体编辑态——canonical 不动
                    DeleteFileIfWritable(entry.FilePath);
                }
                foreach (var pair in variantLeaves)
                {
                    var dz = pair.Key;
                    var vl = pair.Value;
                    if (vl.Leaves.Count > 0)
                    {
                        foreach (var leaf in vl.Leaves)
                        {
                            if (string.IsNullOrWhiteSpace(leaf.Id))
                                continue;
                            DeleteFileIfWritable(_modulesWriter.ResolveModulesPath(projectPath, dz, leaf.Id, vl.Slug));
                        }
                    }
                    else
                    {
                        DeleteFileIfWritable(_modulesWriter.ResolveModulesPath(projectPath, dz, dz, vl.Slug));
                    }
                }

                // Step 4: 写入新数据（全部走 ModulesWriterService，wrapper 形态 + 指针变体路径）
                foreach (var kvp in grouped)
                {
                    var leafZoneId = kvp.Key;
                    var target = leafTarget[leafZoneId];
                    await _modulesWriter.WriteAsync(
                        projectPath, target.DesignZoneId, leafZoneId, target.VariantId,
                        kvp.Value);
                }

                _logger.LogInformation("[SaveModules] 保存完成: {Total} 个模块，{ZoneCount} 个分区，{OrphanCount} 个孤立",
                    modules.Count - orphanModules.Count, grouped.Count, orphanModules.Count);

                return Ok(new
                {
                    success = true,
                    modulesCount = modules.Count,
                    zoneCount = grouped.Count,
                    orphanCount = orphanModules.Count,
                    orphanModules = orphanModules
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存模块数据失败");
                return StatusCode(500, new { message = $"保存模块数据失败: {ex.Message}" });
            }
        }

        /// <summary>
        /// 把叶子 zoneId 反查为其设计区祖先路径——收口到拓扑解析器统一"递归向上找设计区祖先"helper
        /// （递归模型下祖先不必是 segments[0]：容器嵌套叶子 rz_6/dz_客厅/{slug}/dz_1 的祖先 = rz_6/dz_客厅）。
        /// </summary>
        private string ResolveDesignZoneIdForLeaf(string schemesPath, string leafZoneId)
        {
            return ModuleFileTopologyService.ResolveDesignZoneIdForLeaf(schemesPath, leafZoneId);
        }

        /// <summary>
        /// 根据模块 bounds 中心点计算所属分区
        /// </summary>
        private string? CalculateModuleZone(Module module, List<Zone> roomZones)
        {
            if (module.Bounds == null || module.Bounds.Vertices.Length < 3)
                return null;

            // 计算 bounds 中心点
            var center = module.Bounds.ComputeCenter();

            // 遍历所有房间区域，找到包含该点的分区
            foreach (var zone in roomZones.Where(z => z.Type == Core.Models.Shared.ZoneType.Room))
            {
                var boundary = zone.ComputedBoundary ?? zone.RawBoundary;
                if (boundary != null && CollisionDetector.Contains(boundary, center))
                {
                    return zone.Id;
                }
            }

            return null;
        }

        /// <summary>
        /// 在给定 zone 列表（任意类型，含子分区 designable）中按模块 bounds 中心点找包含它的叶子。
        /// 与 <see cref="CalculateModuleZone"/> 的区别：不限 ZoneType==Room，用于变体子分区落盘分组。
        /// </summary>
        private static string? FindContainingLeaf(Module module, List<Zone> leaves)
        {
            if (module.Bounds == null || module.Bounds.Vertices.Length < 3)
                return null;

            var center = module.Bounds.ComputeCenter();

            foreach (var zone in leaves)
            {
                if (string.IsNullOrWhiteSpace(zone.Id))
                    continue;
                var boundary = zone.ComputedBoundary ?? zone.RawBoundary;
                if (boundary != null && CollisionDetector.Contains(boundary, center))
                {
                    return zone.Id;
                }
            }

            return null;
        }

        /// <summary>
        /// API 层的原始请求预检。
        /// Web 保存链路必须显式传递 facing: { value, semantic:null }，
        /// 不接受旧的顶层字符串/顶层数组，也不接受缺失 semantic 字段。
        /// </summary>
        private List<string> ValidateFacingPayloadForProjectSave(JObject requestBody)
        {
            var errors = new List<string>();
            var modulesToken = requestBody["modules"];

            if (modulesToken == null)
            {
                errors.Add("请求体缺少 modules 数组");
                return errors;
            }

            if (modulesToken.Type != JTokenType.Array)
            {
                errors.Add("请求体中的 modules 必须是数组");
                return errors;
            }

            var index = 0;
            foreach (var moduleToken in modulesToken.Children())
            {
                var moduleLabel = $"module[{index}]";

                if (moduleToken.Type != JTokenType.Object)
                {
                    errors.Add($"{moduleLabel} 必须是对象");
                    index++;
                    continue;
                }

                var moduleObj = (JObject)moduleToken;
                moduleLabel = moduleObj["id"]?.Value<string>() ?? moduleLabel;

                var facingToken = moduleObj["facing"];
                if (facingToken == null || facingToken.Type != JTokenType.Object)
                {
                    errors.Add($"模块 {moduleLabel} 的 facing 必须是对象 {{ value, semantic }}");
                    index++;
                    continue;
                }

                var facingObj = (JObject)facingToken;

                if (!facingObj.TryGetValue("value", out var valueToken))
                {
                    errors.Add($"模块 {moduleLabel} 的 facing.value 不能为空");
                }
                else if (valueToken.Type != JTokenType.Array || ((JArray)valueToken).Count != 2 || !((JArray)valueToken).All(IsNumericToken))
                {
                    errors.Add($"模块 {moduleLabel} 的 facing.value 必须是 [x, y] 数值数组");
                }

                if (!facingObj.TryGetValue("semantic", out var semanticToken))
                {
                    errors.Add($"模块 {moduleLabel} 的 facing.semantic 必须显式传 null");
                }
                else if (semanticToken.Type != JTokenType.Null)
                {
                    errors.Add($"模块 {moduleLabel} 的 facing.semantic 必须为 null");
                }

                index++;
            }

            return errors;
        }

        /// <summary>
        /// Web 保存链路只接受规范 facing.value，禁止携带 semantic。
        /// </summary>
        private List<string> ValidateFacingForProjectSave(List<Module> modules)
        {
            var errors = new List<string>();

            foreach (var module in modules)
            {
                if (module.Facing.HasSemantic)
                {
                    errors.Add($"模块 {module.Id} 的 facing.semantic 必须为 null");
                    continue;
                }

                if (!module.Facing.Value.HasValue)
                {
                    errors.Add($"模块 {module.Id} 缺少 facing.value");
                    continue;
                }

                if (!module.Facing.HasFiniteValue() || module.Facing.HasZeroValue() || !module.Facing.TryGetNormalizedValue(out var normalizedValue))
                {
                    errors.Add($"模块 {module.Id} 的 facing.value 不是有效单位向量");
                    continue;
                }

                module.Facing = new Facing(normalizedValue, null);
            }

            return errors;
        }

        private static bool IsNumericToken(JToken token)
        {
            return token.Type == JTokenType.Integer || token.Type == JTokenType.Float;
        }

        /// <summary>
        /// 加载并聚合项目数据
        /// </summary>
        private ProjectData LoadProjectData(string projectPath)
        {
            var data = new ProjectData();

            // 1. 读取 project.json
            var projectJsonPath = Path.Combine(projectPath, "project.json");
            if (System.IO.File.Exists(projectJsonPath))
            {
                data.Project = ReadJson<Project>(projectJsonPath);
            }

            // 2. 读取 baseline 数据
            data.Baseline = LoadBaselineData(projectPath);

            // 3. 读取当前激活策略数据
            if (!string.IsNullOrEmpty(data.Project.ActiveSchemeId))
            {
                data.ActiveScheme = LoadSchemeData(projectPath, data.Project.ActiveSchemeId);
            }

            // 4. 读取 computed 数据
            data.Computed = LoadComputedData(projectPath);

            return data;
        }

        /// <summary>
        /// 加载 Baseline 层数据
        /// </summary>
        private BaselineData LoadBaselineData(string projectPath)
        {
            var baselinePath = Path.Combine(projectPath, "baseline");
            var data = new BaselineData();

            if (!Directory.Exists(baselinePath))
            {
                _logger.LogWarning("baseline 目录不存在: {Path}", baselinePath);
                return data;
            }

            // metadata.json
            var metadataPath = Path.Combine(baselinePath, "metadata.json");
            if (System.IO.File.Exists(metadataPath))
            {
                data.Metadata = ReadJson<BaselineManifest>(metadataPath);
            }

            // architecture.json
            var architecturePath = Path.Combine(baselinePath, "architecture.json");
            if (System.IO.File.Exists(architecturePath))
            {
                var arch = ReadJson<Architecture>(architecturePath);
                data.Walls = arch.Walls ?? new List<Wall>();
                data.Columns = arch.Columns ?? new List<Column>();
            }

            // openings.json
            var openingsPath = Path.Combine(baselinePath, "openings.json");
            if (System.IO.File.Exists(openingsPath))
            {
                data.Openings = ReadJson<List<Opening>>(openingsPath) ?? new List<Opening>();
            }

            // rooms.json
            var roomsPath = Path.Combine(baselinePath, "rooms.json");
            if (System.IO.File.Exists(roomsPath))
            {
                data.Rooms = ReadJson<List<Room>>(roomsPath) ?? new List<Room>();
            }

            // location_lines.json
            var locationLinesPath = Path.Combine(baselinePath, "location_lines.json");
            if (System.IO.File.Exists(locationLinesPath))
            {
                data.LocationLines = ReadJson<List<LocationLine>>(locationLinesPath) ?? new List<LocationLine>();
            }

            _logger.LogDebug("Baseline 数据加载完成: Walls={Walls}, Columns={Columns}, Openings={Openings}, Rooms={Rooms}",
                data.Walls.Count, data.Columns.Count, data.Openings.Count, data.Rooms.Count);

            return data;
        }

        /// <summary>
        /// 加载策略层数据
        /// v3.3: 支持分区子目录格式 schemes/{zoneId}/modules.json
        /// </summary>
        private SchemeData LoadSchemeData(string projectPath, string schemeId)
        {
            var schemePath = Path.Combine(projectPath, "schemes");
            var data = new SchemeData();

            if (!Directory.Exists(schemePath))
            {
                _logger.LogWarning("schemes 目录不存在: {Path}", schemePath);
                return data;
            }

            // strategy.json
            var strategyPath = Path.Combine(schemePath, "strategy.json");
            if (System.IO.File.Exists(strategyPath))
            {
                data.Strategy = ReadJson<Strategy>(strategyPath);
            }

            // zones：有效拓扑视图（读时聚合）——根 baseline rz_* + 各设计区 adopted per-scheme 叶子作 SubZones，
            // 收口到 P1 解析器（不再直读全局 zones.json 整树）。
            data.Zones = ProjectService.BuildEffectiveZoneView(schemePath);

            // finishes.json
            var finishesPath = Path.Combine(schemePath, "finishes.json");
            if (System.IO.File.Exists(finishesPath))
            {
                data.Finishes = ReadJson<List<FinishSegment>>(finishesPath) ?? new List<FinishSegment>();
            }

            // modules: 优先从分区子目录读取，向后兼容单一文件
            var (modules, zoneErrors) = LoadAllZoneModules(schemePath);
            data.Modules = modules;
            data.ZoneErrors = zoneErrors;

            _logger.LogDebug("策略数据加载完成: SchemeId={Id}, Zones={Zones}, Modules={Modules}, ZoneErrors={Errors}",
                schemeId, data.Zones.Count, data.Modules.Count, data.ZoneErrors.Count);

            return data;
        }

        /// <summary>
        /// 递归读取所有分区的 modules.json（wrapper 形态，Phase 0b 起裸数组已淘汰）
        /// v3.4: 不再填充 zoneId，模块无此字段
        /// Load 质检闸门：L1 per-zone 反序列化失败隔离，L2 per-module 结构完整性质检
        /// </summary>
        private (List<Module> modules, List<ZoneLoadError> errors) LoadAllZoneModules(string schemePath)
        {
            var allModules = new List<Module>();
            var allErrors = new List<ZoneLoadError>();

            // 递归查找所有叶子 zone 的 modules.json（支持嵌套分区 schemes/rz_3/dz_1/modules.json）
            var leafFiles = ProjectService.FindAllLeafModuleFiles(schemePath);
            if (leafFiles.Count == 0)
                return (allModules, allErrors);

            foreach (var (filePath, zoneId) in leafFiles)
            {
                List<Module> zoneModules;

                // L1：per-zone 反序列化失败处理（含裸数组报错 → 提示运行迁移脚本）
                try
                {
                    zoneModules = _modulesReader.ReadModulesOnly(filePath) ?? new List<Module>();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine(
                        $"[LoadAllZoneModules] 文件解析失败 | Zone: {zoneId} | 文件: {filePath} | 原因: {ex.Message}");
                    _logger.LogError("[LoadAllZoneModules] 文件解析失败 | Zone: {ZoneId} | 原因: {Message}", zoneId, ex.Message);
                    allErrors.Add(new ZoneLoadError
                    {
                        ZoneId = zoneId,
                        ErrorType = "ParseError",
                        Message = $"modules.json 解析失败：{ex.Message}"
                    });
                    continue;
                }

                // L2：per-module 结构完整性质检
                var failedIds = new List<string>();
                var failedReasons = new List<string>();
                var validModules = new List<Module>();
                foreach (var module in zoneModules)
                {
                    module.ZoneId ??= zoneId;
                    var reason = GetModuleStructureError(module);
                    if (reason != null)
                    {
                        var moduleLabel = module.Id ?? module.ModuleId ?? "(无ID)";
                        failedIds.Add(moduleLabel);
                        failedReasons.Add($"{moduleLabel}：{reason}");
                    }
                    else
                    {
                        validModules.Add(module);
                    }
                }

                if (failedIds.Count > 0)
                {
                    var msg = string.Join("；", failedReasons);
                    System.Diagnostics.Trace.WriteLine(
                        $"[LoadAllZoneModules] 数据质检失败 | Zone: {zoneId} | 跳过模块数: {failedIds.Count} | IDs: {string.Join(",", failedIds)}");
                    _logger.LogWarning("[LoadAllZoneModules] 数据质检失败 | Zone: {ZoneId} | 跳过: {Count} 个模块 | IDs: {Ids}",
                        zoneId, failedIds.Count, string.Join(",", failedIds));
                    allErrors.Add(new ZoneLoadError
                    {
                        ZoneId = zoneId,
                        ErrorType = "StructureError",
                        Message = msg,
                        FailedModuleIds = failedIds
                    });
                }

                allModules.AddRange(validModules);
            }
            _logger.LogDebug("从 {Count} 个叶子分区加载模块，共 {Total} 个（质检后）", leafFiles.Count, allModules.Count);

            return (allModules, allErrors);
        }

        /// <summary>
        /// L2 结构完整性质检：检查单个模块是否满足 Web 渲染的最低要求。
        /// 返回 null 表示合格，返回错误描述字符串表示不合格。
        /// </summary>
        private static string? GetModuleStructureError(Module module)
        {
            if (module.Bounds == null)
                return "bounds 字段缺失";
            if (module.Bounds.Vertices.Length < 3)
                return "bounds 顶点不足";
            if (module.Bounds.Vertices.Any(v => double.IsNaN(v.X) || double.IsNaN(v.Y) ||
                                                double.IsInfinity(v.X) || double.IsInfinity(v.Y)))
                return "bounds 含非法坐标值（NaN 或 Infinity）";
            if (string.IsNullOrEmpty(module.ModuleId))
                return "moduleId 字段缺失";
            return null;
        }

        /// <summary>
        /// 加载 Computed 层数据
        /// </summary>
        private ComputedDataDto LoadComputedData(string projectPath)
        {
            var computedPath = Path.Combine(projectPath, "computed");
            var data = new ComputedDataDto();

            if (!Directory.Exists(computedPath))
            {
                _logger.LogDebug("computed 目录不存在: {Path}", computedPath);
                return data;
            }

            // room_zones.json (房间区域)
            var zonesPath = Path.Combine(computedPath, "room_zones.json");
            if (System.IO.File.Exists(zonesPath))
            {
                data.RoomZones = ReadJson<List<Zone>>(zonesPath) ?? new List<Zone>();
            }

            // exclusions.json (禁区)
            var exclusionsPath = Path.Combine(computedPath, "exclusions.json");
            if (System.IO.File.Exists(exclusionsPath))
            {
                data.Exclusions = ReadJson<List<Zone>>(exclusionsPath) ?? new List<Zone>();
            }

            _logger.LogDebug("Computed 数据加载完成: RoomZones={RoomZoneCount}, Exclusions={ExclusionCount}",
                data.RoomZones.Count, data.Exclusions.Count);

            return data;
        }

        /// <summary>
        /// 读取 JSON 文件并反序列化
        /// </summary>
        private T ReadJson<T>(string path) where T : new()
        {
            var json = System.IO.File.ReadAllText(path, Encoding.UTF8);
            return JsonConvert.DeserializeObject<T>(json, _jsonSettings) ?? new T();
        }

        /// <summary>
        /// 确保文件可写（移除 ReadOnly 属性）
        /// </summary>
        private static void EnsureWritableFile(string path)
        {
            if (!System.IO.File.Exists(path))
                return;

            var attributes = System.IO.File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                System.IO.File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }
        }

        /// <summary>
        /// 文件存在则删除（先清除 ReadOnly）；不存在直接跳过。
        /// 与 ProjectService.ClearAllLeafModuleFiles 内部行为一致。
        /// </summary>
        private static void DeleteFileIfWritable(string path)
        {
            if (!System.IO.File.Exists(path))
                return;
            EnsureWritableFile(path);
            System.IO.File.Delete(path);
        }

        private static ProjectLoadResult CreateSuccessProjectLoadResult(string projectPath, List<string> warnings)
        {
            return new ProjectLoadResult
            {
                Status = "Success",
                ProjectPath = projectPath,
                Warnings = warnings.Count > 0 ? warnings : null
            };
        }

        // ─── v1.1 plugin / scene 解析 helper (主真理源 §4.7 / R3 / R10) ───

        /// <summary>
        /// 设置 ProjectContext 并构造 ProjectLoadResult。
        /// activePlugin = null/空时走 legacy 兼容路径 (SetProject + LaunchContext=null,写入放行);
        /// activePlugin 非空时直接 Bound(项目去插件态:不再读 project.json.scenes[]、无三态匹配)。
        /// </summary>
        private ActionResult<ProjectLoadResult> ApplyAndBuildLoadResult(
            string projectPath, string? bcpPath, List<string> warnings)
        {
            var serverConfig = ConfigService.Load();
            var activePluginId = string.IsNullOrWhiteSpace(serverConfig.Agent.ActivePlugin)
                ? null
                : serverConfig.Agent.ActivePlugin;

            // legacy 路径 (M0 默认):无 active plugin → 兼容 SetProject,写入放行
            if (activePluginId is null)
            {
                _projectContext.SetProject(projectPath, bcpPath);
                return Ok(new ProjectLoadResult
                {
                    Status = "Success",
                    ProjectPath = projectPath,
                    OpenStatus = Models.Plugins.OpenStatus.Bound,
                    CurrentActivePlugin = null,
                    Warnings = warnings.Count > 0 ? warnings : null,
                });
            }

            // 有 active plugin → 命名空间 = active plugin id,直接绑定。
            // (项目去插件态:废弃 sceneId / scenes / lock / 三态;多方案对比走 git 分支。
            //  LaunchContext 只携带 active plugin 身份 + 项目路径。)
            var launchContext = _pluginLifecycle.BuildLaunchContext(projectPath);
            _projectContext.SetBound(projectPath, bcpPath, launchContext);

            try
            {
                _pluginLifecycle.WriteLaunchContextFileAsync(launchContext, Environment.ProcessId)
                    .GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "写 LaunchContext 文件失败 (不阻断 open)");
            }

            return Ok(new ProjectLoadResult
            {
                Status = "Success",
                ProjectPath = projectPath,
                OpenStatus = Models.Plugins.OpenStatus.Bound,
                CurrentActivePlugin = activePluginId,
                Warnings = warnings.Count > 0 ? warnings : null,
            });
        }

    }
}
