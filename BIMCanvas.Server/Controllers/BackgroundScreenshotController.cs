using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BIMCanvas.Server.Dtos;
using BIMCanvas.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BIMCanvas.Server.Controllers
{
    [ApiController]
    [Route("api/screenshot")]
    public class BackgroundScreenshotController : ControllerBase
    {
        private readonly BackgroundScreenshotService _screenshotService;
        private readonly ILogger<BackgroundScreenshotController> _logger;

        public BackgroundScreenshotController(
            BackgroundScreenshotService screenshotService,
            ILogger<BackgroundScreenshotController> logger)
        {
            _screenshotService = screenshotService;
            _logger = logger;
        }

        [HttpPost("render")]
        public async Task<ActionResult<BackgroundScreenshotResponse>> Render(
            [FromBody] BackgroundScreenshotRequest request,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.ProjectPath))
            {
                return BadRequest(new { message = "projectPath 不能为空" });
            }

            if (!Directory.Exists(request.ProjectPath))
            {
                if (System.IO.File.Exists(request.ProjectPath))
                {
                    return BadRequest(new { message = "projectPath 必须是解压后的项目目录（不是 .bcp 文件）" });
                }
                return NotFound(new { message = $"项目目录不存在: {request.ProjectPath}" });
            }

            if (request.Scale < 1 || request.Scale > 4)
            {
                return BadRequest(new { message = "scale 必须在 1-4 之间" });
            }

            var viewportMode = request.Viewport?.Mode?.ToLowerInvariant();
            if (viewportMode == "bounds" && request.Viewport?.Bounds == null)
            {
                return BadRequest(new { message = "viewport.mode=bounds 时必须提供 bounds" });
            }

            if (viewportMode == "room" && string.IsNullOrWhiteSpace(request.Viewport?.RoomId))
            {
                return BadRequest(new { message = "viewport.mode=room 时必须提供 roomId" });
            }

            try
            {
                var imageData = await _screenshotService.CaptureAsync(request, cancellationToken);
                return Ok(new BackgroundScreenshotResponse { ImageData = imageData });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "后台截图失败");
                return StatusCode(500, new { message = $"后台截图失败: {ex.Message}" });
            }
        }
    }
}
