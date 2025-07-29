
namespace NTG.Agent.Shared.Dtos.Documents;

public record DocumentListItem (Guid Id, string Name, DateTime CreatedAt, DateTime UpdatedAt)
{
    public string FormattedCreatedAt => CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");
    public string FormattedUpdatedAt => UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss");
};
