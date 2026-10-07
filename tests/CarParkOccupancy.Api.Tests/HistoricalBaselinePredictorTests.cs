using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json;
using CarParkOccupancy.Api.Models;
using CarParkOccupancy.Api.Options;
using CarParkOccupancy.Api.Services.Prediction;

namespace CarParkOccupancy.Api.Tests;

public sealed class HistoricalBaselinePredictorTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static DateTimeOffset Hkt(int year, int month, int day, int hour, int minute = 0) =>
        new(year, month, day, hour, minute, 0, TimeSpan.FromHours(8));

    private static OccupancyObservation Obs(DateTimeOffset time, int occupied, int capacity = 100) =>
        new()
        {
            SnapshotTime = time,
            Occupied = occupied,
            Capacity = capacity
        };

    private static HistoricalBaselinePredictor CreatePredictor(double decay = 0.5, int recentHours = 6) =>
        new(new PredictionOptions
        {
            Timezone = "Asia/Hong_Kong",
            TrendDecay = decay,
            RecentTrendHours = recentHours
        });

    [Fact]
    public void Api_targets_net10()
    {
        var framework = typeof(HistoricalBaselinePredictor).Assembly
            .GetCustomAttribute<TargetFrameworkAttribute>()
            ?.FrameworkName;

        Assert.Equal(".NETCoreApp,Version=v10.0", framework);
    }

    [Fact]
    public void Uses_same_weekday_hour_average_and_decays_recent_trend()
    {
        var asOf = Hkt(2026, 10, 7, 10);
        var history = new List<OccupancyObservation>
        {
            Obs(Hkt(2026, 9, 23, 10), 50),
            Obs(Hkt(2026, 9, 30, 10), 50),
            Obs(Hkt(2026, 9, 23, 11), 40),
            Obs(Hkt(2026, 9, 30, 11), 40),
            Obs(Hkt(2026, 9, 23, 12), 20),
            Obs(Hkt(2026, 9, 30, 12), 20),
            Obs(Hkt(2026, 10, 1, 11), 90),
            Obs(Hkt(2026, 10, 7, 5), 80),
            Obs(Hkt(2026, 10, 7, 6), 80),
            Obs(Hkt(2026, 10, 7, 7), 80),
            Obs(Hkt(2026, 10, 7, 8), 80),
            Obs(Hkt(2026, 10, 7, 9), 80),
            Obs(Hkt(2026, 10, 7, 10), 80),
            Obs(Hkt(2026, 10, 14, 11), 0)
        };

        var result = CreatePredictor().Predict(new OccupancyPredictionRequest
        {
            CarParkCode = "CP001",
            AsOf = asOf,
            HorizonsHours = [1, 2],
            History = history
        });

        Assert.Equal(HistoricalBaselinePredictor.PredictorName, result.Method);
        Assert.Equal("0-100", result.OccupancyPercentScale);
        Assert.Equal("Asia/Hong_Kong", result.Timezone);
        Assert.Equal(6, result.RecentSampleCount);

        var first = Assert.Single(result.Predictions, prediction => prediction.HorizonHours == 1);
        Assert.Equal(BaselineSources.SameWeekdayHour, first.BaselineSource);
        Assert.Equal(2, first.HistoricalSampleCount);
        Assert.Equal(40, first.BaselinePercent);
        Assert.Equal(30, first.RecentTrendPercent);
        Assert.Equal(30, first.AppliedTrendPercent);
        Assert.Equal(70, first.PredictedOccupancyPercent);
        Assert.Equal(asOf.AddHours(1), first.TargetTime);

        var second = Assert.Single(result.Predictions, prediction => prediction.HorizonHours == 2);
        Assert.Equal(20, second.BaselinePercent);
        Assert.Equal(15, second.AppliedTrendPercent);
        Assert.Equal(35, second.PredictedOccupancyPercent);
    }

    [Fact]
    public void Clamps_prediction_to_0_100_percent()
    {
        var asOf = Hkt(2026, 10, 7, 10);
        var history = new List<OccupancyObservation>
        {
            Obs(Hkt(2026, 9, 23, 10), 50),
            Obs(Hkt(2026, 9, 30, 10), 50),
            Obs(Hkt(2026, 9, 23, 11), 90),
            Obs(Hkt(2026, 9, 30, 11), 90),
            Obs(Hkt(2026, 10, 7, 8), 70),
            Obs(Hkt(2026, 10, 7, 9), 70),
            Obs(Hkt(2026, 10, 7, 10), 70)
        };

        var high = CreatePredictor(decay: 1).Predict(new OccupancyPredictionRequest
        {
            CarParkCode = "CP001",
            AsOf = asOf,
            HorizonsHours = [1],
            History = history
        });

        Assert.Equal(100, Assert.Single(high.Predictions).PredictedOccupancyPercent);

        var lowHistory = new List<OccupancyObservation>
        {
            Obs(Hkt(2026, 9, 23, 10), 80),
            Obs(Hkt(2026, 9, 30, 10), 80),
            Obs(Hkt(2026, 9, 23, 11), 10),
            Obs(Hkt(2026, 9, 30, 11), 10),
            Obs(Hkt(2026, 10, 7, 10), 40)
        };

        var low = CreatePredictor(decay: 1).Predict(new OccupancyPredictionRequest
        {
            CarParkCode = "CP001",
            AsOf = asOf,
            HorizonsHours = [1],
            History = lowHistory
        });

        Assert.Equal(0, Assert.Single(low.Predictions).PredictedOccupancyPercent);
    }

    [Fact]
    public void Reports_percent_not_a_fraction_and_serializes_the_wire_name()
    {
        var asOf = Hkt(2026, 10, 7, 10);
        var result = CreatePredictor().Predict(new OccupancyPredictionRequest
        {
            CarParkCode = "CP001",
            AsOf = asOf,
            HorizonsHours = [1],
            History = [Obs(Hkt(2026, 9, 30, 11), occupied: 50, capacity: 200)]
        });

        var prediction = Assert.Single(result.Predictions);
        Assert.Equal(25, prediction.PredictedOccupancyPercent);
        Assert.Equal(BaselineSources.SameWeekdayHour, prediction.BaselineSource);

        var json = JsonSerializer.Serialize(result, JsonOptions);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(25, document.RootElement.GetProperty("predictions")[0].GetProperty("predictedOccupancyPercent").GetDouble());
        Assert.Equal("0-100", document.RootElement.GetProperty("occupancyPercentScale").GetString());
        Assert.Equal(HistoricalBaselinePredictor.PredictorName, document.RootElement.GetProperty("method").GetString());
    }

    [Fact]
    public void Falls_back_to_same_hour_when_weekday_has_no_samples()
    {
        var asOf = Hkt(2026, 10, 7, 10);
        var result = CreatePredictor().Predict(new OccupancyPredictionRequest
        {
            CarParkCode = "CP001",
            AsOf = asOf,
            HorizonsHours = [1],
            History = [Obs(Hkt(2026, 10, 1, 11), 55)]
        });

        var prediction = Assert.Single(result.Predictions);
        Assert.Equal(BaselineSources.SameHour, prediction.BaselineSource);
        Assert.Equal(55, prediction.PredictedOccupancyPercent);
        Assert.Equal(1, prediction.HistoricalSampleCount);
    }

    [Fact]
    public void Groups_snapshots_inside_one_clock_hour_before_averaging_weeks()
    {
        var asOf = Hkt(2026, 10, 7, 10);
        var result = CreatePredictor().Predict(new OccupancyPredictionRequest
        {
            CarParkCode = "CP001",
            AsOf = asOf,
            HorizonsHours = [1],
            History =
            [
                Obs(Hkt(2026, 9, 23, 11, 0), 0),
                Obs(Hkt(2026, 9, 23, 11, 30), 100),
                Obs(Hkt(2026, 9, 30, 11), 80)
            ]
        });

        var prediction = Assert.Single(result.Predictions);
        Assert.Equal(2, prediction.HistoricalSampleCount);
        Assert.Equal(65, prediction.BaselinePercent);
        Assert.Equal(65, prediction.PredictedOccupancyPercent);
    }

    [Fact]
    public void Uses_recent_average_when_no_older_history_exists()
    {
        var asOf = Hkt(2026, 10, 7, 10);
        var result = CreatePredictor().Predict(new OccupancyPredictionRequest
        {
            CarParkCode = "CP001",
            AsOf = asOf,
            HorizonsHours = [1],
            History = [Obs(Hkt(2026, 10, 7, 9), 80), Obs(Hkt(2026, 10, 7, 10), 80)]
        });

        var prediction = Assert.Single(result.Predictions);
        Assert.Equal(BaselineSources.RecentOnly, prediction.BaselineSource);
        Assert.Equal(80, prediction.PredictedOccupancyPercent);
        Assert.Equal(0, prediction.HistoricalSampleCount);
        Assert.Null(prediction.RecentTrendPercent);
    }

    [Fact]
    public void Returns_null_predictions_when_history_is_empty()
    {
        var result = CreatePredictor().Predict(new OccupancyPredictionRequest
        {
            CarParkCode = "CP001",
            AsOf = Hkt(2026, 10, 7, 10),
            HorizonsHours = [1, 6],
            History = []
        });

        Assert.Equal(2, result.Predictions.Count);
        Assert.All(result.Predictions, prediction =>
        {
            Assert.Null(prediction.PredictedOccupancyPercent);
            Assert.Equal(BaselineSources.None, prediction.BaselineSource);
            Assert.Equal(0, prediction.HistoricalSampleCount);
        });
    }

    [Fact]
    public void Ignores_zero_capacity_and_rejects_horizons_outside_1_to_6()
    {
        var asOf = Hkt(2026, 10, 7, 10);
        var result = CreatePredictor().Predict(new OccupancyPredictionRequest
        {
            CarParkCode = "CP001",
            AsOf = asOf,
            HorizonsHours = [1],
            History =
            [
                Obs(Hkt(2026, 9, 30, 11), occupied: 0, capacity: 0),
                Obs(Hkt(2026, 9, 30, 11, 15), 40)
            ]
        });

        Assert.Equal(40, Assert.Single(result.Predictions).PredictedOccupancyPercent);

        var predictor = CreatePredictor();
        Assert.Throws<ArgumentOutOfRangeException>(() => predictor.Predict(new OccupancyPredictionRequest
        {
            CarParkCode = "CP001",
            AsOf = asOf,
            HorizonsHours = [0],
            History = []
        }));
    }
}
