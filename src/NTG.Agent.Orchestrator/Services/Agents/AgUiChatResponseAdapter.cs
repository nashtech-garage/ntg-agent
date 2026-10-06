using System.Text.Json;
using AGUI.Abstractions;
using Microsoft.Extensions.AI;
using NTG.Agent.Common.Dtos.Chats;

namespace NTG.Agent.Orchestrator.Services.Agents;

/// <summary>
/// Converts the application's prompt stream into the Microsoft.Extensions.AI shape consumed by
/// the AG-UI .NET server adapter.
/// </summary>
public static class AgUiChatResponseAdapter
{
    public static async IAsyncEnumerable<ChatResponseUpdate> ToChatResponseUpdates(
        IAsyncEnumerable<PromptResponse> responses,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var response in responses.WithCancellation(cancellationToken))
        {
            if (string.IsNullOrEmpty(response.Content))
                continue;

            switch (response.ContentType)
            {
                case PromptContentType.Text:
                    yield return CreateUpdate(ChatRole.Assistant, response.Content);
                    break;

                case PromptContentType.Thinking:
                case PromptContentType.SkillNotice:
                    yield return CreateUpdate(
                        ChatRole.Assistant,
                        new List<AIContent> { new TextReasoningContent(response.Content) });
                    break;

                case PromptContentType.ToolCall:
                    if (TryParseToolCall(response.Content, out var toolCall))
                    {
                        yield return CreateUpdate(
                            ChatRole.Assistant,
                            new List<AIContent>
                            {
                                new FunctionCallContent(toolCall.CallId, toolCall.Name, toolCall.Arguments)
                            });
                    }
                    break;

                case PromptContentType.ToolResult:
                    if (TryParseToolResult(response.Content, out var toolResult))
                    {
                        yield return CreateUpdate(
                            ChatRole.Tool,
                            new List<AIContent>
                            {
                                new FunctionResultContent(toolResult.CallId, toolResult.Content)
                            });
                    }
                    break;
            }
        }
    }

    private static ChatResponseUpdate CreateUpdate(ChatRole role, string text)
    {
        var id = Guid.NewGuid().ToString();
        return new(role, text) { MessageId = id, ResponseId = id };
    }

    private static ChatResponseUpdate CreateUpdate(ChatRole role, IList<AIContent> contents)
    {
        var id = Guid.NewGuid().ToString();
        return new(role, contents) { MessageId = id, ResponseId = id };
    }

    private static bool TryParseToolCall(string content, out ToolCall toolCall)
    {
        toolCall = default;

        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            var name = root.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(name))
                return false;

            var callId = root.TryGetProperty("callId", out var callIdElement)
                ? callIdElement.GetString()
                : null;

            var arguments = root.TryGetProperty("arguments", out var argumentsElement)
                ? JsonSerializer.Deserialize<Dictionary<string, object?>>(argumentsElement.GetRawText())
                : null;

            toolCall = new ToolCall(
                string.IsNullOrWhiteSpace(callId) ? Guid.NewGuid().ToString() : callId,
                name,
                arguments ??                 new Dictionary<string, object?>());
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryParseToolResult(string content, out ToolResult toolResult)
    {
        toolResult = default;

        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            var callId = root.TryGetProperty("callId", out var callIdElement)
                ? callIdElement.GetString()
                : null;
            var result = root.TryGetProperty("result", out var resultElement)
                ? resultElement.GetString()
                : null;

            toolResult = new ToolResult(
                string.IsNullOrWhiteSpace(callId) ? Guid.NewGuid().ToString() : callId,
                result ?? string.Empty);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private readonly record struct ToolCall(string CallId, string Name, Dictionary<string, object?> Arguments);
    private readonly record struct ToolResult(string CallId, string Content);
}
