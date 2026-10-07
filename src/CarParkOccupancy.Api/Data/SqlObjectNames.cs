using CarParkOccupancy.Api.Options;

namespace CarParkOccupancy.Api.Data;

public sealed class SqlObjectNames
{
    public SqlObjectNames(CarParkDataOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var columns = options.Columns ?? throw new ArgumentException("CarParkData:Columns is required.");

        QualifiedTable = SqlIdentifier.Bracket(options.Schema) + "." + SqlIdentifier.Bracket(options.TableName);
        CarParkCode = SqlIdentifier.Bracket(columns.CarParkCode);
        SnapshotTime = SqlIdentifier.Bracket(columns.SnapshotTime);
        CountingCategory = SqlIdentifier.Bracket(columns.CountingCategory);
        Capacity = SqlIdentifier.Bracket(columns.Capacity);
        Occupied = SqlIdentifier.Bracket(columns.Occupied);
    }

    public string QualifiedTable { get; }

    public string CarParkCode { get; }

    public string SnapshotTime { get; }

    public string CountingCategory { get; }

    public string Capacity { get; }

    public string Occupied { get; }
}
