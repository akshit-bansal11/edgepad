import CoreBluetooth
import Foundation

/// Tells the app when Bluetooth is switched on or off, so the service record and channel are published again after an
/// off and on. Event-driven: CoreBluetooth calls back on the main queue, nothing polls.
final class BluetoothPower: NSObject, CBCentralManagerDelegate {
    private let onChange: (Bool) -> Void
    private var manager: CBCentralManager?
    /// Nil until the first report, which only says how things stand at launch and is not a change.
    private var wasOn: Bool?

    init(onChange: @escaping (Bool) -> Void) {
        self.onChange = onChange
        super.init()
        manager = CBCentralManager(
            delegate: self, queue: .main, options: [CBCentralManagerOptionShowPowerAlertKey: false])
    }

    func centralManagerDidUpdateState(_ central: CBCentralManager) {
        let on = central.state == .poweredOn
        let previous = wasOn
        wasOn = on
        if let previous, previous != on {
            onChange(on)
        }
    }
}
