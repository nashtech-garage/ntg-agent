namespace NTG.Agent.Common.Dtos.Chats;

/// <summary>Distinguishes between the model's final answer and its chain-of-thought reasoning.</summary>
public enum PromptContentType
{
    Text = 0,
    Thinking = 1
}

/// <summary>
/// A single streamed chunk from the agent. ContentType defaults to Text, making this non-breaking
/// for callers that only care about Fast-mode text content.
/// </summary>
public record PromptResponse(string Content, PromptContentType ContentType = PromptContentType.Text);
