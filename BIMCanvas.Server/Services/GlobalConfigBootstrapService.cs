namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// BIMCANVAS_HOME 下全局配置资产初始化。
    /// 仅补齐缺失项，不覆盖已有内容。
    /// </summary>
    public sealed class GlobalConfigBootstrapService
    {
        private const string ManifestRelativePath = "global-config/manifest.json";

        private readonly BootstrapTemplateService _templateService;

        public GlobalConfigBootstrapService(BootstrapTemplateService templateService)
        {
            _templateService = templateService;
        }

        public void EnsureInitialized()
        {
            _templateService.EnsureInitializedFromManifest(
                ManifestRelativePath,
                ConfigService.GetConfigDir());
        }
    }
}
