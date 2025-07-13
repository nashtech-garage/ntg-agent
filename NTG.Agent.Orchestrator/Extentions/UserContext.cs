using System.Security.Claims;

namespace NTG.Agent.Orchestrator.Extentions;

public static class UserContextExtensions
{
    public static Guid? GetUserId(this ClaimsPrincipal user)
    {
        Guid? userId = null;
        if (user.Identity?.IsAuthenticated == true)
        {
            userId = Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
        }

        return userId;
    }
}
