namespace NTG.Agent.LightRag;

/// <summary>
/// Resolves an agent id to the id of the agent owning its LightRAG knowledge base — the single
/// value that keys the container name, the <c>WORKSPACE</c> env var, and the file-store directory.
/// An agent that owns its knowledge base resolves to itself, which is why agents that predate
/// knowledge-base sharing keep the containers and workspaces they already have.
/// <para>
/// Returns <see langword="null"/> for an agent with no knowledge base (an inner agent). Callers
/// that cannot proceed without one should use <see cref="RequireOwnerAgentIdAsync"/>.
/// </para>
/// <para>
/// Scoped, with a per-scope cache: a single chat turn touches the knowledge service from several
/// call sites (search, the client factory, the file store), and this keeps that to one lookup.
/// </para>
/// </summary>
public sealed class LightRagWorkspaceResolver
{
    private readonly ILightRagAgentStore _store;
    private readonly Dictionary<Guid, Guid?> _cache = [];

    public LightRagWorkspaceResolver(ILightRagAgentStore store)
    {
        _store = store;
    }

    /// <summary>
    /// The knowledge base backing <paramref name="agentId"/>, or <see langword="null"/> when it has
    /// none.
    /// </summary>
    public async Task<Guid?> GetOwnerAgentIdAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(agentId, out var cached))
            return cached;

        var owner = await _store.GetKnowledgeOwnerAsync(agentId, cancellationToken);
        _cache[agentId] = owner;
        return owner;
    }

    /// <summary>
    /// Same as <see cref="GetOwnerAgentIdAsync"/>, but throws when the agent has no knowledge base.
    /// Use at call sites that cannot proceed without one — dialing a container, importing a
    /// document — so the failure names the cause instead of surfacing later as a null-reference or
    /// a request to a nonsense URL.
    /// </summary>
    public async Task<Guid> RequireOwnerAgentIdAsync(Guid agentId, CancellationToken cancellationToken = default)
        => await GetOwnerAgentIdAsync(agentId, cancellationToken)
           ?? throw new InvalidOperationException(
               $"Agent {agentId} has no LightRAG knowledge base. Inner agents do not have one.");
}
