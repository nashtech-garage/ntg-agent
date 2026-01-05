using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using NTG.Agent.Common.Dtos.Agents;
using NTG.Agent.Orchestrator.Controllers;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Services.Agents;
using NTG.Agent.Orchestrator.Services.AnonymousSessions;
using NTG.Agent.Orchestrator.Services.Knowledge;
using System.Security.Claims;

namespace NTG.Agent.Orchestrator.Tests.Controllers;

[TestFixture]
public class AgentsControllerTests
{
    private AgentDbContext _context;
    private AgentsController _controller;
    private Mock<AgentService> _mockAgentService;
    private Guid _testUserId;

    [SetUp]
    public void Setup()
    {
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new AgentDbContext(options);
        _testUserId = Guid.NewGuid();

        // Mock the user principal
        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, _testUserId.ToString()),
        ], "mock"));

        // Mock AgentService with all required dependencies
        _mockAgentService = new Mock<AgentService>(
            Mock.Of<IAgentFactory>(),
            _context,
            Mock.Of<IKnowledgeService>(),
            Mock.Of<IAnonymousSessionService>(),
            Mock.Of<IIpAddressService>(),
            Mock.Of<IHttpContextAccessor>(),
            Mock.Of<ILogger<AgentService>>()
        );

        _controller = new AgentsController(_mockAgentService.Object, _context)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
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
    public async Task GetAgents_WhenPublishedAgentsExist_ReturnsOkWithPublishedAgents()
    {
        // Arrange
        var defaultAgentId = Guid.NewGuid();
        var agent1 = new Models.Agents.Agent
        {
            Id = defaultAgentId,
            Name = "Default Agent",
            Instructions = "Default instructions",
            IsDefault = true,
            IsPublished = true,
            OwnerUserId = _testUserId,
            UpdatedByUserId = _testUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var agent2 = new Models.Agents.Agent
        {
            Id = Guid.NewGuid(),
            Name = "Custom Agent",
            Instructions = "Custom instructions",
            IsDefault = false,
            IsPublished = true,
            OwnerUserId = _testUserId,
            UpdatedByUserId = _testUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var agent3 = new Models.Agents.Agent
        {
            Id = Guid.NewGuid(),
            Name = "Unpublished Agent",
            Instructions = "Unpublished instructions",
            IsDefault = false,
            IsPublished = false,
            OwnerUserId = _testUserId,
            UpdatedByUserId = _testUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _context.Agents.AddRangeAsync(agent1, agent2, agent3);
        await _context.SaveChangesAsync();

        // Act
        var result = await _controller.GetAgents();

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.TypeOf<OkObjectResult>());
        
        var okResult = result as OkObjectResult;
        var agents = okResult!.Value as List<AgentListItemDto>;
        
        Assert.That(agents, Is.Not.Null);
        Assert.That(agents, Has.Count.EqualTo(2), "Only published agents should be returned");
        
        var defaultAgent = agents.FirstOrDefault(a => a.IsDefault);
        Assert.That(defaultAgent, Is.Not.Null, "Default agent should be in the list");
        Assert.That(defaultAgent!.Name, Is.EqualTo("Default Agent"));
        Assert.That(defaultAgent.Id, Is.EqualTo(defaultAgentId));
        
        var customAgent = agents.FirstOrDefault(a => !a.IsDefault);
        Assert.That(customAgent, Is.Not.Null, "Custom agent should be in the list");
        Assert.That(customAgent!.Name, Is.EqualTo("Custom Agent"));
    }

    [Test]
    public async Task GetAgents_WhenNoPublishedAgentsExist_ReturnsOkWithEmptyList()
    {
        // Arrange
        var unpublishedAgent = new Models.Agents.Agent
        {
            Id = Guid.NewGuid(),
            Name = "Unpublished Agent",
            Instructions = "Unpublished instructions",
            IsDefault = false,
            IsPublished = false,
            OwnerUserId = _testUserId,
            UpdatedByUserId = _testUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _context.Agents.AddAsync(unpublishedAgent);
        await _context.SaveChangesAsync();

        // Act
        var result = await _controller.GetAgents();

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.TypeOf<OkObjectResult>());
        
        var okResult = result as OkObjectResult;
        var agents = okResult!.Value as List<AgentListItemDto>;
        
        Assert.That(agents, Is.Not.Null);
        Assert.That(agents, Is.Empty, "No published agents should be returned");
    }

    [Test]
    public async Task GetAgents_WhenNoAgentsExist_ReturnsOkWithEmptyList()
    {
        // Act
        var result = await _controller.GetAgents();

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.TypeOf<OkObjectResult>());
        
        var okResult = result as OkObjectResult;
        var agents = okResult!.Value as List<AgentListItemDto>;
        
        Assert.That(agents, Is.Not.Null);
        Assert.That(agents, Is.Empty, "Empty list should be returned when no agents exist");
    }

    [Test]
    public async Task GetAgents_VerifyAgentListItemDtoProperties_ReturnsCorrectData()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        var agent = new Models.Agents.Agent
        {
            Id = agentId,
            Name = "Test Agent",
            Instructions = "Test instructions",
            IsDefault = true,
            IsPublished = true,
            OwnerUserId = _testUserId,
            UpdatedByUserId = _testUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _context.Agents.AddAsync(agent);
        await _context.SaveChangesAsync();

        // Act
        var result = await _controller.GetAgents();

        // Assert
        var okResult = result as OkObjectResult;
        var agents = okResult!.Value as List<AgentListItemDto>;
        var agentDto = agents!.First();
        
        using (Assert.EnterMultipleScope())
        {
            Assert.That(agentDto.Id, Is.EqualTo(agentId));
            Assert.That(agentDto.Name, Is.EqualTo("Test Agent"));
            Assert.That(agentDto.IsDefault, Is.True);
        }
    }
}
