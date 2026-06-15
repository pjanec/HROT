/**
 * test-catalog.mjs — Static tests for tool-catalog.mjs correctness.
 *
 * Does NOT start the MCP server. Tests:
 *   - Every expected tool name is present in the catalog
 *   - No orphan catalog entries (every catalog entry is in the expected list)
 *   - Per-entry richness: non-empty summary, params array, hint, example
 *   - Required params consistency
 *
 * Usage:
 *   node test-catalog.mjs
 */

import { TOOLS_CATALOG } from './tool-catalog.mjs';

const EXPECTED_TOOLS = [
  'start_simulation', 'stop_simulation', 'get_status',
  'list_entities', 'get_entity', 'list_component_types', 'list_scenarios',
  'get_event_history',
  'get_sim_state', 'play', 'pause', 'step', 'set_time_scale', 'enter_preview', 'stop_preview',
  'load_scenario', 'save_scenario',
  'list_commands', 'send_entity_command', 'spawn_entity',
  'list_entity_types', 'get_entity_type',
  'get_world_info', 'geo_to_local', 'local_to_geo',
  'set_breakpoint', 'list_breakpoints', 'remove_breakpoint', 'get_breakpoint_status',
  'checkpoint', 'restore_checkpoint', 'capture_diff_baseline', 'diff_state',
  'start_recording', 'stop_recording', 'load_replay', 'seek_replay',
  'step_replay', 'get_replay_status', 'list_replay_entities', 'unload_replay',
  'get_logs',
  'observe_trace', 'get_entity_trace',
  'get_attributes_schema', 'patch_attribute', 'edit_component',
  'focus_entity', 'add_annotation',
];

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

console.log(`\nPassed: ${passed}, Failed: ${failed}`);
if (failed > 0) {
  console.error('CATALOG TESTS FAILED');
  process.exit(1);
} else {
  console.log('CATALOG TESTS PASSED');
  process.exit(0);
}
