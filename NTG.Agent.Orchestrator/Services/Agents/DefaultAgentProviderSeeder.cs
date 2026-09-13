using Microsoft.EntityFrameworkCore;
using NTG.Agent.Common.Dtos.Agents;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Models.Agents;

namespace NTG.Agent.Orchestrator.Services.Agents;

/// <summary>
/// Backfills the seeded Default Agent's provider at startup so a fresh install can chat
/// immediately, without the manual Admin UI provider step. Only providers whose ApiKey is
/// still empty are touched — admin edits are never overwritten.
/// </summary>
public sealed class DefaultAgentProviderSeeder(
    IServiceProvider serviceProvider,
    IConfiguration configuration,
    ILogger<DefaultAgentProviderSeeder> logger) : IHostedService
{
    // The default agent rides the developer's own Azure resource/key/deployment that LightRAG
    // already requires (one resource serves chat + embeddings), so no extra secret is needed. GitHub Models
    // was the previous default but is being retired (410 retirement brownouts).
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var apiKey = configuration["LightRag:LlmApiKey"];
        var endpoint = configuration["LightRag:LlmEndpoint"];
        var model = configuration["LightRag:LlmModel"];
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(model))
            return;

        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();

        var agents = await db.Agents
            .Include(a => a.Provider!).ThenInclude(p => p.Models)
            .Where(a => a.IsDefault && a.Provider != null && string.IsNullOrEmpty(a.Provider.ApiKey))
            .ToListAsync(cancellationToken);
        if (agents.Count == 0) return;

        foreach (var agent in agents)
        {
            var provider = agent.Provider!;
            provider.ProviderType = ProviderType.AzureOpenAI;
            // Stored raw; the chat factory normalizes it to the /openai/v1 surface (AzureOpenAIEndpoint.ToV1).
            provider.Endpoint = endpoint;
            provider.ApiKey = apiKey;
            provider.UpdatedAt = DateTime.UtcNow;
            if (!provider.Models.Any(m => m.ModelId == model))
                db.Add(new ProviderModel { Id = Guid.NewGuid(), ProviderId = provider.Id, ModelId = model });
            agent.ModelOverride = model;
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Seeded {Provider}/{Model} on {Count} default agent(s) whose provider had no API key.",
            ProviderType.AzureOpenAI, model, agents.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
