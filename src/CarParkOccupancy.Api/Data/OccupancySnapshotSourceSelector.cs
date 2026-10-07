namespace CarParkOccupancy.Api.Data;

/// <summary>
/// Picks Sql, Http, or Sample from configuration.
/// An empty source keeps the previous Development sample path:
/// <c>CarParkData:UseSampleSnapshots</c> with no connection string.
/// <c>Source=Sql</c> still follows that path so the committed default does not
/// turn the local preview off. <c>Source=Http</c> and <c>Source=Sample</c> are explicit.
/// </summary>
public static class OccupancySnapshotSourceSelector
{
    public static bool TryResolve(
        string? configuredSource,
        bool useSampleSnapshots,
        string? connectionString,
        bool isDevelopment,
        out OccupancySnapshotSourceKind kind,
        out string? error)
    {
        kind = OccupancySnapshotSourceKind.Sql;
        error = null;

        var trimmed = configuredSource?.Trim() ?? string.Empty;
        var explicitSource = trimmed.Length > 0;
        if (explicitSource && !TryParse(trimmed, out kind))
        {
            error = "CarParkData:Source must be Sql, Http, or Sample.";
            kind = OccupancySnapshotSourceKind.Sql;
            return false;
        }

        var sampleOverride = useSampleSnapshots
            && isDevelopment
            && string.IsNullOrWhiteSpace(connectionString);

        if (!explicitSource || kind == OccupancySnapshotSourceKind.Sql)
        {
            if (sampleOverride)
            {
                kind = OccupancySnapshotSourceKind.Sample;
            }

            return true;
        }

        return true;
    }

    public static OccupancySnapshotSourceKind Resolve(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        if (!TryResolve(
                configuration["CarParkData:Source"],
                configuration.GetValue("CarParkData:UseSampleSnapshots", false),
                configuration.GetConnectionString(SqlConnectionFactory.ConnectionStringName),
                environment.IsDevelopment(),
                out var kind,
                out var error))
        {
            throw new InvalidOperationException(error);
        }

        return kind;
    }

    public static bool TryParse(string value, out OccupancySnapshotSourceKind kind)
    {
        if (value.Equals("Sql", StringComparison.OrdinalIgnoreCase))
        {
            kind = OccupancySnapshotSourceKind.Sql;
            return true;
        }

        if (value.Equals("Http", StringComparison.OrdinalIgnoreCase))
        {
            kind = OccupancySnapshotSourceKind.Http;
            return true;
        }

        if (value.Equals("Sample", StringComparison.OrdinalIgnoreCase))
        {
            kind = OccupancySnapshotSourceKind.Sample;
            return true;
        }

        kind = OccupancySnapshotSourceKind.Sql;
        return false;
    }
}
