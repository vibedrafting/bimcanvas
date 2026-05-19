using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BIMCanvas.Core.Converters.Json;
using BIMCanvas.Core.Models.Layout;
using BIMCanvas.Server.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// Phase 0b 起 modules.json 的唯一读取入口。
    /// 严格只认 wrapper 格式 {schemeMetadata, modules}；
    /// 裸数组文件抛 InvalidOperationException，让上游走错误处理路径。
    /// </summary>
    public class ModulesReaderService
    {
        private readonly JsonSerializerSettings _jsonSettings;

        public ModulesReaderService()
        {
            _jsonSettings = new JsonSerializerSettings
            {
                ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
                Converters = { new Polygon2DConverter(), new Point2DConverter(), new FacingConverter() }
            };
        }

        /// <summary>
        /// 读取完整 wrapper。文件不存在返回 null。
        /// 文件存在但格式不是 wrapper（如裸数组）抛 InvalidOperationException。
        /// </summary>
        public ModulesWrapper? Read(string filePath)
        {
            if (!File.Exists(filePath))
                return null;

            var json = File.ReadAllText(filePath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json))
                return new ModulesWrapper();

            var token = JToken.Parse(json);
            if (token is JArray)
            {
                throw new InvalidOperationException(
                    $"modules.json 是裸数组格式，已不再支持。请先运行迁移脚本 MigrateModulesWrapper 升级文件 schema：{filePath}");
            }

            if (token is not JObject obj)
            {
                throw new InvalidOperationException(
                    $"modules.json 既不是 wrapper 对象也不是数组，无法解析：{filePath}");
            }

            var wrapper = obj.ToObject<ModulesWrapper>(JsonSerializer.Create(_jsonSettings))
                          ?? new ModulesWrapper();
            wrapper.SchemeMetadata ??= new SchemeMetadata();
            wrapper.Modules ??= new List<Module>();
            return wrapper;
        }

        /// <summary>
        /// 仅返回 modules 数组（内部解包）。文件不存在返回 null。
        /// 同样会因裸数组抛错。
        /// </summary>
        public List<Module>? ReadModulesOnly(string filePath)
        {
            var wrapper = Read(filePath);
            return wrapper?.Modules;
        }
    }
}
