using System.Globalization;
namespace DiceMaster;

internal static class RollTime
{
    public static string Format(long startsAt) => Convert(startsAt,"HH:mm:ss");
    public static string Detail(long startsAt) => Convert(startsAt,"yyyy-MM-dd HH:mm:ss zzz")+" (local time; roll start)";
    private static string Convert(long startsAt,string format)
    {
        try { return DateTimeOffset.FromUnixTimeMilliseconds(startsAt).ToLocalTime().ToString(format,CultureInfo.InvariantCulture); }
        catch (ArgumentOutOfRangeException) { return "Unknown time"; }
    }
}
