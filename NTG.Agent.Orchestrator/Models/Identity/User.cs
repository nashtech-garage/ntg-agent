namespace NTG.Agent.Orchestrator.Models.Identity;

public class User
{
    public Guid Id { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
}
