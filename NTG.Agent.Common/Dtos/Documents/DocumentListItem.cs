using System.Globalization;

namespace NTG.Agent.Common.Dtos.Documents;

public record DocumentListItem (Guid Id, string Name, DateTime CreatedAt, DateTime UpdatedAt, List<string> Tags)
{
    public string FormattedCreatedAt => CreatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    public string FormattedUpdatedAt => UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
};
