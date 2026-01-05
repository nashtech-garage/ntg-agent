using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Models.AnonymousSessions;

namespace NTG.Agent.Orchestrator.Services.AnonymousSessions;

public class IpAddressService : IIpAddressService
{
    private readonly AgentDbContext _context;
    private readonly AnonymousUserSettings _settings;

    public IpAddressService(
        AgentDbContext context,
        IOptions<AnonymousUserSettings> settings)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
    }

    public string? GetClientIpAddress(HttpContext httpContext)
    {
        if (httpContext == null)
        {
            return null;
        }

        // Check X-Forwarded-For header (for proxies/load balancers)
        var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.Split(',')[0].Trim();
        }

        // Check X-Real-IP header
        var realIp = httpContext.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(realIp))
        {
            return realIp;
        }

        // Fallback to RemoteIpAddress
        return httpContext.Connection.RemoteIpAddress?.ToString();
    }

    public async Task<bool> IsIpAllowedAsync(string ipAddress)
    {
        if (!_settings.EnableIpTracking || string.IsNullOrEmpty(ipAddress))
        {
            return true;
        }

        var count = await GetIpMessageCountTodayAsync(ipAddress);
        return count < _settings.MaxMessagesPerIpPerDay;
    }

    private async Task<int> GetIpMessageCountTodayAsync(string ipAddress)
    {
        if (string.IsNullOrEmpty(ipAddress))
        {
            return 0;
        }

        var today = DateTime.UtcNow.Date;
        var count = await _context.AnonymousSessions
            .Where(s => s.IpAddress == ipAddress && s.LastMessageAt >= today)
            .SumAsync(s => s.MessageCount);

        return count;
    }
}
