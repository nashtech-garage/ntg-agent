using Microsoft.EntityFrameworkCore;
using NTG.Agent.Orchestrator.Data;

namespace NTG.Agent.Orchestrator.Services.Agents;

public sealed class AgentAccessService
{
    private readonly AgentDbContext _db;

    public AgentAccessService(AgentDbContext db)
    {
        _db = db;
    }

    public async Task<bool> HasAccessAsync(Guid agentId, Guid? userId, bool isAdmin, CancellationToken ct = default)
    {
        return await _db.Agents.AnyAsync(a =>
            a.Id == agentId
            && a.IsPublished
            && (!userId.HasValue || a.OwnerUserId == userId || isAdmin
                || _db.AgentRoles.Any(ar =>
                    ar.AgentId == a.Id
                    && _db.UserRoles.Any(ur => ur.UserId == userId && ur.RoleId == ar.RoleId))),
            ct);
    }

    public IQueryable<Models.Agents.Agent> AccessibleAgentsQuery(Guid? userId, bool isAdmin)
    {
        return _db.Agents.Where(a =>
            a.IsPublished
            && (!userId.HasValue || a.OwnerUserId == userId || isAdmin
                || _db.AgentRoles.Any(ar =>
                    ar.AgentId == a.Id
                    && _db.UserRoles.Any(ur => ur.UserId == userId && ur.RoleId == ar.RoleId))));
    }
}
