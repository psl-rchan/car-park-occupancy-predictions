using CarParkOccupancy.Api.Models;
using CarParkOccupancy.Api.Options;
using CarParkOccupancy.Api.Services.Prediction;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace CarParkOccupancy.Api.Data;

public sealed class DapperCarParkReadStore : ICarParkReadStore
{
    private readonly IDbConnectionFactory _connections;
    private readonly SqlObjectNames _names;
    private readonly CarParkDataOptions _dataOptions;
    private readonly TimeZoneInfo _timeZone;
    private readonly ILogger<DapperCarParkReadStore> _logger;

    public DapperCarParkReadStore(
        IDbConnectionFactory connections,
        SqlObjectNames names,
        IOptions<CarParkDataOptions> dataOptions,
        IOptions<PredictionOptions> predictionOptions,
        ILogger<DapperCarParkReadStore> logger)
    {
        _connections = connections;
        _names = names;
        _dataOptions = dataOptions.Value;
        _timeZone = TimeZoneResolver.Resolve(predictionOptions.Value.Timezone);
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>> GetCarParkCodesAsync(CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT DISTINCT {_names.CarParkCode}
            FROM {_names.QualifiedTable}
            ORDER BY {_names.CarParkCode}
            """;

        var codes = await QueryAsync(
            connection => connection.QueryAsync<string>(Command(sql, null, cancellationToken)),
            cancellationToken);

        return codes.Where(code => !string.IsNullOrWhiteSpace(code)).ToArray();
    }

    public async Task<bool> ExistsAsync(string carParkCode, CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM {_names.QualifiedTable}
                WHERE {_names.CarParkCode} = @CarParkCode
            ) THEN 1 ELSE 0 END
            """;

        var found = await QueryAsync(
            connection => connection.ExecuteScalarAsync<int>(
                Command(sql, new { CarParkCode = carParkCode }, cancellationToken)),
            cancellationToken);

        return found == 1;
    }

    public async Task<IReadOnlyList<OccupancyObservation>> GetHistoryAsync(
        string carParkCode,
        string? countingCategory,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        var lookbackDays = Math.Max(1, _dataOptions.HistoryLookbackDays);
        var from = asOf.AddDays(-lookbackDays);
        var sql = $"""
            SELECT {_names.SnapshotTime} AS SnapshotTime,
                   SUM(CAST({_names.Occupied} AS BIGINT)) AS Occupied,
                   SUM(CAST({_names.Capacity} AS BIGINT)) AS Capacity
            FROM {_names.QualifiedTable}
            WHERE {_names.CarParkCode} = @CarParkCode
              AND {_names.SnapshotTime} >= @From
              AND {_names.SnapshotTime} <= @To
              AND (@CountingCategory IS NULL OR {_names.CountingCategory} = @CountingCategory)
            GROUP BY {_names.SnapshotTime}
            ORDER BY {_names.SnapshotTime}
            """;

        var rows = (await QueryAsync(
            connection => connection.QueryAsync<SnapshotRow>(
                Command(
                    sql,
                    new
                    {
                        CarParkCode = carParkCode,
                        From = ToQueryParameter(from),
                        To = ToQueryParameter(asOf),
                        CountingCategory = countingCategory
                    },
                    cancellationToken)),
            cancellationToken)).AsList();

        var observations = new List<OccupancyObservation>(rows.Count);
        foreach (var row in rows)
        {
            observations.Add(new OccupancyObservation
            {
                SnapshotTime = ToOffset(row.SnapshotTime),
                Occupied = ClampToInt(row.Occupied),
                Capacity = ClampToInt(row.Capacity)
            });
        }

        _logger.LogDebug(
            "Loaded {Count} occupancy snapshots for car park {CarParkCode}.",
            observations.Count,
            carParkCode);

        return observations;
    }

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            sql,
            parameters,
            commandTimeout: _dataOptions.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);
    }

    private async Task<T> QueryAsync<T>(Func<SqlConnection, Task<T>> query, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = _connections.Create();
            await connection.OpenAsync(cancellationToken);
            return await query(connection);
        }
        catch (CarParkDataUnavailableException)
        {
            throw;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Car park database query failed.");
            throw new CarParkDataUnavailableException("The car park database could not be queried.");
        }
    }

    private DateTime ToQueryParameter(DateTimeOffset instant)
    {
        if (_dataOptions.SnapshotTimeIsUtc)
        {
            return instant.UtcDateTime;
        }

        var local = TimeZoneInfo.ConvertTime(instant, _timeZone);
        return DateTime.SpecifyKind(local.DateTime, DateTimeKind.Unspecified);
    }

    private DateTimeOffset ToOffset(DateTime value)
    {
        if (_dataOptions.SnapshotTimeIsUtc || value.Kind == DateTimeKind.Utc)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
        }

        var unspecified = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        var offset = _timeZone.GetUtcOffset(unspecified);
        return new DateTimeOffset(unspecified, offset);
    }

    private static int ClampToInt(long value)
    {
        if (value > int.MaxValue)
        {
            return int.MaxValue;
        }

        if (value < int.MinValue)
        {
            return int.MinValue;
        }

        return (int)value;
    }

    private sealed class SnapshotRow
    {
        public DateTime SnapshotTime { get; set; }

        public long Occupied { get; set; }

        public long Capacity { get; set; }
    }
}
