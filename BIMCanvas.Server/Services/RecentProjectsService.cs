using System.Text.Json;

namespace BIMCanvas.Server.Services;

/// <summary>
/// 最近打开项目记录服务
/// 管理 <BIMCANVAS_HOME>/recent_projects.json
/// </summary>
public class RecentProjectsService
{
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>
    /// 加载最近项目列表（按最后打开时间降序）
    /// </summary>
    public List<RecentProjectEntry> Load()
    {
        var path = ConfigService.GetRecentProjectsPath();
        if (!File.Exists(path))
            return new List<RecentProjectEntry>();

        try
        {
            var json = File.ReadAllText(path);
            var entries = JsonSerializer.Deserialize<List<RecentProjectEntry>>(json, ReadOptions);
            return entries ?? new List<RecentProjectEntry>();
        }
        catch
        {
            return new List<RecentProjectEntry>();
        }
    }

    /// <summary>
    /// 记录项目打开（线程安全）
    /// </summary>
    public void RecordOpen(string name, string folderPath)
    {
        lock (_lock)
        {
            var entries = Load();

            var existing = entries.FirstOrDefault(e =>
                string.Equals(e.FolderPath, folderPath, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                existing.LastOpenedAt = DateTime.Now;
                existing.OpenCount++;
                existing.Name = name;
            }
            else
            {
                entries.Add(new RecentProjectEntry
                {
                    Name = name,
                    FolderPath = folderPath,
                    LastOpenedAt = DateTime.Now,
                    OpenCount = 1
                });
            }

            Save(entries);
        }
    }

    /// <summary>
    /// 移除记录（项目被删除时调用）
    /// </summary>
    public void Remove(string folderPath)
    {
        lock (_lock)
        {
            var entries = Load();
            entries.RemoveAll(e =>
                string.Equals(e.FolderPath, folderPath, StringComparison.OrdinalIgnoreCase));
            Save(entries);
        }
    }

    /// <summary>
    /// 加载并过滤掉已不存在的项目（按最后打开时间降序）
    /// </summary>
    public List<RecentProjectEntry> LoadWithExistsCheck()
    {
        var entries = Load();
        var valid = entries.Where(e => Directory.Exists(e.FolderPath)).ToList();

        // 如果有失效记录，自动清理
        if (valid.Count < entries.Count)
        {
            lock (_lock)
            {
                Save(valid);
            }
        }

        return valid.OrderByDescending(e => e.LastOpenedAt).ToList();
    }

    private void Save(List<RecentProjectEntry> entries)
    {
        var path = ConfigService.GetRecentProjectsPath();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(entries, WriteOptions);
        File.WriteAllText(path, json);
    }
}

/// <summary>
/// 最近项目记录条目
/// </summary>
public class RecentProjectEntry
{
    public string Name { get; set; } = "";
    public string FolderPath { get; set; } = "";
    public DateTime LastOpenedAt { get; set; }
    public int OpenCount { get; set; }
}
