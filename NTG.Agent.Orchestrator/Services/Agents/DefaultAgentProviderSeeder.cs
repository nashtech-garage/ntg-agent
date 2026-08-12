using Microsoft.EntityFrameworkCore;
using NTG.Agent.Orchestrator.Data;

namespace NTG.Agent.Orchestrator.Services.Agents;

/// <summary>
/// Backfills the seeded Default Agent's provider at startup so a fresh install can chat
/// immediately, without the manual Admin UI provider step. Only rows whose ProviderName is
/// still empty are touched — admin edits are never overwritten.
/// </summary>
public sealed class DefaultAgentProviderSeeder(
    IServiceProvider serviceProvider,
    IConfiguration configuration,
    ILogger<DefaultAgentProviderSeeder> logger) : IHostedService
{
    // The default agent rides the same Azure resource/key that LightRAG already requires
    // (one key serves chat + embeddings there), so no extra secret is needed. GitHub Models
    // was the previous default but is being retired (410 retirement brownouts).
    private const string ProviderName = "AzureOpenAI";

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
            .Where(a => a.IsDefault && a.ProviderName == "")
            .ToListAsync(cancellationToken);
        if (agents.Count == 0) return;

        // The chat client factory points the OpenAI SDK at Azure's /openai/v1 surface.
        var providerEndpoint = $"{endpoint.TrimEnd('/')}/openai/v1";

        foreach (var agent in agents)
        {
            agent.ProviderName = ProviderName;
            agent.ProviderEndpoint = providerEndpoint;
            agent.ProviderModelName = model;
            agent.ProviderApiKey = apiKey;
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Seeded provider {Provider}/{Model} on {Count} default agent(s) with an empty provider.",
            ProviderName, model, agents.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
