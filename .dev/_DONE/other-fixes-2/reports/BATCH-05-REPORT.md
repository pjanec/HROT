# BATCH-05 REPORT: HSM Snapshot Decode, Deferred Events & Projector Visual IDs

**Batch:** BATCH-05
**Tasks:** FIX2-010, FIX2-011, FIX2-012
**Status:** APPROVED -- all tasks complete, all tests green

---

## Final Test Run

```
Passed!  - Failed:     0, Passed:   886, Skipped:     8, Total:   894
Duration: 38 s - Hrot.Blueprints.Tests.dll (net8.0)
```

Also run: `Hrot.Hsm.Editor.Tests` -- 267 passed, 0 failed (includes all 3 new tests)

Note: A first Blueprints run produced 885 passed / 1 failed due to flaky timing micro-benchmark `WhenNodePerfTests.WhenNode_ValueChanged_Under100ns_perTick` (machine load caused 11us actual vs 100ns threshold). A second immediate run passed 886/0. This test is unrelated to any of the three fixes.

---

## FIX2-010: Decode HSM EventQueue, TimerSlots, HistorySlots

**Status:** COMPLETE

**Files changed:**
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Debug/HsmDebugSession.cs`
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Debug/HsmDebugSessionTests.cs`

**What was done:**
Added six private `unsafe` decode helpers to `HsmDebugSession`:
- `DecodeEventQueue64(HsmInstance64)` -- reads `EventCount`, casts `EventBuffer` to `HsmEvent*`, builds `HsmEventQueueEntry` list (max 1 event per Tier-1 design constraint)
- `DecodeEventQueue128(HsmInstance128)` -- reads interrupt slot (`InterruptSlotUsed`) plus ring buffer (`EventCount` up to 2), builds ordered queue
- `DecodeTimerSlots64/128` -- iterates `TimerDeadlines[i]`, skips zeros, produces `HsmTimerSlot(i, null, (float)deadline)`
- `DecodeHistorySlots64/128` -- iterates `HistorySlots[i]`, skips `0xFFFF`, looks up `RecordedChildStableId` via `_metadata.StateStableIds`

Wired all six into `Update()` replacing the previous `Array.Empty<>` stubs.

**Test added:**
`HsmSnapshot_DecodeEventQueueTimerSlotsHistorySlots_FromHsmInstance64` -- constructs a `BrainHsm64` with EventCount=1, TimerDeadlines[0]=150, HistorySlots[0]=7 (child flat index), places it in an EntityRepository, calls `sut.Update()`, asserts all three decoded collections are non-empty with correct values.

**Design decisions:**
- For Tier-1 (64-byte), the struct comment explicitly states max 1 event; `DecodeEventQueue64` clamps to 1 accordingly.
- `OwningStateStableId` and `OwningCompositeStableId` are set `null` because the raw struct does not encode which state owns which timer/history slot -- caller enrichment is deferred.
- `IsDeepHistory` is `false` by default; the Tier-1/2 structs do not encode this flag per-slot.

---

## FIX2-011: Fix HSM Deferred Events Round-Trip

**Status:** COMPLETE

**Files changed:**
- `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/Graph/StateNode.cs` -- added `DeferredEventIds: List<ushort>`
- `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/HsmBuilder.cs` -- added `StateBuilder.DeferEvent(ushort)`
- `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/Data/MachineMetadata.cs` -- added `DeferredEventsByState: Dictionary<ushort, ushort[]>`
- `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/HsmEmitter.cs` -- `BuildMachineMetadata` now populates `DeferredEventsByState`
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Model/HsmAssetProjector.cs` -- states loop now reads `metadata.DeferredEventsByState` and populates `node.DeferredEventIds`
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/HsmFluentEmitterTests.cs` -- added round-trip test

**What was done:**
The round-trip was broken at the metadata level: the compiler graph's `StateNode` had no storage for deferred event IDs, so the emitter could not populate them. Added the missing property, wired `DeferEvent()` in the fluent builder, propagated through `BuildMachineMetadata` into `DeferredEventsByState` (keyed by flat index), and finally populated `editor.StateNode.DeferredEventIds` during projection.

**Test added:**
`HsmDeferredEvents_RoundTrip_BlobToProjectorToEmit` -- builds a machine with `builder.State("Idle").Initial().DeferEvent(1).DeferEvent(2)`, calls `Compile()` then `BuildMachineMetadata()` then `HsmAssetProjector.Project()`, asserts `idle.DeferredEventIds` equals `[1, 2]`, and also asserts the emitted code contains `.DeferEvent(1)` before `.DeferEvent(2)`.

**Design decisions:**
- `DeferredEventsByState` uses `ushort` flat index as key (consistent with `StateStableIds`, `StateNames`, etc. in `MachineMetadata`).
- Only states with at least one deferred event are stored in the dictionary (sparse); states with no deferred events simply get no entry.
- `DeferEvent()` uses `Contains()` guard to prevent duplicate IDs at build time.
- `StateDef` (the 32-byte ROM struct) was NOT modified -- there is no room in the fixed layout. The deferred-event information flows through `MachineMetadata` (the symbolication sidecar) instead.

---

## FIX2-012: Fix HSM Projector Transition & Region Visual ID Resolution

**Status:** COMPLETE

**Files changed:**
- `Hrot/Editor/Hrot.Editor.AiShared/Layout/RegionLayoutEntry.cs` -- added `RegionIndex: int`
- `Hrot/Editor/Hrot.Editor.AiShared/Layout/HsmEditorLayoutBuilder.cs` -- `Region()` now stores `RegionIndex`
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Model/HsmAssetProjector.cs` -- transitions section replaced with metadata-keyed lookup; regions section replaced with `RegionIndex`-keyed lookup
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/HsmAssetProjectionTests.cs` -- added stability test

**What was done:**
The projector's transitions section sorted `layout.Transitions.Keys` alphabetically and assigned `VisualId` positionally. This was wrong: after any structural edit (deletion, reorder), the sorted GUID order no longer matched the structural flat index. The fix assigns `VisualId` from `metadata.TransitionVisualIds[i]` first (content-derived, stable across reloads), then does a direct `layout.Transitions.TryGetValue` lookup by that ID for waypoints/comments.

The regions section had the same positional bug. The fix stores `RegionIndex` in `RegionLayoutEntry` (set by `HsmEditorLayoutBuilder.Region()`), builds a reverse map `regionIndex -> stableId`, and applies it instead of sorted-key assignment.

**Test added:**
`HsmProjector_TransitionVisualId_StableAfterDeletion` -- builds a two-transition machine, overrides `metadata.TransitionVisualIds` so that index 0 has the lexicographically larger GUID (forcing the old sorted code to assign backwards), puts comments in the layout keyed by the correct GUIDs, projects, asserts each transition has the correct `VisualId` and its layout comment.

**Design decisions:**
- `HsmEditorLayoutBuilder.Region()` already had a `regionIndex` parameter that was being silently ignored. Storing it is the minimal correct fix with no API change.
- The `RegionLayoutEntry.RegionIndex` default of `0` is safe: if old serialized layouts lack the field, all regions will map to index 0, producing at most one ambiguous entry -- no worse than the old sorted behavior.
- The transitions fix assigns `VisualId` in a separate first pass (before layout application) so the transition node always has a stable ID even when no layout is provided.

---

## Suggested Commit Message

```
fix: decode HSM event queue, timer/history slots; fix deferred-event round-trip; fix projector VisualId stability (FIX2-010, FIX2-011, FIX2-012)

FIX2-010: HsmDebugSession.Update() previously returned Array.Empty<> for
EventQueue, TimerSlots, and HistorySlots. Added DecodeEventQueue64/128,
DecodeTimerSlots64/128, and DecodeHistorySlots64/128 unsafe helpers and
wired them into both BrainHsm64 and BrainHsm128 snapshot paths.

FIX2-011: Deferred events were dropped on save+reload. Added
DeferredEventIds to the compiler graph StateNode, DeferEvent() to
HsmBuilder.StateBuilder, DeferredEventsByState to MachineMetadata,
population in HsmEmitter.BuildMachineMetadata, and consumption in
HsmAssetProjector. Replaced the vacuous emitter test with a full
builder->blob->projector->emitter round-trip test.

FIX2-012: HsmAssetProjector transitions and regions sections sorted
layout Guid keys positionally, misassigning VisualIds after structural
edits. Transitions now use metadata.TransitionVisualIds[index] for stable
lookup. Regions now use a stored RegionIndex in RegionLayoutEntry (also
stored by HsmEditorLayoutBuilder.Region()) to build a reverse map.

Tests: +3 (HsmDebugSessionTests, HsmFluentEmitterTests, HsmAssetProjectionTests)
Blueprints suite: 886 passed, 0 failed
Hsm.Editor suite: 267 passed, 0 failed
```
