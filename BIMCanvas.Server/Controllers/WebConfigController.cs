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
        var config = ConfigService.LoadWebConfig();
        return Ok(config);
    }

    [HttpPost]
    public IActionResult Save([FromBody] WebConfig config)
    {
        ConfigService.SaveWebConfig(config);
        return Ok(new { success = true });
    }
}
