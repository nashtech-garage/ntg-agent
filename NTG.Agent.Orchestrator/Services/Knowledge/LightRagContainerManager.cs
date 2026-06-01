using System.Net;
using System.Net.Sockets;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Options;
using NTG.Agent.Orchestrator.Models.Configuration;

namespace NTG.Agent.Orchestrator.Services.Knowledge;

/// <summary>
/// Docker.DotNet-backed implementation of <see cref="ILightRagContainerManager"/>.
/// Spins up one lightweight LightRAG app container per agent, all joined to the same
/// Docker network as the shared <c>lightrag-postgres</c> and isolated by WORKSPACE.
/// </summary>
public sealed class LightRagContainerManager : ILightRagContainerManager, IDisposable
{
    private const string ContainerPort = "9621/tcp";

    private readonly IDockerClient _docker;
    private readonly LightRagSettings _settings;
    private readonly ILogger<LightRagContainerManager> _logger;
    // Serialize create/teardown so two concurrent agent creates can't grab the same free port.
    private readonly SemaphoreSlim _gate = new(1, 1);

    public LightRagContainerManager(IOptions<LightRagSettings> settings, ILogger<LightRagContainerManager> logger)
    {
        _settings = settings.Value;
        _logger = logger;
        _docker = new DockerClientConfiguration().CreateClient();
    }

    private string ImageName => $"{_settings.ImageRef}:{_settings.ImageTag}";

    private static string ContainerName(Guid agentId) => $"lightrag-agent-{agentId}";

    // LightRAG WORKSPACE scopes every row it writes in the shared Postgres tables.
    // Use the dash-less GUID so it is a safe identifier in any backend.
    private static string Workspace(Guid agentId) => agentId.ToString("N");

    public async Task EnsureImagePulledAsync(CancellationToken cancellationToken = default)
    {
        var existing = await _docker.Images.ListImagesAsync(new ImagesListParameters
        {
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["reference"] = new Dictionary<string, bool> { [ImageName] = true }
            }
        }, cancellationToken);

        if (existing.Count > 0)
        {
            _logger.LogInformation("LightRagContainerManager: image {Image} already present.", ImageName);
            return;
        }

        _logger.LogInformation("LightRagContainerManager: pulling image {Image} (first run can take a few minutes)...", ImageName);
        await _docker.Images.CreateImageAsync(
            new ImagesCreateParameters { FromImage = _settings.ImageRef, Tag = _settings.ImageTag },
            authConfig: null,
            new Progress<JSONMessage>(),
            cancellationToken);
        _logger.LogInformation("LightRagContainerManager: pulled image {Image}.", ImageName);
    }

    public async Task<int> EnsureContainerAsync(Guid agentId, int? desiredPort, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureImagePulledAsync(cancellationToken);

            var name = ContainerName(agentId);
            var existing = await FindContainerAsync(name, cancellationToken);

            if (existing is not null)
            {
                var inspect = await _docker.Containers.InspectContainerAsync(existing.ID, cancellationToken);
                if (inspect.State?.Running == true)
                {
                    var runningPort = ReadPublishedPort(inspect);
                    if (runningPort is not null)
                    {
                        _logger.LogInformation("LightRagContainerManager: {Name} already running on :{Port}.", name, runningPort);
                        return runningPort.Value;
                    }
                }

                // Exists but stopped or port unreadable — remove and recreate cleanly.
                _logger.LogInformation("LightRagContainerManager: removing stale container {Name} before recreate.", name);
                await _docker.Containers.RemoveContainerAsync(existing.ID, new ContainerRemoveParameters { Force = true }, cancellationToken);
            }

            var port = desiredPort is > 0 && IsPortFree(desiredPort.Value) ? desiredPort.Value : FindFreePort();
            var networkName = await ResolvePostgresNetworkAsync(cancellationToken);

            var create = await _docker.Containers.CreateContainerAsync(new CreateContainerParameters
            {
                Name = name,
                Image = ImageName,
                Env = BuildEnv(agentId),
                ExposedPorts = new Dictionary<string, EmptyStruct> { [ContainerPort] = default },
                HostConfig = new HostConfig
                {
                    NetworkMode = networkName,
                    PortBindings = new Dictionary<string, IList<PortBinding>>
                    {
                        [ContainerPort] = new List<PortBinding> { new() { HostIP = "127.0.0.1", HostPort = port.ToString() } }
                    },
                    RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.UnlessStopped }
                }
            }, cancellationToken);

            await _docker.Containers.StartContainerAsync(create.ID, new ContainerStartParameters(), cancellationToken);
            _logger.LogInformation("LightRagContainerManager: created {Name} on :{Port} (network {Network}, workspace {Workspace}).",
                name, port, networkName, Workspace(agentId));
            return port;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAndRemoveContainerAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var name = ContainerName(agentId);
            var existing = await FindContainerAsync(name, cancellationToken);
            if (existing is null)
            {
                _logger.LogInformation("LightRagContainerManager: no container {Name} to remove.", name);
                return;
            }

            try
            {
                await _docker.Containers.StopContainerAsync(existing.ID, new ContainerStopParameters { WaitBeforeKillSeconds = 10 }, cancellationToken);
            }
            catch (DockerApiException ex)
            {
                _logger.LogWarning(ex, "LightRagContainerManager: stop {Name} failed (continuing to remove).", name);
            }

            await _docker.Containers.RemoveContainerAsync(existing.ID, new ContainerRemoveParameters { Force = true }, cancellationToken);
            _logger.LogInformation("LightRagContainerManager: removed container {Name}.", name);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ContainerListResponse?> FindContainerAsync(string name, CancellationToken ct)
    {
        var all = await _docker.Containers.ListContainersAsync(new ContainersListParameters { All = true }, ct);
        // Docker prefixes names with '/'; match exactly to avoid prefix collisions.
        return all.FirstOrDefault(c => c.Names.Any(n => n.TrimStart('/') == name));
    }

    private async Task<string> ResolvePostgresNetworkAsync(CancellationToken ct)
    {
        var all = await _docker.Containers.ListContainersAsync(new ContainersListParameters { All = true }, ct);
        var pg = all.FirstOrDefault(c => c.Names.Any(n => n.Contains(_settings.PostgresHostAlias)));
        if (pg is null)
            throw new InvalidOperationException(
                $"Could not find the shared '{_settings.PostgresHostAlias}' container — is the AppHost running?");

        var inspect = await _docker.Containers.InspectContainerAsync(pg.ID, ct);
        var network = inspect.NetworkSettings?.Networks?.Keys.FirstOrDefault();
        if (string.IsNullOrEmpty(network))
            throw new InvalidOperationException($"'{_settings.PostgresHostAlias}' is not attached to any Docker network.");

        return network;
    }

    private List<string> BuildEnv(Guid agentId) =>
    [
        "LIGHTRAG_KV_STORAGE=PGKVStorage",
        "LIGHTRAG_VECTOR_STORAGE=PGVectorStorage",
        "LIGHTRAG_GRAPH_STORAGE=PGGraphStorage",
        "LIGHTRAG_DOC_STATUS_STORAGE=PGDocStatusStorage",
        $"POSTGRES_HOST={_settings.PostgresHostAlias}",
        "POSTGRES_PORT=5432",
        "POSTGRES_USER=postgres",
        $"POSTGRES_PASSWORD={_settings.PostgresPassword}",
        $"POSTGRES_DATABASE={_settings.PostgresDatabase}",
        // The isolation boundary: every row this container writes is scoped to this workspace.
        $"WORKSPACE={Workspace(agentId)}",
        "LLM_BINDING=azure_openai",
        $"LLM_MODEL={_settings.LlmModel}",
        $"LLM_BINDING_HOST={_settings.LlmEndpoint}",
        $"LLM_BINDING_API_KEY={_settings.LlmApiKey}",
        $"AZURE_OPENAI_API_VERSION={_settings.AzureApiVersion}",
        "EMBEDDING_BINDING=azure_openai",
        $"EMBEDDING_MODEL={_settings.EmbeddingModel}",
        $"EMBEDDING_BINDING_HOST={_settings.EmbeddingEndpoint}",
        $"EMBEDDING_BINDING_API_KEY={_settings.EmbeddingApiKey}",
        $"EMBEDDING_DIM={_settings.EmbeddingDim}",
        $"AZURE_EMBEDDING_API_VERSION={_settings.AzureApiVersion}",
        $"CHUNK_SIZE={_settings.ChunkSize}",
        $"CHUNK_OVERLAP_SIZE={_settings.ChunkOverlap}",
        $"MAX_ASYNC={_settings.MaxAsync}",
        $"MAX_PARALLEL_INSERT={_settings.MaxParallelInsert}",
        $"EMBEDDING_FUNC_MAX_ASYNC={_settings.EmbeddingFuncMaxAsync}",
        $"LIGHTRAG_API_KEY={_settings.ApiKey}",
    ];

    private static int? ReadPublishedPort(ContainerInspectResponse inspect)
    {
        if (inspect.HostConfig?.PortBindings != null
            && inspect.HostConfig.PortBindings.TryGetValue(ContainerPort, out var bindings)
            && bindings.Count > 0
            && int.TryParse(bindings[0].HostPort, out var port))
        {
            return port;
        }

        if (inspect.NetworkSettings?.Ports != null
            && inspect.NetworkSettings.Ports.TryGetValue(ContainerPort, out var mapped)
            && mapped is { Count: > 0 }
            && int.TryParse(mapped[0].HostPort, out var mappedPort))
        {
            return mappedPort;
        }

        return null;
    }

    private static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static bool IsPortFree(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    public void Dispose() => _docker.Dispose();
}
