/**
 * The release the download links point at: the newest published GitHub release, read once
 * at build time (the site is a static export, so nothing is fetched in the browser). One
 * unauthenticated request to the releases API; drafts and pre-releases are never returned
 * by `/releases/latest`. A link goes to the asset only when the release really carries it,
 * otherwise to the releases page. If no release can be read the build fails, rather than
 * shipping links that may 404. The site shows a new release only at its next build.
 */
import { LATEST_RELEASE, REPO } from "@/lib/nav";

const API = REPO.replace("https://github.com/", "https://api.github.com/repos/") + "/releases/latest";

type Latest = { version: string; assets: Map<string, string> };

function isRecord(v: unknown): v is Record<string, unknown> {
  return typeof v === "object" && v !== null;
}

async function fetchLatest(): Promise<Latest> {
  const res = await fetch(API, {
    headers: { "User-Agent": "edgepad-site-build", Accept: "application/vnd.github+json" },
  });
  if (!res.ok) throw new Error(`Release lookup failed: ${API} answered ${res.status}`);
  const body: unknown = await res.json();
  if (!isRecord(body) || typeof body.tag_name !== "string" || !Array.isArray(body.assets)) {
    throw new Error("Release lookup failed: unexpected response shape");
  }
  const version = /^v(\d+\.\d+\.\d+)$/.exec(body.tag_name)?.[1];
  if (!version) throw new Error(`Release lookup failed: tag ${body.tag_name} is not vX.Y.Z`);
  const assets = new Map<string, string>();
  for (const a of body.assets) {
    if (isRecord(a) && typeof a.name === "string" && typeof a.browser_download_url === "string") {
      assets.set(a.name, a.browser_download_url);
    }
  }
  return { version, assets };
}

const latest = await fetchLatest();

export const RELEASE_VERSION = latest.version;

const asset = (file: string) => ({ file, href: latest.assets.get(file) ?? LATEST_RELEASE });

export const DOWNLOADS = {
  windows: asset(`Edgepad-${RELEASE_VERSION}.exe`),
  mac: asset(`Edgepad-${RELEASE_VERSION}-mac.zip`),
  linux: asset(`Edgepad-${RELEASE_VERSION}-linux-x64.tar.gz`),
  linuxArm: asset(`Edgepad-${RELEASE_VERSION}-linux-arm64.tar.gz`),
  phone: asset(`Edgepad-${RELEASE_VERSION}.apk`),
  sums: asset("SHA256SUMS.txt"),
};
