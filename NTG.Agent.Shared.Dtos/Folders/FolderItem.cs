namespace NTG.Agent.Shared.Dtos.Folders;
public class FolderItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public FolderItem? Parent { get; set; }
    public bool IsDeletable { get; set; }
    public int? SortOrder { get; set; }
    public ICollection<FolderItem> Children { get; set; } = new List<FolderItem>();
    public Guid AgentId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}