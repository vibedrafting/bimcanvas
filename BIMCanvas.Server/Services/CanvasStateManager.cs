using System.Collections.Concurrent;
using BIMCanvas.Core.Models.RevitSource;

namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// 画布状态管理器 - 内存存储 + 版本控制
    /// </summary>
    public class CanvasStateManager
    {
        private readonly ConcurrentDictionary<string, CanvasDocument> _documents = new();

        /// <summary>
        /// 存储文档（自动递增版本号）
        /// </summary>
        public CanvasDocument Store(CanvasDocument document)
        {
            if (string.IsNullOrEmpty(document.Id))
            {
                document.Id = $"canvas_{Guid.NewGuid():N}";
            }

            _documents.AddOrUpdate(
                document.Id,
                // 新增时设置版本为1
                _ =>
                {
                    document.Version = 1;
                    return document;
                },
                // 更新时递增版本
                (_, existing) =>
                {
                    document.Version = existing.Version + 1;
                    return document;
                });

            return document;
        }

        /// <summary>
        /// 获取文档
        /// </summary>
        public CanvasDocument? Get(string id)
        {
            _documents.TryGetValue(id, out var document);
            return document;
        }

        /// <summary>
        /// 获取所有文档ID
        /// </summary>
        public IEnumerable<string> GetAllIds()
        {
            return _documents.Keys;
        }

        /// <summary>
        /// 检查文档是否存在
        /// </summary>
        public bool Exists(string id)
        {
            return _documents.ContainsKey(id);
        }

        /// <summary>
        /// 删除文档
        /// </summary>
        public bool Remove(string id)
        {
            return _documents.TryRemove(id, out _);
        }

        /// <summary>
        /// 检查版本是否匹配（乐观锁）
        /// </summary>
        public bool CheckVersion(string id, int expectedVersion)
        {
            if (_documents.TryGetValue(id, out var document))
            {
                return document.Version == expectedVersion;
            }
            return false;
        }
    }
}
