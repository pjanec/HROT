# ADA-BATCH-09: NaN/Infinity-Safe Entity Serialization (corrective)

**Batch Number:** ADA-BATCH-09
**Tasks:** ADA-08-D02 corrective (NaN/Infinity-safe serialization across the DebugApi read surface)
**Phase:** Corrective (hardens the core read surface before more leverage features build on it)
**Estimated Effort:** ~8 hours
**Executor:** sonnet (serialization internals + blast-radius awareness)
**Priority:** HIGH (P2 debt; breaks `GET /entities`, `GET /entities/{id}`, `POST /diff/compare`)
**Dependencies:** BATCH-08.

---

## The bug (lead-confirmed on the live process)

The entity dump path serializes then re-parses:
`JsonNode.Parse(JsonSerializer.Serialize(dump, FdpJsonOptionsRegistry.DefaultRelaxed))`
(`DebugApiService.cs` ~line 593, and the same pattern in the TKB/event/diff paths). When **any in-scope
entity has a `NaN` or `Infinity` float** (e.g. a freshly-spawned `tkbType 1001` CivilianPedestrian, before
its fields settle), the write emits the named literal `NaN`/`Infinity` and `JsonNode.Parse` **rejects** it:
`"'N' is an invalid start of a value. LineNumber: 0 | BytePositionInLine: 7."`

Effect: `GET /entities` (list), `GET /entities/{id}` (dump), and `POST /diff/compare` all return `ok:false`
whenever a non-finite-float entity is in scope. Confirmed live: spawn `tkbType 1001` + `step`, then any of
those endpoints fails; after `restore` (clean entity 1000 only) they all work. Pre-existing since BATCH-02;
only now exposed (earlier 1001 spawns checked `entityCount`, never listed/dumped/diffed).

The non-finite floats are inside `SimTransform`/`SimVelocity` vectors (written by the `VectorNArrayConverter`s
as raw array elements) and/or scalar float fields.

---

## Required reading
1. `.dev/.guides/DEV-GUIDE.md`
2. `.dev/ai-debug-api/reviews/ADA-BATCH-08-REVIEW.md` (the finding + reproduce).
3. `FDP/Engine/Fdp.Core/Serialization/FdpJsonOptionsRegistry.cs` (DefaultRelaxed + converters) and
   `FDP/Engine/Fdp.Core/Serialization/Converters/VectorArrayConverters.cs`.
4. `DebugApiService.cs` dump/diff/TKB serialize call sites (the `JsonNode.Parse(JsonSerializer.Serialize(...))`
   pattern); `EventSerializationHelper` (Fdp.Toolkits.Diagnostics).

> No codebase-memory MCP (hangs — Grep/Glob/Read). No git commit. **NEVER regenerate goldens to make a test
> pass** (see the snapshot-regen lesson) — if a golden legitimately changes, justify it explicitly; prefer a
> fix that does NOT churn goldens. The lead re-runs the FULL suite + a live NaN-entity reproduce.

## The fix (requirements — pick the cleanest implementation)
- **Output MUST be valid standard JSON.** Emitting `NaN`/`Infinity` as JSON named literals is NOT acceptable:
  the Node MCP client (`JSON.parse` / `fetch().json()`) rejects them too. Non-finite floats must be
  **sanitized to valid JSON** — preferred: a string sentinel (`"NaN"`, `"Infinity"`, `"-Infinity"`) so the AI
  consumer still sees that the value was non-finite; `null` is acceptable if simpler. Decide and document.
- Cover BOTH scalar `float`/`double` fields AND vector array elements (the `VectorNArrayConverter`s) — a NaN
  inside `Position:[x,y,z]` must also be sanitized.
- **Blast-radius control (important):** `FdpJsonOptionsRegistry.DefaultRelaxed`/`Indented` are SHARED with UI
  panels (`EntityInspectorPanel`, `EventBrowserPanel`), `MetadataSerializer`, and golden snapshots. STRONGLY
  PREFER scoping the fix to the **DebugApi read surface** — e.g. a DebugApi-local `JsonSerializerOptions`
  (clone of DefaultRelaxed + a non-finite-safe float converter + non-finite-safe vector converters) used by
  the dump/diff/TKB/event serialize call sites — so you do NOT change global UI/golden behavior. If you
  instead modify the shared registry, you MUST run the FULL solution test suite and account for every golden
  that changes (justify, don't blindly regenerate).
- Also fix the **string round-trip fragility** where reasonable: prefer `JsonSerializer.SerializeToNode(dump,
  opts)` over `JsonNode.Parse(JsonSerializer.Serialize(dump, opts))` so there's no parse step that can reject
  valid-but-named output. (SerializeToNode with the sanitizing converter yields clean nodes directly.)

## Verification (prove it on the live process + no regression)
- **Tier-1 (EditorHarness):** a test that an entity carrying a `NaN`/`Infinity` float serializes through
  `DumpEntity`/`ListEntities`/the diff snapshot without throwing, and the non-finite field is the sentinel
  (or null). (Construct such an entity in the harness — set a NaN on a SimTransform/SimVelocity field, or
  whatever is reachable.)
- **Tier-2 (live headless / MCP verify):** spawn `tkbType 1001` + `step`, then `GET /entities`,
  `GET /entities/{newId}`, and a `/diff/capture`→`/diff/compare` spanning the spawn **all return `ok:true`**,
  and the MCP client (`npm run verify`, Node) parses the responses without error. Add a step to `verify.mjs`
  that dumps/lists with the NaN entity present. No orphan processes.
- **Full regression:** `dotnet build IOS-IG-SimHost.sln` (0 errors) AND run the **broad** serialization tests
  (at minimum `EventSerializationHelperTests`, the DebugApi suite, and any FdpJsonOptionsRegistry/serializer
  goldens) — report the full results. If you scoped to DebugApi-local options, confirm the shared registry
  tests/goldens are untouched.

## Constraints (hard)
- Valid standard JSON out (Node-parseable). String sentinel preferred over null; never named literals.
- Prefer DebugApi-scoped options to avoid golden churn; if touching shared registry, full-suite + justify goldens.
- Don't regenerate snapshots to pass. Frozen `TestAssets`. Marshalling unchanged.

## Deliverables
- Code + green Tier-1 + green live reproduce + green `npm run verify` (with a NaN-entity dump/list step).
- `.dev/ai-debug-api/reports/ADA-BATCH-09-REPORT.md` (DEV-GUIDE format): the chosen approach (sentinel vs
  null, scoped vs shared), built status, FULL test summary (incl. the broad serialization tests), the live
  NaN-entity reproduce output (list/dump/diff all ok:true with sentinels), blockers, and ADA-08-D02 → RESOLVED.
