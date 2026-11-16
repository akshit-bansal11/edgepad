/** One chunk of out/search-index.json (written by scripts/search-index.mjs). */
export type Chunk = { u: string; t: string; g: string; x: string };

const MAX_RESULTS = 8;

function isChunk(value: unknown): value is Chunk {
  return (
    typeof value === "object" &&
    value !== null &&
    "u" in value &&
    typeof value.u === "string" &&
    "t" in value &&
    typeof value.t === "string" &&
    "g" in value &&
    typeof value.g === "string" &&
    "x" in value &&
    typeof value.x === "string"
  );
}

/** The built index. A malformed entry is skipped rather than trusted. */
export async function loadIndex(): Promise<Chunk[]> {
  const res = await fetch("/search-index.json", { cache: "force-cache" });
  if (!res.ok) throw new Error(`search index: ${res.status}`);
  const data: unknown = await res.json();
  if (!Array.isArray(data)) throw new Error("search index: not a list");
  return data.filter(isChunk);
}

/** Every term must appear in the title or the text; title matches come first. */
export function find(index: Chunk[], query: string): Chunk[] {
  const terms = query.toLowerCase().split(/\s+/).filter(Boolean);
  if (terms.length === 0) return [];
  const inTitle: Chunk[] = [];
  const inText: Chunk[] = [];
  for (const chunk of index) {
    const title = chunk.t.toLowerCase();
    const text = chunk.x.toLowerCase();
    if (terms.every((term) => title.includes(term))) inTitle.push(chunk);
    else if (terms.every((term) => title.includes(term) || text.includes(term)))
      inText.push(chunk);
  }
  return [...inTitle, ...inText].slice(0, MAX_RESULTS);
}
