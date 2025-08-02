namespace NTG.Agent.Shared.Dtos.Folders;
public class UpdateFolderDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public Guid AgentId { get; set; }
}