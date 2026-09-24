using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NTG.Agent.LightRag;

namespace NTG.Agent.Orchestrator.Tests.Services.Knowledge;

[TestFixture]
public class LightRagClientFactoryTests
{
    private Mock<IHttpClientFactory> _httpFactory = null!;
    private Mock<ILightRagContainerManager> _containerManager = null!;
    private Mock<ILightRagHealthProbe> _healthProbe = null!;
    // The factory asks IHttpClientFactory for the real per-agent client (the reachability
    // probe is delegated to ILightRagHealthProbe) — keep the created clients so the test
    // can assert the resulting client's BaseAddress.
    private List<HttpClient> _created = null!;

    [SetUp]
    public void Setup()
    {
        _created = [];
        _httpFactory = new Mock<IHttpClientFactory>();
        _httpFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => { var c = new HttpClient(); _created.Add(c); return c; });

        _containerManager = new Mock<ILightRagContainerManager>();

        // Default: the agent's container does not answer through the gateway, so the factory
        // falls through to ensuring the container. Individual tests can override this.
        _healthProbe = new Mock<ILightRagHealthProbe>();
        _healthProbe
            .Setup(p => p.IsHealthyAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var c in _created)
            c.Dispose();
    }

    private LightRagClientFactory NewFactory(LightRagSettings? settings = null) =>
        new(_httpFactory.Object, _containerManager.Object, _healthProbe.Object,
            new LightRagContainerAccessTracker(), Options.Create(settings ?? new LightRagSettings()), NullLoggerFactory.Instance);

    [Test]
    public async Task GetClientAsync_WhenContainerNotServing_EnsuresContainerAndTargetsAgentPath()
    {
        var agentId = Guid.NewGuid();
        var factory = NewFactory();

        await factory.GetClientAsync(agentId);

        _containerManager.Verify(m => m.EnsureContainerAsync(agentId, It.IsAny<CancellationToken>()), Times.Once);
        Assert.That(_created[^1].BaseAddress, Is.EqualTo(new Uri($"http://localhost:8080/agents/{agentId}/")));
    }

    [Test]
    public async Task GetClientAsync_WhenContainerServing_SkipsEnsure()
    {
        // The gateway routes by container name, so a healthy answer on the agent's path is
        // provably this agent's own container — no container work needed.
        var agentId = Guid.NewGuid();
        _healthProbe
            .Setup(p => p.IsHealthyAsync(agentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var factory = NewFactory();

        await factory.GetClientAsync(agentId);

        _containerManager.Verify(m => m.EnsureContainerAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.That(_created[^1].BaseAddress, Is.EqualTo(new Uri($"http://localhost:8080/agents/{agentId}/")));
    }

    [Test]
    public async Task GetClientAsync_WhenGatewayUrlConfigured_TargetsThatGateway()
    {
        // The remote gateway is dialled directly over TLS, so the configured URL must reach
        // the client rather than the local-dev default.
        var agentId = Guid.NewGuid();
        var factory = NewFactory(new LightRagSettings { GatewayUrl = "https://4.193.109.6" });

        await factory.GetClientAsync(agentId);

        Assert.That(_created[^1].BaseAddress, Is.EqualTo(new Uri($"https://4.193.109.6/agents/{agentId}/")));
    }

    [Test]
    public async Task GetClientAsync_CalledTwiceInScope_ReturnsCachedClientAndEnsuresOnce()
    {
        var agentId = Guid.NewGuid();
        var factory = NewFactory();

        var first = await factory.GetClientAsync(agentId);
        var second = await factory.GetClientAsync(agentId);

        Assert.That(second, Is.SameAs(first));
        _containerManager.Verify(m => m.EnsureContainerAsync(agentId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
