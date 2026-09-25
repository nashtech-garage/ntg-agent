using Microsoft.Extensions.Options;

namespace NTG.Agent.LightRag;

/// <summary>
/// Default <see cref="ILightRagHealthProbe"/>. Issues a short-timeout <c>GET health</c>
/// through the named LightRAG HTTP client against the gateway's per-agent path, so the
/// probe traverses the SOCKS proxy when one is configured (a raw TCP connect cannot).
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

    public async Task<bool> IsHealthyAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        try
        {
            var http = _httpClientFactory.CreateClient(nameof(LightRagClient));
            http.BaseAddress = new Uri($"{_settings.ResolveGatewayUrl()}/agents/{agentId}/");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(ProbeTimeout);
            using var response = await http.GetAsync("health", HttpCompletionOption.ResponseHeadersRead, cts.Token);
            // The gateway answers 502/504 itself when the agent's container is stopped or not
            // yet resolvable — a response alone no longer proves the app is serving.
            return response.IsSuccessStatusCode;
        }
        catch
        {
            // Unreachable, connection reset, or timed out — treat as not-ready. Caller
            // cancellation is surfaced by the caller (which re-checks its own token), not here.
            return false;
        }
    }
}
