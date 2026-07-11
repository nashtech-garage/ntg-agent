namespace NTG.Agent.Common.Dtos.Chats;

/// <summary>Distinguishes between the model's final answer and its chain-of-thought reasoning.</summary>
public enum PromptContentType
{
    Text = 0,
    Thinking = 1,
    /// <summary>A frontend (browser-executed) tool call. Content is JSON: {"callId","name","arguments"}.</summary>
    ToolCall = 2,
    /// <summary>The result of a server-side tool call, surfaced so the browser can render it.
    /// Content is JSON: {"callId","result"}.</summary>
    ToolResult = 3
}

/// <summary>
/// A single streamed chunk from the agent. ContentType defaults to Text, making this non-breaking
/// for callers that only care about Fast-mode text content.
/// </summary>
public record PromptResponse(string Content, PromptContentType ContentType = PromptContentType.Text);
