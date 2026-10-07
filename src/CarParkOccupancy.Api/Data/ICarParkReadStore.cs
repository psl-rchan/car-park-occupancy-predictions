using CarParkOccupancy.Api.Models;

namespace CarParkOccupancy.Api.Data;

public interface ICarParkReadStore
{
    Task<IReadOnlyList<string>> GetCarParkCodesAsync(CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string carParkCode, CancellationToken cancellationToken);

    Task<IReadOnlyList<OccupancyObservation>> GetHistoryAsync(
        string carParkCode,
        string? countingCategory,
        DateTimeOffset asOf,
        CancellationToken cancellationToken);
}
