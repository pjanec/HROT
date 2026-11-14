# ADA-BATCH-08 Review (Checkpoint / Restore + State Diff, Group H + MCP)

**Verdict:** ACCEPTED (first pass) — features correct & proven on the live process. One pre-existing
serialization defect surfaced and logged for a corrective next batch (not a BATCH-08 regression).
**Reviewer:** dev lead (full build + diff + real headless revert/diff reproduce + independent `npm run verify`
+ orphan check).

## Verified independently (lead)
- **Full-solution build** → 0 errors (`DebugApiService` ctor gained a trailing optional `diffService`; harness
  ripples — full build is the gate).
- `dotnet test … --filter DebugApi` → **71/71** (59 prior + 12 new).
- **REAL checkpoint→restore revert on the live headless process** (the feature's whole point):
  `OperatingEdit, entityCount 1` → `POST /checkpoint` (`inPreview:true`) → `POST /entities/spawn{1001}` →
  `POST /sim/step{10}` (`entityCount 2`) → `POST /checkpoint/restore` → **`inPreview:false, entityCount 1`**.
  The spawned entity is genuinely gone. Confirms ADA-08-D01's "mutation needs a version tick" holds in
  practice (stepping to process the spawn supplies the tick) — "no production impact" verified. ✅
- **`npm run verify` (isolated) → 95/95, VERIFICATION PASSED, exit 0, orphan before=0 after=0.** No leak.
  Step 10d exercises checkpoint (+ double-checkpoint rejection) and diff capture/compare.

## Diff review
- `Checkpoint()` rejects when `OperatingLive` (live run) and when already `IsInPreviewMode` (single slot,
  no double-enter); `RestoreCheckpoint()` requires preview. Uses the `IPreviewController` facade only. The
  `/checkpoint` ↔ `/preview/*` slot overlap is handled explicitly and documented (verify calls `stop_preview`
  before checkpointing to free the slot). Sound.
- Diff: `/diff/capture` (serialize scoped entities → stored `BL#N` baseline) + `/diff/compare`
  (`ComputeTreeDiff(before, after, epsilon)` → DiffNode tree); unscoped compare snapshots ALL current
  entities so births appear in the union. `diffService` reused, not hand-rolled. (`DiffFromCheckpoint` is a
  dead method that only throws a redirect message — harmless, unrouted; could be removed later.)
- MCP: 4 Group H tools (33 total); README + ADA-06-D01 updated (G+H done; I/J/K/L pending).

## Finding — pre-existing NaN/Infinity serialization defect (ADA-08-D02, P2, corrective next batch)
The live reproduce exposed it: `POST /diff/compare` returned `ok:false` `"'N' is an invalid start of a value"`.
Localized it — **`GET /entities` (list) and `GET /entities/{id}` (dump) fail the same way** whenever an
in-scope entity has a `NaN`/`Infinity` float. Root cause is the dump path's
`JsonNode.Parse(JsonSerializer.Serialize(dump, DefaultRelaxed))` round-trip: the write emits the named
literal, `JsonNode.Parse` rejects it. A freshly-spawned tkbType 1001 (CivilianPedestrian) carries such a
field; after restore (clean entity 1000 only) all three endpoints work. **This dates to BATCH-02** — earlier
1001 spawns only checked `entityCount`, never listed/dumped the result, so it stayed hidden. NOT a BATCH-08
regression, but BATCH-08's diff is the natural trigger, and it breaks the core read surface, so it's logged
**P2** and made the corrective Task 0 of BATCH-09 (NaN-safe serialization end-to-end). Accepting BATCH-08 on
the merits of its own correct, proven features.

## Lesson
The live reproduce earned its keep again: round-trip + 71 green tests + 95 green verify all passed, yet a
realistic sequence (spawn a common entity type, then list/diff) hit an opaque failure that no test covered —
because the test fixtures (test-move's entity 1000) happen to be NaN-free. Driving the *actual* AI workflow on
the live process is what surfaced a latent core-surface bug. Also: localize before blaming the new code — the
crash looked diff-specific but is in the shared serializer.
