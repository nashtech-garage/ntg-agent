using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Moq;
using NTG.Agent.Common.Dtos.Agents;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Models.Agents;
using NTG.Agent.Orchestrator.Models.Identity;
using NTG.Agent.Orchestrator.Plugins;
using NTG.Agent.Orchestrator.Services.Agents;
using NTG.Agent.Common.Knowledge;
using AgentModel = NTG.Agent.Orchestrator.Models.Agents.Agent;

namespace NTG.Agent.Orchestrator.Tests.Services;

[TestFixture]
public class AgentToolPluginTests
{
    private AgentDbContext _context;
    private AgentAccessService _accessService;

    [SetUp]
    public void Setup()
    {
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new AgentDbContext(options);
        _accessService = new AgentAccessService(_context);
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Test]
    public async Task AskAsync_WhenCallerLacksAccess_ReturnsRefusalAndDoesNotInvokeChild()
    {
        // Arrange: a published child agent owned by someone else, no role grant for our user.
        var childAgentId = Guid.NewGuid();
        _context.Agents.Add(new AgentModel
        {
            Id = childAgentId,
            Name = "HR Docs",
            Instructions = "HR",
            IsPublished = true,
            AgentKind = AgentKind.Inner,
            OwnerUserId = Guid.NewGuid(),
            UpdatedByUserId = Guid.NewGuid()
        });
        await _context.SaveChangesAsync();

        // Strict mock: any call to the child agent fails the test — it must never run.
        var childAgent = new Mock<AIAgent>(MockBehavior.Strict);
        var plugin = new AgentToolPlugin(
            childAgent.Object, _accessService, Mock.Of<IKnowledgeService>(),
            childAgentId, userId: Guid.NewGuid(), isAdmin: false,
            toolName: "hr_docs", toolDescription: "HR docs");

        // Act
        var result = await plugin.AskAsync("What is the secret code?");

        // Assert
        Assert.That(result, Is.EqualTo("You do not have permission to access this resource."));
    }

    [Test]
    public void AsAITool_UsesProvidedNameAndDescription()
    {
        var plugin = new AgentToolPlugin(
            Mock.Of<AIAgent>(), _accessService, Mock.Of<IKnowledgeService>(),
            Guid.NewGuid(), userId: Guid.NewGuid(), isAdmin: false,
            toolName: "hr_docs", toolDescription: "Answers HR policy questions");

        var tool = plugin.AsAITool();

        Assert.That(tool.Name, Is.EqualTo("hr_docs"));
        Assert.That(tool.Description, Is.EqualTo("Answers HR policy questions"));
    }
}
