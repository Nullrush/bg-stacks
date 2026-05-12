/**
 * refresh-descriptions.mjs
 *
 * One-off script: merges BGG short_description from local cache into public/games.json.
 * Uses BGG value when length > 20 chars; keeps existing description as fallback.
 *
 * Usage:
 *   node scripts/refresh-descriptions.mjs
 */

import { readFileSync, writeFileSync, existsSync } from 'fs';
import { join, dirname } from 'path';
import { fileURLToPath } from 'url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const GAMES_PATH = join(__dirname, '../public/games.json');
const CACHE_DIR  = join(__dirname, '.bgg-cache');

const games = JSON.parse(readFileSync(GAMES_PATH, 'utf8'));

let updated = 0, kept = 0, missing = 0;

const result = games.map(game => {
  const cachePath = join(CACHE_DIR, `${game.id}.json`);
  if (!existsSync(cachePath)) {
    missing++;
    return game;
  }
  const cached = JSON.parse(readFileSync(cachePath, 'utf8'));
  const bggDesc = (cached.description || '').trim();
  if (bggDesc.length > 20) {
    if (bggDesc !== game.description) updated++;
    else kept++;
    return { ...game, description: bggDesc };
  }
  kept++;
  return game;
});

writeFileSync(GAMES_PATH, JSON.stringify(result, null, 2));
console.log(`Done: ${updated} updated, ${kept} kept, ${missing} missing cache`);

// Show a sample of changes
const changed = games
  .map((g, i) => ({ name: g.name, old: g.description, new: result[i].description }))
  .filter(g => g.old !== g.new)
  .slice(0, 8);

if (changed.length) {
  console.log('\nSample changes:');
  for (const g of changed) {
    console.log(`  ${g.name}`);
    console.log(`    was: ${g.old}`);
    console.log(`    now: ${g.new}`);
  }
}
