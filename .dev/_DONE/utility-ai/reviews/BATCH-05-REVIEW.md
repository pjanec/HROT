# BATCH-05 Review

**Batch:** BATCH-05 — Fluent Authoring Layer, ThreatMatrixAssignmentSystem, and Starter-Pack
**Verdict:** CHANGES REQUIRED
**Reviewer:** Dev Lead
**Date:** 2025

---

## Overall Assessment

The production code is well-implemented and correct. All four starter-pack decisions, the fluent
authoring infrastructure, `UtilityDecisionCatalog`, `ThreatMatrixAssignmentSystem`, and the
`UtilityScorer` instance API are all solid. The D-04/D-05/D-06/D-07 debt items are correctly
resolved.

However, the test suite has critical gaps. Three of the five explicitly-required success
conditions are not met because the corresponding tests were not written. The batch instructions
required **at least 15 new tests** and specified **11 exact test names** across 5 test classes;
only **12 tests** were delivered and 5 explicitly required tests are missing entirely.

---

## P1 Issues — CHANGES REQUIRED

### P1-1: Trace test missing (SC-P1-08-2 UNMET)

**Status: BLOCKING**

The `Trace_Records_Per_Consideration_Breakdown_For_Winner` test is completely absent from
`StarterPackIntegrationTests.cs`. This is a named success criterion in TASK-DETAIL.md:

> SC-P1-08-2: The trace test (§5) reads back per-consideration breakdown for the winning option
> through `UtilityTraceWorkingMemory1024`.

The batch instructions stated explicitly:
> Trace test (SC-P1-08-2) MUST assert `cover.RawValue` (or similar) and
> `cover.CurveOutput > 0.8f` — not just that a record exists.

The test infrastructure to support this test exists:
- `UtilityDebugFlags` and `UtilityTraceWorkingMemory1024` component types are registered
- `SpawnEqsSensor` helper is present in `UtilityTestWorld.cs`
- `UtilityScorer.Evaluate` wires the trace pointer when `TraceEnabled != 0`

However, `SpawnAgent` does NOT add `UtilityDebugFlags` or `UtilityTraceWorkingMemory1024` to
agent entities. These components must be added per-agent for the trace to activate.

**Fix required:** Add the trace test. Also add `UtilityDebugFlags { TraceEnabled = 1 }` and
`UtilityTraceWorkingMemory1024` to `SpawnAgent` so that trace capture is active by default in
tests (matching what was stated in the batch instructions under §5c point 3).

---

### P1-2: Wounded-member veto test missing (SC-P1-07-3 / SC-P1-08-4 UNMET)

**Status: BLOCKING**

The `Wounded_Member_Vetoes_Assignment_And_Breaks_Off` test is completely absent. This test is
explicitly required by two success conditions:

> SC-P1-07-3: A near-death member's posture decision (run separately) selects `Flee` despite an
> assignment — the veto path, asserted in the starter-pack
> `LeaderAssignmentTests.Wounded_Member_Vetoes_Assignment_And_Breaks_Off`.

> SC-P1-08-4: The wounded-member veto test passes (assignment present, posture selects Flee).

The batch instructions stated:
> Veto test (SC-P1-08-4) MUST assert `Posture.Flee` after the assignment is written.

This test exercises the authority model (§10.3 of the design): the leader proposes an assignment
via `ThreatMatrixAssignmentSystem.Run()`, but the member's autonomous posture decision (`Flee`
at near-death health) overrides the assignment. It is the only test that verifies the separation
between the assignment layer and the posture layer.

**Fix required:** Add `Assignment_WoundedMember_PostureFleesOverridesAssignment` (or
`Wounded_Member_Vetoes_Assignment_And_Breaks_Off`). The test should:
1. Spawn a leader and a near-death member (health01 ≈ 0.05, many enemies seeded).
2. Run `ThreatMatrixAssignmentSystem.Run()` — the member gets assigned.
3. Assert `AssignmentFor(leader, member) != 0` (assignment was written).
4. Run `_world.Scorer.SelectPosture(Repo, member, CombatPostureDecision.Id)`.
5. Assert result == `(byte)Posture.Flee`.

For step 5 to pass, `Flee` requires an EQS escape sensor (non-zero `EqsTopScore`). Use
`SpawnEqsSensor` to set up an escape route sensor on the near-death member.

---

### P1-3: Three EQS-based CombatPosture tests missing

**Status: BLOCKING (SC-P1-08-1 partially unmet)**

The batch instructions required 5 `CombatPostureTests`. Only 4 were delivered. Three were
skipped because `EqsTopScore` returns 0 for agents without EQS sensors. However
`SpawnEqsSensor` IS present in `UtilityTestWorld.cs` and provides the necessary scaffolding.

**Missing tests:**
- `Hurt_With_Cover_Available_Takes_Cover` — agent at health=0.3, EQS cover sensor at 0.85,
  assert posture == `TakeCover`
- `NearDeath_With_Escape_Flees` — agent at health=0.05, enemy strength high, EQS escape
  sensor at 0.85, assert posture == `Flee`
- `NearDeath_With_No_Escape_And_No_Cover_Does_Not_Flee_Into_Nothing` — same near-death
  conditions but NO EQS sensor, assert posture == `Hold` (not `Flee`)

These three tests are the only tests that exercise `TakeCover`, `Flee`, and the `Hold`
fallback-when-injured pathway in `CombatPostureDecision`. Without them, 3 of the 5 decision
branches (`TakeCover`, `Flee`, and the wounded `Hold`) are completely untested.

---

### P1-4: `Assigned_Target_Bias_Promotes_Leader_Choice` test missing

**Status: BLOCKING**

The batch instructions required 2 `ThreatRankingTests` including this one. Only
`Closer_Visible_HighThreat_Contact_Ranks_First` (covered by `ThreatRanking_CloserContactRanksFirst`)
was implemented. The assignment-bias test is absent.

This test exercises `In.IsAssignedTarget` — the only consideration in `LeaderAssignmentDecision`
that checks the current assignment state. It ensures the bias loop works:

1. Leader assigns member to target T1 via `ThreatMatrixAssignmentSystem.Run()`.
2. Member evaluates `ThreatRankingDecision` on its own contacts.
3. T1 ranks first because `IsAssignedTarget = 1` provides an extra weight boost vs. an
   otherwise equally-scored T2.

The `IsAssignedTarget` input IS tested elsewhere only as part of the assignment system, not as
a ranking bias in `ThreatRankingDecision`. This gap leaves a key cross-system integration path
untested.

---

### P1-5: Missing test helpers in UtilityTestWorld

**Status: NON-BLOCKING but required by batch spec**

The batch instructions (§5c) required the following helpers to be added to `UtilityTestWorld.cs`.
None were added:

- `SetHealth(Entity entity, float health01)` — batch instructions required this
- `SetEnemyStrengthRatio(Entity self, float ratio)` — batch instructions required this; needed
  for EQS-based tests
- `SpawnTarget()` — batch instructions required this
- `SeedSquadContacts(Entity leader, Entity[] targets)` — batch instructions required this

The hysteresis test SC-SP-04 manually sets `h.Current = 7f` instead of calling `SetHealth`.
This works but is fragile. The missing helpers should be added along with the P1 test fixes.

---

### P1-6: Test count shortfall

**Status: RELATED TO P1-1 through P1-4**

The batch instructions required **at least 15 new tests** in `StarterPackIntegrationTests.cs`.
12 tests were delivered. Adding the 5 missing tests (trace, veto, 3 EQS-posture) and 1 ranking
bias test would bring the count to 18, exceeding the minimum.

---

## P2 Issues (minor, log for DEBT-TRACKER if not fixed alongside P1 fixes)

### P2-1: Namespace inconsistency in new test file

`StarterPackIntegrationTests.cs` uses `Fdp.Toolkit.Tests.Utility`. The batch instructions
specified `Fdp.Toolkit.Tests` (no `.Utility`) for all new test files. While both compile, this
is inconsistent with the D-04 normalization applied to `UtilityScorerTests.cs` and
`UtilityResultBufferTests.cs`. Fix alongside the P1 test additions.

### P2-2: D-04 partially incomplete

The D-04 namespace fix changed two files from `Fdp.Toolkit.Utility.Tests` to `Fdp.Toolkit.Tests`
(correct per spec). However `StandardInputReaderTests.cs`, `CurveEvaluationTests.cs`,
`AggregatorTests.cs`, and `UtilityCoreTests.cs` still use `Fdp.Toolkit.Tests.Utility`. If D-04
was intended as a full normalization, these files should also be updated. However, since the
batch instructions only listed two specific files, this is a tracking issue — log in DEBT-TRACKER
as D-08 (scope of namespace normalization).

---

## Approved Items

### Production code — APPROVED

All production files are correct and complete:

| File | Assessment |
|------|------------|
| `UtilityDecisionBuilderInfra.cs` | Correct. `In`, `Curve`, `Ctx` factories are clean. `TryFindMountChild` self-mount case correctly handles `evalSelf = mount entity` for `WeaponSelection`. |
| `UtilityDecisionCatalog.cs` | Correct. Reflection scan matches spec. `Shared` lazily initialized and properly replaced on `RegisterAll`. |
| `ThreatMatrixAssignmentSystem.cs` | Correct. Greedy algorithm with stack-allocated focus-fire counters is clean. Clears state before each run. Focus-fire count written back per-slot. |
| `UtilityScorer.cs` (instance API) | Correct. Instance `Evaluate` / `SelectPosture` properly resolve def from registry and wire trace pointer. |
| `Posture.cs` | Correct. Values start at 1 (0 is uninitialized sentinel). |
| `CombatPostureDecision.cs` | Correct. Hysteresis = 0.08, 5 options, values match design. |
| `ThreatRankingDecision.cs` | Correct. 4 considerations, `CandidateOption`, LOS Step gate in place. |
| `WeaponSelectionDecision.cs` | Correct. `WeaponRangeBandFit` Bell + `WeaponEffectivenessVsTarget` Linear combination works correctly because `TryFindMountChild` short-circuits to self when the candidate IS the mount entity. |
| `LeaderAssignmentDecision.cs` | Correct for Phase 1. Phase 1 catalog approximations (`HasLineOfSight` + `WeaponEffectivenessVsTarget`) are reasonable substitutes documented in the report. |
| `UtilityApplicationComponentIds.cs` (D-07) | Correct. ID 151 is the next free slot. |
| `UtilityResultBuffer.cs` (D-07) | Correct. `[ComponentId]` + `[DataPolicy(NoSave)]` added. |
| `UtilityCore.cs` (D-05) | Correct. `Debug.Assert` in `ValidateOptions` / builder path. |

### Tests delivered — APPROVED

All 12 delivered tests are behaviorally meaningful:

| Test | Assessment |
|------|------------|
| SC-SP-01 (AdvanceAndAttack on full health) | Strong. Tests the main combat path. |
| SC-SP-02 (Hold on no contacts) | Strong. Tests HaveLiveTarget gate. |
| SC-SP-03 (Hold on empty ammo) | Strong. Tests AmmoFraction threshold gate. |
| SC-SP-04 (Hysteresis) | Strong. Two-step verify (prime then drop) + exact posture assertion. Hysteresis math in comments is correct. |
| SC-SP-05 (ThreatRanking positive score) | Weak assertion (`Score > 0f`) but passes the behavioral gate (LOS unblocks scoring). |
| SC-SP-06 (Acoustic only → score 0) | Strong. Tests LOS Step gate zero-path. |
| SC-SP-07 (Closer ranks first) | Strong. CandidateHandle assertion ties winner to specific entity. |
| SC-SP-08 (Mount at effective range ranks first) | Strong. Score ordering + winner handle both asserted. |
| SC-SP-09 (Empty mount scores 0) | Strong. Iterates buffer to find specific mount entry and checks exact 0. |
| SC-SP-10 (Single member assigned) | Strong. Integration: SeedContact + Run + AssignmentFor. |
| SC-SP-11 (Acoustic-only not assigned) | Strong. Tests the `bestScore > 0f` guard in assignment system. |
| SC-SP-12 (Focus-fire cap) | Strong. 3 members, 2 targets, cap=2 — exact handle assertions for all 3 members. |

---

## Summary of Required Fixes for Next Batch

The following corrective task must be the first task in the next batch before proceeding to P1-09:

**Corrective Task 0 (BATCH-06):**

1. Add to `SpawnAgent`: `UtilityDebugFlags { TraceEnabled = 1 }` and `UtilityTraceWorkingMemory1024`.
2. Add missing helpers to `UtilityTestWorld`: `SetHealth`, `SetEnemyStrengthRatio`, `SpawnTarget`, `SeedSquadContacts`.
3. Add `Trace_Records_Per_Consideration_Breakdown_For_Winner` test. Set up a `CombatPostureDecision` run on an agent with cover sensor (EQS score 0.85). Assert that `UtilityTraceWorkingMemory1024` has a record with `CurveOutput > 0.8f` for the cover consideration.
4. Add `Wounded_Member_Vetoes_Assignment_And_Breaks_Off` test. Near-death member (health=0.05) + EQS escape sensor. Assert assignment written, then `SelectPosture` returns `Flee`.
5. Add `Hurt_With_Cover_Available_Takes_Cover` — health=0.3, cover sensor 0.85 → `TakeCover`.
6. Add `NearDeath_With_Escape_Flees` — health=0.05, escape sensor 0.85 → `Flee`.
7. Add `NearDeath_With_No_Escape_And_No_Cover_Does_Not_Flee_Into_Nothing` — near-death, no EQS → `Hold`.
8. Add `Assigned_Target_Bias_Promotes_Leader_Choice` — run assignment, then member ranks assigned target first via `IsAssignedTarget` bias in threat ranking.
9. Fix namespace in `StarterPackIntegrationTests.cs` from `Fdp.Toolkit.Tests.Utility` to `Fdp.Toolkit.Tests`.
10. Add D-08 to DEBT-TRACKER for residual namespace inconsistency in other files.

---

## Commit Suggestion

Do NOT commit this batch until corrective tests pass. The commit message below is for after
corrective fixes:

```
feat(utility-ai): BATCH-05 fluent authoring, ThreatMatrixAssignmentSystem, starter-pack

- UtilityDecisionBuilderInfra.cs: [UtilityDecision] attribute, In/Curve/Ctx factories,
  IUtilityDecisionBuilder/IUtilityOptionBuilder, UtilityDecisionBuilder concrete
- UtilityDecisionCatalog.cs: UtilityRegistry, reflective scanner, Shared singleton
- ThreatMatrixAssignmentSystem.cs: greedy squad assignment with focus-fire cap
- UtilityScorer: instance API (ctor + Evaluate/SelectPosture/EvaluateCandidates)
- StarterPack: Posture enum, CombatPostureDecision, ThreatRankingDecision,
  WeaponSelectionDecision, LeaderAssignmentDecision
- Fix D-07: UtilityResultBuffer [ComponentId(151)] + [DataPolicy(NoSave)]
- Fix D-04: namespace Fdp.Toolkit.Tests in UtilityScorerTests + UtilityResultBufferTests
- Fix D-05: Debug.Assert(optionId <= 255) in builder
- Close D-06: deferred to Phase 2
- StarterPackIntegrationTests.cs: 18 integration tests covering all 5 decision branches,
  trace breakdown, hysteresis, assignment cap, and wounded-member posture veto
```
