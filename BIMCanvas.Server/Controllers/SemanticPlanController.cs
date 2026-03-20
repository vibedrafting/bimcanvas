using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BIMCanvas.Server.Hubs;
using BIMCanvas.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace BIMCanvas.Server.Controllers
{
    [ApiController]
    [Route("api/semantic-plan")]
    public class SemanticPlanController : ControllerBase
    {
        private readonly ProjectContext _projectContext;
        private readonly IHubContext<CanvasHub> _hubContext;
        private readonly ILogger<SemanticPlanController> _logger;

        public SemanticPlanController(
            ProjectContext projectContext,
            IHubContext<CanvasHub> hubContext,
            ILogger<SemanticPlanController> logger)
        {
            _projectContext = projectContext;
            _hubContext = hubContext;
            _logger = logger;
        }

        [HttpPost("save")]
        public async Task<ActionResult> SaveSemanticPlan([FromBody] SaveSemanticPlanRequest request)
        {
            if (!_projectContext.IsLoaded)
                return BadRequest(new { message = "没有加载的项目" });

            var projectPath = _projectContext.GetActiveWorktreePath()
                              ?? _projectContext.CurrentProjectPath!;

            // 存储路径：schemes/{zoneId}/semantic_plan.json
            var schemesDir = Path.Combine(projectPath, "schemes", request.ZoneId);
            Directory.CreateDirectory(schemesDir);
            var filePath = Path.Combine(schemesDir, "semantic_plan.json");

            // 读取现有版本（如果存在）
            var versions = new List<SemanticPlanVersion>();
            if (System.IO.File.Exists(filePath))
            {
                var existing = System.IO.File.ReadAllText(filePath);
                versions = JsonConvert.DeserializeObject<List<SemanticPlanVersion>>(existing)
                           ?? new List<SemanticPlanVersion>();
            }

            // 添加或更新版本（同版本覆盖）
            var entry = new SemanticPlanVersion
            {
                ZoneId = request.ZoneId,
                Version = request.Version,
                Content = request.Content,
                Timestamp = DateTime.UtcNow.ToString("o")
            };

            versions.RemoveAll(v => v.Version == request.Version);
            versions.Add(entry);
            versions.Sort((a, b) => string.Compare(a.Version, b.Version, StringComparison.Ordinal));

            // 写入文件
            var json = JsonConvert.SerializeObject(versions, Formatting.Indented);
            await System.IO.File.WriteAllTextAsync(filePath, json);

            // SignalR 推送到 Web 端
            await _hubContext.Clients.All.SendAsync("SemanticPlanUpdated", new
            {
                zoneId = request.ZoneId,
                version = request.Version,
                content = request.Content,
                timestamp = entry.Timestamp
            });

            _logger.LogInformation(
                "[SemanticPlan] 已保存 {ZoneId} {Version}",
                request.ZoneId, request.Version);

            return Ok(new { saved = true, version = request.Version });
        }
    }

    public class SaveSemanticPlanRequest
    {
        public string ZoneId { get; set; }
        public string Version { get; set; }
        public string Content { get; set; }
    }

    public class SemanticPlanVersion
    {
        public string ZoneId { get; set; }
        public string Version { get; set; }
        public string Content { get; set; }
        public string Timestamp { get; set; }
    }
}
