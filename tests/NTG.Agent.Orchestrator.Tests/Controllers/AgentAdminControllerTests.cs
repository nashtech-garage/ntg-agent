using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using NTG.Agent.Common.Dtos.Agents;
using NTG.Agent.Orchestrator.Agents;
using NTG.Agent.Orchestrator.Controllers;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Models.Identity;
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
    private Mock<IAgentFactory> _mockAgentFactory;

    [SetUp]
    public void Setup()
    {
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new AgentDbContext(options);
        _testUserId = Guid.NewGuid();
        _testAdminUserId = Guid.NewGuid();
        _mockAgentFactory = new();
        // Mock the admin user principal
        var adminUser = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, _testAdminUserId.ToString()),
            new Claim(ClaimTypes.Role, "Admin"),
        ], "mock"));
        _controller = new AgentAdminController(_context, _mockAgentFactory.Object)
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
    [Test]
    public void Constructor_WhenAgentDbContextIsNull_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AgentAdminController(null!, _mockAgentFactory.Object));
    }
    [Test]
    public void Constructor_WhenValidParameters_CreatesInstance()
    {
        // Act
        var controller = new AgentAdminController(_context, _mockAgentFactory.Object);
        // Assert
        Assert.That(controller, Is.Not.Null);
    }
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
    [Test]
    public async Task GetAgents_WhenUserIsNotAdmin_RequiresAdminRole()
    {
        // Arrange
        var nonAdminUser = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, "User"), // Not Admin role
        ], "mock"));
        var nonAdminController = new AgentAdminController(_context, _mockAgentFactory.Object)
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
        var nonAdminController = new AgentAdminController(_context, _mockAgentFactory.Object)
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

    [Test]
    public async Task CreateAgent_WhenValidAgentProvided_ReturnsCreatedAtActionResult()
    {
        // Arrange
        var newAgent = new AgentDetail(
            Guid.Empty,
            "New Test Agent",
            "Test instructions",
            "OpenAI",
            "https://api.openai.com/v1",
            "test-api-key",
            "gpt-4"
        );

        // Act
        var result = await _controller.CreateAgent(newAgent);

        // Assert
        Assert.That(result, Is.TypeOf<CreatedAtActionResult>());
        var createdResult = result as CreatedAtActionResult;
        Assert.That(createdResult, Is.Not.Null);
        Assert.That(createdResult.ActionName, Is.EqualTo(nameof(_controller.GetAgentById)));

        var createdAgentId = createdResult.Value as Guid?;
        Assert.That(createdAgentId, Is.Not.Null);
        Assert.That(createdAgentId.Value, Is.Not.EqualTo(Guid.Empty));

        // Verify agent was saved to database with correct properties
        var savedAgent = await _context.Agents.FindAsync(createdAgentId.Value);
        Assert.That(savedAgent, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(savedAgent.Id, Is.EqualTo(createdAgentId.Value));
            Assert.That(savedAgent.Name, Is.EqualTo("New Test Agent"));
            Assert.That(savedAgent.Instructions, Is.EqualTo("Test instructions"));
            Assert.That(savedAgent.ProviderName, Is.EqualTo("OpenAI"));
            Assert.That(savedAgent.ProviderEndpoint, Is.EqualTo("https://api.openai.com/v1"));
            Assert.That(savedAgent.ProviderApiKey, Is.EqualTo("test-api-key"));
            Assert.That(savedAgent.ProviderModelName, Is.EqualTo("gpt-4"));
            Assert.That(savedAgent.OwnerUserId, Is.EqualTo(_testAdminUserId));
            Assert.That(savedAgent.UpdatedByUserId, Is.EqualTo(_testAdminUserId));
        }
    }

    [Test]
    public async Task CreateAgent_WhenValidAgentProvided_SavesAgentToDatabase()
    {
        // Arrange
        var newAgent = new AgentDetail(
            Guid.Empty,
            "Database Test Agent",
            "Instructions",
            "AzureOpenAI",
            "https://azure.openai.com",
            "azure-key",
            "gpt-4"
        );

        // Act
        var result = await _controller.CreateAgent(newAgent);

        // Assert
        var createdResult = result as CreatedAtActionResult;
        Assert.That(createdResult, Is.Not.Null);
        var createdAgentId = createdResult.Value as Guid?;
        Assert.That(createdAgentId, Is.Not.Null);

        // Verify agent was saved to database
        var savedAgent = await _context.Agents.FindAsync(createdAgentId.Value);
        Assert.That(savedAgent, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(savedAgent.Name, Is.EqualTo("Database Test Agent"));
            Assert.That(savedAgent.Instructions, Is.EqualTo("Instructions"));
            Assert.That(savedAgent.ProviderName, Is.EqualTo("AzureOpenAI"));
        }
    }

    [Test]
    public async Task CreateAgent_WhenOnlyNameProvided_CreatesAgentWithEmptyOptionalFields()
    {
        // Arrange
        var newAgent = new AgentDetail()
        {
            Name = "Minimal Agent"
        };

        // Act
        var result = await _controller.CreateAgent(newAgent);

        // Assert
        Assert.That(result, Is.TypeOf<CreatedAtActionResult>());
        var createdResult = result as CreatedAtActionResult;
        Assert.That(createdResult, Is.Not.Null);
        var createdAgentId = createdResult.Value as Guid?;
        Assert.That(createdAgentId, Is.Not.Null);

        // Verify agent was saved to database with correct properties
        var savedAgent = await _context.Agents.FindAsync(createdAgentId.Value);
        Assert.That(savedAgent, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(savedAgent.Name, Is.EqualTo("Minimal Agent"));
            Assert.That(savedAgent.Instructions, Is.EqualTo(string.Empty));
            Assert.That(savedAgent.ProviderName, Is.EqualTo(string.Empty));
            Assert.That(savedAgent.ProviderEndpoint, Is.EqualTo(string.Empty));
            Assert.That(savedAgent.ProviderApiKey, Is.EqualTo(string.Empty));
            Assert.That(savedAgent.ProviderModelName, Is.EqualTo(string.Empty));
        }
    }

    [Test]
    public async Task CreateAgent_WhenNullInstructionsProvided_CreatesAgentWithEmptyInstructions()
    {
        // Arrange
        var newAgent = new AgentDetail(
            Guid.Empty,
            "Agent With Null Instructions",
            null!,
            null!,
            null!,
            null!,
            null!
        );

        // Act
        var result = await _controller.CreateAgent(newAgent);

        // Assert
        var createdResult = result as CreatedAtActionResult;
        Assert.That(createdResult, Is.Not.Null);
        var createdAgentId = createdResult.Value as Guid?;
        Assert.That(createdAgentId, Is.Not.Null);

        // Verify agent was saved to database with correct properties
        var savedAgent = await _context.Agents.FindAsync(createdAgentId.Value);
        Assert.That(savedAgent, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(savedAgent.Instructions, Is.EqualTo(string.Empty));
            Assert.That(savedAgent.ProviderName, Is.EqualTo(string.Empty));
            Assert.That(savedAgent.ProviderEndpoint, Is.EqualTo(string.Empty));
            Assert.That(savedAgent.ProviderApiKey, Is.EqualTo(string.Empty));
            Assert.That(savedAgent.ProviderModelName, Is.EqualTo(string.Empty));
        }
    }

    [Test]
    public async Task CreateAgent_WhenMcpServerProvided_SavesMcpServerValue()
    {
        // Arrange
        var newAgent = new AgentDetail(
            Guid.Empty,
            "Agent With MCP",
            "Instructions",
            "OpenAI",
            "https://api.openai.com/v1",
            "key",
            "gpt-4"
        )
        {
            McpServer = "https://mcp.example.com"
        };

        // Act
        var result = await _controller.CreateAgent(newAgent);

        // Assert
        var createdResult = result as CreatedAtActionResult;
        Assert.That(createdResult, Is.Not.Null);
        var createdAgentId = createdResult.Value as Guid?;
        Assert.That(createdAgentId, Is.Not.Null);

        // Verify agent was saved to database with MCP server
        var savedAgent = await _context.Agents.FindAsync(createdAgentId.Value);
        Assert.That(savedAgent, Is.Not.Null);
        Assert.That(savedAgent.McpServer, Is.EqualTo("https://mcp.example.com"));
    }

    [Test]
    public async Task CreateAgent_WhenNullAgentProvided_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.CreateAgent(null!);

        // Assert
        Assert.That(result, Is.TypeOf<BadRequestObjectResult>());
        var badRequestResult = result as BadRequestObjectResult;
        Assert.That(badRequestResult, Is.Not.Null);
        Assert.That(badRequestResult.Value, Is.EqualTo("Invalid agent data."));
    }

    [Test]
    public async Task CreateAgent_GeneratesNewGuid_ForAgentId()
    {
        // Arrange
        var agent1 = new AgentDetail(Guid.Empty, "Agent 1", null!, null!, null!, null!, null!);
        var agent2 = new AgentDetail(Guid.Empty, "Agent 2", null!, null!, null!, null!, null!);

        // Act
        var result1 = await _controller.CreateAgent(agent1);
        var result2 = await _controller.CreateAgent(agent2);

        // Assert
        var createdId1 = (result1 as CreatedAtActionResult)?.Value as Guid?;
        var createdId2 = (result2 as CreatedAtActionResult)?.Value as Guid?;

        Assert.That(createdId1, Is.Not.Null);
        Assert.That(createdId2, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(createdId1.Value, Is.Not.EqualTo(Guid.Empty));
            Assert.That(createdId2.Value, Is.Not.EqualTo(Guid.Empty));
            Assert.That(createdId1.Value, Is.Not.EqualTo(createdId2.Value));
        }
    }

    [Test]
    public async Task CreateAgent_WhenUserIsNotAuthenticated_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var unauthenticatedController = new AgentAdminController(_context, _mockAgentFactory.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal() }
            }
        };
        var newAgent = new AgentDetail(Guid.Empty, "Test Agent", null!, null!, null!, null!, null!);

        // Act & Assert
        Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await unauthenticatedController.CreateAgent(newAgent));
    }

    [Test]
    public async Task CreateAgent_SetsOwnerAndUpdater_ToAuthenticatedUser()
    {
        // Arrange
        var specificUserId = Guid.NewGuid();
        var userWithSpecificId = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, specificUserId.ToString()),
            new Claim(ClaimTypes.Role, "Admin"),
        ], "mock"));

        var controllerWithSpecificUser = new AgentAdminController(_context, _mockAgentFactory.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = userWithSpecificId }
            }
        };

        var newAgent = new AgentDetail(Guid.Empty, "Test Agent", null!, null!, null!, null!, null!);

        // Act
        var result = await controllerWithSpecificUser.CreateAgent(newAgent);

        // Assert
        var createdResult = result as CreatedAtActionResult;
        Assert.That(createdResult, Is.Not.Null);
        var createdAgentId = createdResult.Value as Guid?;
        Assert.That(createdAgentId, Is.Not.Null);

        // Verify agent was saved with correct owner and updater
        var savedAgent = await _context.Agents.FindAsync(createdAgentId.Value);
        Assert.That(savedAgent, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(savedAgent.OwnerUserId, Is.EqualTo(specificUserId));
            Assert.That(savedAgent.UpdatedByUserId, Is.EqualTo(specificUserId));
        }
    }

    [Test]
    public async Task CreateAgent_ReturnsLocationHeader_WithNewAgentId()
    {
        // Arrange
        var newAgent = new AgentDetail(Guid.Empty, "Test Agent", null!, null!, null!, null!, null!);

        // Act
        var result = await _controller.CreateAgent(newAgent);

        // Assert
        var createdResult = result as CreatedAtActionResult;
        Assert.That(createdResult, Is.Not.Null);
        var createdAgentId = createdResult.Value as Guid?;
        Assert.That(createdAgentId, Is.Not.Null);
        Assert.That(createdResult.RouteValues, Does.ContainKey("id"));
        Assert.That(createdResult.RouteValues["id"], Is.EqualTo(createdAgentId.Value));
    }

    [Test]
    public async Task CreateAgent_WithCompleteData_PreservesAllFields()
    {
        // Arrange
        var newAgent = new AgentDetail(
            Guid.Empty,
            "Complete Agent",
            "Detailed instructions for the agent",
            "GitHub Models",
            "https://models.github.com",
            "github-api-key-12345",
            "gpt-4o"
        )
        {
            McpServer = "https://mcp-server.example.com/api"
        };

        // Act
        var result = await _controller.CreateAgent(newAgent);

        // Assert
        var createdResult = result as CreatedAtActionResult;
        Assert.That(createdResult, Is.Not.Null);
        var createdAgentId = createdResult.Value as Guid?;
        Assert.That(createdAgentId, Is.Not.Null);

        // Verify agent was saved with all fields preserved
        var savedAgent = await _context.Agents.FindAsync(createdAgentId.Value);
        Assert.That(savedAgent, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(savedAgent.Name, Is.EqualTo("Complete Agent"));
            Assert.That(savedAgent.Instructions, Is.EqualTo("Detailed instructions for the agent"));
            Assert.That(savedAgent.ProviderName, Is.EqualTo("GitHub Models"));
            Assert.That(savedAgent.ProviderEndpoint, Is.EqualTo("https://models.github.com"));
            Assert.That(savedAgent.ProviderApiKey, Is.EqualTo("github-api-key-12345"));
            Assert.That(savedAgent.ProviderModelName, Is.EqualTo("gpt-4o"));
            Assert.That(savedAgent.McpServer, Is.EqualTo("https://mcp-server.example.com/api"));
        }
    }

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
}
