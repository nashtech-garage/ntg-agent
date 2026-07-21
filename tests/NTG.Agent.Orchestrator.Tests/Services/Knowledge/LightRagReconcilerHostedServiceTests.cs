using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NTG.Agent.LightRag;

namespace NTG.Agent.Orchestrator.Tests.Services.Knowledge;

[TestFixture]
public class LightRagReconcilerHostedServiceTests
{
    // Runs one reconciliation pass to completion. StartAsync kicks off the BackgroundService's
    // ExecuteAsync and returns at its first async yield; ExecuteTask is that running pass, which
    // we await to completion (the reconciler ends on its own — no stop token needed).
    private static async Task RunOnceAsync(LightRagReconcilerHostedService svc)
    {
        await svc.StartAsync(CancellationToken.None);
        await (svc.ExecuteTask ?? Task.CompletedTask);
    }

    private static LightRagReconcilerHostedService NewReconciler(
        IServiceProvider sp, ILightRagContainerManager manager, LightRagSettings settings) =>
        new(sp, manager, Options.Create(settings), NullLogger<LightRagReconcilerHostedService>.Instance);

    // Builds the root provider the reconciler creates a scope from. The two seams are optional
    // because the "daemon never reachable" path returns before it ever resolves a scope.
    private static IServiceProvider RootProvider(
        ILightRagAgentPortStore? portStore = null,
        ILightRagProvisioner? provisioner = null)
    {
        var collection = new ServiceCollection();
        if (portStore is not null)
            collection.AddSingleton(portStore);
        if (provisioner is not null)
            collection.AddSingleton(provisioner);
        return collection.BuildServiceProvider();
    }

    [Test]
    public async Task ExecuteAsync_GivesUp_WhenDaemonNeverReachable()
    {
        var manager = new Mock<ILightRagContainerManager>();
        manager.Setup(m => m.IsDaemonReachableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var settings = new LightRagSettings { DaemonProbeTimeoutSeconds = 1, DaemonProbePollIntervalMs = 10 };
        var svc = NewReconciler(RootProvider(), manager.Object, settings);

        await RunOnceAsync(svc);

        // Budget exhausted => never proceeds to image pull / provisioning.
        manager.Verify(m => m.EnsureImagePulledAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_ReconcilesOnceDaemonBecomesReachable()
    {
        var manager = new Mock<ILightRagContainerManager>();
        var pings = 0;
        // Unreachable for the first two polls, then the tunnel comes up.
        manager.Setup(m => m.IsDaemonReachableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++pings >= 3);
        manager.Setup(m => m.EnsureImagePulledAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var portStore = new Mock<ILightRagAgentPortStore>();
        portStore.Setup(p => p.GetAgentIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Guid>)Array.Empty<Guid>());
        var provisioner = new Mock<ILightRagProvisioner>();

        var settings = new LightRagSettings { DaemonProbeTimeoutSeconds = 5, DaemonProbePollIntervalMs = 10 };
        var svc = NewReconciler(RootProvider(portStore.Object, provisioner.Object), manager.Object, settings);

        await RunOnceAsync(svc);

        Assert.That(pings, Is.EqualTo(3));
        manager.Verify(m => m.EnsureImagePulledAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
