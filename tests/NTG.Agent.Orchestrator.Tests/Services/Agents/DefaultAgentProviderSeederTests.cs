using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Services.Agents;
using AgentEntity = NTG.Agent.Orchestrator.Models.Agents.Agent;

namespace NTG.Agent.Orchestrator.Tests.Services.Agents;

[TestFixture]
public class DefaultAgentProviderSeederTests
{
    private static ServiceProvider BuildProvider(string dbName)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AgentDbContext>(o => o.UseInMemoryDatabase(dbName));
        return services.BuildServiceProvider();
    }

    private static IConfiguration BuildConfig(string? apiKey, string? endpoint = "https://res.openai.azure.com/", string? model = "gpt-5.1") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LightRag:LlmApiKey"] = apiKey,
                ["LightRag:LlmEndpoint"] = endpoint,
                ["LightRag:LlmModel"] = model,
            })
            .Build();

    private static async Task<Guid> SeedAgentAsync(IServiceProvider sp, bool isDefault, string providerName = "")
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        var id = Guid.NewGuid();
        db.Agents.Add(new AgentEntity { Id = id, Name = "A", IsDefault = isDefault, ProviderName = providerName });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<AgentEntity> GetAgentAsync(IServiceProvider sp, Guid id)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        return await db.Agents.AsNoTracking().FirstAsync(a => a.Id == id);
    }

    private static DefaultAgentProviderSeeder BuildSeeder(IServiceProvider sp, IConfiguration config) =>
        new(sp, config, NullLogger<DefaultAgentProviderSeeder>.Instance);

    [Test]
    public async Task StartAsync_FillsAzureProvider_WhenDefaultAgentHasEmptyProvider()
    {
        var sp = BuildProvider(Guid.NewGuid().ToString());
        var id = await SeedAgentAsync(sp, isDefault: true);

        await BuildSeeder(sp, BuildConfig("azure-key")).StartAsync(CancellationToken.None);

        var agent = await GetAgentAsync(sp, id);
        Assert.Multiple(() =>
        {
            Assert.That(agent.ProviderName, Is.EqualTo("AzureOpenAI"));
            // The chat client factory targets Azure's /openai/v1 surface directly.
            Assert.That(agent.ProviderEndpoint, Is.EqualTo("https://res.openai.azure.com/openai/v1"));
            Assert.That(agent.ProviderModelName, Is.EqualTo("gpt-5.1"));
            Assert.That(agent.ProviderApiKey, Is.EqualTo("azure-key"));
        });
    }

    // The seeder exists to unbreak first chat on fresh installs; it must never fight
    // an admin's explicit provider choice.
    [Test]
    public async Task StartAsync_LeavesConfiguredProviderUntouched()
    {
        var sp = BuildProvider(Guid.NewGuid().ToString());
        var id = await SeedAgentAsync(sp, isDefault: true, providerName: "GitHubModel");

        await BuildSeeder(sp, BuildConfig("azure-key")).StartAsync(CancellationToken.None);

        var agent = await GetAgentAsync(sp, id);
        Assert.That(agent.ProviderName, Is.EqualTo("GitHubModel"));
        Assert.That(agent.ProviderApiKey, Is.Empty);
    }

    [Test]
    public async Task StartAsync_IgnoresNonDefaultAgents()
    {
        var sp = BuildProvider(Guid.NewGuid().ToString());
        var id = await SeedAgentAsync(sp, isDefault: false);

        await BuildSeeder(sp, BuildConfig("azure-key")).StartAsync(CancellationToken.None);

        var agent = await GetAgentAsync(sp, id);
        Assert.That(agent.ProviderName, Is.Empty);
    }

    [Test]
    public async Task StartAsync_DoesNothing_WhenAzureConfigIncomplete()
    {
        var sp = BuildProvider(Guid.NewGuid().ToString());
        var id = await SeedAgentAsync(sp, isDefault: true);

        await BuildSeeder(sp, BuildConfig(apiKey: null)).StartAsync(CancellationToken.None);
        await BuildSeeder(sp, BuildConfig("azure-key", endpoint: "")).StartAsync(CancellationToken.None);
        await BuildSeeder(sp, BuildConfig("azure-key", model: null)).StartAsync(CancellationToken.None);

        var agent = await GetAgentAsync(sp, id);
        Assert.That(agent.ProviderName, Is.Empty);
    }
}
