using Microsoft.Extensions.AI;
using NTG.Agent.Common.Dtos.Knowledge;
using NTG.Agent.Common.Knowledge;
using System.ComponentModel;

namespace NTG.Agent.Orchestrator.Plugins;

public sealed class KnowledgePlugin
{
    private readonly IKnowledgeService _knowledgeService;
    private readonly List<string> _tags;
    private readonly Guid _agentId;

    // Speculative prefetch: the orchestrator kicks off a knowledge search using the raw user
    // prompt at request start (concurrently with agent build and the first LLM call), so the
    // result is warm by the time the model decides to call the memory tool.
    private readonly string? _prefetchQuery;
    private readonly Task<KnowledgeSearchResponse>? _prefetch;

    public KnowledgePlugin(IKnowledgeService knowledgeService, List<string> tags, Guid agentId)
        : this(knowledgeService, tags, agentId, null, null)
    {
    }

    public KnowledgePlugin(
        IKnowledgeService knowledgeService,
        List<string> tags,
        Guid agentId,
        string? prefetchQuery,
        Task<KnowledgeSearchResponse>? prefetch)
    {
        _knowledgeService = knowledgeService ?? throw new ArgumentNullException(nameof(knowledgeService));
        _tags = tags ?? throw new ArgumentNullException(nameof(tags));
        _agentId = agentId;
        _prefetchQuery = prefetchQuery;
        _prefetch = prefetch;
    }

    [Description("Search knowledge base")]
    public async Task<KnowledgeSearchResponse> SearchAsync([Description("the value to search")]string query)
    {
        // Reuse the speculative prefetch only when the model's query matches the prompt we
        // prefetched with (after normalization). On a mismatch the model reformulated the query
        // (e.g. a context-dependent follow-up), so run a fresh search for the correct context.
        if (_prefetch is not null && NormalizedEquals(query, _prefetchQuery))
        {
            try
            {
                return await _prefetch;
            }
            catch
            {
                // The prefetch faulted (e.g. LightRAG hiccup). Fall through to a fresh query
                // rather than surfacing the background task's exception.
            }
        }

        var result = await _knowledgeService.SearchAsync(query, _agentId, _tags);
        return result;
    }

    // Trim, lowercase, and collapse internal whitespace so trivial wording differences between
    // the raw prompt and the model's tool query still count as a match.
    private static bool NormalizedEquals(string? a, string? b)
        => string.Equals(Normalize(a), Normalize(b), StringComparison.Ordinal);

    private static string Normalize(string? value)
        => value is null ? string.Empty : string.Join(' ', value.Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public AITool AsAITool()
    {
        return AIFunctionFactory.Create(this.SearchAsync, new AIFunctionFactoryOptions { Name = "memory" });
    }
}
