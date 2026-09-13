using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NTG.Agent.Common.Dtos.Agents;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Models.Agents;
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

    // Mirrors the HasData seed: an OpenAI-typed provider with no key, one enabled model, one default agent.
    private static async Task<Guid> SeedAgentAsync(IServiceProvider sp, bool isDefault, string? providerApiKey = null)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        var provider = new Provider { Id = Guid.NewGuid(), Name = "Default Provider", ProviderType = ProviderType.OpenAI, ApiKey = providerApiKey };
        provider.Models.Add(new ProviderModel { Id = Guid.NewGuid(), ModelId = "gpt-4o" });
        var id = Guid.NewGuid();
        db.Providers.Add(provider);
        db.Agents.Add(new AgentEntity { Id = id, Name = "A", IsDefault = isDefault, ProviderId = provider.Id, ModelOverride = "gpt-4o" });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<AgentEntity> GetAgentAsync(IServiceProvider sp, Guid id)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        return await db.Agents.AsNoTracking().Include(a => a.Provider!).ThenInclude(p => p.Models).FirstAsync(a => a.Id == id);
    }

    private static DefaultAgentProviderSeeder BuildSeeder(IServiceProvider sp, IConfiguration config) =>
        new(sp, config, NullLogger<DefaultAgentProviderSeeder>.Instance);

    [Test]
    public async Task StartAsync_FillsAzureProvider_WhenDefaultAgentProviderHasNoKey()
    {
        var sp = BuildProvider(Guid.NewGuid().ToString());
        var id = await SeedAgentAsync(sp, isDefault: true);

        await BuildSeeder(sp, BuildConfig("azure-key")).StartAsync(CancellationToken.None);

        var agent = await GetAgentAsync(sp, id);
        Assert.Multiple(() =>
        {
            Assert.That(agent.Provider!.ProviderType, Is.EqualTo(ProviderType.AzureOpenAI));
            Assert.That(agent.Provider.Endpoint, Is.EqualTo("https://res.openai.azure.com/"));
            Assert.That(agent.Provider.ApiKey, Is.EqualTo("azure-key"));
            Assert.That(agent.Provider.Models.Select(m => m.ModelId), Does.Contain("gpt-5.1"));
            Assert.That(agent.ModelOverride, Is.EqualTo("gpt-5.1"));
        });
    }

    // The seeder exists to unbreak first chat on fresh installs; it must never fight
    // an admin's explicit provider choice.
    [Test]
    public async Task StartAsync_LeavesConfiguredProviderUntouched()
    {
        var sp = BuildProvider(Guid.NewGuid().ToString());
        var id = await SeedAgentAsync(sp, isDefault: true, providerApiKey: "admin-key");

        await BuildSeeder(sp, BuildConfig("azure-key")).StartAsync(CancellationToken.None);

        var agent = await GetAgentAsync(sp, id);
        Assert.Multiple(() =>
        {
            Assert.That(agent.Provider!.ProviderType, Is.EqualTo(ProviderType.OpenAI));
            Assert.That(agent.Provider.ApiKey, Is.EqualTo("admin-key"));
            Assert.That(agent.ModelOverride, Is.EqualTo("gpt-4o"));
        });
    }

    [Test]
    public async Task StartAsync_IgnoresNonDefaultAgents()
    {
        var sp = BuildProvider(Guid.NewGuid().ToString());
        var id = await SeedAgentAsync(sp, isDefault: false);

        await BuildSeeder(sp, BuildConfig("azure-key")).StartAsync(CancellationToken.None);

        var agent = await GetAgentAsync(sp, id);
        Assert.That(agent.Provider!.ApiKey, Is.Null);
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
        Assert.That(agent.Provider!.ApiKey, Is.Null);
    }
}
