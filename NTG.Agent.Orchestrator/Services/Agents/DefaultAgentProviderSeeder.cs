using Microsoft.EntityFrameworkCore;
using NTG.Agent.Orchestrator.Data;

namespace NTG.Agent.Orchestrator.Services.Agents;

/// <summary>
/// Backfills the seeded Default Agent's provider from GitHub:Models:GitHubToken at startup so a
/// fresh install can chat immediately, without the manual Admin UI provider step. Only rows whose
/// ProviderName is still empty are touched — admin edits are never overwritten.
/// </summary>
public sealed class DefaultAgentProviderSeeder(
    IServiceProvider serviceProvider,
    IConfiguration configuration,
    ILogger<DefaultAgentProviderSeeder> logger) : IHostedService
{
    // Matches the keyed IAgentClientFactory registration and the GitHub Models catalog.
    private const string ProviderName = "GitHubModel";
    private const string ProviderEndpoint = "https://models.github.ai/inference";
    private const string ProviderModelName = "openai/gpt-4.1";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var token = configuration["GitHub:Models:GitHubToken"];
        if (string.IsNullOrWhiteSpace(token)) return;

        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();

        var agents = await db.Agents
            .Where(a => a.IsDefault && a.ProviderName == "")
            .ToListAsync(cancellationToken);
        if (agents.Count == 0) return;

        foreach (var agent in agents)
        {
            agent.ProviderName = ProviderName;
            agent.ProviderEndpoint = ProviderEndpoint;
            agent.ProviderModelName = ProviderModelName;
            agent.ProviderApiKey = token;
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Seeded provider {Provider}/{Model} on {Count} default agent(s) with an empty provider.",
            ProviderName, ProviderModelName, agents.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
