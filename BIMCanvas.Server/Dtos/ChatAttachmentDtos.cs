using System.Collections.Generic;

namespace BIMCanvas.Server.Dtos
{
    public class ChatAttachmentRef
    {
        public string AttachmentId { get; set; } = string.Empty;
        public string ClientMessageId { get; set; } = string.Empty;
        public string SourceKind { get; set; } = string.Empty;
        public string OriginalFileName { get; set; } = string.Empty;
        public string MimeType { get; set; } = "application/octet-stream";
        public long SizeBytes { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
        public string Status { get; set; } = "draft";
        public string ContentUrl { get; set; } = string.Empty;
    }

    public class CommitChatAttachmentsRequest
    {
        public string ProjectPath { get; set; } = string.Empty;
        public string WindowId { get; set; } = string.Empty;
        public string ClientMessageId { get; set; } = string.Empty;
        public List<string> AttachmentIds { get; set; } = new();
    }
}
