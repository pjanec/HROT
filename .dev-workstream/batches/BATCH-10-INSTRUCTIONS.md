# BATCH-10: Phase 5 Combat Continuation — FireProcessingSystem + BallisticsSystem + DamageSystem (BCS-P5-T4, T5)

**Batch Number:** BATCH-10  
**Tasks:** BCS-P5-T4 (FireProcessingSystem + BallisticsSystem), BCS-P5-T5 (DamageSystem)  
**Phase:** Phase 5 — FDP.Toolkit.Combat (completion)  
**Estimated Effort:** 10–13 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-09 ✅ (Combat project bootstrapped; WeaponState, Health, BallisticProjectile, AimAndFireExecutor all in place)

---

## 📋 Onboarding & Workflow

### Required Reading (IN ORDER)

1. **BATCH-09 Review:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reviews\BATCH-09-REVIEW.md`
2. **DEBT-TRACKER.md:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\DEBT-TRACKER.md` — DEBT-024, DEBT-027 are background concerns; no correctives this batch.
3. **CODE-STANDARDS.md:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\guides\CODE-STANDARDS.md`
4. **Task Details BCS-P5-T4, T5:** `FDP/Docs/projects/behavior-control/TASK-DETAIL.md` — read Section "Phase 5 — BCS-P5-T4" and "BCS-P5-T5" in full.
5. **Existing Combat code (read before writing anything):**
   - `FDP/Toolkits/FDP.Toolkit.Combat/Components/CombatComponents.cs` — `WeaponState`, `Health`, `BallisticProjectile`
   - `FDP/Toolkits/FDP.Toolkit.Combat/Events/CombatEvents.cs` — `FireRequestEvent`, `HitEvent`
   - `FDP/Toolkits/FDP.Toolkit.Combat/Executors/AimAndFireExecutor.cs` — how the executor fires
   - `FDP/Toolkits/FDP.Toolkit.Combat/CombatConstants.cs` — event IDs
6. **Physics pipeline (understand before implementing BallisticsSystem):**
   - `FDP/Toolkits/FDP.Toolkit.Physics/Components/PhysicsComponents.cs` — `RaycastRequest`, `RaycastBatchData`
   - `FDP/Toolkits/FDP.Toolkit.Physics/PhysicsConstants.cs` — `MaxBroadphaseCandidates`, `PackBulletRayId`
   - `FDP/Toolkits/FDP.Toolkit.Physics/Systems/RaycastSolverSystem.cs` — how requests are resolved
7. **Kernel components:** `FDP/Kernel/Fdp.Kernel/CoreComponents/SimComponents.cs` — `SimTransform`, `SimVelocity`

### Source Locations

| Area | Path |
|---|---|
| Existing Combat project | `FDP/Toolkits/FDP.Toolkit.Combat/` |
| Existing Combat tests | `FDP/Toolkits/FDP.Toolkit.Combat.Tests/` |
| **New systems** | `FDP/Toolkits/FDP.Toolkit.Combat/Systems/` ← create directory |
| Physics | `FDP/Toolkits/FDP.Toolkit.Physics/` |

### Build & Test

```powershell
cd D:\Work\IOS-IG-SimHost-FDP\FDP
dotnet build FDP.sln
dotnet test FDP.sln
dotnet test Toolkits/FDP.Toolkit.Combat.Tests/    # must gain 12+ new tests
```

### Report Submission

`D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reports\BATCH-10-REPORT.md`

---

## 🔄 MANDATORY WORKFLOW

1. Task 1 — FireProcessingSystem: implement → tests pass ✅
2. Task 2 — BallisticsSystem: implement → tests pass ✅
3. Task 3 — DamageSystem: implement → tests pass ✅
4. Full solution green before submitting report ✅

**Do not move to next task until all previous tests are green.**

---

## ✅ Tasks

### Task 1: `FireProcessingSystem` (BCS-P5-T4, first half)

**File:** `FDP/Toolkits/FDP.Toolkit.Combat/Systems/FireProcessingSystem.cs` ← NEW  
**Task Definition:** TASK-DETAIL.md §BCS-P5-T4 — read the full section  
**Execution phase:** `InputSystemGroup`, after the dispatcher group (before physics)

**Responsibility:** Consume `FireRequestEvent`s and spawn bullet entities.

**Per event:**
1. Create a new entity (`world.CreateEntity()`).
2. Compute bullet initial velocity: `direction * WeaponState.MuzzleVelocity` from the shooter's `WeaponState`. Direction comes from `FireRequestEvent.Direction`.
3. `AddComponent<SimTransform>` with `Position = evt.Origin`, `Rotation = Quaternion.Identity`.
4. `AddComponent<SimVelocity>` with `Linear = direction * muzzleVelocity`, `Angular = Vector3.Zero`.
5. `AddComponent<BallisticProjectile>` with `Shooter = evt.Shooter`, `PreviousPosition = evt.Origin`, `Damage = [from doctrine/config — use a constant for now: CombatConstants.DefaultBulletDamage]`, `SpawnTick = world.CurrentTick`.
6. `AddComponent<PhysicsCollider>` with `Radius = CombatConstants.BulletColliderRadius`, `CollisionLayer = CombatConstants.BulletCollisionLayer`.

Add to `CombatConstants.cs`:
```csharp
public const float DefaultBulletDamage    = 25f;
public const float BulletColliderRadius   = 0.1f;
public const int   BulletCollisionLayer   = 2;    // bit 1 — distinct from entity layer (bit 0)
public const uint  BulletLifetimeTicks    = 120;  // ~2 seconds at 60 Hz
```

**Key note — MuzzleVelocity source:** `FireRequestEvent` does not carry muzzle velocity. Read it from the shooter's `WeaponState` component. If the shooter entity is no longer alive (edge case), skip the event.

**Tests (new file `FireProcessingSystemTests.cs`):**

```csharp
[Fact] void FireProcessing_SpawnsBulletEntity_WhenFireRequestReceived()
// Seed a FireRequestEvent. Run system. Query entities with BallisticProjectile.
// Assert: exactly 1 bullet entity created.
// Assert: bullet SimTransform.Position == FireRequestEvent.Origin.
// Assert: bullet BallisticProjectile.Shooter == FireRequestEvent.Shooter.

[Fact] void FireProcessing_SetsBulletVelocity_UsingMuzzleVelocityFromWeapon()
// Shooter WeaponState.MuzzleVelocity = 800f, evt.Direction = (1,0,0) (normalised).
// Assert: bullet SimVelocity.Linear == new Vector3(800f, 0f, 0f).

[Fact] void FireProcessing_SkipsEvent_WhenShooterEntityNotAlive()
// Source entity destroyed before system runs.
// Assert: no bullet entity created (no BallisticProjectile component anywhere).

[Fact] void FireProcessing_SetsPhysicsCollider_WithBulletLayer()
// Assert: bullet PhysicsCollider.CollisionLayer == CombatConstants.BulletCollisionLayer.
// Assert: bullet PhysicsCollider.Radius == CombatConstants.BulletColliderRadius.

[Fact] void FireProcessing_AddsPhysicsCollider_ToNewBullet()
// Integration: sets up shooter + fire event → runs system → bullet has PhysicsCollider.
```

---

### Task 2: `BallisticsSystem` (BCS-P5-T4, second half)

**File:** `FDP/Toolkits/FDP.Toolkit.Combat/Systems/BallisticsSystem.cs` ← NEW  
**Task Definition:** TASK-DETAIL.md §BCS-P5-T4 — read the Phase 0 Adaptation note carefully  
**Execution phase:** `SimulationSystemGroup`, **before** `LinearKinematicsSystem`

**Responsibility:** Per-frame housekeeping for all live bullet entities.

**Per bullet entity (query `With<BallisticProjectile>()`)**:

1. **Lifetime check:** if `world.CurrentTick - proj.SpawnTick >= CombatConstants.BulletLifetimeTicks`, destroy the entity (`world.DestroyEntity(entity)`) and continue.
2. **Submit raycast:** build a `RaycastRequest` for the swept segment and append it to `RaycastBatchData`:
   ```csharp
   var tf = world.GetComponent<SimTransform>(entity);
   var req = new RaycastRequest
   {
       Start        = proj.PreviousPosition,
       End          = tf.Position,          // current position (before this frame's physics advances it)
       RayId        = PhysicsConstants.PackBulletRayId(entity.Index),
       LayerMask    = ~CombatConstants.BulletCollisionLayer,  // hit everything EXCEPT other bullets
       IgnoreEntity = proj.Shooter,
   };
   // Append to batch (guard against overflow):
   ref var batch = ref world.GetSingleton<RaycastBatchData>();
   if (batch.Count < PhysicsConstants.RaycastBatchCapacity)
       batch.Requests[batch.Count++] = req;
   ```
3. **Update PreviousPosition:** `proj.PreviousPosition = tf.Position;` — record current position for next frame's sweep.

> ⚠️ **Phase 0 Adaptation note (TASK-DETAIL.md §BCS-P5-T4):** Bullet velocity is on `SimVelocity`, not `BallisticProjectile`. The previous-frame position is captured HERE (before `LinearKinematicsSystem` moves it). The ordering `BallisticsSystem → LinearKinematicsSystem → RaycastSolverSystem` guarantees the swept segment is always the exact distance traversed this frame.

**Tests (new file `BallisticsSystemTests.cs`):**

```csharp
[Fact] void Ballistics_SubmitsRaycastRequest_ForEachLiveBullet()
// Spawn 2 bullet entities. Set up RaycastBatchData singleton. Run system.
// Assert: batch.Count == 2.

[Fact] void Ballistics_UpdatesPreviousPosition_AfterRequest()
// Bullet at (5,0,0), PreviousPosition=(0,0,0). Run system.
// Assert: proj.PreviousPosition == (5,0,0) after run.

[Fact] void Ballistics_DestroysEntity_WhenLifetimeExpired()
// SpawnTick=0, CurrentTick=121 (> BulletLifetimeTicks=120).
// Run system. Assert: entity no longer alive (world.IsAlive(entity) == false).

[Fact] void Ballistics_DoesNotSubmitRaycast_WhenLifetimeExpired()
// Same as above. Assert: batch.Count == 0 (destroyed before submit).

[Fact] void Ballistics_IgnoresShooter_InRaycastRequest()
// Assert: request.IgnoreEntity == proj.Shooter.
// This guards against bullet self-hits.

[Fact] void Ballistics_RespectsCapacity_WhenBatchFull()
// Seed batch.Count = PhysicsConstants.RaycastBatchCapacity.
// Spawn one bullet. Run system.
// Assert: batch.Count == PhysicsConstants.RaycastBatchCapacity (not incremented, not crashed).
```

---

### Task 3: `DamageSystem` (BCS-P5-T5)

**File:** `FDP/Toolkits/FDP.Toolkit.Combat/Systems/DamageSystem.cs` ← NEW  
**Task Definition:** TASK-DETAIL.md §BCS-P5-T5 — read in full  
**Execution phase:** `InputSystemGroup`, after `HitResolutionSystem` (uses `[UpdateAfter(typeof(HitResolutionSystem))]`)

**Responsibility:** Consume `HitEvent`s and apply damage via `Health` component.

**Per event:**
1. Check `world.IsAlive(evt.HitEntity)` — if dead already, skip.
2. Check `world.HasComponent<Health>(evt.HitEntity)` — if no health component, skip (non-damageable entity).
3. Get the bullet entity by index: `world.GetEntityByIndex(evt.BulletIndex)` — check alive.
4. Get `BallisticProjectile` from bullet entity → extract `Damage`.
5. Apply: `health.Current -= damage`. Clamp to 0.
6. If `health.Current <= 0f`: destroy the hit entity (`world.DestroyEntity(evt.HitEntity)`).
7. Optionally destroy the bullet entity too (bullets are usually single-hit).

> ⚠️ **DEBT-027 note:** `HitEvent.BulletIndex` is a raw int (from `PackBulletRayId`). Use `world.GetEntityByIndex(evt.BulletIndex)` and immediately check `IsAlive` to guard against recycled slots.

**Tests (new file `DamageSystemTests.cs`):**

```csharp
[Fact] void Damage_ReducesHealth_WhenEntityIsHit()
// Entity with Health{Current=100f, Max=100f}. Bullet with Damage=25f.
// Seed HitEvent. Run DamageSystem.
// Assert: entity Health.Current == 75f.

[Fact] void Damage_DestroysEntity_WhenHealthDropsToZero()
// Health{Current=20f}. Damage=25f (lethal).
// Assert: world.IsAlive(entity) == false after run.

[Fact] void Damage_DoesNotApplyDamage_WhenEntityHasNoHealthComponent()
// Entity without Health. HitEvent for it.
// Assert: no crash; no Health component added.

[Fact] void Damage_SkipsHit_WhenEntityAlreadyDead()
// Destroy entity before run.
// Assert: no exception, system skips gracefully.

[Fact] void Damage_SkipsHit_WhenBulletEntityNotAlive()
// Bullet entity destroyed before DamageSystem runs.
// Assert: no damage applied (cannot get Damage without the bullet component).
```

---

## 🧪 Testing Requirements

- **Minimum 16 new tests:** 5 FireProcessing + 6 Ballistics + 5 Damage.
- **All 29 existing tests in `FDP.Toolkit.Combat.Tests` must remain green.**
- **No mocking of `EntityRepository`** — use real world with real components.
- **`Ballistics_RespectsCapacity` test is mandatory** — confirms the DEBT-021 pattern is applied at fill sites too.

---

## ⚠️ Quality Standards

**❗ `BallisticsSystem` execution phase must be before `LinearKinematicsSystem`** — document this with `[UpdateBefore(typeof(LinearKinematicsSystem))]` if the attribute exists; otherwise add a comment.

**❗ `DamageSystem` must use `[UpdateAfter(typeof(HitResolutionSystem))]`** — ordering enforced by attribute.

**❗ `GetEntityByIndex` generational check** — always `IsAlive` before using the entity. Raw-index access is the DEBT-027 pattern; mitigate it with the alive check.

**❗ No `VehicleState` references anywhere in this batch.**

**❗ `CombatConstants.DefaultBulletDamage` in constants file** — no raw `25f` literals in production code.

---

## 📊 Report Requirements

`D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reports\BATCH-10-REPORT.md`

**Q1:** What is the system execution order for the Combat pipeline this batch? List: FireProcessingSystem → BallisticsSystem → LinearKinematicsSystem → RaycastSolverSystem → HitResolutionSystem → DamageSystem. Confirm each system's declared phase/group and any `[UpdateAfter]`/`[UpdateBefore]` attributes you added.

**Q2:** `DamageSystem` uses `world.GetEntityByIndex(evt.BulletIndex)` — a raw-index lookup (DEBT-027 pattern). How did you mitigate the generational-safety gap in this system? Did you find any API other than `IsAlive` to do this safely?

**Q3:** Did you encounter any issues with `RaycastBatchData` access from `BallisticsSystem` — specifically, does the singleton exist at the time `BallisticsSystem` runs? what guard did you add?

**Q4:** Any additional design decisions beyond the spec? Edge cases discovered?

---

## 🎯 Success Criteria

- [ ] `FireProcessingSystem` — spawns bullet entities from `FireRequestEvent`; 5 tests pass
- [ ] `BallisticsSystem` — submits swept raycasts, updates PreviousPosition, lifetime culling; 6 tests pass
- [ ] `DamageSystem` — applies damage from `HitEvent`, destroys on zero health; 5 tests pass
- [ ] Full solution: 0 errors
- [ ] All tests green (Combat.Tests 27+ total; full solution clean)
- [ ] Report submitted

---

## 📚 Reference Materials

- **Task Detail:** `FDP/Docs/projects/behavior-control/TASK-DETAIL.md` §BCS-P5-T4 and §BCS-P5-T5
- **Existing Combat:** `FDP/Toolkits/FDP.Toolkit.Combat/` (all files)
- **Physics pipeline:** `FDP/Toolkits/FDP.Toolkit.Physics/Components/PhysicsComponents.cs`, `PhysicsConstants.cs`
- **BATCH-09 Review:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\reviews\BATCH-09-REVIEW.md`
- **CODE-STANDARDS.md:** `D:\Work\IOS-IG-SimHost-FDP\FDP\.dev-workstream\guides\CODE-STANDARDS.md`
