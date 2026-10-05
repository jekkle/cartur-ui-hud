// Grok Imagine edit over a layout sheet. node gen.mjs <sheet.png> <prompt.txt> <outPrefix> [count]
// Auth and endpoint as recorded in memory grok-plugin-client-version: /images/edits on the
// subscription proxy, model grok-imagine-image, headers from the plugin's buildAuthHeaders.
import fs from 'node:fs';
import { pathToFileURL } from 'node:url';
const SRC = 'C:/Users/Tool1/.claude/plugins/cache/grok-plugin/grok/0.4.0/src/';
const { loadConfig } = await import(pathToFileURL(SRC + 'config.js').href);

const [sheet, promptFile, out, count = '3'] = process.argv.slice(2);
const config = loadConfig();
// The bridge's own token expired (401 "no auth context"); the official CLI's ~/.grok/auth.json
// carries a fresh one. Read from the file, never printed.
const cli = Object.values(JSON.parse(fs.readFileSync(process.env.USERPROFILE + '/.grok/auth.json', 'utf8')))[0];
const headers = { authorization: `Bearer ${cli.key}`, 'x-grok-client-version': config.clientVersion,
  'x-grok-client-mode': config.clientMode, 'Content-Type': 'application/json' };
const dataUri = 'data:image/png;base64,' + fs.readFileSync(sheet).toString('base64');
const prompt = fs.readFileSync(promptFile, 'utf8');

for (let i = 0; i < Number(count); i++) {
  const res = await fetch(config.upstreamBase + '/images/edits', {
    method: 'POST', headers,
    body: JSON.stringify({ model: 'grok-imagine-image', prompt,
      image: { url: dataUri, type: 'image_url' }, response_format: 'b64_json' }),
  });
  if (!res.ok) { console.log('HTTP', res.status, (await res.text()).slice(0, 300)); process.exit(1); }
  const j = await res.json();
  const b64 = j.data?.[0]?.b64_json;
  if (!b64) { console.log('no image', JSON.stringify(j).slice(0, 300)); process.exit(1); }
  fs.writeFileSync(`${out}_${i + 1}.png`, Buffer.from(b64, 'base64'));
  console.log('wrote', `${out}_${i + 1}.png`);
}
