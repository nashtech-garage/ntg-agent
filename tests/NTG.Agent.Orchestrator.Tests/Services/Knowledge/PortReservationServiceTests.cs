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

    private static PortReservationService NewService(AgentDbContext db, FakeLedger ledger, int start = 20000, int end = 20999)
        => new(new LightRagEfAgentPortStore(db), ledger, Options(start, end));

    /// <summary>
    /// In-memory stand-in for the shared Postgres ledger, mirroring the SQL semantics: one port per
    /// agent, globally unique ports, lowest-free allocation, and exhaustion when the range is full.
    /// </summary>
    private sealed class FakeLedger : ILightRagPortReservationStore
    {
        private readonly Dictionary<Guid, int> _byAgent = [];

        public int CallCount { get; private set; }

        public void Seed(Guid agentId, int port) => _byAgent[agentId] = port;

        public Task<int?> GetReservedPortAsync(Guid agentId, CancellationToken cancellationToken = default)
            => Task.FromResult(_byAgent.TryGetValue(agentId, out var p) ? p : (int?)null);

        public Task<int> GetOrReserveAsync(Guid agentId, int rangeStart, int rangeEnd, CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (_byAgent.TryGetValue(agentId, out var existing)) return Task.FromResult(existing);

            var port = LowestFree(rangeStart, rangeEnd, avoid: null)
                ?? throw new PortPoolExhaustedException(rangeStart, rangeEnd);
            _byAgent[agentId] = port;
            return Task.FromResult(port);
        }

        public Task<int> ReassignAsync(Guid agentId, int rangeStart, int rangeEnd, CancellationToken cancellationToken = default)
        {
            CallCount++;
            var current = _byAgent.TryGetValue(agentId, out var c) ? c : (int?)null;
            _byAgent.Remove(agentId);

            var port = LowestFree(rangeStart, rangeEnd, avoid: current)
                ?? throw new PortPoolExhaustedException(rangeStart, rangeEnd);
            _byAgent[agentId] = port;
            return Task.FromResult(port);
        }

        public Task ReleaseAsync(Guid agentId, CancellationToken cancellationToken = default)
        {
            CallCount++;
            _byAgent.Remove(agentId);
            return Task.CompletedTask;
        }

        private int? LowestFree(int start, int end, int? avoid)
        {
            var taken = _byAgent.Values.ToHashSet();
            for (var p = start; p <= end; p++)
                if (p != avoid && !taken.Contains(p)) return p;
            return null;
        }
    }

    [Test]
    public async Task GetOrReserveAsync_ReturnsCachedPort_WithoutConsultingLedger()
    {
        await using var db = NewContext();
        var agentId = await SeedAgentAsync(db, port: 20007);
        var ledger = new FakeLedger();
        var svc = NewService(db, ledger);

        var port = await svc.GetOrReserveAsync(agentId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(port, Is.EqualTo(20007));
            // Hot path stays local: a cached port must not cost a cross-database round-trip.
            Assert.That(ledger.CallCount, Is.Zero);
        }
    }

    [Test]
    public async Task GetOrReserveAsync_ReservesFromLedger_AndCachesPortLocally()
    {
        await using var db = NewContext();
        var agentId = await SeedAgentAsync(db, port: null);
        var svc = NewService(db, new FakeLedger());

        var port = await svc.GetOrReserveAsync(agentId);

        var saved = await db.Agents.FindAsync(agentId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(port, Is.EqualTo(20000));
            Assert.That(saved!.LightRagPort, Is.EqualTo(port));
        }
    }

    [Test]
    public async Task GetOrReserveAsync_AllocatesLowestFreePortInRange()
    {
        await using var db = NewContext();
        var agentId = await SeedAgentAsync(db, port: null);
        var ledger = new FakeLedger();
        // Ports already held by other developers' agents in the shared ledger.
        ledger.Seed(Guid.NewGuid(), 20000);
        ledger.Seed(Guid.NewGuid(), 20001);
        ledger.Seed(Guid.NewGuid(), 20002);
        ledger.Seed(Guid.NewGuid(), 20004);
        var svc = NewService(db, ledger);

        var port = await svc.GetOrReserveAsync(agentId);

        // Lowest gap is 20003 — and critically, it never collides with another developer's agent.
        Assert.That(port, Is.EqualTo(20003));
    }

    [Test]
    public async Task GetOrReserveAsync_TwoAgents_GetDistinctPorts()
    {
        await using var db = NewContext();
        var a = await SeedAgentAsync(db, port: null);
        var b = await SeedAgentAsync(db, port: null);
        var ledger = new FakeLedger();
        var svc = NewService(db, ledger);

        var portA = await svc.GetOrReserveAsync(a);
        var portB = await svc.GetOrReserveAsync(b);

        Assert.That(portA, Is.Not.EqualTo(portB));
    }

    [Test]
    public async Task GetOrReserveAsync_Throws_WhenPoolExhausted()
    {
        await using var db = NewContext();
        var agentId = await SeedAgentAsync(db, port: null);
        var ledger = new FakeLedger();
        ledger.Seed(Guid.NewGuid(), 20000);
        ledger.Seed(Guid.NewGuid(), 20001);
        var svc = NewService(db, ledger, start: 20000, end: 20001);

        Assert.ThrowsAsync<PortPoolExhaustedException>(() => svc.GetOrReserveAsync(agentId));
    }

    [Test]
    public async Task ReassignAsync_ReturnsDifferentPort_AndPersistsCache()
    {
        await using var db = NewContext();
        var agentId = await SeedAgentAsync(db, port: 20000);
        var ledger = new FakeLedger();
        ledger.Seed(agentId, 20000);
        var svc = NewService(db, ledger);

        var port = await svc.ReassignAsync(agentId);

        var saved = await db.Agents.FindAsync(agentId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(port, Is.Not.EqualTo(20000));
            Assert.That(saved!.LightRagPort, Is.EqualTo(port));
        }
    }

    [Test]
    public async Task ReassignAsync_NeverReturnsAnotherAgentsPort()
    {
        await using var db = NewContext();
        var agentId = await SeedAgentAsync(db, port: 20000);
        var ledger = new FakeLedger();
        ledger.Seed(agentId, 20000);
        ledger.Seed(Guid.NewGuid(), 20001);   // another developer's agent
        var svc = NewService(db, ledger);

        var port = await svc.ReassignAsync(agentId);

        // Not its own old port (20000) and not the other agent's (20001).
        Assert.That(port, Is.EqualTo(20002));
    }

    [Test]
    public async Task ReleaseAsync_ReturnsPortToPool_ForReuseByANewAgent()
    {
        await using var db = NewContext();
        var deleted = await SeedAgentAsync(db, port: null);
        var newcomer = await SeedAgentAsync(db, port: null);
        var ledger = new FakeLedger();
        var svc = NewService(db, ledger);

        var released = await svc.GetOrReserveAsync(deleted);   // takes 20000
        await svc.ReleaseAsync(deleted);                        // agent deleted -> port freed
        var reused = await svc.GetOrReserveAsync(newcomer);

        // Deleting an agent must return its port to the pool, otherwise the range leaks over time.
        Assert.That(reused, Is.EqualTo(released));
    }
}
