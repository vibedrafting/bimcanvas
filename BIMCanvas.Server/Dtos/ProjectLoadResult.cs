using System.Collections.Generic;
using BIMCanvas.Server.Models.Plugins;

namespace BIMCanvas.Server.Dtos
{
    /// <summary>
    /// 项目加载结果
    /// </summary>
    public class ProjectLoadResult
    {
        /// <summary>
        /// 加载状态：Success, Conflict, Error
        /// </summary>
        public string Status { get; set; } = "Success";

        /// <summary>
        /// 成功时的项目路径
        /// </summary>
        public string? ProjectPath { get; set; }

        /// <summary>
        /// 冲突时的已存在路径
        /// </summary>
        public string? ExistingPath { get; set; }

        /// <summary>
        /// 冲突时的项目名称
        /// </summary>
        public string? ProjectName { get; set; }

        /// <summary>
        /// 错误或提示信息
        /// </summary>
        public string? Message { get; set; }

        /// <summary>
        /// 成功时返回的 warning 列表
        /// </summary>
        public List<string>? Warnings { get; set; }

        // ─── v1.1 plugin / scene 解析字段 (主真理源 §4.7) ───

        /// <summary>
        /// scene 绑定状态。Web 据此决定弹哪个对话框 (null = legacy 路径 / 兼容模式)。
        /// </summary>
        public OpenStatus? OpenStatus { get; set; }

        /// <summary>
        /// 当前 server_config.json.agent.activePlugin (供 Web 提示用户)。
        /// </summary>
        public string? CurrentActivePlugin { get; set; }
    }

    /// <summary>
    /// 打开项目请求
    /// </summary>
    public class OpenProjectRequest
    {
        /// <summary>
        /// BCP 文件路径
        /// </summary>
        public string BcpFilePath { get; set; } = "";
    }

    /// <summary>
    /// 冲突解决请求
    /// </summary>
    public class ConflictResolutionRequest
    {
        /// <summary>
        /// BCP 文件路径
        /// </summary>
        public string BcpFilePath { get; set; } = "";

        /// <summary>
        /// 解决策略：Overwrite（覆盖）, UseExisting（使用已存在）
        /// </summary>
        public string Resolution { get; set; } = "Overwrite";
    }

    /// <summary>
    /// 项目摘要（轻量，用于列表展示）
    /// </summary>
    public class ProjectSummary
    {
        public string Name { get; set; } = "";
        public string FolderPath { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int SchemeCount { get; set; }
        public string? ActiveScheme { get; set; }
        public string Version { get; set; } = "";
        public bool IsValid { get; set; } = true;
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// 打开项目文件夹请求
    /// </summary>
    public class OpenFolderRequest
    {
        public string FolderPath { get; set; } = "";
    }

    /// <summary>
    /// 关闭项目请求
    /// </summary>
    public class CloseProjectRequest
    {
        public bool Force { get; set; }
    }

    /// <summary>
    /// 保存模块数据请求
    /// v3.3: 支持按分区保存
    /// </summary>
    public class SaveModulesRequest
    {
        /// <summary>
        /// 模块数据列表
        /// </summary>
        public List<BIMCanvas.Core.Models.Layout.Module>? Modules { get; set; }

        /// <summary>
        /// 可选的分区 ID
        /// 如果指定，只保存到该分区子目录 schemes/{zoneId}/modules.json
        /// 如果不指定，按模块的 zoneId 自动分组写入分区子目录
        /// </summary>
        public string? ZoneId { get; set; }

        /// <summary>
        /// 按叶子分区 id → variantId 的映射，表示该 zone 当前显示的是哪份变体。
        /// 命中的 zone：本次保存写入 modules-{variantId}.json，canonical modules.json 不动；
        /// 未命中（或 variantId 为空）：照常写入 canonical modules.json。
        /// 缺省（null/空）= 全 canonical，行为与旧版一致。
        /// </summary>
        public Dictionary<string, string>? VariantSelection { get; set; }
    }
}
