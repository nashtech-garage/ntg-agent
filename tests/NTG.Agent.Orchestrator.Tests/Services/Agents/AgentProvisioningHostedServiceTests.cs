using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NTG.Agent.Common.Dtos.Agents;
using NTG.Agent.Common.Knowledge;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Models.Agents;
using NTG.Agent.Orchestrator.Services.Agents;
using AgentEntity = NTG.Agent.Orchestrator.Models.Agents.Agent;

namespace NTG.Agent.Orchestrator.Tests.Services.Agents;

[TestFixture]
public class AgentProvisioningHostedServiceTests
{
    // Root provider the worker creates scopes from: a shared in-memory AgentDbContext plus the
    // mocked knowledge provisioner.
    private static ServiceProvider BuildProvider(string dbName, IKnowledgeProvisioner provisioner)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AgentDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton(provisioner);
        return services.BuildServiceProvider();
    }

    private static async Task<Guid> SeedProvisioningAgentAsync(IServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        var id = Guid.NewGuid();
        db.Agents.Add(new AgentEntity { Id = id, Name = "Pending", ProvisioningStatus = AgentProvisioningStatus.Provisioning });
        await db.SaveChangesAsync();
        return id;
    }

    // Polls the shared store until the agent leaves Provisioning (the worker's startup drain runs
    // immediately, so this resolves in milliseconds), or fails after the timeout.
    private static async Task<AgentEntity> WaitForTerminalAsync(IServiceProvider sp, Guid id, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
            var agent = await db.Agents.AsNoTracking().FirstAsync(a => a.Id == id);
            if (agent.ProvisioningStatus != AgentProvisioningStatus.Provisioning)
                return agent;
            await Task.Delay(20);
        }
        throw new TimeoutException("Agent did not reach a terminal provisioning state in time.");
    }

    [Test]
    public async Task Worker_SetsReady_WhenProvisioningSucceeds()
    {
        var provisioner = new Mock<IKnowledgeProvisioner>();
        provisioner.Setup(p => p.ProvisionAgentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var sp = BuildProvider(Guid.NewGuid().ToString(), provisioner.Object);
        var agentId = await SeedProvisioningAgentAsync(sp);

        var worker = new AgentProvisioningHostedService(sp, new AgentProvisioningSignal(), NullLogger<AgentProvisioningHostedService>.Instance);
        await worker.StartAsync(CancellationToken.None);
        var agent = await WaitForTerminalAsync(sp, agentId, TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(agent.ProvisioningStatus, Is.EqualTo(AgentProvisioningStatus.Ready));
            Assert.That(agent.ProvisioningError, Is.Null);
            Assert.That(agent.ProvisionedAt, Is.Not.Null);
        }
        provisioner.Verify(p => p.ProvisionAgentAsync(agentId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Worker_SetsFailed_WithError_WhenProvisioningThrows()
    {
        var provisioner = new Mock<IKnowledgeProvisioner>();
        provisioner.Setup(p => p.ProvisionAgentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("docker down"));
        var sp = BuildProvider(Guid.NewGuid().ToString(), provisioner.Object);
        var agentId = await SeedProvisioningAgentAsync(sp);

        var worker = new AgentProvisioningHostedService(sp, new AgentProvisioningSignal(), NullLogger<AgentProvisioningHostedService>.Instance);
        await worker.StartAsync(CancellationToken.None);
        var agent = await WaitForTerminalAsync(sp, agentId, TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(agent.ProvisioningStatus, Is.EqualTo(AgentProvisioningStatus.Failed));
            Assert.That(agent.ProvisioningError, Is.EqualTo("docker down"));
        }
    }
}
