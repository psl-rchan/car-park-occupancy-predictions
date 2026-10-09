using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CarParkOccupancy.Api.Models;
using CarParkOccupancy.Api.Options;

namespace CarParkOccupancy.Api.Data;

/// <summary>
/// Maps third-party snapshot JSON onto <see cref="OccupancySnapshot"/>.
/// A JSON array, one snapshot object, or an object whose collection property is an array are accepted.
/// Property names come from <see cref="HttpSnapshotJsonNames"/> and are matched without case sensitivity.
/// </summary>
public static partial class OccupancySnapshotJson
{
    [GeneratedRegex(
        @"(?:Z|[+-]\d{2}:\d{2}|[+-]\d{4}|[+-]\d{2})$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OffsetSuffix();

    public static IReadOnlyList<OccupancySnapshot> Read(
        string json,
        HttpSnapshotJsonNames names,
        bool snapshotTimeIsUtc,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(timeZone);

        if (string.IsNullOrWhiteSpace(json))
        {
            throw Bad("The occupancy HTTP API returned JSON that does not match the snapshot contract.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw Bad("The occupancy HTTP API returned JSON that does not match the snapshot contract.");
        }

        using (document)
        {
            return ReadRoot(document.RootElement, names, snapshotTimeIsUtc, timeZone);
        }
    }

    private static IReadOnlyList<OccupancySnapshot> ReadRoot(
        JsonElement root,
        HttpSnapshotJsonNames names,
        bool snapshotTimeIsUtc,
        TimeZoneInfo timeZone)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return ReadArray(root, names, snapshotTimeIsUtc, timeZone);
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw Bad("Occupancy snapshot JSON must be an array, a snapshot object, or an object with a snapshot array.");
        }

        if (TryFind(root, names.Collection, out var collection) && collection.ValueKind == JsonValueKind.Array)
        {
            return ReadArray(collection, names, snapshotTimeIsUtc, timeZone);
        }

        if (TryFind(root, names.CarParkCode, out _))
        {
            return [ReadSnapshot(root, names, snapshotTimeIsUtc, timeZone)];
        }

        throw Bad(
            $"Occupancy snapshot JSON must be an array or an object with a '{names.Collection}' array.");
    }

    private static IReadOnlyList<OccupancySnapshot> ReadArray(
        JsonElement array,
        HttpSnapshotJsonNames names,
        bool snapshotTimeIsUtc,
        TimeZoneInfo timeZone)
    {
        var snapshots = new List<OccupancySnapshot>(array.GetArrayLength());
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw Bad("Occupancy snapshot JSON array items must be objects.");
            }

            snapshots.Add(ReadSnapshot(item, names, snapshotTimeIsUtc, timeZone));
        }

        return snapshots;
    }

    private static OccupancySnapshot ReadSnapshot(
        JsonElement element,
        HttpSnapshotJsonNames names,
        bool snapshotTimeIsUtc,
        TimeZoneInfo timeZone)
    {
        return new OccupancySnapshot
        {
            CarParkCode = ReadCode(element, names.CarParkCode),
            SnapshotTime = ReadTime(element, names.SnapshotTime, snapshotTimeIsUtc, timeZone),
            CountingCategory = ReadCategory(element, names.CountingCategory),
            Capacity = ReadInt(element, names.Capacity),
            Occupied = ReadInt(element, names.Occupied)
        };
    }

    private static string ReadCode(JsonElement element, string name)
    {
        if (!TryFind(element, name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw Bad($"Occupancy snapshot JSON is missing '{name}'.");
        }

        var code = value.GetString()?.Trim();
        if (string.IsNullOrEmpty(code))
        {
            throw Bad($"Occupancy snapshot JSON is missing '{name}'.");
        }

        return code;
    }

    private static string? ReadCategory(JsonElement element, string name)
    {
        if (!TryFind(element, name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw Bad($"Occupancy snapshot JSON property '{name}' must be a string.");
        }

        var text = value.GetString()?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static int ReadInt(JsonElement element, string name)
    {
        if (!TryFind(element, name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            throw Bad($"Occupancy snapshot JSON is missing '{name}'.");
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
        {
            return number;
        }

        throw Bad($"Occupancy snapshot JSON property '{name}' must be an integer.");
    }

    private static DateTimeOffset ReadTime(
        JsonElement element,
        string name,
        bool snapshotTimeIsUtc,
        TimeZoneInfo timeZone)
    {
        if (!TryFind(element, name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw Bad($"Occupancy snapshot JSON is missing '{name}'.");
        }

        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw Bad($"Occupancy snapshot JSON is missing '{name}'.");
        }

        if (OffsetSuffix().IsMatch(text.Trim()))
        {
            if (!DateTimeOffset.TryParse(
                    text,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var withOffset))
            {
                throw Bad($"Occupancy snapshot JSON has an unreadable '{name}'.");
            }

            return withOffset;
        }

        if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
        {
            throw Bad($"Occupancy snapshot JSON has an unreadable '{name}'.");
        }

        var wall = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
        if (snapshotTimeIsUtc)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(wall, DateTimeKind.Utc));
        }

        return new DateTimeOffset(wall, timeZone.GetUtcOffset(wall));
    }

    internal static bool TryFind(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty(name, out value))
            {
                return true;
            }

            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static CarParkDataUnavailableException Bad(string message) =>
        new(message, CarParkDataUnavailableException.BadGatewayStatus);
}
