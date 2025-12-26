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
}
