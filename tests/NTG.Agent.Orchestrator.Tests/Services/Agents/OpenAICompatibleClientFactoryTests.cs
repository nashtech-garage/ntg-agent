using Microsoft.Extensions.AI;
using NTG.Agent.Orchestrator.Services.Agents.Clients;
using OpenAI.Responses;

namespace NTG.Agent.Orchestrator.Tests.Services.Agents;

#pragma warning disable OPENAI001

// Holds the Responses API to stateless mode. With server-side storage on, the client reports a
// ConversationId and FunctionInvokingChatClient stops resending history, chaining every tool-loop
// continuation to previous_response_id — an id Azure's /openai/v1 surface intermittently cannot
// resolve, which turns a successful tool call into HTTP 400 previous_response_not_found. The second
// test exists because the obvious way to lose that chaining is to drop the raw options entirely,
// which would silently take the reasoning summaries with it.
[TestFixture]
public class OpenAICompatibleClientFactoryTests
{
    private static CreateResponseOptions BuildRawResponsesOptions()
    {
        var options = new ChatOptions();
        OpenAICompatibleClientFactory.ConfigureResponsesOptions(options);

        Assert.That(options.RawRepresentationFactory, Is.Not.Null);
        return (CreateResponseOptions)options.RawRepresentationFactory!(null!)!;
    }

    [Test]
    public void ResponsesOptions_DisableServerSideResponseStorage()
    {
        Assert.That(BuildRawResponsesOptions().StoredOutputEnabled, Is.False);
    }

    [Test]
    public void ResponsesOptions_StillRequestReasoningSummaries()
    {
        var reasoning = BuildRawResponsesOptions().ReasoningOptions;

        Assert.Multiple(() =>
        {
            Assert.That(reasoning, Is.Not.Null);
            Assert.That(reasoning!.ReasoningSummaryVerbosity, Is.EqualTo(ResponseReasoningSummaryVerbosity.Auto));
            Assert.That(reasoning.ReasoningEffortLevel, Is.EqualTo(ResponseReasoningEffortLevel.High));
        });
    }
}

#pragma warning restore OPENAI001
