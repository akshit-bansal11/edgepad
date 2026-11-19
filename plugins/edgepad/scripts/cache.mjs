// Adapted from Plimsoll by Akshit Bansal, MIT (scripts/cache.mjs).
// Where scan-claude.mjs keeps its cache and its lock: <config dir>/edgepad/, apart from Plimsoll's.
// Imports nothing: callers pass in node's path.join, so a test can check the paths without a file system.
// The number moves when what the cache stores changes meaning, so old entries are rebuilt.
const CACHE_FILE = 'scan-cache-1.json'

export const cachePaths = (configDir, join) => {
    const dir = join(configDir, 'edgepad')

    return { dir, file: join(dir, CACHE_FILE), lock: join(dir, 'scan.lock') }
}
