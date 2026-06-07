# BATCH-04: Utility AI — Standard Input Readers Catalog

**Batch Number:** BATCH-04
**Tasks:** Debt D-03 (P3 doc fix), Corrective-0 (SeedContact fix), TASK-UAI-P1-06
**Phase:** Phase 1 — Standard input readers
**Estimated Effort:** 15–20 hours
**Priority:** HIGH
**Dependencies:** BATCH-03 (UtilityResultBuffer, UtilityScorer — complete)

---

## 📋 Onboarding & Workflow

### Required Reading (IN ORDER)

1. **Task Detail:** `.dev/utility-ai/TASK-DETAIL.md` — Phase 1 task P1-06 (section `### TASK-UAI-P1-06`)
2. **Architecture:** `.dev/utility-ai/Utility_AI_Design_v1_1.md`
   - §6 "Inputs" (full section) — catalog, normalization, EQS child resolution, component mapping table
   - §10.1 "Leader entity and shared blackboard" — ThreatMatrixAssignmentState shape
3. **Previous Review:** `.dev/utility-ai/reviews/BATCH-03-REVIEW.md`
4. **UtilityTestWorld (existing):** `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityTestWorld.cs`
   Understand all existing helpers before extending.
5. **Real component definitions:**
   - `Health`: `FDP/Toolkits/Fdp.Toolkits/Combat/Components/Health.cs` (`Current`, `Max` floats)
   - `WeaponState`: `FDP/Toolkits/Fdp.Toolkits/Combat/Components/CombatComponents.cs` (`Ammo`, `MaxAmmo`, `CooldownSecondsRemaining`)
   - `WeaponMountInfo`: same file — `EffectiveRange`
   - `TargetMemory`: `FDP/Toolkits/Fdp.Toolkits/Perception/Components/PerceptionComponents.cs` — `ThreatScores`, `Modalities`, `EntityIds`, `PositionsX/Y`, `Count`
   - `SensorModality`: `FDP/Toolkits/Fdp.Toolkits/Perception/Components/SensorModality.cs`
   - `Position`: `FDP/Modules/Geographic/Components/Position.cs` (or similar — use the import already in UtilityTestWorld.cs)
   - `EqsCognitiveBuffer` + `EqsSensor`: `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsComponents.cs`
   - `UnitRoster`, `UnitSubordinate`, `Blackboard1024`: `FDP/Toolkits/Fdp.Toolkits/...` (already imported in UtilityTestWorld.cs)
   - `PartMetadata`: imported in UtilityTestWorld.cs
6. **Debt Tracker:** `.dev/utility-ai/DEBT-TRACKER.md` — D-03 (P3 doc fix)

### Source Code Locations

- **New production files:**
  - `FDP/Toolkits/Fdp.Toolkits/Utility/Inputs/UtilityInputAttribute.cs` — NEW (the `[UtilityInput]` attribute, stub for Phase 2)
  - `FDP/Toolkits/Fdp.Toolkits/Utility/Inputs/StandardInputs.cs` — NEW (all catalog readers)
  - `FDP/Toolkits/Fdp.Toolkits/Utility/Group/ThreatMatrixAssignmentState.cs` — NEW (unmanaged struct; populated by P1-07 in BATCH-05)
- **Existing test helper to fix:**
  - `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityTestWorld.cs` — UPDATE (fix `SeedContact`, `AssignmentFor`, register new components)
- **New test file:**
  - `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/StandardInputReaderTests.cs` — NEW
- **Existing production file to update (D-03):**
  - `FDP/Toolkits/Fdp.Toolkits/Utility/Core/ResponseCurveEvaluate.cs`

### Build and Test Commands

```bat
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build IOS-IG-SimHost.sln
dotnet test FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj
```

All 70 prior utility tests **plus all new BATCH-04 tests** must pass. Run the full suite, not just the utility subset.

### Report Submission

When done, submit your report to: `.dev/utility-ai/reports/BATCH-04-REPORT.md`
If you have questions: `.dev/utility-ai/questions/BATCH-04-QUESTIONS.md`

---

## Context

BATCH-03 delivered the storage layer (UtilityResultBuffer, UtilityTraceWorkingMemory1024) and the scoring engine (UtilityScorer). BATCH-04 implements the catalog of standard input readers — the bridge between raw ECS component state and the [0, 1] normalized values the scorer consumes. Once this batch lands, all Phase-1 readers exist and can be wired into the starter-pack decisions (BATCH-05).

**Related Tasks:**
- [Debt D-03](../DEBT-TRACKER.md) — Quadratic/InverseQuadratic Exponent field doc fix
- [TASK-UAI-P1-06](../TASK-DETAIL.md#task-uai-p1-06-standard-input-readers-catalog) — All catalog readers

---

## 🎯 Batch Objectives

1. Fix D-03: add a clear doc comment to `Quadratic` and `InverseQuadratic` in `ResponseCurveEvaluate.cs` warning that `Exponent` is ignored (the curve names are fixed quadratics).
2. Fix `SeedContact` in `UtilityTestWorld`: make `hasLos` actually control the `SensorModality` and `contactHealth01` actually set a `Health` component on the contact entity.
3. Implement `ThreatMatrixAssignmentState` struct (skeleton only — populated in BATCH-05).
4. Implement all 14 standard input readers in `StandardInputs.cs`.
5. Register all readers via `UtilityInputRegistrar` in a static initializer / `RegisterAll()` helper.
6. All SC-P1-06-1 through SC-P1-06-5 pass as concrete test assertions plus additional reader-specific tests.

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **D-03 + Corrective-0:** Fix doc comment + fix SeedContact → **ALL 70 tests pass** ✅
2. **P1-06 (readers, group A — weapon/health/distance):** Implement AmmoFraction, WeaponHasAmmo, WeaponReadiness, HealthFraction, ContactHealthFraction, DistanceToContext → Write tests → **ALL tests pass** ✅
3. **P1-06 (readers, group B — perception):** Implement ContactThreatLevel, HasLineOfSight, HaveLiveTarget, EnemyStrengthRatio → Write tests → **ALL tests pass** ✅
4. **P1-06 (readers, group C — EQS + assignment + misc):** Implement EqsTopScore, EqsResultCount, IsAssignedTarget, AllyAdvancingNearby, Constant, WeaponRangeBandFit, WeaponEffectivenessVsTarget → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next group until the current group's tests are green.

---

## ✅ Tasks

---

### Corrective 0: Fix `SeedContact` + register new components in `UtilityTestWorld`

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityTestWorld.cs` — UPDATE

**Problem 1 — `SeedContact` ignores `hasLos` and `contactHealth01`:**
The method calls `TargetMemory.AddOrUpdateTarget` with the default modality `SensorModality.Visual`. When `hasLos = false`, the Visual modality bit should NOT be set. When `contactHealth01` is meaningful, the contact entity should have a `Health` component.

**Fix `SeedContact`:**
1. Pass `modality: hasLos ? SensorModality.Visual : SensorModality.Acoustic` to `AddOrUpdateTarget`.
2. After the memory update, if `contactHealth01 >= 0`:
   - If `contact` entity doesn't have a `Health` component, add one with `Current = contactHealth01 * 100f, Max = 100f`.
   - If it already has one, update `Current = contactHealth01 * health.Max`.
3. Also add a `Position` component to `contact` if not already present (set to `new Position { Value = new Vector3(distanceM, 0f, 0f) }` — places it `distanceM` meters along X-axis from origin for distance calculations).

**Fix `AssignmentFor` placeholder:** Replace the `return -1L` stub with the real implementation using `ThreatMatrixAssignmentState` (see Task 1 below).

**Problem 2 — new components not registered:**
Add `Repo.RegisterComponent<UtilityDebugFlags>()` and `Repo.RegisterComponent<UtilityTraceWorkingMemory1024>()` to the constructor. Also add `Repo.RegisterComponent<Health>()` on the contact entity when it doesn't exist (the contact entity is just `Entity contact` — it may not have any components yet). 

Actually, `UtilityTestWorld` constructor already registers `Health` — no change needed there. What's missing is that `SeedContact` creates a contact entity implicitly (the contact might not have been spawned via `SpawnAgent`). The `contact` parameter should be any entity; callers who want a contact with health would need to either `SpawnAgent` for the contact or have `SeedContact` add health automatically.

**Simplest fix:** If `contactHealth01 >= 0f` and the contact entity does NOT have a `Health` component, call `Repo.AddComponent(contact, new Health { Current = contactHealth01 * 100f, Max = 100f })`. If it already has `Health`, update it via `GetComponentRW`.

---

### Task 0: D-03 — Add doc warning to `Quadratic`/`InverseQuadratic`

**File:** `FDP/Toolkits/Fdp.Toolkits/Utility/Core/ResponseCurveEvaluate.cs` — UPDATE

Add a `<remarks>` or inline comment on the `Quadratic` and `InverseQuadratic` branches clearly stating:
"The `Exponent` field is ignored; this curve always applies a fixed quadratic (`x^2`). To use a general power curve, use `MathF.Pow(x, Exponent)` which is not currently implemented but can be added if needed."

No behavioral change — this is documentation only. No new test needed (it is a doc fix for P3 debt D-03).

---

### Task 1: `ThreatMatrixAssignmentState` struct

**File:** `FDP/Toolkits/Fdp.Toolkits/Utility/Group/ThreatMatrixAssignmentState.cs` — NEW

**Design Reference:** `Utility_AI_Design_v1_1.md` §10.1 — the struct is projected onto the commander's `Blackboard1024`.

**Requirements:**
- `[StructLayout(LayoutKind.Sequential)]` unmanaged struct.
- Must fit in 1024 bytes (the Blackboard1024 size). At 16 members: 16 × 64 = 1024 bytes; use exactly 16 slots.
- Per-slot layout (64 bytes each):
  - `long AssignedTargetHandle` — packed entity value of the assigned target (0 = unassigned)
  - `float AssignmentScore` — the utility score that drove this assignment
  - `byte FocusFireCount` — how many squad members are assigned to this target (leader writes; members read only their own slot)
  - `byte Flags` — reserved
  - Padding to reach 64 bytes per slot
- Provide a static `ref ThreatMatrixAssignmentState Project(ref Blackboard1024 bb)` method that uses `Blackboard1024.Project<ThreatMatrixAssignmentState>(ref bb)` (the P0.5 helper).
- Provide `GetSlot(int memberIndex)` returning a ref to the slot at `memberIndex` (for leader write access).
- Provide `GetAssignedTarget(int memberIndex)` returning `AssignedTargetHandle` for the given slot.

Also update `UtilityTestWorld.AssignmentFor` to use the real struct:
```csharp
public long AssignmentFor(Entity leader, Entity member)
{
    ref var bb = ref Repo.GetComponentRW<Blackboard1024>(leader);
    ref var state = ref ThreatMatrixAssignmentState.Project(ref bb);
    int idx = UnitRoster.IndexOf(ref Repo.GetComponentRW<UnitRoster>(leader), (long)member.PackedValue);
    return idx >= 0 ? state.GetAssignedTarget(idx) : -1L;
}
```

---

### Task 2: `[UtilityInput]` attribute stub

**File:** `FDP/Toolkits/Fdp.Toolkits/Utility/Inputs/UtilityInputAttribute.cs` — NEW

Define the `[UtilityInput]` attribute that Phase 2 source generator will pick up:
```csharp
[AttributeUsage(AttributeTargets.Method)]
public sealed class UtilityInputAttribute : Attribute
{
    public string Name { get; }
    public UtilityInputAttribute(string name) { Name = name; }
}
```

This is a small stub. No source generator logic here — that is Phase 2.

---

### Task 3: Standard input readers (TASK-UAI-P1-06)

**File:** `FDP/Toolkits/Fdp.Toolkits/Utility/Inputs/StandardInputs.cs` — NEW

**Design Reference:** `Utility_AI_Design_v1_1.md` §6 (entire section, especially the component-mapping table in §6.1).

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-uai-p1-06-standard-input-readers-catalog).

Implement all readers as a `static class StandardInputs` containing:

**Group A — weapon / health / distance:**

```csharp
[UtilityInput("AmmoFraction")]
public static float AmmoFraction(in UtilityInputCtx ctx)
// reads WeaponState on ctx.Self; returns Ammo/MaxAmmo clamped; 0 if MaxAmmo==0

[UtilityInput("WeaponHasAmmo")]
public static float WeaponHasAmmo(in UtilityInputCtx ctx)
// 1 if Ammo > 0, else 0

[UtilityInput("WeaponReadiness")]
public static float WeaponReadiness(in UtilityInputCtx ctx)
// 1 if CooldownSecondsRemaining <= 0, else 0 (binary gate; Phase 1)

[UtilityInput("HealthFraction")]
public static float HealthFraction(in UtilityInputCtx ctx)
// reads Health on ctx.Self; Current/Max clamped to [0,1]; 0 if Max==0

[UtilityInput("ContactHealthFraction")]
public static float ContactHealthFraction(in UtilityInputCtx ctx)
// same but on ctx.Context; 0 if context entity has no Health component

[UtilityInput("DistanceToContext")]
public static float DistanceToContext(in UtilityInputCtx ctx)
// reads Position on both ctx.Self and ctx.Context; computes 2D or 3D distance;
// normalizes as 1 - clamp(distance / MaxRange, 0, 1) where MaxRange = ctx.Params.MaxRange if > 0, else 1000f;
// closer = higher score: 0m -> 1.0, MaxRange -> 0.0
```

**Group B — perception:**

```csharp
[UtilityInput("ContactThreatLevel")]
public static float ContactThreatLevel(in UtilityInputCtx ctx)
// reads TargetMemory on ctx.Self; finds slot where EntityIds[i] == ctx.Context.PackedValue;
// returns ThreatScores[i] clamped to [0,1]; returns 0 if not found

[UtilityInput("HasLineOfSight")]
public static float HasLineOfSight(in UtilityInputCtx ctx)
// reads TargetMemory on ctx.Self; finds slot for ctx.Context;
// returns 1 if (Modalities[i] & (byte)SensorModality.Visual) != 0, else 0

[UtilityInput("HaveLiveTarget")]
public static float HaveLiveTarget(in UtilityInputCtx ctx)
// reads TargetMemory on ctx.Self; returns 1 if Count > 0, else 0

[UtilityInput("EnemyStrengthRatio")]
public static float EnemyStrengthRatio(in UtilityInputCtx ctx)
// reads TargetMemory on ctx.Self; sums all ThreatScores[0..Count-1];
// normalizes against own Health fraction (sum / (self.Health.Current / Health.Max * MaxTrackedTargets));
// clamp to [0,1]; 0 if no contacts
```

**Group C — EQS sensors:**

```csharp
[UtilityInput("EqsTopScore")]
public static float EqsTopScore(in UtilityInputCtx ctx)
// calls TryFindEqsChild(ctx.Repo, ctx.Self, ctx.Params.BlueprintId, out child);
// returns child's EqsCognitiveBuffer.GetTop().Score if IsReady && Count > 0, else 0

[UtilityInput("EqsResultCount")]
public static float EqsResultCount(in UtilityInputCtx ctx)
// calls TryFindEqsChild; returns (float)buf.Count / 16f (normalized) if ready, else 0
```

**Group D — assignment / misc:**

```csharp
[UtilityInput("IsAssignedTarget")]
public static float IsAssignedTarget(in UtilityInputCtx ctx)
// reads UnitSubordinate on ctx.Self to get commander;
// projects ThreatMatrixAssignmentState from commander's Blackboard1024;
// reads self's slot via UnitRoster.IndexOf;
// returns 1 if assigned target handle == ctx.Context.PackedValue, else 0;
// returns 0 if no commander or slot not found

[UtilityInput("AllyAdvancingNearby")]
public static float AllyAdvancingNearby(in UtilityInputCtx ctx)
// Phase 1 stub: reads UnitRoster on ctx.Self (or checks if ctx.Self has UnitSubordinate);
// returns 0.0f — no ally tracking data in Phase 1.
// NOTE: add a doc comment explaining this is a Phase 2 stub pending formation/posture state.

[UtilityInput("Constant")]
public static float Constant(in UtilityInputCtx ctx)
// returns ctx.Params.MaxRange (repurposed as a "constant value" carrier; MaxRange is float);
// i.e., authors set ctx.Params.MaxRange = desiredConstant when constructing the consideration.
// Valid range: [0,1]; author's responsibility.

[UtilityInput("WeaponRangeBandFit")]
public static float WeaponRangeBandFit(in UtilityInputCtx ctx)
// reads WeaponMountInfo on ctx.Self (the candidate weapon mount entity, via ctx.Params.MountIndex);
// reads Position on ctx.Context (the target);
// computes distance; returns 1 if distance <= EffectiveRange, 0 if > 2×EffectiveRange,
// otherwise linear interpolation in [EffectiveRange, 2×EffectiveRange] → [1, 0].
// If no Position or MountIndex child found, returns 0.

[UtilityInput("WeaponEffectivenessVsTarget")]
public static float WeaponEffectivenessVsTarget(in UtilityInputCtx ctx)
// Phase 1: same as WeaponRangeBandFit (armor match not yet modeled);
// add a doc comment noting armor and target type are Phase 2+.
```

**EQS child resolution helper:**

```csharp
private static bool TryFindEqsChild(EntityRepository repo, Entity owner, uint blueprintId, out Entity child)
// iterates entities with EqsSensor + PartMetadata;
// returns the first entity where PartMetadata.ParentEntity == owner AND EqsSensor.BlueprintId == blueprintId;
// Phase 1: no caching — linear scan (acceptable at ≤16 children per agent)
```

**Registration helper:**

```csharp
public static class StandardInputIds
{
    // FNV-1a-16 hashes of the catalog input names.
    // Computed as: (ushort)(Fnv1a32(name) & 0xFFFF) where Fnv1a32 uses basis=2166136261, prime=16777619.
    // Values must be verified against UtilityTestWorld.Fnv1a32 at test time.
    public const ushort AmmoFraction              = ???; // to be computed
    // ... (one const per reader)
}

public static unsafe void RegisterAll()
{
    UtilityInputRegistrar.Register(StandardInputIds.AmmoFraction,              &StandardInputs.AmmoFraction);
    // ... one line per reader
}
```

Compute the hash values at test time: write a test `StandardInputIds_HashValues_MatchFnv1a32` that asserts each `StandardInputIds` const matches `(ushort)(UtilityTestWorld.Fnv1a32(name) & 0xFFFF)`. This is a cross-check that the manual hash constants are correct.

---

## 🧪 Testing Requirements

**New test file:** `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/StandardInputReaderTests.cs`

**Minimum 20 tests.** Required coverage:

**SC-P1-06-1 (`AmmoFraction`):**
- Returns 0 when `MaxAmmo == 0` (guard test)
- Returns exactly `Ammo / MaxAmmo` for a known fixture (e.g. 15/30 = 0.5f)
- Clamps to 1.0f when Ammo > MaxAmmo (defensive)

**SC-P1-06-2 (`HasLineOfSight`):**
- Returns 1 when `SensorModality.Visual` bit is set
- Returns 0 when only `SensorModality.Acoustic` is set
- Returns 0 when slot not found in TargetMemory
- Returns 0 when Visual bit unset AND Acoustic bit set (all 4 combinations from spec)

**SC-P1-06-3 (`EqsTopScore`):**
- Returns `GetTop().Score` when `IsReady && Count > 0`
- Returns 0 when no child entity with matching BlueprintId exists
- Returns 0 when child exists but `buf.IsReady == false` (LastUpdateTick == 0)

**SC-P1-06-4 (`DistanceToContext`):**
- Returns 1.0 at distance 0 (self and context at same position)
- Returns 0.0 at distance = MaxRange
- Returns approximately 0.5 at distance = MaxRange/2 (monotonic interpolation)
- Clamps: distance > MaxRange returns 0.0

**SC-P1-06-5 (`IsAssignedTarget`):**
- Returns 1 when commander's ThreatMatrixAssignmentState[memberSlot].AssignedTargetHandle == ctx.Context.PackedValue
- Returns 0 when assignment is to a different target

**Additional (recommended):**
- `WeaponHasAmmo`: returns 1 when Ammo > 0, 0 when Ammo == 0
- `HealthFraction`: returns 0 when Max == 0
- `ContactThreatLevel`: returns ThreatScores[i] clamped; 0 when contact not in TargetMemory
- `HaveLiveTarget`: 1 when Count > 0, 0 when Count == 0
- `Constant`: returns exactly ctx.Params.MaxRange
- `StandardInputIds_HashValues_MatchFnv1a32`: all hash constants are correct

**Test quality bar:**
- Tests MUST use `UtilityTestWorld` and register readers via `StandardInputs.RegisterAll()` + clear in teardown.
- Tests must assert specific numeric values, not just "non-zero".
- The `HasLineOfSight` test MUST cover all 4 combinations (Visual×Acoustic) per SC-P1-06-2.

---

## ⚠️ Quality Standards

**NOT ACCEPTABLE:**
- `AllyAdvancingNearby` that silently returns garbage instead of a documented 0.
- `EnemyStrengthRatio` that divides by zero when health is 0 (must have safe guards).
- `TryFindEqsChild` that allocates (no LINQ; use a manual loop over a query).
- `IsAssignedTarget` that panics when `UnitSubordinate` is absent (return 0 gracefully).
- Hash constants that are computed wrong (verify each one with the `Fnv1a32` test).

**REQUIRED:**
- All readers return values in [0, 1] — add a `Debug.Assert(result >= 0f && result <= 1f)` at the end of each reader body in DEBUG builds.
- Readers that access a missing component must safely return 0 (check `repo.HasComponent<T>` before reading).
- `ThreatMatrixAssignmentState` must fit in `sizeof(ThreatMatrixAssignmentState) <= 1024` — assert this in a test.

---

## 📊 Report Requirements

Submit `.dev/utility-ai/reports/BATCH-04-REPORT.md` with:

1. **Implementation Summary:** Files created/modified, key design decisions.
2. **Hash constant table:** For every `StandardInputIds` const, show the `Name` and computed hash value (e.g. `"AmmoFraction" → 0xXXXX`).
3. **Test Results:** `dotnet test` output (pass/fail counts). All prior tests plus new tests must pass.
4. **SC Checklist:** Confirm SC-P1-06-1 through SC-P1-06-5 each map to named tests.
5. **Developer Insights:**
   - Issues encountered and resolutions.
   - Any weak points spotted in the codebase.
   - Design decisions beyond the spec.
6. **Deferred items** (if any) with justification.
