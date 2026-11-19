import EdgepadCore

/// `MacStats` as the reporter's `StatsSource`: its own `Fast`, `Info` and `Disk` read into the wire's types. A Mac has
/// no temperature or GPU power without a driver, so those are always -1; ping and loss are the reporter's to fill.
extension MacStats: StatsSource {
    func readFast() throws -> StatsSample {
        let f = fast()
        return StatsSample(
            cpu: f.cpu, ram: f.ram, gpu: f.gpu, vram: f.vram, down: f.down, up: f.up, ping: -1, loss: -1, cpuTemp: -1,
            gpuTemp: -1, gpuPower: -1)
    }

    func readInfo() throws -> StatsInfo {
        let i = info()
        return StatsInfo(
            cpuName: i.cpuName, ramMiB: i.ramMiB, gpuName: i.gpuName, vramMiB: i.vramMiB, netName: i.netName)
    }

    func readDisks() throws -> [DiskEntry] {
        disks().map { DiskEntry(label: $0.label, usedPct: $0.usedPct, totalGiB: $0.totalGiB, freeGiB: $0.freeGiB) }
    }
}
