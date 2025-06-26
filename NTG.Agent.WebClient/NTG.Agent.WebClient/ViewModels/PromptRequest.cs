namespace NTG.Agent.WebClient.ViewModels;

public class PromptRequest(string prompt)
{
    public string Prompt { get; } = prompt;
}
