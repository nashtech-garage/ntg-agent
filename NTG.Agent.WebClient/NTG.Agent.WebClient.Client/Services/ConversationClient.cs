using NTG.Agent.Common.Dtos.Chats;
using NTG.Agent.Common.Dtos.Constants;
using NTG.Agent.Common.Dtos.Conversations;
using System.Net;
using System.Net.Http.Json;

namespace NTG.Agent.WebClient.Client.Services;

public class ConversationClient(HttpClient httpClient)
{
    public async Task<ConversationCreated> Create(string currentSessionId)
    {
        string url = string.IsNullOrWhiteSpace(currentSessionId)
        ? "/api/conversations"
        : $"/api/conversations?currentSessionId={currentSessionId}";

        var response = await httpClient.PostAsync(url, null);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ConversationCreated>();
        return result!;
    }

    /// <summary>
    /// Gets a paginated list of conversations with support for lazy loading.
    /// </summary>
    /// <param name="pageNumber">The page number to retrieve (1-based). Defaults to 1.</param>
    /// <param name="pageSize">The number of items per page. Defaults to <see cref="PaginationConstants.DefaultPageSize"/>.</param>
    /// <returns>A paginated response containing conversation items and metadata.</returns>
    public async Task<ConversationListResponse> GetConversationsPagedAsync(int pageNumber = 1, int pageSize = PaginationConstants.DefaultPageSize)
    {
        var response = await httpClient.GetAsync($"/api/conversations?pageNumber={pageNumber}&pageSize={pageSize}");
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ConversationListResponse>();
        return result ?? new ConversationListResponse();
    }

    public async Task<IList<ChatMessageListItem>> GetConversationMessagesAsync(Guid conversationId, string currentSessionId)
    {
        string url = string.IsNullOrWhiteSpace(currentSessionId)
            ? $"/api/conversations/{conversationId}/messages"
            : $"/api/conversations/{conversationId}/messages?currentSessionId={Uri.EscapeDataString(currentSessionId)}";

        var response = await httpClient.GetAsync(url);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new UnauthorizedAccessException("Access denied to this conversation.");
        }
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<IList<ChatMessageListItem>>();
        return result ?? [];
    }



    public async Task<bool> DeleteConversationAsync(Guid conversationId)
    {
        var response = await httpClient.DeleteAsync($"/api/conversations/{conversationId}");
        response.EnsureSuccessStatusCode();
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> UpdateConversationAsync(Guid conversationId, string newName)
    {
        var response = await httpClient.PutAsync($"/api/conversations/{conversationId}/rename?newName={Uri.EscapeDataString(newName)}", null);
        response.EnsureSuccessStatusCode();
        return response.IsSuccessStatusCode;
    }

    public async Task<IList<ChatSearchResultItem>> SearchChatMessages(string keyword)
    {
        var response = await httpClient.GetAsync($"/api/conversations/search?keyword={Uri.EscapeDataString(keyword)}");
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IList<ChatSearchResultItem>>();
        return result ?? [];
    }

    public async Task<bool> UpdateMessageReactionAsync(Guid conversationId, Guid messageId, ReactionType reaction)
    {
        var request = new UpdateReactionRequest { Reaction = reaction };
        var response = await httpClient.PutAsJsonAsync($"/api/conversations/{conversationId}/messages/{messageId}/reaction", request);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> UpdateMessageCommentAsync(Guid conversationId, Guid messageId, string comment)
    {
        var request = new UpdateCommentRequest { Comment = comment };
        var response = await httpClient.PutAsJsonAsync($"/api/conversations/{conversationId}/messages/{messageId}/comment", request);
        return response.IsSuccessStatusCode;
    }
}
