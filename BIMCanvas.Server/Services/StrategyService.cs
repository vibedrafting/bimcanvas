using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BIMCanvas.Core.Models.Project;
using BIMCanvas.Core.Models.Shared;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// 策略管理服务
    /// 负责 schemes/ 目录下策略的创建和管理
    /// </summary>
    public class StrategyService
    {
        private readonly ILogger<StrategyService> _logger;
        private readonly JsonSerializerSettings _jsonSettings;

        public StrategyService(ILogger<StrategyService> logger)
        {
            _logger = logger;
            _jsonSettings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Formatting = Formatting.Indented
            };
        }

        /// <summary>
        /// 创建默认策略
        /// </summary>
        /// <param name="schemesPath">schemes 目录路径</param>
        /// <param name="baselineHash">baseline 哈希值</param>
        /// <returns>策略 ID</returns>
        public string CreateDefaultStrategy(string schemesPath, string baselineHash)
        {
            var strategyId = "s1_Default";
            var strategyPath = Path.Combine(schemesPath, strategyId);

            _logger.LogInformation("创建默认策略: {Path}", strategyPath);

            // 创建策略目录
            Directory.CreateDirectory(strategyPath);

            // 创建 strategy.json
            var strategy = new Strategy
            {
                Id = strategyId,
                Name = "Default",
                Approach = StrategyApproach.CirculationFirst,
                Description = "默认策略",
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
                Origin = null,
                LastValidatedBaselineHash = baselineHash,
                Status = StrategyStatus.Valid
            };
            WriteJsonFile(Path.Combine(strategyPath, "strategy.json"), strategy);

            // 创建空的 zones.json
            WriteJsonFile(Path.Combine(strategyPath, "zones.json"), new List<object>());

            // 创建空的 finishes.json
            WriteJsonFile(Path.Combine(strategyPath, "finishes.json"), new List<object>());

            // 创建空的 modules.json
            WriteJsonFile(Path.Combine(strategyPath, "modules.json"), new List<object>());

            _logger.LogInformation("默认策略创建完成: {Id}", strategyId);
            return strategyId;
        }

        /// <summary>
        /// 创建新策略
        /// </summary>
        /// <param name="schemesPath">schemes 目录路径</param>
        /// <param name="name">策略名称</param>
        /// <param name="approach">设计方法</param>
        /// <param name="baselineHash">baseline 哈希值</param>
        /// <returns>策略 ID</returns>
        public string CreateStrategy(string schemesPath, string name, StrategyApproach approach, string baselineHash)
        {
            // 生成策略 ID
            var existingCount = Directory.Exists(schemesPath)
                ? Directory.GetDirectories(schemesPath).Length
                : 0;
            var strategyId = $"s{existingCount + 1}_{SanitizeName(name)}";
            var strategyPath = Path.Combine(schemesPath, strategyId);

            _logger.LogInformation("创建策略: {Id} ({Name})", strategyId, name);

            // 创建策略目录
            Directory.CreateDirectory(strategyPath);

            // 创建 strategy.json
            var strategy = new Strategy
            {
                Id = strategyId,
                Name = name,
                Approach = approach,
                Description = null,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
                Origin = null,
                LastValidatedBaselineHash = baselineHash,
                Status = StrategyStatus.Valid
            };
            WriteJsonFile(Path.Combine(strategyPath, "strategy.json"), strategy);

            // 创建空文件
            WriteJsonFile(Path.Combine(strategyPath, "zones.json"), new List<object>());
            WriteJsonFile(Path.Combine(strategyPath, "finishes.json"), new List<object>());
            WriteJsonFile(Path.Combine(strategyPath, "modules.json"), new List<object>());

            _logger.LogInformation("策略创建完成: {Id}", strategyId);
            return strategyId;
        }

        /// <summary>
        /// 检查策略是否存在
        /// </summary>
        public bool StrategyExists(string schemesPath, string strategyId)
        {
            var strategyPath = Path.Combine(schemesPath, strategyId);
            return Directory.Exists(strategyPath) &&
                   File.Exists(Path.Combine(strategyPath, "strategy.json"));
        }

        /// <summary>
        /// 获取所有策略 ID
        /// </summary>
        public List<string> GetAllStrategyIds(string schemesPath)
        {
            if (!Directory.Exists(schemesPath))
                return new List<string>();

            var result = new List<string>();
            foreach (var dir in Directory.GetDirectories(schemesPath))
            {
                var strategyJsonPath = Path.Combine(dir, "strategy.json");
                if (File.Exists(strategyJsonPath))
                {
                    result.Add(Path.GetFileName(dir));
                }
            }
            return result;
        }

        /// <summary>
        /// 初始化策略 Git 仓库（后续实现）
        /// </summary>
        public void InitializeGit(string strategyPath)
        {
            _logger.LogDebug("Git 初始化暂未实现: {Path}", strategyPath);
            // TODO: 执行 git init
        }

        /// <summary>
        /// 创建变体（Git 分支，后续实现）
        /// </summary>
        public void CreateVariant(string strategyPath, string branchName)
        {
            _logger.LogDebug("创建变体暂未实现: {Branch} @ {Path}", branchName, strategyPath);
            // TODO: 执行 git checkout -b {branchName}
        }

        /// <summary>
        /// 切换变体（后续实现）
        /// </summary>
        public void SwitchVariant(string strategyPath, string branchName)
        {
            _logger.LogDebug("切换变体暂未实现: {Branch} @ {Path}", branchName, strategyPath);
            // TODO: 执行 git checkout {branchName}
        }

        /// <summary>
        /// 写入 JSON 文件
        /// </summary>
        private void WriteJsonFile(string path, object data)
        {
            var json = JsonConvert.SerializeObject(data, _jsonSettings);
            File.WriteAllText(path, json, Encoding.UTF8);
        }

        /// <summary>
        /// 清理名称（用于文件夹命名）
        /// </summary>
        private static string SanitizeName(string name)
        {
            // 移除或替换非法字符
            var invalid = Path.GetInvalidFileNameChars();
            var result = new StringBuilder();
            foreach (var c in name)
            {
                if (Array.IndexOf(invalid, c) >= 0)
                    result.Append('_');
                else
                    result.Append(c);
            }
            return result.ToString();
        }
    }
}
