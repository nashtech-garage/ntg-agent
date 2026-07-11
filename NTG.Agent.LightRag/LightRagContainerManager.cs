using Microsoft.Extensions.Logging;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Options;
using Npgsql;

namespace NTG.Agent.LightRag;

/// <summary>
/// Docker.DotNet-backed implementation of <see cref="ILightRagContainerManager"/>.
/// Spins up one lightweight LightRAG app container per agent, all joined to the same
/// Docker network as the shared <c>lightrag-postgres</c> and isolated by WORKSPACE.
/// </summary>
public sealed class LightRagContainerManager : ILightRagContainerManager, IDisposable
{
    private const string ContainerPort = "9621/tcp";

    private readonly IDockerClient _docker;
    private readonly ILightRagHealthProbe _healthProbe;
    private readonly LightRagSettings _settings;
    private readonly ILogger<LightRagContainerManager> _logger;
    // Serialize create/teardown so two concurrent agent creates can't grab the same free port.
    private readonly SemaphoreSlim _gate = new(1, 1);

    // IDockerClient is injected (built from LightRagSettings.DockerHost in DI) so the
    // manager can be unit-tested with a mocked daemon and is not tied to a concrete
    // client construction (DIP). ILightRagHealthProbe is likewise injected so the readiness
    // gate can be exercised without real HTTP.
    public LightRagContainerManager(IDockerClient docker, ILightRagHealthProbe healthProbe, IOptions<LightRagSettings> settings, ILogger<LightRagContainerManager> logger)
    {
        _docker = docker;
        _healthProbe = healthProbe;
        _settings = settings.Value;
        _logger = logger;
    }

    private string ImageName => $"{_settings.ImageRef}:{_settings.ImageTag}";

    private static string ContainerName(Guid agentId) => $"lightrag-agent-{agentId}";

    // LightRAG WORKSPACE scopes every row it writes in the shared Postgres tables.
    // Use the dash-less GUID so it is a safe identifier in any backend.
    private static string Workspace(Guid agentId) => $"w{agentId:N}";

    // Cheap round-trip that proves the daemon is reachable before we attempt the heavier
    // image/container operations. Catches connectivity failures (SSH tunnel down => refused)
    // and reports not-reachable rather than throwing — same "catch => not-ready" shape as the
    // container health probe. The caller's cancellation is surfaced by the caller.
    public async Task<bool> IsDaemonReachableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _docker.System.PingAsync(cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

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

    public async Task<int> EnsureContainerAsync(Guid agentId, int hostPort, CancellationToken cancellationToken = default)
    {
        int port;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            port = await EnsureContainerCoreAsync(agentId, hostPort, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        // The published host port is bound the instant Docker starts the container, but the
        // LightRAG ASGI app inside is still booting — a request sent now races that boot and the
        // connection is dropped ("response ended prematurely"). Wait for the app to actually
        // serve before returning. Polled OUTSIDE _gate so one agent's cold boot does not
        // serialize every other agent's container operations behind it; by this point the port
        // is already bound, so the port-race _gate guards is over.
        await WaitUntilReadyAsync(port, ContainerName(agentId), cancellationToken);
        return port;
    }

    // The port-allocation-sensitive create/start/inspect flow, run under _gate. Returns the
    // agent's live published host port; readiness is awaited by the caller after the gate is
    // released.
    private async Task<int> EnsureContainerCoreAsync(Guid agentId, int hostPort, CancellationToken cancellationToken)
    {
        // Fail fast with a clean, typed error if the daemon (SSH tunnel) is down, instead of
        // letting the raw Docker.DotNet/SocketException leak from the first Docker call below to
        // the chat/upload caller. LightRagProvisioner only catches PortReservationConflictException,
        // so this propagates cleanly.
        if (!await IsDaemonReachableAsync(cancellationToken))
            throw new LightRagDaemonUnavailableException(_settings.DockerHost);

        await EnsureImagePulledAsync(cancellationToken);
        var network = await EnsureSharedNetworkAsync(cancellationToken);

        var name = ContainerName(agentId);
        var existing = await FindContainerAsync(name, cancellationToken);

        if (existing is not null)
        {
            var inspect = await _docker.Containers.InspectContainerAsync(existing.ID, cancellationToken);
            var onNetwork = inspect.NetworkSettings?.Networks?.ContainsKey(network) == true;
            var runningPort = inspect.State?.Running == true ? ReadPublishedPort(inspect) : null;

            // Healthy only if it is attached to the shared network AND published on the
            // agent's reserved port AND its env matches. A detached/crash-looping container,
            // one on the wrong port, or one with drifted env all fail this and are recreated.
            if (onNetwork && runningPort is not null)
            {
                var desiredEnv = BuildEnv(agentId);
                var currentEnv = inspect.Config?.Env ?? [];
                var driftedKeys = FindEnvDrift(currentEnv, desiredEnv);
                var portMatches = runningPort.Value == hostPort;

                if (portMatches && driftedKeys.Count == 0)
                {
                    _logger.LogInformation("LightRagContainerManager: {Name} healthy on :{Port}.", name, hostPort);
                    return hostPort;
                }

                if (!portMatches)
                    _logger.LogInformation("LightRagContainerManager: {Name} on :{Old} but reserved :{Reserved}, recreating.", name, runningPort, hostPort);
                if (driftedKeys.Count > 0)
                    _logger.LogInformation("LightRagContainerManager: env drift on {Name} (keys: {Keys}), recreating.", name, string.Join(", ", driftedKeys));

                // EMBEDDING_DIM change means the PGVector columns are wrong dimension — wipe
                // the workspace vector data so LightRAG rebuilds the index at correct size.
                if (driftedKeys.Contains("EMBEDDING_DIM"))
                {
                    await ResetVectorSchemaAsync(agentId, cancellationToken);
                }
            }
            else
            {
                _logger.LogInformation("LightRagContainerManager: recreating {Name} (onNetwork={OnNetwork}, livePort={Port}).", name, onNetwork, runningPort);
            }

            await _docker.Containers.RemoveContainerAsync(existing.ID, new ContainerRemoveParameters { Force = true }, cancellationToken);
        }

        // Bind to the agent's reserved port exactly. A conflict here means an external
        // process holds the port — surfaced as PortReservationConflictException so the
        // caller can reassign. We never silently pick another port (that recycling is
        // what allowed cross-agent misrouting).
        var containerId = await CreateAndStartContainerAsync(name, agentId, network, hostPort, cancellationToken);

        var started = await _docker.Containers.InspectContainerAsync(containerId, cancellationToken);
        var publishedPort = ReadPublishedPort(started)
            ?? throw new InvalidOperationException($"LightRAG container {name} started but no published host port was found.");

        _logger.LogInformation("LightRagContainerManager: created {Name} on {Host}:{Port} (network {Network}, workspace {Workspace}).",
            name, _settings.ServerHost, publishedPort, network, Workspace(agentId));
        return publishedPort;
    }

    // Polls GET /health until the container's app answers, or throws once the readiness
    // budget is exhausted. A container reused on the fast path is already serving, so this
    // returns on the first probe; a freshly-started one is waited out through its boot.
    private async Task WaitUntilReadyAsync(int port, string name, CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(Math.Max(1, _settings.ReadinessTimeoutSeconds));
        var interval = TimeSpan.FromMilliseconds(Math.Max(50, _settings.ReadinessPollIntervalMs));
        var deadline = DateTime.UtcNow + timeout;
        var attempts = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempts++;
            if (await _healthProbe.IsHealthyAsync(port, cancellationToken))
            {
                _logger.LogInformation("LightRagContainerManager: {Name} ready on :{Port} after {Attempts} probe(s).", name, port, attempts);
                return;
            }

            if (DateTime.UtcNow >= deadline)
                throw new LightRagContainerNotReadyException(name, port, timeout);

            await Task.Delay(interval, cancellationToken);
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

    public async Task StopContainerAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var name = ContainerName(agentId);
            var existing = await FindContainerAsync(name, cancellationToken);
            if (existing is null)
            {
                _logger.LogInformation("LightRagContainerManager: no container {Name} to stop.", name);
                return;
            }

            var inspect = await _docker.Containers.InspectContainerAsync(existing.ID, cancellationToken);
            if (inspect.State?.Running != true)
            {
                _logger.LogInformation("LightRagContainerManager: {Name} is already stopped.", name);
                return;
            }

            try
            {
                await _docker.Containers.StopContainerAsync(existing.ID, new ContainerStopParameters { WaitBeforeKillSeconds = 10 }, cancellationToken);
                _logger.LogInformation("LightRagContainerManager: stopped idle container {Name}.", name);
            }
            catch (DockerApiException ex)
            {
                _logger.LogWarning(ex, "LightRagContainerManager: stop {Name} failed.", name);
            }
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

    // A stable, manager-owned bridge network that survives AppHost restarts — unlike
    // Aspire's per-session network, which is recreated (new name) every `dotnet run` and
    // would orphan our persistent agent containers. Agent containers live here permanently;
    // the shared Postgres (recreated each session) is reconnected to it on every startup
    // with the alias agents resolve by.
    private const string SharedNetwork = "ntg-agent-lightrag";

    private async Task<string> EnsureSharedNetworkAsync(CancellationToken ct)
    {
        var networks = await _docker.Networks.ListNetworksAsync(new NetworksListParameters
        {
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["name"] = new Dictionary<string, bool> { [SharedNetwork] = true }
            }
        }, ct);

        if (!networks.Any(n => n.Name == SharedNetwork))
        {
            await _docker.Networks.CreateNetworkAsync(new NetworksCreateParameters { Name = SharedNetwork, Driver = "bridge" }, ct);
            _logger.LogInformation("LightRagContainerManager: created shared network {Network}.", SharedNetwork);
        }

        // (Re)connect the shared Postgres to the stable network with the alias agents use.
        var all = await _docker.Containers.ListContainersAsync(new ContainersListParameters { All = true }, ct);
        var pg = all.FirstOrDefault(c => c.Names.Any(n => n.Contains(_settings.PostgresHostAlias)));
        if (pg is null)
            throw new InvalidOperationException(
                $"Could not find the shared '{_settings.PostgresHostAlias}' container — is the AppHost running?");

        try
        {
            await _docker.Networks.ConnectNetworkAsync(SharedNetwork, new NetworkConnectParameters
            {
                Container = pg.ID,
                EndpointConfig = new EndpointSettings { Aliases = [_settings.PostgresHostAlias] }
            }, ct);
            _logger.LogInformation("LightRagContainerManager: connected {Pg} to {Network} as '{Alias}'.",
                pg.Names.FirstOrDefault(), SharedNetwork, _settings.PostgresHostAlias);
        }
        catch (DockerApiException ex) when (ex.Message.Contains("already", StringComparison.OrdinalIgnoreCase))
        {
            // Postgres is already attached to the shared network — nothing to do.
        }

        return SharedNetwork;
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
        // Must be true when EMBEDDING_DIM < the model's native output dimension (e.g. 1536 < 3072
        // for text-embedding-3-large). Without this, LightRAG skips the 'dimensions' parameter in
        // the Azure OpenAI call, gets full 3072-dim vectors back, and the count/reshape mismatch
        // (expected N vectors, got 2×N) is triggered.
        $"EMBEDDING_SEND_DIM={_settings.EmbeddingSendDim.ToString().ToLowerInvariant()}",
        $"AZURE_EMBEDDING_API_VERSION={_settings.AzureApiVersion}",
        $"CHUNK_SIZE={_settings.ChunkSize}",
        $"CHUNK_OVERLAP_SIZE={_settings.ChunkOverlap}",
        $"MAX_ASYNC={_settings.MaxAsync}",
        $"MAX_PARALLEL_INSERT={_settings.MaxParallelInsert}",
        $"EMBEDDING_FUNC_MAX_ASYNC={_settings.EmbeddingFuncMaxAsync}",
        $"LIGHTRAG_API_KEY={_settings.ApiKey}",
    ];

    // Reads the *live* host port from runtime network state. A detached container has an
    // empty NetworkSettings.Ports even if HostConfig.PortBindings still names a port, so we
    // deliberately trust the runtime mapping only — that is what makes the health check catch
    // orphaned containers.
    private static int? ReadPublishedPort(ContainerInspectResponse inspect, string portKey = ContainerPort)
    {
        if (inspect.NetworkSettings?.Ports != null
            && inspect.NetworkSettings.Ports.TryGetValue(portKey, out var mapped)
            && mapped is { Count: > 0 }
            && int.TryParse(mapped[0].HostPort, out var mappedPort))
        {
            return mappedPort;
        }

        return null;
    }

    // Creates and starts the agent container bound to its reserved host port. If the port
    // is already taken on the host (by an external process — never another agent, since
    // ports are identity-bound), the half-created container is removed and a
    // PortReservationConflictException is thrown so the caller can reassign and retry.
    private async Task<string> CreateAndStartContainerAsync(string name, Guid agentId, string network, int hostPort, CancellationToken ct)
    {
        var create = await _docker.Containers.CreateContainerAsync(BuildCreateParameters(name, agentId, network, hostPort), ct);
        try
        {
            await _docker.Containers.StartContainerAsync(create.ID, new ContainerStartParameters(), ct);
            return create.ID;
        }
        catch (DockerApiException ex) when (IsPortConflict(ex))
        {
            _logger.LogWarning(ex, "LightRagContainerManager: reserved port {Port} unavailable for {Name}; caller should reassign.", hostPort, name);
            try
            {
                await _docker.Containers.RemoveContainerAsync(create.ID, new ContainerRemoveParameters { Force = true }, ct);
            }
            catch (DockerApiException removeEx)
            {
                _logger.LogWarning(removeEx, "LightRagContainerManager: failed to remove half-created {Name} after port conflict.", name);
            }
            throw new PortReservationConflictException(agentId, hostPort, ex);
        }
    }

    private CreateContainerParameters BuildCreateParameters(string name, Guid agentId, string network, int hostPort) =>
        new()
        {
            Name = name,
            Image = ImageName,
            Env = BuildEnv(agentId),
            ExposedPorts = new Dictionary<string, EmptyStruct> { [ContainerPort] = default },
            HostConfig = new HostConfig
            {
                NetworkMode = network,
                PortBindings = new Dictionary<string, IList<PortBinding>>
                {
                    // Publish on the server's loopback (PortBindHostIp) so the Orchestrator
                    // reaches it through the SSH SOCKS proxy, never on a public interface.
                    [ContainerPort] = new List<PortBinding>
                    {
                        new()
                        {
                            HostIP = string.IsNullOrWhiteSpace(_settings.PortBindHostIp) ? "127.0.0.1" : _settings.PortBindHostIp,
                            HostPort = hostPort.ToString()
                        }
                    }
                },
                RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.UnlessStopped }
            },
            // Explicitly attach to the shared network at creation. Relying on NetworkMode
            // alone does not reliably connect the container to a user-defined network.
            NetworkingConfig = new NetworkingConfig
            {
                EndpointsConfig = new Dictionary<string, EndpointSettings> { [network] = new EndpointSettings() }
            }
        };

    // Docker surfaces an already-taken host port as a 500 when starting the container.
    private static bool IsPortConflict(DockerApiException ex) =>
        ex.Message.Contains("port is already allocated", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("address already in use", StringComparison.OrdinalIgnoreCase);

    // The env keys we control and that affect container behaviour. Internal Docker env vars
    // (PATH, HOME, etc.) are excluded — we only compare what we set in BuildEnv.
    private static readonly HashSet<string> TrackedEnvKeys =
    [
        "EMBEDDING_DIM", "EMBEDDING_SEND_DIM", "EMBEDDING_MODEL", "EMBEDDING_BINDING_HOST",
        "CHUNK_SIZE", "CHUNK_OVERLAP_SIZE", "MAX_ASYNC", "MAX_PARALLEL_INSERT",
        "LLM_MODEL", "LLM_BINDING_HOST",
    ];

    // Returns the set of env key names where current and desired values differ.
    private static HashSet<string> FindEnvDrift(IList<string> current, IList<string> desired)
    {
        static Dictionary<string, string> Parse(IEnumerable<string> envList) =>
            envList
                .Where(e => e.Contains('='))
                .Select(e => (e[..e.IndexOf('=')], e[(e.IndexOf('=') + 1)..]))
                .Where(kv => TrackedEnvKeys.Contains(kv.Item1))
                .ToDictionary(kv => kv.Item1, kv => kv.Item2);

        var cur = Parse(current);
        var des = Parse(desired);
        var drifted = new HashSet<string>();
        foreach (var (key, desiredVal) in des)
        {
            if (!cur.TryGetValue(key, out var currentVal) || currentVal != desiredVal)
                drifted.Add(key);
        }
        return drifted;
    }

    private async Task ResetVectorSchemaAsync(Guid agentId, CancellationToken ct)
    {
        var workspace = Workspace(agentId);
        // The standalone server Postgres publishes 5432 on the server loopback (reached via
        // an ssh -L forward), so the endpoint is configured (PostgresHost/PostgresPort),
        // not discovered from the daemon.
        var pgHost = string.IsNullOrWhiteSpace(_settings.PostgresHost) ? _settings.ServerHost : _settings.PostgresHost;
        var pgEndpoint = $"{pgHost}:{_settings.PostgresPort}";
        var connStr = $"Host={pgHost};Port={_settings.PostgresPort};Username=postgres;" +
                      $"Password={_settings.PostgresPassword};Database={_settings.PostgresDatabase}";
        try
        {
            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync(ct);

            // LightRAG v1.4+ names tables as lightrag_vdb_{type}_{model}_{dim}d, e.g.
            // lightrag_vdb_chunks_text_embedding_3_large_1536d. Use LIKE to match all
            // variants so a dimension change correctly clears the previous model's tables.
            foreach (var prefix in new[] { "lightrag_vdb_chunks", "lightrag_vdb_entity", "lightrag_vdb_relation" })
            {
                // DO blocks cannot bind command parameters, so pass values through
                // session settings instead of interpolating them into the SQL text.
                await using (var setCfg = conn.CreateCommand())
                {
                    setCfg.CommandText =
                        "SELECT set_config('ntg.table_prefix', @prefix, false), set_config('ntg.workspace', @workspace, false)";
                    setCfg.Parameters.AddWithValue("prefix", prefix);
                    setCfg.Parameters.AddWithValue("workspace", workspace);
                    await setCfg.ExecuteNonQueryAsync(ct);
                }

                await using var cmd = conn.CreateCommand();
                // Table names come from pg_tables (not user input); %I quotes them safely.
                cmd.CommandText = @"
                    DO $do$
                    DECLARE t text;
                    BEGIN
                        FOR t IN
                            SELECT tablename FROM pg_tables
                            WHERE schemaname = 'public'
                              AND tablename LIKE current_setting('ntg.table_prefix') || '%'
                        LOOP
                            EXECUTE format('DELETE FROM %I WHERE workspace = $1', t)
                            USING current_setting('ntg.workspace');
                        END LOOP;
                    END $do$;";
                await cmd.ExecuteNonQueryAsync(ct);
                _logger.LogInformation(
                    "LightRagContainerManager: cleared {Prefix}* rows for workspace {W} ({Endpoint}).",
                    prefix, workspace, pgEndpoint);
            }

            _logger.LogWarning(
                "LightRagContainerManager: wiped vector rows for agent {AgentId} (workspace={W}). " +
                "Documents must be re-uploaded to be queryable.", agentId, workspace);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "LightRagContainerManager: failed to reset vector schema for agent {AgentId} ({Endpoint}). " +
                "Container will still be recreated but first upload may fail until schema is manually reset.",
                agentId, pgEndpoint);
        }
    }

    public void Dispose()
    {
        // _docker is owned by the DI container (registered as a singleton) and disposed there.
        _gate.Dispose();
    }
}
