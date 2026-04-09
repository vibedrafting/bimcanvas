using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BIMCanvas.Server.Services
{
    public class ChatAttachmentService
    {
        private const string ManifestFileName = "_chat_attachments.json";
        private const string ScreenshotsDirectoryName = "screenshots";
        private static readonly Regex InvalidFileNameCharsRegex = new(
            $"[{Regex.Escape(new string(Path.GetInvalidFileNameChars()))}]",
            RegexOptions.Compiled);

        private readonly ProjectContext _projectContext;
        private readonly ILogger<ChatAttachmentService> _logger;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly JsonSerializerSettings _jsonSettings = new()
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        public ChatAttachmentService(
            ProjectContext projectContext,
            ILogger<ChatAttachmentService> logger)
        {
            _projectContext = projectContext;
            _logger = logger;
        }

        public async Task<ChatAttachmentRecord> SaveAsync(
            string projectPath,
            string windowId,
            string clientMessageId,
            string sourceKind,
            IFormFile file,
            int? width,
            int? height,
            CancellationToken cancellationToken)
        {
            if (file == null || file.Length <= 0)
            {
                throw new ArgumentException("上传文件不能为空");
            }

            projectPath = ValidateProjectPath(projectPath);
            windowId = string.IsNullOrWhiteSpace(windowId) ? "window-main" : windowId.Trim();
            clientMessageId = string.IsNullOrWhiteSpace(clientMessageId)
                ? Guid.NewGuid().ToString("N")
                : clientMessageId.Trim();
            sourceKind = NormalizeSourceKind(sourceKind);

            var screenshotsDir = EnsureScreenshotsDirectory(projectPath);
            var attachmentId = $"att_{Guid.NewGuid():N}";
            var extension = ResolveExtension(file.FileName, file.ContentType);
            var storedFileName = $"chat_{SanitizeFileName(windowId)}_{attachmentId}{extension}";
            var storedPath = Path.Combine(screenshotsDir, storedFileName);

            await using (var output = new FileStream(
                storedPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 81920,
                useAsync: true))
            {
                await file.CopyToAsync(output, cancellationToken);
            }

            var now = DateTime.UtcNow;
            var record = new ChatAttachmentRecord
            {
                AttachmentId = attachmentId,
                ProjectPath = projectPath,
                WindowId = windowId,
                ClientMessageId = clientMessageId,
                SourceKind = sourceKind,
                OriginalFileName = string.IsNullOrWhiteSpace(file.FileName) ? storedFileName : file.FileName,
                StoredFileName = storedFileName,
                StoredPath = storedPath,
                MimeType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
                SizeBytes = file.Length,
                Width = width,
                Height = height,
                Status = "draft",
                CreatedAt = now,
                LastUsedAt = now
            };

            await _gate.WaitAsync(cancellationToken);
            try
            {
                var manifest = await LoadManifestAsync(projectPath, cancellationToken);
                manifest.GeneratedAt = now;
                manifest.Attachments.Add(record);
                await SaveManifestAsync(projectPath, manifest, cancellationToken);
            }
            catch
            {
                SafeDeleteFile(storedPath);
                throw;
            }
            finally
            {
                _gate.Release();
            }

            _logger.LogInformation(
                "聊天附件已保存: {AttachmentId} -> {StoredPath}",
                record.AttachmentId,
                record.StoredPath);

            return record;
        }

        public async Task<ChatAttachmentRecord> GetAsync(
            string projectPath,
            string attachmentId,
            CancellationToken cancellationToken)
        {
            projectPath = ValidateProjectPath(projectPath);
            attachmentId = NormalizeAttachmentId(attachmentId);

            await _gate.WaitAsync(cancellationToken);
            try
            {
                var manifest = await LoadManifestAsync(projectPath, cancellationToken);
                return FindAttachment(manifest, attachmentId);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task DeleteAsync(
            string projectPath,
            string attachmentId,
            CancellationToken cancellationToken)
        {
            projectPath = ValidateProjectPath(projectPath);
            attachmentId = NormalizeAttachmentId(attachmentId);

            await _gate.WaitAsync(cancellationToken);
            try
            {
                var manifest = await LoadManifestAsync(projectPath, cancellationToken);
                var record = FindAttachment(manifest, attachmentId);

                if (string.Equals(record.Status, "deleted", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (string.Equals(record.Status, "draft", StringComparison.OrdinalIgnoreCase))
                {
                    SafeDeleteFile(record.StoredPath);
                }

                record.Status = "deleted";
                record.LastUsedAt = DateTime.UtcNow;
                manifest.GeneratedAt = DateTime.UtcNow;

                await SaveManifestAsync(projectPath, manifest, cancellationToken);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task CommitAsync(
            string projectPath,
            string windowId,
            string clientMessageId,
            IReadOnlyCollection<string> attachmentIds,
            CancellationToken cancellationToken)
        {
            if (attachmentIds == null || attachmentIds.Count == 0)
            {
                return;
            }

            projectPath = ValidateProjectPath(projectPath);
            windowId = string.IsNullOrWhiteSpace(windowId) ? "window-main" : windowId.Trim();
            clientMessageId = string.IsNullOrWhiteSpace(clientMessageId) ? string.Empty : clientMessageId.Trim();
            var normalizedIds = attachmentIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(NormalizeAttachmentId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            await _gate.WaitAsync(cancellationToken);
            try
            {
                var manifest = await LoadManifestAsync(projectPath, cancellationToken);
                var now = DateTime.UtcNow;

                foreach (var attachmentId in normalizedIds)
                {
                    var record = FindAttachment(manifest, attachmentId);

                    if (!string.Equals(record.WindowId, windowId, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"附件不属于当前窗口: {attachmentId}");
                    }

                    if (!string.IsNullOrWhiteSpace(clientMessageId) &&
                        !string.Equals(record.ClientMessageId, clientMessageId, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"附件不属于当前消息: {attachmentId}");
                    }

                    if (!string.Equals(record.Status, "deleted", StringComparison.OrdinalIgnoreCase))
                    {
                        record.Status = "submitted";
                        record.LastUsedAt = now;
                    }
                }

                manifest.GeneratedAt = now;
                await SaveManifestAsync(projectPath, manifest, cancellationToken);
            }
            finally
            {
                _gate.Release();
            }
        }

        private string ValidateProjectPath(string projectPath)
        {
            if (!_projectContext.IsLoaded || string.IsNullOrWhiteSpace(_projectContext.CurrentProjectPath))
            {
                throw new InvalidOperationException("当前没有已加载项目");
            }

            if (string.IsNullOrWhiteSpace(projectPath))
            {
                throw new ArgumentException("projectPath 不能为空");
            }

            var requestedPath = Path.GetFullPath(projectPath);
            var currentProjectPath = Path.GetFullPath(_projectContext.CurrentProjectPath);

            if (!string.Equals(requestedPath, currentProjectPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("projectPath 与当前已加载项目不一致");
            }

            if (!Directory.Exists(requestedPath))
            {
                throw new DirectoryNotFoundException($"项目目录不存在: {requestedPath}");
            }

            return requestedPath;
        }

        private string EnsureScreenshotsDirectory(string projectPath)
        {
            var screenshotsDir = Path.Combine(projectPath, ScreenshotsDirectoryName);
            Directory.CreateDirectory(screenshotsDir);
            return screenshotsDir;
        }

        private string GetManifestPath(string projectPath)
        {
            return Path.Combine(EnsureScreenshotsDirectory(projectPath), ManifestFileName);
        }

        private async Task<ChatAttachmentManifest> LoadManifestAsync(string projectPath, CancellationToken cancellationToken)
        {
            var manifestPath = GetManifestPath(projectPath);
            if (!File.Exists(manifestPath))
            {
                return new ChatAttachmentManifest();
            }

            await using var stream = new FileStream(
                manifestPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 81920,
                useAsync: true);
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(json))
            {
                return new ChatAttachmentManifest();
            }

            return JsonConvert.DeserializeObject<ChatAttachmentManifest>(json, _jsonSettings)
                   ?? new ChatAttachmentManifest();
        }

        private async Task SaveManifestAsync(
            string projectPath,
            ChatAttachmentManifest manifest,
            CancellationToken cancellationToken)
        {
            var manifestPath = GetManifestPath(projectPath);
            var tempPath = manifestPath + ".tmp";
            var json = JsonConvert.SerializeObject(manifest, _jsonSettings);

            await File.WriteAllTextAsync(tempPath, json, cancellationToken);
            File.Move(tempPath, manifestPath, overwrite: true);
        }

        private static ChatAttachmentRecord FindAttachment(ChatAttachmentManifest manifest, string attachmentId)
        {
            var record = manifest.Attachments.FirstOrDefault(item =>
                string.Equals(item.AttachmentId, attachmentId, StringComparison.OrdinalIgnoreCase));

            if (record == null)
            {
                throw new FileNotFoundException($"附件不存在: {attachmentId}");
            }

            return record;
        }

        private static string NormalizeSourceKind(string sourceKind)
        {
            if (string.IsNullOrWhiteSpace(sourceKind))
            {
                return "upload";
            }

            sourceKind = sourceKind.Trim().ToLowerInvariant();
            return sourceKind switch
            {
                "screenshot" => "screenshot",
                "paste" => "paste",
                _ => "upload"
            };
        }

        private static string NormalizeAttachmentId(string attachmentId)
        {
            if (string.IsNullOrWhiteSpace(attachmentId))
            {
                throw new ArgumentException("attachmentId 不能为空");
            }

            return attachmentId.Trim();
        }

        private static string SanitizeFileName(string value)
        {
            var sanitized = InvalidFileNameCharsRegex.Replace(value, "_");
            return sanitized.Replace(' ', '_');
        }

        private static string ResolveExtension(string originalFileName, string? mimeType)
        {
            var originalExtension = Path.GetExtension(originalFileName);
            if (!string.IsNullOrWhiteSpace(originalExtension))
            {
                return originalExtension.ToLowerInvariant();
            }

            return (mimeType ?? string.Empty).ToLowerInvariant() switch
            {
                "image/jpeg" => ".jpg",
                "image/webp" => ".webp",
                "image/gif" => ".gif",
                "image/bmp" => ".bmp",
                "image/tiff" => ".tiff",
                _ => ".png"
            };
        }

        private void SafeDeleteFile(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                File.Delete(path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "删除聊天附件文件失败: {Path}", path);
            }
        }
    }

    public class ChatAttachmentManifest
    {
        public int Version { get; set; } = 1;
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
        public List<ChatAttachmentRecord> Attachments { get; set; } = new();
    }

    public class ChatAttachmentRecord
    {
        public string AttachmentId { get; set; } = string.Empty;
        public string ProjectPath { get; set; } = string.Empty;
        public string WindowId { get; set; } = string.Empty;
        public string ClientMessageId { get; set; } = string.Empty;
        public string SourceKind { get; set; } = string.Empty;
        public string OriginalFileName { get; set; } = string.Empty;
        public string StoredFileName { get; set; } = string.Empty;
        public string StoredPath { get; set; } = string.Empty;
        public string MimeType { get; set; } = "application/octet-stream";
        public long SizeBytes { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
        public string Status { get; set; } = "draft";
        public DateTime CreatedAt { get; set; }
        public DateTime LastUsedAt { get; set; }
    }
}
