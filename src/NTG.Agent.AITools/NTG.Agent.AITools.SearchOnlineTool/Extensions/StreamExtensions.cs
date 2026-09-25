namespace NTG.Agent.AITools.SearchOnlineTool.Extensions;

public static class StreamExtensions
{
    public static byte[] ReadAllBytes(this Stream stream)
    {
        if (stream is MemoryStream s1)
        {
            return s1.ToArray();
        }

        using (var s2 = new MemoryStream())
        {
            stream.CopyTo(s2);
            return s2.ToArray();
        }
    }
}
