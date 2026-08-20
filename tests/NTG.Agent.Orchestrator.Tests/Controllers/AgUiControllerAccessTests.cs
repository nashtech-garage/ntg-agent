using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NTG.Agent.Common.Knowledge;
using NTG.Agent.Orchestrator.Controllers;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Exceptions;
using NTG.Agent.Orchestrator.Dtos;
using NTG.Agent.Orchestrator.Models.Identity;
using NTG.Agent.Orchestrator.Services.Agents;
using NTG.Agent.Orchestrator.Services.AnonymousSessions;
using NTG.Agent.Orchestrator.Services.DocumentAnalysis;
using NTG.Agent.Orchestrator.Services.Skills;
using NTG.Agent.Orchestrator.Tests.Services.Agents;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using AgentModel = NTG.Agent.Orchestrator.Models.Agents.Agent;

namespace NTG.Agent.Orchestrator.Tests.Controllers;

/// <summary>
/// How the AG-UI endpoint answers a caller who may not use the agent.
/// </summary>
/// <remarks>
/// <para>
/// An agent is reachable by its owner, an Admin, or a role it has been granted, and a fresh install
/// grants it to nobody — so "signed out" is the single most likely thing to happen to this endpoint,
/// and it used to arrive as <c>RUN_ERROR / INTERNAL_ERROR</c>. That is not a cosmetic complaint: it
/// sent a real debugging session looking for a crash that never happened, because the only honest
/// signal — <c>AgentAccessDeniedException</c> — was swallowed by the catch-all and replaced with
/// "An internal error occurred."
/// </para>
/// <para>
/// So these assert on the emitted SSE stream rather than on a return value, since the stream is the
/// entire contract of this endpoint: a refusal has to be a readable assistant message inside a
/// normally-finished run, and must never be an error frame.
/// </para>
/// </remarks>
[TestFixture]
public class AgUiControllerAccessTests
{
    private AgentDbContext _context = null!;
    private AgUiController _controller = null!;
    private MemoryStream _body = null!;
    private CapturingAgentFactory _agentFactory = null!;
    private Guid _ownerId;
    private Guid _outsiderId;
    private Guid _agentId;

    [SetUp]
    public async Task Setup()
    {
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new AgentDbContext(options);
        _agentFactory = new CapturingAgentFactory();

        _ownerId = Guid.NewGuid();
        _outsiderId = Guid.NewGuid();
        _agentId = Guid.NewGuid();

        _context.Users.Add(new User { Id = _ownerId, UserName = "owner", Email = "owner@test.com" });
        _context.Users.Add(new User { Id = _outsiderId, UserName = "outsider", Email = "outsider@test.com" });
        _context.Agents.Add(new AgentModel
        {
            Id = _agentId,
            Name = "Default Agent",
            OwnerUserId = _ownerId,
            IsPublished = true,
        });

        await _context.SaveChangesAsync();
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
        _body.Dispose();
    }

    // ---------------------------------------------------------------- refusal

    [Test]
    public async Task SignedOutCaller_IsRefusedInProse_NotAsAnInternalError()
    {
        var events = await RunAsync(userId: null);

        Assert.Multiple(() =>
        {
            Assert.That(EventTypes(events), Has.No.Member("RUN_ERROR"));
            Assert.That(Deltas(events), Has.Some.Contains("do not have access to this agent"));
        });
    }

    /// <summary>
    /// A refusal still ends the run cleanly. A client that never sees RUN_FINISHED is left with a
    /// spinner, which is its own kind of "something broke and nobody said what".
    /// </summary>
    [Test]
    public async Task SignedOutCaller_StillGetsAWellFormedRun()
    {
        var events = await RunAsync(userId: null);

        Assert.That(EventTypes(events), Is.EqualTo(new[]
        {
            "RUN_STARTED",
            "STEP_STARTED",
            "TEXT_MESSAGE_START",
            "TEXT_MESSAGE_CONTENT",
            "TEXT_MESSAGE_END",
            "STEP_FINISHED",
            "RUN_FINISHED",
        }));
    }

    /// <summary>
    /// The other half of checking up front. GetOrCreateConversationAsync writes a row before
    /// anything has established the caller may chat, so a signed-out client polling this endpoint
    /// used to leave one behind on every request.
    /// </summary>
    [Test]
    public async Task SignedOutCaller_LeavesNoConversationRow()
    {
        await RunAsync(userId: null);

        Assert.That(await _context.Conversations.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task SignedOutCaller_NeverStartsTheRun()
    {
        await RunAsync(userId: null);

        Assert.That(_agentFactory.Agent.Messages, Is.Empty);
    }

    /// <summary>An agent someone else owns is refused for the same reason a signed-out caller is.</summary>
    [Test]
    public async Task CallerWithoutAGrant_IsRefused()
    {
        var events = await RunAsync(userId: _outsiderId);

        Assert.Multiple(() =>
        {
            Assert.That(Deltas(events), Has.Some.Contains("do not have access to this agent"));
            Assert.That(EventTypes(events), Has.No.Member("RUN_ERROR"));
        });
    }

    /// <summary>
    /// The refusal the up-front check cannot make, and therefore the one that keeps the catch block
    /// honest. <c>AgentAccessService.HasAccessAsync</c> does not look at <c>AgentKind</c>; only
    /// <c>AgentFactory.CreateAgent</c> refuses an inner, tool-only agent, and access can also be
    /// revoked between the two. Either way the caller clears the gate and is refused by the run —
    /// exactly the path that used to end in "An internal error occurred."
    /// </summary>
    /// <remarks>
    /// Driven by making the factory throw rather than by seeding an inner agent, because the fake
    /// factory here is not the one that inspects <c>AgentKind</c>. That inspection is
    /// <c>AgentFactoryTests</c>' subject; this fixture's subject is what the controller does with
    /// the exception once it arrives.
    /// </remarks>
    [Test]
    public async Task RunRefusedByTheAgentFactory_IsStillReportedInProse()
    {
        _agentFactory.RefuseWith = new AgentAccessDeniedException(_agentId);

        var events = await RunAsync(userId: _ownerId);

        Assert.Multiple(() =>
        {
            Assert.That(EventTypes(events), Has.No.Member("RUN_ERROR"));
            Assert.That(Deltas(events), Has.Some.Contains("do not have access to this agent"));
            Assert.That(EventTypes(events), Has.Member("RUN_FINISHED"));
        });
    }

    /// <summary>A genuine fault is still an error frame — the new catch must not swallow those.</summary>
    [Test]
    public async Task RunFailingForAnyOtherReason_IsStillAnInternalError()
    {
        _agentFactory.RefuseWith = new InvalidOperationException("provider exploded");

        var events = await RunAsync(userId: _ownerId);

        Assert.Multiple(() =>
        {
            Assert.That(EventTypes(events), Has.Member("RUN_ERROR"));
            Assert.That(Deltas(events), Has.None.Contains("do not have access to this agent"));
        });
    }

    // ---------------------------------------------------------------- the gate lets the owner through

    /// <summary>
    /// The counterweight: without this, every assertion above would also pass if the endpoint
    /// refused absolutely everyone.
    /// </summary>
    [Test]
    public async Task OwnerIsNotRefused_AndTheRunActuallyHappens()
    {
        var events = await RunAsync(userId: _ownerId);

        Assert.Multiple(() =>
        {
            Assert.That(Deltas(events), Has.None.Contains("do not have access to this agent"));
            Assert.That(_agentFactory.Agent.Messages, Is.Not.Empty, "the agent should have been run");
            Assert.That(EventTypes(events), Has.Member("RUN_FINISHED"));
        });
    }

    [Test]
    public async Task OwnerRun_CreatesTheConversationRow()
    {
        await RunAsync(userId: _ownerId);

        Assert.That(await _context.Conversations.CountAsync(), Is.EqualTo(1));
    }

    // ---------------------------------------------------------------- helpers

    private async Task<List<JsonElement>> RunAsync(Guid? userId)
    {
        _body = new MemoryStream();

        var identity = userId is null
            ? new ClaimsIdentity()
            : new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString())], "mock");

        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        httpContext.Response.Body = _body;

        var agentService = new AgentService(
            _agentFactory,
            _context,
            Mock.Of<IKnowledgeService>(),
            Mock.Of<IAnonymousSessionService>(),
            Mock.Of<IIpAddressService>(),
            Mock.Of<IHttpContextAccessor>(),
            Mock.Of<IDocumentAnalysisService>(),
            new AgentAccessService(_context),
            new RenderableToolCapture(),
            new SkillRegistry(_context, new SkillPackageImporter(), NullLogger<SkillRegistry>.Instance),
            new SkillActivityLog(),
            NullLogger<AgentService>.Instance);

        _controller = new AgUiController(
            agentService,
            _context,
            new AgentAccessService(_context),
            NullLogger<AgUiController>.Instance,
            new MemoryCache(new MemoryCacheOptions()))
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };

        await _controller.RunAgentAsync(_agentId, new AgUiRunRequest
        {
            // A GUID, because an unauthenticated run needs the thread id to double as a session id.
            ThreadId = Guid.NewGuid().ToString(),
            RunId = Guid.NewGuid().ToString(),
            Messages = [new AgUiMessage { Id = "m1", Role = "user", Content = "Hello" }],
        });

        return ParseSse(_body);
    }

    /// <summary>Reads the `data: {...}` frames back off the response body, in order.</summary>
    private static List<JsonElement> ParseSse(MemoryStream body)
    {
        var text = Encoding.UTF8.GetString(body.ToArray());

        return [.. text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("data: ", StringComparison.Ordinal))
            .Select(line => JsonDocument.Parse(line["data: ".Length..]).RootElement.Clone())];
    }

    private static IReadOnlyList<string> EventTypes(IEnumerable<JsonElement> events) =>
        [.. events.Select(e => e.GetProperty("type").GetString() ?? string.Empty)];

    private static IReadOnlyList<string> Deltas(IEnumerable<JsonElement> events) =>
        [.. events
            .Where(e => e.TryGetProperty("delta", out _))
            .Select(e => e.GetProperty("delta").GetString() ?? string.Empty)];
}
