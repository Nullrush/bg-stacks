#!/usr/bin/env node
// Loads api/local.settings.json Values into process.env so SWA CLI can
// resolve auth provider credentials (GOOGLE_CLIENT_ID, etc.), then starts
// Azurite and SWA CLI together via concurrently.
import { execSync } from 'child_process';
import { readFileSync } from 'fs';

const settings = JSON.parse(readFileSync(new URL('../api/local.settings.json', import.meta.url)));
for (const [k, v] of Object.entries(settings.Values ?? {})) {
  process.env[k] = v;
}

execSync(
  'concurrently -k -n azurite,swa "npx azurite --location .azurite --silent" "swa start"',
  { stdio: 'inherit', env: process.env }
);
