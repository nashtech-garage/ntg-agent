using Microsoft.Extensions.AI;
using NTG.Agent.Common.Dtos.Chats;
using NTG.Agent.Orchestrator.Services.Agents;

namespace NTG.Agent.Orchestrator.Tests.Services.Agents;

[TestFixture]
public class AgUiChatResponseAdapterTests
{
    [Test]
    public async Task TextResponse_IsMappedToAssistantTextUpdate()
    {
        var updates = await CollectAsync(
            new PromptResponse("Hello"));

        Assert.That(updates, Has.Count.EqualTo(1));
        Assert.That(updates[0].Role, Is.EqualTo(ChatRole.Assistant));
        Assert.That(updates[0].Text, Is.EqualTo("Hello"));
    }

    [Test]
    public async Task ThinkingAndSkillNotice_AreMappedToReasoningContent()
    {
        var updates = await CollectAsync(
            new PromptResponse("thinking", PromptContentType.Thinking),
            new PromptResponse("skill activity", PromptContentType.SkillNotice));

        Assert.That(updates, Has.Count.EqualTo(2));
        Assert.That(updates.SelectMany(update => update.Contents), Has.All.TypeOf<TextReasoningContent>());
        Assert.That(updates.SelectMany(update => update.Contents).Select(content => content.ToString()),
            Is.EqualTo(new[] { "thinking", "skill activity" }));
    }

    [Test]
    public async Task ToolCall_PreservesCallIdNameAndArguments()
    {
        var updates = await CollectAsync(
            new PromptResponse(
                """{"callId":"call-1","name":"render_a2ui","arguments":{"surfaceId":"surface-1","count":2}}""",
                PromptContentType.ToolCall));

        var call = updates.Single().Contents.OfType<FunctionCallContent>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(call.CallId, Is.EqualTo("call-1"));
            Assert.That(call.Name, Is.EqualTo("render_a2ui"));
            Assert.That(call.Arguments["surfaceId"]?.ToString(), Is.EqualTo("surface-1"));
            Assert.That(call.Arguments["count"]?.ToString(), Is.EqualTo("2"));
        });
    }

    [Test]
    public async Task ToolResult_PreservesCallIdAndSerializedResult()
    {
        var updates = await CollectAsync(
            new PromptResponse(
                """{"callId":"call-2","result":"{\"a2ui_operations\":[{\"op\":\"createSurface\"}]}"}""",
                PromptContentType.ToolResult));

        var result = updates.Single().Contents.OfType<FunctionResultContent>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(updates.Single().Role, Is.EqualTo(ChatRole.Tool));
            Assert.That(result.CallId, Is.EqualTo("call-2"));
            Assert.That(result.Result?.ToString(), Does.Contain("a2ui_operations"));
        });
    }

    private static async Task<List<ChatResponseUpdate>> CollectAsync(params PromptResponse[] responses) =>
        await CollectAsync(ToAsyncEnumerable(responses));

    private static async Task<List<ChatResponseUpdate>> CollectAsync(
        IAsyncEnumerable<PromptResponse> responses)
    {
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in AgUiChatResponseAdapter.ToChatResponseUpdates(responses))
            updates.Add(update);
        return updates;
    }

    private static async IAsyncEnumerable<PromptResponse> ToAsyncEnumerable(
        params PromptResponse[] responses)
    {
        foreach (var response in responses)
            yield return response;

        await Task.CompletedTask;
    }
}
