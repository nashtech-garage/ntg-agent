
using Microsoft.KernelMemory;

namespace NTG.Agent.Orchestrator.Knowledge;

public class KernelMemoryKnowledge : IKnowledge
{
    private readonly MemoryWebClient _memoryWebClient;

    public KernelMemoryKnowledge()
    {
        var _memoryWebClient = new MemoryWebClient("https://localhost:7181", "Blm8d7sFx7arM9EN2QUxGy7yUjCyvRjx");
    }
    public Task ImportDocument(Stream content, Guid agentId)
    {
        throw new NotImplementedException();
    }

    public Task<string> SearchKnowledgeAsync(string query, Guid agentId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<string> SearchKnowledgeAsync(string query, Guid agentId, Guid userId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
