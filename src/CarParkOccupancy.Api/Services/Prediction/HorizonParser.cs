using System.Globalization;

namespace CarParkOccupancy.Api.Services.Prediction;

public static class HorizonParser
{
    public const int MinHorizonHours = 1;

    public const int MaxHorizonHours = 6;

    public const int MaxBatchSize = 50;

    public static readonly int[] DefaultHorizons = [1, 2, 3, 4, 5, 6];

    public static bool TryParse(IReadOnlyList<int>? raw, out int[] horizons, out string? error)
    {
        if (raw is null || raw.Count == 0)
        {
            horizons = DefaultHorizons;
            error = null;
            return true;
        }

        if (raw.Any(horizon => horizon < MinHorizonHours || horizon > MaxHorizonHours))
        {
            horizons = [];
            error = "Horizons must be integers from 1 to 6.";
            return false;
        }

        horizons = raw.Distinct().OrderBy(horizon => horizon).ToArray();
        error = null;
        return true;
    }

    public static bool TryParse(IEnumerable<string>? raw, out int[] horizons, out string? error)
    {
        if (raw is null || !raw.Any(value => !string.IsNullOrWhiteSpace(value)))
        {
            horizons = DefaultHorizons;
            error = null;
            return true;
        }

        var numbers = new List<int>();
        foreach (var part in raw.SelectMany(value =>
                     value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
        {
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var horizon)
                || horizon < MinHorizonHours
                || horizon > MaxHorizonHours)
            {
                horizons = [];
                error = "Horizons must be integers from 1 to 6.";
                return false;
            }

            numbers.Add(horizon);
        }

        if (numbers.Count == 0)
        {
            horizons = DefaultHorizons;
            error = null;
            return true;
        }

        horizons = numbers.Distinct().OrderBy(horizon => horizon).ToArray();
        error = null;
        return true;
    }
}
