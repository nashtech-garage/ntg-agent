namespace NTG.Agent.Common.Dtos.Agents;

public record AgentListItem (Guid Id, string Name, string OwnerEmail, string UpdatedByEmail, DateTime UpdatedAt, bool IsDefault, bool IsPublished, AgentKind AgentKind = AgentKind.Outer, AgentProvisioningStatus ProvisioningStatus = AgentProvisioningStatus.Ready, string? ProvisioningError = null)
{
    /// <summary>User-facing provisioning label shown on the Admin agent card.</summary>
    public string ProvisioningLabel => ProvisioningStatus switch
    {
        AgentProvisioningStatus.Ready => "Ready",
        AgentProvisioningStatus.Failed => "Failed",
        _ => "Provisioning"
    };
}

public record AgentListItemDto(Guid Id, string Name, bool IsDefault, AgentMode Mode = AgentMode.Fast);
