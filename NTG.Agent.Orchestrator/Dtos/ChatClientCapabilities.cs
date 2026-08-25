namespace NTG.Agent.Orchestrator.Dtos;

/// <summary>
/// What the client on the other end of a chat run is able to render.
/// </summary>
/// <remarks>
/// <para>
/// The two chat clients arrive through two different endpoints, and that — not a header, a user
/// agent, or a field on the request — is the signal. <c>AgentsController</c>'s form endpoint serves
/// the Blazor web client, whose chat renders markdown text and nothing else.
/// <c>AgUiController</c>'s AG-UI endpoint serves <c>my-copilot-app</c>, which mounts the A2UI
/// renderer and executes frontend tools. A controller states which one it is; nothing downstream
/// has to guess.
/// </para>
/// <para>
/// Deliberately a parameter rather than a property on <c>PromptRequestForm</c>. That type is
/// <c>[FromForm]</c>-bound on the text-only endpoint, so every property on it is client-supplied —
/// a capability declared there would be a capability any caller could grant itself.
/// </para>
/// <para>
/// <see cref="TextOnly"/> is the zero value so that a call site which forgets to say degrades to
/// the safe answer: features are withheld, not leaked.
/// </para>
/// </remarks>
public enum ChatClientCapabilities
{
    /// <summary>Renders assistant text. No A2UI surfaces, no frontend tools, no protocol chunks.</summary>
    TextOnly = 0,

    /// <summary>An AG-UI client with the A2UI renderer mounted: surfaces, frontend tools, tool-render cards.</summary>
    GenerativeUi = 1,
}
