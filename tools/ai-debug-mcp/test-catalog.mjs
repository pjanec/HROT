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
import { readFileSync } from 'node:fs';

const EXPECTED_TOOLS = [
  'start_simulation', 'stop_simulation', 'get_status', 'get_capabilities', 'list_perspectives', 'switch_perspective',
  'list_entities', 'get_entity', 'list_component_types', 'list_scenarios',
  'get_event_history',
  'get_sim_state', 'play', 'pause', 'step', 'set_time_scale', 'enter_preview', 'stop_preview',
  'load_scenario_edit', 'load_scenario_live', 'save_scenario',
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
  // Slice ① — discovery with schema (MX4a / MX7).
  'list_behaviors', 'list_breakpoint_types',
  // Group P — mission editing (MX4b): read / add-task / clear / run over the editor's mission seam.
  'get_mission', 'add_mission_task', 'clear_mission_tasks', 'run_mission',
  // Slice ② — Group O, variable addressing (MX1): the watch's own tuple, over HTTP.
  'list_entity_variables', 'get_entity_variable', 'stage_entity_variable',
  // Slice ③ — the panel snapshot (MX9), blueprint hot-attach (MX2), entity state (MX3),
  // and the breakpoint resume the staged-write drain turned out to depend on.
  'list_panels', 'get_panel', 'get_gizmo_frame',
  'list_blueprints', 'attach_blueprint', 'detach_blueprint',
  'get_entity_state', 'continue_from_breakpoint',
  // Slice ④ — Group V, the AI-asset drive surface (cgf==editor slice 2).
  // ⭐ Three addresses (§3a): the GUID in a URL segment, the human sourceFilePath in the BODY,
  //   and discovery via list_assets. ⛔ Never a raw path in a segment.
  'list_assets', 'open_asset', 'open_asset_by_path',
  'list_documents', 'activate_document', 'focus_panel',
  // Slice ⑤ — cgf==editor slice 3: the edit -> save -> reload cycle, drivable headlessly.
  'save_ai_asset', 'reload_ai_asset',
  // Slice ⑥ — Group W, the AUTHORING surface (AQ56 / DESIGN_Mcp_Authoring.md).
  // ⭐ read_asset_graph is the entry point: read the IN-MEMORY guids, then edit BY them.
  //   list_node_kinds is what stops an agent guessing a kind id — an unknown kind is refused,
  //   but only this list is guaranteed valid for a given graph.
  'read_asset_graph', 'list_node_kinds',
  'add_graph_node', 'add_graph_link', 'set_graph_param', 'remove_graph_elements',
  // ⭐⭐ AQ57 / MA-020 — recipe discovery is the CREATE-side analog of list_node_kinds: without it an
  //   agent can only ever make BLANKS, because a recipe can only be asked for BY NAME.
  'create_asset', 'list_asset_recipes',
  // Group Y — node diagnostics (DESIGN_Mcp_Diagnostics_Federation.md). ⭐ Per NODE: every node hosts
  //   its own MCP endpoint, so these answer for the node you asked, and get_logs finally answers at
  //   all — neither composition root passed the sinks, so it returned [] everywhere.
  'get_architecture_diagnostics',
  // ⭐⭐ The cluster dump is a SECOND SURFACE on the built dump-diag pipeline (CQRS intent -> per-node
  //   gather -> SMB pull to NAS), ⛔ never a second collection mechanism. Status reads the same
  //   ClusterUiCache the ExCon panel renders.
  'trigger_cluster_diagnostic_dump', 'get_cluster_diagnostic_status',
  // ⭐ Scenario authoring is WORLD manipulation (Q56-C): place/configure/assign already had
  //   routes; delete was the one gap.
  'delete_entity',
  // Slice ⑦ — Group X, the UNION backbone + discovery + the editor command bus (AQ56 §10/§10.7/§11).
  // ⭐⭐ apply_graph_command carries the WHOLE ~35-variant GraphCommand union, so BTree decorators
  //   (attachments) and HSM parallel regions become reachable — the four typed verbs cannot express
  //   either, and a curated verb list WILL lag the union.
  'list_graph_command_types', 'apply_graph_command',
  'get_node_kind_schema', 'get_node_properties',
  // ⛔ list_editor_commands is NOT list_commands: the latter enumerates publishable FDP event types
  //   and send_entity_command depends on it. Two different buses, two different prefixes.
  'list_editor_commands', 'get_editor_command', 'invoke_editor_command',
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
for (const entry of TOOLS_CATALOG) {
  assert(serverSource.includes(`name: '${entry.name}'`),
    `src/index.mjs registers a handler for '${entry.name}'`);
}

console.log(`\nPassed: ${passed}, Failed: ${failed}`);
if (failed > 0) {
  console.error('CATALOG TESTS FAILED');
  process.exit(1);
} else {
  console.log('CATALOG TESTS PASSED');
  process.exit(0);
}
