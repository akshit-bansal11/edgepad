import Foundation

/// What `UpdatePolicy.decide` says to do with an offered release, the twin of the laptop core's UpdateDecision.
public enum UpdateDecision {
    /// Not newer than the running version, or the running version cannot be read: nothing to do.
    case notNewer
    /// The owner chose to skip this version.
    case skipped
    /// This build has no update-signing key, so nothing can be verified: open the release page instead.
    case noKey
    /// Download, verify and install it.
    case install
    /// A new major version, which needs the phone updated too: the owner confirms before it is installed.
    case installNeedsMajorConfirm
}

/// What to do with an offered release. Pure.
public enum UpdatePolicy {
    /// `offered` is a `Version`, so it is already three plain parts. A running version that cannot be read is
    /// "not newer", so nothing is installed. With no signing key the answer is to open the release page. A
    /// different first number means the phone must be updated too, so the owner confirms first.
    public static func decide(offered: Version, running: String, skipped: Version?, keySet: Bool) -> UpdateDecision {
        guard let core = running.split(separator: "-", omittingEmptySubsequences: false).first,
            let ours = Version(String(core)), offered > ours
        else {
            return .notNewer
        }
        if skipped == offered {
            return .skipped
        }
        if !keySet {
            return .noKey
        }
        return offered.major != ours.major ? .installNeedsMajorConfirm : .install
    }
}
