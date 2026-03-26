using BIMCanvas.Server.Dtos;
using BIMCanvas.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace BIMCanvas.Server.Controllers;

[ApiController]
[Route("api/settings")]
public class SettingsController : ControllerBase
{
    private readonly SettingsService _settingsService;
    private readonly SettingsRestartService _restartService;

    public SettingsController(
        SettingsService settingsService,
        SettingsRestartService restartService)
    {
        _settingsService = settingsService;
        _restartService = restartService;
    }

    [HttpGet]
    public ActionResult<SettingsSnapshotDto> Get()
    {
        return Ok(_settingsService.GetSettings());
    }

    [HttpPut]
    public ActionResult<UpdateSettingsResponseDto> Save([FromBody] UpdateSettingsRequestDto request)
    {
        try
        {
            return Ok(_settingsService.SaveSettings(request));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                success = false,
                message = ex.Message
            });
        }
    }

    [HttpPost("restart")]
    public IActionResult Restart()
    {
        var scheduled = _restartService.TryScheduleRestart();
        return Accepted(new
        {
            success = true,
            scheduled,
            message = scheduled
                ? "实例重启请求已提交，当前连接可能会中断。"
                : "实例正在重启中，请稍后刷新。"
        });
    }
}
