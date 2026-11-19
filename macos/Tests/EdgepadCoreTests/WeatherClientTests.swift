import XCTest

@testable import EdgepadCore

final class WeatherClientTests: XCTestCase {
    private let berlin =
        #"{"results":[{"name":"Berlin","latitude":52.52437,"longitude":13.41053,"country":"Germany"}]}"#
    private let mild =
        #"{"latitude":52.52,"current":{"time":"2026-11-19T12:00","temperature_2m":12.3,"weather_code":3}}"#
    private let hosts = ["geocoding-api.open-meteo.com", "api.open-meteo.com"]
    private let berlinUrls = [
        "https://geocoding-api.open-meteo.com/v1/search?name=Berlin&count=1&language=en&format=json",
        "https://api.open-meteo.com/v1/forecast?latitude=52.5244&longitude=13.4105"
            + "&current=temperature_2m,weather_code&timezone=auto",
    ]
    private let clock = TestClock()

    /// Geocoding answers `geocode` and the forecast host `forecast`; any other host is a failure.
    private func serve(_ geocode: String, _ forecast: String) -> FakeHttp {
        FakeHttp { request in
            switch request.url?.host {
            case "geocoding-api.open-meteo.com": return FakeHttp.json200(geocode)
            case "api.open-meteo.com": return FakeHttp.json200(forecast)
            default: return nil
            }
        }
    }

    private func client(_ http: FakeHttp) -> WeatherClient {
        WeatherClient(http: http, clock: { [clock] in clock.now })
    }

    func testACityIsLookedUpThenItsForecastIsAskedAtTheExactAddresses() async {
        let http = serve(berlin, mild)
        let weather = await client(http).get("  Berlin ")

        XCTAssertEqual(weather, WeatherInfo(tempTenthsC: 123, wmoCode: 3, city: "Berlin"))
        XCTAssertEqual(http.seen.map(\.url), berlinUrls)
        XCTAssertTrue(http.seen.allSatisfy { $0.method == "GET" })
    }

    func testAHostileCityStaysInsideTheNameParameter() async {
        let cases = [
            ("a&b#c", "a%26b%23c"), ("../etc/passwd", "..%2Fetc%2Fpasswd"), ("New York", "New%20York"),
            ("München", "M%C3%BCnchen"), ("東京", "%E6%9D%B1%E4%BA%AC"),
            ("x?name=y&count=9", "x%3Fname%3Dy%26count%3D9"),
        ]
        for (city, escaped) in cases {
            let http = serve(berlin, mild)
            _ = await client(http).get(city)
            let expected =
                "https://geocoding-api.open-meteo.com/v1/search?name=" + escaped + "&count=1&language=en&format=json"
            XCTAssertEqual(http.seen.first?.url, expected)
            XCTAssertTrue(http.seen.allSatisfy { hosts.contains($0.host) && $0.url.hasPrefix("https://") })
        }
    }

    func testAnUnusableCityAsksNobody() async {
        let bell = String(UnicodeScalar(7))
        for city in [nil, "", "   ", "Ber" + "\n" + "lin", "Ber" + bell + "lin", String(repeating: "a", count: 61)] {
            let http = serve(berlin, mild)
            let weather = await client(http).get(city)
            XCTAssertNil(weather)
            XCTAssertTrue(http.seen.isEmpty)
        }
    }

    func testAnUnknownCityGivesNothingAndNoForecastIsAsked() async {
        let http = serve(#"{"results":[]}"#, mild)
        let weather = await client(http).get("Nowhere")
        XCTAssertNil(weather)
        XCTAssertEqual(http.seen.count, 1)
    }

    func testAServerErrorOrARedirectGivesNothing() async {
        for status in [500, 302] {
            let http = FakeHttp { _ in HttpReply(status: status, headers: ["Location": "https://evil.example/"]) }
            let weather = await client(http).get("Berlin")
            XCTAssertNil(weather)
            XCTAssertEqual(http.seen.count, 1)
        }
    }

    func testAReplyOverSixteenKibIsRefused() async {
        let http = serve(berlin + String(repeating: " ", count: WeatherClient.maxBytes), mild)
        let weather = await client(http).get("Berlin")
        XCTAssertNil(weather)
    }

    func testAForecastOutOfRangeOrUnreadableIsDropped() async {
        let forecasts = [
            #"{"current":{"temperature_2m":"NaN","weather_code":3}}"#,
            #"{"current":{"temperature_2m":60.1,"weather_code":3}}"#,
            #"{"current":{"temperature_2m":-90.5,"weather_code":3}}"#,
            #"{"current":{"temperature_2m":10,"weather_code":100}}"#,
            #"{"current":{"temperature_2m":10,"weather_code":-1}}"#,
            #"{"current":{"temperature_2m":10,"weather_code":2.5}}"#, #"{"current":{"temperature_2m":10}}"#,
            #"{"current":null}"#, "not json",
        ]
        for forecast in forecasts {
            let weather = await client(serve(berlin, forecast)).get("Berlin")
            XCTAssertNil(weather, forecast)
        }
    }

    func testAPlaceOutOfRangeIsDropped() async {
        let places = [
            #"{"results":[{"name":"X","latitude":91,"longitude":0}]}"#,
            #"{"results":[{"name":"X","latitude":0,"longitude":-180.5}]}"#,
            #"{"results":[{"name":"X","latitude":"1","longitude":2}]}"#, #"{"results":[]}"#, #"{"results":{}}"#,
        ]
        for geocode in places {
            let http = serve(geocode, mild)
            let weather = await client(http).get("Berlin")
            XCTAssertNil(weather, geocode)
            XCTAssertEqual(http.seen.count, 1, geocode)
        }
    }

    func testAHostilePlaceNameIsCleanedAndCut() async {
        let name = "Evil|City/With:Separators" + String(repeating: "x", count: 40)
        let geocode = #"{"results":[{"name":""# + name + #"","latitude":1,"longitude":2}]}"#
        let weather = await client(serve(geocode, mild)).get("Berlin")
        XCTAssertEqual(weather?.city, DashWire.clean(name, WeatherClient.cityShown))
        XCTAssertEqual(weather?.city.unicodeScalars.count, WeatherClient.cityShown)
    }

    func testAForecastIsKeptThirtyMinutesAndTheCityIsLookedUpOnce() async {
        let http = serve(berlin, mild)
        let weather = client(http)

        _ = await weather.get("Berlin")
        clock.advance(29 * 60)
        let kept = await weather.get("Berlin")
        XCTAssertNotNil(kept)
        XCTAssertEqual(http.seen.count, 2)

        clock.advance(2 * 60)
        let fresh = await weather.get("Berlin")
        XCTAssertNotNil(fresh)
        XCTAssertEqual(http.seen.count, 3)
        XCTAssertEqual(http.seen[2].host, "api.open-meteo.com")
        XCTAssertEqual(weather.place, WeatherPlace(lat: 52.52437, lon: 13.41053, name: "Berlin"))
    }

    func testAFailureIsRetriedAfterTwoMinutesNotBefore() async {
        var fail = true
        let http = FakeHttp { _ in fail ? HttpReply(status: 500) : FakeHttp.json200(self.berlin) }
        let weather = client(http)

        let first = await weather.get("Berlin")
        XCTAssertNil(first)
        fail = false
        clock.advance(60)
        let second = await weather.get("Berlin")
        XCTAssertNil(second)
        XCTAssertEqual(http.seen.count, 1)

        clock.advance(2 * 60)
        _ = await weather.get("Berlin")
        XCTAssertEqual(http.seen[1].host, "geocoding-api.open-meteo.com")
    }

    func testARememberedPlaceSkipsTheLookup() async {
        let http = serve(berlin, mild)
        let weather = client(http)
        weather.remember("Berlin", WeatherPlace(lat: 52.52, lon: 13.41, name: "Berlin"))

        let read = await weather.get("Berlin")

        XCTAssertEqual(read, WeatherInfo(tempTenthsC: 123, wmoCode: 3, city: "Berlin"))
        XCTAssertEqual(http.seen.map(\.host), ["api.open-meteo.com"])
    }

    func testARememberedPlaceOutOfRangeIsIgnored() async {
        let http = serve(berlin, mild)
        let weather = client(http)
        weather.remember("Berlin", WeatherPlace(lat: 200, lon: 13.41, name: "Berlin"))

        _ = await weather.get("Berlin")

        XCTAssertEqual(http.seen.first?.host, "geocoding-api.open-meteo.com")
    }

    func testANewCityIsAskedAtOnce() async {
        let http = serve(berlin, mild)
        let weather = client(http)
        _ = await weather.get("Berlin")
        _ = await weather.get("Paris")
        XCTAssertEqual(http.seen.count, 4)
    }

    func testCoordinatesAreWrittenWithAtMostFourDecimals() {
        XCTAssertEqual(WeatherClient.decimal(52.52437), "52.5244")
        XCTAssertEqual(WeatherClient.decimal(-9.14), "-9.14")
        XCTAssertEqual(WeatherClient.decimal(13), "13")
    }
}
