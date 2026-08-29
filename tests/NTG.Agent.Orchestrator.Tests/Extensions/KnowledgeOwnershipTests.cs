using Microsoft.EntityFrameworkCore;
using NTG.Agent.Common.Dtos.Agents;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Extentions;

namespace NTG.Agent.Orchestrator.Tests.Extensions;

/// <summary>
/// Covers the two rules the whole knowledge-base feature rests on: an agent's knowledge base is its
/// owner's id (and ownership never chains), and only outer agents have one at all.
/// </summary>
[TestFixture]
public class KnowledgeOwnershipTests
{
    private AgentDbContext _context = null!;

    [SetUp]
    public void Setup()
    {
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new AgentDbContext(options);
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    private async Task<Guid> AddAgentAsync(
        AgentKind kind = AgentKind.Outer, Guid? knowledgeOwnerAgentId = null, string name = "Agent")
    {
        var agent = new Orchestrator.Models.Agents.Agent
        {
            Id = Guid.NewGuid(),
            Name = name,
            AgentKind = kind,
            KnowledgeOwnerAgentId = knowledgeOwnerAgentId
        };
        _context.Agents.Add(agent);
        await _context.SaveChangesAsync();
        return agent.Id;
    }

    [Test]
    public async Task GetKnowledgeOwnerIdAsync_WhenAgentOwnsItsKnowledgeBase_ReturnsItself()
    {
        // This is what every agent predating the feature looks like, which is why they keep the
        // containers and workspaces they already have.
        var agentId = await AddAgentAsync();

        Assert.That(await _context.GetKnowledgeOwnerIdAsync(agentId), Is.EqualTo(agentId));
    }

    [Test]
    public async Task GetKnowledgeOwnerIdAsync_WhenAgentIsGuest_ReturnsOwner()
    {
        var ownerId = await AddAgentAsync(name: "Owner");
        var guestId = await AddAgentAsync(knowledgeOwnerAgentId: ownerId, name: "Guest");

        Assert.That(await _context.GetKnowledgeOwnerIdAsync(guestId), Is.EqualTo(ownerId));
    }

    [Test]
    public async Task GetKnowledgeOwnerIdAsync_WhenAgentIsInner_ReturnsNull()
    {
        var innerId = await AddAgentAsync(AgentKind.Inner, name: "Inner");

        Assert.That(await _context.GetKnowledgeOwnerIdAsync(innerId), Is.Null);
    }

    [Test]
    public async Task GetKnowledgeOwnerIdAsync_WhenAgentMissing_ReturnsNull()
    {
        Assert.That(await _context.GetKnowledgeOwnerIdAsync(Guid.NewGuid()), Is.Null);
    }

    [Test]
    public async Task GetKnowledgeOwnerIdsAsync_CollapsesSharedKnowledgeBasesAndSkipsInnerAgents()
    {
        // Three outer agents across two knowledge bases, plus an inner agent that has none:
        // two containers, not four. This is the saving the whole feature exists for.
        var ownerA = await AddAgentAsync(name: "A");
        await AddAgentAsync(knowledgeOwnerAgentId: ownerA, name: "A-guest");
        var ownerB = await AddAgentAsync(name: "B");
        await AddAgentAsync(AgentKind.Inner, name: "Inner");

        var owners = await _context.GetKnowledgeOwnerIdsAsync();

        Assert.That(owners, Is.EquivalentTo(new[] { ownerA, ownerB }));
    }

    [Test]
    public async Task GetKnowledgeGuestsAsync_ReturnsGuestsOnly()
    {
        var ownerId = await AddAgentAsync(name: "Owner");
        var guestId = await AddAgentAsync(knowledgeOwnerAgentId: ownerId, name: "Guest");
        await AddAgentAsync(name: "Unrelated");

        var guests = await _context.GetKnowledgeGuestsAsync(ownerId);

        Assert.That(guests.Select(g => g.Id), Is.EquivalentTo(new[] { guestId }));
    }

    [Test]
    public async Task ValidateJoinTargetAsync_WhenTargetOwnsItsKnowledgeBase_Allows()
    {
        var ownerId = await AddAgentAsync(name: "Owner");
        var joinerId = await AddAgentAsync(name: "Joiner");

        Assert.That(await _context.ValidateJoinTargetAsync(ownerId, joinerId), Is.Null);
    }

    [Test]
    public async Task ValidateJoinTargetAsync_WhenTargetIsSelf_Rejects()
    {
        var agentId = await AddAgentAsync();

        Assert.That(await _context.ValidateJoinTargetAsync(agentId, agentId), Is.Not.Null);
    }

    [Test]
    public async Task ValidateJoinTargetAsync_WhenTargetMissing_Rejects()
    {
        Assert.That(await _context.ValidateJoinTargetAsync(Guid.NewGuid(), null), Is.Not.Null);
    }

    [Test]
    public async Task ValidateJoinTargetAsync_WhenTargetIsInner_Rejects()
    {
        var innerId = await AddAgentAsync(AgentKind.Inner, name: "Inner");

        Assert.That(await _context.ValidateJoinTargetAsync(innerId, null), Is.Not.Null);
    }

    [Test]
    public async Task ValidateJoinTargetAsync_WhenTargetIsItselfAGuest_Rejects()
    {
        // Ownership must not chain: joining a guest would leave the real owner ambiguous, and the
        // container/workspace naming has no way to express a second hop.
        var ownerId = await AddAgentAsync(name: "Owner");
        var guestId = await AddAgentAsync(knowledgeOwnerAgentId: ownerId, name: "Guest");

        Assert.That(await _context.ValidateJoinTargetAsync(guestId, null), Is.Not.Null);
    }
}
