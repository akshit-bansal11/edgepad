import Foundation

/// The core's one way to ask for a word: the twin of the app's `tr`, reading this target's own bundle. `english` is
/// the fallback, so a test or a missing lookup still shows English. Placeholders are `%1$@`, `%2$@`, in the order of
/// `args`, and every argument is a String. scripts/resx-to-lproj.ps1 -Check holds `english` against the resx.
func tr(_ key: String, _ english: String, _ args: CVarArg...) -> String {
    String(
        format: NSLocalizedString(key, bundle: .module, value: english, comment: ""), arguments: args)
}
