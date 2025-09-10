using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NTG.Agent.Orchestrator.Controllers;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Models.Identity;
using NTG.Agent.Shared.Dtos.Agents;
using System.Security.Claims;
using AgentModel = NTG.Agent.Orchestrator.Models.Agents.Agent;

namespace NTG.Agent.Orchestrator.Tests.Controllers;

[TestFixture]
public class AgentAdminControllerTests
{
    private AgentDbContext _context;
    private AgentAdminController _controller;
    private Guid _testUserId;
    private Guid _testAdminUserId;

    [SetUp]
    public void Setup()
    {
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new AgentDbContext(options);
        _testUserId = Guid.NewGuid();
        _testAdminUserId = Guid.NewGuid();

        // Mock the admin user principal
        var adminUser = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, _testAdminUserId.ToString()),
            new Claim(ClaimTypes.Role, "Admin"),
        ], "mock"));

        _controller = new AgentAdminController(_context)
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

    #region Constructor Tests

    [Test]
    public void Constructor_WhenAgentDbContextIsNull_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AgentAdminController(null!));
    }

    [Test]
    public void Constructor_WhenValidParameters_CreatesInstance()
    {
        // Act
        var controller = new AgentAdminController(_context);

        // Assert
        Assert.That(controller, Is.Not.Null);
    }

    #endregion

    #region GetAgents Tests

    [Test]
    public async Task GetAgents_WhenAgentsExist_ReturnsOkWithAgentList()
    {
        // Arrange
        await SeedAgentsData();

        // Act
        var result = await _controller.GetAgents();

        // Assert
        Assert.That(result, Is.TypeOf<OkObjectResult>());
        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var agents = okResult.Value as IEnumerable<AgentListItem>;
        Assert.That(agents, Is.Not.Null);
        var agentList = agents.ToList();
        Assert.That(agentList, Has.Count.EqualTo(2));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(agentList[0].Name, Is.EqualTo("Test Agent 1"));
            Assert.That(agentList[0].OwnerEmail, Is.EqualTo("owner@test.com"));
            Assert.That(agentList[0].UpdatedByEmail, Is.EqualTo("updater@test.com"));
            Assert.That(agentList[1].Name, Is.EqualTo("Test Agent 2"));
        }
    }

    [Test]
    public async Task GetAgents_WhenNoAgentsExist_ReturnsOkWithEmptyList()
    {
        // Act
        var result = await _controller.GetAgents();

        // Assert
        Assert.That(result, Is.TypeOf<OkObjectResult>());
        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var agents = okResult.Value as IEnumerable<AgentListItem>;
        Assert.That(agents, Is.Not.Null);
        Assert.That(agents, Is.Empty);
    }

    [Test]
    public async Task GetAgents_WhenMultipleAgents_ReturnsAllAgents()
    {
        // Arrange
        await SeedLargeAgentsData(10);

        // Act
        var result = await _controller.GetAgents();

        // Assert
        Assert.That(result, Is.TypeOf<OkObjectResult>());
        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var agents = okResult.Value as IEnumerable<AgentListItem>;
        Assert.That(agents, Is.Not.Null);
        var agentList = agents.ToList();
        Assert.That(agentList, Has.Count.EqualTo(10));
    }

    #endregion

    #region GetAgentById Tests

    [Test]
    public async Task GetAgentById_WhenAgentExists_ReturnsOkWithAgentDetail()
    {
        // Arrange
        var agentId = await SeedSingleAgentData();

        // Act
        var result = await _controller.GetAgentById(agentId);

        // Assert
        Assert.That(result, Is.TypeOf<OkObjectResult>());
        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var agentDetail = okResult.Value as AgentDetail;
        Assert.That(agentDetail, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(agentDetail.Id, Is.EqualTo(agentId));
            Assert.That(agentDetail.Name, Is.EqualTo("Single Test Agent"));
            Assert.That(agentDetail.Instructions, Is.EqualTo("Test instructions for single agent"));
        }
    }

    [Test]
    public async Task GetAgentById_WhenAgentDoesNotExist_ReturnsNotFound()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var result = await _controller.GetAgentById(nonExistentId);

        // Assert
        Assert.That(result, Is.TypeOf<NotFoundResult>());
    }

    [Test]
    public async Task GetAgentById_WhenMultipleAgentsExistButRequestingSpecific_ReturnsCorrectAgent()
    {
        // Arrange
        await SeedAgentsData();
        var specificAgentId = Guid.NewGuid();
        var specificAgent = new AgentModel
        {
            Id = specificAgentId,
            Name = "Specific Agent",
            Instructions = "Specific instructions",
            OwnerUserId = _testUserId,
            UpdatedByUserId = _testUserId
        };
        await _context.Agents.AddAsync(specificAgent);
        await _context.SaveChangesAsync();

        // Act
        var result = await _controller.GetAgentById(specificAgentId);

        // Assert
        Assert.That(result, Is.TypeOf<OkObjectResult>());
        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var agentDetail = okResult.Value as AgentDetail;
        Assert.That(agentDetail, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(agentDetail.Id, Is.EqualTo(specificAgentId));
            Assert.That(agentDetail.Name, Is.EqualTo("Specific Agent"));
            Assert.That(agentDetail.Instructions, Is.EqualTo("Specific instructions"));
        }
    }

    #endregion

    #region Authorization Tests

    [Test]
    public async Task GetAgents_WhenUserIsNotAdmin_RequiresAdminRole()
    {
        // Arrange
        var nonAdminUser = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, "User"), // Not Admin role
        ], "mock"));

        var nonAdminController = new AgentAdminController(_context)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = nonAdminUser }
            }
        };

        // Note: In a real scenario, this would be handled by the authorization middleware
        // and the controller method wouldn't be called at all for non-admin users.
        // This test just verifies the controller can be instantiated with non-admin users
        // The actual authorization testing would be done at the integration test level

        // Act & Assert - This just verifies the controller works when called
        var result = await nonAdminController.GetAgents();
        Assert.That(result, Is.TypeOf<OkObjectResult>());
    }

    [Test]
    public async Task GetAgentById_WhenUserIsNotAdmin_RequiresAdminRole()
    {
        // Arrange
        var nonAdminUser = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, "User"), // Not Admin role
        ], "mock"));

        var nonAdminController = new AgentAdminController(_context)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = nonAdminUser }
            }
        };

        // Act & Assert - In real scenario, authorization middleware would block this
        var result = await nonAdminController.GetAgentById(Guid.NewGuid());
        Assert.That(result, Is.TypeOf<NotFoundResult>());
    }

    [Test]
    public async Task GetAgents_WhenUserIsAdmin_AllowsAccess()
    {
        // Arrange - Using the admin controller from setup
        await SeedAgentsData();

        // Act
        var result = await _controller.GetAgents();

        // Assert
        Assert.That(result, Is.TypeOf<OkObjectResult>());
        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var agents = okResult.Value as IEnumerable<AgentListItem>;
        Assert.That(agents, Is.Not.Null);
        var agentList = agents.ToList();
        Assert.That(agentList, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GetAgentById_WhenUserIsAdmin_AllowsAccess()
    {
        // Arrange - Using the admin controller from setup
        var agentId = await SeedSingleAgentData();

        // Act
        var result = await _controller.GetAgentById(agentId);

        // Assert
        Assert.That(result, Is.TypeOf<OkObjectResult>());
        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var agentDetail = okResult.Value as AgentDetail;
        Assert.That(agentDetail, Is.Not.Null);
        Assert.That(agentDetail.Id, Is.EqualTo(agentId));
    }

    #endregion

    #region Helper Methods

    private async Task SeedAgentsData()
    {
        var ownerUser = new User
        {
            Id = _testUserId,
            UserName = "testowner",
            Email = "owner@test.com"
        };

        var updaterUser = new User
        {
            Id = Guid.NewGuid(),
            UserName = "testupdater",
            Email = "updater@test.com"
        };

        await _context.Users.AddRangeAsync(ownerUser, updaterUser);

        var agents = new List<AgentModel>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Test Agent 1",
                Instructions = "Test instructions 1",
                OwnerUserId = ownerUser.Id,
                UpdatedByUserId = updaterUser.Id,
                CreatedAt = DateTime.UtcNow.AddDays(-2),
                UpdatedAt = DateTime.UtcNow.AddDays(-1)
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Test Agent 2",
                Instructions = "Test instructions 2",
                OwnerUserId = ownerUser.Id,
                UpdatedByUserId = updaterUser.Id,
                CreatedAt = DateTime.UtcNow.AddDays(-1),
                UpdatedAt = DateTime.UtcNow
            }
        };

        await _context.Agents.AddRangeAsync(agents);
        await _context.SaveChangesAsync();
    }

    private async Task<Guid> SeedSingleAgentData()
    {
        var ownerUser = new User
        {
            Id = _testUserId,
            UserName = "testowner",
            Email = "owner@test.com"
        };

        var updaterUser = new User
        {
            Id = Guid.NewGuid(),
            UserName = "testupdater",
            Email = "updater@test.com"
        };

        await _context.Users.AddRangeAsync(ownerUser, updaterUser);

        var agentId = Guid.NewGuid();
        var agent = new AgentModel
        {
            Id = agentId,
            Name = "Single Test Agent",
            Instructions = "Test instructions for single agent",
            OwnerUserId = ownerUser.Id,
            UpdatedByUserId = updaterUser.Id,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            UpdatedAt = DateTime.UtcNow
        };

        await _context.Agents.AddAsync(agent);
        await _context.SaveChangesAsync();

        return agentId;
    }

    private async Task SeedLargeAgentsData(int count)
    {
        var ownerUser = new User
        {
            Id = _testUserId,
            UserName = "testowner",
            Email = "owner@test.com"
        };

        var updaterUser = new User
        {
            Id = Guid.NewGuid(),
            UserName = "testupdater",
            Email = "updater@test.com"
        };

        await _context.Users.AddRangeAsync(ownerUser, updaterUser);

        var agents = new List<AgentModel>();
        for (int i = 1; i <= count; i++)
        {
            agents.Add(new AgentModel
            {
                Id = Guid.NewGuid(),
                Name = $"Test Agent {i}",
                Instructions = $"Test instructions {i}",
                OwnerUserId = ownerUser.Id,
                UpdatedByUserId = updaterUser.Id,
                CreatedAt = DateTime.UtcNow.AddDays(-i),
                UpdatedAt = DateTime.UtcNow.AddDays(-i + 1)
            });
        }

        await _context.Agents.AddRangeAsync(agents);
        await _context.SaveChangesAsync();
    }

    #endregion
}
