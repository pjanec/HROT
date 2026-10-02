# ADA-BATCH-09 Review (NaN/Infinity-safe entity serialization — corrective for ADA-08-D02)

**Verdict:** ACCEPTED after THREE rounds. **Reviewer:** dev lead (full build + diff + broad serialization
tests + live NaN-entity reproduce + `npm run verify`). **Commit:** all rounds squashed into one.

## The bug (recap)
The entity dump/list path crashed (`ok:false`, `"'N' is an invalid start of a value"`) whenever any in-scope
entity held a `NaN`/`Infinity` float — breaking `GET /entities`, `GET /entities/{id}`, and `/diff/compare`.
Pre-existing since BATCH-02; surfaced by BATCH-08's diff + a freshly-spawned tkbType 1001 (CivilianPedestrian
— `SimVelocity.Angular.Z` and `VehicleState.SteerAngle` are non-finite by design, persistently).

## Three rounds (the gate did its job each time)
1. **Wrong layer.** Added DebugApi-scoped sentinel converters on `DumpToJsonNode`. Unit tests + the agent's
   own verify went green, but my live reproduce showed `GET /entities` STILL `ok:false`. Rejected — the crash
   is upstream in the shared `EntityStateExtractionService.ExtractEntities` (`SerializeEntity(...).ToJsonString()`
   → `Deserialize(...,DefaultRelaxed)` rejects the named literal), which both list and dump hit first.
2. **Right layer, too lossy.** Moved the fix to `EntityStateExtractionService`: `try-catch(JsonException)`
   returning **empty components** for a throwing entity. Live: crash fixed (list ok:true, clean entity intact),
   but the spawned 1001 showed **0 components** — persistently (verified across 200 steps). Whole entity
   classes would be un-inspectable. Rejected for the flagship inspection feature.
3. **Complete.** On `JsonException`, fall back to reflection-based per-component extraction
   (`_repo.GetRegisteredComponentTypes()` → raw component objects), which `DumpToJsonNode` then serializes via
   the round-1 sentinel-safe `DebugApiDumpOptions`. Reuses both prior rounds' work; no shared-converter/golden
   blast radius.

## Verified independently (lead, round 3)
- Full build → 0 errors. DebugApi suite **75/75**. `EntityStateExtractionServiceTests` 8/8,
  `EventSerializationHelperTests` 3/3. No golden changes (sentinel path triggers only on non-finite values).
  (Pre-existing Fdp.Core benchmark failures unrelated — match baseline.)
- **Live reproduce (the arbiter):** load test-move → capture baseline → preview → spawn 1001 → step 10 →
  - `GET /entities` → ok:true; entity 1000 = 37 comps, **entity 1001 = 27 comps** (was 0).
  - `GET /entities/1001` → ok:true, 27 components, non-finite fields shown as sentinels:
    `SimVelocity.Angular = [0,0,"NaN"]`, `VehicleState.SteerAngle = "NaN"`.
  - `/diff/compare` → ok:true.
- `npm run verify` → **109/109** (Step 10e now asserts the spawned NaN entity has a >0 component count).
  Orphan check clean.

## Outcome & debt
- **ADA-08-D02 → RESOLVED.** The core read surface is robust to non-finite floats: no crash, finite components
  preserved, non-finite fields surfaced as readable `"NaN"/"Infinity"/"-Infinity"` sentinels — which is
  actually *better* than silently dropping them (an AI can now SEE that e.g. `VehicleState.SteerAngle` is NaN,
  potentially flagging a real sim-data issue).
- **ADA-09-D01 (P3, residual):** for a non-finite entity the fallback yields RAW component struct fields
  (27 comps) rather than the translator-shaped DTO output (37 comps for the clean path) — fully inspectable,
  just less readable, and some translator-only components are absent. Acceptable; a converter-level sentinel
  fix in the shared serializer would close it but carries scenario-save golden risk (deferred).

## Lesson
Three rounds, and each rejection came from the same discipline: **re-run the exact failing sequence on the
live process; the agent's green unit tests + its own verify are not the arbiter.** Round 1 was green-but-broken
(wrong layer); round 2 was green-and-non-crashing-but-lossy (empty components) — only the live reproduce, and
specifically *checking the spawned entity's component count*, exposed each gap. Worth the rounds: this is the
core inspection surface, and the final fix elegantly composes all three rounds' pieces.
