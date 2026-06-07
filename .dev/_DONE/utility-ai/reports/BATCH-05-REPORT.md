# BATCH-05 Completion Report

**Batch:** BATCH-05 — Fluent Authoring Layer, ThreatMatrixAssignmentSystem, and Starter-Pack
**Status:** COMPLETE (all 12 integration tests pass; build clean)
**Date:** 2025

---

## Summary

All tasks completed. 12 new integration tests pass (12/12). All previously passing Utility AI tests
continue to pass. Build: 0 errors, 0 warnings.

---

## Tasks Completed

### D-04 — Namespace normalisation in existing test files

**Files modified:**
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityScorerTests.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityResultBufferTests.cs`

Changed namespace declaration from `Fdp.Toolkit.Utility.Tests` to `Fdp.Toolkit.Tests.Utility`
to match all other utility test files in the project.

---

### D-05 — OptionId guard in UtilityCore.cs

**File:** `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityCore.cs`

Added `Debug.Assert(option.OptionId <= byte.MaxValue, ...)` inside
`UtilityDecisionDef.ValidateOptions()` (called from `UtilityDecisionBuilder.Build()`).
Phase 1 options all fit in [0,255]; this assert catches future truncation silently.

---

### D-06 — Close: UtilityInputRegistrar dictionary (tracking note)

No code change required. D-06 is a Phase 2 source-gen task; closing as "deferred to Phase 2" in
the debt tracker. The `Dictionary<ushort, nint>` approach is acceptable for the current usage
pattern (registered once at startup, read many times per frame at Phase 1 scale).

---

### Task 1 — UtilityDecisionBuilderInfra (new file)

**File:** `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityDecisionBuilderInfra.cs`

Defined the fluent authoring infrastructure:
- `IUtilityDecisionDefinition` — marker interface; decision def classes implement this
- `IUtilityDecisionBuilder` — fluent root (`.Option(...)`)
- `IUtilityOptionBuilder` — fluent option scope (`.Consider(...)`)
- `In` — static factory for `InputParams` (thin wrappers: `HealthFraction()`, `AmmoFraction()`,
  `EnemyStrengthRatio()`, `HaveLiveTarget()`, `EqsTopScore(name)`, `AllyAdvancingNearby()`,
  `Constant(v)`, `DistanceToContext()`, `EffectiveRangeFraction()`, `AmmoFractionMount()`)
- `Curve` — static factory for `ResponseCurve` (one constant per `CurveKind`)
- `UtilityDecisionAttribute` — `[AttributeUsage(AttributeTargets.Class)]` with `assetId`,
  `displayName`, `kind`, `category`, `hysteresisBonus`
- `UtilityDecisionBuilder` — `Build(IUtilityDecisionDefinition def)` returns
  `UtilityDecisionDef`; `ComputeId(string guid)` returns deterministic int via FNV-32

---

### Task 2 — UtilityDecisionCatalog (new file)

**File:** `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityDecisionCatalog.cs`

Static registry: `Register(UtilityDecisionDef)`, `TryGet(int id, out UtilityDecisionDef)`,
`Clear()`. Thread-safe via `lock`. Populated by `UtilityDecisionBuilder.Build()` if the
caller passes `autoRegister: true` (default false for unit-test isolation).

---

### Task 3 — ThreatMatrixAssignmentSystem (new file)

**File:** `FDP/Toolkits/Fdp.Toolkits/Utility/Group/ThreatMatrixAssignmentSystem.cs`

Instance class (one per decision type). Constructor: `(int decisionId, int maxFocusFireCount)`.
Method: `Run(EntityRepository repo, Entity leader)`.

Algorithm:
1. Collects subordinate list from `UnitRoster` on `leader`.
2. Calls `UtilityScorer.Evaluate` for each member using `decisionId`.
3. Greedy assignment: iterates members in insertion order; for each member picks the highest-scoring
   unblocked candidate from `UtilityResultBuffer` (a candidate is blocked when its `FocusFireCount`
   has reached `maxFocusFireCount`).
4. Writes result to `ThreatMatrixAssignmentState` projected from the leader's `Blackboard1024`.

---

### Task 4 — UtilityScorer extensions

**File:** `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityScorer.cs`

Added:
- `Evaluate(EntityRepository, Entity agent, int decisionId)` — PostureSelect path; reads
  `UtilityDecisionDef` from catalog, evaluates all options, writes `UtilityResultBuffer`.
- `Evaluate(EntityRepository, Entity agent, int decisionId, Entity context)` — CandidateRank
  path; also evaluates with a context entity (for `WeaponSelection`).
- `EvaluateCandidates(...)` — enumerates candidates from `TargetMemory`, calls `Evaluate`
  per candidate, fills and sorts buffer.
- `SelectPosture(EntityRepository, Entity agent, int decisionId)` — reads prior `WinningPostureId`
  from buffer for hysteresis, calls `Evaluate`, applies `hysteresisBonus` to previous winner,
  returns winning `WinningPostureId`.

---

### Task 5 — StarterPack decisions and integration tests

**New production files:**
- `FDP/Toolkits/Fdp.Toolkits/Utility/StarterPack/Posture.cs` — `Posture : byte` enum:
  `AdvanceAndAttack=1`, `TakeCover=2`, `Suppress=3`, `Flee=4`, `Hold=5`
- `FDP/Toolkits/Fdp.Toolkits/Utility/StarterPack/CombatPostureDecision.cs` — 5 options,
  assetId `3c6f9e42-...posture0000001`, hysteresis 0.08
- `FDP/Toolkits/Fdp.Toolkits/Utility/StarterPack/ThreatRankingDecision.cs` — 4 considerations,
  kind `CandidateRank`
- `FDP/Toolkits/Fdp.Toolkits/Utility/StarterPack/WeaponSelectionDecision.cs` — 4 considerations,
  kind `CandidateRank`
- `FDP/Toolkits/Fdp.Toolkits/Utility/StarterPack/LeaderAssignmentDecision.cs` — 3 considerations,
  kind `CandidateRank`

**Modified test infrastructure:**
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityTestWorld.cs`:
  - Added `Scorer` property (`UtilityScorer` instance)
  - Added `Repo.RegisterComponent<UtilityResultBuffer>()` in constructor
  - `SpawnWeaponMount` now adds `Position` component to the agent
  - `AssignmentFor` fully implemented via `ThreatMatrixAssignmentState`

**New test file:**
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/StarterPackIntegrationTests.cs`
  12 tests, all passing:

| ID | Test | Result |
|----|------|--------|
| SC-SP-01 | `CombatPosture_FullHealth_WithVisualContact_SelectsAdvanceAndAttack` | PASS |
| SC-SP-02 | `CombatPosture_NoContacts_SelectsHold` | PASS |
| SC-SP-03 | `CombatPosture_EmptyAmmo_WithVisualContact_SelectsHold` | PASS |
| SC-SP-04 | `CombatPosture_Hysteresis_SmallHealthDrop_DoesNotFlipPosture` | PASS |
| SC-SP-05 | `ThreatRanking_VisualContact_ReturnsPositiveScore` | PASS |
| SC-SP-06 | `ThreatRanking_AcousticContactOnly_ScoresZero` | PASS |
| SC-SP-07 | `ThreatRanking_CloserContactRanksFirst` | PASS |
| SC-SP-08 | `WeaponSelection_MountAtEffectiveRange_RanksFirst` | PASS |
| SC-SP-09 | `WeaponSelection_EmptyMount_ScoresZero` | PASS |
| SC-SP-10 | `Assignment_SingleMember_VisualContact_GetsAssigned` | PASS |
| SC-SP-11 | `Assignment_SingleMember_AcousticOnly_NotAssigned` | PASS |
| SC-SP-12 | `Assignment_FocusFireCap_ThirdMemberAssignedToSecondTarget` | PASS |

---

## Bug Fixed During Implementation

**`UtilityResultBuffer` missing `[ComponentId]` attribute**

`UtilityResultBuffer` was a plain `[StructLayout(LayoutKind.Sequential)]` struct with no
`[ComponentId]` decoration. When `UtilityTestWorld` called `Repo.RegisterComponent<UtilityResultBuffer>()`,
the ECS threw `InvalidOperationException` at test startup.

Fix:
1. Added `public const int UtilityResultBuffer = 151;` to `UtilityApplicationComponentIds.cs`
   (next free slot in the 140-159 ModuleHost network block; 149 and 150 were already taken by
   `UtilityDebugFlags` and `UtilityTraceWorkingMemory`).
2. Added `[ComponentId(UtilityApplicationComponentIds.UtilityResultBuffer)]` and
   `[DataPolicy(DataPolicy.NoSave)]` to the `UtilityResultBuffer` struct declaration.

**Hysteresis test value correction (SC-SP-04)**

Initial health values for the hysteresis test (prime at 0.04, drop to 0.03) were wrong.
At health=0.04, `EnemyStrengthRatio = 0.5 / (0.04 * 16) = 0.781`, so `InverseLinear` yields 0.219
and `AdvanceAndAttack` already loses to `Hold` at that health level.

Corrected values: prime at health=0.08 (AA score ~0.191, Hold ~0.172 — AA wins clearly), then
drop to health=0.07 (AA ~0.163, Hold ~0.170 — Hold would win without hysteresis; with +0.08
applied to AA: 0.243 > 0.170 — AA retained).

---

## Decisions Made

- **`EqsTopScore` stub returns 0** when no matching EQS sensor is registered. This causes
  `TakeCover` and `Flee` options to always score 0 in tests that do not set up EQS sensors.
  Tests exploit this to reduce option count and simplify score expectations.
- **`AllyAdvancingNearby` stub returns 0** (Phase 2 placeholder). `Suppress` option always
  scores 0. Tests that rely on `Hold` as fallback benefit from this.
- **`UtilityDecisionCatalog` is populated via `Build()` + `autoRegister: true`** in each
  `[UtilityDecision]`-decorated class's static initializer. Test setup calls
  `StandardInputs.RegisterAll()` which triggers static initialization of all four decisions.
- **Hysteresis is applied only in `SelectPosture`**, not in `EvaluateCandidates`. For
  candidate-rank decisions (`ThreatRanking`, `WeaponSelection`, `LeaderAssignment`) the
  buffer is always re-ranked without a stability bonus.

---

## Files Changed Summary

| File | Change |
|------|--------|
| `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityApplicationComponentIds.cs` | Added `UtilityResultBuffer = 151` |
| `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityResultBuffer.cs` | Added `[ComponentId]` + `[DataPolicy(NoSave)]` |
| `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityDecisionBuilderInfra.cs` | NEW |
| `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityDecisionCatalog.cs` | NEW |
| `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityCore.cs` | `Debug.Assert` for D-05 |
| `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityScorer.cs` | `Evaluate`, `SelectPosture`, `EvaluateCandidates` |
| `FDP/Toolkits/Fdp.Toolkits/Utility/Group/ThreatMatrixAssignmentSystem.cs` | NEW |
| `FDP/Toolkits/Fdp.Toolkits/Utility/StarterPack/Posture.cs` | NEW |
| `FDP/Toolkits/Fdp.Toolkits/Utility/StarterPack/CombatPostureDecision.cs` | NEW |
| `FDP/Toolkits/Fdp.Toolkits/Utility/StarterPack/ThreatRankingDecision.cs` | NEW |
| `FDP/Toolkits/Fdp.Toolkits/Utility/StarterPack/WeaponSelectionDecision.cs` | NEW |
| `FDP/Toolkits/Fdp.Toolkits/Utility/StarterPack/LeaderAssignmentDecision.cs` | NEW |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityTestWorld.cs` | `Scorer`, `UtilityResultBuffer` registration, `SpawnWeaponMount` Position, `AssignmentFor` |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/StarterPackIntegrationTests.cs` | NEW — 12 tests |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityScorerTests.cs` | Namespace fix (D-04) |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityResultBufferTests.cs` | Namespace fix (D-04) |
