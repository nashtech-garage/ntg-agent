namespace NTG.Agent.Orchestrator.Models.AnonymousSessions;

public class AnonymousUserSettings
{
    public int MaxMessagesPerSession { get; set; } = 10;

    public int ResetPeriodHours { get; set; } = 24;

    public bool EnableIpTracking { get; set; } = true;

    public int MaxMessagesPerIpPerDay { get; set; } = 50;

    /* For cleanup of old sessions */
    public int SessionExpirationDays { get; set; } = 7;
    public double CleanupProbability { get; set; } = 0.01; // 1% chance
}
