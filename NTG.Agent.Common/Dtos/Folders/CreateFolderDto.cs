using System.ComponentModel.DataAnnotations;

namespace NTG.Agent.Common.Dtos.Folders;
public class CreateFolderDto
{
    [Required]
    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public Guid AgentId { get; set; }
}