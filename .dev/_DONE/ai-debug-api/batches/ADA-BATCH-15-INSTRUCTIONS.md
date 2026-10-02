# ADA-BATCH-15: Single-Source Tool Catalog → SKILL.md + MCP schemas + Educating Errors

**Batch Number:** ADA-BATCH-15
**Tasks:** Consolidate the MCP tool surface into ONE source of truth that generates (a) the per-tool reference
in `SKILL.md`, (b) the MCP tool `description` + `inputSchema`, and (c) the `HINTS` used in error output.
**Executor:** sonnet (refactor + generator + tests; care to NOT lose doc detail)
**Priority:** MEDIUM-HIGH (the tool surface will keep growing — automate before it does)
**Dependencies:** BATCH-06..14 (the 49 tools + SKILL.md exist).

---

## Goal & non-negotiable constraint

Right now three things describe each tool independently and will drift: the inline tool defs in
`src/index.mjs` (description + inputSchema), the per-tool reference in `SKILL.md`, and (not yet existing)
error hints. Consolidate to ONE source: a **tool catalog**. From it, GENERATE the MCP tool defs, the SKILL.md
per-tool reference, and an error-hints map.

**NON-NEGOTIABLE: the regenerated `SKILL.md` must LOSE NO detail vs the current hand-written one.** The
generator ASSEMBLES (concatenates narrative partials verbatim + renders a per-tool reference table) — it must
never summarize or compress prose. The dev lead will diff generated-vs-current and require the generated file
to be a *superset*.

> No codebase-memory MCP (hangs — Grep/Glob/Read). No git commit. Report HONESTLY; RE-RUN `npm run verify`
> to a real PASS tally before reporting.

---

## Architecture (build exactly this)

### 1. `tools/ai-debug-mcp/tool-catalog.mjs` — the single source
An ordered array of tool entries. Each entry is RICH enough to losslessly produce all three outputs:
```js
export const TOOLS_CATALOG = [
  {
    name: 'spawn_entity',
    group: 'F — Commands, discovery, spawn',
    summary: 'Spawn an entity from a TKB type.',            // one line → MCP description + SKILL summary
    http: { method: 'POST', path: '/entities/spawn' },
    params: [                                                // → inputSchema AND SKILL param table
      { name: 'tkbType', type: 'number', required: true,  description: 'TKB type id (from list_entity_types).' },
      { name: 'transform', type: 'object', required: false, description: '{position:{x,y,z}, rotation:{x,y,z,w}}' },
      // ...
    ],
    returns: '{ spawned, tkbType, awaited, reason? }',       // → SKILL "Returns"
    notes: [ 'Processed on the next tick — step to realize it.' ], // → SKILL per-tool notes (preserve detail)
    example: { args: { tkbType: 1001 }, gist: 'spawns a CivilianPedestrian; step to see entityCount rise' },
    hint: 'spawn_entity needs tkbType (number, from list_entity_types); optional transform {position,rotation}. e.g. {"tkbType":1001}', // → error hint (short, actionable, with a tiny example)
    manualVerify: false,                                     // true for focus_entity / add_annotation
  },
  // ... all 49 tools
];
```
Migrate EVERY existing tool into this catalog, preserving the exact param names/types/required/defaults the
current `src/index.mjs` inputSchemas use, and folding in the per-tool detail currently in `SKILL.md` §4
(returns, notes/gotchas, examples). Lifecycle tools (`start_simulation`/`stop_simulation`) are MCP-only — mark
them (e.g. `http: null`) so the generator notes "MCP-side lifecycle tool".

### 2. `src/index.mjs` — consume the catalog (remove the duplicated literals)
- Build the `TOOLS` array from `TOOLS_CATALOG`: derive `inputSchema` (JSON Schema: `type:'object'`,
  `properties` from `params` type/description, `required` from the required params) and `description` from
  `summary` (+ the `http` line). Keep each tool's existing `handler` (the handlers stay in index.mjs, keyed by
  name — only the *schema/description* is generated). Behavior must be UNCHANGED (same params, same calls).
- Build `HINTS = Object.fromEntries(catalog.map(t => [t.name, t.hint]))`.
- Change `toolError(message, envelope, toolName)` to append the hint:
  `{ ok:false, error, hint: HINTS[toolName], docs: 'ai-debug-sim skill — see the tool reference', isError:true }`
  Thread the tool name through every `handler`'s catch (each handler knows its own name). Keep `error` first.
- The actual request behavior, paths, and `callApi` are unchanged.

### 3. `skill-parts/*.md` — hand-authored narrative (the detail that is NOT generated)
Split the CURRENT `SKILL.md` narrative into partials, VERBATIM (do not reword): e.g.
`00-frontmatter.md` (the `--- name/description ---` block + the H1 + intro), `10-mental-model.md` (§1),
`20-lifecycle.md` (§2 — or generate it from the catalog group A; your call, but if hand-authored keep it a
partial), `30-workflows.md` (§3 — the 9 recipes), `50-gotchas.md` (§5), `60-discovery.md` (§6). These are the
source of all narrative detail; the generator emits them unchanged.

### 4. `generate-skill.mjs` — the assembler
- Reads the partials + the catalog; writes `SKILL.md` = frontmatter + intro + mental-model + lifecycle +
  workflows + **generated per-tool reference (§4)** (grouped by `group`, each tool rendered with summary,
  param table, returns, notes, example) + gotchas + discovery.
- Deterministic output (stable ordering/formatting) so a re-run is byte-identical.
- Support `node generate-skill.mjs --check` → exit non-zero (with a diff summary) if the on-disk `SKILL.md`
  differs from freshly-generated. Add `npm run gen:skill` and `npm run gen:skill:check` scripts.

---

## Verification (prove single-source + no detail lost + hints work)
- **Detail-preservation (the crux):** after generating, the new `SKILL.md` must contain, for every tool, at
  least the params/returns/notes/example present in the current version, and ALL narrative sections verbatim.
  Produce a short `MIGRATION-NOTES.md` listing anything intentionally reworded (ideally nothing) for the lead
  to diff-review.
- **Coverage tests** (`node` test script, runnable via npm — e.g. `npm run test:catalog`):
  1. Every tool registered in `index.mjs` has a catalog entry, and every catalog entry is registered (no
     orphans either way).
  2. Every catalog entry has non-empty `summary`, `params` (array), `hint`, and `example`.
  3. Built `inputSchema.required` matches the params marked `required` for each tool.
- **`generate-skill.mjs --check`** passes on the committed `SKILL.md` (i.e. you regenerated and committed it).
- **Educating-error proof** — extend `verify.mjs` (and RE-RUN it green): force ≥2 deliberate errors (e.g.
  `send_entity_command` with an unknown `eventType`; `spawn_entity` with no `tkbType`) and assert the MCP
  error output now contains BOTH `error` and a non-empty `hint`.
- `npm run verify` green; no orphan runner processes. (No .NET changes expected in this batch — if you touch
  any .cs, run `dotnet build IOS-IG-SimHost.sln`.)

## Constraints (hard)
- Tool runtime behavior, names, params, and `callApi` semantics are UNCHANGED — this is a
  description/schema/hint consolidation, not a behavior change. The existing `verify.mjs` assertions must
  still pass (203+).
- Generator is ASSEMBLY (verbatim partials + rendered tables); never auto-summarize prose. SKILL.md stays
  committed (agents read it directly); the `--check` keeps it in sync.
- Hints are SHORT (one line: required params + a tiny example) — they are appended to error output, not essays.

## Deliverables
- `tool-catalog.mjs`, `skill-parts/*.md`, `generate-skill.mjs`, refactored `src/index.mjs` (catalog-driven
  defs + hint-augmented errors), regenerated `SKILL.md`, `package.json` scripts (`gen:skill`,
  `gen:skill:check`, `test:catalog`), `MIGRATION-NOTES.md`.
- `.dev/_DONE/ai-debug-api/reports/ADA-BATCH-15-REPORT.md` (DEV-GUIDE format): the design as built, the
  detail-preservation evidence (what the generated SKILL.md covers vs the old; anything reworded), FULL
  `npm run verify` + `test:catalog` + `gen:skill:check` output, the educating-error sample (an error showing
  `error` + `hint`), blockers, debt.
