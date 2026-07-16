using System.Globalization;

namespace NTG.Agent.Common.Dtos.Documents;

public record DocumentListItem (Guid Id, string Name, DateTime CreatedAt, DateTime UpdatedAt, List<string> Tags, DocumentStatus Status, string? ErrorMessage)
{
    public string FormattedCreatedAt => CreatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    public string FormattedUpdatedAt => UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>User-facing status label shown in the Admin knowledge list.</summary>
    public string StatusLabel => Status switch
    {
        DocumentStatus.Completed => "Uploaded",
        DocumentStatus.Failed => "Failed to upload",
        _ => "Uploading"
    };
};
