using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NTG.Agent.Orchestrator.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class TestController : ControllerBase
{
    public IActionResult Index()
    {
        return Ok($"Test endpoint is working correctly. Authorization is successful. User: {User.Identity!.Name}");
    }
}
