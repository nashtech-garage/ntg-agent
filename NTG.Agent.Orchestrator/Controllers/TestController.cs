using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NTG.Agent.Orchestrator.Controllers;

[Route("api/[controller]")]
[ApiController]
public class TestController : ControllerBase
{
    public IActionResult Index()
    {
        Console.WriteLine("Test endpoint hit successfully.");
        return Ok($"Test endpoint is working correctly. Authorization is successful. User: {User.Identity!.Name}");
    }
}
