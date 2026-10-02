/**
 * gen-catalog.mjs — generate tool-catalog.mjs from the C# route table (HN-030).
 *
 * ⭐⭐⭐ WHY THIS EXISTS. tool-catalog.mjs was a hand-maintained mirror of the HTTP routes — the
 *    "hand-authored vs derived" rot GET /capabilities was built to kill (Q54-1 / charter D4). It rotted
 *    exactly as predicted: HN-025/026/027 shipped /capabilities, /perspectives and /perspective with no
 *    catalog update, and HN-029's skill prose then told agents to call a switch_perspective tool that did
 *    not exist. Nobody had to forget; the mirror just drifted.
 *
 * ⭐ Now: each endpoint's agent-facing contract is a RouteDoc beside its route in
 *    Hrot.Editor/DebugApi/DebugApiRouteDocs.cs, `--mode dump-api` emits it, and this script projects that
 *    dump into the catalog. One route-derived source, three consumers (the manifest, the catalog, SKILL.md).
 *
 * ⛔ WHAT IT DOES NOT DO: it does not write the prose. Summaries, notes, hints and examples are authored by
 *    a person — they are teaching content and cannot be derived from a method signature. What changed is
 *    WHERE they are authored (next to the route) and that a route without them fails the C# rail.
 *
 * Usage:
 *   node gen-catalog.mjs --dump <manifest.json>          # write tool-catalog.mjs
 *   node gen-catalog.mjs --dump <manifest.json> --check  # fail if the committed catalog is stale
 *   node gen-catalog.mjs                                 # run --mode dump-api itself, then generate
 */

import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { CATALOG_SUPPLEMENT } from './catalog-supplement.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const OUT = join(HERE, 'tool-catalog.mjs');

// ⭐ The only authored ordering in the generator: which group comes first. Within a group, entries are
//   ordered by path so the output is stable and a diff means a real change.
const GROUP_ORDER = [
  'A — Lifecycle & status',
  'B — Queries',
  'C — Components & attributes',
  'D — Time & preview',
  'E — Scenario',
  'F — Commands & spawn',
  'G — Breakpoints',
  'H — Checkpoint / diff',
  'I — Recording & replay',
  'J — Logs',
  'K — Panels',
  'L — Traces',
  'M — Focus & annotations',
  'N — World & geo',
  'O — Variables',
  'P — Blueprints',
  'Q — Behaviors',
  'R — Missions',
  'S — TKB',
  'T — Events',
];

function loadDump(path) {
  if (path) return JSON.parse(readFileSync(path, 'utf8'));

  // No dump given — produce one. Cheap: --mode dump-api boots nothing.
  const dll = process.env.HROT_RUNNER_DLL
    || join(HERE, '../../Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0/Hrot.ClusterRunner.dll');
  if (!existsSync(dll)) {
    throw new Error(
      `Runner dll not found at ${dll}. Build the solution first, or pass --dump <manifest.json>, `
      + `or set HROT_RUNNER_DLL.`);
  }
  const out = execFileSync('dotnet', [dll, '--mode', 'dump-api'],
    { encoding: 'utf8', maxBuffer: 32 * 1024 * 1024, stdio: ['ignore', 'pipe', 'ignore'] });
  return JSON.parse(out);
}

/** One manifest endpoint -> one catalog entry, or null when the route is deliberately not a tool. */
function toEntry(ep) {
  const d = ep.doc;
  if (!d) {
    throw new Error(
      `${ep.method} ${ep.path} has no RouteDoc. Add one in DebugApiRouteDocs.cs — `
      + `EveryRouteIsDocumentedTests should already have failed for this.`);
  }
  if (d.notATool) return null;

  const e = {
    name: d.tool,
    group: d.group,
    summary: d.summary,
    http: { method: ep.method, path: ep.path },
    // ⚠ EVERY schema field, not just the obvious four. A round-trip diff against the hand-written catalog
    //   caught this dropping 5 `enum`s, 2 `items` and 3 `properties` — the tools would have silently
    //   accepted anything. If a field is added to RouteParam, it must be copied here too.
    params: (d.params ?? []).map((p) => {
      const q = { name: p.name, type: p.type, required: p.required, description: p.description };
      if (p.enum       !== undefined) q.enum       = p.enum;
      if (p.default    !== undefined) q.default    = p.default;
      if (p.items      !== undefined) q.items      = p.items;
      if (p.properties !== undefined) q.properties = p.properties;
      return q;
    }),
    returns: d.returns,
  };
  if (d.notes?.length) e.notes = d.notes;
  if (d.example) e.example = d.example;
  e.hint = d.hint;
  e.manualVerify = d.manualVerify === true;
  return e;
}

function render(entries) {
  const byGroup = new Map();
  for (const e of entries) {
    if (!byGroup.has(e.group)) byGroup.set(e.group, []);
    byGroup.get(e.group).push(e);
  }

  const groups = [...byGroup.keys()].sort((a, b) => {
    const ia = GROUP_ORDER.indexOf(a), ib = GROUP_ORDER.indexOf(b);
    if (ia !== ib) return (ia < 0 ? 999 : ia) - (ib < 0 ? 999 : ib);
    return a.localeCompare(b);
  });

  const lines = [
    '/**',
    ' * tool-catalog.mjs — GENERATED. Do not edit.',
    ' *',
    ' * ⛔ Edits here are lost on the next `npm run gen:catalog`. The source of truth for every',
    ' *    endpoint-backed tool is its RouteDoc in Hrot/Subsystems/Hrot.Editor/DebugApi/DebugApiRouteDocs.cs;',
    ' *    the one tool with no endpoint (start_simulation) lives in catalog-supplement.mjs.',
    ' *',
    ' * ⭐ Regenerate:  npm run gen:catalog       Check for staleness:  npm run gen:catalog:check',
    ' * ⭐ SKILL.md is then generated from this file, as before (npm run gen:skill).',
    ' */',
    '',
    'export const TOOLS_CATALOG = [',
  ];

  for (const g of groups) {
    const items = byGroup.get(g).slice().sort((a, b) => {
      // Supplement entries (no http) first, then by path, then by method.
      const pa = a.http ? a.http.path : '', pb = b.http ? b.http.path : '';
      if (pa !== pb) return pa.localeCompare(pb);
      return (a.http?.method ?? '').localeCompare(b.http?.method ?? '');
    });
    lines.push('');
    lines.push(`  // ── Group ${g} ${'─'.repeat(Math.max(3, 66 - g.length))}`);
    for (const e of items) {
      lines.push('');
      lines.push(indent(JSON.stringify(e, null, 2), '  ').replace(/^ {2}/, '  ') + ',');
    }
  }

  lines.push('');
  lines.push('];');
  lines.push('');
  return lines.join('\n');
}

function indent(text, pad) {
  return text.split('\n').map((l, i) => (i === 0 ? pad + l : pad + l)).join('\n');
}

// ── main ─────────────────────────────────────────────────────────────────────

const argv = process.argv.slice(2);
const check = argv.includes('--check');
const dumpIdx = argv.indexOf('--dump');
const dumpPath = dumpIdx >= 0 ? argv[dumpIdx + 1] : null;

const manifest = loadDump(dumpPath);
const fromRoutes = manifest.endpoints.map(toEntry).filter(Boolean);
const all = [...CATALOG_SUPPLEMENT, ...fromRoutes];
const rendered = render(all);

if (check) {
  const current = existsSync(OUT) ? readFileSync(OUT, 'utf8') : '';
  if (current !== rendered) {
    console.error('gen:catalog:check FAILED — tool-catalog.mjs is stale vs the C# route docs.');
    console.error('  Run: npm run gen:catalog   (then npm run gen:skill)');
    process.exit(1);
  }
  console.log(`gen:catalog:check PASSED (${all.length} tools, ${manifest.endpoints.length} endpoints)`);
} else {
  writeFileSync(OUT, rendered, 'utf8');
  console.log(`tool-catalog.mjs written — ${all.length} tools from ${manifest.endpoints.length} endpoints `
    + `(${CATALOG_SUPPLEMENT.length} supplement, ${manifest.endpoints.length - fromRoutes.length} route(s) not exposed as tools).`);
}
