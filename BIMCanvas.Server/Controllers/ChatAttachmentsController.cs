using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BIMCanvas.Server.Dtos;
using BIMCanvas.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Logging;

namespace BIMCanvas.Server.Controllers
{
    [ApiController]
    [Route("api/chat/attachments")]
    public class ChatAttachmentsController : ControllerBase
    {
        private readonly ChatAttachmentService _attachmentService;
        private readonly ILogger<ChatAttachmentsController> _logger;
        private readonly FileExtensionContentTypeProvider _contentTypeProvider = new();

        public ChatAttachmentsController(
            ChatAttachmentService attachmentService,
            ILogger<ChatAttachmentsController> logger)
        {
            _attachmentService = attachmentService;
            _logger = logger;
        }

        [HttpPost]
        [RequestSizeLimit(25 * 1024 * 1024)]
        public async Task<ActionResult<ChatAttachmentRef>> Upload(
            [FromForm] string projectPath,
            [FromForm] string windowId,
            [FromForm] string clientMessageId,
            [FromForm] string sourceKind,
            [FromForm] IFormFile file,
            [FromForm] int? width,
            [FromForm] int? height,
            CancellationToken cancellationToken)
        {
            try
            {
                var record = await _attachmentService.SaveAsync(
                    projectPath,
                    windowId,
                    clientMessageId,
                    sourceKind,
                    file,
                    width,
                    height,
                    cancellationToken);

                return Ok(ToAttachmentRef(record));
            }
            catch (Exception ex) when (
                ex is ArgumentException ||
                ex is InvalidOperationException ||
                ex is DirectoryNotFoundException)
            {
                _logger.LogWarning(ex, "上传聊天附件失败");
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("{attachmentId}/content")]
        public async Task<IActionResult> GetContent(
            string attachmentId,
            [FromQuery] string projectPath,
            CancellationToken cancellationToken)
        {
            try
            {
                var record = await _attachmentService.GetAsync(projectPath, attachmentId, cancellationToken);
                if (string.Equals(record.Status, "deleted", StringComparison.OrdinalIgnoreCase))
                {
                    return NotFound(new { message = "附件已删除" });
                }

                if (!System.IO.File.Exists(record.StoredPath))
                {
                    return NotFound(new { message = $"附件文件不存在: {record.StoredPath}" });
                }

                var contentType = ResolveContentType(record.StoredPath, record.MimeType);
                return PhysicalFile(record.StoredPath, contentType, enableRangeProcessing: true);
            }
            catch (FileNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex) when (
                ex is ArgumentException ||
                ex is InvalidOperationException ||
                ex is DirectoryNotFoundException)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpDelete("{attachmentId}")]
        public async Task<IActionResult> Delete(
            string attachmentId,
            [FromQuery] string projectPath,
            CancellationToken cancellationToken)
        {
            try
            {
                await _attachmentService.DeleteAsync(projectPath, attachmentId, cancellationToken);
                return Ok(new { success = true });
            }
            catch (FileNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex) when (
                ex is ArgumentException ||
                ex is InvalidOperationException ||
                ex is DirectoryNotFoundException)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("commit")]
        public async Task<IActionResult> Commit(
            [FromBody] CommitChatAttachmentsRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return BadRequest(new { message = "请求体不能为空" });
            }

            try
            {
                await _attachmentService.CommitAsync(
                    request.ProjectPath,
                    request.WindowId,
                    request.ClientMessageId,
                    request.AttachmentIds,
                    cancellationToken);
                return Ok(new { success = true });
            }
            catch (FileNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex) when (
                ex is ArgumentException ||
                ex is InvalidOperationException ||
                ex is DirectoryNotFoundException)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        private ChatAttachmentRef ToAttachmentRef(ChatAttachmentRecord record)
        {
            return new ChatAttachmentRef
            {
                AttachmentId = record.AttachmentId,
                ClientMessageId = record.ClientMessageId,
                SourceKind = record.SourceKind,
                OriginalFileName = record.OriginalFileName,
                MimeType = record.MimeType,
                SizeBytes = record.SizeBytes,
                Width = record.Width,
                Height = record.Height,
                Status = record.Status,
                ContentUrl = $"{Request.Scheme}://{Request.Host}/api/chat/attachments/{record.AttachmentId}/content?projectPath={Uri.EscapeDataString(record.ProjectPath)}"
            };
        }

        private string ResolveContentType(string filePath, string fallbackMimeType)
        {
            return _contentTypeProvider.TryGetContentType(filePath, out var resolved)
                ? resolved
                : fallbackMimeType;
        }
    }
}
