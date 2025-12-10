using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace BIMCanvas.Revit.Services
{
    /// <summary>
    /// 画布导出配置选项
    /// </summary>
    [DataContract]
    public class ExportOptions
    {
        private const string ConfigFileName = "ExportOptions.json";

        /// <summary>
        /// 是否显示配置窗口（让用户确认房间类型）
        /// </summary>
        [DataMember(Name = "showConfigWindow")]
        public bool ShowConfigWindow { get; set; } = true;

        /// <summary>
        /// 默认保存路径（可选）
        /// </summary>
        [DataMember(Name = "defaultSavePath")]
        public string? DefaultSavePath { get; set; }

        /// <summary>
        /// 布置高度（毫米），默认 0（地面高度）
        /// </summary>
        [DataMember(Name = "placementElevation")]
        public double PlacementElevation { get; set; } = 0;

        /// <summary>
        /// 边界切割高度（毫米），默认 1200mm
        /// 后期可能根据所有窗户的窗台高度属性动态确定
        /// </summary>
        [DataMember(Name = "boundaryCutHeightMm")]
        public double BoundaryCutHeightMm { get; set; } = 1200;

        /// <summary>
        /// 是否导出边界轮廓
        /// </summary>
        [DataMember(Name = "exportBoundarys")]
        public bool ExportBoundarys { get; set; } = true;

        /// <summary>
        /// 是否导出门窗
        /// </summary>
        [DataMember(Name = "exportOpenings")]
        public bool ExportOpenings { get; set; } = true;

        /// <summary>
        /// 是否导出房间
        /// </summary>
        [DataMember(Name = "exportRooms")]
        public bool ExportRooms { get; set; } = true;

        /// <summary>
        /// 从程序集所在目录加载配置文件
        /// </summary>
        /// <returns>加载的配置，如果文件不存在或解析失败则返回默认配置</returns>
        public static ExportOptions Load()
        {
            try
            {
                var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(assemblyDir))
                {
                    return new ExportOptions();
                }

                var configPath = Path.Combine(assemblyDir, ConfigFileName);
                if (!File.Exists(configPath))
                {
                    return new ExportOptions();
                }

                var json = File.ReadAllText(configPath, Encoding.UTF8);
                return Deserialize(json) ?? new ExportOptions();
            }
            catch (Exception)
            {
                return new ExportOptions();
            }
        }

        /// <summary>
        /// 从指定路径加载配置文件
        /// </summary>
        /// <param name="configPath">配置文件路径</param>
        /// <returns>加载的配置，如果解析失败则返回默认配置</returns>
        public static ExportOptions LoadFrom(string configPath)
        {
            try
            {
                if (!File.Exists(configPath))
                {
                    return new ExportOptions();
                }

                var json = File.ReadAllText(configPath, Encoding.UTF8);
                return Deserialize(json) ?? new ExportOptions();
            }
            catch (Exception)
            {
                return new ExportOptions();
            }
        }

        /// <summary>
        /// 保存配置到程序集所在目录
        /// </summary>
        public void Save()
        {
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrEmpty(assemblyDir))
            {
                return;
            }

            var configPath = Path.Combine(assemblyDir, ConfigFileName);
            SaveTo(configPath);
        }

        /// <summary>
        /// 保存配置到指定路径
        /// </summary>
        /// <param name="configPath">配置文件路径</param>
        public void SaveTo(string configPath)
        {
            var json = Serialize(this);
            File.WriteAllText(configPath, json, Encoding.UTF8);
        }

        private static string Serialize(ExportOptions options)
        {
            var serializer = new DataContractJsonSerializer(typeof(ExportOptions));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, options);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static ExportOptions? Deserialize(string json)
        {
            var serializer = new DataContractJsonSerializer(typeof(ExportOptions));
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return serializer.ReadObject(stream) as ExportOptions;
            }
        }
    }
}
