namespace NTG.Agent.Common.Dtos.Chats;

/// <summary>
/// Represents a paginated response containing a list of conversations.
/// </summary>
public class ConversationListResponse
{
    /// <summary>
    /// Gets or sets the list of conversation items for the current page.
    /// </summary>
    public IList<ConversationListItem> Items { get; set; } = new List<ConversationListItem>();

    /// <summary>
    /// Gets or sets the current page number (1-based).
    /// </summary>
    public int PageNumber { get; set; }

    /// <summary>
    /// Gets or sets the number of items per page.
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    /// Gets or sets the total number of conversations across all pages.
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Gets or sets the total number of pages.
    /// </summary>
    public int TotalPages { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether there are more pages to load.
    /// </summary>
    public bool HasMore { get; set; }
}
