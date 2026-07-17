using Microsoft.Extensions.Options;

namespace NTG.Agent.LightRag;

/// <summary>
/// Default <see cref="ILightRagHealthProbe"/>. Issues a short-timeout <c>GET /health</c>
/// through the named LightRAG HTTP client, so the probe traverses the SOCKS proxy / SSH
/// tunnel when one is configured (a raw TCP connect cannot). Host resolution mirrors the
/// factory: empty <see cref="LightRagSettings.ServerHost"/> means the local loopback.
/// </summary>
public sealed class LightRagHealthProbe : ILightRagHealthProbe
{
    // Per-attempt cap: a booting-but-not-ready container should fail fast so the caller's
    // poll loop can retry, not block for the client's multi-minute request timeout.
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly LightRagSettings _settings;

    public LightRagHealthProbe(IHttpClientFactory httpClientFactory, IOptions<LightRagSettings> settings)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
    }

    public async Task<bool> IsHealthyAsync(int port, CancellationToken cancellationToken = default)
    {
        try
        {
            var http = _httpClientFactory.CreateClient(nameof(LightRagClient));
            http.BaseAddress = new Uri($"http://{ResolveHost()}:{port}");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(ProbeTimeout);
            using var _ = await http.GetAsync("health", HttpCompletionOption.ResponseHeadersRead, cts.Token);
            return true;
        }
        catch
        {
            // Unreachable, connection reset, or timed out — treat as not-ready. Caller
            // cancellation is surfaced by the caller (which re-checks its own token), not here.
            return false;
        }
    }

    private string ResolveHost() => string.IsNullOrWhiteSpace(_settings.ServerHost) ? "localhost" : _settings.ServerHost;
}
