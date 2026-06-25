using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Moq;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Models.Agents;
using NTG.Agent.Orchestrator.Models.Identity;
using NTG.Agent.Orchestrator.Plugins;
using NTG.Agent.Orchestrator.Services.Agents;
using NTG.Agent.Orchestrator.Services.Knowledge;
using AgentModel = NTG.Agent.Orchestrator.Models.Agents.Agent;

namespace NTG.Agent.Orchestrator.Tests.Services;

[TestFixture]
public class AgentToolPluginTests
{
    private const string RefusalMessage = "You do not have permission to access this resource.";
    private const string ToolName = "doc-tool";
    private const string ToolDescription = "Search the special documents.";

    private AgentDbContext _context = null!;
    private AgentAccessService _accessService = null!;
    private Mock<IAgentFactory> _factoryMock = null!;
    private Mock<IKnowledgeService> _knowledgeMock = null!;
    private Guid _ownerId;
    private Guid _otherUserId;
    private Guid _adminUserId;
    private Guid _roleId;
    private Guid _childAgentId;

    [SetUp]
    public void Setup()
    {
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new AgentDbContext(options);
        _accessService = new AgentAccessService(_context);
        _factoryMock = new Mock<IAgentFactory>();
        _knowledgeMock = new Mock<IKnowledgeService>();

        _ownerId = Guid.NewGuid();
        _otherUserId = Guid.NewGuid();
        _adminUserId = Guid.NewGuid();
        _roleId = Guid.NewGuid();
        _childAgentId = Guid.NewGuid();

        _context.Roles.Add(new Role { Id = _roleId, Name = "Engineering" });
        _context.Users.AddRange(
            new User { Id = _ownerId, UserName = "owner", Email = "owner@test.com" },
            new User { Id = _otherUserId, UserName = "other", Email = "other@test.com" },
            new User { Id = _adminUserId, UserName = "admin", Email = "admin@test.com" }
        );
        // The child "document" agent, published and owned by _ownerId.
        _context.Agents.Add(new AgentModel
        {
            Id = _childAgentId,
            Name = "Document Agent",
            Instructions = "Answer from special docs",
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

    private AgentToolPlugin CreatePlugin(Guid? userId, bool isAdmin) =>
        new(_factoryMock.Object, _accessService, _knowledgeMock.Object, _childAgentId, userId, isAdmin, ToolName, ToolDescription);

    private void GrantRoleTo(Guid userId)
    {
        _context.UserRoles.Add(new UserRole { UserId = userId, RoleId = _roleId });
        _context.AgentRoles.Add(new AgentRole { Id = Guid.NewGuid(), AgentId = _childAgentId, RoleId = _roleId });
        _context.SaveChanges();
    }

    private sealed class FactoryReachedException : Exception { }

    // --- Denied path: the critical security gate ---

    [Test]
    public async Task AskAsync_UserWithoutAccess_ReturnsRefusalAndNeverCreatesChildAgent()
    {
        var plugin = CreatePlugin(_otherUserId, isAdmin: false);

        var result = await plugin.AskAsync("What is in the secret doc?");

        Assert.That(result, Is.EqualTo(RefusalMessage));
        _factoryMock.Verify(
            f => f.CreateAgent(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<bool>()),
            Times.Never,
            "Child agent must never be built when the user lacks access.");
    }

    [Test]
    public async Task AskAsync_NullUser_ReturnsRefusalAndNeverCreatesChildAgent()
    {
        var plugin = CreatePlugin(userId: null, isAdmin: false);

        var result = await plugin.AskAsync("anything");

        Assert.That(result, Is.EqualTo(RefusalMessage));
        _factoryMock.Verify(
            f => f.CreateAgent(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<bool>()),
            Times.Never);
    }

    [Test]
    public async Task AskAsync_UnpublishedChildAgent_ReturnsRefusalEvenForOwner()
    {
        var unpublishedId = Guid.NewGuid();
        _context.Agents.Add(new AgentModel
        {
            Id = unpublishedId,
            Name = "Draft Agent",
            Instructions = "",
            IsPublished = false,
            OwnerUserId = _ownerId,
            UpdatedByUserId = _ownerId
        });
        _context.SaveChanges();
        var plugin = new AgentToolPlugin(
            _factoryMock.Object, _accessService, _knowledgeMock.Object, unpublishedId, _ownerId, isAdmin: false, ToolName, ToolDescription);

        var result = await plugin.AskAsync("anything");

        Assert.That(result, Is.EqualTo(RefusalMessage));
        _factoryMock.Verify(
            f => f.CreateAgent(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<bool>()),
            Times.Never);
    }

    // --- Authorized path: gate opens and the child agent is built ---
    // The child agent's RunAsync is thin pass-through glue over the Agents.AI runtime and is
    // exercised by the end-to-end verification step; here we assert the gate lets the call
    // through and the factory is invoked with the exact (childAgentId, userId, isAdmin) tuple.

    [Test]
    public void AskAsync_OwnerUser_InvokesFactoryWithExpectedArguments()
    {
        _factoryMock
            .Setup(f => f.CreateAgent(_childAgentId, _ownerId, false))
            .ThrowsAsync(new FactoryReachedException());
        var plugin = CreatePlugin(_ownerId, isAdmin: false);

        Assert.ThrowsAsync<FactoryReachedException>(() => plugin.AskAsync("question"));
        _factoryMock.Verify(f => f.CreateAgent(_childAgentId, _ownerId, false), Times.Once);
    }

    [Test]
    public void AskAsync_AdminUser_InvokesFactoryEvenWithoutRoleGrant()
    {
        _factoryMock
            .Setup(f => f.CreateAgent(_childAgentId, _adminUserId, true))
            .ThrowsAsync(new FactoryReachedException());
        var plugin = CreatePlugin(_adminUserId, isAdmin: true);

        Assert.ThrowsAsync<FactoryReachedException>(() => plugin.AskAsync("question"));
        _factoryMock.Verify(f => f.CreateAgent(_childAgentId, _adminUserId, true), Times.Once);
    }

    [Test]
    public void AskAsync_UserWithRoleGrant_InvokesFactory()
    {
        GrantRoleTo(_otherUserId);
        _factoryMock
            .Setup(f => f.CreateAgent(_childAgentId, _otherUserId, false))
            .ThrowsAsync(new FactoryReachedException());
        var plugin = CreatePlugin(_otherUserId, isAdmin: false);

        Assert.ThrowsAsync<FactoryReachedException>(() => plugin.AskAsync("question"));
        _factoryMock.Verify(f => f.CreateAgent(_childAgentId, _otherUserId, false), Times.Once);
    }

    // --- Tool metadata ---

    [Test]
    public void AsAITool_UsesConfiguredNameAndDescription()
    {
        var plugin = CreatePlugin(_ownerId, isAdmin: false);

        AITool tool = plugin.AsAITool();

        Assert.Multiple(() =>
        {
            Assert.That(tool.Name, Is.EqualTo(ToolName));
            Assert.That(tool.Description, Is.EqualTo(ToolDescription));
        });
    }

    // --- Constructor guards ---

    [Test]
    public void Constructor_NullFactory_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new AgentToolPlugin(
            null!, _accessService, _knowledgeMock.Object, _childAgentId, _ownerId, false, ToolName, ToolDescription));
    }

    [Test]
    public void Constructor_NullAccessService_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new AgentToolPlugin(
            _factoryMock.Object, null!, _knowledgeMock.Object, _childAgentId, _ownerId, false, ToolName, ToolDescription));
    }

    [Test]
    public void Constructor_NullKnowledgeService_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new AgentToolPlugin(
            _factoryMock.Object, _accessService, null!, _childAgentId, _ownerId, false, ToolName, ToolDescription));
    }

    [Test]
    public void Constructor_NullToolName_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new AgentToolPlugin(
            _factoryMock.Object, _accessService, _knowledgeMock.Object, _childAgentId, _ownerId, false, null!, ToolDescription));
    }

    [Test]
    public void Constructor_NullToolDescription_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new AgentToolPlugin(
            _factoryMock.Object, _accessService, _knowledgeMock.Object, _childAgentId, _ownerId, false, ToolName, null!));
    }
}
