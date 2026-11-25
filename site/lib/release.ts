/**
 * The release the download links point at: the newest published GitHub release, read once
 * at build time (the site is a static export, so nothing is fetched in the browser). Like the
 * apps' update check, it makes one request to `/releases/latest` and reads the redirect's
 * `Location` (`.../releases/tag/vX.Y.Z`); the REST API is not used because its unauthenticated
 * limit is shared by CI and build machines. Asset links are built from the tag. If the redirect
 * or tag is missing the build fails, rather than shipping links that may 404. The site shows a
 * new release only at its next build.
 */
import { REPO } from "@/lib/nav";

async function fetchVersion(): Promise<string> {
  const url = `${REPO}/releases/latest`;
  const res = await fetch(url, {
    method: "HEAD",
    redirect: "manual",
    headers: { "User-Agent": "edgepad-site-build" },
  });
  const location = res.headers.get("location");
  const version = location
    ? /\/releases\/tag\/v(\d+\.\d+\.\d+)$/.exec(location)?.[1]
    : undefined;
  if (!version) {
    throw new Error(
      `Release lookup failed: ${url} answered ${res.status} with Location ${location ?? "(none)"}`,
    );
  }
  return version;
}

/** The newest release's version, such as `5.1.0`, read once at build time. */
export const RELEASE_VERSION = await fetchVersion();

const asset = (file: string) => ({
  file,
  href: `${REPO}/releases/download/v${RELEASE_VERSION}/${file}`,
});

// The arm64 build is published from 5.0.0 on.
const hasArm = Number(RELEASE_VERSION.split(".")[0]) >= 5;

/**
 * The download link of each platform's file in that release. `linuxArm` is null before
 * 5.0.0, which had no arm64 build.
 */
export const DOWNLOADS = {
  windows: asset(`Edgepad-${RELEASE_VERSION}.exe`),
  mac: asset(`Edgepad-${RELEASE_VERSION}-mac.zip`),
  linux: asset(`Edgepad-${RELEASE_VERSION}-linux-x64.tar.gz`),
  linuxArm: hasArm ? asset(`Edgepad-${RELEASE_VERSION}-linux-arm64.tar.gz`) : null,
  phone: asset(`Edgepad-${RELEASE_VERSION}.apk`),
  sums: asset("SHA256SUMS.txt"),
};
