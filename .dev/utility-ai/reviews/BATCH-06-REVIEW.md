# BATCH-06 Review

**Batch:** BATCH-06 — Corrective (BATCH-05 fixes) + TASK-UAI-P1-09 Integration Nodes  
**Verdict:** APPROVED WITH DEV-LEAD FIXES  
**Reviewer:** Dev Lead  
**Date:** 2025

---

## Overall Assessment

The production code delivered in BATCH-06 is correct. `UtilitySelectorNode`, `UtilityTransitionArbiter`,
and the partial Blueprint 1-C changes (`Nodes.cs`, `IrOperation.cs`) are solid and build cleanly.
The corrective helper methods in `UtilityTestWorld.cs` (SetHealth, SetEnemyStrengthRatio, SeedContact,
SpawnTarget, SpawnLeader, SpawnSquadMember, AssignmentFor) are all well-formed.

However, the batch was delivered with **3 out of 18 tests failing** and two production defects that
caused those failures. These defects were identified and corrected by the Dev Lead in post-delivery
review. All 18 StarterPack tests now pass. The section below documents each defect and its fix.

**Final test count:** 18/18 StarterPack + 2/2 SelectorNode + 2/2 TransitionArbiter = **22/22 passing**.

Blueprint 1-C remains partially complete (Steps 4-7 pending); this will carry into BATCH-07.

---

## Defects Found and Fixed by Dev Lead

### Defect 1 — `IsAssignedTarget` returned 0f for neutral cases

**File:** `FDP/Toolkits/Fdp.Toolkits/Utility/Inputs/StandardInputs.cs`  
**Severity:** P1 — caused `Assigned_Target_Bias_Promotes_Leader_Choice` and indirectly `Wounded_Member`

**Problem:** `IsAssignedTarget` returned `0f` in every case where there was no explicit assignment:
no `UnitSubordinate`, no commander Blackboard/Roster, member not in roster, and when
`assignedHandle == 0L` (no assignment set). A zero output in `WeightedProduct` collapses the
entire option score to zero regardless of all other considerations.

The design intent (Utility_AI_StarterPack_Examples_v1_1.md §10) is that `IsAssignedTarget` should
be **neutral (1.0)** when there is no assignment — it should only gate when an explicit non-zero
assignment exists and does NOT match the candidate. Only in that case should it return `0f`.

**Fix:**
```csharp
// All "no assignment" cases: return neutral pass
if (!repo.HasComponent<UnitSubordinate>(ctx.Self)) return 1f;
// ... check commander has Blackboard/Roster ...
int idx = UnitRoster.IndexOf(ref roster, (long)ctx.Self.PackedValue);
if (idx < 0) return 1f;
// ... get state ...
long assignedHandle = state.GetAssignedTarget(idx);
if (assignedHandle == 0L) return 1f;   // ← was 0f; must be 1f
float result = assignedHandle == (long)ctx.Context.PackedValue ? 1f : 0f;
```

---

### Defect 2 — `ThreatRankingDecision` missing `IsAssignedTarget` consideration

**File:** `FDP/Toolkits/Fdp.Toolkits/Utility/StarterPack/ThreatRankingDecision.cs`  
**Severity:** P1 — `Assigned_Target_Bias_Promotes_Leader_Choice` could not pass without this

**Problem:** The batch added the `IsAssignedTarget` input to `StandardInputs.cs` and the test
assumed it was wired into `ThreatRankingDecision` — but the consideration was never added to
the decision's `CandidateOption` builder. The decision only had 4 considerations.

**Fix:** Added 5th consideration:
```csharp
.Consider(In.IsAssignedTarget(), 0.9f, Curve.Threshold)
```

This wires the assignment signal into the scoring. With weight 0.9 and `Curve.Threshold`,
a non-assigned target (IsAssignedTarget=0) collapses its score to zero, while the assigned
target passes through with the full 5-consideration score.

---

### Defect 3 — `SpawnTarget()` placed at `Vector3.Zero`, collapsing distance-based scores

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityTestWorld.cs`  
**Severity:** P1 — caused `Wounded_Member_Vetoes_Assignment_And_Breaks_Off` to fail

**Problem:** `SpawnTarget()` placed entities at `(0, 0, 0)`. The squad member (`self`) is also at
`(0, 0, 0)`. `LeaderAssignmentDecision` uses `Curve.InverseLinear` on `DistanceToContext`. The
reader for `DistanceToContext` returns `1 - d/maxRange`; at distance zero this is `1.0`. Then
`InverseLinear(1.0) = 1 - 1.0 = 0.0` — the score collapses to zero. With score zero,
`ThreatMatrixAssignmentSystem` assigns nothing, so the Wounded test asserted a non-zero
assignment and got 0.

**Fix:**
```csharp
Repo.AddComponent(t, new Position { Value = new Vector3(100f, 0f, 0f) });
// Position at (100, 0, 0) so that distance-based scoring does not collapse
```

At d=100m with maxRange=1000m: reader = 0.9, `InverseLinear(0.9) = 0.1` — non-zero, assignment
proceeds correctly.

---

### Defect 4 — `Trace_Records_PerConsideration_Breakdown_For_Winner` used values that caused wrong branch to win

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/StarterPackIntegrationTests.cs`  
**Severity:** P1 — test failed with "EqsTopScore consideration for TakeCover branch should be > 0.5"

**Problem:** The test was set up with `health01: 1.0f` and `SetEnemyStrengthRatio(self, 0.5f)`.
With these values `AdvanceAndAttack` won (not `TakeCover`), so the trace captured
`AdvanceAndAttack`'s winner branch which does NOT include the `EqsTopScore` consideration
(that consideration is only in `TakeCover`). The assertion on `EqsTopScore > 0.5` then failed
because the trace had no such entry.

**Fix:** Changed to `health01: 0.35f` and `SetEnemyStrengthRatio(self, 1.3f)`. With these values
`TakeCover` wins (higher health loss + more enemies). The trace correctly records `EqsTopScore`
for the `TakeCover` branch and `Linear(0.85f) = 0.85 > 0.5`. ✓

---

### Defect 5 — `Assigned_Target_Bias` test used manual Blackboard writes that hit the ECS `[InlineArray]` stale-ref trap

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/StarterPackIntegrationTests.cs`  
**Severity:** P1 — test wrote the assignment but reads always returned stale data

**Problem:** The test attempted to write directly to `ThreatMatrixAssignmentState` via a manual
ref chain:
```csharp
ref var bb    = ref _world.Repo.GetComponentRW<Blackboard1024>(leader);
ref var state = ref ThreatMatrixAssignmentState.Project(ref bb);
ref var roster = ref _world.Repo.GetComponentRW<UnitRoster>(leader);
// ...
state.SetAssignment(slot, b.PackedValue);
```

The ECS `EntityRepository.GetComponentRW` documentation explicitly warns:
> **⚠ DANGER — `[InlineArray]` Mutation Trap (C# 12)**  
> Components that contain an `[InlineArray]` field are susceptible to a JIT defensive-copy bug.
> The mutation hits the temporary, not the ECS chunk. The write is **silently lost**.

`ThreatMatrixAssignmentState.Slots` is an `[InlineArray(16)]` type. Calling
`GetComponentRW<UnitRoster>` after obtaining `ref var bb` while `state` is aliased to `bb`
triggers the stale-ref condition. The write did not persist.

**Fix:** Redesigned the test to use `ThreatMatrixAssignmentSystem.Run` — the proven write path —
instead of manual ref-chain writes. Added `SeedContact(leader, b, ...)` so the system's
`LeaderAssignmentDecision` has only one candidate and assigns `b` to `self`. Also changed
`contactHealth01` from `1f` to `0.5f` to ensure `ContactHealthFraction` consideration is
non-zero (with health=1.0, `Curve.InverseLinear(1.0) = 0` would collapse all scores).

---

## Test Quality Assessment

### What was done well

- All six posture/EQS tests are correctly structured and exercise real code paths.
- `UtilitySelectorNode` and `UtilityTransitionArbiter` tests are clean and non-trivial.
- Helper methods in `UtilityTestWorld` are well-abstracted and reusable.

### Issues with the original submission

1. **Trace test** (Defect 4): The test setup was not validated against which branch wins with
   those parameter values. A trace test MUST ensure the winning branch contains the consideration
   under scrutiny before asserting its value. Health=1.0 naturally selects AdvanceAndAttack.

2. **Assignment bias test** (Defects 1, 2, 5): Three separate defects converged here:
   - `IsAssignedTarget` had wrong neutral return value
   - `ThreatRankingDecision` was missing the 5th consideration entirely
   - The test's write mechanism hit the ECS stale-ref trap

   The test should have been designed to use `ThreatMatrixAssignmentSystem.Run` from the start,
   matching the pattern already established by the `Wounded_Member` and `Assignment_FocusFireCap`
   tests rather than reinventing a write path.

3. **Wounded veto test** (Defect 3): `SpawnTarget` at `(0,0,0)` was a silent failure —
   `ThreatMatrixAssignmentSystem` returned without assigning (score=0), the test then asserted
   a non-zero assignment value. The defect would have been caught by checking the return value
   of `AssignmentFor` before the posture assert.

---

## Status of Success Conditions

| Condition | Description | Status |
|-----------|-------------|--------|
| SC-P1-07-3 | Wounded member veto test passes | ✅ DONE |
| SC-P1-08-2 | Trace test with per-consideration breakdown | ✅ DONE |
| SC-P1-08-4 | EQS-driven posture tests (3 tests) | ✅ DONE |
| SC-P1-06-5 | Assignment bias promotes leader choice | ✅ DONE |
| SC-P1-09-1 | UtilitySelectorNode + 2 tests | ✅ DONE |
| SC-P1-09-2 | UtilityTransitionArbiter + 2 tests | ✅ DONE |
| SC-P1-09-3 | Blueprint compiler integration (partial) | ⚠️ PARTIAL — Steps 1-3 of 7 |
| SC-P1-09-4 | Blueprint runtime tests | ❌ NOT DONE |

---

## Remaining Work for BATCH-07

Blueprint 1-C completion (SC-P1-09-3 / SC-P1-09-4):
- `Stage5_Schedule.cs`: `ScoreDecisionNode` case emits `IrOp_ScoreDecision`; `ReadRankedResultNode` case in `ResolveNodeOutput` emits `IrOp_ReadRankedResult`
- `StatementEmitter.cs`: `case IrOp_ScoreDecision op` and `case IrOp_ReadRankedResult op`
- `InstanceEmitter.cs`: collectors + emitters wired in `Emit(IrAsset)`
- `UtilityNodeRuntimeTests.cs`: build asset programmatically, compile, register, tick, assert

---

## Verdict: APPROVED WITH DEV-LEAD FIXES

The production additions are correct and the tests now all pass. The five defects above have
been corrected by the Dev Lead. P1-07 and P1-08 are now DONE. P1-09 is 4/7 complete and
continues in BATCH-07.
