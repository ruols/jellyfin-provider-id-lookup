using Jellyfin.Plugin.ProviderIdLookup;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace ProviderIdLookup.Tests;

public class ParserTests
{
    [Theory]
    [InlineData("Tmdb.575265", "Tmdb", "575265")]
    [InlineData(" imdb.tt1234567 ", "Imdb", "tt1234567")]
    [InlineData("tvdb.123", "Tvdb", "123")]
    [InlineData("Custom.some.id", "Custom", "some.id")]
    public void ParsesFirstDot(string input, string provider, string id)
    {
        Assert.True(ProviderIdParser.TryParse(input, out var pairs));
        Assert.Equal([new ProviderId(provider, id)], pairs);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Tmdb")]
    [InlineData("Tmdb.")]
    [InlineData(".12")]
    [InlineData("Tmdb.1,")]
    [InlineData("Tm db.1")]
    [InlineData("Tmdb.some id")]
    public void RejectsMalformedInput(string value) => Assert.False(ProviderIdParser.TryParse(value, out _));

    [Fact]
    public void RepeatedKeysAndSameProviderAreOrAlternatives()
    {
        Assert.True(ProviderIdParser.TryParse(new StringValues(["tmdb.1,Tmdb.2", "TMDB.1,Imdb.tt3"]), out var pairs));
        Assert.Equal(3, pairs.Length);
    }

    [Fact]
    public void BoundsInput()
    {
        Assert.False(ProviderIdParser.TryParse(new string('a', 2049), out _));
        Assert.False(ProviderIdParser.TryParse(string.Join(',', Enumerable.Repeat("Tmdb.1", 17)), out _));
    }
}
