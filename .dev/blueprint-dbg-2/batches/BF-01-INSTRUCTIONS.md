# BF-01: Fix node-granular inspector crash — scratch repo missing recordable-non-snapshotable component types

**Type:** Bug fix (single, focused)   **Est:** ~2h
**Onboarding:** `.dev/.guides/DEV-GUIDE.md` (your working contract). One objective only — do not touch unrelated files or other batches' code.

## The bug (reproduced from a user crash)
Attaching a blueprint to an entity and pausing crashes the editor's runtime inspector:
```
System.InvalidOperationException: 'Component type ID 162 not found in repository.
Ensure all component types are registered before playback.'
  at Fdp.Core.FlightRecorder.PlaybackSystem.ApplyChunkData(...)
  at Fdp.Core.FlightRecorder.PlaybackSystem.ApplyFrame(...)
  at Hrot.Blueprints.Core.Debug.SubTickSnapshotRecorder.RestoreTo(...)
  at Hrot.Blueprints.Core.Debug.BlueprintDebugSession.RestorePointerToScratch()   // line ~723
  at ...CaptureStateSnapshot -> GetCurrentStateSnapshot -> ResolveInspectorSnapshot -> InspectorPane.Draw
```

## Root cause (already diagnosed — do not re-investigate, just fix)
`BlueprintDebugSession.RestorePointerToScratch()` seeds the scratch repo with
`_scratchRepo.SyncFrom(_liveRepo)`. With **no** `includeTransient`, `SyncFrom` registers only
**snapshotable** component types (`GetSnapshotableMask(false)` → `ComponentTypeRegistry.GetSnapshotableTypeIds()`).
But the per-node snapshot is a full Flight Recorder **keyframe** (`SubTickSnapshotRecorder.RecordNodeEntry` →
`RecorderSystem.RecordKeyframe` → `RecordAllChunks`), which captures **recordable** types
(`GetRecordableMask()`). A component marked `[DataPolicy(DataPolicy.NoSnapshot)]` is **recordable but NOT
snapshotable** — so the keyframe contains it (e.g. type 162) but `SyncFrom` never registered it in the
scratch → `PlaybackSystem.ApplyChunkData` throws.

## The fix (prescribed — exactly this)
**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs`, method `RestorePointerToScratch()` (~line 723).
Change `_scratchRepo.SyncFrom(_liveRepo);` to **`_scratchRepo.SyncFrom(_liveRepo, includeTransient: true);`**.
Rationale (put in a code comment): with `includeTransient: true`, `SyncFrom` uses `GetSnapshotableMask(true)` =
`ComponentTypeRegistry.GetAllIds()` = **all registered types**, which is a superset of the recordable types the
keyframe contains — so `ApplyFrame` finds every type registered. (Do NOT change `RecordKeyframe` or any
FlightRecorder code; do NOT change the snapshotable/recordable masks.)

## Test (prescribed — assert the discriminating behavior, do not invent your own)
Add ONE regression test (new file `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/SubTickRestoreRegistrationTests.cs`) that **reproduces the recordable-but-not-snapshotable mismatch** at the recorder/repo level (no editor/ImGui needed):

1. Define a test component marked `[ComponentId(<unused id>)] [DataPolicy(DataPolicy.NoSnapshot)] struct NoSnapshotProbe { public int V; }` — this is recordable but excluded from the snapshotable mask (mirrors type 162).
2. Build an `EntityRepository`, register `NoSnapshotProbe` (+ a normal int component), create an entity, set both component values.
3. `var rec = new SubTickSnapshotRecorder(); rec.BeginTick(repo); rec.RecordNodeEntry(repo, "n0");` (the keyframe now includes `NoSnapshotProbe`).
4. **Reproduce the bug:** a scratch seeded with the OLD seeding — `var bad = new EntityRepository(); bad.SyncFrom(repo);` then `Assert.Throws<InvalidOperationException>(() => rec.RestoreTo(0, bad));` (proves the default mask omits the recordable-non-snapshotable type).
5. **Prove the fix:** a scratch seeded the NEW way — `var good = new EntityRepository(); good.SyncFrom(repo, includeTransient: true);` then `rec.RestoreTo(0, good);` must NOT throw, and the restored normal-int component value must equal what was set (assert the exact value).

(If `Assert.Throws` in step 4 is brittle across environments, instead assert that `repo.GetSnapshotableMask(false)` does NOT contain the `NoSnapshotProbe` type id while `GetSnapshotableMask(true)` DOES — proving the mask difference that the fix relies on — and keep step 5 as the positive proof.)

## Do-not-stop-until-green (MANDATORY)
Run the FULL affected test suite yourself and loop until `Failed: 0`:
- `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests` (no `BLUEPRINT_REGENERATE_SNAPSHOTS`, no regen flags)
The ONLY acceptable remaining failures are these documented pre-existing reds (do NOT try to "fix" them, do NOT suppress/exclude/weaken anything to pass): `AiPrimitive_EmitMatchesGoldenSource` (×2), `Stage8_PdbContainsEmbeddedSource`, `Stage8_RoslynCompiler_ProducesNonEmptyPeAndPdb`, `TickFrame_1000Frames_AllocatesZeroBytes`, `MoveToAndFire_GeneratedSource_Snapshot`, `WhenNode_ZeroAllocOnHotPath`. Any NEW failure is yours — diagnose root cause and fix, re-run the whole suite, loop until green.
- A transient first-build error mentioning `MapKeyboardKey.idl` (DDS codegen) can occur — just re-run the build; it is unrelated.

## Constraints
- Touch ONLY `BlueprintDebugSession.cs` (the one-line fix + comment) and the new test file. Do NOT edit other batches' files, do NOT exclude assets, do NOT suppress diagnostics, do NOT weaken existing tests.
- Do NOT commit. Write a short report to `.dev/blueprint-dbg-2/reports/BF-01-REPORT.md` (what you changed, the test, the exact `dotnet test` summary line). The lead reviews and commits.
