namespace NTG.Agent.WebClient.Client.ViewModels;

public class PromptRequest(string prompt)
{
    public string Prompt { get; } = prompt;
}
