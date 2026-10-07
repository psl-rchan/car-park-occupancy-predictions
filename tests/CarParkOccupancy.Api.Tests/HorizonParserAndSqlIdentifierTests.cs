using CarParkOccupancy.Api.Data;
using CarParkOccupancy.Api.Services.Prediction;

namespace CarParkOccupancy.Api.Tests;

public sealed class HorizonParserAndSqlIdentifierTests
{
    [Fact]
    public void Parses_default_comma_separated_and_repeated_horizons()
    {
        Assert.True(HorizonParser.TryParse((IEnumerable<string>?)null, out var defaults, out var defaultError));
        Assert.Null(defaultError);
        Assert.Equal([1, 2, 3, 4, 5, 6], defaults);

        Assert.True(HorizonParser.TryParse(["1, 3, 6"], out var parsed, out _));
        Assert.Equal([1, 3, 6], parsed);

        Assert.True(HorizonParser.TryParse(["2", "1", "2"], out var repeated, out _));
        Assert.Equal([1, 2], repeated);

        Assert.False(HorizonParser.TryParse(["0"], out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.False(HorizonParser.TryParse([7], out _, out _));
    }

    [Theory]
    [InlineData("CarParkCode", "[CarParkCode]")]
    [InlineData("Occupied", "[Occupied]")]
    [InlineData("_snapshot", "[_snapshot]")]
    public void Brackets_safe_identifiers(string name, string expected)
    {
        Assert.Equal(expected, SqlIdentifier.Bracket(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Car Park")]
    [InlineData("dbo.Table")]
    [InlineData("CarPark;DROP")]
    [InlineData("[CarPark]")]
    [InlineData("1Park")]
    public void Rejects_unsafe_identifiers(string name)
    {
        Assert.Throws<ArgumentException>(() => SqlIdentifier.Bracket(name));
    }
}
