import Foundation

/// Where a typed city was found: its latitude, longitude and the name Open-Meteo gives it. Saved with the settings so
/// the city is looked up once.
public struct WeatherPlace: Equatable {
    public let lat: Double
    public let lon: Double
    public let name: String

    public init(lat: Double, lon: Double, name: String) {
        self.lat = lat
        self.lon = lon
        self.name = name
    }

    public var isValid: Bool { (-90...90).contains(lat) && (-180...180).contains(lon) }
}

/// The weather for the city the user typed, from Open-Meteo: one lookup of the city on the geocoding host, then the
/// current temperature and weather code from the forecast host. Those two fixed https hosts are the only ones ever
/// asked, and the typed city is the only thing sent; the location is never guessed from the IP address. A city is
/// looked up once and kept (`place`, `remember`), a forecast is kept 30 minutes, a failure is not retried for 2, and
/// only one fetch runs at a time. When to ask at all (the switch, the phone drawing the widget) is the caller's
/// decision. The twin of the laptop core's WeatherClient.
public final class WeatherClient {
    public static let maxCityLength = 60
    public static let maxBytes = 16 * 1024
    public static let cityShown = 24
    public static let forecastFor: TimeInterval = 30 * 60
    public static let retryAfter: TimeInterval = 2 * 60

    private static let geocodeBase = "https://geocoding-api.open-meteo.com/v1/search?name="
    private static let forecastBase = "https://api.open-meteo.com/v1/forecast?latitude="
    private static let timeout: TimeInterval = 10

    private let http: HttpSender
    private let now: () -> Date
    private let gate = NSLock()
    private var placeCity: String?
    private var found: WeatherPlace?
    private var cachedCity: String?
    private var cached: WeatherInfo?
    private var nextAt = Date.distantPast
    private var inFlight: Task<WeatherInfo?, Never>?

    public init(http: HttpSender = CappedHttp(), clock: @escaping () -> Date = Date.init) {
        self.http = http
        self.now = clock
    }

    /// The place the last looked-up city resolved to, for the caller to save; nil before any lookup.
    public var place: WeatherPlace? {
        gate.lock()
        defer { gate.unlock() }
        return found
    }

    /// The typed city made safe to send: trimmed, 1..60 characters, no control characters; nil when it is none of
    /// those.
    public static func cityFrom(_ typed: String?) -> String? {
        guard let city = typed?.trimmingCharacters(in: .whitespacesAndNewlines),
            (1...maxCityLength).contains(city.utf16.count),
            !city.unicodeScalars.contains(where: { $0.properties.generalCategory == .control })
        else { return nil }
        return city
    }

    public static func geocodeUrl(_ city: String) -> String {
        geocodeBase + UrlText.escape(city) + "&count=1&language=en&format=json"
    }

    public static func forecastUrl(_ place: WeatherPlace) -> String {
        forecastBase + decimal(place.lat) + "&longitude=" + decimal(place.lon)
            + "&current=temperature_2m,weather_code&timezone=auto"
    }

    /// A place saved with the settings for `city`, so only the forecast is asked. An out-of-range place is ignored.
    public func remember(_ city: String, _ saved: WeatherPlace) {
        guard let clean = Self.cityFrom(city), saved.isValid else { return }
        gate.lock()
        defer { gate.unlock() }
        placeCity = clean
        found = saved
    }

    /// The current weather for `city`; nil when the city is unusable, unknown, or Open-Meteo could not be asked.
    public func get(_ city: String?) async -> WeatherInfo? {
        guard let clean = Self.cityFrom(city) else { return nil }
        gate.lock()
        if let running = inFlight {
            gate.unlock()
            return await running.value
        }
        if clean == cachedCity && now() < nextAt {
            let weather = cached
            gate.unlock()
            return weather
        }
        let task = Task { await self.fetch(clean) }
        inFlight = task
        gate.unlock()
        return await task.value
    }

    /// The first result of a geocoding reply; nil when there is none or its coordinates are out of range.
    public static func parsePlace(_ body: Data) -> WeatherPlace? {
        guard let root = JsonRead.object(body, maxDepth: 64), let results = root["results"] as? [Any],
            let first = results.first as? [String: Any], let lat = JsonRead.double(first["latitude"]),
            let lon = JsonRead.double(first["longitude"])
        else { return nil }
        let place = WeatherPlace(lat: lat, lon: lon, name: DashWire.clean(first["name"] as? String ?? "", cityShown))
        return place.isValid ? place : nil
    }

    /// The weather in a forecast reply, temperature -90..60 C and code 0..99; nil when either is missing or out of
    /// range.
    public static func parseForecast(_ body: Data, city: String) -> WeatherInfo? {
        guard let root = JsonRead.object(body, maxDepth: 64), let current = root["current"] as? [String: Any],
            let temp = JsonRead.double(current["temperature_2m"]), (-90...60).contains(temp),
            let code = JsonRead.double(current["weather_code"]), (0...99).contains(code), code == code.rounded(.down)
        else { return nil }
        let tenths = Int((temp * 10).rounded(.toNearestOrEven))
        return WeatherInfo(tempTenthsC: tenths, wmoCode: Int(code), city: DashWire.clean(city, cityShown))
    }

    /// Up to four decimals, no trailing zeros, a dot whatever the locale: .NET's "0.####".
    static func decimal(_ value: Double) -> String {
        let formatter = NumberFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.numberStyle = .decimal
        formatter.usesGroupingSeparator = false
        formatter.minimumIntegerDigits = 1
        formatter.minimumFractionDigits = 0
        formatter.maximumFractionDigits = 4
        formatter.roundingMode = .halfUp
        return formatter.string(from: NSNumber(value: value)) ?? "0"
    }

    private func fetch(_ city: String) async -> WeatherInfo? {
        gate.lock()
        let known = city == placeCity ? found : nil
        gate.unlock()
        var place = known
        if place == nil {
            place = await geocode(city)
        }
        var weather: WeatherInfo?
        if let place {
            weather = await forecast(place, city)
        }
        gate.lock()
        if let place {
            placeCity = city
            found = place
        }
        cachedCity = city
        cached = weather
        nextAt = now().addingTimeInterval(weather == nil ? Self.retryAfter : Self.forecastFor)
        inFlight = nil
        gate.unlock()
        return weather
    }

    private func geocode(_ city: String) async -> WeatherPlace? {
        guard let reply = await ask(Self.geocodeUrl(city), "Looking up the weather city"), reply.isSuccess else {
            return nil
        }
        return Self.parsePlace(reply.body)
    }

    private func forecast(_ place: WeatherPlace, _ city: String) async -> WeatherInfo? {
        guard let reply = await ask(Self.forecastUrl(place), "Fetching the weather"), reply.isSuccess else {
            return nil
        }
        return Self.parseForecast(reply.body, city: place.name.isEmpty ? city : place.name)
    }

    private func ask(_ url: String, _ what: String) async -> HttpReply? {
        guard let address = URL(string: url) else { return nil }
        var request = URLRequest(url: address, timeoutInterval: Self.timeout)
        request.httpMethod = "GET"
        return await http.send(request, maxBytes: Self.maxBytes, what: what)
    }
}
