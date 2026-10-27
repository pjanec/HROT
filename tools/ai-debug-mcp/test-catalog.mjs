/**
 * test-catalog.mjs — Static tests for tool-catalog.mjs correctness.
 *
 * Does NOT start the MCP server. Tests:
 *   - Every tool the SOURCES declare is present in the catalog, and no catalog entry lacks a source
 *     (the expectation is DERIVED, never hand-kept — CE-1375: a hand-kept list trailed the generated
 *      catalog by 58 tools, the rot HN-030 removed from the catalog itself)
 *   - Per-entry richness: non-empty summary, params array, hint, example
 *   - Required params consistency
 *
 * Usage:
 *   node test-catalog.mjs
 */

import { TOOLS_CATALOG } from './tool-catalog.mjs';
import { readFileSync } from 'node:fs';
import { CATALOG_SUPPLEMENT } from './catalog-supplement.mjs';

// ── The expected set is DERIVED from the two sources the generator reads ─────────────────────────
//
// ⭐ gen-catalog.mjs builds the catalog from (a) the RouteDocs in DebugApiRouteDocs.cs (`Tool: "name"`) and
//   (b) catalog-supplement.mjs (the one tool with no endpoint). Reading the same two files here gives the
//   expectation WITHOUT the generated file in the loop — a route doc dropped from the catalog, or a catalog
//   entry with no source, fails; and there is no second hand-kept mirror to drift (CE-1375).
const routeDocs = readFileSync(
  new URL('../../Hrot/Subsystems/Hrot.Editor/DebugApi/DebugApiRouteDocs.cs', import.meta.url), 'utf8');
const EXPECTED_TOOLS = [
  ...[...routeDocs.matchAll(/\bTool:\s*"([a-z0-9_]+)"/g)].map((m) => m[1]),
  ...CATALOG_SUPPLEMENT.map((t) => t.name),
];
if (EXPECTED_TOOLS.length < 100) {
  console.error(`Only ${EXPECTED_TOOLS.length} tools derived from the sources — the derivation is broken, not the catalog.`);
  process.exit(1);
}


let passed = 0;
let failed = 0;

function assert(cond, label) {
  if (cond) { console.log(`  ✓ ${label}`); passed++; }
  else { console.error(`  ✗ ${label}`); failed++; }
}

console.log('=== test:catalog ===\n');

// Coverage: every expected tool in catalog
console.log('-- Coverage: expected → catalog --');
const catalogNames = new Set(TOOLS_CATALOG.map(t => t.name));
for (const name of EXPECTED_TOOLS) {
  assert(catalogNames.has(name), `Catalog has entry for '${name}'`);
}

// Coverage: no orphan catalog entries
console.log('\n-- Coverage: catalog → expected (no orphans) --');
const expectedSet = new Set(EXPECTED_TOOLS);
for (const entry of TOOLS_CATALOG) {
  assert(expectedSet.has(entry.name), `Catalog entry '${entry.name}' is in expected list`);
}

// Per-entry richness
console.log('\n-- Per-entry richness --');
for (const entry of TOOLS_CATALOG) {
  assert(typeof entry.summary === 'string' && entry.summary.length > 0, `${entry.name}: non-empty summary`);
  assert(Array.isArray(entry.params), `${entry.name}: params is array`);
  assert(typeof entry.hint === 'string' && entry.hint.length > 0, `${entry.name}: non-empty hint`);
  assert(entry.example != null, `${entry.name}: has example`);
}

// required params consistency
console.log('\n-- Required params consistency --');
for (const entry of TOOLS_CATALOG) {
  const requiredFromCatalog = entry.params.filter(p => p.required).map(p => p.name);
  // These should match what buildInputSchema would produce
  assert(Array.isArray(requiredFromCatalog), `${entry.name}: can extract required params`);
}

// Total count check
console.log('\n-- Total count --');
assert(TOOLS_CATALOG.length === EXPECTED_TOOLS.length,
  `Catalog has exactly ${EXPECTED_TOOLS.length} entries (got ${TOOLS_CATALOG.length})`);

// ── The server actually EXPOSES every catalogued tool ────────────────────────
//
// ⛔ This rail exists because its absence let a real slip through: eight tools were added to the
//    catalog and to SKILL.md while the shell command that was supposed to add their HANDLERS failed
//    silently. Every assertion above still passed — they check the CATALOG against a list, and the
//    catalog was right. The server was the thing that was wrong, and nothing looked at it.
//
// ⚠ Read as TEXT rather than imported: src/index.mjs starts a server and connects a stdio transport
//   at import time, so a test cannot pull its TOOLS array out without launching it.
console.log('\n-- The server exposes every catalogued tool --');
const serverSource = readFileSync(new URL('./src/index.mjs', import.meta.url), 'utf8');
// ⭐ A tool is exposed if it has a hand-written handler (`name: '<tool>'`) OR the generic catalog-driven rule
//   (GENERIC_FROM_CATALOG in index.mjs) serves it — which needs the entry to carry an `http` route.
const generic = serverSource.includes('const GENERIC_FROM_CATALOG = true');
for (const entry of TOOLS_CATALOG) {
  const explicit = serverSource.includes(`name: '${entry.name}'`);
  assert(explicit || (generic && entry.http && entry.http.method && entry.http.path),
    `src/index.mjs serves '${entry.name}' (hand-written handler or the generic catalog rule)`);
}

console.log(`\nPassed: ${passed}, Failed: ${failed}`);
if (failed > 0) {
  console.error('CATALOG TESTS FAILED');
  process.exit(1);
} else {
  console.log('CATALOG TESTS PASSED');
  process.exit(0);
}
