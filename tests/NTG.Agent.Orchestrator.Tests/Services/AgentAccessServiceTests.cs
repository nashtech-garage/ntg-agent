using Microsoft.EntityFrameworkCore;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Models.Agents;
using NTG.Agent.Orchestrator.Models.Identity;
using NTG.Agent.Orchestrator.Services.Agents;
using AgentModel = NTG.Agent.Orchestrator.Models.Agents.Agent;

namespace NTG.Agent.Orchestrator.Tests.Services;

[TestFixture]
public class AgentAccessServiceTests
{
    private AgentDbContext _context;
    private AgentAccessService _service;
    private Guid _ownerId;
    private Guid _otherUserId;
    private Guid _adminUserId;
    private Guid _roleId;
    private Guid _agentId;

    [SetUp]
    public void Setup()
    {
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new AgentDbContext(options);
        _service = new AgentAccessService(_context);

        _ownerId = Guid.NewGuid();
        _otherUserId = Guid.NewGuid();
        _adminUserId = Guid.NewGuid();
        _roleId = Guid.NewGuid();
        _agentId = Guid.NewGuid();

        // Seed a role
        _context.Roles.Add(new Role { Id = _roleId, Name = "Engineering" });

        // Seed users
        _context.Users.AddRange(
            new User { Id = _ownerId, UserName = "owner", Email = "owner@test.com" },
            new User { Id = _otherUserId, UserName = "other", Email = "other@test.com" },
            new User { Id = _adminUserId, UserName = "admin", Email = "admin@test.com" }
        );

        // Seed a published agent owned by _ownerId
        _context.Agents.Add(new AgentModel
        {
            Id = _agentId,
            Name = "Test Agent",
            Instructions = "Test",
            IsPublished = true,
            OwnerUserId = _ownerId,
            UpdatedByUserId = _ownerId
        });

        _context.SaveChanges();
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    // HasAccessAsync tests

    [Test]
    public async Task HasAccessAsync_OwnerUser_ReturnsTrue()
    {
        var result = await _service.HasAccessAsync(_agentId, _ownerId, isAdmin: false, CancellationToken.None);
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task HasAccessAsync_AdminUser_ReturnsTrue()
    {
        var result = await _service.HasAccessAsync(_agentId, _adminUserId, isAdmin: true, CancellationToken.None);
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task HasAccessAsync_UserWithRoleGrant_ReturnsTrue()
    {
        _context.UserRoles.Add(new UserRole { UserId = _otherUserId, RoleId = _roleId });
        _context.AgentRoles.Add(new AgentRole { Id = Guid.NewGuid(), AgentId = _agentId, RoleId = _roleId });
        _context.SaveChanges();

        var result = await _service.HasAccessAsync(_agentId, _otherUserId, isAdmin: false, CancellationToken.None);
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task HasAccessAsync_UserWithoutRoleGrant_ReturnsFalse()
    {
        var result = await _service.HasAccessAsync(_agentId, _otherUserId, isAdmin: false, CancellationToken.None);
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task HasAccessAsync_NullUserId_ReturnsFalse()
    {
        var result = await _service.HasAccessAsync(_agentId, null, isAdmin: false, CancellationToken.None);
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task HasAccessAsync_UnpublishedAgent_ReturnsFalse()
    {
        var unpublishedId = Guid.NewGuid();
        _context.Agents.Add(new AgentModel
        {
            Id = unpublishedId,
            Name = "Unpublished",
            Instructions = "",
            IsPublished = false,
            OwnerUserId = _ownerId,
            UpdatedByUserId = _ownerId
        });
        _context.SaveChanges();

        var result = await _service.HasAccessAsync(unpublishedId, _ownerId, isAdmin: false, CancellationToken.None);
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task HasAccessAsync_NonExistentAgent_ReturnsFalse()
    {
        var result = await _service.HasAccessAsync(Guid.NewGuid(), _ownerId, isAdmin: false, CancellationToken.None);
        Assert.That(result, Is.False);
    }

    // AccessibleAgentsQuery tests

    [Test]
    public async Task AccessibleAgentsQuery_OwnerUser_ReturnsOwnedAgents()
    {
        var agents = await _service.AccessibleAgentsQuery(_ownerId, isAdmin: false).ToListAsync();
        Assert.That(agents, Has.Count.EqualTo(1));
        Assert.That(agents[0].Id, Is.EqualTo(_agentId));
    }

    [Test]
    public async Task AccessibleAgentsQuery_AdminUser_ReturnsAllPublishedAgents()
    {
        var agents = await _service.AccessibleAgentsQuery(_adminUserId, isAdmin: true).ToListAsync();
        Assert.That(agents, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task AccessibleAgentsQuery_UserWithRoleGrant_ReturnsGrantedAgents()
    {
        _context.UserRoles.Add(new UserRole { UserId = _otherUserId, RoleId = _roleId });
        _context.AgentRoles.Add(new AgentRole { Id = Guid.NewGuid(), AgentId = _agentId, RoleId = _roleId });
        _context.SaveChanges();

        var agents = await _service.AccessibleAgentsQuery(_otherUserId, isAdmin: false).ToListAsync();
        Assert.That(agents, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task AccessibleAgentsQuery_UserWithoutAccess_ReturnsEmpty()
    {
        var agents = await _service.AccessibleAgentsQuery(_otherUserId, isAdmin: false).ToListAsync();
        Assert.That(agents, Is.Empty);
    }

    [Test]
    public async Task AccessibleAgentsQuery_NullUserId_ReturnsEmpty()
    {
        var agents = await _service.AccessibleAgentsQuery(null, isAdmin: false).ToListAsync();
        Assert.That(agents, Is.Empty);
    }

    [Test]
    public async Task AccessibleAgentsQuery_ExcludesUnpublishedAgents()
    {
        _context.Agents.Add(new AgentModel
        {
            Id = Guid.NewGuid(),
            Name = "Unpublished",
            Instructions = "",
            IsPublished = false,
            OwnerUserId = _ownerId,
            UpdatedByUserId = _ownerId
        });
        _context.SaveChanges();

        var agents = await _service.AccessibleAgentsQuery(_ownerId, isAdmin: false).ToListAsync();
        // Owner can only see published agents they own — unpublished are excluded
        Assert.That(agents, Has.Count.EqualTo(1));
        Assert.That(agents.All(a => a.IsPublished), Is.True);
    }
}
