namespace NTG.Agent.WebClient.Client.States;

public class ConversationState
{
    public event Action? OnConversationAdded;

    public void NotifyConversationAdded()
    {
        OnConversationAdded?.Invoke();
    }
}
