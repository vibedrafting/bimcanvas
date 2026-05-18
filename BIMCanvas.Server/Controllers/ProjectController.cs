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
        /// 新增 scene 绑定 (主真理源 v1.1 §2.2 步骤 6 / §4.8 / 模板 §4.8)。
        /// <para>
        /// body <c>{sceneId, plugin: {id, versionRange}, scene}</c>;
        /// 通过 JObject patch 写入 <c>project.json.scenes[]</c>,调
        /// <see cref="ProjectFixedFilesBootstrapService.MountSceneScaffold"/> 物化 plugin projectMount,
        /// 写 <c>plugins.lock.json</c>,生成 LaunchContext 并 SetBound。
        /// </para>
        /// <para>
        /// <b>R10 缓解</b>:本端点是 plugin projectMount 物化的<b>唯一</b>入口;
        /// open project 不触发 MountSceneScaffold。
        /// </para>
        /// </summary>
        [HttpPost("scenes")]
        public ActionResult<BindSceneResult> BindScene([FromBody] BindSceneRequest request)
        {
            if (!_projectContext.IsLoaded || string.IsNullOrEmpty(_projectContext.CurrentProjectPath))
                return BadRequest(new BindSceneResult { Success = false, Message = "没有加载的项目" });

            if (request is null
                || string.IsNullOrWhiteSpace(request.SceneId)
                || request.Plugin is null
                || string.IsNullOrWhiteSpace(request.Plugin.Id))
                return BadRequest(new BindSceneResult { Success = false, Message = "请求缺失 sceneId / plugin.id" });

            try
            {
                var projectPath = _projectContext.CurrentProjectPath!;
                var sceneId = request.SceneId.Trim();
                var pluginId = request.Plugin.Id.Trim();

                // 1. JObject patch 写 project.json.scenes[]
                var projectJsonPath = Path.Combine(projectPath, "project.json");
                JObject projectJson;
                if (System.IO.File.Exists(projectJsonPath))
                {
                    projectJson = JObject.Parse(System.IO.File.ReadAllText(projectJsonPath));
                }
                else
                {
                    projectJson = new JObject();
                }

                var scenesArr = projectJson["scenes"] as JArray ?? new JArray();

                // sceneId 唯一性
                foreach (var existing in scenesArr)
                {
                    if (string.Equals((string?)existing?["sceneId"], sceneId, StringComparison.OrdinalIgnoreCase))
                        return Conflict(new BindSceneResult { Success = false, Message = $"sceneId '{sceneId}' 已存在" });
                }

                var newScene = new JObject
                {
                    ["sceneId"] = sceneId,
                    ["scene"] = request.Scene ?? "",
                    ["plugin"] = new JObject
                    {
                        ["id"] = pluginId,
                        ["versionRange"] = request.Plugin.VersionRange ?? "^1.0.0",
                    },
                    ["status"] = "active",
                    ["createdAt"] = DateTimeOffset.Now.ToString("o"),
                };
                scenesArr.Add(newScene);
                projectJson["scenes"] = scenesArr;
                System.IO.File.WriteAllText(projectJsonPath, projectJson.ToString(Formatting.Indented), new UTF8Encoding(false));

                // 2. MountSceneScaffold (R10 唯一物化入口)
                _projectFixedFiles.MountSceneScaffold(projectPath, sceneId, pluginId);

                // 3. 写 plugins.lock.json
                WritePluginLockEntry(projectPath, sceneId, pluginId);

                // 4. 生成 LaunchContext + SetBound
                var allScenes = ReadProjectScenes(projectPath);
                var summary = new ProjectScenesSummary(allScenes, sceneId);
                var lockSummary = TryReadPluginLockSummary(projectPath, sceneId);
                var launchContext = _pluginLifecycle.BuildLaunchContext(
                    projectPath, sceneId, summary, lockSummary, readOnlySceneIds: null);
                _projectContext.SetBound(projectPath, _projectContext.SourceBcpPath, launchContext);

                // 5. LaunchContext 文件 (Agent 子进程接收;M0 阶段 Agent 单例已启动,本文件供组3 hot-reload)
                try
                {
                    var ctxPath = _pluginLifecycle.WriteLaunchContextFileAsync(launchContext, Environment.ProcessId)
                        .GetAwaiter().GetResult();
                    _logger.LogInformation("LaunchContext 已写入: {Path}", ctxPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "写 LaunchContext 文件失败 (不阻断 bind)");
                }

                return Ok(new BindSceneResult
                {
                    Success = true,
                    SceneId = sceneId,
                    PluginId = pluginId,
                    ProjectPath = projectPath,
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "bind scene 失败: sceneId={Id}, plugin={P}", request.SceneId, request.Plugin?.Id);
                return StatusCode(500, new BindSceneResult { Success = false, Message = ex.Message });
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

                // Step 2: 根据 bounds 位置分组
                var grouped = new Dictionary<string, List<Module>>();
                var orphanModules = new List<string>();

                foreach (var module in modules)
                {
                    var zoneId = CalculateModuleZone(module, roomZones);

                    if (string.IsNullOrEmpty(zoneId))
                    {
                        orphanModules.Add(module.Id);
                        zoneId = "_unzoned";
                        _logger.LogWarning("[SaveModules] 模块 {ModuleId} 不在任何分区内，归入 _unzoned", module.Id);
                    }

                    if (!grouped.ContainsKey(zoneId))
                        grouped[zoneId] = new List<Module>();
                    grouped[zoneId].Add(module);
                }

                // 变体激活映射：zoneId → variantSlug（仅保留 slug 非空且合法的条目）
                // 命中的 zone：写入 schemes/{dz}/variants/{slug}/{leaf}/modules.json，canonical 不动；
                // 未命中：照常写入 canonical modules.json。
                var variantSelection = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (request.VariantSelection != null)
                {
                    foreach (var kvp in request.VariantSelection)
                    {
                        if (string.IsNullOrWhiteSpace(kvp.Key) || string.IsNullOrWhiteSpace(kvp.Value))
                            continue;
                        try
                        {
                            ModuleFileTopologyService.EnsureSafeVariantId(kvp.Value);
                            variantSelection[kvp.Key] = kvp.Value;
                        }
                        catch (ArgumentException ex)
                        {
                            return BadRequest(new { message = $"variantSelection[{kvp.Key}] 非法: {ex.Message}" });
                        }
                    }
                }

                // 在 variantSelection 里出现但 grouped 中缺席的 zone 补空数组——
                // 用户可能把变体里的所有家具拖光了，得写空数组到变体文件而不是放任不管。
                foreach (var zoneId in variantSelection.Keys)
                {
                    if (!grouped.ContainsKey(zoneId))
                        grouped[zoneId] = new List<Module>();
                }

                // Step 3: 按 zone 状态分别清空旧文件
                //   variant 状态的 zone：清空对应 variants/{slug}/{leaf}/modules.json，canonical 不动
                //   canonical 状态的 zone：清空 canonical modules.json（防家具被拖出后残留）
                var existingCanonicalFiles = ModuleFileTopologyService.FindExistingCanonicalModuleFiles(schemesPath);
                foreach (var entry in existingCanonicalFiles)
                {
                    if (variantSelection.TryGetValue(entry.ZoneId, out var slug))
                    {
                        // 该 zone 在变体状态——不动 canonical，只清它的 New 路径变体文件
                        var dz = ResolveDesignZoneIdForLeaf(schemesPath, entry.ZoneId);
                        var variantFileToDelete = _modulesWriter.ResolveModulesPath(
                            projectPath, dz, entry.ZoneId, slug, VariantPathMode.New);
                        DeleteFileIfWritable(variantFileToDelete);
                    }
                    else
                    {
                        DeleteFileIfWritable(entry.FilePath);
                    }
                }

                // Step 4: 写入新数据（全部走 ModulesWriterService，wrapper 形态 + New 变体路径）
                foreach (var kvp in grouped)
                {
                    var leafZoneId = kvp.Key;
                    var designZoneId = ResolveDesignZoneIdForLeaf(schemesPath, leafZoneId);
                    var variantId = variantSelection.TryGetValue(leafZoneId, out var vid) ? vid : null;
                    await _modulesWriter.WriteAsync(
                        projectPath, designZoneId, leafZoneId, variantId,
                        VariantPathMode.New, kvp.Value);
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
        /// 把叶子 zoneId 反查为其所属设计区祖先 ID（schemes/ 下相对路径首段）。
        /// 顶层叶子 zone 时 designZoneId 等于 leafZoneId；嵌套叶子（如 rz_3/dz_1）时返回 rz_3。
        /// _unzoned 等不在拓扑里的特殊 zone 直接返回自身。
        /// </summary>
        private string ResolveDesignZoneIdForLeaf(string schemesPath, string leafZoneId)
        {
            if (string.Equals(leafZoneId, "_unzoned", StringComparison.OrdinalIgnoreCase))
                return leafZoneId;

            var zoneDir = ProjectService.ResolveZoneDirectory(schemesPath, leafZoneId);
            var relative = Path.GetRelativePath(schemesPath, zoneDir).Replace('\\', '/');
            var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return segments.Length > 0 ? segments[0] : leafZoneId;
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

            // zones.json
            var zonesPath = Path.Combine(schemePath, "zones.json");
            if (System.IO.File.Exists(zonesPath))
            {
                data.Zones = ReadJson<List<Zone>>(zonesPath) ?? new List<Zone>();
            }

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
        /// activePlugin 非空时读 project.json.scenes[] 做三态匹配 (Bound / SceneSelectRequired / RequiresSceneBinding)。
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

            // 有 active plugin → 读 project.json.scenes[] 匹配
            var scenes = ReadProjectScenes(projectPath);
            var matching = scenes
                .Where(s => string.Equals(s.Plugin?.Id, activePluginId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matching.Count == 1)
            {
                var matched = matching[0];
                var summary = new ProjectScenesSummary(scenes, matched.SceneId);
                var lockSummary = TryReadPluginLockSummary(projectPath, matched.SceneId);
                var launchContext = _pluginLifecycle.BuildLaunchContext(
                    projectPath, matched.SceneId, summary, lockSummary, readOnlySceneIds: null);
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
                    ActiveSceneId = matched.SceneId,
                    Warnings = warnings.Count > 0 ? warnings : null,
                });
            }

            if (matching.Count > 1)
            {
                _projectContext.SetPending(projectPath, bcpPath, Models.Plugins.OpenStatus.SceneSelectRequired, matching);
                return Ok(new ProjectLoadResult
                {
                    Status = "Success",
                    ProjectPath = projectPath,
                    OpenStatus = Models.Plugins.OpenStatus.SceneSelectRequired,
                    CurrentActivePlugin = activePluginId,
                    Candidates = matching,
                    Warnings = warnings.Count > 0 ? warnings : null,
                });
            }

            // 无匹配:要求用户决定新增 / 切回 core-base
            _projectContext.SetPending(projectPath, bcpPath, Models.Plugins.OpenStatus.RequiresSceneBinding, scenes);
            return Ok(new ProjectLoadResult
            {
                Status = "Success",
                ProjectPath = projectPath,
                OpenStatus = Models.Plugins.OpenStatus.RequiresSceneBinding,
                CurrentActivePlugin = activePluginId,
                ExistingScenes = scenes,
                Warnings = warnings.Count > 0 ? warnings : null,
            });
        }

        /// <summary>
        /// 读 project.json.scenes[] 转为强类型列表。文件不存在 / 无 scenes 字段 → 空列表。
        /// </summary>
        private List<ProjectScene> ReadProjectScenes(string projectPath)
        {
            var path = Path.Combine(projectPath, "project.json");
            if (!System.IO.File.Exists(path)) return new();
            try
            {
                var root = JObject.Parse(System.IO.File.ReadAllText(path));
                var arr = root["scenes"] as JArray;
                if (arr is null) return new();
                var result = new List<ProjectScene>(arr.Count);
                foreach (var token in arr)
                {
                    if (token is not JObject obj) continue;
                    var sceneId = (string?)obj["sceneId"];
                    if (string.IsNullOrEmpty(sceneId)) continue;
                    var sceneType = (string?)obj["scene"] ?? "";
                    var pluginObj = obj["plugin"] as JObject;
                    var plugin = new ScenePluginRef(
                        Id: (string?)pluginObj?["id"] ?? "",
                        VersionRange: (string?)pluginObj?["versionRange"] ?? "^1.0.0");
                    var statusStr = (string?)obj["status"] ?? "active";
                    Enum.TryParse<SceneStatus>(statusStr, ignoreCase: true, out var status);
                    var createdStr = (string?)obj["createdAt"];
                    var createdAt = DateTimeOffset.TryParse(createdStr, out var c) ? c : DateTimeOffset.MinValue;
                    result.Add(new ProjectScene(sceneId, sceneType, plugin, status, createdAt));
                }
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "读 project.json.scenes 失败: {Path}", path);
                return new();
            }
        }

        /// <summary>
        /// 读 plugins.lock.json[sceneId] 转为 PluginLockSummary;不存在 → null。
        /// </summary>
        private PluginLockSummary? TryReadPluginLockSummary(string projectPath, string sceneId)
        {
            var path = PluginPaths.ProjectPluginsLockFile(projectPath);
            if (!System.IO.File.Exists(path)) return null;
            try
            {
                var root = JObject.Parse(System.IO.File.ReadAllText(path));
                if (root[sceneId] is not JObject entry) return null;
                return new PluginLockSummary(
                    PluginId: (string?)entry["pluginId"] ?? "",
                    Version: (string?)entry["version"] ?? "",
                    SourceUrl: (string?)entry["sourceUrl"],
                    ResolvedCommit: (string?)entry["resolvedCommit"],
                    SourceKind: Enum.TryParse<SourceKind>((string?)entry["sourceKind"], ignoreCase: true, out var sk) ? sk : SourceKind.Github,
                    ManifestChecksum: (string?)entry["manifestChecksum"] ?? "",
                    ScaffoldChecksum: (string?)entry["scaffoldChecksum"],
                    TrustedAt: DateTimeOffset.TryParse((string?)entry["trustedAt"], out var t) ? t : null,
                    InstalledAt: DateTimeOffset.TryParse((string?)entry["installedAt"], out var i) ? i : DateTimeOffset.MinValue);
            }
            catch { return null; }
        }

        /// <summary>
        /// 在 plugins.lock.json 添加一个 sceneId 条目。直接从 plugins-state.json + manifest 拿 source 元数据,
        /// 避免再注入 PluginTrustService (信任元数据本身就是文件,Controller 读文件足够)。
        /// </summary>
        private void WritePluginLockEntry(string projectPath, string sceneId, string pluginId)
        {
            var stateFile = PluginPaths.PluginsStateFile;
            JObject? stateObj = null;
            if (System.IO.File.Exists(stateFile))
            {
                try { stateObj = JObject.Parse(System.IO.File.ReadAllText(stateFile))[pluginId] as JObject; }
                catch { }
            }
            // manifest 读 version
            string? version = null;
            var manifestPath = PluginPaths.PluginManifestFile(pluginId);
            if (System.IO.File.Exists(manifestPath))
            {
                try { version = (string?)JObject.Parse(System.IO.File.ReadAllText(manifestPath))["version"]; }
                catch { }
            }

            var lockPath = PluginPaths.ProjectPluginsLockFile(projectPath);
            JObject root;
            if (System.IO.File.Exists(lockPath))
            {
                try { root = JObject.Parse(System.IO.File.ReadAllText(lockPath)); }
                catch { root = new JObject(); }
            }
            else
            {
                root = new JObject();
            }

            var entry = new JObject
            {
                ["pluginId"] = pluginId,
                ["version"] = version ?? "",
                ["sourceUrl"] = (string?)stateObj?["sourceUrl"],
                ["resolvedCommit"] = (string?)stateObj?["resolvedCommit"],
                ["sourceKind"] = (string?)stateObj?["sourceKind"] ?? "github",
                ["manifestChecksum"] = (string?)stateObj?["manifestChecksum"] ?? "",
                ["scaffoldChecksum"] = null, // M2 物化时计算
                ["trustedAt"] = (string?)stateObj?["trustedAt"],
                ["installedAt"] = (string?)stateObj?["installedAt"] ?? DateTimeOffset.Now.ToString("o"),
            };
            root[sceneId] = entry;
            System.IO.File.WriteAllText(lockPath, root.ToString(Formatting.Indented), new UTF8Encoding(false));
        }
    }
}
