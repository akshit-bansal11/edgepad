import CoreWLAN
import EdgepadCore
import Foundation
import SystemConfiguration

/// How this Mac is connected, for TEXT NET_INFO: the kind of the default route's interface, its IPv4 address and the
/// router's, and for Wi-Fi the transmit rate, signal and band from CoreWLAN. None of those needs Location access. The
/// network name (`ssid`, `bssid`) is never read or sent, here or anywhere: it would need Location access, and no
/// Wi-Fi name leaves this Mac. A reading takes a few small system calls, so it is cheap enough for once a second.
final class MacNetInfo: NetInfoSource {
    private let store = SCDynamicStoreCreate(nil, "edgepad.netinfo" as CFString, nil, nil)
    private let lock = NSLock()
    private var kinds: [String: String] = [:]

    func read() throws -> NetInfo {
        guard let store, let global = copy(store, "State:/Network/Global/IPv4"),
            let bsd = global["PrimaryInterface"] as? String, !bsd.isEmpty
        else { return NetInfo.unknown }
        let gateway = global["Router"] as? String ?? ""
        let own = copy(store, "State:/Network/Interface/\(bsd)/IPv4")
        let address = (own?["Addresses"] as? [String])?.first ?? ""
        let kind = kindOf(bsd)
        guard kind == NetKind.wifi.rawValue, let wifi = CWWiFiClient.shared().interface(withName: bsd) else {
            return NetInfo(kind: kind, band: "?", linkMbps: -1, signalPct: -1, localIp: address, gateway: gateway)
        }
        let rate = wifi.transmitRate()
        return NetInfo(
            kind: kind, band: Self.band(wifi.wlanChannel()?.channelBand),
            linkMbps: rate > 0 ? Int(rate.rounded()) : -1, signalPct: NetInfoWire.signalPercent(rssi: wifi.rssiValue()),
            localIp: address, gateway: gateway)
    }

    private func copy(_ store: SCDynamicStore, _ key: String) -> [String: Any]? {
        SCDynamicStoreCopyValue(store, key as CFString) as? [String: Any]
    }

    /// The kind token of an interface by its BSD name, looked up once.
    private func kindOf(_ bsd: String) -> String {
        lock.lock()
        defer { lock.unlock() }
        if let known = kinds[bsd] { return known }
        var kind = NetKind.other.rawValue
        let interfaces = SCNetworkInterfaceCopyAll() as? [SCNetworkInterface] ?? []
        for interface in interfaces where SCNetworkInterfaceGetBSDName(interface) as String? == bsd {
            let type = SCNetworkInterfaceGetInterfaceType(interface)
            if type == kSCNetworkInterfaceTypeIEEE80211 {
                kind = NetKind.wifi.rawValue
            } else if type == kSCNetworkInterfaceTypeEthernet {
                kind = NetKind.ethernet.rawValue
            }
        }
        kinds[bsd] = kind
        return kind
    }

    private static func band(_ band: CWChannelBand?) -> String {
        switch band {
        case .band2GHz: return "2.4"
        case .band5GHz: return "5"
        case .band6GHz: return "6"
        default: return "?"
        }
    }
}
