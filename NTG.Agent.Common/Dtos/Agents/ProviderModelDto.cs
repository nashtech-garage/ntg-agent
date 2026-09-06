namespace NTG.Agent.Common.Dtos.Agents;

/// <summary>
/// A model enabled for a provider. Admins toggle <see cref="AllowsThinking"/> per model;
/// agent settings surface only these enabled models and allow Thinking mode only when
/// <see cref="AllowsThinking"/> is true.
/// </summary>
public class ProviderModelDto
{
    public Guid Id { get; set; }
    public string ModelId { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public bool AllowsThinking { get; set; }
}
