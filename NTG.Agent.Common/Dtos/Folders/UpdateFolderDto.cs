namespace NTG.Agent.Common.Dtos.Folders;
public class UpdateFolderDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public Guid AgentId { get; set; }
}