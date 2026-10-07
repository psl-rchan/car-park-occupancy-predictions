using CarParkOccupancy.Api.Data;
using CarParkOccupancy.Api.Models;
using CarParkOccupancy.Api.Services;
using CarParkOccupancy.Api.Services.Prediction;
using Microsoft.AspNetCore.Mvc;

namespace CarParkOccupancy.Api.Controllers;

[ApiController]
[Route("api/carparks")]
public sealed class CarParksController : ControllerBase
{
    private readonly ICarParkReadStore _store;
    private readonly OccupancyPredictionService _predictions;

    public CarParksController(ICarParkReadStore store, OccupancyPredictionService predictions)
    {
        _store = store;
        _predictions = predictions;
    }

    [HttpGet]
    [ProducesResponseType(typeof(CarParkListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<CarParkListResponse>> GetCarParks(CancellationToken cancellationToken)
    {
        var codes = await _store.GetCarParkCodesAsync(cancellationToken);
        return Ok(new CarParkListResponse { CarParks = codes });
    }

    /// <summary>
    /// Hourly occupancy predictions for horizons 1 through 6.
    /// PredictedOccupancyPercent is a 0–100 percent of capacity.
    /// </summary>
    [HttpGet("{code}/predictions")]
    [ProducesResponseType(typeof(CarParkPredictionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CarParkPredictionResult>> GetPredictions(
        string code,
        [FromQuery(Name = "horizons")] string[]? horizons,
        [FromQuery] string? countingCategory,
        [FromQuery] DateTimeOffset? asOf,
        [FromQuery] string? predictor,
        CancellationToken cancellationToken)
    {
        if (!IsValidCode(code))
        {
            return BadRequest(Details(StatusCodes.Status400BadRequest, "Car park code is required and must be at most 64 characters."));
        }

        if (!IsValidCategory(countingCategory))
        {
            return BadRequest(Details(StatusCodes.Status400BadRequest, "countingCategory must be at most 64 characters."));
        }

        if (!HorizonParser.TryParse(horizons, out var parsedHorizons, out var horizonError))
        {
            return BadRequest(Details(StatusCodes.Status400BadRequest, horizonError!));
        }

        var prediction = await _predictions.PredictAsync(
            code,
            countingCategory,
            asOf ?? DateTimeOffset.UtcNow,
            parsedHorizons,
            predictor,
            cancellationToken);

        if (prediction is null)
        {
            return NotFound(Details(StatusCodes.Status404NotFound, $"Car park '{code.Trim()}' was not found."));
        }

        return Ok(prediction);
    }

    [HttpPost("predictions")]
    [ProducesResponseType(typeof(BatchPredictionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BatchPredictionResponse>> PostPredictions(
        [FromBody] BatchPredictionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.CarParkCodes is null || request.CarParkCodes.Count == 0)
        {
            return BadRequest(Details(StatusCodes.Status400BadRequest, "carParkCodes must contain at least one code."));
        }

        var codes = request.CarParkCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (codes.Length == 0)
        {
            return BadRequest(Details(StatusCodes.Status400BadRequest, "carParkCodes must contain at least one code."));
        }

        if (codes.Length > HorizonParser.MaxBatchSize)
        {
            return BadRequest(Details(
                StatusCodes.Status400BadRequest,
                $"A batch can include at most {HorizonParser.MaxBatchSize} car parks."));
        }

        if (codes.Any(code => !IsValidCode(code)))
        {
            return BadRequest(Details(StatusCodes.Status400BadRequest, "Each car park code must be at most 64 characters."));
        }

        if (!IsValidCategory(request.CountingCategory))
        {
            return BadRequest(Details(StatusCodes.Status400BadRequest, "countingCategory must be at most 64 characters."));
        }

        if (!HorizonParser.TryParse(request.Horizons, out var horizons, out var horizonError))
        {
            return BadRequest(Details(StatusCodes.Status400BadRequest, horizonError!));
        }

        var asOf = request.AsOf ?? DateTimeOffset.UtcNow;
        var results = new List<BatchPredictionItem>(codes.Length);
        foreach (var code in codes)
        {
            var prediction = await _predictions.PredictAsync(
                code,
                request.CountingCategory,
                asOf,
                horizons,
                request.Predictor,
                cancellationToken);

            results.Add(prediction is null
                ? new BatchPredictionItem
                {
                    CarParkCode = code,
                    Found = false,
                    Error = "Car park was not found."
                }
                : new BatchPredictionItem
                {
                    CarParkCode = code,
                    Found = true,
                    Prediction = prediction
                });
        }

        return Ok(new BatchPredictionResponse
        {
            GeneratedAt = asOf,
            Results = results
        });
    }

    private static bool IsValidCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var trimmed = code.Trim();
        return trimmed.Length is > 0 and <= 64 && trimmed.All(character => !char.IsControl(character));
    }

    private static bool IsValidCategory(string? countingCategory)
    {
        if (string.IsNullOrWhiteSpace(countingCategory))
        {
            return true;
        }

        var trimmed = countingCategory.Trim();
        return trimmed.Length <= 64 && trimmed.All(character => !char.IsControl(character));
    }

    private static ProblemDetails Details(int status, string title) =>
        new()
        {
            Status = status,
            Title = title
        };
}
