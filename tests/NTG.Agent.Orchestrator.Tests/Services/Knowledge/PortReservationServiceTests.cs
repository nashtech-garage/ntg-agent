using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NTG.Agent.LightRag;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Services.Knowledge;
using AgentModel = NTG.Agent.Orchestrator.Models.Agents.Agent;

namespace NTG.Agent.Orchestrator.Tests.Services.Knowledge;

[TestFixture]
public class PortReservationServiceTests
{
    private string _dbName = null!;

    [SetUp]
    public void Setup() => _dbName = Guid.NewGuid().ToString();

    private AgentDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AgentDbContext>()
            .UseInMemoryDatabase(_dbName)
            .Options);

    private static IOptions<LightRagSettings> Options(int start, int end) =>
        Microsoft.Extensions.Options.Options.Create(new LightRagSettings { PortRangeStart = start, PortRangeEnd = end });

    private async Task<Guid> SeedAgentAsync(AgentDbContext db, int? port)
    {
        var id = Guid.NewGuid();
        db.Agents.Add(new AgentModel { Id = id, Name = $"agent-{id:N}", LightRagPort = port });
        await db.SaveChangesAsync();
        return id;
    }

    [Test]
    public async Task GetOrReserveAsync_ReturnsExistingReservation_WhenAlreadySet()
    {
        await using var db = NewContext();
        var agentId = await SeedAgentAsync(db, port: 20007);
        var svc = new PortReservationService(new LightRagEfAgentPortStore(db), Options(20000, 20999));

        var port = await svc.GetOrReserveAsync(agentId);

        Assert.That(port, Is.EqualTo(20007));
    }

    [Test]
    public async Task GetOrReserveAsync_AllocatesLowestFreePortInRange()
    {
        await using var db = NewContext();
        await SeedAgentAsync(db, port: 20000);
        var agentId = await SeedAgentAsync(db, port: null);
        var svc = new PortReservationService(new LightRagEfAgentPortStore(db), Options(20000, 20999));

        var port = await svc.GetOrReserveAsync(agentId);

        Assert.That(port, Is.EqualTo(20001));
    }

    [Test]
    public async Task GetOrReserveAsync_NeverReturnsAnotherAgentsPort()
    {
        await using var db = NewContext();
        foreach (var p in new[] { 20000, 20001, 20002, 20004 })
            await SeedAgentAsync(db, port: p);
        var agentId = await SeedAgentAsync(db, port: null);
        var svc = new PortReservationService(new LightRagEfAgentPortStore(db), Options(20000, 20999));

        var port = await svc.GetOrReserveAsync(agentId);

        // Lowest gap is 20003; must not collide with any existing reservation.
        Assert.That(port, Is.EqualTo(20003));
    }

    [Test]
    public async Task GetOrReserveAsync_TwoAgents_GetDistinctPorts()
    {
        await using var db = NewContext();
        var a = await SeedAgentAsync(db, port: null);
        var b = await SeedAgentAsync(db, port: null);
        var svc = new PortReservationService(new LightRagEfAgentPortStore(db), Options(20000, 20999));

        var portA = await svc.GetOrReserveAsync(a);
        var portB = await svc.GetOrReserveAsync(b);

        Assert.That(portA, Is.Not.EqualTo(portB));
    }

    [Test]
    public async Task GetOrReserveAsync_ConcurrentDifferentAgents_GetDistinctPorts()
    {
        Guid a, b;
        await using (var seed = NewContext())
        {
            a = await SeedAgentAsync(seed, port: null);
            b = await SeedAgentAsync(seed, port: null);
        }

        // Separate contexts (sharing the same in-memory database) exercise the static
        // allocation gate that serialises cross-scope reservations.
        await using var ctx1 = NewContext();
        await using var ctx2 = NewContext();
        var svc1 = new PortReservationService(new LightRagEfAgentPortStore(ctx1), Options(20000, 20999));
        var svc2 = new PortReservationService(new LightRagEfAgentPortStore(ctx2), Options(20000, 20999));

        var ports = await Task.WhenAll(svc1.GetOrReserveAsync(a), svc2.GetOrReserveAsync(b));

        Assert.That(ports[0], Is.Not.EqualTo(ports[1]));
    }

    [Test]
    public async Task GetOrReserveAsync_Throws_WhenPoolExhausted()
    {
        await using var db = NewContext();
        await SeedAgentAsync(db, port: 20000);
        await SeedAgentAsync(db, port: 20001);
        var agentId = await SeedAgentAsync(db, port: null);
        var svc = new PortReservationService(new LightRagEfAgentPortStore(db), Options(20000, 20001));

        Assert.ThrowsAsync<PortPoolExhaustedException>(() => svc.GetOrReserveAsync(agentId));
    }

    [Test]
    public async Task ReassignAsync_ReturnsDifferentPort_ThanCurrent()
    {
        await using var db = NewContext();
        var agentId = await SeedAgentAsync(db, port: 20000);
        var svc = new PortReservationService(new LightRagEfAgentPortStore(db), Options(20000, 20999));

        var port = await svc.ReassignAsync(agentId);

        Assert.That(port, Is.Not.EqualTo(20000));
    }

    [Test]
    public async Task ReassignAsync_NeverReturnsAnotherAgentsPort()
    {
        await using var db = NewContext();
        await SeedAgentAsync(db, port: 20001);
        var agentId = await SeedAgentAsync(db, port: 20000);
        var svc = new PortReservationService(new LightRagEfAgentPortStore(db), Options(20000, 20999));

        var port = await svc.ReassignAsync(agentId);

        // Not its own old port (20000) and not the other agent's (20001).
        Assert.That(port, Is.EqualTo(20002));
    }

    [Test]
    public async Task ReassignAsync_PersistsNewPort()
    {
        await using var db = NewContext();
        var agentId = await SeedAgentAsync(db, port: 20000);
        var svc = new PortReservationService(new LightRagEfAgentPortStore(db), Options(20000, 20999));

        var port = await svc.ReassignAsync(agentId);

        var saved = await db.Agents.FindAsync(agentId);
        Assert.That(saved!.LightRagPort, Is.EqualTo(port));
    }
}
