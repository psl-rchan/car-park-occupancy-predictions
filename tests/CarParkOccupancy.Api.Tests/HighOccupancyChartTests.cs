using CarParkOccupancy.Api.Models;
using CarParkOccupancy.Api.Options;
using CarParkOccupancy.Api.Ui;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CarParkOccupancy.Api.Tests;

public sealed class HighOccupancyChartTests
{
    private static readonly int[] Horizons = [1, 2, 3, 4, 5, 6];

    [Fact]
    public void Predicted_chart_includes_any_horizon_and_marks_parks_high_in_every_hour()
    {
        var parks = new[]
        {
            Park("CP-ALL", latest: 70, hours: [80, 81, 90, 88, 86, 82]),
            Park("CP-ONE", latest: 50, hours: [40, 55, 70, 79.9, 80, 60]),
            Park("CP-LOW", latest: 79.9, hours: [10, 20, 30, 40, 50, 79.9]),
            Park("CP-MISS", latest: 95, hours: [90, null, 90, 90, 90, 90], found: false),
            Park("CP-NULL", latest: null, hours: [null, null, null, null, null, 81])
        };

        var predicted = HighOccupancyCharts.Predicted(parks, Horizons, 80);

        Assert.Equal(["CP-ALL", "CP-NULL", "CP-ONE"], predicted.Select(park => park.Code).ToArray());
        Assert.True(predicted[0].HighInEveryHorizon);
        Assert.Equal("all", predicted[0].Span);
        Assert.Equal(90, predicted[0].MaxPredictedPercent);
        Assert.Equal(3, predicted[0].MaxHorizonHours);
        Assert.False(predicted[1].HighInEveryHorizon);
        Assert.Equal(6, predicted[1].MaxHorizonHours);
        Assert.Equal("any", predicted[2].Span);
        Assert.Equal(5, predicted[2].MaxHorizonHours);
        Assert.Equal([false, false, false, false, true, false], predicted[2].Hours.Select(hour => hour.IsHigh).ToArray());

        Assert.DoesNotContain("CP-MISS", predicted.Select(park => park.Code));
        Assert.Empty(HighOccupancyCharts.Current(parks, 80));
    }

    [Fact]
    public void Current_chart_uses_the_latest_snapshot_and_includes_the_threshold()
    {
        var parks = new[]
        {
            Park("CPB", latest: 80, hours: [10, 10, 10, 10, 10, 10], snapshot: DateTimeOffset.Parse("2026-10-07T01:00:00Z")),
            Park("CPA", latest: 91.2, hours: [10, 10, 10, 10, 10, 10]),
            Park("CP-EDGE", latest: 79.9, hours: [99, 99, 99, 99, 99, 99]),
            Park("CP-GONE", latest: 100, hours: [100, 100, 100, 100, 100, 100], found: false)
        };

        var current = HighOccupancyCharts.Current(parks, 80);
        Assert.Equal(["CPA", "CPB"], current.Select(park => park.Code).ToArray());
        Assert.Equal(91.2, current[0].OccupancyPercent);
        Assert.Equal(DateTimeOffset.Parse("2026-10-07T01:00:00Z"), current[1].SnapshotTime);

        var predicted = HighOccupancyCharts.Predicted(parks, Horizons, 80);
        Assert.Equal(["CP-EDGE"], predicted.Select(park => park.Code).ToArray());
    }

    [Fact]
    public void Tied_max_keeps_the_earlier_horizon_and_sorts_codes()
    {
        var parks = new[]
        {
            Park("CPB", latest: 10, hours: [80, 85, 85, 70, 70, 70]),
            Park("CPA", latest: 10, hours: [85, 85, 70, 70, 70, 70])
        };

        var predicted = HighOccupancyCharts.Predicted(parks, Horizons, 80);
        Assert.Equal(["CPA", "CPB"], predicted.Select(park => park.Code).ToArray());
        Assert.Equal(1, predicted[0].MaxHorizonHours);
        Assert.Equal(2, predicted[1].MaxHorizonHours);
    }

    [Theory]
    [InlineData(80, true)]
    [InlineData(80.1, false)]
    [InlineData(0, true)]
    public void Threshold_comparison_is_inclusive(double threshold, bool expected)
    {
        Assert.Equal(expected, HighOccupancyCharts.IsAtOrAbove(80, threshold));
    }

    [Fact]
    public void Non_finite_percents_are_not_high()
    {
        Assert.False(HighOccupancyCharts.IsAtOrAbove(null, 80));
        Assert.False(HighOccupancyCharts.IsAtOrAbove(double.NaN, 80));
        Assert.False(HighOccupancyCharts.IsAtOrAbove(double.PositiveInfinity, 80));
        Assert.False(HighOccupancyCharts.IsAtOrAbove(80, double.NaN));
    }

    [Fact]
    public void Threshold_label_and_mark_use_the_percent_scale()
    {
        Assert.Equal("80", HighOccupancyCharts.FormatThreshold(80));
        Assert.Equal("82.5", HighOccupancyCharts.FormatThreshold(82.5));
        Assert.Equal("80%", HighOccupancyCharts.MarkLeft(80));
        Assert.Equal("0%", HighOccupancyCharts.MarkLeft(double.NaN));
        Assert.Equal("100%", HighOccupancyCharts.MarkLeft(140));
    }

    [Fact]
    public void Options_reject_a_threshold_outside_0_to_100()
    {
        double[] rejected = [-0.1, 100.1, double.NaN, double.PositiveInfinity, double.NegativeInfinity];
        foreach (var threshold in rejected)
        {
            var validator = new CarParkDataOptionsValidator(
                new ConfigurationBuilder().Build(),
                new TestEnvironment());
            var result = validator.Validate(null, new CarParkDataOptions
            {
                Source = "Sample",
                HighOccupancyThresholdPercent = threshold
            });

            Assert.False(result.Succeeded);
            Assert.Contains("HighOccupancyThresholdPercent", result.FailureMessage);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(80)]
    [InlineData(100)]
    public void Options_accept_a_threshold_from_0_to_100(double threshold)
    {
        var validator = new CarParkDataOptionsValidator(
            new ConfigurationBuilder().Build(),
            new TestEnvironment());
        var result = validator.Validate(null, new CarParkDataOptions
        {
            Source = "Sample",
            HighOccupancyThresholdPercent = threshold
        });

        Assert.True(result.Succeeded);
    }

    private static ParkOccupancyInput Park(
        string code,
        double? latest,
        double?[] hours,
        bool found = true,
        DateTimeOffset? snapshot = null) =>
        new()
        {
            Code = code,
            Found = found,
            LatestOccupancyPercent = latest,
            LatestSnapshotTime = snapshot,
            Predictions = hours.Select((percent, index) => new HourlyPrediction
            {
                HorizonHours = index + 1,
                TargetTime = DateTimeOffset.UnixEpoch.AddHours(index + 1),
                PredictedOccupancyPercent = percent
            }).ToArray()
        };

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "CarParkOccupancy.Api.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
