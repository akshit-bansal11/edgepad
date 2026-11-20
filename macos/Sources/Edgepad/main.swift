import AppKit
import EdgepadCore

// A Claude Code or Codex hook runs this executable as `--hook <agent> <kind>`: answered and gone before anything of the
// app starts (no single-instance check, no menu bar item, no window), and always with exit code 0.
let launchArguments = Array(CommandLine.arguments.dropFirst())
if HookClient.isHook(launchArguments) {
    exit(HookClient.runConsole(launchArguments))
}

let delegate = AppDelegate()
let application = NSApplication.shared
application.delegate = delegate
application.setActivationPolicy(.accessory)
application.run()
