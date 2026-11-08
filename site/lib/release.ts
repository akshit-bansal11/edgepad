/**
 * The release the download links point at. One source: the newest released heading in
 * ../CHANGELOG.md (`## [4.0.0] - date`), read at build time like the protocol fixtures,
 * so the file names on the page cannot go stale on their own. `[Unreleased]` is skipped.
 */
import { readFileSync } from "node:fs";
import path from "node:path";
import { REPO } from "@/lib/nav";

function newestVersion(): string {
  const log = readFileSync(path.join(process.cwd(), "..", "CHANGELOG.md"), "utf8");
  const match = /^## \[(\d+\.\d+\.\d+)\]/m.exec(log);
  if (!match?.[1]) throw new Error("CHANGELOG.md has no released version heading");
  return match[1];
}

export const RELEASE_VERSION = newestVersion();

const asset = (file: string) => ({
  file,
  href: `${REPO}/releases/download/v${RELEASE_VERSION}/${file}`,
});

export const DOWNLOADS = {
  windows: asset(`Edgepad-${RELEASE_VERSION}.exe`),
  mac: asset(`Edgepad-${RELEASE_VERSION}-mac.zip`),
  linux: asset(`Edgepad-${RELEASE_VERSION}-linux-x64.tar.gz`),
  phone: asset(`Edgepad-${RELEASE_VERSION}.apk`),
  sums: asset("SHA256SUMS.txt"),
};
