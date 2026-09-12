# BATCH-HS-01-REPORT — Command sink: create state (+ asset state-registration API)

**Date:** 2026-06-12 | **Branch:** `blueprint-integ-1` | **Task:** TASK-HS-01

## Summary

Added a state/transition registration API to `HsmAsset`, implemented `HsmCommandSink.ApplyAddNode` with the kind→flags mapping, and wrote 8 headless tests. Build 0 errors, 0 warnings; full test run 390 passed, 0 failed.

---

## Part 1 — API added to `HsmAsset.cs`

### Backing-list fields (captured in ctor)

```csharp
private readonly List<StateNode>      _allStatesList;
private readonly List<TransitionNode> _allTransitionsList;
```

Assigned in the ctor alongside the existing `_allRegionsList` / `_allGlobalTransitionsList`. Because `AllStates`/`AllTransitions` already wrap these exact lists via `.AsReadOnly()`, mutating the backing lists updates the public views automatically.

### Mutators

| Method | Signature | What it updates |
|---|---|---|
| `RegisterState` | `(StateNode state, StateNode parent)` | `parent.Children`, `state.Parent`, `state.FlatIndex` (next-free), `_allStatesList`, `_stableIdToState`, `_flatIndexToState` |
| `UnregisterState` | `(StateNode state)` | `state.Parent?.Children`, `_allStatesList`, `_stableIdToState`, `_flatIndexToState` |
| `RegisterTransition` | `(TransitionNode t)` | `t.FlatIndex` (next-free), `t.Source.OutgoingTransitions`, `_allTransitionsList`, `_visualIdToTransition`, `_flatIndexToTransition` |
| `UnregisterTransition` | `(Guid visualId)` | `t.Source?.OutgoingTransitions`, `_allTransitionsList`, `_visualIdToTransition`, `_flatIndexToTransition` |

### Private helpers

- `NextFreeStateFlatIndex()` → `(ushort)(max(existing keys) + 1)`, starting at 1 if map empty.
- `NextFreeTransitionFlatIndex()` → same pattern on `_flatIndexToTransition`.

**Not wired in this batch:** `RegisterTransition`/`UnregisterTransition` — the methods are present but not called from any sink handler yet (that is HS-03/04).

---

## Part 2 — `HsmCommandSink.ApplyAddNode` implementation

Replaced the `{ /* TODO */ }` body. Mapping from `cmd.Kind.Id` (a `NodeKindKey` string):

| Kind ID | Flags set | Default name |
|---|---|---|
| `HsmKinds.Simple` | (none) | `"State"` |
| `HsmKinds.Composite` | (none — implicit via `Children.Count > 0`) | `"State"` |
| `HsmKinds.Parallel` | `IsParallel = true` | `"Parallel"` |
| `HsmKinds.Final` | `IsFinal = true` | `"Final"` |
| `HsmKinds.History` | `IsHistory = true` | `"History"` |
| `HsmKinds.DeepHistory` | `IsDeepHistory = true` | `"DeepHistory"` |
| Unknown / any other | (none) | `"State"` |

The created state is registered via `_asset.RegisterState(state, _asset.RootState)`. `StableId` is set to `cmd.AssignedId.Value`, `Position` to `cmd.Position`. No explicit promote-to-composite code — that is automatic via `StateNode.Kind`/`IsContainer` + the existing reparent handler (per D-HS-01).

---

## Part 3 — Tests (`HsmCommandSinkCreateStateTests.cs`)

New file: `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Host/HsmCommandSinkCreateStateTests.cs`

Helper `BuildTestAsset()` creates a minimal empty asset (root only, no pre-existing states).

| # | Test name | What it asserts |
|---|---|---|
| 1 | `AddNode_Simple_creates_state_under_root` | `AllStates.Count == 1`; `FindStateByStableId` resolves; `Parent == RootState`; `Kind.Id == HsmKinds.Simple`; position persisted |
| 2 | `AddNode_Parallel_sets_IsParallel_flag` | `IsParallel == true`; all other flags false; `Kind.Id == HsmKinds.Parallel` |
| 3 | `AddNode_Final_sets_IsFinal_flag` | `IsFinal == true`; other flags false |
| 4 | `AddNode_History_sets_IsHistory_flag` | `IsHistory == true`; `IsDeepHistory == false`; `Kind.Id == HsmKinds.History` |
| 5 | `AddNode_DeepHistory_sets_IsDeepHistory_flag` | `IsDeepHistory == true`; `IsHistory == false`; `Kind.Id == HsmKinds.DeepHistory` |
| 6 | `Reparenting_child_under_simple_state_promotes_to_composite` | Create S1+S2, reparent S2 under S1 via `ChangeParent`; `S1.IsContainer == true`; `S1.Kind.Id == HsmKinds.Composite`; `S1.Children` contains S2 |
| 7 | `AddNode_two_states_have_unique_flat_indices` | Two states' `FlatIndex` values differ; both resolve via `FindStateByFlatIndex` |
| 8 | `AddNode_multiple_states_increments_AllStates_count` | After 3 adds of different kinds, `AllStates.Count` is 3 |

---

## Build & test results

| Metric | Before | After |
|---|---|---|
| Build errors | 0 | 0 |
| Build warnings | 0 | 0 |
| `HsmCommandSinkCreateState` tests | N/A (new file) | 8 passed, 0 failed |
| Full `Hrot.Hsm.Editor.Tests` suite | 382 passed* | 390 passed, 0 failed, 0 skipped |

\* Before count: 390 (total now) − 8 (new) = 382 pre-existing tests, all passing.

**Pre-existing failures: none.** The full suite runs clean at 390/0/0.

---

## Anything not done

Nothing — all objectives in the instructions are complete. `RegisterTransition`/`UnregisterTransition` are added but intentionally not called from any sink handler (per instructions: HS-03/04 territory).
