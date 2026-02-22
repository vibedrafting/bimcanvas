using Microsoft.AspNetCore.Mvc;
using BIMCanvas.Server.Models;
using BIMCanvas.Server.Services;

namespace BIMCanvas.Server.Controllers;

[ApiController]
[Route("api/web_config")]
public class WebConfigController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        // 直接返回原始 JSON，避免 Newtonsoft CamelCasePropertyNamesContractResolver
        // 将字典键 "User"/"Agent" 转成 "user"/"agent" 导致前端匹配失败
        var json = ConfigService.LoadWebConfigRaw();
        return Content(json, "application/json");
    }

    [HttpPost]
    public IActionResult Save([FromBody] WebConfig config)
    {
        ConfigService.SaveWebConfig(config);
        return Ok(new { success = true });
    }
}
