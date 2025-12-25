using Microsoft.AspNetCore.Mvc;
using BIMCanvas.Core.Models.Document;
using BIMCanvas.Server.Services;

namespace BIMCanvas.Server.Controllers
{
    /// <summary>
    /// 画布 REST API 控制器
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class CanvasController : ControllerBase
    {
        private readonly CanvasStateManager _stateManager;
        private readonly ZoneCalculator _zoneCalculator;
        private readonly ILogger<CanvasController> _logger;

        public CanvasController(
            CanvasStateManager stateManager,
            ZoneCalculator zoneCalculator,
            ILogger<CanvasController> logger)
        {
            _stateManager = stateManager;
            _zoneCalculator = zoneCalculator;
            _logger = logger;
        }

        /// <summary>
        /// 获取画布文档
        /// GET /api/canvas/{id}
        /// </summary>
        [HttpGet("{id}")]
        public ActionResult<DesignDocument> GetCanvas(string id)
        {
            var document = _stateManager.Get(id);
            if (document == null)
            {
                return NotFound(new { error = "Canvas not found", canvasId = id });
            }
            return Ok(document);
        }

        /// <summary>
        /// 创建/更新画布文档
        /// POST /api/canvas
        /// </summary>
        [HttpPost]
        public ActionResult<DesignDocument> CreateCanvas([FromBody] DesignDocument document)
        {
            if (document == null)
            {
                return BadRequest(new { error = "Invalid document" });
            }

            // 验证坐标系统
            if (document.CoordinateSystem != "cartesian_mm_yUp")
            {
                return BadRequest(new { error = "Invalid coordinate system", expected = "cartesian_mm_yUp" });
            }

            var stored = _stateManager.Store(document);
            _logger.LogInformation("Canvas stored: {Id}, version: {Version}", stored.Id, stored.Version);

            return CreatedAtAction(nameof(GetCanvas), new { id = stored.Id }, stored);
        }

        /// <summary>
        /// 加载并处理画布文档（计算禁区等）
        /// POST /api/canvas/load
        /// </summary>
        [HttpPost("load")]
        public ActionResult<DesignDocument> Load([FromBody] DesignDocument document)
        {
            if (document == null)
            {
                return BadRequest(new { error = "Invalid document" });
            }

            // 验证坐标系统
            if (!string.IsNullOrEmpty(document.CoordinateSystem) &&
                document.CoordinateSystem != "cartesian_mm_yUp")
            {
                return BadRequest(new { error = "Invalid coordinate system", expected = "cartesian_mm_yUp" });
            }

            // 确保坐标系统设置正确
            if (string.IsNullOrEmpty(document.CoordinateSystem))
            {
                document.CoordinateSystem = "cartesian_mm_yUp";
            }

            // 调用 ZoneCalculator 处理数据（计算禁区等）
            var processed = _zoneCalculator.Process(document);

            // 存储到状态管理器
            var stored = _stateManager.Store(processed);

            _logger.LogInformation("画布已加载并处理: {Id}, 版本: {Version}", stored.Id, stored.Version);

            return Ok(stored);
        }

        /// <summary>
        /// 获取所有画布ID列表
        /// GET /api/canvas
        /// </summary>
        [HttpGet]
        public ActionResult<IEnumerable<string>> GetAllCanvasIds()
        {
            return Ok(_stateManager.GetAllIds());
        }

        /// <summary>
        /// 删除画布
        /// DELETE /api/canvas/{id}
        /// </summary>
        [HttpDelete("{id}")]
        public ActionResult DeleteCanvas(string id)
        {
            if (!_stateManager.Exists(id))
            {
                return NotFound(new { error = "Canvas not found", canvasId = id });
            }

            _stateManager.Remove(id);
            _logger.LogInformation("Canvas deleted: {Id}", id);
            return NoContent();
        }
    }
}
