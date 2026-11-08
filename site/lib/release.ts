/**
 * The release the download links point at. One source: the newest released heading in
 * ../CHANGELOG.md (`## [4.0.0] - date`), read at build time like the protocol fixtures,
 * so the file names on the page cannot go stale on their own. `[Unreleased]` has no
 * version number and is skipped. If no version can be read, the names fall back to
 * `x.y.z` and every link goes to the releases page rather than to a file that may not
 * exist.
 */
import { readFileSync } from "node:fs";
import path from "node:path";
import { LATEST_RELEASE, REPO } from "@/lib/nav";

function newestVersion(): string | null {
  try {
    const log = readFileSync(path.join(process.cwd(), "..", "CHANGELOG.md"), "utf8");
    return /^## \[(\d+\.\d+\.\d+)\]/m.exec(log)?.[1] ?? null;
  } catch {
    return null;
  }
}

const version = newestVersion();

export const RELEASE_VERSION = version ?? "x.y.z";

const asset = (file: string) => ({
  file,
  href: version ? `${REPO}/releases/download/v${version}/${file}` : LATEST_RELEASE,
});

export const DOWNLOADS = {
  windows: asset(`Edgepad-${RELEASE_VERSION}.exe`),
  mac: asset(`Edgepad-${RELEASE_VERSION}-mac.zip`),
  linux: asset(`Edgepad-${RELEASE_VERSION}-linux-x64.tar.gz`),
  phone: asset(`Edgepad-${RELEASE_VERSION}.apk`),
  sums: asset("SHA256SUMS.txt"),
};
