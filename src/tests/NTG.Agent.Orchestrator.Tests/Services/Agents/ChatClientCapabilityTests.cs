using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NTG.Agent.Common.Dtos.Chats;
using NTG.Agent.Common.Knowledge;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Dtos;
using NTG.Agent.Orchestrator.Models.Chat;
using NTG.Agent.Orchestrator.Models.Identity;
using NTG.Agent.Orchestrator.Models.Skills;
using NTG.Agent.Orchestrator.Services.Agents;
using NTG.Agent.Orchestrator.Services.AnonymousSessions;
using NTG.Agent.Orchestrator.Services.DocumentAnalysis;
using NTG.Agent.Orchestrator.Services.Skills;
using NTG.Agent.Orchestrator.Tests.Services.Skills;
using System.Text.Json;
using AgentModel = NTG.Agent.Orchestrator.Models.Agents.Agent;
// Microsoft.Agents.AI exports an AgentSkill of its own, unrelated to ours.
using AgentSkill = NTG.Agent.Orchestrator.Models.Skills.AgentSkill;

namespace NTG.Agent.Orchestrator.Tests.Services.Agents;

/// <summary>
/// The line between the two chat clients: which one gets A2UI surfaces, frontend tools and the
/// protocol chunks that carry them.
/// </summary>
/// <remarks>
/// <para>
/// The Blazor web client renders assistant markdown and nothing else. Everything the A2UI/AG-UI
/// integration produces — a rendered surface, a frontend tool call, a tool-render card — either has
/// no renderer there or, worse, lands in the assistant's bubble as raw protocol JSON. So the
/// orchestrator decides per run, from the endpoint the request arrived on, and these tests hold
/// that line at the only place it is enforced: the tool list and the leading system messages the
/// model is handed, and the chunk types the stream emits.
/// </para>
/// <para>
/// Asserted against a captured <see cref="ChatOptions"/> rather than a mocked collaborator, because
/// what matters is not that a method was called — it is what the model ends up being told it can
/// do. A gate that attaches the tool but hides it later would pass the former and fail the latter.
/// </para>
/// </remarks>
[TestFixture]
public class ChatClientCapabilityTests
{
    private const string PanelSurface = """
        {
          "surfaceId": "srf-panel",
          "components": [
            { "id": "root", "component": "Column", "children": ["title"] },
            { "id": "title", "component": "Text", "text": "Panel" }
          ],
          "data": {}
        }
        """;

    /// <summary>A frontend tool list shaped the way <c>AgUiController.BuildFrontendToolsJson</c> emits one.</summary>
    private const string FrontendTools = """
        [
          { "name": "render_a2ui", "description": "Render a surface", "parameters": { "type": "object", "properties": {} } },
          { "name": "change_background", "description": "Recolour the page", "parameters": { "type": "object", "properties": {} } }
        ]
        """;

    private AgentDbContext _context = null!;
    private SkillRegistry _registry = null!;
    private SkillActivityLog _activityLog = null!;
    private RenderableToolCapture _capture = null!;
    private CapturingAgentFactory _agentFactory = null!;
    private AgentService _service = null!;
    private Guid _userId;
    private Guid _agentId;
    private Guid _conversationId;

    [SetUp]
    public async Task Setup()
    {
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new AgentDbContext(options);
        _registry = new SkillRegistry(_context, new SkillPackageImporter(), NullLogger<SkillRegistry>.Instance);
        _activityLog = new SkillActivityLog();
        _capture = new RenderableToolCapture();
        _agentFactory = new CapturingAgentFactory();

        _userId = Guid.NewGuid();
        _agentId = Guid.NewGuid();
        _conversationId = Guid.NewGuid();

        _context.Users.Add(new User { Id = _userId, UserName = "admin", Email = "admin@test.com" });
        _context.Agents.Add(new AgentModel { Id = _agentId, Name = "Agent", OwnerUserId = _userId });

        // Named up front: a conversation still called "New Conversation" would send the service off
        // to generate a name, which is a second agent run this fixture has no interest in.
        _context.Conversations.Add(new Conversation
        {
            Id = _conversationId,
            Name = "Capability test",
            UserId = _userId,
        });

        await _context.SaveChangesAsync();

        await BindSkillAsync();

        _service = new AgentService(
            _agentFactory,
            _context,
            Mock.Of<IKnowledgeService>(),
            Mock.Of<IAnonymousSessionService>(),
            Mock.Of<IIpAddressService>(),
            Mock.Of<Microsoft.AspNetCore.Http.IHttpContextAccessor>(),
            Mock.Of<IDocumentAnalysisService>(),
            _capture,
            _registry,
            _activityLog,
            NullLogger<AgentService>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    // ---------------------------------------------------------------- skills

    [Test]
    public async Task TextOnlyClient_IsOfferedNeitherSkillTool()
    {
        await RunAsync(ChatClientCapabilities.TextOnly);

        Assert.That(ToolNames(), Has.No.Member(SkillPrompt.LoadToolName).And.No.Member(SkillPrompt.RenderToolName));
    }

    [Test]
    public async Task GenerativeUiClient_IsOfferedBothSkillTools()
    {
        await RunAsync(ChatClientCapabilities.GenerativeUi);

        Assert.That(ToolNames(), Has.Member(SkillPrompt.LoadToolName).And.Member(SkillPrompt.RenderToolName));
    }

    /// <summary>
    /// The catalog is the only thing that tells the model a skill exists. Withholding the tools but
    /// leaving the catalog in would advertise a capability the run cannot deliver.
    /// </summary>
    [Test]
    public async Task TextOnlyClient_IsNotGivenTheSkillCatalog()
    {
        await RunAsync(ChatClientCapabilities.TextOnly);

        Assert.That(SystemMessages(), Has.None.Contains("# Available skills"));
    }

    [Test]
    public async Task GenerativeUiClient_IsGivenTheSkillCatalog()
    {
        await RunAsync(ChatClientCapabilities.GenerativeUi);

        Assert.That(SystemMessages(), Has.Some.Contains("# Available skills"));
    }

    /// <summary>
    /// The whole point of the gate: a text-only run must look exactly like a run on an agent with no
    /// skills bound, not merely like one where the model is discouraged from using them.
    /// </summary>
    [Test]
    public async Task TextOnlyClient_WithSkillsBound_RunsAsThoughNoneWere()
    {
        await RunAsync(ChatClientCapabilities.TextOnly);
        var withSkills = (ToolNames(), SystemMessages());

        _context.AgentSkills.RemoveRange(_context.AgentSkills);
        await _context.SaveChangesAsync();
        _agentFactory.Agent.Reset();

        await RunAsync(ChatClientCapabilities.TextOnly);

        Assert.That(withSkills, Is.EqualTo((ToolNames(), SystemMessages())));
    }

    // ---------------------------------------------------------------- frontend tools & A2UI guide

    /// <summary>
    /// <c>PromptRequestForm</c> is <c>[FromForm]</c>-bound on the text-only endpoint, so
    /// <c>FrontendToolsJson</c> is caller-supplied there. Declaring <c>render_a2ui</c> must not
    /// conjure the tool or the render guide into a run whose client cannot draw a surface.
    /// </summary>
    [Test]
    public async Task TextOnlyClient_DeclaringFrontendTools_GetsNeitherTheToolsNorTheRenderGuide()
    {
        await RunAsync(ChatClientCapabilities.TextOnly, frontendToolsJson: FrontendTools);

        Assert.Multiple(() =>
        {
            Assert.That(ToolNames(), Has.No.Member(A2uiPrompt.RenderToolName).And.No.Member("change_background"));
            Assert.That(SystemMessages(), Has.None.Contains("A2UI v0.9"));
        });
    }

    [Test]
    public async Task GenerativeUiClient_DeclaringFrontendTools_GetsBothTheToolsAndTheRenderGuide()
    {
        await RunAsync(ChatClientCapabilities.GenerativeUi, frontendToolsJson: FrontendTools);

        Assert.Multiple(() =>
        {
            Assert.That(ToolNames(), Has.Member(A2uiPrompt.RenderToolName).And.Member("change_background"));
            Assert.That(SystemMessages(), Has.Some.Contains("A2UI v0.9"));
        });
    }

    // ---------------------------------------------------------------- renderable tool chunks

    [Test]
    public async Task TextOnlyClient_EmitsNoToolCallOrToolResultChunk()
    {
        _agentFactory.Agent.OnRun = CaptureAToolCall;

        var chunks = await RunAsync(ChatClientCapabilities.TextOnly);

        Assert.That(
            chunks.Select(c => c.ContentType),
            Has.No.Member(PromptContentType.ToolCall).And.No.Member(PromptContentType.ToolResult));
    }

    [Test]
    public async Task GenerativeUiClient_EmitsTheToolCallAndItsResult()
    {
        _agentFactory.Agent.OnRun = CaptureAToolCall;

        var chunks = await RunAsync(ChatClientCapabilities.GenerativeUi);

        Assert.That(
            chunks.Select(c => c.ContentType),
            Has.Member(PromptContentType.ToolCall).And.Member(PromptContentType.ToolResult));
    }

    /// <summary>
    /// Withheld, not merely unread. The capture is request-scoped and shared with inner agents, so a
    /// call left sitting in it is not inert — it is the next thing whatever drains next will hand to
    /// the browser, against an unrelated part of the stream.
    /// </summary>
    /// <remarks>
    /// This pins the buffer being empty, not the discard being eager. Eagerness is why the withhold
    /// path is a plain method rather than an iterator that yields nothing, and it holds only because
    /// both call sites enumerate what they are given — which no test can see from out here.
    /// </remarks>
    [Test]
    public async Task TextOnlyClient_StillEmptiesTheCaptureBuffer()
    {
        _agentFactory.Agent.OnRun = CaptureAToolCall;

        await RunAsync(ChatClientCapabilities.TextOnly);

        Assert.That(_capture.DrainPending(), Is.Empty);
    }

    /// <summary>
    /// The Tool-role row SaveMessages writes is built from the very chunks withheld above, so
    /// withholding them also stops the row being written. Asserted rather than left implicit
    /// because it is a storage-shape difference between the two clients, not just a display one.
    /// </summary>
    [Test]
    public async Task TextOnlyClient_PersistsNoToolRenderRow()
    {
        _agentFactory.Agent.OnRun = CaptureAToolCall;

        await RunAsync(ChatClientCapabilities.TextOnly);

        Assert.That(await MessagesInRoleAsync(ChatRole.Tool), Is.Empty);
    }

    [Test]
    public async Task GenerativeUiClient_PersistsTheToolRenderRow()
    {
        _agentFactory.Agent.OnRun = CaptureAToolCall;

        await RunAsync(ChatClientCapabilities.GenerativeUi);

        Assert.That(await MessagesInRoleAsync(ChatRole.Tool), Has.Count.EqualTo(1));
    }

    // ---------------------------------------------------------------- skill narration

    /// <summary>
    /// Nothing narrates on a text-only run today, because the skill tools are the log's only two
    /// writers and they are not attached. These cover the gate itself rather than that chain: the
    /// buffer is deliberately shared with inner agents, so the day a skill tool is baked in by
    /// AgentFactory instead of attached per request, the chain breaks and only the gate is left.
    /// </summary>
    [Test]
    public async Task TextOnlyClient_EmitsNoSkillNotice()
    {
        _agentFactory.Agent.OnRun = NarrateSkillActivity;

        var chunks = await RunAsync(ChatClientCapabilities.TextOnly);

        Assert.That(chunks.Select(c => c.ContentType), Has.No.Member(PromptContentType.SkillNotice));
    }

    [Test]
    public async Task TextOnlyClient_StillEmptiesTheNarrationBuffer()
    {
        _agentFactory.Agent.OnRun = NarrateSkillActivity;

        await RunAsync(ChatClientCapabilities.TextOnly);

        Assert.That(_activityLog.DrainPending(), Is.Empty);
    }

    [Test]
    public async Task GenerativeUiClient_EmitsTheSkillNotice()
    {
        _agentFactory.Agent.OnRun = NarrateSkillActivity;

        var chunks = await RunAsync(ChatClientCapabilities.GenerativeUi);

        Assert.That(
            chunks.Where(c => c.ContentType == PromptContentType.SkillNotice).Select(c => c.Content),
            Has.Some.EqualTo("Using the demo-skill skill."));
    }

    // ---------------------------------------------------------------- PersistUserMessage

    /// <summary>
    /// Suppressing the user message is an AG-UI affordance for tool-result follow-up turns, whose
    /// prompt is synthetic. The text-only endpoint binds <c>PromptRequestForm</c> from the form, so
    /// honouring the flag there would let a crafted post run the agent, spend the tokens and keep
    /// the reply while dropping the user's own message from the transcript.
    /// </summary>
    [Test]
    public async Task TextOnlyClient_AskingNotToPersistTheUserMessage_IsIgnored()
    {
        await RunAsync(ChatClientCapabilities.TextOnly, persistUserMessage: false);

        Assert.That(await UserMessagesAsync(), Has.Some.EqualTo("Book me two tickets."));
    }

    [Test]
    public async Task GenerativeUiClient_AskingNotToPersistTheUserMessage_IsHonoured()
    {
        await RunAsync(ChatClientCapabilities.GenerativeUi, persistUserMessage: false);

        // The assistant row is asserted alongside the absent user row on purpose. SaveMessages runs
        // inside a catch-all, so "no user message" on its own is equally the signature of the whole
        // save having thrown — a pass for the opposite of the reason claimed.
        var userMessages = await UserMessagesAsync();
        var assistantMessages = await MessagesInRoleAsync(ChatRole.Assistant);

        Assert.Multiple(() =>
        {
            Assert.That(userMessages, Is.Empty);
            Assert.That(assistantMessages, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task GenerativeUiClient_ByDefault_PersistsTheUserMessage()
    {
        await RunAsync(ChatClientCapabilities.GenerativeUi);

        Assert.That(await UserMessagesAsync(), Has.Some.EqualTo("Book me two tickets."));
    }

    // ---------------------------------------------------------------- the default

    /// <summary>
    /// The zero value is the safe one. A call site that forgets to say which client it is speaking
    /// for must withhold the feature, never leak it.
    /// </summary>
    [Test]
    public async Task CapabilityOmitted_DefaultsToTheTextOnlyRun()
    {
        await foreach (var _ in _service.ChatStreamingAsync(_userId, Request(null)))
        {
        }

        Assert.That(ToolNames(), Has.No.Member(SkillPrompt.LoadToolName).And.No.Member(SkillPrompt.RenderToolName));
    }

    // ---------------------------------------------------------------- helpers

    private async Task<List<PromptResponse>> RunAsync(
        ChatClientCapabilities capabilities,
        string? frontendToolsJson = null,
        bool persistUserMessage = true)
    {
        var chunks = new List<PromptResponse>();

        await foreach (var chunk in _service.ChatStreamingAsync(
            _userId, Request(frontendToolsJson, persistUserMessage), isAdmin: false, capabilities))
        {
            chunks.Add(chunk);
        }

        return chunks;
    }

    private PromptRequestForm Request(string? frontendToolsJson, bool persistUserMessage = true) => new(
        Prompt: "Book me two tickets.",
        ConversationId: _conversationId,
        SessionId: null,
        Documents: null,
        AgentId: _agentId)
    {
        FrontendToolsJson = frontendToolsJson,
        PersistUserMessage = persistUserMessage,
    };

    private Task<IReadOnlyList<string>> UserMessagesAsync() => MessagesInRoleAsync(ChatRole.User);

    private async Task<IReadOnlyList<string>> MessagesInRoleAsync(ChatRole role) =>
        await _context.ChatMessages
            .Where(m => m.ConversationId == _conversationId && m.Role == role)
            .Select(m => m.Content)
            .ToListAsync();

    /// <summary>Stands in for a skill tool having narrated its activity mid-stream.</summary>
    private void NarrateSkillActivity() => _activityLog.Add("Using the demo-skill skill.");

    /// <summary>Stands in for a renderable server-side tool (e.g. get_weather) having run mid-stream.</summary>
    private void CaptureAToolCall() => _capture.Add(new CapturedToolCall(
        Guid.NewGuid().ToString(),
        "get_weather",
        new Dictionary<string, object?> { ["city"] = "Hanoi" },
        """{"tempC":31}"""));

    /// <summary>
    /// Every tool the model was offered, invocable or not. Filtered on
    /// <see cref="AIFunctionDeclaration"/> rather than <see cref="AIFunction"/> on purpose: a
    /// frontend tool is a declaration the browser executes, so the narrower filter would report a
    /// declared render_a2ui as absent — the exact result the gate is supposed to produce, arrived at
    /// for the wrong reason.
    /// </summary>
    private IReadOnlyList<string> ToolNames() =>
        [.. _agentFactory.Agent.Tools.OfType<AIFunctionDeclaration>().Select(t => t.Name)];

    private IReadOnlyList<string> SystemMessages() =>
        [.. _agentFactory.Agent.Messages
            .Where(m => m.Role == ChatRole.System)
            .Select(m => m.Text ?? string.Empty)];

    private async Task BindSkillAsync()
    {
        var package = new TestZipBuilder()
            .AddFile("demo-skill/SKILL.md", """
                ---
                name: demo-skill
                description: A demonstration skill used by the chat client capability tests.
                metadata:
                  version: "1.0"
                ---

                # demo-skill

                Body text.
                """)
            .AddFile("demo-skill/assets/panel.json", PanelSurface)
            .Build();

        var outcome = await _registry.ImportAsync(package, "demo-skill.zip", _userId);
        Assert.That(outcome.Succeeded, Is.True, string.Join(" | ", outcome.Errors));

        _context.AgentSkills.Add(new AgentSkill
        {
            AgentId = _agentId,
            SkillId = outcome.SkillId!.Value,
            IsEnabled = true,
        });

        await _context.SaveChangesAsync();
    }
}
