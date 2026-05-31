# BATCH-05: HSM Snapshot Decode, Deferred Events & Projector Visual IDs

**Batch Number:** BATCH-05  
**Tasks:** FIX2-010, FIX2-011, FIX2-012  
**Priority:** MEDIUM  
**Dependencies:** None from previous batches

---

## Mandatory Workflow

**Read AGENTS.md at the repo root before writing a single line of code.**

Complete tasks in strict sequence. For each task:
1. Define the **success condition** BEFORE touching any code.
2. Implement the fix.
3. Write / update tests that drive the **production path**.
4. Run the relevant test project and confirm all tests pass.
5. Fix any failures before moving to the next task.

Do NOT ask for permission at any step. Do NOT stop early. Finish all three tasks, make all tests green, then write the report.

---

## Onboarding & Workflow

### Required Reading (in order)
1. **Task details:** `.dev/other-fixes-2/TASK-DETAIL.md` -- sections FIX2-010, FIX2-011, FIX2-012
2. **Source findings BPF-010, BPF-022, BPF-025:** `.dev/blueprint-fixes-1/TASK-DETAIL.md`
3. **HSM Debug design doc:** search for `Blueprint_Subsystem_Debug_Protocol_Detailed_Design.md` for HSM snapshot section
4. **HSM data structures:** search for `HsmInstance64`, `HsmInstance128`, `MachineMetadata` in the blueprints/HSM source

### Source Code Areas
- **HSM debug session:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Hsm/HsmDebugSession.cs`
- **HSM asset:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Assets/Hsm/HsmAsset.cs`
- **HSM asset projector:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Assets/Hsm/HsmAssetProjector.cs`
- **HSM fluent emitter:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Hsm/HsmFluentEmitter.cs`
- **HSM emitter tests:** search for `HsmFluentEmitterTests.cs`
- **Test project:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/`

### Build & Test
```
cd d:\WORK\IOS-IG-SimHost-FDP
dotnet build Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Hrot.Blueprints.Tests.csproj --nologo -v q
dotnet test Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Hrot.Blueprints.Tests.csproj --filter "FullyQualifiedName!~AllocationFree" --nologo
```

### Report Submission
Submit report to: `.dev/other-fixes-2/reports/BATCH-05-REPORT.md`

---

## Context

All three tasks are in the HSM (Hierarchical State Machine) area:

- **FIX2-010:** `HsmDebugSession` decodes `ActiveLeafStableIds` correctly, but `EventQueue`, `TimerSlots`, and `HistorySlots` are still `Array.Empty<>`. The raw data is in `HsmInstance64` / `HsmInstance128`.
- **FIX2-011:** `HsmAssetProjector` never populates `StateNode.DeferredEventIds`. The kernel `StateDef` has no deferred-event field. So save+reload drops all deferred events.
- **FIX2-012:** The projector's transitions and regions sections still sort layout Guid keys positionally, causing mapping errors after structural edits. `metadata.TransitionVisualIds` already exists and is populated but never consulted.

---

## Tasks

### Task 1 -- FIX2-010: Decode HSM EventQueue, TimerSlots, HistorySlots

**Full details:** `.dev/other-fixes-2/TASK-DETAIL.md#fix2-010`

**Success condition (define before coding):**
A test creates an `HsmInstance64` (or `HsmInstance128`) with known values in `TimerDeadlines`, `HistorySlots`, `EventCount`, and `EventBuffer`, calls the snapshot decode method, and asserts the decoded `EventQueue`, `TimerSlots`, and `HistorySlots` in the returned snapshot match the source data. Currently these fields are `Array.Empty<>` -- if the decode helpers are absent the test fails.

**What to fix:**
- Add decode helpers for `EventQueue`, `TimerSlots`, and `HistorySlots` to `HsmDebugSession` (mirroring `DecodeLeaves64/128`).
- Wire them into the snapshot capture path so all three fields are populated when `GetCurrentStateSnapshot()` is called.

**Test required:**
- Test name: `HsmSnapshot_DecodeEventQueueTimerSlotsHistorySlots_FromHsmInstance` (or similar)
- Must: construct an `HsmInstance64` with non-zero event count, timer deadlines, and history slots; call the decode; assert all three decoded collections are non-empty and have correct values.
- Must NOT: pre-populate the snapshot fields directly.

---

### Task 2 -- FIX2-011: Fix HSM deferred events round-trip

**Full details:** `.dev/other-fixes-2/TASK-DETAIL.md#fix2-011`

**Success condition (define before coding):**
A test builds an HSM asset with a state that has `DeferredEventIds`, saves it (serializes), reloads it (deserializes via the projector), and asserts `StateNode.DeferredEventIds` is non-empty in the reloaded asset. The vacuous existing test that pre-populates `DeferredEventIds` directly must be replaced with a real round-trip test.

**What to fix:**
1. Add a deferred-event field to `StateDef` (the kernel blob-side struct) and `MachineMetadata` (per-state mapping).
2. In the flattener/emitter, when building the `StateDef`, populate the deferred-event field.
3. In `HsmAssetProjector`, read the deferred-event field from the blob and populate `StateNode.DeferredEventIds`.
4. Replace or update the vacuous test in `HsmFluentEmitterTests.cs` (around line 170-171 per task detail) with a real blob->projector->emit round-trip test.

**Test required:**
- Test name: `HsmDeferredEvents_RoundTrip_BlobToProjectorToEmit` (or similar)
- Must: build an HSM with a deferred event on a state, compile (which produces the blob), load via the projector, and assert `StateNode.DeferredEventIds` contains the expected event IDs.
- Must NOT: set `DeferredEventIds` directly on the state node without going through the projector.

---

### Task 3 -- FIX2-012: Fix HSM projector transition & region visual ID resolution

**Full details:** `.dev/other-fixes-2/TASK-DETAIL.md#fix2-012`

**Success condition (define before coding):**
A test inserts a transition into an HSM, removes an earlier transition (shifting indices), reloads via the projector, and asserts the surviving transition has the correct `VisualId` (from `metadata.TransitionVisualIds`). With positional sorting the test would return the wrong GUID after the shift.

**What to fix:**
- In `HsmAssetProjector.cs`, the transitions section (around lines 145-158 per task detail) still sorts by layout Guid key index and assigns positionally.
- Replace the positional fallback with a lookup via `metadata.TransitionVisualIds[index]` (keyed by flat transition index) -- exactly as the states section was fixed.
- Apply the same fix to the regions section (around lines 181-192).

**Test required:**
- Test name: `HsmProjector_TransitionVisualId_StableAfterDeletion` (or similar)
- Must: build an HSM with 2 transitions and explicit visual IDs, delete transition 0 (shifts index), reload via projector, and assert transition 1 still has its original visual ID.
- Must NOT: use sorted/positional key assignment.

---

## Quality Standards

**PRODUCTION PATH:** All tests must go through the compile/save/load/projector pipeline. Direct field assignment does NOT count.

**ALL EXISTING TESTS (886) MUST STAY GREEN.**

---

## Developer Insights (Report Questions)

1. What obstacles did you hit adding the deferred-event field to `StateDef`? How did you handle backward-compat concerns?
2. What data structures are `TransitionVisualIds` keyed by? How did you verify the key is the flat transition index vs. some other identifier?
3. Did the vacuous `HsmFluentEmitterTests` deferred-event test exist and did you replace or augment it?
4. Any edge cases discovered (e.g., HSM with no transitions, HSM with no deferred events)?
5. **Suggested commit message** for this batch.
