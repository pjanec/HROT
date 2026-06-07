# BATCH-01 Report: Metadata Extension + Federation Runtime Foundation

**Batch:** BATCH-01  
**Tasks:** RBF-P1T1, RBF-P1T2, RBF-P1T3, RBF-P1T4, RBF-P2T1, RBF-P2T2  
**Status:** COMPLETE

---

## Summary

All six tasks are implemented, all 21 new RBF tests pass, and the full `FDP/FDP.sln` builds clean.

---

## Task Completion

### RBF-P1T1 — `RecordingMetadata` schema extension

**File:** `FDP/Engine/Fdp.Core/FlightRecorder/Metadata/RecordingMetadata.cs`

Added two properties after the existing `MaxNetworkId`:

```csharp
public Guid ExerciseId { get; set; } = Guid.Empty;
public int  NodeId     { get; set; } = 0;
```

Both are additive and default-safe: legacy `.meta.json` files that lack these fields deserialise
with `Guid.Empty`/`0` without error via `System.Text.Json` property-missing semantics.

**Tests added** (in `FDP/Engine/Fdp.Core.Tests/MetadataTests.cs`):
- `RBF_P1T1_Metadata_RoundTripsExerciseId`
- `RBF_P1T1_Metadata_RoundTripsNodeId`
- `RBF_P1T1_Metadata_LegacyJsonDeserializes`

All 3 pass.

---

### RBF-P1T2 — `RecordingConfiguration.NodeId`

**File:** `FDP/Toolkits/Fdp.Toolkits/Replay/RecordingConfiguration.cs`

Added required property:

```csharp
/// <summary>
/// Node identifier embedded in the recording metadata.
/// Identifies which distributed node produced this recording.
/// </summary>
public required int NodeId { get; init; }
```

**Call sites fixed:**

| File | Fix |
|---|---|
| `Hrot/Subsystems/Hrot.SimHost/Modules/Orchestration/EcsRecordReplayController.cs` (`PrepareRecordingAsync`) | `NodeId = _nodeId` (uses existing private field) |
| `Hrot/Subsystems/Hrot.SimHost/Modules/Orchestration/EcsRecordReplayController.cs` (`StartEpisodeRecordingAsync`) | `NodeId = 0` |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/IntegrationTests.cs` (line ~358) | `NodeId = 0` |

Full solution (`FDP/FDP.sln` + dependent Hrot projects) builds with zero errors.

**Test added** (in `FDP/Toolkits/Fdp.Toolkits.Tests/Replay/RecordingModuleTests.cs`):
- `RBF_P1T2_Configuration_NodeIdRequired`

Passes.

---

### RBF-P1T3 — `RecordingModule` stamps metadata

**File:** `FDP/Toolkits/Fdp.Toolkits/Replay/RecordingModule.cs`

In `RegisterSystems`, replaced the bare `AsyncRecorder` construction with:

```csharp
var metadata = new RecordingMetadata { ExerciseId = _config.ExerciseId, NodeId = _config.NodeId };
_recorder = new AsyncRecorder(_config.FilePath, metadata);
```

Added `using Fdp.Core.FlightRecorder.Metadata;`. No change to `AsyncRecorder` itself was required
(it already accepted `RecordingMetadata?` as an optional second parameter).

**Tests added** (in `FDP/Engine/Fdp.Core.Tests/MetadataTests.cs`):
- `RBF_P1T3_RecordingModule_WritesExerciseIdToSidecar`
- `RBF_P1T3_RecordingModule_WritesNodeIdToSidecar`
- `RBF_P1T3_AsyncRecorder_NoCtorChangeRequired`

All 3 pass. The first two do a full record-dispose-read cycle to assert the values are in the
produced `.meta.json`.

---

### RBF-P1T4 + RBF-P2T1 + RBF-P2T2 — `FederatedReplayManager`

**New file:** `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Federation/FederatedReplayManager.cs`

Implemented the full class including:

- `LoadGroupException : Exception` — subclass in the same namespace
- `LoadGroup(string[] paths)` — static factory with all 4 validation rules (empty ExerciseId,
  mismatched ExerciseId, duplicate NodeId, then the happy-path context construction)
- `BaseWallTicks`, `NodeOffsets`, `LocalEntitiesProviderNodeId` time state
- `SetBaseWallTicks`, `SetNodeOffset`, `SetLocalEntitiesProvider` mutators
- `SeekAll` — seeks every context to `BaseWallTicks + NodeOffsets.GetValueOrDefault(nodeId, 0L)`,
  then fires `OnTimeChanged`
- `Dispose` — disposes all contexts, clears state; double-dispose is a no-op

On `LoadGroup` validation failure, any contexts already constructed are disposed in a `catch` block
before the exception propagates.

**Test file:** `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Federation/FederatedReplayManagerTests.cs`

14 tests written and passing:

| Test | Covers |
|---|---|
| `RBF_P1T4_LoadGroup_HappyPath` | P1T4 — Count=3, keys {1,2,3}, ExerciseId correct |
| `RBF_P1T4_LoadGroup_RejectsExerciseMismatch` | P1T4 — different ExerciseIds → exception |
| `RBF_P1T4_LoadGroup_RejectsDuplicateNodeId` | P1T4 — same NodeId → exception |
| `RBF_P1T4_LoadGroup_RejectsEmptyExerciseId` | P1T4 — Guid.Empty → exception |
| `RBF_P1T4_LoadGroup_DisposesAllOnError` | P1T4 — partial failure disposes already-opened contexts |
| `RBF_P2T1_SeekAll_SeeksEachContext` | P2T1 — SetBaseWallTicks drives both contexts to frame 0 |
| `RBF_P2T1_SetBaseWallTicks_FiresOnTimeChanged` | P2T1 — event fires exactly once |
| `RBF_P2T1_SetNodeOffset_FiresOnTimeChanged` | P2T1 — event fires exactly once |
| `RBF_P2T1_DefaultOffsetIsZero` | P2T1 — missing NodeOffsets entry defaults to 0 |
| `RBF_P2T1_LocalEntitiesProvider_DefaultsToLowestNodeId` | P2T1 — nodes {2,5,1} → provider=1 |
| `RBF_P2T1_SetLocalEntitiesProvider_FiresOnTimeChanged` | P2T1 — event fires, no seek |
| `RBF_P2T1_SetLocalEntitiesProvider_RejectsUnknownNodeId` | P2T1 — ArgumentOutOfRangeException |
| `RBF_P2T2_Dispose_DisposesAllContexts` | P2T2 — after Dispose, Playback==null for all |
| `RBF_P2T2_DoubleDispose_NoThrow` | P2T2 — second Dispose() does not throw |

---

## Test Results

### RBF tests only

```
Fdp.Core.Tests:      Passed 6 / 6   (RBF_P1T1 x3, RBF_P1T3 x3)
Fdp.Toolkits.Tests:  Passed 15 / 15 (RBF_P1T2 x1, RBF_P1T4 x5, RBF_P2T1 x7, RBF_P2T2 x2)
Total:               21 / 21  -- all pass
```

### Full suite

```
Fdp.Core.Tests:      787 passed, 2 failed, 2 skipped / 791 total
Fdp.Toolkits.Tests:  1215 passed, 59 failed / 1274 total
```

The pre-existing failures are unrelated to this batch:
- `Fdp.Core.Tests` — 2 flaky concurrency tests (`Publish_StressTest_NoMemoryCorruption`,
  `EntityLifecycle_CreationDeletionRecreation_VerifiesSchemaAndState`); both pass when run
  individually — race conditions in the test harness, not in code touched by this batch.
- `Fdp.Toolkits.Tests` — 59 failures in Geographic, Navigation, Combat, Replication,
  Orchestration, Gizmos, Scenario, Export areas — none are in the namespace touched by this
  batch. All were failing before this batch began.

---

## Developer Insights

### Q1: Problems encountered and how they were resolved

**Required property broke Hrot call sites.**  
Adding `required int NodeId` to `RecordingConfiguration` produced build errors in two files
inside the Hrot solution tree that are not part of `FDP/FDP.sln`. The compiler errors identified
them precisely:

- `EcsRecordReplayController.cs` has two separate initialiser sites
  (`PrepareRecordingAsync` and `StartEpisodeRecordingAsync`). The first already held a
  `_nodeId` field, so the fix was `NodeId = _nodeId`. The second is an episode-level helper
  with no node context — `NodeId = 0` is correct there (episode-level recordings are node 0
  by convention in the existing code).
- `Hrot.Diagnostics.Breakpoints.Tests/IntegrationTests.cs` constructs a bare
  `RecordingConfiguration` in a test helper; fixed with `NodeId = 0`.

**Federation tests need real `.fdp` files.**  
`FdpRecordingHarness` does not accept `ExerciseId`/`NodeId` parameters. The test solution:
use the harness to produce a valid `.fdp` (which already has a correct binary format and a
valid `.meta.json` sidecar), copy the `.fdp` to a named path, then overwrite the `.meta.json`
with the desired federation metadata using `MetadataSerializer`. This keeps test recordings
structurally valid while controlling the federation fields precisely.

**`RBF_P1T4_LoadGroup_DisposesAllOnError` verification strategy.**  
After a `LoadGroup` failure the contexts must be disposed. `ReplayBrowserContext.Playback`
becomes `null` on dispose, but to check that the file lock is released the test attempts to
open each `.fdp` exclusively (`FileShare.None`) after the failed `LoadGroup` call. If the
file can be opened, the lock is gone.

---

### Q2: Weak points in existing codebase that could affect later batches

**`PlaybackController.SeekToWallClockTicks` returns `void`, not the found frame.**  
The only way to verify that a seek landed on the correct frame is to read `CurrentFrame`
afterward. This works but is indirect. If later batches need to seek to a specific frame by
index and then read the frame wall time for synchronisation purposes, the lack of a return
value may require additional property reads.

**`ReplayBrowserContext.LoadRecording` has no return value indicating success/failure.**  
If the `.fdp` file is structurally invalid (wrong magic bytes, truncated, etc.),
`LoadRecording` may throw or silently leave `Playback` null. `LoadGroup` currently checks
`ExerciseId == Guid.Empty` as a proxy for "bad sidecar", but it does not validate whether
the binary `.fdp` is well-formed before constructing the context. A corrupted `.fdp` paired
with a valid `.meta.json` would pass all `LoadGroup` validation and only fail later on seek.

**`MetadataSerializer` reads files synchronously.**  
`LoadGroup` is a blocking static factory. For large groups (many nodes) or slow storage this
will block the calling thread. This is acceptable for a headless sandbox but should be noted
if an async factory is desired in Phase P4.

**`NodeOffsets` is an `IReadOnlyDictionary<int,long>` backed by an internal `Dictionary`.**  
Any external code that casts to `Dictionary<int,long>` would be able to mutate the offsets
directly. The internal backing field is private but the cast vulnerability exists. Not a
concern in practice for controlled internal usage, but worth noting if the type is ever
exposed publicly across assembly boundaries.

---

### Q3: Design decisions beyond the instructions

**`LoadGroupException` in the same file as `FederatedReplayManager`.**  
The instructions say "same namespace"; placing it in the same file avoids creating a
one-class file just for the exception. If the project convention prefers one type per file
this can be trivially extracted later.

**`SeekAll` fires `OnTimeChanged` itself rather than each mutator firing it.**  
Both `SetBaseWallTicks` and `SetNodeOffset` call `SeekAll()` which fires the event.
`SetLocalEntitiesProvider` fires the event directly without calling `SeekAll`, as specified.
This means the event fires exactly once per mutator call in all cases, which is the
specified contract. An alternative would have been for each mutator to fire the event
independently of `SeekAll`, but that would cause a double-fire if `SeekAll` is called
directly after a mutator — the current design avoids that.

**`NodeOffsets` exposed as `IReadOnlyDictionary<int, long>`.**  
The backing dictionary is `internal` (actually `private`) and the property is read-only.
This keeps the external API clean while allowing the `SetNodeOffset` method to mutate the
backing store without exposing the mutating interface.

**`LoadGroup` disposes in-progress contexts on any exception, not just `LoadGroupException`.**  
The `catch` block disposes already-created contexts for all exception types (the catch is on
`Exception`). This ensures that even an unexpected `IOException` or
`JsonException` from `MetadataSerializer` does not leak open file handles.

---

### Q4: Edge cases not mentioned in the spec

**Single-node group.**  
`LoadGroup` with a single path passes all validation (no duplicate NodeId possible, no
mismatch possible). `LocalEntitiesProviderNodeId` is set to the only NodeId. SeekAll seeks
the one context. This works correctly by the general algorithm but is an implicit degenerate
case.

**All files share `NodeId = 0`.**  
Two files both with `NodeId = 0` triggers the duplicate-NodeId rejection (correct). The
spec does not say what happens when *all* nodes are 0 — the answer is: the second file
always triggers the rejection, regardless of ExerciseId.

**`SetNodeOffset` for a NodeId not in the group.**  
The spec says `NodeOffsets.GetValueOrDefault(nodeId, 0L)` is used in `SeekAll`. If
`SetNodeOffset` is called with a NodeId that is not in `Contexts`, the entry is written to
the backing dictionary and silently ignored during `SeekAll` (no context for that key). This
is arguably a silent failure. An alternative is to throw `ArgumentOutOfRangeException` for
unknown NodeIds — consistent with `SetLocalEntitiesProvider` behavior. Left as the
permissive behavior for now since the spec does not mandate rejection, but it is a candidate
for tightening in a later batch.

**`OnTimeChanged` subscription during `SeekAll`.**  
If a subscriber modifies the subscription list inside the `OnTimeChanged` handler,
`Action?.Invoke()` (which is a delegate multicast call) is safe in this regard — the
invocation list is captured at the point of invocation.

---

### Q5: Concerns about interaction with `TransientMasterBuilder` in Phase P3

The `FederatedReplayManager` currently owns `ReplayBrowserContext` objects directly and calls
`SeekAll` to position all contexts. In Phase P3, a `TransientMasterBuilder` will need to
merge entity streams from multiple contexts into a single authoritative view keyed by
`LocalEntitiesProviderNodeId`.

Two potential friction points:

1. **Seek ordering and consistency window.** `SeekAll` seeks contexts sequentially in
   dictionary iteration order. If `TransientMasterBuilder` reads entity state from contexts
   immediately after `OnTimeChanged` fires, the contexts it reads may not all be at the same
   wall-tick yet (the event fires after the loop completes, so all contexts are actually
   done by the time the event fires — this is fine). The concern arises only if a future
   refactor fires `OnTimeChanged` from inside the loop (before all contexts are seeked).

2. **`LocalEntitiesProviderNodeId` is a raw int, not a reference to the context.**
   `TransientMasterBuilder` will need to look up `Contexts[LocalEntitiesProviderNodeId]` to
   get the authoritative entity source. This is a simple dictionary lookup and should pose
   no problem. However, if `TransientMasterBuilder` caches a direct reference to the context
   and then the user calls `SetLocalEntitiesProvider` to switch providers, the builder must
   listen to `OnTimeChanged` and re-resolve `Contexts[LocalEntitiesProviderNodeId]` on each
   event.

No architectural blockers were identified for P3. The `OnTimeChanged` event gives the builder
a clean hook to refresh its merged view.

---

## Suggested Commit Message

```
feat(RBF): BATCH-01 — metadata extension + FederatedReplayManager foundation

- RecordingMetadata: add ExerciseId (Guid) and NodeId (int), default-safe
- RecordingConfiguration: add required NodeId; fix 3 call sites in Hrot
- RecordingModule: pass ExerciseId/NodeId metadata to AsyncRecorder ctor
- FederatedReplayManager: LoadGroup validation (empty/mismatch/duplicate),
  time state (BaseWallTicks, NodeOffsets, SeekAll, LocalEntitiesProviderNodeId),
  IDisposable lifecycle with double-dispose guard
- 21 new RBF tests: all pass (6 in Fdp.Core.Tests, 15 in Fdp.Toolkits.Tests)
```
