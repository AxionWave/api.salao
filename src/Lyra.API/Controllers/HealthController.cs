using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lyra.API.Controllers;

[ApiController]
public sealed class HealthController : ControllerBase
{
    /// <summary>Healthcheck do container (não passa pelo Gateway).</summary>
    [AllowAnonymous]
    [HttpGet("/health")]
    public IActionResult Health() => Ok(new { status = "healthy", product = "Lyra", service = "salao" });
}
