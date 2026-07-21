using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace NTG.Agent.LightRag;

/// <summary>
/// Postgres-backed <see cref="ILightRagPortReservationStore"/>: the team-wide port ledger living in
/// the shared <c>lightrag-postgres</c>, alongside the Docker host whose ports it governs.
/// <para>
/// Correctness comes from the database, not from process-local locking: the lowest free port is
/// claimed with a single atomic INSERT arbitrated by <c>uq_agent_port_reservations_port</c>. If a
/// concurrent Orchestrator (another developer) claims that port first, the insert simply affects no
/// rows and we retry with the next free one — so no two agents can ever hold the same port.
/// </para>
/// <para>
/// The table is created by <c>deploy/lightrag-postgres/migrations/001_create_agent_port_reservations.sql</c>,
/// which is applied once by hand — this store deliberately never creates it, so schema changes stay
/// reviewable and versioned rather than happening implicitly at runtime.
/// </para>
/// </summary>
public sealed class LightRagPgPortReservationStore : ILightRagPortReservationStore
{
    // Bounds the retry loop when repeatedly losing the race for a port to other Orchestrators.
    // Each attempt costs one round-trip and only happens under genuine contention.
    private const int MaxAttempts = 25;

    private const string UndefinedTableSqlState = "42P01";
    private const string UniqueViolationSqlState = "23505";

    private readonly LightRagSettings _settings;
    private readonly ILogger<LightRagPgPortReservationStore> _logger;

    public LightRagPgPortReservationStore(IOptions<LightRagSettings> settings, ILogger<LightRagPgPortReservationStore> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    // Same endpoint the vector-schema reset uses: the shared Postgres reached over the SSH -L forward.
    private string BuildConnectionString()
    {
        var host = string.IsNullOrWhiteSpace(_settings.PostgresHost) ? _settings.ServerHost : _settings.PostgresHost;
        return $"Host={host};Port={_settings.PostgresPort};Username=postgres;" +
               $"Password={_settings.PostgresPassword};Database={_settings.PostgresDatabase}";
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new NpgsqlConnection(BuildConnectionString());
        await conn.OpenAsync(ct);
        return conn;
    }

    public async Task<int?> GetReservedPortAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenAsync(cancellationToken);
        return await Guarded(() => ReadPortAsync(conn, agentId, cancellationToken));
    }

    public async Task<int> GetOrReserveAsync(Guid agentId, int rangeStart, int rangeEnd, CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenAsync(cancellationToken);

        return await Guarded(async () =>
        {
            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                // Already reserved (by us earlier, or by a concurrent call for this same agent —
                // the agent_id primary key makes reservation idempotent per agent).
                var existing = await ReadPortAsync(conn, agentId, cancellationToken);
                if (existing is not null)
                    return existing.Value;

                var claimed = await TryClaimLowestFreeAsync(conn, agentId, rangeStart, rangeEnd, cancellationToken);
                if (claimed is not null)
                {
                    _logger.LogInformation("LightRag port reservation: agent {AgentId} reserved port {Port}.", agentId, claimed.Value);
                    return claimed.Value;
                }

                // No row inserted: either the pool is full, or another Orchestrator took the port we
                // targeted between our SELECT and INSERT. Distinguish, then retry the race.
                if (!await AnyFreePortAsync(conn, rangeStart, rangeEnd, cancellationToken))
                    throw new PortPoolExhaustedException(rangeStart, rangeEnd);

                _logger.LogDebug("LightRag port reservation: lost race for a port (agent {AgentId}, attempt {Attempt}); retrying.", agentId, attempt);
            }

            throw new InvalidOperationException(
                $"Could not reserve a LightRAG port for agent {agentId} after {MaxAttempts} attempts due to contention.");
        });
    }

    public async Task<int> ReassignAsync(Guid agentId, int rangeStart, int rangeEnd, CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenAsync(cancellationToken);

        return await Guarded(async () =>
        {
            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                try
                {
                    var moved = await TryMoveToLowestFreeAsync(conn, agentId, rangeStart, rangeEnd, cancellationToken);
                    if (moved is not null)
                    {
                        _logger.LogInformation("LightRag port reservation: agent {AgentId} reassigned to port {Port}.", agentId, moved.Value);
                        return moved.Value;
                    }

                    // No row updated: the agent holds no reservation yet, or there is no free port.
                    if (!await AnyFreePortAsync(conn, rangeStart, rangeEnd, cancellationToken))
                        throw new PortPoolExhaustedException(rangeStart, rangeEnd);

                    // No existing row to move — fall back to a fresh claim.
                    var claimed = await TryClaimLowestFreeAsync(conn, agentId, rangeStart, rangeEnd, cancellationToken);
                    if (claimed is not null)
                        return claimed.Value;
                }
                catch (PostgresException ex) when (ex.SqlState == UniqueViolationSqlState)
                {
                    // Another Orchestrator claimed the target port first; retry with the next free one.
                    _logger.LogDebug("LightRag port reservation: reassign race for agent {AgentId} (attempt {Attempt}); retrying.", agentId, attempt);
                }
            }

            throw new InvalidOperationException(
                $"Could not reassign a LightRAG port for agent {agentId} after {MaxAttempts} attempts due to contention.");
        });
    }

    public async Task ReleaseAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenAsync(cancellationToken);

        await Guarded(async () =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM agent_port_reservations WHERE agent_id = @agentId";
            cmd.Parameters.AddWithValue("agentId", agentId);
            var deleted = await cmd.ExecuteNonQueryAsync(cancellationToken);
            if (deleted > 0)
                _logger.LogInformation("LightRag port reservation: released the port held by agent {AgentId}.", agentId);
            return deleted;
        });
    }

    private static async Task<int?> ReadPortAsync(NpgsqlConnection conn, Guid agentId, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT port FROM agent_port_reservations WHERE agent_id = @agentId";
        cmd.Parameters.AddWithValue("agentId", agentId);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is int port ? port : null;
    }

    // Claims the lowest port in range that no agent holds, in one atomic statement. Returns null when
    // nothing was inserted (pool full, or another Orchestrator won the race for that port).
    private static async Task<int?> TryClaimLowestFreeAsync(NpgsqlConnection conn, Guid agentId, int rangeStart, int rangeEnd, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO agent_port_reservations (agent_id, port)
            SELECT @agentId, p.port
            FROM generate_series(@rangeStart, @rangeEnd) AS p(port)
            WHERE NOT EXISTS (SELECT 1 FROM agent_port_reservations r WHERE r.port = p.port)
            ORDER BY p.port
            LIMIT 1
            ON CONFLICT DO NOTHING
            RETURNING port
            """;
        cmd.Parameters.AddWithValue("agentId", agentId);
        cmd.Parameters.AddWithValue("rangeStart", rangeStart);
        cmd.Parameters.AddWithValue("rangeEnd", rangeEnd);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is int port ? port : null;
    }

    // Moves an existing reservation to the lowest free port, freeing the old one. Returns null when the
    // agent has no reservation yet or no free port exists.
    private static async Task<int?> TryMoveToLowestFreeAsync(NpgsqlConnection conn, Guid agentId, int rangeStart, int rangeEnd, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE agent_port_reservations AS t
            SET port = sub.port, updated_at = now()
            FROM (
                SELECT p.port
                FROM generate_series(@rangeStart, @rangeEnd) AS p(port)
                WHERE NOT EXISTS (SELECT 1 FROM agent_port_reservations r WHERE r.port = p.port)
                ORDER BY p.port
                LIMIT 1
            ) AS sub
            WHERE t.agent_id = @agentId
            RETURNING t.port
            """;
        cmd.Parameters.AddWithValue("agentId", agentId);
        cmd.Parameters.AddWithValue("rangeStart", rangeStart);
        cmd.Parameters.AddWithValue("rangeEnd", rangeEnd);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is int port ? port : null;
    }

    private static async Task<bool> AnyFreePortAsync(NpgsqlConnection conn, int rangeStart, int rangeEnd, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM generate_series(@rangeStart, @rangeEnd) AS p(port)
                WHERE NOT EXISTS (SELECT 1 FROM agent_port_reservations r WHERE r.port = p.port)
            )
            """;
        cmd.Parameters.AddWithValue("rangeStart", rangeStart);
        cmd.Parameters.AddWithValue("rangeEnd", rangeEnd);
        return await cmd.ExecuteScalarAsync(ct) is true;
    }

    // Turns a missing-table error into an actionable message instead of a raw Npgsql failure — the
    // table is applied by hand, so "you forgot the migration" is a real and recoverable situation.
    private static async Task<T> Guarded<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (PostgresException ex) when (ex.SqlState == UndefinedTableSqlState)
        {
            throw new InvalidOperationException(
                "The 'agent_port_reservations' table does not exist in the shared LightRAG Postgres. " +
                "Apply deploy/lightrag-postgres/migrations/001_create_agent_port_reservations.sql once " +
                "against the shared database before provisioning agents.", ex);
        }
    }
}
