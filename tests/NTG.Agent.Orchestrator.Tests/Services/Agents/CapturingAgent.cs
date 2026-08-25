using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using NTG.Agent.Orchestrator.Services.Agents;
using System.Text.Json;

namespace NTG.Agent.Orchestrator.Tests.Services.Agents;

// Shared test doubles for driving AgentService without a model provider. Extracted from
// ChatClientCapabilityTests once AgUiControllerAccessTests needed the same pair: the fixtures ask
// different questions of the same seam — what the run was handed, and whether it ran at all — and
// a second copy of a fake this fiddly is a second copy to keep in step.



/// <summary>Hands back one <see cref="CapturingAgent"/> so a test can read what the run was given.</summary>
internal sealed class CapturingAgentFactory : IAgentFactory
{
    public CapturingAgent Agent { get; } = new();

    /// <summary>
    /// When set, <see cref="CreateAgent(Guid, Guid?, bool)"/> throws it instead of returning an
    /// agent. The real factory refuses a run for reasons no caller can check in advance — an inner,
    /// tool-only agent, or access revoked mid-request — and a caller's only view of that is the
    /// exception. This is the seam for driving what happens to it.
    /// </summary>
    public Exception? RefuseWith { get; set; }

    public string ToolContext { get; set; } = string.Empty;

    public Task<AIAgent> CreateAgent(Guid agentId) => Task.FromResult<AIAgent>(Agent);

    public Task<AIAgent> CreateAgent(Guid agentId, Guid? userId, bool isAdmin) =>
        RefuseWith is not null ? Task.FromException<AIAgent>(RefuseWith) : Task.FromResult<AIAgent>(Agent);

    public Task<AIAgent> CreateBasicAgent(string instructions) => Task.FromResult<AIAgent>(Agent);

    public Task<List<AITool>> GetAvailableTools(NTG.Agent.Orchestrator.Models.Agents.Agent agent) =>
        Task.FromResult(new List<AITool>());

    public Task<IEnumerable<AITool>> GetMcpToolsAsync(string endpoint) =>
        Task.FromResult(Enumerable.Empty<AITool>());
}

/// <summary>
/// Records the messages and tools of the most recent run, then streams one short reply.
/// </summary>
internal sealed class CapturingAgent : AIAgent
{
    public IReadOnlyList<ChatMessage> Messages { get; private set; } = [];

    public IReadOnlyList<AITool> Tools { get; private set; } = [];

    /// <summary>Runs once per streamed run, before the reply — the seam for simulating a tool call.</summary>
    public Action? OnRun { get; set; }

    public void Reset()
    {
        Messages = [];
        Tools = [];
    }

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Messages = [.. messages];
        Tools = options is ChatClientAgentRunOptions { ChatOptions.Tools: { } tools } ? [.. tools] : [];

        OnRun?.Invoke();

        yield return new AgentResponseUpdate(ChatRole.Assistant, "Done.");

        await Task.CompletedTask;
    }

    protected override Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Messages = [.. messages];
        return Task.FromResult(new AgentResponse(new ChatMessage(ChatRole.Assistant, "Done.")));
    }

    protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
        JsonElement serializedSession,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
        AgentSession session,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
