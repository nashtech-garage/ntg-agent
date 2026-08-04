using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NTG.Agent.LightRag;

namespace NTG.Agent.Orchestrator.Tests.Services.Knowledge;

[TestFixture]
public class LightRagContainerManagerTests
{
    // A probe that always reports the container is serving, so the readiness gate returns on
    // the first poll. Tests that exercise the not-ready path supply their own probe.
    private static ILightRagHealthProbe HealthyProbe()
    {
        var probe = new Mock<ILightRagHealthProbe>();
        probe.Setup(p => p.IsHealthyAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return probe.Object;
    }

    private static LightRagContainerManager NewManager(IDockerClient docker, LightRagSettings? settings = null, ILightRagHealthProbe? healthProbe = null) =>
        new(docker, healthProbe ?? HealthyProbe(), Options.Create(settings ?? new LightRagSettings()), NullLogger<LightRagContainerManager>.Instance);

    private static ContainerListResponse PgContainer() =>
        new() { ID = "pg", Names = new List<string> { "/lightrag-postgres" } };

    private static ContainerListResponse GatewayContainer() =>
        new() { ID = "gw", Names = new List<string> { "/lightrag-gateway" } };

    // Builds a mocked Docker client whose image/network bootstrap succeeds and whose
    // container list is supplied by the test. Returns the container-operations mock so
    // tests can add/verify create/start/inspect behaviour.
    private static (Mock<IDockerClient> docker, Mock<IContainerOperations> containers) BuildDocker(IList<ContainerListResponse> containerList)
    {
        var images = new Mock<IImageOperations>();
        images.Setup(i => i.ListImagesAsync(It.IsAny<ImagesListParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IList<ImagesListResponse>)new List<ImagesListResponse> { new() });

        var networks = new Mock<INetworkOperations>();
        networks.Setup(n => n.ListNetworksAsync(It.IsAny<NetworksListParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IList<NetworkResponse>)new List<NetworkResponse> { new() { Name = "ntg-agent-lightrag" } });
        networks.Setup(n => n.ConnectNetworkAsync(It.IsAny<string>(), It.IsAny<NetworkConnectParameters>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var containers = new Mock<IContainerOperations>();
        containers.Setup(c => c.ListContainersAsync(It.IsAny<ContainersListParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(containerList);
        containers.Setup(c => c.RemoveContainerAsync(It.IsAny<string>(), It.IsAny<ContainerRemoveParameters>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // The daemon-reachability guard pings before any container work; a reachable daemon by default.
        var system = new Mock<ISystemOperations>();
        system.Setup(s => s.PingAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var docker = new Mock<IDockerClient>();
        docker.Setup(d => d.System).Returns(system.Object);
        docker.Setup(d => d.Images).Returns(images.Object);
        docker.Setup(d => d.Networks).Returns(networks.Object);
        docker.Setup(d => d.Containers).Returns(containers.Object);
        return (docker, containers);
    }

    private static ContainerInspectResponse BuildInspect(bool running, bool onNetwork, IList<string> env) =>
        new()
        {
            State = new ContainerState { Running = running },
            Config = new Config { Env = env },
            NetworkSettings = new NetworkSettings
            {
                Networks = onNetwork
                    ? new Dictionary<string, EndpointSettings> { ["ntg-agent-lightrag"] = new EndpointSettings() }
                    : new Dictionary<string, EndpointSettings>()
            }
        };

    // The tracked env keys the manager compares (mirrors LightRagContainerManager.BuildEnv),
    // so a healthy container shows no env drift.
    private static List<string> TrackedEnv(LightRagSettings s) =>
    [
        $"EMBEDDING_DIM={s.EmbeddingDim}",
        $"EMBEDDING_SEND_DIM={s.EmbeddingSendDim.ToString().ToLowerInvariant()}",
        $"EMBEDDING_MODEL={s.EmbeddingModel}",
        $"EMBEDDING_BINDING_HOST={s.EmbeddingEndpoint}",
        $"CHUNK_SIZE={s.ChunkSize}",
        $"CHUNK_OVERLAP_SIZE={s.ChunkOverlap}",
        $"MAX_ASYNC={s.MaxAsync}",
        $"MAX_PARALLEL_INSERT={s.MaxParallelInsert}",
        $"LLM_MODEL={s.LlmModel}",
        $"LLM_BINDING_HOST={s.LlmEndpoint}",
    ];

    [Test]
    public async Task EnsureContainerAsync_CreatesContainer_WithoutHostPortsOrSsl()
    {
        // The gateway reaches the container by name over the network, so the container must
        // publish no host ports and serve plain HTTP (no SSL_* vars, no cert mounts).
        var (docker, containers) = BuildDocker([PgContainer(), GatewayContainer()]);
        containers.Setup(c => c.CreateContainerAsync(It.IsAny<CreateContainerParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateContainerResponse { ID = "new" });
        containers.Setup(c => c.StartContainerAsync("new", It.IsAny<ContainerStartParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var manager = NewManager(docker.Object);
        await manager.EnsureContainerAsync(Guid.NewGuid());

        containers.Verify(c => c.CreateContainerAsync(
            It.Is<CreateContainerParameters>(p =>
                p.HostConfig.PortBindings == null
                && p.HostConfig.Binds == null
                && !p.Env.Any(e => e.StartsWith("SSL", StringComparison.Ordinal))),
            It.IsAny<CancellationToken>()), Times.Once);
        containers.Verify(c => c.StartContainerAsync("new", It.IsAny<ContainerStartParameters>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public void EnsureContainerAsync_Throws_WhenGatewayContainerMissing()
    {
        // Without the gateway, agent containers are unreachable — fail with a clear pointer
        // to the compose stack instead of timing out in the readiness gate.
        var (docker, containers) = BuildDocker([PgContainer()]);

        var manager = NewManager(docker.Object);

        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.EnsureContainerAsync(Guid.NewGuid()));
        Assert.That(ex!.Message, Does.Contain("lightrag-gateway"));
        containers.Verify(c => c.CreateContainerAsync(It.IsAny<CreateContainerParameters>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task EnsureContainerAsync_ReusesHealthyContainer()
    {
        var agentId = Guid.NewGuid();
        var agentContainer = new ContainerListResponse { ID = "agent-cid", Names = new List<string> { $"/lightrag-agent-{agentId}" } };
        var (docker, containers) = BuildDocker([PgContainer(), GatewayContainer(), agentContainer]);
        var settings = new LightRagSettings();
        containers.Setup(c => c.InspectContainerAsync("agent-cid", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildInspect(running: true, onNetwork: true, env: TrackedEnv(settings)));

        var manager = NewManager(docker.Object, settings);
        await manager.EnsureContainerAsync(agentId);

        containers.Verify(c => c.CreateContainerAsync(It.IsAny<CreateContainerParameters>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task EnsureContainerAsync_RecreatesContainer_WhenStaleSslEnvPresent()
    {
        // A container created before the gateway carries SSL=true and serves HTTPS, which the
        // gateway cannot proxy — it must be detected as drifted and recreated, even though the
        // desired env no longer contains any SSL key.
        var agentId = Guid.NewGuid();
        var agentContainer = new ContainerListResponse { ID = "agent-cid", Names = new List<string> { $"/lightrag-agent-{agentId}" } };
        var (docker, containers) = BuildDocker([PgContainer(), GatewayContainer(), agentContainer]);
        var settings = new LightRagSettings();
        var staleEnv = TrackedEnv(settings).Concat(["SSL=true"]).ToList();
        containers.Setup(c => c.InspectContainerAsync("agent-cid", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildInspect(running: true, onNetwork: true, env: staleEnv));
        containers.Setup(c => c.CreateContainerAsync(It.IsAny<CreateContainerParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateContainerResponse { ID = "new" });
        containers.Setup(c => c.StartContainerAsync("new", It.IsAny<ContainerStartParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var manager = NewManager(docker.Object, settings);
        await manager.EnsureContainerAsync(agentId);

        containers.Verify(c => c.RemoveContainerAsync("agent-cid", It.IsAny<ContainerRemoveParameters>(), It.IsAny<CancellationToken>()), Times.Once);
        containers.Verify(c => c.CreateContainerAsync(It.IsAny<CreateContainerParameters>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task EnsureContainerAsync_WaitsForReadiness_BeforeReturning()
    {
        // The container is present/healthy in Docker but its app is not serving for the first
        // two probes — EnsureContainerAsync must not return until the health probe answers.
        var agentId = Guid.NewGuid();
        var agentContainer = new ContainerListResponse { ID = "agent-cid", Names = new List<string> { $"/lightrag-agent-{agentId}" } };
        var (docker, containers) = BuildDocker([PgContainer(), GatewayContainer(), agentContainer]);
        var settings = new LightRagSettings { ReadinessPollIntervalMs = 10 };
        containers.Setup(c => c.InspectContainerAsync("agent-cid", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildInspect(running: true, onNetwork: true, env: TrackedEnv(settings)));

        var probeCalls = 0;
        var probe = new Mock<ILightRagHealthProbe>();
        probe.Setup(p => p.IsHealthyAsync(agentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++probeCalls >= 3); // not-ready for the first two polls, then serving

        var manager = NewManager(docker.Object, settings, probe.Object);
        await manager.EnsureContainerAsync(agentId);

        Assert.That(probeCalls, Is.EqualTo(3));
    }

    [Test]
    public void EnsureContainerAsync_ThrowsDaemonUnavailable_WhenDaemonUnreachable()
    {
        // The pre-flight daemon ping fails. The manager must surface a clean typed exception
        // and never attempt any container work.
        var (docker, containers) = BuildDocker([PgContainer(), GatewayContainer()]);
        var system = new Mock<ISystemOperations>();
        system.Setup(s => s.PingAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));
        docker.Setup(d => d.System).Returns(system.Object);

        var manager = NewManager(docker.Object);

        Assert.ThrowsAsync<LightRagDaemonUnavailableException>(
            () => manager.EnsureContainerAsync(Guid.NewGuid()));
        containers.Verify(c => c.CreateContainerAsync(It.IsAny<CreateContainerParameters>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task IsDaemonReachableAsync_ReturnsFalse_WhenPingThrows()
    {
        var (docker, _) = BuildDocker([PgContainer(), GatewayContainer()]);
        var system = new Mock<ISystemOperations>();
        system.Setup(s => s.PingAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));
        docker.Setup(d => d.System).Returns(system.Object);

        var manager = NewManager(docker.Object);

        Assert.That(await manager.IsDaemonReachableAsync(), Is.False);
    }

    [Test]
    public void EnsureContainerAsync_ThrowsNotReady_WhenAppNeverServes()
    {
        // Docker reports the container up, but its app never answers the health probe — the
        // readiness gate must give up after the budget and throw rather than hand back a dead
        // endpoint.
        var agentId = Guid.NewGuid();
        var agentContainer = new ContainerListResponse { ID = "agent-cid", Names = new List<string> { $"/lightrag-agent-{agentId}" } };
        var (docker, _) = BuildDocker([PgContainer(), GatewayContainer(), agentContainer]);
        var settings = new LightRagSettings { ReadinessTimeoutSeconds = 1, ReadinessPollIntervalMs = 10 };
        var containers = docker.Object.Containers;
        Mock.Get(containers).Setup(c => c.InspectContainerAsync("agent-cid", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildInspect(running: true, onNetwork: true, env: TrackedEnv(settings)));

        var probe = new Mock<ILightRagHealthProbe>();
        probe.Setup(p => p.IsHealthyAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var manager = NewManager(docker.Object, settings, probe.Object);

        Assert.ThrowsAsync<LightRagContainerNotReadyException>(
            () => manager.EnsureContainerAsync(agentId));
    }
}
