# ADA-BATCH-15 Review (Single-Source Tool Catalog → SKILL.md + MCP schemas + Educating Errors)

**Verdict:** ACCEPTED (one lead doc-data fix). **Reviewer:** dev lead (static catalog tests +
gen:skill:check + detail-preservation diff vs the hand-written SKILL.md + live `npm run verify`).

## The crux — no detail lost (the user's concern)
- **Narrative preserved verbatim.** `skill-parts/{00-frontmatter,10-mental-model,20-lifecycle,30-workflows,
  50-gotchas,60-discovery}.md` are byte-for-byte splits of the original SKILL.md §1–3,5–6. The diff vs the
  hand-written version (`0702b05e`) shows changes ONLY in the §4 group headers — the narrative sections are
  untouched. `MIGRATION-NOTES.md`: "not one word was changed", "Intentional rewordings: None".
- **§4 is a superset.** The generated per-tool reference covers all **49** tools, each with summary, param
  table (names/types/required preserved from the index.mjs inputSchemas), Returns, Notes, and now an
  **Example** (new — the old §4 had none). Nothing dropped.

## Single source works (no drift possible)
- `tool-catalog.mjs` (49 entries) is the one source; `src/index.mjs` derives `description` + `inputSchema`
  via `buildDescription`/`buildInputSchema` and the `HINTS` map from it; `generate-skill.mjs` renders §4 from
  it. `gen:skill:check` (wired as an npm script) fails if the committed SKILL.md drifts from regeneration.
- **Coverage tests** (`test-catalog.mjs`, no runner needed): **344/0** — every registered tool has a catalog
  entry and vice-versa (no orphans), every entry has summary/params/hint/example, built `inputSchema.required`
  matches the required params.

## Educating errors (proxy / Tier 2) — verified live
`toolError(msg, env, toolName)` now appends `hint` + `docs` from the catalog; every handler threads its name.
Confirmed on the live process:
- `get_entity{-999999}` → `{error:"Entity -999999 not found.", hint:"Req: networkId (number/long). Example:
  get_entity({networkId:1000})", docs:"ai-debug-sim skill — see the tool reference"}`.
- `send_entity_command{eventType:"__NONEXISTENT__"}` → error + hint naming `list_commands` + an example.
- `npm run verify` → **206/0, VERIFICATION PASSED** (203 prior + 3 new educating-error assertions),
  orphan-clean. Tool runtime behavior unchanged.

## Lead doc-data fix (disclosed)
The first generation mislabeled `focus_entity`/`add_annotation` as a second `Group M` (collided with the TKB
group). I corrected their catalog `group` to `O — Manual-assist (focus / annotations)`, regenerated, and
re-ran gen:skill:check + test:catalog (both green). This is doc-data curation (a label string in the catalog),
same category as the tracker edits — not production logic.

## Note
`spawn_entity{tkbType:0}` was accepted by the API (the hint-on-error assertion for it was inconclusive and the
agent correctly logged it as such rather than forcing it). The educating-error path is proven via the
`send_entity_command` / `get_entity` cases. Fine.

## Lesson
Approach B (single source + assembly-not-compression generation) delivered the no-drift guarantee without
losing the hand-authored narrative — because the generator concatenates verbatim partials and only *renders*
the structured per-tool data. The `gen:skill:check` is what keeps it honest going forward. The only defect
was a data-entry label collision, caught by reading the generated section headers in the diff.
