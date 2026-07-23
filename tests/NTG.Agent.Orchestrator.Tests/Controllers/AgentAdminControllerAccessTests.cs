using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NTG.Agent.Common.Dtos.Agents;
using NTG.Agent.Common.Dtos.Tags;
using NTG.Agent.Orchestrator.Controllers;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Models.Agents;
using NTG.Agent.Orchestrator.Models.Identity;
using NTG.Agent.Orchestrator.Services.Agents;
using NTG.Agent.Common.Knowledge;
using System.Security.Claims;
using AgentModel = NTG.Agent.Orchestrator.Models.Agents.Agent;

namespace NTG.Agent.Orchestrator.Tests.Controllers;

[TestFixture]
public class AgentAdminControllerAccessTests
{
    private AgentDbContext _context;
    private AgentAdminController _controller;
    private AgentAccessService _accessService;
    private Guid _testAdminUserId;
    private Guid _testAgentId;
    private Guid _testRoleId;

    [SetUp]
    public void Setup()
    {
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new AgentDbContext(options);
        _accessService = new AgentAccessService(_context);
        _testAdminUserId = Guid.NewGuid();
        _testAgentId = Guid.NewGuid();
        _testRoleId = Guid.NewGuid();

        // Seed role
        _context.Roles.Add(new Role { Id = _testRoleId, Name = "Engineering" });
        // Seed admin user
        _context.Users.Add(new User { Id = _testAdminUserId, UserName = "admin", Email = "admin@test.com" });
        // Seed agent
        _context.Agents.Add(new AgentModel        {
            Id = _testAgentId,
            Name = "Test Agent",
            Instructions = "Test",
            IsPublished = true,
            OwnerUserId = _testAdminUserId,
            UpdatedByUserId = _testAdminUserId
        });
        _context.SaveChanges();

        var adminUser = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, _testAdminUserId.ToString()),
            new Claim(ClaimTypes.Role, "Admin"),
        ], "mock"));

        _controller = new AgentAdminController(
            _context,
            Mock.Of<IAgentFactory>(),
            Mock.Of<IKnowledgeProvisioner>(),
            Mock.Of<IKnowledgeService>(),
            NullLogger<AgentAdminController>.Instance,
            _accessService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = adminUser }
            }
        };
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    // GetAgentAccess tests

    [Test]
    public async Task GetAgentAccess_WhenNoGrants_ReturnsEmptyList()
    {
        var result = await _controller.GetAgentAccess(_testAgentId);
        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var grants = okResult!.Value as List<AgentRoleGrantDto>;
        Assert.That(grants, Is.Not.Null);
        Assert.That(grants, Is.Empty);
    }

    [Test]
    public async Task GetAgentAccess_WhenGrantsExist_ReturnsGrants()
    {
        var grantId = Guid.NewGuid();
        _context.AgentRoles.Add(new AgentRole { Id = grantId, AgentId = _testAgentId, RoleId = _testRoleId });
        _context.SaveChanges();

        var result = await _controller.GetAgentAccess(_testAgentId);
        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var grants = okResult!.Value as List<AgentRoleGrantDto>;
        Assert.That(grants, Is.Not.Null);
        Assert.That(grants, Has.Count.EqualTo(1));
        Assert.That(grants[0].RoleId, Is.EqualTo(_testRoleId));
        Assert.That(grants[0].RoleName, Is.EqualTo("Engineering"));
    }

    [Test]
    public async Task GetAgentAccess_WhenAgentNotFound_ReturnsNotFound()
    {
        var result = await _controller.GetAgentAccess(Guid.NewGuid());
        Assert.That(result, Is.TypeOf<NotFoundObjectResult>());
    }

    // GrantAgentAccess tests

    [Test]
    public async Task GrantAgentAccess_WhenValid_ReturnsGrant()
    {
        var result = await _controller.GrantAgentAccess(_testAgentId, new GrantAgentAccessRequest(_testRoleId));
        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var grant = okResult!.Value as AgentRoleGrantDto;
        Assert.That(grant, Is.Not.Null);
        Assert.That(grant!.RoleId, Is.EqualTo(_testRoleId));
        Assert.That(grant.RoleName, Is.EqualTo("Engineering"));
    }

    [Test]
    public async Task GrantAgentAccess_WhenAgentNotFound_ReturnsNotFound()
    {
        var result = await _controller.GrantAgentAccess(Guid.NewGuid(), new GrantAgentAccessRequest(_testRoleId));
        Assert.That(result, Is.TypeOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task GrantAgentAccess_WhenRoleNotFound_ReturnsBadRequest()
    {
        var result = await _controller.GrantAgentAccess(_testAgentId, new GrantAgentAccessRequest(Guid.NewGuid()));
        Assert.That(result, Is.TypeOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task GrantAgentAccess_WhenAlreadyGranted_ReturnsExistingGrant()
    {
        // First grant
        await _controller.GrantAgentAccess(_testAgentId, new GrantAgentAccessRequest(_testRoleId));
        // Second grant (idempotent)
        var result = await _controller.GrantAgentAccess(_testAgentId, new GrantAgentAccessRequest(_testRoleId));
        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var grant = okResult!.Value as AgentRoleGrantDto;
        Assert.That(grant, Is.Not.Null);
        Assert.That(grant!.RoleId, Is.EqualTo(_testRoleId));

        // Verify only one row in DB
        var grants = await _context.AgentRoles.Where(ar => ar.AgentId == _testAgentId).ToListAsync();
        Assert.That(grants, Has.Count.EqualTo(1));
    }

    // RevokeAgentAccess tests

    [Test]
    public async Task RevokeAgentAccess_WhenGrantExists_ReturnsNoContent()
    {
        await _controller.GrantAgentAccess(_testAgentId, new GrantAgentAccessRequest(_testRoleId));

        var result = await _controller.RevokeAgentAccess(_testAgentId, _testRoleId);
        Assert.That(result, Is.TypeOf<NoContentResult>());

        // Verify grant was removed
        var grants = await _context.AgentRoles.Where(ar => ar.AgentId == _testAgentId).ToListAsync();
        Assert.That(grants, Is.Empty);
    }

    [Test]
    public async Task RevokeAgentAccess_WhenGrantDoesNotExist_ReturnsNoContent()
    {
        // Idempotent revoke
        var result = await _controller.RevokeAgentAccess(_testAgentId, _testRoleId);
        Assert.That(result, Is.TypeOf<NoContentResult>());
    }

    [Test]
    public async Task RevokeAgentAccess_WhenAgentNotFound_ReturnsNotFound()
    {
        var result = await _controller.RevokeAgentAccess(Guid.NewGuid(), _testRoleId);
        Assert.That(result, Is.TypeOf<NotFoundObjectResult>());
    }

    // GetRoles tests

    [Test]
    public async Task GetRoles_ReturnsAllRoles()
    {
        var result = await _controller.GetRoles();
        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var roles = okResult!.Value as List<RoleDto>;
        Assert.That(roles, Is.Not.Null);
        Assert.That(roles, Has.Count.EqualTo(1));
        Assert.That(roles![0].Name, Is.EqualTo("Engineering"));
    }
}
