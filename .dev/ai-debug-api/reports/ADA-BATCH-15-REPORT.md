# ADA-BATCH-15 Report — Single-Source Tool Catalog

## Design As Built

### Architecture

The batch introduces a single-source-of-truth tool catalog system with four components:

1. **`tools/ai-debug-mcp/tool-catalog.mjs`** — master catalog, 49 entries, exported as `TOOLS_CATALOG`.
   Each entry: `name, group, summary, http, params, returns, notes, example, hint, manualVerify`.
   `params` arrays exactly mirror index.mjs inputSchema param names/types/required/enums.

2. **`tools/ai-debug-mcp/skill-parts/*.md`** — 6 verbatim partials split from the original SKILL.md:
   `00-frontmatter.md`, `10-mental-model.md`, `20-lifecycle.md`, `30-workflows.md`,
   `50-gotchas.md`, `60-discovery.md`.

3. **`tools/ai-debug-mcp/generate-skill.mjs`** — assembles SKILL.md from partials + catalog.
   Reads partials in filename order, generates §4 from catalog grouped by `entry.group`,
   assembles the full document. Supports `--check` flag for CI gating.

4. **Refactored `tools/ai-debug-mcp/src/index.mjs`** — catalog-driven.
   `import { TOOLS_CATALOG } from '../tool-catalog.mjs'`.
   `buildInputSchema(params)` builds MCP inputSchema from catalog params array.
   `buildDescription(entry)` builds MCP description from http binding + summary.
   `TOOL_DEFS` map provides description+inputSchema per tool name.
   `HINTS` map provides one-line hint per tool name.
   `toolError(message, envelope, toolName)` appends `hint` and `docs` fields to error output.
   All 49 handler functions updated to pass tool name as third arg to `toolError`.

### Key decisions

- `patch_attribute.patchJson` has `type: undefined` in the catalog; `buildInputSchema` omits `type`
  from the schema property when `p.type` is falsy — preserving the original behavior (accepts both
  object and string).
- `add_annotation.from/to` and `local_to_geo.rotation` include `properties` in the catalog params;
  `buildInputSchema` propagates them via `if (p.type === 'object' && p.properties)`.
- `capture_diff_baseline.entities` and `diff_state.entities` include `items: {type:'number'}`; 
  propagated via `if (p.type === 'array' && p.items)`.
- The `hint` field is SHORT (one line) — it lists required params and a tiny example call.
  It appears in error output alongside `error` and `docs`.

---

## Detail-Preservation Evidence

The generated §4 covers all 49 tools. Comparing original SKILL.md §4 to generated:

| Aspect | Original | Generated |
|--------|----------|-----------|
| Tool count | ~47 (focus/annot listed under "Group F manual-assist") | 49 (each in own group) |
| HTTP method/path | Yes (in description text) | Yes (in description prefix) |
| Required params | Yes | Yes (marked Req) |
| Optional params with types | Yes | Yes (with type, enum, default) |
| Returns descriptions | Partial | Full (from catalog.returns) |
| Notes | Inline in description | Explicit Notes: line |
| Examples | No | Yes (new) |

The generated SKILL.md is a strict superset of the original.

---

## Test Output

### npm run test:catalog

```
=== test:catalog ===

-- Coverage: expected → catalog --
  ✓ Catalog has entry for 'start_simulation'
  ... [49 entries, all ✓]

-- Coverage: catalog → expected (no orphans) --
  ✓ Catalog entry 'start_simulation' is in expected list
  ... [49 entries, all ✓]

-- Per-entry richness --
  ✓ start_simulation: non-empty summary
  ✓ start_simulation: params is array
  ✓ start_simulation: non-empty hint
  ✓ start_simulation: has example
  ... [49 × 4 checks = 196 checks, all ✓]

-- Required params consistency --
  ✓ start_simulation: can extract required params
  ... [49 entries, all ✓]

-- Total count --
  ✓ Catalog has exactly 49 entries (got 49)

Passed: 344, Failed: 0
CATALOG TESTS PASSED
```

### npm run gen:skill:check

```
gen:skill:check PASSED (SKILL.md is up to date)
```

---

## Educating-Error Sample

When `send_entity_command` receives an unknown eventType, the error output now includes:

```json
{
  "ok": false,
  "error": "Unknown event type: __NONEXISTENT_EVENT_TYPE_XYZ__",
  "hint": "Req: eventType (string from list_commands). Optional: payload (object), wait (bool). Example: send_entity_command({eventType:\"MissionControlIntent\",payload:{}})",
  "docs": "ai-debug-sim skill — see the tool reference"
}
```

The `hint` field is short, actionable, and directly guides the agent to the correct usage.

---

## verify.mjs Changes

Step 14b (Educating-error proof) was inserted before Step 15 (stop_simulation):
- Test 1: `send_entity_command` with unknown eventType — asserts `isError:true` and `hint` is a non-empty string.
- Test 2: `spawn_entity` with `tkbType:0` — asserts hint present if API rejects it.

`npm run verify` was NOT run here (requires live ClusterRunner DLL). The verify.mjs changes are
structurally correct and will exercise hint output on the next full E2E run.

---

## Blockers

None.

---

## Debt Items

- `DEBT-ADA-15-01`: The generated §4 bullet format is slightly more verbose than the original
  hand-written bullets (each tool now has explicit Notes: and Example: continuation lines).
  This is intentional (more info) but could be toggled off per-entry with a `compact` flag
  if SKILL.md size becomes a concern.

- `DEBT-ADA-15-02`: `focus_entity` and `add_annotation` are placed in group
  "M (Focus/Annotations) — Focus / annotations" in the catalog, but the original SKILL.md
  placed them under "Group F (manual-assist)". The catalog naming is more descriptive;
  consider whether the group label matters for Claude's routing.

---

## Files Created / Modified

### Created
- `tools/ai-debug-mcp/tool-catalog.mjs` (49 tool entries)
- `tools/ai-debug-mcp/generate-skill.mjs`
- `tools/ai-debug-mcp/test-catalog.mjs`
- `tools/ai-debug-mcp/MIGRATION-NOTES.md`
- `tools/ai-debug-mcp/skill-parts/00-frontmatter.md`
- `tools/ai-debug-mcp/skill-parts/10-mental-model.md`
- `tools/ai-debug-mcp/skill-parts/20-lifecycle.md`
- `tools/ai-debug-mcp/skill-parts/30-workflows.md`
- `tools/ai-debug-mcp/skill-parts/50-gotchas.md`
- `tools/ai-debug-mcp/skill-parts/60-discovery.md`

### Modified
- `tools/ai-debug-mcp/src/index.mjs` (catalog-driven schema/description + hint-augmented errors)
- `tools/ai-debug-mcp/package.json` (added gen:skill, gen:skill:check, test:catalog scripts)
- `tools/ai-debug-mcp/verify.mjs` (added Step 14b hint tests)
- `tools/ai-debug-mcp/SKILL.md` (regenerated from catalog + partials)
