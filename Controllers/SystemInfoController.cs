using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuizAPI.Services;

namespace QuizAPI.Controllers;

[ApiController]
[Route("api/system")]
public sealed class SystemInfoController : ControllerBase
{
    private readonly ApplicationVersionInfoService _versionInfo;

    public SystemInfoController(ApplicationVersionInfoService versionInfo)
    {
        _versionInfo = versionInfo;
    }

    [AllowAnonymous]
    [HttpGet("version")]
    public IActionResult GetVersion()
    {
        return Ok(_versionInfo.CreateVersionPayload());
    }
}
