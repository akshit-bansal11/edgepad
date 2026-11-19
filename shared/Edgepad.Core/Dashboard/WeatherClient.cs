using System.Globalization;
using System.Text.Json;

namespace Edgepad.Dashboard;

/// <summary>Where a typed city was found: its latitude, longitude and the name Open-Meteo gives it. Saved with the settings so the city is looked up once.</summary>
internal sealed record WeatherPlace(double Lat, double Lon, string Name)
{
    public bool IsValid => Lat is >= -90 and <= 90 && Lon is >= -180 and <= 180;
}

/// <summary>
/// The weather for the city the user typed, from Open-Meteo: one lookup of the city on the geocoding host, then the current
/// temperature and weather code from the forecast host. Those two fixed https hosts are the only ones ever asked, and the
/// typed city is the only thing sent; the location is never guessed from the IP address. A city is looked up once and kept
/// (<see cref="Place"/>, <see cref="Remember"/>), a forecast is kept 30 minutes, a failure is not retried for 2, and only one
/// fetch runs at a time. When to ask at all (the switch, the phone drawing the widget) is the caller's decision.
/// </summary>
internal sealed class WeatherClient(HttpMessageHandler? handler = null, Func<DateTimeOffset>? clock = null)
{
    public const int MaxCityLength = 60;
    public const int MaxBytes = 16 * 1024;
    public const int CityShown = 24;

    public static readonly TimeSpan ForecastFor = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(2);

    private const string GeocodeBase = "https://geocoding-api.open-meteo.com/v1/search?name=";
    private const string ForecastBase = "https://api.open-meteo.com/v1/forecast?latitude=";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly Lock gate = new();

    private string? placeCity;
    private WeatherPlace? place;
    private string? cachedCity;
    private WeatherInfo? cached;
    private DateTimeOffset nextAt;
    private Task<WeatherInfo?>? inFlight;

    /// <summary>The place the last looked-up city resolved to, for the caller to save; null before any lookup.</summary>
    public WeatherPlace? Place
    {
        get
        {
            lock (gate)
            {
                return place;
            }
        }
    }

    /// <summary>The typed city made safe to send: trimmed, 1..60 characters, no control characters; null when it is none of those.</summary>
    public static string? CityFrom(string? typed)
    {
        var city = typed?.Trim();
        return city is { Length: > 0 and <= MaxCityLength } && !city.Any(char.IsControl) ? city : null;
    }

    public static Uri GeocodeUri(string city) =>
        new($"{GeocodeBase}{Uri.EscapeDataString(city)}&count=1&language=en&format=json");

    public static Uri ForecastUri(WeatherPlace place) =>
        new(string.Create(
            CultureInfo.InvariantCulture,
            $"{ForecastBase}{place.Lat:0.####}&longitude={place.Lon:0.####}&current=temperature_2m,weather_code&timezone=auto"));

    /// <summary>A place saved with the settings for <paramref name="city"/>, so only the forecast is asked. An out-of-range place is ignored.</summary>
    public void Remember(string city, WeatherPlace saved)
    {
        if (CityFrom(city) is not { } clean || !saved.IsValid)
        {
            return;
        }

        lock (gate)
        {
            placeCity = clean;
            place = saved;
        }
    }

    /// <summary>The current weather for <paramref name="city"/>; null when the city is unusable, unknown, or Open-Meteo could not be asked.</summary>
    public Task<WeatherInfo?> GetAsync(string? city, CancellationToken ct)
    {
        if (CityFrom(city) is not { } clean)
        {
            return Task.FromResult<WeatherInfo?>(null);
        }

        lock (gate)
        {
            if (inFlight is { IsCompleted: false })
            {
                return inFlight;
            }

            if (clean == cachedCity && now() < nextAt)
            {
                return Task.FromResult(cached);
            }

            inFlight = FetchAsync(clean, ct);
            return inFlight;
        }
    }

    /// <summary>The first result of a geocoding reply; null when there is none or its coordinates are out of range.</summary>
    public static WeatherPlace? ParsePlace(byte[] body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array
                || results.GetArrayLength() == 0 || results[0] is not { ValueKind: JsonValueKind.Object } first
                || !TryNumber(first, "latitude", out var lat) || !TryNumber(first, "longitude", out var lon))
            {
                return null;
            }

            var name = first.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? string.Empty : string.Empty;
            var found = new WeatherPlace(lat, lon, DashWire.Clean(name, CityShown));
            return found.IsValid ? found : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The weather in a forecast reply, temperature -90..60 C and code 0..99; null when either is missing or out of range.</summary>
    public static WeatherInfo? ParseForecast(byte[] body, string city)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("current", out var current) || current.ValueKind != JsonValueKind.Object
                || !TryNumber(current, "temperature_2m", out var temp) || temp is < -90 or > 60
                || !TryNumber(current, "weather_code", out var code) || code is < 0 or > 99 || code != Math.Floor(code))
            {
                return null;
            }

            return new WeatherInfo((int)Math.Round(temp * 10), (int)code, DashWire.Clean(city, CityShown));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryNumber(JsonElement parent, string name, out double value)
    {
        value = 0;
        return parent.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out value) && double.IsFinite(value);
    }

    private async Task<WeatherInfo?> FetchAsync(string city, CancellationToken ct)
    {
        WeatherPlace? known;
        lock (gate)
        {
            known = city == placeCity ? place : null;
        }

        var found = known ?? await GeocodeAsync(city, ct).ConfigureAwait(false);
        var weather = found is null ? null : await ForecastAsync(found, city, ct).ConfigureAwait(false);
        lock (gate)
        {
            if (found is not null)
            {
                placeCity = city;
                place = found;
            }

            cachedCity = city;
            cached = weather;
            nextAt = now() + (weather is null ? RetryAfter : ForecastFor);
        }

        return weather;
    }

    private async Task<WeatherPlace?> GeocodeAsync(string city, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, GeocodeUri(city));
        var reply = await CappedHttp.SendAsync(request, MaxBytes, Timeout, handler, "Looking up the weather city", ct).ConfigureAwait(false);
        return reply is { IsSuccess: true } ? ParsePlace(reply.Body) : null;
    }

    private async Task<WeatherInfo?> ForecastAsync(WeatherPlace found, string city, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ForecastUri(found));
        var reply = await CappedHttp.SendAsync(request, MaxBytes, Timeout, handler, "Fetching the weather", ct).ConfigureAwait(false);
        return reply is { IsSuccess: true } ? ParseForecast(reply.Body, found.Name.Length > 0 ? found.Name : city) : null;
    }
}
