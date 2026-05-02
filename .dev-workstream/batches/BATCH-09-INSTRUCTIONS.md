# BATCH-09: Geographic P1 Fix + Physics P2 Fixes + Phase 5 Combat Start (BCS-P5-T1, T2)

**Batch Number:** BATCH-09  
**Tasks:** CORRECTIVE-P1 (DEBT-025), CORRECTIVE-P2 (DEBT-021, 026, 027, 028 + P3 cleanups), BCS-P5-T1, BCS-P5-T2  
**Phase:** Corrective + Phase 5 — FDP.Toolkit.Combat (start)  
**Estimated Effort:** 10–13 hours  
**Priority:** HIGH (P1 geographic bug blocks SimHost egress correctness)  
**Dependencies:** BATCH-08 ✅ (Phase 4 complete)

---

## 📋 Onboarding & Workflow

### Required Reading (IN ORDER)

1. **BATCH-08 Review (full):** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reviews\BATCH-08-REVIEW.md`  
   — Read both the production code and test quality sections. All issues found there are in scope.
2. **DEBT-TRACKER.md:** Focus on DEBT-021, 025, 026, 027, 028.
3. **CODE-STANDARDS.md:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\guides\CODE-STANDARDS.md`
4. **Task Details BCS-P5-T1, T2:** `FDP/Docs/projects/behavior-control/TASK-DETAIL.md` — lines 871–946
5. **GeoTransform conventions:** `FDP/Toolkits/Fdp.Toolkit.Geographic/Components/GeoTransform.cs` — read orientation convention comments
6. **SimTransformBridgeSystem current code:** `FDP/Toolkits/Fdp.Toolkit.Geographic/Systems/SimTransformBridgeSystem.cs`
7. **SimTransformBridgeSystemTests current tests:** `FDP/Toolkits/Fdp.Toolkit.Geographic.Tests/SimTransformBridgeSystemTests.cs`

### Source Locations

| Area | Path |
|---|---|
| **P1 fix (production)** | `FDP/Toolkits/Fdp.Toolkit.Geographic/Systems/SimTransformBridgeSystem.cs` |
| **P1 fix (tests)** | `FDP/Toolkits/Fdp.Toolkit.Geographic.Tests/SimTransformBridgeSystemTests.cs` |
| **P2 fixes (solver)** | `FDP/Toolkits/FDP.Toolkit.Physics/Systems/RaycastSolverSystem.cs` |
| **P2 fix (constants)** | `FDP/Toolkits/FDP.Toolkit.Physics/PhysicsConstants.cs` |
| **P2 fix (test)** | `FDP/Toolkits/FDP.Toolkit.Physics.Tests/Intersection2DTests.cs` |
| **P3 fix (test comment)** | `FDP/Toolkits/FDP.Toolkit.Physics.Tests/RaycastSolverSystemTests.cs` |
| **New project** | `FDP/Toolkits/FDP.Toolkit.Combat/FDP.Toolkit.Combat.csproj` ← create |
| **New test project** | `FDP/Toolkits/FDP.Toolkit.Combat.Tests/FDP.Toolkit.Combat.Tests.csproj` ← create |
| **Combat components** | `FDP/Toolkits/FDP.Toolkit.Combat/Components/CombatComponents.cs` ← create |
| **Combat events** | `FDP/Toolkits/FDP.Toolkit.Combat/Events/CombatEvents.cs` ← create |
| **AimAndFireExecutor** | `FDP/Toolkits/FDP.Toolkit.Combat/Executors/AimAndFireExecutor.cs` ← create |

### Build & Test Commands

```powershell
cd D:\Work\IOS-IG-SimHost-FDP\FDP
dotnet build FDP.sln
dotnet test FDP.sln
dotnet test Toolkits/Fdp.Toolkit.Geographic.Tests/   # must gain 5 new tests
dotnet test Toolkits/FDP.Toolkit.Physics.Tests/       # all 16 must stay green + Test 4 geometry change
dotnet test Toolkits/FDP.Toolkit.Combat.Tests/        # new suite
```

### Report Submission

`D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reports\BATCH-09-REPORT.md`

---

## 🔄 MANDATORY WORKFLOW

1. Corrective DEBT-025 — `SimTransformBridgeSystem` pitch/roll fix ✅
2. Corrective DEBT-021 — `RaycastSolverSystem` bounds cap ✅
3. Corrective DEBT-026 — 64-candidate constant + doc comment ✅
4. Corrective DEBT-027 — document raw-index LOS gap in `TargetVisibleEvent` (see task below) ✅
5. Corrective DEBT-028 — fix `Intersection2DTests` Test 4 geometry ✅
6. P3 cleanups (`QueryExpansionMeters` → float, stale comment) ✅
7. BCS-P5-T1 — Combat component types (`WeaponState`, `Health`, `BallisticProjectile`) + events ✅
8. BCS-P5-T2 — `AimAndFireExecutor` + tests ✅
9. Full solution green ✅

---

## ✅ Tasks

### Task 0 (Corrective P1): `SimTransformBridgeSystem` — pitch and roll (DEBT-025)

**File:** `FDP/Toolkits/Fdp.Toolkit.Geographic/Systems/SimTransformBridgeSystem.cs`

**Convention (from `GeoTransform.cs`):**
- `HeadingDeg`: compass [0, 360), 0=North, 90=East, clockwise — **already correct**
- `PitchDeg`: +ve = nose up
- `RollDeg`: +ve = right wing down (clockwise looking forward)

**Current bug:** Lines setting `PitchDeg = 0f` and `RollDeg = 0f` in `UpdateEntity`.

**Fix:** Add a `public static void RotationToPitchRollDeg(Quaternion rotation, out float pitchDeg, out float rollDeg)` static method alongside the existing `RotationToHeadingDeg`. The existing convention uses **UnitX as the forward axis** (confirmed by `RotationToHeadingDeg` which does `Vector3.Transform(Vector3.UnitX, rotation)`). Keep it consistent.

**Math derivation** (UnitX-forward, UnitZ-up ENU):
```csharp
// Body axes after rotation (matching RotationToHeadingDeg convention):
Vector3 forward = Vector3.Transform(Vector3.UnitX, rotation); // body forward
Vector3 up      = Vector3.Transform(Vector3.UnitZ, rotation); // body up
Vector3 right   = Vector3.Transform(Vector3.UnitY, rotation); // body right (if Y = right in body frame)
                                                               // adjust if local Y is left

// Pitch: how much the forward vector tilts out of the horizontal plane
// forward.Z = sin(pitch) in ENU (Z is world-up)
// Clamp to avoid asin domain errors from floating-point noise
pitchDeg = MathF.Asin(Math.Clamp(forward.Z, -1f, 1f)) * (180f / MathF.PI);

// Roll: the angle of the body's up/right axes relative to world-vertical
// Project bodyUp into the plane perpendicular to forward (removes pitch component)
// Then measure its tilt from world-up
// Simplest correct formula: atan2(bodyRight.Z, bodyUp.Z)
// When level: bodyRight.Z = 0, bodyUp.Z = 1 → roll = 0
// When right wing down 90°: bodyRight.Z = -1, bodyUp.Z = 0 → roll = -90°... verify sign
// GeoTransform convention: +ve = right wing down
// Adjust sign to match convention.
rollDeg = MathF.Atan2(right.Z, up.Z) * (180f / MathF.PI);
// VERIFY: right-wing-down means the right wing (body +Y side) goes toward ground (−Z).
// When rolled 90° right: up = world -X (or -Y), right = world -Z → right.Z = -1, up.Z ≈ 0
// atan2(-1, 0) = -90°. But convention says +ve = right wing down, so negate:
rollDeg = -MathF.Atan2(right.Z, up.Z) * (180f / MathF.PI);
```

> **Important:** Work through the sign carefully with your tests. Write the tests FIRST, then implement. The sign of `rollDeg` depends on what "right" means in your body frame (UnitY vs -UnitY). If the CarKinematicsSystem uses UnitX-forward with UnitY-right, then `Vector3.Transform(Vector3.UnitY, rotation)` gives the right wing direction. If UnitY is left, negate. Let the tests drive this.

**Call from `UpdateEntity`:**
```csharp
float headingDeg = RotationToHeadingDeg(tf.Rotation);
RotationToPitchRollDeg(tf.Rotation, out float pitchDeg, out float rollDeg);

var geoTf = new GeoTransform
{
    Latitude   = lat,
    Longitude  = lon,
    Altitude   = (float)alt,
    HeadingDeg = headingDeg,
    PitchDeg   = pitchDeg,
    RollDeg    = rollDeg,
};
```

**New tests in `SimTransformBridgeSystemTests.cs`:**

```csharp
[Fact]
void RotationToPitchRollDeg_LevelFlight_ReturnsBothZero()
// Quaternion.Identity → pitch = 0f ± 0.1, roll = 0f ± 0.1

[Fact]
void RotationToPitchRollDeg_NoseUp30_ReturnsPitchPositive30()
// Pitch 30° nose-up: rotate 30° around the body-right axis (UnitY if Y=right, or -UnitY if Y=left)
// → pitchDeg ≈ +30, rollDeg ≈ 0
// Use Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 6f) if UnitX-forward, Y=left
// OR Quaternion.CreateFromAxisAngle(-Vector3.UnitY, MathF.PI / 6f)
// Determine by running level test first, then figure out which axis-angle tilts the nose up.
// Assert: Assert.InRange(pitchDeg, 28f, 32f), Assert.InRange(rollDeg, -1f, 1f)

[Fact]
void RotationToPitchRollDeg_NoseDown30_ReturnsPitchNegative30()
// Pitch 30° nose-down → pitchDeg ≈ −30

[Fact]
void RotationToPitchRollDeg_RightWingDown45_ReturnsRollPositive45()
// Roll 45° right-wing-down → rollDeg ≈ +45, pitchDeg ≈ 0
// Convention: +ve = right wing down (from GeoTransform.cs)

[Fact]
void RotationToPitchRollDeg_Combined_PitchAndRollIndependent()
// 20° pitch-up AND 30° right-wing-down (compound rotation)
// → pitchDeg ≈ +20, rollDeg ≈ +30 (within ±2° tolerance on each)

[Fact]
void UpdateEntity_GeoTransform_PitchDeg_NonZero_WhenEntityIsPitched()
// Integration test: entity has SimTransform with 20°-nose-up rotation applied
// Run UpdateEntity (via the system) → GeoTransform.PitchDeg != 0f
// This is the regression guard proving the fix: if PitchDeg were still hardcoded to 0f this fails
```

> **Note on the integration test:** You need an `IGeographicTransform` mock for this test. Look at how `GeographicModuleTests.cs` or `SimTransformBridgeSystemTests.cs` are wired — use the same pattern (probably a mock/stub `IGeographicTransform` that returns a fixed `(lat, lon, alt)`).

---

### Task 1 (Corrective P2): `RaycastSolverSystem` — bounds cap + candidate constant (DEBT-021, DEBT-026)

**File:** `FDP/Toolkits/FDP.Toolkit.Physics/Systems/RaycastSolverSystem.cs`  
**File:** `FDP/Toolkits/FDP.Toolkit.Physics/PhysicsConstants.cs`

**1a — Bounds cap (DEBT-021):**  
Replace line `int count = batch.Count;` with:
```csharp
// Cap to array size — prevents IndexOutOfRangeException if upstream overflows the batch.
// The capacity is capped rather than thrown so excess rays are silently dropped rather than
// crashing. A Debug.Assert at the fill site would alert during development.
int count = System.Math.Min(batch.Count, PhysicsConstants.RaycastBatchCapacity);
```

**1b — Candidate buffer constant (DEBT-026):**  
In `PhysicsConstants.cs` add:
```csharp
/// <summary>
/// Maximum number of broadphase candidates inspected per ray per frame.
/// Entities beyond this limit are silently dropped from narrow-phase testing.
/// In practise 64 is sufficient for typical scenario densities; raise if you
/// observe missed hits in high-density areas.
/// </summary>
public const int MaxBroadphaseCandidates = 64;
```
In `RaycastSolverSystem.cs` replace `stackalloc (Entity, Vector2)[64]` with `stackalloc (Entity, Vector2)[PhysicsConstants.MaxBroadphaseCandidates]`.

**1c — `QueryExpansionMeters` type fix (P3):**  
In `PhysicsConstants.cs` rename to `QueryExpansionRadius` and change type to `float`:
```csharp
public const float QueryExpansionRadius = 5f;
```
Update the one use site in `RaycastSolverSystem.cs` accordingly.

---

### Task 2 (Corrective P2): Document LOS raw-index gap (DEBT-027)

**File:** `FDP/Toolkits/FDP.Toolkit.Physics/Systems/HitResolutionSystem.cs`

This is **documentation only** in this batch — a full fix requires changing the RayId format (which couples to `LosRequestBatchingSystem` and `TargetVisibleEvent` in Perception). Add an XML comment in `HitResolutionSystem.OnUpdate` at the LOS event publishing block:

```csharp
// DEBT-027: TargetVisibleEvent carries raw int indices (ObserverEntityIndex, TargetEntityIndex).
// These were packed into RayId as raw ints by LosRequestBatchingSystem.
// If an entity is destroyed and its index recycled between LOS submission and event consumption
// by ThreatEvaluationSystem, the wrong entity's threat memory could be updated.
// Full fix: carry full Entity handles (Index + Generation) through the LOS event pipeline.
// Deferred to when LosRequestBatchingSystem is reworked.
```

No behaviour change this batch.

---

### Task 3 (Corrective P2): Fix `Intersection2DTests` Test 4 geometry (DEBT-028)

**File:** `FDP/Toolkits/FDP.Toolkit.Physics.Tests/Intersection2DTests.cs`

Replace Test 4 (`ReturnsTMin_WhenTwoIntersections`) with a geometry where the entry and exit t values are well separated so the test actually proves the minimum is returned:

**New geometry:** Ray from `(-10f, 0f)` to `(10f, 0f)`. Circle at `(0f, 0f)`, radius `4f`.
- Entry at x = −4 → t = (−10 − (−4)) / (10 − (−10)) = 6/20 = **0.30**
- Exit  at x = +4 → t = (−10 − (+4)) / ... wait: t = (entryX − startX) / (endX − startX) = (−4 − (−10)) / 20 = 6/20 = 0.30; exit = (4 − (−10)) / 20 = 14/20 = 0.70

Assert: `Assert.InRange(t, 0.25f, 0.35f)` — this window (0.25–0.35) does NOT contain the exit t (0.70), so the test proves the entry is returned, not the exit. This is the important difference from Test 1.

Also add a comment: `// Exit t ≈ 0.70 — asserting [0.25, 0.35] proves the entry (not exit) is returned.`

---

### Task 4 (P3 Cleanup): Remove stale comment + rename constant

**File:** `FDP/Toolkits/FDP.Toolkit.Physics.Tests/RaycastSolverSystemTests.cs`  
Remove the line: `// Need to dispose farEntity's grid addition doesn't create issues.` (line 178)  
This is a copy-paste artefact from the test authoring.

---

### Task 5: `Combat Component Types` + `Combat Events` (BCS-P5-T1, T2)

**New project:** `FDP/Toolkits/FDP.Toolkit.Combat/FDP.Toolkit.Combat.csproj`  
References: `Fdp.Kernel`, `FDP.Toolkit.Behavior`, `FDP.Toolkit.Physics` (for `HitEvent` migration, see below), `FDP.Toolkit.CarKinem`

**Task Definition:** [TASK-DETAIL.md §BCS-P5-T1 & T2](../../../Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p5-t1--combat-component-types) — lines 871–917

#### `HitEvent` migration (DEBT-023 partial resolution)

Now that `FDP.Toolkit.Combat` exists, migrate `HitEvent` from `FDP.Toolkit.Physics/Events/PhysicsEvents.cs` to `FDP.Toolkit.Combat/Events/CombatEvents.cs`. Update `FDP.Toolkit.Physics.csproj` to reference `FDP.Toolkit.Combat` instead of defining `HitEvent` locally. Update `HitResolutionSystem.cs` using statement. The event ID (`5001`) stays the same. Verify all tests still compile and pass after the move.

#### `CombatComponents.cs`

```csharp
/// <summary>
/// State of a weapon attachment (gun, launcher, etc.).
/// Unmanaged; fits in one cache line.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct WeaponState
{
    /// <summary>Current ammo count. Fire is refused when 0.</summary>
    public int Ammo;
    /// <summary>Remaining cooldown ticks before the next shot is allowed.</summary>
    public int CooldownTicksRemaining;
    /// <summary>Muzzle velocity in m/s (copied from behavior at init time).</summary>
    public float MuzzleVelocity;
}

/// <summary>
/// Hit-point pool. Health.Current <= 0 means the entity is destroyed/defeated.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Health
{
    public float Current;
    public float Max;
}

/// <summary>
/// Marks a bullet entity. Added by FireProcessingSystem on spawn.
/// PreviousPosition is updated by BallisticsSystem each frame to build the swept segment.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct BallisticProjectile
{
    /// <summary>Entity that fired this bullet (excluded from self-hit).</summary>
    public Entity Shooter;
    /// <summary>Bullet's SimTransform.Position from the PREVIOUS frame. Set to origin on spawn.</summary>
    public Vector3 PreviousPosition;
    /// <summary>Damage dealt on hit.</summary>
    public float Damage;
    /// <summary>Tick at which the bullet was spawned (for lifetime check).</summary>
    public uint SpawnTick;
}
```

**⚠️ Phase 0 Adaptation:** See TASK-DETAIL.md §BCS-P5-T1 lines 883–897 — `BallisticProjectile.Velocity` is removed (bullet movement handled by `SimVelocity` via `LinearKinematicsSystem`). Only `PreviousPosition` (Vector3) is kept for swept-segment raycasting.

#### `CombatEvents.cs`

```csharp
[EventId(CombatConstants.FireRequestEventId)]  // 5001 — or whatever ID is specified in DESIGN.md
public struct FireRequestEvent
{
    public Entity   Shooter;
    public Entity   Target;
    public Vector3  Origin;       // shooter position
    public Vector3  Direction;    // normalised
}

// HitEvent migrated here from FDP.Toolkit.Physics:
[EventId(CombatConstants.HitEventId)]           // keep same ID as before
public struct HitEvent
{
    public Entity HitEntity;
    public int    BulletIndex;
    public float  HitT;
}
```

Add `CombatConstants.cs` with `FireRequestEventId` and `HitEventId` values.

#### Tests (new file `CombatComponentTests.cs`):

```csharp
[Fact]
void WeaponState_IsUnmanagedValueType()
// Assert.True(typeof(WeaponState).IsValueType)

[Fact]
void Health_DefaultCurrentIsZero()
// var h = new Health(); Assert.Equal(0f, h.Current)

[Fact]
void BallisticProjectile_ContainsEntityShooter_NotRawIndex()
// typeof(BallisticProjectile).GetField("Shooter").FieldType == typeof(Entity)
// Guards against accidentally reverting to a raw int

[Fact]
void BallisticProjectile_HasPreviousPosition_NotVelocity()
// typeof(BallisticProjectile).GetField("Velocity") == null  (removed in Phase 0 adaptation)
// typeof(BallisticProjectile).GetField("PreviousPosition") != null

[Fact]
void FireRequestEvent_HasEventIdAttribute()
// typeof(FireRequestEvent).GetCustomAttribute<EventIdAttribute>() != null

[Fact]
void HitEvent_HasSameIdAsPhysicsToolkitHitEvent()
// (after migration) — CombatConstants.HitEventId == 5001 (or the agreed ID)
// This guards against the ID changing during migration
```

---

### Task 6: `AimAndFireExecutor` (BCS-P5-T2)

**File:** `FDP/Toolkits/FDP.Toolkit.Combat/Executors/AimAndFireExecutor.cs`  
**Task Definition:** [TASK-DETAIL.md §BCS-P5-T2/T3](../../../Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p5-t3--aimandfireexecutor) — lines 921–946

Implements `IActionExecutor<WeaponChannel>` (or the equivalent weapon-specific channel type — check what the Behavior toolkit exposes; it may be `LocomotionChannel` for weapons or a separate `WeaponChannel`. If `WeaponChannel` doesn't exist yet, create it in `FDP.Toolkit.Behavior/Components/BehaviorComponents.cs` following the same pattern as `LocomotionChannel`).

**`OnEnter`:** Read `AimAndFireParams` from the channel. Store target entity. Set `channel.Status = Running`.

**`Execute` (each tick):**
1. Check `world.IsAlive(params.Target)` — if not alive → `Status = Success` (target is dead; mission complete).
2. Check `WeaponState.Ammo == 0` → `Status = Failure`.
3. Check `WeaponState.CooldownTicksRemaining > 0` → decrement cooldown, `Status = Running`.
4. Otherwise: compute aim direction from `SimTransform` (NOT `VehicleState`):
   ```csharp
   Vector3 origin    = world.GetComponent<SimTransform>(entity).Position;
   Vector3 targetPos = world.GetComponent<SimTransform>(params.Target).Position;
   Vector3 direction = Vector3.Normalize(targetPos - origin);
   ```
5. Publish `FireRequestEvent { Shooter=entity, Target=params.Target, Origin=origin, Direction=direction }`.
6. Decrement `WeaponState.Ammo`, set `CooldownTicksRemaining = AimAndFireParams.CooldownTicks`.
7. `Status = Running`.

**`OnExit`:** No state to clean up.

**Tests (new file `AimAndFireExecutorTests.cs`):**

```csharp
[Fact]
void AimAndFire_EmitsFireRequestEvent_WhenConditionsAreMet()
// Ammo=5, CooldownTicksRemaining=0, Target alive at known position
// OnEnter + Execute
// Consume<FireRequestEvent>() → Length == 1
// Direction is normalised unit vector pointing from entity to target
// Assert: Math.Abs(Vector3.Length(evt.Direction) - 1f) < 0.001f (direction is normalised)
// Assert: Ammo decremented to 4

[Fact]
void AimAndFire_DoesNotFire_WhenCooldownActive()
// CooldownTicksRemaining=5
// Execute → no FireRequestEvent published
// Status = Running, cooldown decremented to 4

[Fact]
void AimAndFire_ReportsFailure_WhenAmmoZero()
// Ammo=0, CooldownTicksRemaining=0
// Execute → Status = Failure
// No FireRequestEvent published

[Fact]
void AimAndFire_ReportsSuccess_WhenTargetDead()
// Target entity alive on OnEnter
// DestroyEntity(target)
// Execute → Status = Success (dead target = objective complete)
// No FireRequestEvent published (IsAlive check fires first)

[Fact]
void AimAndFire_DecrementsCooldown_EachTick_UntilCanFire()
// Cooldown=3 → Execute tick 1: cooldown=2, Running; tick 2: cooldown=1, Running;
// tick 3: cooldown=0, Running; tick 4: now fires, FireRequestEvent emitted
// Assert: event emitted on tick 4, not before
```

> **Test 5 (`DecrementsCooldown`) is the most important test for this executor.** It proves the multi-tick gating works correctly — this is harder to fake than a simple one-tick test. Use a loop and check the event bus after every tick.

---

## 🧪 Testing Requirements

- **Minimum 17 new tests:** 6 Geographic pitch/roll + 1 test geometry fix + 6 Combat components + 5 AimAndFireExecutor.
- **All existing tests must remain green**, including the 16 Physics tests and the 14 Geographic tests.
- **`RotationToPitchRollDeg` tests must use `Assert.InRange`** with a tolerance of ±1° (float). Do NOT use `Assert.Equal` with float precision — Euler extraction has floating-point noise.
- **Write pitch/roll tests BEFORE implementing** — let the sign/convention be driven by the test assertions, not assumption.
- **`AimAndFireExecutor` test 1 must verify the emitted direction is normalised** (unit vector length ≈ 1.0) — this ensures `Vector3.Normalize` was called, not just the raw difference.

---

## ⚠️ Quality Standards

**❗ `RotationToPitchRollDeg` must be `public static`** — consistent with `RotationToHeadingDeg`; tests call it directly without instantiating the system.

**❗ The UnitX-forward convention must be consistent** — `RotationToPitchRollDeg` must use `Vector3.Transform(Vector3.UnitX, rotation)` for forward, matching `RotationToHeadingDeg`. Do not switch conventions mid-class.

**❗ `Asin` input must be clamped to `[-1, 1]`** — floating-point quaternion normalisation drift can produce `forward.Z` slightly outside this range, causing `NaN`. Use `MathF.Asin(Math.Clamp(forward.Z, -1f, 1f))`.

**❗ `PhysicsConstants.MaxBroadphaseCandidates` must be used** — no more raw `64` literals.

**❗ `PhysicsConstants.QueryExpansionRadius` (float)** — rename done; update use site.

**❗ `AimAndFire` uses `SimTransform` for position, NOT `VehicleState`** — zero occurrences of `VehicleState.Position` in Combat toolkit.

**❗ `HitEvent` migration** — after moving to Combat, remove `PhysicsEvents.cs` from the Physics project entirely. All references updated. Tests still green.

---

## 📊 Report Requirements

Submit `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reports\BATCH-09-REPORT.md`:

- **Test results:** Full `dotnet test FDP.sln` summary.
- **Q1 (`RotationToPitchRollDeg` sign convention):** What is the body-frame right axis in this codebase — `UnitY` or `-UnitY`? How did you determine it? Did you have to negate `rollDeg`? Show the test that confirmed the sign.
- **Q2 (`HitEvent` migration):** What changes were required in `FDP.Toolkit.Physics.csproj` and `HitResolutionSystem.cs` after moving `HitEvent` to Combat? Were there any circular reference issues? How did you resolve them?
- **Q3 (`WeaponChannel`):** Does `FDP.Toolkit.Behavior` already define a `WeaponChannel` component? If not, did you create it? What fields does it have?
- **Q4 (DEBT-027 scope):** Now that you've implemented `AimAndFireExecutor` and understand the full combat flow end-to-end, how serious is the DEBT-027 raw-index LOS gap in practice? Under what specific scenario would a wrong entity's threat memory be updated?

---

## 🎯 Success Criteria

- [ ] **DEBT-025** — `RotationToPitchRollDeg` static method added; `UpdateEntity` calls it; 6 new tests pass (including integration test)
- [ ] **DEBT-021** — `Math.Min` cap applied in `RaycastSolverSystem`
- [ ] **DEBT-026** — `PhysicsConstants.MaxBroadphaseCandidates = 64` constant added; used in `RaycastSolverSystem`
- [ ] **DEBT-027** — Comment added in `HitResolutionSystem` documenting the raw-index gap
- [ ] **DEBT-028** — `Intersection2DTests` Test 4 uses distinct geometry; entry/exit t values differ by > 0.3
- [ ] **DEBT-023** — `HitEvent` moved to `FDP.Toolkit.Combat`; Physics no longer defines it
- [ ] **P3** — `QueryExpansionMeters` → `QueryExpansionRadius: float`; stale test comment removed
- [ ] **BCS-P5-T1** — `WeaponState`, `Health`, `BallisticProjectile`, `FireRequestEvent`, `HitEvent` (migrated); 6 component tests pass
- [ ] **BCS-P5-T2** — `AimAndFireExecutor`; 5 tests pass including multi-tick cooldown gating
- [ ] **`FDP.Toolkit.Combat` + `FDP.Toolkit.Combat.Tests` added to `FDP.sln`**
- [ ] **Full solution build:** 0 errors
- [ ] **All tests green** (existing 16 Physics, 14 Geographic + 17 new minimum)
- [ ] **Report submitted**

---

## 📚 Reference Materials

- **BATCH-08 Review:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reviews\BATCH-08-REVIEW.md`
- **DEBT-TRACKER.md:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\DEBT-TRACKER.md`
- **CODE-STANDARDS.md:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\guides\CODE-STANDARDS.md`
- **Task Details BCS-P5-T1–T2:** `FDP/Docs/projects/behavior-control/TASK-DETAIL.md` — lines 871–946
- **GeoTransform conventions:** `FDP/Toolkits/Fdp.Toolkit.Geographic/Components/GeoTransform.cs`
- **SimTransformBridgeSystem (current):** `FDP/Toolkits/Fdp.Toolkit.Geographic/Systems/SimTransformBridgeSystem.cs`
- **SimComponents:** `FDP/Kernel/Fdp.Kernel/CoreComponents/SimComponents.cs`
- **Phase 0 Adaptations for Combat:** TASK-DETAIL.md §BCS-P5-T1 lines 883–897, §BCS-P5-T3 lines 927–934
