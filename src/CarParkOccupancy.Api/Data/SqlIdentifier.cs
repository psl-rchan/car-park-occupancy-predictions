using System.Text.RegularExpressions;

namespace CarParkOccupancy.Api.Data;

/// <summary>
/// Quotes SQL Server identifiers that come from configuration.
/// Names are allow-listed so they cannot change the query.
/// </summary>
public static partial class SqlIdentifier
{
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    public static string Bracket(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier) || !NamePattern().IsMatch(identifier))
        {
            throw new ArgumentException(
                $"Invalid SQL identifier '{identifier}'. Use letters, digits, and underscores, and start with a letter or underscore.");
        }

        return "[" + identifier + "]";
    }
}
