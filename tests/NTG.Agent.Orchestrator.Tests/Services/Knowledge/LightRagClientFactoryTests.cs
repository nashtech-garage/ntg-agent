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
    private Mock<ILightRagAgentStore> _agentStore = null!;
    private LightRagContainerAccessTracker _accessTracker = null!;
    // The factory asks IHttpClientFactory for the real per-knowledge-base client (the reachability
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
        _accessTracker = new LightRagContainerAccessTracker();

        // Default: the container does not answer through the gateway, so the factory
        // falls through to ensuring the container. Individual tests can override this.
        _healthProbe = new Mock<ILightRagHealthProbe>();
        _healthProbe
            .Setup(p => p.IsHealthyAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Default: every agent owns its own knowledge base, which is how agents that predate
        // knowledge-base sharing behave. Tests that exercise sharing override this per agent.
        _agentStore = new Mock<ILightRagAgentStore>();
        _agentStore
            .Setup(s => s.GetKnowledgeOwnerAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => id);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var c in _created)
            c.Dispose();
    }

    private LightRagClientFactory NewFactory(LightRagSettings? settings = null) =>
        new(_httpFactory.Object, _containerManager.Object, _healthProbe.Object,
            _accessTracker, new LightRagWorkspaceResolver(_agentStore.Object),
            Options.Create(settings ?? new LightRagSettings()), NullLoggerFactory.Instance);

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

    [Test]
    public async Task GetClientAsync_WhenAgentIsGuest_TargetsOwnersPathAndContainer()
    {
        // A guest shares the owner's container and workspace, so every hop has to be keyed on the
        // owner: dialing the guest's own path would reach a container that does not exist, and
        // ensuring one under the guest's id would create a second container, silently splitting
        // the two agents apart.
        var ownerAgentId = Guid.NewGuid();
        var guestAgentId = Guid.NewGuid();
        _agentStore
            .Setup(s => s.GetKnowledgeOwnerAsync(guestAgentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ownerAgentId);
        var factory = NewFactory();

        await factory.GetClientAsync(guestAgentId);

        Assert.Multiple(() =>
        {
            Assert.That(_created[^1].BaseAddress, Is.EqualTo(new Uri($"http://localhost:8080/agents/{ownerAgentId}/")));
            // Idle shutdown must count the guest's traffic against the owner's container, or the
            // container gets reclaimed while a guest is still actively using it.
            Assert.That(_accessTracker.GetLastAccess(ownerAgentId), Is.Not.Null);
            Assert.That(_accessTracker.GetLastAccess(guestAgentId), Is.Null);
        });
        _containerManager.Verify(m => m.EnsureContainerAsync(ownerAgentId, It.IsAny<CancellationToken>()), Times.Once);
        _containerManager.Verify(m => m.EnsureContainerAsync(guestAgentId, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task GetClientAsync_WhenTwoAgentsShareAKnowledgeBase_ReuseOneClientAndOneContainer()
    {
        var ownerAgentId = Guid.NewGuid();
        var guestAgentId = Guid.NewGuid();
        _agentStore
            .Setup(s => s.GetKnowledgeOwnerAsync(guestAgentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ownerAgentId);
        var factory = NewFactory();

        var viaOwner = await factory.GetClientAsync(ownerAgentId);
        var viaGuest = await factory.GetClientAsync(guestAgentId);

        // The scope cache is keyed by knowledge base, not by caller — that is what makes sharing
        // cost one container rather than one per agent.
        Assert.That(viaGuest, Is.SameAs(viaOwner));
        _containerManager.Verify(m => m.EnsureContainerAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public void GetClientAsync_WhenAgentHasNoKnowledgeBase_Throws()
    {
        // Inner agents have no knowledge base, so there is no container to dial. Fail with a
        // named cause rather than building a request against a nonsense URL.
        var innerAgentId = Guid.NewGuid();
        _agentStore
            .Setup(s => s.GetKnowledgeOwnerAsync(innerAgentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);
        var factory = NewFactory();

        Assert.ThrowsAsync<InvalidOperationException>(() => factory.GetClientAsync(innerAgentId));
        _containerManager.Verify(m => m.EnsureContainerAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
