namespace NTG.Agent.Orchestrator.Services.AnonymousSessions;

public interface IIpAddressService
{
    string? GetClientIpAddress(HttpContext httpContext);
    
    Task<bool> IsIpAllowedAsync(string ipAddress);
}
