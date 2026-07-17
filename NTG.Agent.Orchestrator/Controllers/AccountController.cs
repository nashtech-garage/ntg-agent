using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NTG.Agent.Common.Dtos.Account;
using NTG.Agent.Orchestrator.Extentions;

namespace NTG.Agent.Orchestrator.Controllers;

/// <summary>
/// JSON authentication endpoints used by SPA clients (e.g. my-copilot-app) to sign in
/// against the shared ASP.NET Identity accounts. Issues/clears the
/// <c>.AspNetCore.Identity.Application</c> cookie via <see cref="SignInManager{TUser}"/>.
/// </summary>
[Route("api/account")]
[ApiController]
public class AccountController : ControllerBase
{
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly UserManager<IdentityUser> _userManager;

    public AccountController(SignInManager<IdentityUser> signInManager, UserManager<IdentityUser> userManager)
    {
        _signInManager = signInManager;
        _userManager = userManager;
    }

    [HttpPost("login")]
    public async Task<ActionResult<UserInfo>> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest("Email and password are required.");
        }

        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return Unauthorized("Invalid login attempt.");
        }

        var result = await _signInManager.PasswordSignInAsync(
            user.UserName!, request.Password, request.RememberMe, lockoutOnFailure: false);

        if (!result.Succeeded)
        {
            return Unauthorized("Invalid login attempt.");
        }

        return Ok(ToUserInfo(user));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return Ok();
    }

    [HttpGet("me")]
    public async Task<ActionResult<UserInfo>> Me()
    {
        var userId = User.GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var user = await _userManager.FindByIdAsync(userId.Value.ToString());
        if (user is null)
        {
            return Unauthorized();
        }

        return Ok(ToUserInfo(user));
    }

    private static UserInfo ToUserInfo(IdentityUser user) => new()
    {
        Id = Guid.TryParse(user.Id, out var id) ? id : Guid.Empty,
        Email = user.Email,
        UserName = user.UserName
    };
}
