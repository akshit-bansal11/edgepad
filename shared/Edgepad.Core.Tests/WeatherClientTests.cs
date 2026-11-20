using System.Net;
using Edgepad.Dashboard;
using Xunit;

namespace Edgepad.Tests;

public sealed class WeatherClientTests
{
    private const string Berlin = """{"results":[{"name":"Berlin","latitude":52.52437,"longitude":13.41053,"country":"Germany"}]}""";
    private const string Mild = """{"latitude":52.52,"current":{"time":"2026-11-19T12:00","temperature_2m":12.3,"weather_code":3}}""";

    private static readonly string[] Hosts = ["geocoding-api.open-meteo.com", "api.open-meteo.com"];

    private static readonly string[] BerlinUris =
    [
        "https://geocoding-api.open-meteo.com/v1/search?name=Berlin&count=1&language=en&format=json",
        "https://api.open-meteo.com/v1/forecast?latitude=52.5244&longitude=13.4105&current=temperature_2m,weather_code&timezone=auto",
    ];

    private readonly FakeClock clock = new();

    [Fact]
    public async Task ACityIsLookedUpThenItsForecastIsAskedAtTheExactAddresses()
    {
        var http = Serve(Berlin, Mild);
        var weather = await new WeatherClient(http, () => clock.Now).GetAsync("  Berlin ", TestContext.Current.CancellationToken);

        Assert.Equal(new WeatherInfo(123, 3, "Berlin"), weather);
        Assert.Equal(BerlinUris, http.Seen.Select(s => s.Uri.AbsoluteUri));
        Assert.All(http.Seen, s => Assert.Equal(HttpMethod.Get, s.Method));
    }

    [Theory]
    [InlineData("a&b#c", "a%26b%23c")]
    [InlineData("../etc/passwd", "..%2Fetc%2Fpasswd")]
    [InlineData("New York", "New%20York")]
    [InlineData("München", "M%C3%BCnchen")]
    [InlineData("東京", "%E6%9D%B1%E4%BA%AC")]
    [InlineData("x?name=y&count=9", "x%3Fname%3Dy%26count%3D9")]
    public async Task AHostileCityStaysInsideTheNameParameter(string city, string escaped)
    {
        var http = Serve(Berlin, Mild);
        await new WeatherClient(http, () => clock.Now).GetAsync(city, TestContext.Current.CancellationToken);

        Assert.Equal($"https://geocoding-api.open-meteo.com/v1/search?name={escaped}&count=1&language=en&format=json", http.Seen[0].Uri.AbsoluteUri);
        Assert.All(http.Seen, s => Assert.Contains(s.Uri.Host, Hosts));
        Assert.All(http.Seen, s => Assert.Equal("https", s.Uri.Scheme));
    }

    [Theory]
    [InlineData((string?)null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ber\nlin")]
    [InlineData("Ber\u0007lin")]
    public async Task AnUnusableCityAsksNobody(string? city)
    {
        var http = Serve(Berlin, Mild);
        Assert.Null(await new WeatherClient(http, () => clock.Now).GetAsync(city, TestContext.Current.CancellationToken));
        Assert.Empty(http.Seen);
    }

    [Fact]
    public async Task ACityOverSixtyCharactersIsRefused()
    {
        var http = Serve(Berlin, Mild);
        Assert.Null(await new WeatherClient(http, () => clock.Now).GetAsync(new string('a', 61), TestContext.Current.CancellationToken));
        Assert.NotNull(await new WeatherClient(http, () => clock.Now).GetAsync(new string('a', 60), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnUnknownCityGivesNothingAndNoForecastIsAsked()
    {
        var http = Serve("""{"generationtime_ms":0.5}""", Mild);
        Assert.Null(await new WeatherClient(http, () => clock.Now).GetAsync("Nowhere", TestContext.Current.CancellationToken));
        Assert.Single(http.Seen);
    }

    [Fact]
    public async Task AServerErrorGivesNothing()
    {
        var http = new FakeHttp(_ => FakeHttp.Status(HttpStatusCode.InternalServerError));
        Assert.Null(await new WeatherClient(http, () => clock.Now).GetAsync("Berlin", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ARedirectIsNeverFollowed()
    {
        var http = new FakeHttp(_ => FakeHttp.Redirect("https://evil.example/v1/search"));
        Assert.Null(await new WeatherClient(http, () => clock.Now).GetAsync("Berlin", TestContext.Current.CancellationToken));
        Assert.Single(http.Seen);
        Assert.Equal("geocoding-api.open-meteo.com", http.Seen[0].Uri.Host);
    }

    [Fact]
    public async Task AReplyOverSixteenKibIsRefused()
    {
        var huge = """{"results":[{"name":"Berlin","latitude":52.5,"longitude":13.4,"pad":""" + "\"" + new string('x', WeatherClient.MaxBytes) + "\"}]}";
        var http = Serve(huge, Mild);
        Assert.Null(await new WeatherClient(http, () => clock.Now).GetAsync("Berlin", TestContext.Current.CancellationToken));
        Assert.Single(http.Seen);
    }

    [Theory]
    [InlineData("""{"current":{"temperature_2m":"NaN","weather_code":3}}""")]
    [InlineData("""{"current":{"temperature_2m":60.1,"weather_code":3}}""")]
    [InlineData("""{"current":{"temperature_2m":-90.5,"weather_code":3}}""")]
    [InlineData("""{"current":{"temperature_2m":10,"weather_code":100}}""")]
    [InlineData("""{"current":{"temperature_2m":10,"weather_code":-1}}""")]
    [InlineData("""{"current":{"temperature_2m":10,"weather_code":2.5}}""")]
    [InlineData("""{"current":{"temperature_2m":10}}""")]
    [InlineData("""{"current":null}""")]
    [InlineData("""not json""")]
    public async Task AForecastOutOfRangeOrUnreadableIsDropped(string forecast)
    {
        var http = Serve(Berlin, forecast);
        Assert.Null(await new WeatherClient(http, () => clock.Now).GetAsync("Berlin", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("""{"results":[{"name":"X","latitude":91,"longitude":0}]}""")]
    [InlineData("""{"results":[{"name":"X","latitude":0,"longitude":-180.5}]}""")]
    [InlineData("""{"results":[{"name":"X","latitude":"1","longitude":2}]}""")]
    [InlineData("""{"results":[]}""")]
    [InlineData("""{"results":{}}""")]
    public async Task APlaceOutOfRangeIsDropped(string geocode)
    {
        var http = Serve(geocode, Mild);
        Assert.Null(await new WeatherClient(http, () => clock.Now).GetAsync("Berlin", TestContext.Current.CancellationToken));
        Assert.Single(http.Seen);
    }

    [Fact]
    public async Task AHostilePlaceNameIsCleanedAndCut()
    {
        var http = Serve($$"""{"results":[{"name":"Evil{{(char)0x202E}}\ttown/with:a very long name indeed","latitude":1,"longitude":2}]}""", Mild);
        var weather = await new WeatherClient(http, () => clock.Now).GetAsync("Berlin", TestContext.Current.CancellationToken);
        Assert.Equal("Evil town with a very lo", weather!.City);
    }

    [Fact]
    public async Task AForecastIsKeptThirtyMinutesAndTheCityIsLookedUpOnce()
    {
        var http = Serve(Berlin, Mild);
        var client = new WeatherClient(http, () => clock.Now);
        var ct = TestContext.Current.CancellationToken;

        await client.GetAsync("Berlin", ct);
        clock.Advance(TimeSpan.FromMinutes(29));
        Assert.NotNull(await client.GetAsync("Berlin", ct));
        Assert.Equal(2, http.Seen.Count);

        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.NotNull(await client.GetAsync("Berlin", ct));
        Assert.Equal(3, http.Seen.Count);
        Assert.Equal("api.open-meteo.com", http.Seen[2].Uri.Host);
        Assert.Equal(new WeatherPlace(52.52437, 13.41053, "Berlin"), client.Place);
    }

    [Fact]
    public async Task AFailureIsRetriedAfterTwoMinutesNotBefore()
    {
        var fail = true;
        var http = new FakeHttp(_ => fail ? FakeHttp.Status(HttpStatusCode.InternalServerError) : FakeHttp.Json(Berlin));
        var client = new WeatherClient(http, () => clock.Now);
        var ct = TestContext.Current.CancellationToken;

        Assert.Null(await client.GetAsync("Berlin", ct));
        fail = false;
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Null(await client.GetAsync("Berlin", ct));
        Assert.Single(http.Seen);

        clock.Advance(TimeSpan.FromMinutes(2));
        await client.GetAsync("Berlin", ct);
        Assert.Equal("geocoding-api.open-meteo.com", http.Seen[1].Uri.Host);
    }

    [Fact]
    public async Task ARememberedPlaceSkipsTheLookup()
    {
        var http = Serve(Berlin, Mild);
        var client = new WeatherClient(http, () => clock.Now);
        client.Remember("Berlin", new WeatherPlace(52.52, 13.41, "Berlin"));

        Assert.Equal(new WeatherInfo(123, 3, "Berlin"), await client.GetAsync("Berlin", TestContext.Current.CancellationToken));
        Assert.Single(http.Seen);
        Assert.Equal("api.open-meteo.com", http.Seen[0].Uri.Host);
    }

    [Fact]
    public async Task ARememberedPlaceOutOfRangeIsIgnored()
    {
        var http = Serve(Berlin, Mild);
        var client = new WeatherClient(http, () => clock.Now);
        client.Remember("Berlin", new WeatherPlace(200, 13.41, "Berlin"));

        await client.GetAsync("Berlin", TestContext.Current.CancellationToken);
        Assert.Equal("geocoding-api.open-meteo.com", http.Seen[0].Uri.Host);
    }

    [Fact]
    public async Task ANewCityIsAskedAtOnce()
    {
        var http = Serve(Berlin, Mild);
        var client = new WeatherClient(http, () => clock.Now);
        var ct = TestContext.Current.CancellationToken;

        await client.GetAsync("Berlin", ct);
        await client.GetAsync("Paris", ct);
        Assert.Equal(4, http.Seen.Count);
    }

    /// <summary>Geocoding answers <paramref name="geocode"/> and the forecast host <paramref name="forecast"/>; any other host fails the test.</summary>
    private static FakeHttp Serve(string geocode, string forecast) =>
        new(s => s.Uri.Host switch
        {
            "geocoding-api.open-meteo.com" => FakeHttp.Json(geocode),
            "api.open-meteo.com" => FakeHttp.Json(forecast),
            _ => throw new InvalidOperationException($"Asked {s.Uri.Host}"),
        });
}
