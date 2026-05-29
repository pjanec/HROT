# BATCH-06: Corrective (BATCH-05 review fixes) + TASK-UAI-P1-09 Integration Nodes

**Developer guide:** `.dev/.guides/DEV-GUIDE.md`
**Design references:** All docs under `.dev/utility-ai/` (architecture, starter-pack, build-order).
**Previous review:** `.dev/utility-ai/reviews/BATCH-05-REVIEW.md` — verdict CHANGES REQUIRED.

---

## Overview

Two goals:

1. **Task 0 (mandatory corrective)** — fix every P1 issue from the BATCH-05 review so that
   P1-07 and P1-08 can finally be marked DONE.
2. **Task 1 (new)** — implement TASK-UAI-P1-09 (integration nodes: BTree / HSM / Blueprint).

Do NOT commit anything until all tests pass. The commit after this batch's approval will
cover both BATCH-05 production code (already done) and all of BATCH-06.

---

## Task 0 — Corrective fixes for BATCH-05 review issues

### 0-A: Fix `SpawnAgent` in `UtilityTestWorld.cs`

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityTestWorld.cs`

`SpawnAgent` currently adds `Health`, `WeaponState`, `Position`, `TargetMemory`, and
`UtilityResultBuffer`. It is MISSING the two components required for trace recording.

Add to `SpawnAgent` after the `UtilityResultBuffer` line:

```csharp
Repo.AddComponent(entity, new UtilityDebugFlags { TraceEnabled = 1 });
Repo.AddComponent<UtilityTraceWorkingMemory1024>(entity);
```

These components are already registered in the constructor. The trace test (0-E-1 below) cannot
pass without them being present on every agent created by `SpawnAgent`.

### 0-B: Fix `SpawnSquadMember` — add launcher mount when `asLauncher = true`

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityTestWorld.cs`

Currently `SpawnSquadMember` ignores `asLauncher`. Immediately after the `UnitSubordinate` and
roster lines, add:

```csharp
if (asLauncher)
    SpawnWeaponMount(member, mountIndex: 1, weaponGuid: Weapons.LauncherGuid,
                     effRange: 350f, ammo01: ammo01, initialAmmunition: 4);
```

`Weapons.LauncherGuid` is the constant already used in the design doc for the starter-pack
tests (see `Utility_AI_StarterPack_Examples_v1_1.md`). Verify that `Weapons` static class
exists in the test project; if not, declare it in `UtilityTestWorld.cs`:

```csharp
public static class Weapons
{
    public const ulong RifleGuid    = 0x0000_0000_0000_0001UL;
    public const ulong PistolGuid   = 0x0000_0000_0000_0002UL;
    public const ulong LauncherGuid = 0x0000_0000_0000_0003UL;
}
```

### 0-C: Add missing helpers to `UtilityTestWorld.cs`

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityTestWorld.cs`

Add the following four helper methods. Each has a summary comment; do NOT rewrite them.

#### `SetHealth`

```csharp
/// <summary>
/// Overwrites Health.Current on <paramref name="entity"/> to
/// <paramref name="health01"/> * Health.Max.
/// Creates a Health component if one is absent.
/// </summary>
public void SetHealth(Entity entity, float health01)
{
    if (!Repo.HasComponent<Health>(entity))
    {
        Repo.AddComponent(entity, new Health { Current = health01 * 100f, Max = 100f });
        return;
    }
    ref var h = ref Repo.GetComponentRW<Health>(entity);
    h.Current = health01 * h.Max;
}
```

#### `SetEnemyStrengthRatio`

`EnemyStrengthRatio` in `StandardInputs.cs` is computed as:
`threatSum / (healthFraction * PerceptionConstants.MaxTrackedTargets)`.

To drive it to a target ratio, scale the ThreatScores of whatever contacts are already in
`TargetMemory`. If there are none, seed a single synthetic contact.

```csharp
/// <summary>
/// Adjusts the entity's TargetMemory ThreatScores so that
/// <see cref="StandardInputs.EnemyStrengthRatio"/> returns approximately
/// <paramref name="ratio"/> (clamped to [0,1]).
/// If no contacts exist, seeds a synthetic entity with the required score.
/// </summary>
public void SetEnemyStrengthRatio(Entity entity, float ratio)
{
    float healthFraction = 1f;
    if (Repo.HasComponent<Health>(entity))
    {
        ref readonly var h = ref Repo.GetComponentRO<Health>(entity);
        if (h.Max > 0f)
            healthFraction = Math.Clamp(h.Current / h.Max, 0f, 1f);
    }
    float targetSum = ratio * healthFraction * PerceptionConstants.MaxTrackedTargets;

    ref var tm = ref Repo.GetComponentRW<TargetMemory>(entity);
    if (tm.Count == 0)
    {
        // Seed one synthetic contact with the required aggregate threat score.
        var dummy = Repo.CreateEntity();
        Repo.AddComponent(dummy, new Position { Value = new Vector3(100f, 0f, 0f) });
        TargetMemory.AddOrUpdateTarget(ref tm, (long)dummy.PackedValue,
            posX: 100f, posY: 0f, scoreBoost: targetSum, tick: ++Tick,
            modality: SensorModality.Visual);
    }
    else
    {
        // Scale existing ThreatScores proportionally.
        float currentSum = 0f;
        for (int i = 0; i < tm.Count; i++) currentSum += tm.ThreatScores[i];
        if (currentSum > 0f)
        {
            float scale = targetSum / currentSum;
            for (int i = 0; i < tm.Count; i++) tm.ThreatScores[i] *= scale;
        }
        else
        {
            float perContact = targetSum / Math.Max(1, tm.Count);
            for (int i = 0; i < tm.Count; i++) tm.ThreatScores[i] = perContact;
        }
    }
}
```

#### `SpawnTarget`

```csharp
/// <summary>
/// Creates a generic target entity with Health (full) and Position (zero).
/// </summary>
public Entity SpawnTarget()
{
    var t = Repo.CreateEntity();
    Repo.AddComponent(t, new Health { Current = 100f, Max = 100f });
    Repo.AddComponent(t, new Position { Value = Vector3.Zero });
    return t;
}
```

#### `SeedSquadContacts`

```csharp
/// <summary>
/// Seeds each target into the leader's TargetMemory at 120 m, threat 0.6, full health, LOS.
/// </summary>
public void SeedSquadContacts(Entity leader, Entity[] targets)
{
    foreach (var t in targets)
        SeedContact(leader, t, distanceM: 120f, threatBoost: 0.6f,
                    contactHealth01: 1f, hasLos: true);
}
```

### 0-D: Fix namespace in `StarterPackIntegrationTests.cs`

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/StarterPackIntegrationTests.cs`

The namespace declaration at line 1 reads `namespace Fdp.Toolkit.Tests.Utility`.
Change it to `namespace Fdp.Toolkit.Tests`.

No other changes to the file header are needed for this step.

### 0-E: Add 6 missing tests to `StarterPackIntegrationTests.cs`

Add the tests below. Each test class already exists in the file; add the missing `[Fact]`
methods inside the appropriate class. References to `Fnv1a32` and `StandardInputs` are
already in scope.

Refer to `Utility_AI_StarterPack_Examples_v1_1.md` for the authoritative test pseudocode.
Implement them as close to the doc as possible, adapting only where the real API differs
from the pseudocode.

---

#### Test 1 — Trace records per-consideration breakdown for winner (SC-P1-08-2)

Add inside the `CombatPostureTests` class (or a new `TraceTests` class in the same file).

Setup:
- `SpawnAgent(health01: 1.0f, ammo01: 1.0f)`
- `SeedContact(self, enemy, 80f, 0.5f, 1f, hasLos: true)`
- `SetEnemyStrengthRatio(self, 0.5f)` — outnumbering
- `SpawnEqsSensor(self, Fnv1a32("CoverQuery"), topScore: 0.85f, count: 3, instanceId: 0)`
- Call `w.Scorer.SelectPosture(w.Repo, self, CombatPostureDecision.Id)`

Assertions:
```csharp
ref readonly var trace = ref w.Repo.GetComponentRO<UtilityTraceWorkingMemory1024>(self);
var winner = trace.LatestSelected();
Assert.True(winner.ConsiderationCount > 0);
Assert.True(winner.RunnerUpMargin >= 0f);
// The EqsTopScore consideration (cover query) was a decisive factor.
var coverConsideration = winner.ConsiderationByInput(StandardInputIds.EqsTopScore);
Assert.True(coverConsideration.CurveOutput > 0.5f,
    "EqsTopScore consideration for TakeCover branch should be > 0.5");
```

Note: `StandardInputIds.EqsTopScore` is the input ID constant for the cover query reader.
Verify the constant name in `StandardInputs.cs` and use the correct one.

---

#### Test 2 — Wounded member vetoes assignment and breaks off (SC-P1-07-3 / SC-P1-08-4)

This is `Wounded_Member_Vetoes_Assignment_And_Breaks_Off`. The authoritative pseudocode is in
`Utility_AI_StarterPack_Examples_v1_1.md §4.3`. Implement it in `LeaderAssignmentTests`.

Key points:
- Member health01 = 0.08 (near-death)
- Run `ThreatMatrixAssignmentSystem` to confirm m1 is assigned to t1
- Add `SpawnEqsSensor(m1, Fnv1a32("RetreatQuery"), topScore: 0.7f, count: 1, instanceId: 1)`
  so Flee is not gated
- Run `w.Scorer.SelectPosture(w.Repo, m1, CombatPostureDecision.Id)`
- Assert posture == `Posture.Flee`

---

#### Tests 3–5 — EQS-based CombatPosture scenarios

These tests belong in `CombatPostureTests`. They exercise paths that require `SetEnemyStrengthRatio`
and `SpawnEqsSensor`. The authoritative pseudocode is in
`Utility_AI_StarterPack_Examples_v1_1.md §3.2`.

**Test 3 — `Hurt_With_Cover_Available_Takes_Cover`**
- `SpawnAgent(health01: 0.35f, ammo01: 0.8f)`
- `SeedContact(self, enemy, 90f, 0.8f, 1f, hasLos: true)`
- `SetEnemyStrengthRatio(self, 1.3f)` — slightly outnumbered
- `SpawnEqsSensor(self, Fnv1a32("CoverQuery"),   topScore: 0.85f, count: 3, instanceId: 0)`
- `SpawnEqsSensor(self, Fnv1a32("RetreatQuery"), topScore: 0.20f, count: 1, instanceId: 1)`
- `Assert.Equal(Posture.TakeCover, w.Scorer.SelectPosture(...))`

**Test 4 — `NearDeath_With_Escape_Flees`**
- `SpawnAgent(health01: 0.12f, ammo01: 0.3f)`
- `SeedContact(self, enemy, 70f, 0.9f, 1f, hasLos: true)`
- `SetEnemyStrengthRatio(self, 2.5f)` — badly outnumbered
- `SpawnEqsSensor(self, Fnv1a32("CoverQuery"),   topScore: 0.30f, count: 1, instanceId: 0)`
- `SpawnEqsSensor(self, Fnv1a32("RetreatQuery"), topScore: 0.75f, count: 2, instanceId: 1)`
- `Assert.Equal(Posture.Flee, w.Scorer.SelectPosture(...))`

**Test 5 — `NearDeath_With_No_Escape_And_No_Cover_Does_Not_Flee_Into_Nothing`**
- `SpawnAgent(health01: 0.12f, ammo01: 0.6f)`
- `SeedContact(self, enemy, 50f, 0.9f, 1f, hasLos: true)`
- `SetEnemyStrengthRatio(self, 2.5f)`
- `SpawnEqsSensor(self, Fnv1a32("CoverQuery"),   topScore: 0.05f, count: 0, instanceId: 0)`
- `SpawnEqsSensor(self, Fnv1a32("RetreatQuery"), topScore: 0.05f, count: 0, instanceId: 1)`
- `Assert.Equal(Posture.Hold, w.Scorer.SelectPosture(...))`
  (cover and flee are EQS-gated; Hold survives because it uses weighted-sum with Constant(0.2f) floor)

---

#### Test 6 — Assigned target bias promotes leader choice (SC-P1-06-5 / ThreatRanking)

This is `Assigned_Target_Bias_Promotes_Leader_Choice`. The authoritative pseudocode is in
`Utility_AI_StarterPack_Examples_v1_1.md §1.2`. Add to `ThreatRankingTests`.

Setup:
- `SpawnLeader()` → `leader`
- `SpawnSquadMember(leader, 1.0f, 1.0f)` → `self`
- Create entities `a` and `b`; seed both into `self`'s TargetMemory with similar but not equal scores
  so that `a` would win without the assignment bias
- Write `b.PackedValue` as `self`'s assignment via the projected `ThreatMatrixAssignmentState`
  on the leader's `Blackboard1024`:

```csharp
ref var bb    = ref w.Repo.GetComponentRW<Blackboard1024>(leader);
ref var state = ref ThreatMatrixAssignmentState.Project(ref bb);
ref var roster = ref w.Repo.GetComponentRW<UnitRoster>(leader);
int slot = UnitRoster.IndexOf(ref roster, (long)self.PackedValue);
state.SetAssignedTarget(slot, (long)b.PackedValue);
```

- Run `w.Scorer.Evaluate(w.Repo, self, ThreatRankingDecision.Id)`
- Assert `w.Repo.GetComponentRO<UtilityResultBuffer>(self).Top().Candidate == b.PackedValue`

Note: `ThreatMatrixAssignmentState.SetAssignedTarget` might be named differently in the actual
struct (check `ThreatMatrixAssignmentSystem.cs` and the struct definition for the exact API).
If needed, write the assignment directly into the projected state's array.

---

### 0-F: Add D-08 to DEBT-TRACKER.md

**File:** `.dev/utility-ai/DEBT-TRACKER.md`

Add a new entry:

```
## D-08: Residual namespace inconsistency in older test files

**Status:** OPEN
**Files affected:**
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/StandardInputReaderTests.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/CurveEvaluationTests.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/AggregatorTests.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityCoreTests.cs`

**Problem:** These files still use `namespace Fdp.Toolkit.Tests.Utility` (old convention).
The canonical namespace is `Fdp.Toolkit.Tests` (fixed in BATCH-05 for UtilityScorerTests
and UtilityResultBufferTests; fixed in BATCH-06 for StarterPackIntegrationTests).

**Deferred to:** Phase 2 cleanup batch; no test behaviour change required.
```

---

## Task 1 — TASK-UAI-P1-09: Integration nodes (BTree / HSM / Blueprint)

**Design reference:** `Utility_AI_Design_v1_1.md §7`.
**Task detail:** `.dev/utility-ai/TASK-DETAIL.md` §TASK-UAI-P1-09.

Success conditions: SC-P1-09-1 through SC-P1-09-4 (all four must pass).

### 1-A: `UtilitySelectorNode` (BTree integration — SC-P1-09-1)

**New file:** `FDP/Toolkits/Fdp.Toolkits/Utility/Integration/UtilitySelectorNode.cs`

This is a standalone helper class for BTree-hosted behaviors. It wraps the utility scorer
into a "branch selector" — given a list of option IDs (one per BTree branch), it scores the
decision and returns the index of the winning branch, with hysteresis suppressing switches
within the bonus window.

```csharp
namespace Fdp.Toolkit.Utility.Integration
{
    /// <summary>
    /// BTree integration helper: evaluates a utility decision and selects one of N branches
    /// based on score. Hysteresis prevents rapid branch switching.
    ///
    /// Typical BTree usage:
    ///   var selector = new UtilitySelectorNode(scorer, decisionId, new[]{ PostureA, PostureB });
    ///   // Inside [BTreeCondition] for branch i:
    ///   return selector.IsActiveBranch(repo, entity, branchIndex: i);
    /// </summary>
    public sealed class UtilitySelectorNode
    {
        private readonly UtilityScorer _scorer;
        private readonly int           _decisionId;
        private readonly byte[]        _optionIds;       // ordered branch option IDs
        private int                    _activeBranch;    // index of last winning branch (-1 = none)

        public UtilitySelectorNode(UtilityScorer scorer, int decisionId, byte[] optionIds)
        {
            _scorer      = scorer;
            _decisionId  = decisionId;
            _optionIds   = optionIds;
            _activeBranch = -1;
        }

        /// <summary>
        /// Re-scores the decision and returns the 0-based index of the branch that should run.
        /// Applies <paramref name="hysteresisBonus"/> to the currently active branch.
        /// Returns -1 if the decision is not registered.
        /// </summary>
        public int SelectBranch(EntityRepository repo, Entity entity,
                                float hysteresisBonus = 0.08f, ushort tick = 0)
        {
            _scorer.Evaluate(repo, entity, _decisionId, context: default, tick: tick);

            ref readonly var buf = ref repo.GetComponentRO<UtilityResultBuffer>(entity);
            if (buf.Count == 0) return _activeBranch;

            int bestBranch = -1;
            float bestScore = -1f;
            for (int i = 0; i < _optionIds.Length; i++)
            {
                float s = ScoreForOption(ref buf, _optionIds[i]);
                if (i == _activeBranch) s += hysteresisBonus;   // boost active branch
                if (s > bestScore) { bestScore = s; bestBranch = i; }
            }
            _activeBranch = bestBranch;
            return bestBranch;
        }

        /// <summary>
        /// Returns true iff <paramref name="branchIndex"/> is the currently active branch
        /// after calling <see cref="SelectBranch"/> with the same arguments.
        /// </summary>
        public bool IsActiveBranch(EntityRepository repo, Entity entity,
                                   int branchIndex, float hysteresisBonus = 0.08f, ushort tick = 0)
            => SelectBranch(repo, entity, hysteresisBonus, tick) == branchIndex;

        private static float ScoreForOption(ref readonly UtilityResultBuffer buf, byte optionId)
        {
            var span = buf.GetSpanRO();
            for (int i = 0; i < buf.Count; i++)
                if (span[i].WinningPostureId == optionId) return span[i].Score;
            return 0f;
        }
    }
}
```

Note: `_scorer.Evaluate` for `PostureSelect` decisions writes scores for each posture option
into `UtilityResultBuffer`; `WinningPostureId` carries the option byte on each entry.
Verify that the actual `UtilityResultBuffer` entries populated by `PostureSelect` evaluation
use `WinningPostureId` correctly (check `UtilityScorer.cs` `EvaluateCandidates`). If the
field is populated differently, adjust `ScoreForOption` accordingly.

**New test file:** `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/Integration/UtilitySelectorNodeTests.cs`

SC-P1-09-1 test:
```csharp
[Fact]
public void SelectBranch_Returns_HighestScoringOption_Index()
{
    using var w = new UtilityTestWorld();
    var self = w.SpawnAgent(health01: 1.0f, ammo01: 1.0f);
    var enemy = w.Repo.CreateEntity();
    w.SeedContact(self, enemy, 80f, 0.5f, 1f, hasLos: true);
    w.SetEnemyStrengthRatio(self, 0.4f);     // outnumbering -> AdvanceAndAttack scores high

    var node = new UtilitySelectorNode(
        w.Scorer,
        CombatPostureDecision.Id,
        new byte[] {
            (byte)Posture.AdvanceAndAttack,
            (byte)Posture.TakeCover,
            (byte)Posture.Flee,
        });

    int branch = node.SelectBranch(w.Repo, self);
    Assert.Equal(0, branch);    // AdvanceAndAttack is index 0
}

[Fact]
public void Hysteresis_Suppresses_Switch_On_Marginal_Score_Change()
{
    using var w = new UtilityTestWorld();
    var self = w.SpawnAgent(health01: 0.55f, ammo01: 0.9f);
    var enemy = w.Repo.CreateEntity();
    w.SeedContact(self, enemy, 90f, 0.6f, 1f, hasLos: true);
    w.SetEnemyStrengthRatio(self, 1.0f);
    w.SpawnEqsSensor(self, UtilityTestWorld.Fnv1a32("CoverQuery"), topScore: 0.55f,
                     count: 2, instanceId: 0);

    var node = new UtilitySelectorNode(
        w.Scorer,
        CombatPostureDecision.Id,
        new byte[] {
            (byte)Posture.AdvanceAndAttack,
            (byte)Posture.TakeCover,
        });

    int first = node.SelectBranch(w.Repo, self);

    // 1% health nudge -- without hysteresis this could flip branches
    w.SetHealth(self, 0.54f);
    int second = node.SelectBranch(w.Repo, self);

    Assert.Equal(first, second);    // hysteresis holds the selection
}
```

---

### 1-B: `UtilityTransitionArbiter` (HSM integration — SC-P1-09-2)

**New file:** `FDP/Toolkits/Fdp.Toolkits/Utility/Integration/UtilityTransitionArbiter.cs`

The HSM `[HsmGuard]`-shaped arbiter reads the entity's `UtilityResultBuffer` and returns
`true` when the top posture option matches the guarded state's option ID.

```csharp
using Fdp.Core;
using Fdp.Toolkit.Utility;
using Hrot.Editor.AiShared;   // for [HsmGuard] attribute

namespace Fdp.Toolkit.Utility.Integration
{
    /// <summary>
    /// HSM guard: returns true when the entity's utility result buffer indicates that
    /// <paramref name="optionId"/> is the winning posture option.
    ///
    /// Intended use in HSM transition guards (tagged with [HsmGuard] for editor discovery).
    /// </summary>
    public static class UtilityTransitionArbiter
    {
        /// <summary>
        /// Returns true iff the entity's <see cref="UtilityResultBuffer"/> top entry's
        /// <see cref="UtilityResultEntry.WinningPostureId"/> equals <paramref name="optionId"/>.
        /// Returns false if the entity has no result buffer or buffer is empty.
        /// </summary>
        [HsmGuard]
        public static bool Evaluate(EntityRepository repo, Entity entity, byte optionId)
        {
            if (!repo.HasComponent<UtilityResultBuffer>(entity)) return false;
            ref readonly var buf = ref repo.GetComponentRO<UtilityResultBuffer>(entity);
            if (buf.Count == 0) return false;
            return buf.Top().WinningPostureId == optionId;
        }
    }
}
```

Note: the `[HsmGuard]` attribute is from `Hrot.Editor.AiShared`. Check the correct namespace
in `Hrot/Editor/Hrot.Editor.AiShared/` — look at existing `HsmGuardAttribute.cs` or similar.
If the project reference is not already included in `Fdp.Toolkits`, add the attribute inline
instead of importing the editor project:

```csharp
// In the same file or a shared file in Utility/Integration/:
[AttributeUsage(AttributeTargets.Method)]
internal sealed class HsmGuardAttribute : Attribute { }
```

Use whichever approach compiles. The attribute is only for editor tooling discovery; the
`Evaluate` method is the real deliverable.

**New test file:** `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/Integration/UtilityTransitionArbiterTests.cs`

SC-P1-09-2 tests:

```csharp
[Fact]
public void Evaluate_ReturnsTrue_ForWinningOption()
{
    using var w = new UtilityTestWorld();
    var self = w.SpawnAgent(health01: 1.0f, ammo01: 1.0f);
    var enemy = w.Repo.CreateEntity();
    w.SeedContact(self, enemy, 80f, 0.5f, 1f, hasLos: true);
    w.SetEnemyStrengthRatio(self, 0.4f);   // AdvanceAndAttack should win

    w.Scorer.SelectPosture(w.Repo, self, CombatPostureDecision.Id);

    byte winner = w.Repo.GetComponentRO<UtilityResultBuffer>(self).Top().WinningPostureId;
    Assert.True(UtilityTransitionArbiter.Evaluate(w.Repo, self, winner));
}

[Fact]
public void Evaluate_ReturnsFalse_ForLosingOption()
{
    using var w = new UtilityTestWorld();
    var self = w.SpawnAgent(health01: 1.0f, ammo01: 1.0f);
    var enemy = w.Repo.CreateEntity();
    w.SeedContact(self, enemy, 80f, 0.5f, 1f, hasLos: true);
    w.SetEnemyStrengthRatio(self, 0.4f);

    w.Scorer.SelectPosture(w.Repo, self, CombatPostureDecision.Id);

    byte winner = w.Repo.GetComponentRO<UtilityResultBuffer>(self).Top().WinningPostureId;
    byte loser = winner == (byte)Posture.AdvanceAndAttack
        ? (byte)Posture.Flee
        : (byte)Posture.AdvanceAndAttack;

    Assert.False(UtilityTransitionArbiter.Evaluate(w.Repo, self, loser));
}

[Fact]
public void Evaluate_ReturnsFalse_WhenNoResultBuffer()
{
    using var w = new UtilityTestWorld();
    var bare = w.Repo.CreateEntity();   // no components
    Assert.False(UtilityTransitionArbiter.Evaluate(w.Repo, bare, 1));
}
```

---

### 1-C: Blueprint integration — `ScoreDecisionNode` + `ReadRankedResultNode`
#### SC-P1-09-3 and SC-P1-09-4

This section adds two new Blueprint AST nodes and wires them through the full compilation
pipeline, modeled exactly on `ReadEqsResultNode`. Read each of the following files carefully
before writing any code:

- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Assets/Nodes.cs`
  (for `ReadEqsResultNode` definition and `[JsonDerivedType]` pattern)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Ir/IrOperation.cs`
  (for `IrOp_ReadEqsResult` record — lines 208–225)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Stages/Stage5_Schedule.cs`
  (for the `case ReadEqsResultNode rer:` lowering — line 930, in `ResolveNodeOutput`)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Emit/StatementEmitter.cs`
  (for the `case IrOp_ReadEqsResult op:` emit — line 471)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Emit/InstanceEmitter.cs`
  (for `CollectReadEqsResultOps` + `EmitReadEqsResultHelpers` — lines 375–455)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/ReadEqsResultNodeRuntimeTests.cs`
  (for the full test pattern: building assets, registering, invoking, asserting)

#### Step 1-C-1: AST nodes in `Nodes.cs`

Add two new classes and two `[JsonDerivedType]` attributes alongside `ReadEqsResultNode`.

`[JsonDerivedType(typeof(ScoreDecisionNode),    "ScoreDecision")]`
`[JsonDerivedType(typeof(ReadRankedResultNode), "ReadRankedResult")]`

```csharp
// ScoreDecisionNode (DESIGN §7.3 -- runs a UtilityDecisionDef, outputs WinningOptionId)
public sealed class ScoreDecisionNode : Node
{
    /// <summary>
    /// The GUID string of the UtilityDecisionDef asset to evaluate (e.g.
    /// "3c6f9e42-5d10-6f3a-ac23-posture0000001" for CombatPostureDecision).
    /// </summary>
    public string AssetId { get; set; } = string.Empty;
}

// ReadRankedResultNode (DESIGN §7.3 -- reads rank-i entry from UtilityResultBuffer)
public sealed class ReadRankedResultNode : Node
{
    /// <summary>0-based rank index (0 = top-ranked).</summary>
    public int Rank { get; set; }
}
```

Standard pins for `ScoreDecisionNode` — add in the node's constructor or as a factory:
- Input exec pin (Name="In", IsExec=true, Direction="In")
- Output exec pin (Name="Out", IsExec=true, Direction="Out")
- Output data pin: "WinningOptionId" (Direction="Out", TypeRef="System.Byte")

Standard pins for `ReadRankedResultNode`:
- No exec pins (pure data node, inline like `ReadEqsResultNode`)
- Output: "Entity" (Direction="Out", TypeRef="System.Int64")
- Output: "Score"  (Direction="Out", TypeRef="System.Single")
- Output: "IsValid" (Direction="Out", TypeRef="System.Boolean")

Note: Look at `ReadEqsResultNode.cs` to see how pins are declared (they may be set via
`Pins.AddRange(...)` by the node builder, not in the class itself). Follow the same approach.

#### Step 1-C-2: `UtilityBlueprintBridge` static helper

**New file:** `FDP/Toolkits/Fdp.Toolkits/Utility/Integration/UtilityBlueprintBridge.cs`

The Blueprint generated code uses `ISimulationView` as its ECS access interface. Since
`EntityRepository : ISimulationView`, a downcast is safe within this system's tests.

```csharp
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Utility;

namespace Fdp.Toolkit.Utility.Integration
{
    /// <summary>
    /// Static helpers for Blueprint-generated code to call the Utility AI scorer
    /// via the <see cref="ISimulationView"/> interface.
    /// Called from generated Blueprint instance code; no allocations on the hot path.
    /// </summary>
    public static class UtilityBlueprintBridge
    {
        /// <summary>
        /// Runs the utility decision identified by <paramref name="decisionId"/> for
        /// <paramref name="self"/> and returns the winning posture option byte.
        /// Returns 0 if the decision is not found or the buffer is empty.
        /// </summary>
        public static byte ScoreDecision(ISimulationView view, Entity self, int decisionId, uint tick)
        {
            if (view is not EntityRepository repo) return 0;
            if (!repo.HasComponent<UtilityResultBuffer>(self)) return 0;

            var scorer = new UtilityScorer(UtilityDecisionCatalog.Shared);
            return (byte)scorer.SelectPosture(repo, self, decisionId, tick);
        }

        /// <summary>
        /// Reads rank-<paramref name="rank"/> entry from the entity's
        /// <see cref="UtilityResultBuffer"/>.
        /// Returns (0, 0f, false) if the buffer is absent or rank is out of range.
        /// </summary>
        public static (long candidateHandle, float score, bool isValid)
            ReadRankedResult(ISimulationView view, Entity self, int rank)
        {
            if (!view.HasComponent<UtilityResultBuffer>(self))
                return default;
            ref readonly var buf = ref view.GetComponentRO<UtilityResultBuffer>(self);
            if (rank < 0 || rank >= buf.Count) return default;
            var e = buf.GetSpanRO()[rank];
            return (e.CandidateHandle, e.Score, true);
        }
    }
}
```

#### Step 1-C-3: IR operations in `IrOperation.cs`

Add two new records after `IrOp_ReadEqsResult`:

```csharp
/// <summary>
/// Emitted by Stage 5 when a ScoreDecisionNode is encountered in the exec chain.
/// Stage 7 emits an [AggressiveInlining] helper that calls UtilityBlueprintBridge.ScoreDecision.
/// </summary>
public sealed record IrOp_ScoreDecision(
    /// <summary>Baked numeric decision ID literal (FNV-1a hash of the AssetId GUID).</summary>
    string DecisionIdLiteral,
    /// <summary>8-char hex prefix of the node ID.</summary>
    string NodeId8
) : IrOperation;

/// <summary>
/// Emitted by Stage 5 when a ReadRankedResultNode output pin is first resolved.
/// Stage 7 emits an [AggressiveInlining] helper + result struct per node.
/// </summary>
public sealed record IrOp_ReadRankedResult(
    /// <summary>Rank literal (0 = top).</summary>
    string RankLiteral,
    /// <summary>8-char hex prefix of the node ID.</summary>
    string NodeId8,
    /// <summary>Name of the generated result struct type.</summary>
    string ResultStructTypeName
) : IrOperation;
```

#### Step 1-C-4: Stage 5 lowering in `Stage5_Schedule.cs`

**`ScoreDecisionNode` — add in `EmitNodeStatements`:**

After `case SpawnEqsSensorNode:`, add:

```csharp
case ScoreDecisionNode sdn:
{
    string id8 = sdn.Id.ToString("N").Substring(0, 8);
    // Bake the decision ID at compile time.
    int decisionId = UtilityDecisionCatalog.ComputeId(sdn.AssetId);
    string decisionIdLiteral = decisionId.ToString();

    var byteType = new IrTypeRef { FullName = "System.Byte", IsUnmanaged = true, SizeBytes = 1 };
    var optionResult = AllocValue(byteType);
    stmts.Add(new IrStatement
    {
        ResultValue = optionResult,
        Operation   = new IrOp_ScoreDecision(decisionIdLiteral, id8),
        Debug       = DebugOf(sdn),
    });

    var outPin = sdn.Pins.FirstOrDefault(p => !p.IsExec && p.Direction == "Out"
                     && string.Equals(p.Name, "WinningOptionId", StringComparison.OrdinalIgnoreCase));
    if (outPin is not null)
        _pinValueCache[outPin.Id] = optionResult;
    break;
}
```

**`ReadRankedResultNode` — add in `ResolveNodeOutput`:**

Following the exact same structure as `case ReadEqsResultNode rer:` (line 930):

```csharp
case ReadRankedResultNode rrn:
{
    string id8 = rrn.Id.ToString("N").Substring(0, 8);
    string structTypeName = $"_RankedResultRead_{id8}";

    var resultStructType = new IrTypeRef
    {
        FullName    = structTypeName,
        IsUnmanaged = true,
        SizeBytes   = 16, // bool(1) + long(8) + float(4) + pad = 16
    };

    string rankLiteral = rrn.Rank.ToString();

    var helperResult = AllocValue(resultStructType);
    stmts.Add(new IrStatement
    {
        ResultValue = helperResult,
        Operation   = new IrOp_ReadRankedResult(rankLiteral, id8, structTypeName),
        Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = rrn.Id },
    });

    foreach (var outPin in rrn.Pins.Where(p => !p.IsExec && p.Direction == "Out"))
    {
        if (_pinValueCache.ContainsKey(outPin.Id)) continue;
        IrTypeRef fieldType = _typed.PinTypes.TryGetValue(outPin.Id, out var t2)
            ? t2 : Stage5_Schedule.UnknownType;
        var fieldResult = AllocValue(fieldType);
        stmts.Add(new IrStatement
        {
            ResultValue = fieldResult,
            Operation   = new IrOp_FieldRead(helperResult, outPin.Name, fieldType),
            Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = rrn.Id, PinId = outPin.Id },
        });
        _pinValueCache[outPin.Id] = fieldResult;
    }

    result = _pinValueCache.TryGetValue(sourcePinId, out var pinRes) ? pinRes : helperResult;
    break;
}
```

#### Step 1-C-5: Statement emitter in `StatementEmitter.cs`

Add two cases after `case IrOp_ReadEqsResult`:

```csharp
case IrOp_ScoreDecision op:
{
    if (idx >= 0)
        e.WriteLine($"var __t{idx} = ScoreDecision_{op.NodeId8}({wv}, self, time);");
    break;
}

case IrOp_ReadRankedResult op:
{
    if (idx >= 0)
        e.WriteLine($"var __t{idx} = ReadRankedResult_{op.NodeId8}({wv}, self);");
    break;
}
```

#### Step 1-C-6: Instance emitter in `InstanceEmitter.cs`

Add helpers collection + emission methods, modeled on `CollectReadEqsResultOps` and
`EmitReadEqsResultHelpers`. Call them from the same place those are called.

**Collect ops:**

```csharp
private static List<IrOp_ScoreDecision> CollectScoreDecisionOps(IrAsset asset)
{
    var result = new List<IrOp_ScoreDecision>();
    var seen   = new HashSet<string>();
    foreach (var graph in asset.Graphs)
    foreach (var block in graph.Blocks)
    foreach (var stmt  in block.Statements)
    {
        if (stmt.Operation is not IrOp_ScoreDecision op) continue;
        if (!seen.Add(op.NodeId8)) continue;
        result.Add(op);
    }
    return result;
}

private static List<IrOp_ReadRankedResult> CollectReadRankedResultOps(IrAsset asset)
{
    var result = new List<IrOp_ReadRankedResult>();
    var seen   = new HashSet<string>();
    foreach (var graph in asset.Graphs)
    foreach (var block in graph.Blocks)
    foreach (var stmt  in block.Statements)
    {
        if (stmt.Operation is not IrOp_ReadRankedResult op) continue;
        if (!seen.Add(op.NodeId8)) continue;
        result.Add(op);
    }
    return result;
}
```

**Emit helpers:**

For `ScoreDecisionNode`:

```csharp
private static void EmitScoreDecisionHelpers(CSharpEmitter e, List<IrOp_ScoreDecision> ops)
{
    foreach (var op in ops)
    {
        e.WriteLine($"[global::System.Runtime.CompilerServices.MethodImpl(" +
                    $"global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
        e.WriteLine($"private static byte ScoreDecision_{op.NodeId8}(");
        e.Indent();
        e.WriteLine($"global::Fdp.ModuleHost.Abstractions.ISimulationView view,");
        e.WriteLine($"global::Fdp.Core.Entity self,");
        e.WriteLine($"float time)");
        e.Outdent();
        e.WriteLine("{");
        e.Indent();
        e.WriteLine($"uint tick = (uint)(time * 60f);");   // approximate tick from time
        e.WriteLine($"return global::Fdp.Toolkit.Utility.Integration.UtilityBlueprintBridge" +
                    $".ScoreDecision(view, self, {op.DecisionIdLiteral}, tick);");
        e.Outdent();
        e.WriteLine("}");
        e.WriteLine();
    }
}
```

For `ReadRankedResultNode`:

```csharp
private static void EmitReadRankedResultHelpers(CSharpEmitter e, List<IrOp_ReadRankedResult> ops)
{
    foreach (var op in ops)
    {
        // Emit the result struct
        e.WriteLine($"[global::System.Runtime.InteropServices.StructLayout(" +
                    $"global::System.Runtime.InteropServices.LayoutKind.Sequential)]");
        e.WriteLine($"private struct {op.ResultStructTypeName}");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine("public bool  IsValid;");
        e.WriteLine("public long  Entity;");
        e.WriteLine("public float Score;");
        e.Outdent();
        e.WriteLine("}");
        e.WriteLine();

        // Emit the helper method
        e.WriteLine($"[global::System.Runtime.CompilerServices.MethodImpl(" +
                    $"global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
        e.WriteLine($"private static {op.ResultStructTypeName} ReadRankedResult_{op.NodeId8}(");
        e.Indent();
        e.WriteLine($"global::Fdp.ModuleHost.Abstractions.ISimulationView view,");
        e.WriteLine($"global::Fdp.Core.Entity self)");
        e.Outdent();
        e.WriteLine("{");
        e.Indent();
        e.WriteLine($"var result = default({op.ResultStructTypeName});");
        e.WriteLine($"var (handle, score, isValid) = " +
                    $"global::Fdp.Toolkit.Utility.Integration.UtilityBlueprintBridge" +
                    $".ReadRankedResult(view, self, {op.RankLiteral});");
        e.WriteLine("result.IsValid = isValid;");
        e.WriteLine("result.Entity  = handle;");
        e.WriteLine("result.Score   = score;");
        e.WriteLine("return result;");
        e.Outdent();
        e.WriteLine("}");
        e.WriteLine();
    }
}
```

Wire these into the main `Emit(IrAsset)` method in `InstanceEmitter` alongside the existing
`CollectReadEqsResultOps`/`EmitReadEqsResultHelpers` calls.

#### Step 1-C-7: Blueprint runtime tests

**New file:**
`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/UtilityNodeRuntimeTests.cs`

Use `ReadEqsResultNodeRuntimeTests.cs` as the structural template. The tests must:

1. Build a `BlueprintAsset` programmatically (using the same builder helpers as in that file)
2. Add a `ScoreDecisionNode` with `AssetId = CombatPostureDecision.AssetId` into the graph
3. Wire the `WinningOptionId` output to a `SetVariableNode` storing to a byte variable
4. Compile, register, tick
5. Assert the stored variable matches `w.Scorer.SelectPosture(...)` called on the same entity

For SC-P1-09-4 (`ReadRankedResultNode`):
1. Build a fixture with a seeded `UtilityResultBuffer` (manually written via `GetSpanRW`)
2. Add a `ReadRankedResultNode` with `Rank = 0`
3. Wire `Entity` output to a variable, `Score` output to another
4. After tick: assert `entityVar == buf.Top().CandidateHandle`, `scoreVar == buf.Top().Score`

For the `UtilityDecisionCatalog.RegisterAll` call inside the test fixture, note that
`CombatPostureDecision` must be in scope (it already is from the Fdp.Toolkits assembly).

---

## Summary of files changed / created

### Modified files (Task 0 corrective):
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityTestWorld.cs`
  (SpawnAgent, SpawnSquadMember, SetHealth, SetEnemyStrengthRatio, SpawnTarget, SeedSquadContacts)
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/StarterPackIntegrationTests.cs`
  (namespace fix + 6 new test methods)
- `.dev/utility-ai/DEBT-TRACKER.md` (add D-08)

### New files (Task 1 P1-09):
- `FDP/Toolkits/Fdp.Toolkits/Utility/Integration/UtilitySelectorNode.cs`
- `FDP/Toolkits/Fdp.Toolkits/Utility/Integration/UtilityTransitionArbiter.cs`
- `FDP/Toolkits/Fdp.Toolkits/Utility/Integration/UtilityBlueprintBridge.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/Integration/UtilitySelectorNodeTests.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/Integration/UtilityTransitionArbiterTests.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/UtilityNodeRuntimeTests.cs`

### Modified files (Task 1 P1-09 Blueprint pipeline):
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Assets/Nodes.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Ir/IrOperation.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Stages/Stage5_Schedule.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Emit/StatementEmitter.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Emit/InstanceEmitter.cs`

---

## Build and test

Run the full test suite before submitting the report:

```
dotnet build IOS-IG-SimHost.sln
dotnet test FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj
dotnet test Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Hrot.Blueprints.Tests.csproj
```

All existing tests must pass and the new tests must pass (minimum **18 total new tests**:
6 corrective + 2 UtilitySelectorNode + 3 UtilityTransitionArbiter + 2 Blueprint node runtime
+ existing tests must remain green).

---

## Report template

Fill in `.dev/utility-ai/reports/BATCH-06-REPORT.md` per the standard developer template in
`.dev/.guides/DEV-GUIDE.md`. Include answers to:
- Q1: How did you implement `SetEnemyStrengthRatio`? Did you use scale or synthetic contact?
- Q2: Did `Posture.Hold` win in Test 5 (`NearDeath_With_No_Escape...`)? What scores did
  AdvanceAndAttack, TakeCover, Flee, and Hold get?
- Q3: Did the `UtilitySelectorNode.ScoreForOption` field lookup use `WinningPostureId`
  correctly for `PostureSelect` mode decisions?
- Q4: For the Blueprint `ScoreDecisionNode` — did you need any additional wiring in
  `InstanceEmitter.Emit()` to call `EmitScoreDecisionHelpers`? Describe the exact
  call-site addition.
- Q5: Did the `ReadRankedResultNode` test (SC-P1-09-4) pass with `rank=0` matching `Top()`?
