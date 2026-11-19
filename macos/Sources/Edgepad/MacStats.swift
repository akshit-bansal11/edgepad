import Darwin
import EdgepadCore
import Foundation
import SystemConfiguration

/// This Mac's numbers for the dashboard's TEXT STATS, STATS_INFO and DISKS. Every number a Mac does not give is -1,
/// never 0: there are no temperatures or GPU power on macOS without a driver, so those are always -1 and not read.
/// `fast()` and `disks()` keep the previous reading, so one caller (the reporter's queue) uses them at a time; `info()`
/// and `ping()` run /sbin/route and /sbin/ping and must never run on the main thread. Nothing runs until called.
final class MacStats {
    /// One second's numbers: percents 0 to 100, down and up in KiB/s, -1 unknown.
    struct Fast: Equatable {
        let cpu: Int
        let ram: Int
        let gpu: Int
        let vram: Int
        let down: Int
        let up: Int
    }

    /// The names the numbers belong to and the memory sizes in MiB, -1 unknown.
    struct Info: Equatable {
        let cpuName: String
        let ramMiB: Int
        let gpuName: String
        let vramMiB: Int
        let netName: String
    }

    /// One local volume.
    struct Disk: Equatable {
        let label: String
        let usedPct: Int
        let totalGiB: Int64
        let freeGiB: Int64
    }

    private static let host = mach_host_self()
    private static let maxDisks = 6
    private static let gibibyte: Int64 = 1 << 30
    private static let routeSeconds: TimeInterval = 60
    private static let processSeconds: TimeInterval = 2

    private var lastTicks: [UInt32]?
    private var lastBytes: [String: (down: UInt32, up: UInt32)] = [:]
    private var lastBytesAt: TimeInterval?

    private let readLock = NSLock()
    private let routeLock = NSLock()
    private var route: (gateway: String, interface: String)?
    private var routeReadAt: TimeInterval?

    func fast() -> Fast {
        readLock.lock()
        defer { readLock.unlock() }
        let gpu = MacGpu.load()
        let net = network()
        return Fast(cpu: cpu(), ram: ram(), gpu: gpu.gpu, vram: gpu.vram, down: net.down, up: net.up)
    }

    /// The reporter stopped: forget the previous counters, so the next watch starts from a fresh first reading, and
    /// let the default route be read again. MacGpu keeps no registry handle between calls; there is none to release.
    func idle() {
        readLock.lock()
        lastTicks = nil
        lastBytes = [:]
        lastBytesAt = nil
        readLock.unlock()
        routeLock.lock()
        route = nil
        routeReadAt = nil
        routeLock.unlock()
    }

    func info() -> Info {
        let gpu = MacGpu.info()
        let ram = Int(ProcessInfo.processInfo.physicalMemory / 1_048_576)
        let cpuName = sysctlText("machdep.cpu.brand_string")
        let net = netName()
        return Info(cpuName: cpuName, ramMiB: ram > 0 ? ram : -1, gpuName: gpu.name, vramMiB: gpu.vramMiB, netName: net)
    }

    /// Local, browsable volumes in the order macOS mounts them, at most six.
    func disks() -> [Disk] {
        var keys: [URLResourceKey] = [.volumeLocalizedNameKey, .volumeIsLocalKey, .volumeIsBrowsableKey]
        keys += [.volumeTotalCapacityKey, .volumeAvailableCapacityForImportantUsageKey, .volumeAvailableCapacityKey]
        let manager = FileManager.default
        // Only the cheap keys are prefetched; capacity is asked later and only for a local volume.
        let cheap: [URLResourceKey] = [.volumeIsLocalKey, .volumeIsBrowsableKey]
        let urls = manager.mountedVolumeURLs(includingResourceValuesForKeys: cheap, options: [.skipHiddenVolumes]) ?? []
        var disks: [Disk] = []
        for url in urls where disks.count < Self.maxDisks {
            if let disk = Self.disk(url, keys) { disks.append(disk) }
        }
        return disks
    }

    /// The round trip to the default gateway in milliseconds, or -1 when there is no IPv4 gateway or no reply within
    /// a second. Blocks for up to two seconds.
    func ping() -> Int {
        guard let gateway = currentRoute()?.gateway, MacStatsParse.isIPv4(gateway) else { return -1 }
        guard let output = Self.run("/sbin/ping", ["-c", "1", "-W", "1000", gateway]) else { return -1 }
        return MacStatsParse.pingMs(output) ?? -1
    }

    // ---- CPU and memory

    private func cpu() -> Int {
        var load = host_cpu_load_info()
        var count = mach_msg_type_number_t(MemoryLayout<host_cpu_load_info>.size / MemoryLayout<integer_t>.size)
        let status = withUnsafeMutablePointer(to: &load) { pointer in
            pointer.withMemoryRebound(to: integer_t.self, capacity: Int(count)) {
                host_statistics(Self.host, HOST_CPU_LOAD_INFO, $0, &count)
            }
        }
        guard status == KERN_SUCCESS else { return -1 }
        let ticks = [load.cpu_ticks.0, load.cpu_ticks.1, load.cpu_ticks.2, load.cpu_ticks.3]
        // The counters count from boot: the first reading has nothing to be a difference of, so it is unknown.
        guard let previous = lastTicks else {
            lastTicks = ticks
            return -1
        }
        lastTicks = ticks
        // Per-state counters wrap at 2^32: a wrapping difference is still the right delta.
        let delta = zip(ticks, previous).map { UInt64($0 &- $1) }
        let idle = delta[Int(CPU_STATE_IDLE)]
        let total = delta.reduce(0, +)
        return MacStatsParse.cpuPercent(busy: total - idle, total: total)
    }

    /// App memory (internal pages less purgeable ones), wired and compressed pages over the installed memory: what
    /// Activity Monitor calls Memory Used.
    private func ram() -> Int {
        var stats = vm_statistics64()
        var count = mach_msg_type_number_t(MemoryLayout<vm_statistics64>.size / MemoryLayout<integer_t>.size)
        let status = withUnsafeMutablePointer(to: &stats) { pointer in
            pointer.withMemoryRebound(to: integer_t.self, capacity: Int(count)) {
                host_statistics64(Self.host, HOST_VM_INFO64, $0, &count)
            }
        }
        let total = ProcessInfo.processInfo.physicalMemory
        guard status == KERN_SUCCESS, total > 0 else { return -1 }
        let app = UInt64(stats.internal_page_count).subtractingReportingOverflow(UInt64(stats.purgeable_count))
        let pages = (app.overflow ? 0 : app.partialValue) + UInt64(stats.wire_count)
            + UInt64(stats.compressor_page_count)
        return Int(min(100, pages * UInt64(vm_kernel_page_size) * 100 / total))
    }

    // ---- Network

    /// Bytes in and out of the up `en*` interfaces since the last call, as KiB/s. The first call is 0, as is an
    /// interface seen for the first time.
    private func network() -> (down: Int, up: Int) {
        var head: UnsafeMutablePointer<ifaddrs>?
        guard getifaddrs(&head) == 0, let first = head else { return (-1, -1) }
        defer { freeifaddrs(head) }
        var bytes: [String: (down: UInt32, up: UInt32)] = [:]
        for pointer in sequence(first: first, next: { $0.pointee.ifa_next }) {
            let entry = pointer.pointee
            let flags = Int32(bitPattern: entry.ifa_flags)
            guard flags & IFF_UP != 0, flags & IFF_RUNNING != 0, flags & IFF_LOOPBACK == 0 else { continue }
            guard entry.ifa_addr?.pointee.sa_family == UInt8(AF_LINK), let data = entry.ifa_data else { continue }
            let name = String(cString: entry.ifa_name)
            guard name.hasPrefix("en") else { continue }
            let counters = data.assumingMemoryBound(to: if_data.self).pointee
            bytes[name] = (counters.ifi_ibytes, counters.ifi_obytes)
        }

        let now = ProcessInfo.processInfo.systemUptime
        let seconds = lastBytesAt.map { now - $0 } ?? 0
        var down: UInt64 = 0
        var up: UInt64 = 0
        for (name, current) in bytes {
            guard let previous = lastBytes[name] else { continue }
            // The counters are 32-bit: a wrapping difference is the right delta across one wrap.
            down += UInt64(current.down &- previous.down)
            up += UInt64(current.up &- previous.up)
        }
        lastBytes = bytes
        lastBytesAt = now
        let downRate = MacStatsParse.kibPerSecond(bytes: down, seconds: seconds)
        return (downRate, MacStatsParse.kibPerSecond(bytes: up, seconds: seconds))
    }

    /// The display name of the interface the default route uses ("Wi-Fi"), its BSD name when it has none, or "".
    private func netName() -> String {
        guard let bsd = currentRoute()?.interface, !bsd.isEmpty else { return "" }
        let interfaces = SCNetworkInterfaceCopyAll() as? [SCNetworkInterface] ?? []
        for interface in interfaces where SCNetworkInterfaceGetBSDName(interface) as String? == bsd {
            return SCNetworkInterfaceGetLocalizedDisplayName(interface) as String? ?? bsd
        }
        return bsd
    }

    /// The default route, re-read at most once a minute.
    private func currentRoute() -> (gateway: String, interface: String)? {
        routeLock.lock()
        defer { routeLock.unlock() }
        let now = ProcessInfo.processInfo.systemUptime
        if let readAt = routeReadAt, now - readAt < Self.routeSeconds { return route }
        route = Self.run("/sbin/route", ["-n", "get", "default"]).flatMap(MacStatsParse.route)
        routeReadAt = now
        return route
    }

    // ---- helpers

    private static func disk(_ url: URL, _ keys: [URLResourceKey]) -> Disk? {
        // Asked first and alone: a network volume that has stopped answering can hang a capacity query, so it is
        // skipped before one is made.
        let kind: Set<URLResourceKey> = [.volumeIsLocalKey, .volumeIsBrowsableKey]
        guard let gate = try? url.resourceValues(forKeys: kind),
            gate.volumeIsLocal == true, gate.volumeIsBrowsable != false,
            let values = try? url.resourceValues(forKeys: Set(keys))
        else { return nil }
        guard let capacity = values.volumeTotalCapacity, capacity > 0 else { return nil }
        let total = Int64(capacity)
        let label = values.volumeLocalizedName ?? url.lastPathComponent
        let fallback = values.volumeAvailableCapacity.map { Int64($0) }
        guard let available = values.volumeAvailableCapacityForImportantUsage ?? fallback, available >= 0 else {
            return Disk(label: label, usedPct: -1, totalGiB: total / gibibyte, freeGiB: -1)
        }
        let usedPct = Int(min(100, max(0, total - available) * 100 / total))
        return Disk(label: label, usedPct: usedPct, totalGiB: total / gibibyte, freeGiB: available / gibibyte)
    }

    private func sysctlText(_ name: String) -> String {
        var size = 0
        guard sysctlbyname(name, nil, &size, nil, 0) == 0, size > 0 else { return "" }
        var buffer = [UInt8](repeating: 0, count: size)
        guard sysctlbyname(name, &buffer, &size, nil, 0) == 0 else { return "" }
        return String(decoding: buffer.prefix { $0 != 0 }, as: UTF8.self).trimmingCharacters(in: .whitespaces)
    }

    /// Runs a fixed program by absolute path, no shell, and returns its output on exit status 0; nil on failure or
    /// after two seconds.
    private static func run(_ path: String, _ arguments: [String]) -> String? {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: path)
        process.arguments = arguments
        let output = Pipe()
        process.standardOutput = output
        process.standardError = FileHandle.nullDevice
        do {
            try process.run()
        } catch {
            return nil
        }
        DispatchQueue.global().asyncAfter(deadline: .now() + processSeconds) {
            if process.isRunning { process.terminate() }
        }
        let data = output.fileHandleForReading.readDataToEndOfFile()
        process.waitUntilExit()
        return process.terminationStatus == 0 ? String(decoding: data, as: UTF8.self) : nil
    }
}
