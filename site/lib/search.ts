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

/** Typos tolerated in a term: none for short ones, where any word is a near neighbour. */
function allowedTypos(length: number): number {
  if (length < 4) return 0;
  return length < 8 ? 1 : 2;
}

/** Edit distance (insert, delete, substitute, swap of neighbours), giving up above `max`. */
function distance(a: string, b: string, max: number): number {
  if (Math.abs(a.length - b.length) > max) return max + 1;
  let prev2: number[] = [];
  let prev: number[] = Array.from({ length: b.length + 1 }, (_, j) => j);
  for (let i = 1; i <= a.length; i++) {
    const row: number[] = [i];
    for (let j = 1; j <= b.length; j++) {
      const cost = a[i - 1] === b[j - 1] ? 0 : 1;
      let best = Math.min(prev[j] + 1, row[j - 1] + 1, prev[j - 1] + cost);
      if (i > 1 && j > 1 && a[i - 1] === b[j - 2] && a[i - 2] === b[j - 1])
        best = Math.min(best, prev2[j - 2] + 1);
      row.push(best);
    }
    prev2 = prev;
    prev = row;
  }
  return prev[b.length];
}

const WORDS = /[\p{L}\p{N}’]+/gu;
type Words = { title: string; titleWords: string[]; textWords: string[] };
const wordCache = new WeakMap<Chunk, Words>();

function wordsOf(chunk: Chunk): Words {
  let words = wordCache.get(chunk);
  if (!words) {
    const title = chunk.t.toLowerCase();
    const textWords = new Set(chunk.x.toLowerCase().match(WORDS));
    words = {
      title,
      titleWords: title.match(WORDS) ?? [],
      textWords: [...textWords],
    };
    wordCache.set(chunk, words);
  }
  return words;
}

/**
 * Cost of finding `term` in a text: 0 as typed, 1 as the start of a word being typed or
 * with a small typo, Infinity when absent. Typos are only forgiven in whole words or in
 * the start of one, so "notifcations" finds "notifications" and "notif" finds it too.
 */
function cost(term: string, text: string, words: string[]): number {
  if (text.includes(term)) return 0;
  const typos = allowedTypos(term.length);
  if (typos === 0) return Number.POSITIVE_INFINITY;
  let best = Number.POSITIVE_INFINITY;
  for (const word of words) {
    const d = Math.min(
      distance(term, word, typos),
      distance(term, word.slice(0, term.length), typos) + 1,
    );
    if (d <= typos) best = Math.min(best, d + 1);
  }
  return best;
}

/**
 * Every term must be in the title or the text, as typed, as the start of a word, or
 * within a small typo of one. Title matches come first, then fewer typos.
 */
export function find(index: Chunk[], query: string): Chunk[] {
  const terms = query.toLowerCase().replaceAll("'", "’").split(/\s+/).filter(Boolean);
  if (terms.length === 0) return [];
  const hits: { chunk: Chunk; inTitle: boolean; cost: number }[] = [];
  for (const chunk of index) {
    const { title, titleWords, textWords } = wordsOf(chunk);
    const text = chunk.x.toLowerCase();
    let inTitle = true;
    let total = 0;
    for (const term of terms) {
      const t = cost(term, title, titleWords);
      if (t !== Number.POSITIVE_INFINITY) {
        total += t;
        continue;
      }
      inTitle = false;
      const x = cost(term, text, textWords);
      if (x === Number.POSITIVE_INFINITY) {
        total = x;
        break;
      }
      total += x;
    }
    if (total !== Number.POSITIVE_INFINITY) hits.push({ chunk, inTitle, cost: total });
  }
  hits.sort((a, b) => Number(b.inTitle) - Number(a.inTitle) || a.cost - b.cost);
  return hits.slice(0, MAX_RESULTS).map((hit) => hit.chunk);
}
