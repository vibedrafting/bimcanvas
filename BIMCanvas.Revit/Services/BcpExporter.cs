using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using BIMCanvas.Core.Converters.Json;
using BIMCanvas.Core.Models.Computed;
using BIMCanvas.Core.Models.Layout;
using BIMCanvas.Core.Models.Project;
using BIMCanvas.Core.Models.Revit;
using BIMCanvas.Core.Models.Shared;
using BIMCanvas.Core.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BIMCanvas.Revit.Services
{
    /// <summary>
    /// BCP (BIMCanvas Project) 导出器
    /// 将数据导出为多文件夹结构并打包为 .bcp 压缩包
    /// </summary>
    public class BcpExporter
    {
        private readonly JsonSerializerSettings _jsonSettings;

        public BcpExporter()
        {
            _jsonSettings = CreateJsonSettings();
        }

        /// <summary>
        /// 导出为 .bcp 文件
        /// </summary>
        /// <param name="outputPath">输出路径（不含扩展名）</param>
        /// <param name="projectName">项目名称</param>
        /// <param name="manifest">Baseline 元数据</param>
        /// <param name="architecture">建筑构造（墙体 + 柱子）</param>
        /// <param name="openings">门窗列表</param>
        /// <param name="rooms">房间列表</param>
        /// <param name="locationLines">定位线列表</param>
        /// <returns>.bcp 文件完整路径</returns>
        public string ExportToBcp(
            string outputPath,
            string projectName,
            BaselineManifest manifest,
            Architecture architecture,
            List<Opening> openings,
            List<Room> rooms,
            List<LocationLine> locationLines)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("输出路径不能为空", nameof(outputPath));

            // 确保目录存在
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            // 创建临时文件夹
            var tempDir = Path.Combine(Path.GetTempPath(), $"bcp_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                // 创建文件夹结构
                CreateFolderStructure(tempDir, projectName, manifest, architecture,
                    openings, rooms, locationLines);

                // 打包为 .bcp (ZIP)
                var bcpPath = outputPath + ".bcp";
                if (File.Exists(bcpPath))
                {
                    File.Delete(bcpPath);
                }

                ZipFile.CreateFromDirectory(tempDir, bcpPath, CompressionLevel.Optimal, false);

                return bcpPath;
            }
            finally
            {
                // 清理临时目录
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch
                {
                    // 忽略清理失败
                }
            }
        }

        /// <summary>
        /// 导出为解压后的文件夹结构（用于调试或直接使用）
        /// </summary>
        public string ExportToFolder(
            string outputDir,
            string projectName,
            BaselineManifest manifest,
            Architecture architecture,
            List<Opening> openings,
            List<Room> rooms,
            List<LocationLine> locationLines)
        {
            if (string.IsNullOrWhiteSpace(outputDir))
                throw new ArgumentException("输出目录不能为空", nameof(outputDir));

            // 如果目录已存在，清空
            if (Directory.Exists(outputDir))
            {
                Directory.Delete(outputDir, true);
            }
            Directory.CreateDirectory(outputDir);

            CreateFolderStructure(outputDir, projectName, manifest, architecture,
                openings, rooms, locationLines);

            return outputDir;
        }

        /// <summary>
        /// 创建文件夹结构并写入 JSON 文件
        /// </summary>
        private void CreateFolderStructure(
            string rootDir,
            string projectName,
            BaselineManifest manifest,
            Architecture architecture,
            List<Opening> openings,
            List<Room> rooms,
            List<LocationLine> locationLines)
        {
            var now = DateTime.Now;

            // 创建子目录
            var baselineDir = Path.Combine(rootDir, "baseline");
            var contextDir = Path.Combine(rootDir, "context");
            var schemesDir = Path.Combine(rootDir, "schemes");
            var defaultSchemeDir = Path.Combine(schemesDir, "s1_Default");

            Directory.CreateDirectory(baselineDir);
            Directory.CreateDirectory(contextDir);
            Directory.CreateDirectory(defaultSchemeDir);

            // 计算 baseline hash
            string baselineHash;
            try
            {
                baselineHash = ComputeBaselineHashFromData(manifest, architecture, openings, rooms, locationLines);
            }
            catch
            {
                baselineHash = $"sha256:{Guid.NewGuid():N}";
            }

            // 1. 写入 project.json
            var project = new Project
            {
                Id = $"proj_{SanitizeName(projectName)}_{now:yyyyMMddHHmmss}",
                Name = projectName,
                Version = "3.0",
                CreatedAt = now,
                UpdatedAt = now,
                CoordinateSystem = "cartesian_mm_yUp",
                ActiveSchemeId = "s1_Default",
                Schemes = new List<SchemeRef>
                {
                    new SchemeRef
                    {
                        Id = "s1_Default",
                        Path = "schemes/s1_Default",
                        Name = "Default"
                    }
                }
            };
            WriteJson(Path.Combine(rootDir, "project.json"), project);

            // 2. 写入 baseline/metadata.json
            WriteJson(Path.Combine(baselineDir, "metadata.json"), manifest);

            // 3. 写入 baseline/architecture.json
            WriteJson(Path.Combine(baselineDir, "architecture.json"), architecture);

            // 4. 写入 baseline/openings.json
            WriteJson(Path.Combine(baselineDir, "openings.json"), openings);

            // 5. 写入 baseline/rooms.json
            WriteJson(Path.Combine(baselineDir, "rooms.json"), rooms);

            // 6. 写入 baseline/location_lines.json
            WriteJson(Path.Combine(baselineDir, "location_lines.json"), locationLines);

            // 7. 写入 context/requirements.md（空模板）
            File.WriteAllText(
                Path.Combine(contextDir, "requirements.md"),
                "# 设计需求\n\n> 在此填写项目设计需求和约束条件\n\n",
                Encoding.UTF8);

            // 8. 写入 schemes/s1_Default/strategy.json
            var strategy = new Strategy
            {
                Id = "s1_Default",
                Name = "Default",
                Approach = StrategyApproach.CirculationFirst,
                Description = "从 Revit 导出的默认策略",
                CreatedAt = now,
                UpdatedAt = now,
                Origin = null,
                LastValidatedBaselineHash = baselineHash,
                Status = StrategyStatus.Valid
            };
            WriteJson(Path.Combine(defaultSchemeDir, "strategy.json"), strategy);

            // 9. 写入 schemes/s1_Default/zones.json（空数组）
            WriteJson(Path.Combine(defaultSchemeDir, "zones.json"), new List<Zone>());

            // 10. 写入 schemes/s1_Default/finishes.json（空数组）
            WriteJson(Path.Combine(defaultSchemeDir, "finishes.json"), new List<FinishSegment>());

            // 11. 写入 schemes/s1_Default/modules.json（空数组）
            WriteJson(Path.Combine(defaultSchemeDir, "modules.json"), new List<Module>());
        }

        /// <summary>
        /// 从数据计算 baseline hash
        /// </summary>
        private string ComputeBaselineHashFromData(
            BaselineManifest manifest,
            Architecture architecture,
            List<Opening> openings,
            List<Room> rooms,
            List<LocationLine> locationLines)
        {
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                var combined = new StringBuilder();

                // 按固定顺序序列化
                combined.Append(NormalizeJson(JsonConvert.SerializeObject(manifest, _jsonSettings)));
                combined.Append(NormalizeJson(JsonConvert.SerializeObject(architecture, _jsonSettings)));
                combined.Append(NormalizeJson(JsonConvert.SerializeObject(rooms, _jsonSettings)));
                combined.Append(NormalizeJson(JsonConvert.SerializeObject(openings, _jsonSettings)));
                combined.Append(NormalizeJson(JsonConvert.SerializeObject(locationLines, _jsonSettings)));

                var bytes = Encoding.UTF8.GetBytes(combined.ToString());
                var hash = sha256.ComputeHash(bytes);
                var hashString = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

                return $"sha256:{hashString}";
            }
        }

        /// <summary>
        /// 规范化 JSON（移除格式差异）
        /// </summary>
        private string NormalizeJson(string json)
        {
            return System.Text.RegularExpressions.Regex.Replace(json, @"\s+", "");
        }

        /// <summary>
        /// 写入 JSON 文件
        /// </summary>
        private void WriteJson<T>(string filePath, T data)
        {
            var json = JsonConvert.SerializeObject(data, _jsonSettings);
            File.WriteAllText(filePath, json, Encoding.UTF8);
        }

        /// <summary>
        /// 创建 JSON 序列化设置
        /// </summary>
        private static JsonSerializerSettings CreateJsonSettings()
        {
            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            };

            // 添加自定义转换器
            settings.Converters.Add(new StrategyStatusConverter());
            settings.Converters.Add(new StrategyApproachConverter());
            settings.Converters.Add(new FinishSourceConverter());

            return settings;
        }

        /// <summary>
        /// 净化名称（移除特殊字符）
        /// </summary>
        private static string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "unnamed";

            var result = new StringBuilder();
            foreach (var c in name)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                {
                    result.Append(c);
                }
                else if (c == ' ')
                {
                    result.Append('_');
                }
            }

            return result.Length > 0 ? result.ToString() : "unnamed";
        }
    }
}
