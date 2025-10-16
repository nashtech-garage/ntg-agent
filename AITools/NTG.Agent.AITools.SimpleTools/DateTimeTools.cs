using System.ComponentModel;

namespace NTG.Agent.AITools.SimpleTools;

public static class DateTimeTools
{
    [Description("Get current datetime")]
    public static DateTime GetCurrentDateTime()
    {
        return DateTime.Now;
    }
}
