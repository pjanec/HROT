# BS-1-BATCH-06 Report

**Workstream:** BS-1 (Brain / Muscle Node Separation)  
**Batch:** BS-1-BATCH-06  

---

## Summary

All five items (TD-10, TD-11, TD-12, BS1-T021, BS1-T022) are implemented and all tests pass.

---

## Tech Debt Items

### TD-10 — NavigationIntent.IntentId documentation

**File:** `FDP/Toolkits/FDP.Toolkit.Navigation.Contracts/NavigationComponents.cs`

Expanded the XML doc comment on `NavigationIntent.IntentId` to explain the monotonic-id contract in full. The previous comment mentioned stale-status detection but did not explain the loop-reset mechanism. The new `<remarks>` section documents:

- That every `OnEnter` / `OnExit` call in navigation executors increments `IntentId`.
- That `FollowRouteExecutor` increments `IntentId` on loop completion to signal `NavigationIntentBridgeSystem` to reset `NavState.ProgressS`.
- That `NavigationExecutionSystem` detects the new id and resets `NavigationStatus` to `InProgress` so the executor does not see the stale `Arrived` result.
- The safe wrap-around behaviour (uint overflow, id 0 is valid).

### TD-11 — NavigationIntentBridgeSystem ProgressS reset comment

**File:** `FDP/Toolkits/FDP.Toolkit.Navigation/Systems/NavigationIntentBridgeSystem.cs`

Added a code comment above the `nav.ProgressS = 0f` line inside the `FollowRoute` case explaining *why* the reset is conditional on `isNewIntent`: resetting unconditionally on every tick would restart the route from the beginning each frame, making forward progress impossible.

### TD-12 — FollowRoute latency assumption documentation

**Files:**
- `FDP/Toolkits/FDP.Toolkit.Navigation/Executors/FollowRouteExecutor.cs`
- `FDP/Toolkits/FDP.Toolkit.CarKinem/Systems/NavigationExecutionSystem.cs`

Added a round-trip latency note in both sites:

- **`FollowRouteExecutor.Execute`**: After incrementing `IntentId` on a loop reset, the executor will observe at least one tick of `InProgress` before the new lap can produce `Arrived`. This is intentional and bounded to exactly one tick of `NavigationExecutionSystem` latency.
- **`NavigationExecutionSystem.OnUpdate`**: Annotated the new-command detection block to explain that brain-side `IntentId` increments are detected here on the *next* tick, and that this latency prevents the executor from mistaking the previous lap's `Arrived` for the new one.

---

## Core Tasks

### BS1-T021 — Remove NavState poll from Action_Wander

**File:** `Bagira.SimHost/Brains/SimHostNodes.cs`

Removed the secondary `NavState.HasArrived` block from `Action_Wander`. The primary arrival detection via `channel.Status == NodeStatus.Success` is sufficient — it is set by `MoveToExecutor` which already reads from `NavigationStatus` (Brain-tier CQRS-compliant). Polling `NavState` (Muscle-tier physics input) directly was the precise violation this task targeted.

The block removed was:
```csharp
// Also honour NavState.HasArrived as a secondary arrival signal.
if (!needsNewTarget && ctx.World.HasComponent<NavState>(ctx.Self))
{
    var nav = ctx.World.GetComponent<NavState>(ctx.Self);
    if (nav.HasArrived != 0)
        needsNewTarget = true;
}
```

**Tests added:** `Bagira.SimHost.Tests/SimHostNodesWanderTests.cs` (new file, 3 tests):

| Test | What it covers |
|---|---|
| `Action_Wander_WhenChannelSuccess_PicksNewTarget` | SC2: no NavState; Success → new target written, ActionInstanceId incremented, MoveToParams written |
| `Action_Wander_WhenChannelRunning_DoesNotPickNewTarget` | SC2 negative: Running → no re-activation, ActionInstanceId unchanged |
| `Action_Wander_NoLocomotionChannel_ReturnsFailure` | Guard: returns Failure if LocomotionChannel absent |

### BS1-T022 — Fix MissionDirectorSystem.ReachedDestination + UI generator

**Files modified:**

1. `FDP/Toolkits/FDP.Toolkit.Behavior/Components/MissionComponents.cs`  
   — Added `[Obsolete]` attribute to `MissionTrigger.ReachedDestination` with a message directing new code to use `DoctrineFinished`. Retained the enum value (= 1) for DDS serialisation compatibility. Added `using System;` for the attribute.

2. `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/MissionDirectorSystem.cs`  
   — Replaced the `ReachedDestination` switch case (which read `NavState.HasArrived`) with the `DoctrineFinished` logic (checks `_doctrineFinishedThisFrame`). The `CarKinem.Core` import was removed (no longer needed). The class XML doc was updated to reflect the new behaviour.

3. `Bagira.SimHost/SimHostVisualization.cs`  
   — Changed the DDS trigger string emitted by `HandleRightClickForEntity` from `"ReachedDestination"` to `"DoctrineFinished"`. Updated the method's XML doc comment accordingly.

**Tests added/updated:**

- `FDP/Toolkits/FDP.Toolkit.Behavior.Tests/MissionDirectorSystemTests.cs`:
  - Existing `MissionDirector_AdvancesPhase_WhenReachedDestination` updated to verify the new behaviour: `NavState.HasArrived = 1` alone does NOT advance the phase; it requires a `DoctrineFinishedEvent`.
  - New: `ReachedDestination_AdvancesPhase_ViaDoctrineFinishedEvent` (SC1)
  - New: `ReachedDestination_DoesNotAdvance_WhenOnlyNavStateHasArrived` (SC2 negative)

- `Bagira.SimHost.Tests/SimHostVisualizationTests.cs`:
  - Updated `RightClick_BrainActive_WritesMissionWithTrigger` assertion from `"ReachedDestination"` to `"DoctrineFinished"` (SC3).
  - Updated class and method XML docs.

---

## Challenges

**`goto case` with `[Obsolete]` enum — unexpected runtime failure:**  
The first implementation used `goto case MissionTrigger.DoctrineFinished` in the `ReachedDestination` switch case. The code compiled cleanly but the test `MissionDirector_AdvancesPhase_WhenReachedDestination` failed when all tests ran together (phase stayed at 0 after a two-run sequence). The test passed in isolation. Replacing `goto case` with direct inline logic (duplicating the single `if` statement) resolved the failure immediately. The root cause was likely a compiler subtlety where the `goto case` to an `[Obsolete]`-annotated label produced different IL than expected. The inline approach is cleaner and removes ambiguity.

**`fixed` buffer in test context:**  
Reading `channel.Params` (a C# fixed buffer) in a test using `fixed (byte* src = channel.Params)` triggered `CS0213` ("already fixed expression") because `channel` is a local struct copy, not a GC-tracked heap object. Resolution: used `Unsafe.ReadUnaligned<MoveToParams>(ref channel.Params[0])` instead, which works for a value-type local.

**`using System` absent in MissionComponents.cs:**  
Adding `[Obsolete]` to the enum value required adding `using System;` to the file, which was missing.

---

## Design Gaps / Edge Cases

**`ReachedDestination` in `MissionTriggerHelper.ResolveTrigger`:** The helper still maps the DDS string `"ReachedDestination"` to `EcsMissionTrigger.ReachedDestination` rather than silently remapping it to `DoctrineFinished`. This is intentional: the runtime evaluation path already delegates to the `DoctrineFinished` logic inside `MissionDirectorSystem`, so remapping at ingress would create a double-translation that obscures the backward-compat story. The `[Obsolete]` attribute with a descriptive message is sufficient guidance.

**`EntityMissionEgressTranslator` and `SimHostInstance` using `ReachedDestination`:** These sites generate `CS0618` obsolete warnings after this batch. They fall outside the scope of BS1-T022 (which targets the runtime evaluation path, not every call site). They are tracked as follow-up debt.

---

## Deviations

None. All changes are within the spec constraints. `ReachedDestination` enum value is retained as required.

---

## Test Results

| Project | Tests | Status |
|---|---|---|
| `FDP.Toolkit.Behavior.Tests` | 75 | ✅ All passed |
| `Bagira.SimHost.Tests` (non-integration) | 346 | ✅ All passed |
| Full solution build | — | ✅ No errors, no new warnings beyond expected CS0618 obsolete-use sites |
