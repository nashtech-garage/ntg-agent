using Microsoft.EntityFrameworkCore;
using NTG.Agent.Common.Dtos.Agents;
using NTG.Agent.Common.Dtos.Constants;
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
        if (!userId.HasValue)
        {
            var anonymousRoleId = Guid.Parse(Constants.AnonymousRoleId);
            return await _db.Agents.AnyAsync(a =>
                a.Id == agentId
                && a.IsPublished
                && a.AgentKind == AgentKind.Agent
                && _db.AgentRoles.Any(ar => ar.AgentId == a.Id && ar.RoleId == anonymousRoleId),
                ct);
        }

        return await _db.Agents.AnyAsync(a =>
            a.Id == agentId
            && a.IsPublished
            && (a.OwnerUserId == userId || isAdmin
                || _db.AgentRoles.Any(ar =>
                    ar.AgentId == a.Id
                    && _db.UserRoles.Any(ur => ur.UserId == userId && ur.RoleId == ar.RoleId))),
            ct);
    }

    public IQueryable<Models.Agents.Agent> AccessibleAgentsQuery(Guid? userId, bool isAdmin)
    {
        if (!userId.HasValue)
        {
            var anonymousRoleId = Guid.Parse(Constants.AnonymousRoleId);
            return _db.Agents.Where(a =>
                a.IsPublished
                && a.AgentKind == AgentKind.Agent
                && _db.AgentRoles.Any(ar => ar.AgentId == a.Id && ar.RoleId == anonymousRoleId));
        }

        return _db.Agents.Where(a =>
            a.IsPublished
            && (a.OwnerUserId == userId || isAdmin
                || _db.AgentRoles.Any(ar =>
                    ar.AgentId == a.Id
                    && _db.UserRoles.Any(ur => ur.UserId == userId && ur.RoleId == ar.RoleId))));
    }
}
