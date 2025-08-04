using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace NTG.Agent.Orchestrator.Models.Documents;

public class Folder
{
    public Folder()
    {
        Id = Guid.NewGuid();
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; set; }
    [Required]
    [MaxLength(255)]
    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    [ForeignKey(nameof(ParentId))]
    [JsonIgnore]
    public Folder? Parent { get; set; }
    public bool IsDeletable { get; set; } = true;
    public int? SortOrder { get; set; } = null;
    public ICollection<Folder> Children { get; set; } = new List<Folder>();
    public ICollection<Document> Documents { get; set; } = new List<Document>();
    public Guid AgentId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
