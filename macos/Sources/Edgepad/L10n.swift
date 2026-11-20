import Foundation

/// The one way the Mac app asks for a word. `english` is the fallback, so a test or a missing lookup still shows
/// English. Placeholders are `%1$@`, `%2$@`, in the order of `args`. scripts/resx-to-lproj.ps1 -Check holds
/// `english` against macos/Localization/MacStrings.resx.
func tr(_ key: String, _ english: String, _ args: CVarArg...) -> String {
    String(
        format: NSLocalizedString(key, bundle: .module, value: english, comment: ""), arguments: args)
}
