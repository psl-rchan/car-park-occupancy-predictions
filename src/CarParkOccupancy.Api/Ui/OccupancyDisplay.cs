using System.Globalization;
using CarParkOccupancy.Api.Models;

namespace CarParkOccupancy.Api.Ui;

public static class OccupancyDisplay
{
    private static readonly TimeZoneInfo HongKong = ResolveHongKong();

    public static string FormatPercent(double? value) =>
        value is null
            ? "—"
            : value.Value.ToString("0.0", CultureInfo.InvariantCulture) + "%";

    public static string BarWidth(double? value)
    {
        if (value is null || double.IsNaN(value.Value))
        {
            return "0%";
        }

        var clamped = Math.Clamp(value.Value, 0, 100);
        return clamped.ToString("0.0", CultureInfo.InvariantCulture) + "%";
    }

    public static string LevelClass(double? value) => value switch
    {
        null => "is-none",
        < 60 => "is-quiet",
        < 85 => "is-busy",
        _ => "is-full"
    };

    public static HourlyPrediction? AtHorizon(IReadOnlyList<HourlyPrediction>? predictions, int horizon) =>
        predictions?.FirstOrDefault(prediction => prediction.HorizonHours == horizon);

    public static string FormatHongKong(DateTimeOffset value)
    {
        var local = TimeZoneInfo.ConvertTime(value, HongKong);
        return local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " HKT";
    }

    public static string FormatIso(DateTimeOffset value) =>
        value.ToString("o", CultureInfo.InvariantCulture);

    private static TimeZoneInfo ResolveHongKong()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Hong_Kong");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.CreateCustomTimeZone(
                "Asia/Hong_Kong",
                TimeSpan.FromHours(8),
                "Hong Kong",
                "Hong Kong");
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.CreateCustomTimeZone(
                "Asia/Hong_Kong",
                TimeSpan.FromHours(8),
                "Hong Kong",
                "Hong Kong");
        }
    }
}
