using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using NTG.Agent.Common.Dtos.Agents;
using NTG.Agent.Common.Knowledge;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Exceptions;
using NTG.Agent.Orchestrator.Models.Agents;
using NTG.Agent.Orchestrator.Models.Identity;
using NTG.Agent.Orchestrator.Services.Agents;
using AgentModel = NTG.Agent.Orchestrator.Models.Agents.Agent;

namespace NTG.Agent.Orchestrator.Tests.Services;

[TestFixture]
public class AgentFactoryTests
{
    private AgentDbContext _context;
    private AgentFactory _factory;
    private Guid _userId;
    private Guid _roleId;

    [SetUp]
    public void Setup()
    {
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new AgentDbContext(options);
        _factory = new AgentFactory(
            new ConfigurationBuilder().Build(),
            _context,
            new Mock<IKnowledgeService>().Object,
            new AgentAccessService(_context),
            new RenderableToolCapture());

        _userId = Guid.NewGuid();
        _roleId = Guid.NewGuid();

        // Seed a user holding a role that will be granted access to the agents under test.
        _context.Roles.Add(new Role { Id = _roleId, Name = "Engineering" });
        _context.Users.Add(new User { Id = _userId, UserName = "user", Email = "user@test.com" });
        _context.UserRoles.Add(new UserRole { UserId = _userId, RoleId = _roleId });
        _context.SaveChanges();
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    // Seeds a published agent of the given kind with a role grant for _userId's role,
    // so any refusal in CreateAgent stems from the agent's kind, not from access.
    private Guid SeedGrantedAgent(AgentKind kind)
    {
        var agentId = Guid.NewGuid();
        _context.Agents.Add(new AgentModel
        {
            Id = agentId,
            Name = "Test Agent",
            Instructions = "Test",
            IsPublished = true,
            AgentKind = kind,
            ProviderName = "Bogus", // unreachable for Inner; makes Outer fail *after* the access gate
            OwnerUserId = Guid.NewGuid(),
            UpdatedByUserId = Guid.NewGuid()
        });
        _context.AgentRoles.Add(new AgentRole { Id = Guid.NewGuid(), AgentId = agentId, RoleId = _roleId });
        _context.SaveChanges();
        return agentId;
    }

    [Test]
    public void CreateAgent_WithUser_InnerAgentWithRoleGrant_ThrowsAccessDenied()
    {
        // Inner agents are tool-only: even with a valid role grant, the user-facing
        // overload must refuse to instantiate one directly (PR #277 review fix).
        var agentId = SeedGrantedAgent(AgentKind.Inner);

        Assert.ThrowsAsync<AgentAccessDeniedException>(() =>
            _factory.CreateAgent(agentId, _userId, isAdmin: false));
    }

    [Test]
    public void CreateAgent_WithUser_OuterAgentWithRoleGrant_PassesAccessGate()
    {
        // Control for the test above: an identical Outer agent gets through the access
        // gate and only fails later at provider creation (bogus provider name) — proving
        // the Inner refusal is kind-based, not access-based.
        var agentId = SeedGrantedAgent(AgentKind.Outer);

        Assert.ThrowsAsync<NotSupportedException>(() =>
            _factory.CreateAgent(agentId, _userId, isAdmin: false));
    }
}
