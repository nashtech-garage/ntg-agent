namespace NTG.Agent.Common.Helpers;
public static class DateTimeExtensions
{
    public static string ToBeautify(this TimeSpan timeSpan)
    {
        if (timeSpan.TotalHours >= 1)
        {
            return $"{(int)timeSpan.TotalHours}h {timeSpan.Minutes}m";
        }
        else if (timeSpan.TotalMinutes >= 1)
        {
            return $"{(int)timeSpan.TotalMinutes}m";
        }
        else
        {
            return $"{(int)timeSpan.TotalSeconds}s";
        }
    }
}
