import AppKit
import EdgepadCore

/// Installs a newer Edgepad over the running one. The order is UpdateVerifier's: signed list, then the hash of the
/// exact `Edgepad-<v>-mac.zip`, then the download, and only a file that matches is unpacked. The old bundle is
/// replaced only after the new one has the right identity and a valid code signature. With no signing key, or under
/// App Translocation, or in a folder that cannot be written, nothing is installed and the release page is opened.
final class MacUpdater {
    private static let bundleId = "me.akshitbansal.edgepad"

    private(set) var installing = false

    /// True when this copy can replace itself where it sits.
    static func canReplaceSelf() -> Bool {
        let bundle = Bundle.main.bundleURL
        return !bundle.path.contains("AppTranslocation")
            && FileManager.default.isWritableFile(atPath: bundle.deletingLastPathComponent().path)
    }

    /// Percent downloaded so far, for a menu that opens mid-download. Written and read on the main thread.
    private(set) var percent = 0

    /// Runs on the main actor's behalf; `progress` gets whole-percent changes and `done` gets nil on success (the
    /// new copy has been started and will end this one) or a short reason, both on the main queue. The whole job,
    /// download included, is stopped after `UpdateDownload.assetTimeout`.
    func install(_ version: Version, progress: @escaping (Int) -> Void, done: @escaping (String?) -> Void) {
        guard !installing else { return }
        installing = true
        percent = 0
        Task.detached {
            let failure = await Self.runGuarded(version) { value in
                DispatchQueue.main.async {
                    self.percent = value
                    progress(value)
                }
            }
            await MainActor.run {
                self.installing = false
                done(failure)
            }
        }
    }

    /// `run` with nothing allowed to escape: a thrown error and the deadline are logged and become a reason.
    private static func runGuarded(_ version: Version, progress: @escaping (Int) -> Void) async -> String? {
        Log.write("Installing Edgepad \(version)")
        let outcome = await UpdateDownload.withDeadline(UpdateDownload.assetTimeout) { () -> String? in
            do {
                return try await run(version, progress: progress)
            } catch {
                Log.write("Edgepad \(version) could not be installed: \(error.localizedDescription)")
                return "The update could not be installed."
            }
        }
        guard let outcome else {
            Log.write("Edgepad \(version): the install took longer than 20 minutes and was stopped")
            return "The update took longer than 20 minutes and was stopped."
        }
        return outcome
    }

    private static func run(_ version: Version, progress: @escaping (Int) -> Void) async throws -> String? {
        guard UpdateKey.isSet, let sumsUrl = UpdateCheck.sumsUrl(version), let sigUrl = UpdateCheck.sigUrl(version),
            let assetUrl = UpdateCheck.assetUrl(version, UpdateCheck.macName(version))
        else {
            return "Updates are not signed yet."
        }
        guard canReplaceSelf() else {
            return "Edgepad cannot replace itself here. Move it to Applications first."
        }
        guard let sums = await UpdateDownload.getBytes(sumsUrl, maxBytes: UpdateDownload.maxSumsBytes),
            let signature = await UpdateDownload.getBytes(sigUrl, maxBytes: UpdateDownload.maxSignatureBytes)
        else {
            return "The update could not be downloaded."
        }
        guard UpdateVerifier.verifySums(sums, derSignature: signature, spki: UpdateKey.spki()),
            let text = String(data: sums, encoding: .utf8),
            let expected = UpdateVerifier.hashFor(sums: text, fileName: UpdateCheck.macName(version))
        else {
            return "The update's signature did not check out."
        }
        let fm = FileManager.default
        let bundle = Bundle.main.bundleURL
        let work = try fm.url(
            for: .itemReplacementDirectory, in: .userDomainMask, appropriateFor: bundle, create: true)
        defer { try? fm.removeItem(at: work) }
        let zip = work.appendingPathComponent(UpdateCheck.macName(version))
        guard
            let actual = await UpdateDownload.download(
                assetUrl, to: zip.path, maxBytes: UpdateDownload.maxAssetBytes, progress: progress),
            UpdateVerifier.hashesMatch(actual, expected)
        else {
            return "The update did not match its signed hash."
        }
        let unpacked = work.appendingPathComponent("unpacked")
        guard
            (try? fm.moveItem(atPath: zip.path + ".download", toPath: zip.path)) != nil,
            exec("/usr/bin/ditto", ["-x", "-k", zip.path, unpacked.path]),
            let new = try? fm.contentsOfDirectory(at: unpacked, includingPropertiesForKeys: nil)
                .first(where: { $0.pathExtension == "app" }),
            let info = Bundle(url: new)?.infoDictionary,
            info["CFBundleIdentifier"] as? String == bundleId,
            info["CFBundleShortVersionString"] as? String == "\(version)",
            exec("/usr/bin/codesign", ["--verify", "--strict", new.path])
        else {
            return "The update was not the expected Edgepad."
        }
        do {
            _ = try fm.replaceItemAt(bundle, withItemAt: new)
        } catch {
            Log.write("The update could not be put in place: \(error.localizedDescription)")
            return "The update could not be put in place."
        }
        // The new copy sees a different version running and takes over from this one.
        let configuration = NSWorkspace.OpenConfiguration()
        configuration.createsNewApplicationInstance = true
        NSWorkspace.shared.openApplication(at: bundle, configuration: configuration) { _, error in
            if let error { Log.write("The new copy did not start: \(error.localizedDescription)") }
        }
        return nil
    }

    private static func exec(_ tool: String, _ arguments: [String]) -> Bool {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: tool)
        process.arguments = arguments
        process.standardOutput = FileHandle.nullDevice
        process.standardError = FileHandle.nullDevice
        do {
            try process.run()
        } catch {
            return false
        }
        process.waitUntilExit()
        return process.terminationStatus == 0
    }
}
