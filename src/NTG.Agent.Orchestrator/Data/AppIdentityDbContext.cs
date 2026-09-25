using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace NTG.Agent.Orchestrator.Data;

/// <summary>
/// Read/write access to the ASP.NET Identity tables that are owned (schema + migrations)
/// by the WebClient's ApplicationDbContext. The Orchestrator uses this only to issue and
/// validate the shared <c>.AspNetCore.Identity.Application</c> cookie via SignInManager.
/// Because <c>ApplicationUser : IdentityUser</c> adds no columns, stock <see cref="IdentityUser"/>
/// maps onto the same tables. Do NOT create migrations from this context.
/// </summary>
public class AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options) : IdentityDbContext<IdentityUser>(options)
{
}
