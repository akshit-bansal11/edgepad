// Builds out/search-index.json after `next build`: one chunk per documented section and
// per sub-heading, as plain text. Node built-ins only, so the site gains no dependency.
// The file is fetched by components/docs-search.tsx the first time the dialog opens.
import { readFileSync, writeFileSync } from "node:fs";
import process from "node:process";

const PAGES = [
  // `output: "export"` without trailingSlash writes each route as <route>.html.
  { file: "out/docs.html", path: "/docs" },
  { file: "out/docs/developers.html", path: "/docs/developers" },
];
const MAX_TEXT = 1500;
const ENTITIES = {
  amp: "&",
  lt: "<",
  gt: ">",
  quot: '"',
  apos: "'",
  nbsp: " ",
};

function decode(s) {
  return s.replace(/&(?:#x([0-9a-f]+)|#(\d+)|([a-z]+));/gi, (whole, hex, dec, name) => {
    if (hex) return String.fromCodePoint(Number.parseInt(hex, 16));
    if (dec) return String.fromCodePoint(Number.parseInt(dec, 10));
    return ENTITIES[name.toLowerCase()] ?? whole;
  });
}

/** Tags out, entities decoded, whitespace collapsed. */
export function plain(html) {
  const bare = html
    .replace(/<(script|style|template)\b[\s\S]*?<\/\1>/gi, " ")
    .replace(/<[^>]*>/g, " ");
  return decode(bare).replace(/\s+/g, " ").trim();
}

/** The nav lists in lib/nav.ts give each section id its group name. */
function groupsOf(navSource) {
  const out = new Map();
  const blocks = navSource.matchAll(/label: "([^"]+)",\s*items: \[([\s\S]*?)\]/g);
  for (const [, group, items] of blocks) {
    for (const [, id] of items.matchAll(/id: "([^"]+)"/g)) out.set(id, group);
  }
  return out;
}

/** html -> [{ u, t, g, x }]: a chunk per section, split further at each <h3 id>. */
export function chunk(html, path, groups) {
  const chunks = [];
  for (const part of html.split(/<section id="/).slice(1)) {
    const id = part.slice(0, part.indexOf('"'));
    const group = groups.get(id) ?? "";
    const title = plain(/<h2\b[\s\S]*?<\/h2>/.exec(part)?.[0] ?? id);
    const pieces = part.split(/<h3 id="/);
    const push = (anchor, heading, body) => {
      const x = plain(body).slice(0, MAX_TEXT);
      if (x) chunks.push({ u: `${path}#${anchor}`, t: heading, g: group, x });
    };
    push(id, title, pieces[0]);
    for (const sub of pieces.slice(1)) {
      const anchor = sub.slice(0, sub.indexOf('"'));
      const h3 = /<h3\b[\s\S]*?<\/h3>/.exec(`<h3 id="${sub}`)?.[0];
      const heading = plain(h3 ?? anchor);
      push(anchor, `${title}: ${heading}`, sub);
    }
  }
  return chunks;
}

if (process.argv[1]?.endsWith("search-index.mjs")) {
  const groups = groupsOf(readFileSync("lib/nav.ts", "utf8"));
  const index = PAGES.flatMap(({ file, path }) =>
    chunk(readFileSync(file, "utf8"), path, groups),
  );
  if (index.length === 0) {
    console.error("search-index: found no sections; the page markup changed?");
    process.exit(1);
  }
  writeFileSync("out/search-index.json", JSON.stringify(index));
}
