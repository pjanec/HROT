# BATCH-01: Metadata Extension + Federation Runtime Foundation

**Batch Number:** BATCH-01
**Tasks:** RBF-P1T1, RBF-P1T2, RBF-P1T3, RBF-P1T4, RBF-P2T1, RBF-P2T2
**Phase:** P1 — Metadata extension + validated group loading; P2 (partial) — Federation time state
**Estimated Effort:** 10-14 hours
**Priority:** HIGH
**Dependencies:** None (first batch)

---

## Onboarding & Workflow

### Developer Instructions

This batch lays the foundational infrastructure for the Replay Browser Frankenstein (RBF) feature.
You will:
1. Extend `RecordingMetadata` and `RecordingConfiguration` with federation fields.
2. Wire `RecordingModule` to stamp those fields into `.meta.json` at record time.
3. Implement `FederatedReplayManager.LoadGroup` — the entry point for loading a multi-node exercise.
4. Implement the manager's time state (`BaseWallTicks`, `NodeOffsets`, `SeekAll`) and `IDisposable` lifecycle.

These six tasks form the complete data-writing pipeline and the headless replay manager core. All code is **headless** (no UI dependencies). Phase P4 UI work comes in a later batch.

### Required Reading (IN ORDER)

1. **Design:** `.dev/replay-browser-frankenstein/DESIGN.md`
   - §4 "Recording-side metadata" — drives P1T1, P1T2, P1T3, P1T4
   - §5.1 "FederatedReplayManager" — drives P2T1
   - §5.2 "Per-node entry / disposal" — drives P2T2
2. **Task Details:** `.dev/replay-browser-frankenstein/TASK-DETAILS.md`
   - Tasks: RBF-P1T1, RBF-P1T2, RBF-P1T3, RBF-P1T4, RBF-P2T1, RBF-P2T2
3. **Onboarding:** `.dev/replay-browser-frankenstein/ONBOARDING.md`

### Source Code Locations

| File | Role |
|---|---|
| `FDP/Engine/Fdp.Core/FlightRecorder/Metadata/RecordingMetadata.cs` | Add `ExerciseId`, `NodeId` fields (P1T1) |
| `FDP/Engine/Fdp.Core/FlightRecorder/Metadata/MetadataSerializer.cs` | Used by LoadGroup to read `.meta.json` |
| `FDP/Toolkits/Fdp.Toolkits/Replay/RecordingConfiguration.cs` | Add required `NodeId` property (P1T2) |
| `FDP/Toolkits/Fdp.Toolkits/Replay/RecordingModule.cs` | Pass metadata to AsyncRecorder ctor (P1T3) |
| `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/ReplayBrowserContext.cs` | Used by LoadGroup to instantiate per-node contexts (P1T4) |
| `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Federation/FederatedReplayManager.cs` | **NEW FILE** — LoadGroup + time state + dispose (P1T4, P2T1, P2T2) |

**Test projects:**
- `FDP/Engine/Fdp.Core.Tests/` — metadata round-trip tests (P1T1, P1T3)
- `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Federation/` — manager tests (P1T4, P2T1, P2T2)

### Report Submission

When done, submit your report to:
`.dev/replay-browser-frankenstein/reports/BATCH-01-REPORT.md`

If you have questions, create:
`.dev/replay-browser-frankenstein/questions/BATCH-01-QUESTIONS.md`

---

## Context

This batch is Phase P1 + P2 (partial). Phases depend on each other:
- P1 — writes correct metadata into recordings (read by LoadGroup at load time)
- P2 — the `FederatedReplayManager` owns contexts and coordinates wall-tick seeks

The remaining P2 task (RBF-P2T3 subsystem wiring) and all of P3/P4 depend on this batch completing cleanly.

**Related Tasks:**
- [RBF-P1T1](../TASK-DETAILS.md#rbf-p1t1--recordingmetadata-schema-extension) — `RecordingMetadata` schema extension
- [RBF-P1T2](../TASK-DETAILS.md#rbf-p1t2--recordingconfigurationnodeid) — `RecordingConfiguration.NodeId`
- [RBF-P1T3](../TASK-DETAILS.md#rbf-p1t3--asyncrecorder-stamps-metadata) — `RecordingModule` stamps metadata
- [RBF-P1T4](../TASK-DETAILS.md#rbf-p1t4--federatedreplaymanagerloadgroupstring-paths) — `FederatedReplayManager.LoadGroup`
- [RBF-P2T1](../TASK-DETAILS.md#rbf-p2t1--federatedreplaymanager-time-state--seekall) — Time state + `SeekAll`
- [RBF-P2T2](../TASK-DETAILS.md#rbf-p2t2--federatedreplaymanager-lifecycle--dispose) — Lifecycle + dispose

---

## Tasks

### Task 1: `RecordingMetadata` schema extension (RBF-P1T1)

**File:** `FDP/Engine/Fdp.Core/FlightRecorder/Metadata/RecordingMetadata.cs` (UPDATE)
**Task Definition:** See [TASK-DETAILS.md RBF-P1T1](../TASK-DETAILS.md#rbf-p1t1--recordingmetadata-schema-extension)
**Design ref:** DESIGN.md §4.1

Add two public properties to `RecordingMetadata` — additive only, must be default-safe so legacy JSON without these fields deserialises without error:
```csharp
public Guid ExerciseId { get; set; } = Guid.Empty;
public int  NodeId     { get; set; } = 0;
```

`MetadataSerializer` uses `System.Text.Json`. Existing tests must continue to pass; new tests must verify JSON round-trips and legacy compatibility.

**Tests Required** (in `FDP/Engine/Fdp.Core.Tests/` — find the nearest suitable test file or create one):
- `RBF_P1T1_Metadata_RoundTripsExerciseId`
- `RBF_P1T1_Metadata_RoundTripsNodeId`
- `RBF_P1T1_Metadata_LegacyJsonDeserializes` — JSON string without `ExerciseId`/`NodeId` still deserialises successfully with `Guid.Empty`/`0` defaults

---

### Task 2: `RecordingConfiguration.NodeId` (RBF-P1T2)

**File:** `FDP/Toolkits/Fdp.Toolkits/Replay/RecordingConfiguration.cs` (UPDATE)
**Task Definition:** See [TASK-DETAILS.md RBF-P1T2](../TASK-DETAILS.md#rbf-p1t2--recordingconfigurationnodeid)
**Design ref:** DESIGN.md §4.2

Add:
```csharp
/// <summary>
/// Node identifier embedded in the recording metadata.
/// Identifies which distributed node produced this recording.
/// </summary>
public required int NodeId { get; init; }
```

Then **find every call site** in the solution that constructs `RecordingConfiguration { ... }` and add `NodeId = 0` (or the appropriate value if context makes it obvious). Use the build output to find missing required members — compile and fix until clean.

Search locations to check:
- `FDP/Toolkits/Fdp.Toolkits.Tests/Replay/` — test helpers
- `FDP/Engine/Fdp.Core.Tests/` — any recording tests
- `Hrot/` — subsystem constructors
- `FDP/Examples/` — example code

**Tests Required:**
- Compile check: after the change, `dotnet build FDP/FDP.sln` must succeed with zero errors.
- `RBF_P1T2_Configuration_NodeIdRequired` — assert `typeof(RecordingConfiguration).GetProperty("NodeId")!.GetCustomAttributes(typeof(System.Runtime.CompilerServices.RequiredMemberAttribute), false).Length > 0` (verifies the `required` keyword is in effect).

---

### Task 3: `RecordingModule` stamps metadata (RBF-P1T3)

**File:** `FDP/Toolkits/Fdp.Toolkits/Replay/RecordingModule.cs` (UPDATE)
**Task Definition:** See [TASK-DETAILS.md RBF-P1T3](../TASK-DETAILS.md#rbf-p1t3--asyncrecorder-stamps-metadata)
**Design ref:** DESIGN.md §4.2

In `RegisterSystems`, replace:
```csharp
_recorder = new AsyncRecorder(_config.FilePath);
```
with:
```csharp
var metadata = new RecordingMetadata { ExerciseId = _config.ExerciseId, NodeId = _config.NodeId };
_recorder = new AsyncRecorder(_config.FilePath, metadata);
```

No other change to `RecordingModule` or `AsyncRecorder` is needed.

**Tests Required** (in `FDP/Engine/Fdp.Core.Tests/` — place near existing `AsyncRecorderTests.cs`):
- `RBF_P1T3_RecordingModule_WritesExerciseIdToSidecar` — install the module with a known `ExerciseId`, record at least 1 frame, dispose, read back the `.meta.json`, assert `ExerciseId` matches.
- `RBF_P1T3_RecordingModule_WritesNodeIdToSidecar` — same for `NodeId = 7`.
- `RBF_P1T3_AsyncRecorder_NoCtorChangeRequired` — verify (as a reflection assertion) that `AsyncRecorder`'s constructor signature accepts `(string, RecordingMetadata?)` and NOT any new parameters.

For these tests you need a real `AsyncRecorder` write cycle. Look at `FDP/Engine/Fdp.Core.Tests/AsyncRecorderTests.cs` to understand the pattern: create a temp file, install the module/recorder, tick frames, dispose, read back.

---

### Task 4: `FederatedReplayManager.LoadGroup` (RBF-P1T4 + P2T1 + P2T2)

**File:** `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Federation/FederatedReplayManager.cs` (NEW FILE)
**Task Definition:** See TASK-DETAILS.md tasks RBF-P1T4, RBF-P2T1, RBF-P2T2
**Design ref:** DESIGN.md §4.3, §5.1, §5.2

Create the directory `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Federation/` and implement `FederatedReplayManager`:

```csharp
namespace Fdp.Toolkit.ReplayBrowser.Federation
{
    public sealed class FederatedReplayManager : IDisposable
    {
        // --- state from LoadGroup ---
        public IReadOnlyDictionary<int, ReplayBrowserContext> Contexts { get; }
        public Guid ExerciseId { get; }

        // --- time state (RBF-P2T1) ---
        public long BaseWallTicks { get; private set; }
        public IReadOnlyDictionary<int, long> NodeOffsets { get; }   // wraps internal mutable dict
        public int LocalEntitiesProviderNodeId { get; private set; } // defaults to lowest NodeId

        public event Action? OnTimeChanged;

        // --- entry points ---
        public static FederatedReplayManager LoadGroup(string[] paths);  // throws LoadGroupException on rejection
        public void SetBaseWallTicks(long ticks);
        public void SetNodeOffset(int nodeId, long offsetTicks);
        public void SetLocalEntitiesProvider(int nodeId);
        public void SeekAll();

        // --- lifecycle (RBF-P2T2) ---
        public void Dispose();
    }
}
```

**LoadGroup validation rules (DESIGN §4.3, binding):**
1. For each path, load the `.meta.json` sidecar via `MetadataSerializer.Deserialize`.
   The sidecar path is `path + ".meta.json"` (existing convention — verify by looking at `AsyncRecorder.Dispose` or existing tests).
2. Reject with `"unknown exercise"` if any `ExerciseId == Guid.Empty`.
3. Reject with `"exercise mismatch"` if not all `ExerciseId` values are identical.
4. Reject with `"duplicate NodeId {id}"` if any two files share the same `NodeId`.
5. On success, instantiate `ReplayBrowserContext` per file and call `ctx.LoadRecording(path)`.
6. Store in `Dictionary<int, ReplayBrowserContext>` keyed by `NodeId`.
7. Set `LocalEntitiesProviderNodeId` to the **lowest** NodeId in the loaded set.

On rejection, dispose any contexts already created (if partial) and throw `LoadGroupException` (define as a simple `Exception` subclass in the same namespace).

**SeekAll (DESIGN §5.1):**
```
targetWallTicks_node = BaseWallTicks + NodeOffsets.GetValueOrDefault(nodeId, 0L)
ctx.Playback.SeekToWallClockTicks(ctx.SandboxRepo, targetWallTicks_node)
```
Use `PlaybackController.SeekToWallClockTicks` at `FDP/Engine/Fdp.Core/FlightRecorder/PlaybackController.cs` (line 245 area).

**OnTimeChanged firing rules:**
- `SetBaseWallTicks` → `SeekAll()` → fires `OnTimeChanged`.
- `SetNodeOffset` → `SeekAll()` → fires `OnTimeChanged`.
- `SetLocalEntitiesProvider` → fires `OnTimeChanged` only (does NOT seek; the merged-view must rebuild, but seeking is unchanged).
- `SeekAll` fires `OnTimeChanged` itself (always, even if called directly).

**Dispose (DESIGN §5.2):**
- Dispose every owned `ReplayBrowserContext`.
- Clear internal state.
- Double-dispose must be a no-op.

**Tests Required** (create `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Federation/FederatedReplayManagerTests.cs`):

For LoadGroup tests you need synthetic `.fdp` + `.meta.json` files. Look at the existing test support in `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Support/` and `FDP/Engine/Fdp.Core.Tests/` to understand how test recordings are produced.

- `RBF_P1T4_LoadGroup_HappyPath` — three synthetic `.fdp`+`.meta.json` with identical `ExerciseId` and distinct NodeIds {1,2,3} — manager loads, `Contexts.Count == 3`, `Contexts.Keys` == {1,2,3}, `ExerciseId` correct.
- `RBF_P1T4_LoadGroup_RejectsExerciseMismatch` — two files, different `ExerciseId` → `LoadGroupException`, no contexts allocated.
- `RBF_P1T4_LoadGroup_RejectsDuplicateNodeId` — two files, same NodeId → `LoadGroupException`.
- `RBF_P1T4_LoadGroup_RejectsEmptyExerciseId` — file with `ExerciseId == Guid.Empty` → `LoadGroupException`.
- `RBF_P1T4_LoadGroup_DisposesAllOnError` — three paths, third triggers mid-batch rejection (or file-not-found); first two contexts disposed (verify `Playback == null`).
- `RBF_P2T1_SeekAll_SeeksEachContext` — manager with two contexts; `SetBaseWallTicks(t)` → both contexts seeked (verify via `CurrentFrame` or mock).
- `RBF_P2T1_SetBaseWallTicks_FiresOnTimeChanged` — event fires exactly once per call.
- `RBF_P2T1_SetNodeOffset_FiresOnTimeChanged` — event fires exactly once per call.
- `RBF_P2T1_DefaultOffsetIsZero` — node with no `NodeOffsets` entry seeked to `BaseWallTicks`.
- `RBF_P2T1_LocalEntitiesProvider_DefaultsToLowestNodeId` — after `LoadGroup` of nodes {2,5,1}, provider is 1.
- `RBF_P2T1_SetLocalEntitiesProvider_FiresOnTimeChanged` — event fires once per call (does NOT seek).
- `RBF_P2T1_SetLocalEntitiesProvider_RejectsUnknownNodeId` — throws `ArgumentOutOfRangeException` for unknown node.
- `RBF_P2T2_Dispose_DisposesAllContexts` — after dispose, all `ctx.Playback == null`.
- `RBF_P2T2_DoubleDispose_NoThrow` — second `Dispose()` call does not throw.

---

## Testing Requirements

- All tests in the batch must pass before submitting the report.
- Also run the full existing test suite for the touched assemblies:
  ```powershell
  dotnet test FDP/Engine/Fdp.Core.Tests/Fdp.Core.Tests.csproj
  dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj
  ```
- Minimum 20 new tests across all tasks.
- Tests must verify **actual behaviour** (values, frame indices, event counts), not just compilation.

---

## Quality Standards

**TEST QUALITY:**
- NOT ACCEPTABLE: tests that only check object creation or "does not throw" without verifying state.
- REQUIRED: tests that assert specific values (e.g., `CurrentFrame`, event counts, `ExerciseId` equality).
- `RBF_P1T4_LoadGroup_HappyPath` must assert `Contexts.Count`, the key set, AND the `ExerciseId` on the manager.

**CODE QUALITY:**
- All new public APIs must have XML doc comments (at minimum a one-line `<summary>`).
- No silent exception swallowing. `LoadGroup` rejects loudly via exception with a human-readable reason.
- `FederatedReplayManager` must be `sealed`.

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1 (RBF-P1T1):** Implement → Write tests → **ALL tests pass** ✅
2. **Task 2 (RBF-P1T2):** Implement → Fix all call sites → Build succeeds → Write tests → **ALL tests pass** ✅
3. **Task 3 (RBF-P1T3):** Implement → Write tests → **ALL tests pass** ✅
4. **Task 4 (RBF-P1T4 + P2T1 + P2T2):** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until current task tests all pass.
**DO NOT** stop to ask for permission for obvious things (running tests, fixing build errors, finding call sites). Work autonomously until all tests pass, then write the report.

---

## Success Criteria

- [ ] `RecordingMetadata` has `ExerciseId` and `NodeId` (additive, default-safe)
- [ ] `RecordingConfiguration` has required `NodeId`; all call sites compile
- [ ] `RecordingModule.RegisterSystems` passes metadata to `AsyncRecorder` ctor
- [ ] `FederatedReplayManager.LoadGroup` validates and loads multi-node groups
- [ ] `FederatedReplayManager` manages time state (`BaseWallTicks`, offsets, `SeekAll`)
- [ ] `FederatedReplayManager` implements `IDisposable` correctly
- [ ] All new tests pass (`dotnet test` green)
- [ ] All existing tests still pass
- [ ] Report submitted to `.dev/replay-browser-frankenstein/reports/BATCH-01-REPORT.md`

---

## Developer Insights

**Q1:** What problems did you encounter during implementation? How did you resolve them?

**Q2:** Did you spot any weak points in the existing codebase that could cause issues in later batches (especially around `ReplayBrowserContext`, `PlaybackController.SeekToWallClockTicks`, or `MetadataSerializer`)?

**Q3:** What design decisions did you make beyond the instructions? What alternatives did you consider?

**Q4:** What edge cases did you discover that weren't mentioned in the spec?

**Q5:** Are there any concerns about how `FederatedReplayManager` will interact with the `TransientMasterBuilder` in Phase P3?

**Suggested commit message:** What did you achieve in this batch?

---

## Reference Materials

- **Task Defs:** `.dev/replay-browser-frankenstein/TASK-DETAILS.md` — RBF-P1T1, RBF-P1T2, RBF-P1T3, RBF-P1T4, RBF-P2T1, RBF-P2T2
- **Design:** `.dev/replay-browser-frankenstein/DESIGN.md` — §4 and §5
- **AsyncRecorder:** `FDP/Engine/Fdp.Core/FlightRecorder/AsyncRecorder.cs` — ctor signature, metadata write
- **MetadataSerializer:** `FDP/Engine/Fdp.Core/FlightRecorder/Metadata/MetadataSerializer.cs`
- **ReplayBrowserContext:** `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/ReplayBrowserContext.cs`
- **PlaybackController:** `FDP/Engine/Fdp.Core/FlightRecorder/PlaybackController.cs` (line 245 area for `SeekToWallClockTicks`)
- **Existing recorder tests:** `FDP/Engine/Fdp.Core.Tests/AsyncRecorderTests.cs`
- **Existing replay browser tests:** `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/`
- **Developer skill guide:** `.github/skills/developer/SKILL.md`
