namespace NTG.Agent.Common.Dtos.Agents;

/// <summary>
/// Tracks where an agent is in its knowledge-backend provisioning lifecycle (reserving a port and
/// booting its dedicated LightRAG container). Persisted on the Agent row so the Admin UI can show
/// "Provisioning" / "Ready" / "Failed" without the create request blocking on the
/// container boot.
/// </summary>
public enum AgentProvisioningStatus
{
    Provisioning = 1,
    Ready = 2,
    Failed = 3
}
