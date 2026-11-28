import MiniSearch from "minisearch";

/** One chunk of out/search-index.json (written by scripts/search-index.mjs). */
export type Chunk = { u: string; t: string; g: string; x: string };

/** A loaded index: `find` answers a query, typos and half-typed words included. */
export type Searcher = { find: (query: string) => Chunk[] };

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

/** Titles weigh more than body text; every term must match (as a prefix, or within a typo). */
function searcherFor(chunks: Chunk[]): Searcher {
  const mini = new MiniSearch<Chunk>({
    idField: "u",
    fields: ["t", "x"],
    searchOptions: {
      boost: { t: 3 },
      prefix: true,
      fuzzy: (term) => (term.length > 3 ? 0.2 : false),
      combineWith: "AND",
    },
  });
  mini.addAll(chunks);
  const byId = new Map(chunks.map((chunk) => [chunk.u, chunk]));
  return {
    find: (query) =>
      mini
        .search(query)
        .slice(0, MAX_RESULTS)
        .flatMap((hit) => byId.get(String(hit.id)) ?? []),
  };
}

/** The built index. A malformed entry is skipped rather than trusted. */
export async function loadIndex(): Promise<Searcher> {
  const res = await fetch("/search-index.json", { cache: "force-cache" });
  if (!res.ok) throw new Error(`search index: ${res.status}`);
  const data: unknown = await res.json();
  if (!Array.isArray(data)) throw new Error("search index: not a list");
  return searcherFor(data.filter(isChunk));
}
