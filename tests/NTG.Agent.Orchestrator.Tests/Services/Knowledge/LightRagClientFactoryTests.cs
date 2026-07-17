using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NTG.Agent.LightRag;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Services.Knowledge;
using AgentModel = NTG.Agent.Orchestrator.Models.Agents.Agent;

namespace NTG.Agent.Orchestrator.Tests.Services.Knowledge;

[TestFixture]
public class LightRagClientFactoryTests
{
    // A port nothing listens on, so the reachability probe always fails and the factory
    // falls through to provisioning.
    private const int UnreachablePort = 1;
    private const int ReservedPort = 20005;

    private AgentDbContext _db = null!;
    private Mock<IHttpClientFactory> _httpFactory = null!;
    private Mock<ILightRagProvisioner> _provisioner = null!;
    private Mock<ILightRagHealthProbe> _healthProbe = null!;
    // The factory asks IHttpClientFactory for the real per-agent client (the reachability
    // probe is now delegated to ILightRagHealthProbe) — keep the created clients so the test
    // can assert the resulting client's BaseAddress.
    private List<HttpClient> _created = null!;

    [SetUp]
    public void Setup()
    {
        _db = new AgentDbContext(new DbContextOptionsBuilder<AgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        _created = [];
        _httpFactory = new Mock<IHttpClientFactory>();
        _httpFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => { var c = new HttpClient(); _created.Add(c); return c; });

        _provisioner = new Mock<ILightRagProvisioner>();
        _provisioner
            .Setup(p => p.ProvisionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReservedPort);

        // Default: nothing answers on the cached port, so the fast-path probe fails and the
        // factory falls through to provisioning. Individual tests can override this.
        _healthProbe = new Mock<ILightRagHealthProbe>();
        _healthProbe
            .Setup(p => p.IsHealthyAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var c in _created)
            c.Dispose();
        _db.Dispose();
    }

    private LightRagClientFactory NewFactory() =>
        new(new LightRagEfAgentPortStore(_db), _httpFactory.Object, _provisioner.Object, _healthProbe.Object,
            new LightRagContainerAccessTracker(), Options.Create(new LightRagSettings()), NullLoggerFactory.Instance);

    private async Task<Guid> SeedAgentAsync(int? port)
    {
        var id = Guid.NewGuid();
        _db.Agents.Add(new AgentModel { Id = id, Name = "agent", LightRagPort = port });
        await _db.SaveChangesAsync();
        return id;
    }

    [Test]
    public async Task GetClientAsync_WhenNoCachedPort_ProvisionsAndUsesReservedPort()
    {
        var agentId = await SeedAgentAsync(port: null);
        var factory = NewFactory();

        await factory.GetClientAsync(agentId);

        _provisioner.Verify(p => p.ProvisionAsync(agentId, It.IsAny<CancellationToken>()), Times.Once);
        Assert.That(_created[^1].BaseAddress, Is.EqualTo(new Uri($"http://localhost:{ReservedPort}")));
    }

    [Test]
    public async Task GetClientAsync_WhenCachedPortUnreachable_ReprovisionsToAgentsOwnPort()
    {
        // The DB holds a stale/foreign port. The fast-path probe fails, so the factory must
        // re-provision and use the agent's OWN reserved port — never the stale port. This is
        // the regression guard against cross-agent misrouting via a recycled port.
        var agentId = await SeedAgentAsync(port: UnreachablePort);
        var factory = NewFactory();

        await factory.GetClientAsync(agentId);

        _provisioner.Verify(p => p.ProvisionAsync(agentId, It.IsAny<CancellationToken>()), Times.Once);
        Assert.That(_created[^1].BaseAddress, Is.EqualTo(new Uri($"http://localhost:{ReservedPort}")));
    }

    [Test]
    public async Task GetClientAsync_CalledTwiceInScope_ReturnsCachedClientAndProvisionsOnce()
    {
        var agentId = await SeedAgentAsync(port: null);
        var factory = NewFactory();

        var first = await factory.GetClientAsync(agentId);
        var second = await factory.GetClientAsync(agentId);

        Assert.That(second, Is.SameAs(first));
        _provisioner.Verify(p => p.ProvisionAsync(agentId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
