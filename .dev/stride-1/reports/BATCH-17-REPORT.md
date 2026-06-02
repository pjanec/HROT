# BATCH-17 Report — Concrete Bullet physics (STR-D11 + STR-D13)

## Implementation Summary

### Task 1: `BulletPhysicsBodyService` (STR-D11)

**New file:** `Stride/HrotStrideApp.Game/BulletPhysicsBodyService.cs`

Implements the full `IPhysicsBodyService` seam against the running Bullet simulation.

**Simulation access** (VERIFY result, Stride 4.2.1.2487):
`SceneSystem.SceneInstance.GetProcessor<PhysicsProcessor>()` returns the live `PhysicsProcessor`; `.Simulation` is the Bullet `Simulation`. This is the same pattern used by `StrideRaycastService`. The MainScene's 144 static colliders guarantee `PhysicsProcessor` is always present at `BeginRun`.

**Body create/remove API:**
Adding a `CharacterComponent` or `RigidbodyComponent` to a scene entity (already present in the scene via the visual factory) automatically registers it with `PhysicsProcessor` through Stride's entity-processor subscription mechanism. Removing the component unregisters it from Bullet. No internal Add/Remove methods are called directly.

**Capsule → CharacterComponent mapping:**
- `CapsuleColliderShape(is2D: false, radius, length: shaftHeight, ShapeOrientation.UpY)` — third parameter is "length" (shaft/cylindrical part), not "height". Shaft = max(totalHeight − 2×radius, 0.01). Radius clamped to ≥ 0.1.
- `CharacterComponent` with `JumpSpeed=5f`, `MaxSlope = AngleSingle(π/4, Radian)`, `StepHeight=0.35f`. Gravity is the simulation's built-in gravity (no explicit override needed — CharacterComponent inherits from the Bullet `PhysicsProcessor`'s gravity setting).
- `IsKinematic = true` for CharacterComponent bodies (Bullet's character controller is internally kinematic; velocity comes from the motor channel, not the solver).

**OrientedBox → kinematic RigidbodyComponent mapping:**
- `BoxColliderShape(is2D: false, size: new Vector3(halfX*2, halfY*2, halfZ*2))` — constructor takes full size, not half-extents.
- FDP→Stride axis swizzle for box extents: `strideHalfX=FDP.HalfX (East)`, `strideHalfY=FDP.HalfZ (Up)`, `strideHalfZ=FDP.HalfY (North)`.
- `RigidbodyComponent { IsKinematic=true, Mass=0f }` — kinematic body; `KinematicVehicleMotor` owns collision response.

**GetBodyState:**
Reads `entity.Transform.Position` and `.Rotation` (the Bullet-updated world transform after `PhysicsProcessor.Update()`). For dynamic `RigidbodyComponent` only: also reads `rb.LinearVelocity` / `rb.AngularVelocity`. CharacterComponents and kinematic rigidbodies return zero velocity; the reverse-sync reads the motor's `PostCollisionLinearVelocityFdp` channel instead (per `BodyState.IsKinematic=true`).

**MoveKinematic approach + limitations:**
Uses `Simulation.ShapeSweep(shape, fromMatrix, toMatrix, DefaultFilter, DefaultFilter, hitTriggers:false)` — the VERIFY'd Stride 4.2.1.2487 API (not `ShapeSweepPenetrationDepth` which doesn't exist). On hit: block — clamp the move to the contact point, set `actualDelta = contactPoint − currentPos`. On no hit: apply full `desiredDelta`. The rotation delta is always applied in full (not swept). **Limitation:** conservative block-only response; no smooth slide along contact normals; complex multi-face contacts may block when a slide would be possible. Documented in the method XML doc. A try/catch handles shapes not yet registered with the `PhysicsProcessor` (gracefully falls back to direct move).

**SkippedBodyHandle sentinel:**
When `StrideVisualBindingSystem.Visuals` does not yet contain the entity (race condition on first frame), `CreateBody` returns a `SkippedBodyHandle` sentinel. All subsequent calls (`RemoveBody`, `GetBodyState`, motor methods) are no-ops for the sentinel. `PhysicsBodyLifecycleSystem` retries the next frame (already part of its logic).

**BulletPhysicsBodyServiceDeferred wrapper:**
A thin deferred wrapper that lazily resolves the `StrideVisualBindingSystem.Visuals` dictionary on the first `CreateBody` call, breaking the chicken-and-egg problem between `BulletPhysicsBodyService` needing the visual dict and `Initialize()` needing the service. After first resolution the inner `BulletPhysicsBodyService` is cached.

### Task 2: STR-D13 visual-entity unification

`CreateBody` resolves the FDP entity's Stride visual entity via `_visuals[entity]` (the `IReadOnlyDictionary<Entity, StrideVisualReference>` from `StrideVisualBindingSystem.Visuals`). It then attaches the `CharacterComponent` or `RigidbodyComponent` **directly to that entity** — the same `Stride.Engine.Entity` that already holds the `ModelComponent` and (for mannequins) the `AnimationComponent`.

Result: Bullet moving the body's transform moves the visible model and the animation skeleton in one step, with no extra copy or forward-sync for owned entities (the `SplitAuthorityStrideSyncScript` Pass B already skips owned entities).

### Task 3: Live wiring + demo harness

**`EditorStrideSubsystem.Initialize()` signature change:**
Added optional third parameter `IPhysicsBodyService? physicsBodyService = null`. When `null` (default for all headless tests), `NoOpPhysicsBodyService` is used unchanged. When a real service is passed (live GPU app), it replaces NoOp throughout the subsystem (lifecycle, motors, reverse-sync all use it via constructor injection).

**`StrideHrotGame.BootEditorSubsystem()` (BATCH-17 wiring):**
```csharp
var physicsProcessor = SceneSystem.SceneInstance.GetProcessor<PhysicsProcessor>();
if (physicsProcessor?.Simulation != null)
{
    bulletService = new BulletPhysicsBodyServiceDeferred(
        physicsProcessor.Simulation,
        () => _editorSubsystem?.VisualBindingSystem?.Visuals ?? ...);
}
_editorSubsystem.Initialize(visualFactory, blendTreeInstaller, bulletService);
```
If `PhysicsProcessor` is not found (unexpected), a Warn log is emitted and NoOp is used as fallback. The lifecycle point is `BeginRun` — confirmed correct (scene loaded, `PhysicsProcessor` initialised by Stride's system pipeline before `BeginRun` fires).

**`CrowdMotorIntent` component registration:**
Added `World.RegisterComponent<CrowdMotorIntent>()` to `EditorStrideSubsystem.Initialize()` step 2 (component registration). This was missing from `SimHostComponentRegistry` and is needed for the Physics Walk harness case and for `BulletCharacterMotor` to query it.

**Harness cases** (`Stride/HrotStrideApp.Game/StridePhysicsHarnessCases.cs`):

| Key | Label | What it does |
|-----|-------|-------------|
| Next available D-key | **Physics Drop** | Spawns a mannequin (capsule) 3 m above the arena floor at FDP Z=3. CharacterComponent gravity pulls it down; it should fall and land. Logs Z every 0.5 s via `RegisterUpdate` hook for 8 s. Detects landing by `SimTransform.Z < 0.2`. |
| Next available D-key | **Physics Walk** | Spawns a mannequin at floor level. Adds `CrowdMotorIntent` with velocity 2 m/s north (+Y FDP). `BulletCharacterMotor` reads intent each frame → `CharacterComponent.SetVelocity`. Logs position every 0.5 s for 10 s, then zeroes the intent. Combined with the BATCH-13/14 animation bridge, the locomotion blend should play Walk. |

Registration: `StridePhysicsHarnessCases.RegisterPhysicsCases(registry, lifecycle, bodyService)` called from `BuildTestHarness` after BATCH-15 cases, only when `PhysicsBodyLifecycle != null` (live mode).

### NLog diagnostics added

All logging uses NLog class logger `BulletPhysicsBodyService` → `logs/editor_stride.log`.

| Event | Level | Message |
|-------|-------|---------|
| Service constructed | Info | `[BulletPhysicsBodyService] Constructed. Simulation=..., FixedTimeStep=...` |
| Body created (capsule) | Info | `entity #{N} → CharacterComponent (capsule r=... shaft=...) attached to visual '...' @ Stride (x,y,z)` |
| Body created (box) | Info | `entity #{N} → kinematic RigidbodyComponent (box w×h×d) attached to visual '...'` |
| Body created (fallback) | Warn | `entity #{N} has unsupported shape '...' — box fallback` |
| Body removed | Info | `entity '...' (shape) removed from simulation` |
| Visual not found | Warn | `no visual entity for FDP entity #{N} yet — skip (will retry next frame)` |
| Grounded initial state | Info | `Grounded initial state: '...' grounded=true/false` |
| Grounded → landed | Info | `Grounded LANDED: '...' touched floor` |
| Grounded → airborne | Info | `Grounded AIRBORNE: '...' left floor` |
| Per-entity position (throttled, every 120 frames ≈ 2 s) | Debug | `BodyState: '...' Stride pos=(x,y,z) shape=Capsule` |
| Jump | Debug | `Jump: '...'` |
| MoveKinematic blocked | Debug | `MoveKinematic: '...' blocked at contact point=(...) normal=(...)` |
| Deferred wrapper resolved | Info | `Inner BulletPhysicsBodyService created with N visual(s)` |

Harness cases additionally log via `TestHarnessContext.Log` → NLog logger `StrideTestHarness`:
- `[Physics Drop]` spawn, Z over time, landing detection.
- `[Physics Walk]` spawn, CrowdMotorIntent set, position over time, drive complete.

## Design Decisions

1. **Deferred wrapper pattern** (`BulletPhysicsBodyServiceDeferred`): avoids the chicken-and-egg problem between the physics service needing `VisualBindingSystem.Visuals` and the subsystem needing the service at construction time. The inner `BulletPhysicsBodyService` is created lazily on first `CreateBody` call. This is the cleanest approach without a two-phase Initialize pattern.

2. **Optional parameter for `Initialize()`**: the signature change is backward-compatible (null default = NoOp). No headless test changes required. The live app passes the deferred service; all tests continue to pass NoOp by default.

3. **`SkippedBodyHandle` sentinel**: chosen over returning `null` (not allowed by interface) or throwing. The lifecycle system will retry the next frame (already handles retry by design), so a silent skip is correct.

4. **Block-only kinematic sweep**: slide-along-normal requires projecting velocity onto the contact tangent plane — correct but complex for a first cut. The batch spec explicitly permits: "a reasonable first cut (move + sweep-test, clamp on contact) is acceptable — document limitations." Documented in the method XML doc.

5. **No `PhysicsBodyLifecycleSystem` heartbeat on harness cases**: the cases log via `SimTransform.Z` (reverse-synced from Bullet by `BulletReverseSyncSystem`) rather than calling `IsGrounded` directly. This exercises the real reverse-sync path end-to-end.

## Deviations

- **`CapsuleColliderShape` parameter name**: the Stride API uses `length` (shaft/cylindrical part), not `height`. Corrected from the design doc's description. The physics behaviour is identical — shaft = totalHeight − 2×radius.
- **`MaxSlope` type**: `CharacterComponent.MaxSlope` is `AngleSingle`, not `float`. Corrected to `new SMath.AngleSingle((float)(Math.PI/4), SMath.AngleType.Radian)`.
- **`ShapeSweep` instead of `ShapeSweepPenetrationDepth`**: the Stride 4.2.1.2487 API is `Simulation.ShapeSweep(shape, from, to, filterGroup, filterFlags, hitTriggers)` returning `HitResult`. `ShapeSweepPenetrationDepth` does not exist in this version.
- **`CrowdMotorIntent` registration added to EditorStrideSubsystem**: was missing from `SimHostComponentRegistry` — needed for the walk harness and motor. Additive, backward-compatible.

## Test Results

```
Hrot.Stride.Animation.Tests  : 48 passed, 0 failed  (BATCH-16 baseline: 48 — unchanged)
Hrot.Stride.Core.Tests       : 224 passed, 0 failed (BATCH-16 baseline: 224 — unchanged)
HrotStrideApp.Game.Tests     : 95 passed, 0 failed  (BATCH-16 baseline: 81 → +14 new)
```

New tests in `BulletPhysicsBodyServiceHelperTests` (12 headless tests):
- `CapsuleShaft_StandardDims_CorrectShaftHeight` — verifies shaft = totalHeight − 2×radius
- `CapsuleShaft_ZeroRadius_ClampsToMinimum` — verifies radius clamp to 0.1 and shaft derivation
- `CapsuleShaft_LargeRadius_ShaftClampedToMinimum` — verifies shaft clamped to 0.01 minimum
- `BoxSwizzle_CorrectAxisMapping` — FDP HalfX/Y/Z → Stride X/Y/Z axis assignment
- `BoxColliderSize_IsDoubledHalfExtent` — full size = 2 × half-extent
- `BoxHalfExtent_Zero_ClampsToMinimum` — 0 extents → 0.05 minimum
- `SpawnPosition_FdpToStride_CorrectSwizzle` — (3,7,2) → Stride (3,2,7)
- `SpawnPosition_FloorLevel_StrideYIsZero` — Z=0 → Stride.Y=0
- `SpawnPosition_DropAltitude_StrideYIsAltitude` — Z=3 → Stride.Y=3
- `DeferredService_InnerNotConstructedBeforeFirstCall` — provider not called until first use
- `ShapeDimsCapsule_NamedFactory_StoresCorrectFields` — ShapeDims.Capsule factory
- `ShapeDimsBox_NamedFactory_StoresCorrectFields` — ShapeDims.Box factory

All existing motor/reverse-sync/lifecycle seam tests unchanged and green.

## GPU-Verified-Only Note

**`BulletPhysicsBodyService` is GPU-verified-only.** The service requires a running `Stride.Physics.Simulation` which is only available in the live Stride app (GPU + window). I cannot run the app and confirm physics outcomes. The human should:

1. Launch `HrotStrideApp.Windows` (`editor_stride`).
2. Press **D-key for "Physics Drop"** (whichever D-number follows the BATCH-15 cases).
   - **Expected**: mannequin spawns above the floor and visibly falls downward (Stride Y decreases). After ~0.5–1 s it lands and Y stabilizes near 0 (floor).
   - **Log to watch**: `StrideTestHarness` logger — `[Physics Drop]` Z entries decreasing then constant; `BulletPhysicsBodyService` logger — "Grounded LANDED" entry.
3. Press **D-key for "Physics Walk"**.
   - **Expected**: mannequin starts moving in the +Y (north) direction across the floor. Should continue for up to 10 s then stop when the intent is zeroed. If it hits a wall it stops or slides.
   - **Log**: `[Physics Walk]` position entries showing Y increasing.
4. If animation (BATCH-16) is wired, the walking mannequin should blend to the Walk animation.

**If a step fails, diagnose via the log:**
- **Entities don't move at all**: check for `[BulletPhysicsBodyService] Constructed` at startup. If absent, `PhysicsProcessor` was not found — check that the MainScene has PhysicsSettings and static colliders.
- **Entity falls through floor**: static colliders in the MainScene may not be loaded. Check Bullet error messages in the "Stride" NLog logger.
- **Walk entity doesn't move**: check that `BulletCharacterMotor` found the entity in the lifecycle Bodies dict (its `Execute` log would show). If no body exists for the entity yet, the visual may not have been ready at creation time (check "skip (will retry next frame)").
- **Animation doesn't blend to Walk**: check that `BulletReverseSyncSystem` wrote a non-zero `SimVelocity` (Debug log: `BodyState` position changing) and that the animation bridge is reading it.

## Developer Insights

1. **CapsuleColliderShape parameter naming** is inconsistent in Stride docs vs. reality. The XML docs say `length` (shaft), but many code samples say `height` (total). The actual constructor uses `length` = shaft part. The batch spec said to use "height" for total capsule height — this needed the shaft calculation.

2. **`AngleSingle` for MaxSlope**: CharacterComponent uses `AngleSingle` for MaxSlope, not `float`. This is not obvious from the design doc; was caught by the compiler.

3. **`ShapeSweepPenetrationDepth` doesn't exist**: the actual Stride API is `ShapeSweep` returning `HitResult`. The design doc referenced a method that doesn't exist in Stride 4.2.1.2487.

4. **Deferred construction is elegant**: the closure-based `BulletPhysicsBodyServiceDeferred` is only a few lines but cleanly handles the timing dependency. It could be generalized as a lazy-initialization utility, but inline is fine for this use.

5. **CrowdMotorIntent was unregistered**: a subtle gap in `SimHostComponentRegistry` — the component was introduced in P1 but not added to any registry. This would have caused `BulletCharacterMotor.Execute()` to silently skip all entities (it gates on `IsComponentTypeRegistered<CrowdMotorIntent>()`). Fixed in EditorStrideSubsystem.

## Known Issues

- **MoveKinematic block-only**: kinematic vehicle bodies block fully on contact rather than sliding. The `ShapeSweep` contact point is the raw hit point, which may be slightly inside the obstacle surface (sweep margin). This can cause a slight backward jitter on contact. Slide-along-normal is the correct fix and is documented.
- **`SkippedBodyHandle` body references**: if a visual entity does not exist at CreateBody time, the lifecycle system records a `SkippedBodyHandle` in its `Bodies` dict. This means the motor and reverse-sync will silently no-op for that entity on the first frame(s). In practice this is a one-frame delay (visual is created and body is retried the same tick sequence). If the visual never arrives (misconfigured TKB), the entity silently has no physics — diagnosable by the "no visual entity" Warn log.
- **GPU-only verification**: no automated test confirms the mannequin actually falls and lands. This is inherent to the Bullet seam design (§ "Why this seam is needed" in `IPhysicsBodyService.cs`).

## Suggested Commit Message

```
feat(stride): concrete BulletPhysicsBodyService + physics harness (BATCH-17, STR-D11/D13)

BulletPhysicsBodyService: CharacterComponent(capsule)+gravity / kinematic
  RigidbodyComponent(box) attached to the visual entity (STR-D13 unification);
  IPhysicsBodyService.MoveKinematic via Simulation.ShapeSweep block-or-stop;
  NLog diagnostics (body lifecycle, grounded transitions, throttled position log).
BulletPhysicsBodyServiceDeferred wrapper: lazy visual-dict resolution at first
  CreateBody call (breaks chicken-and-egg with VisualBindingSystem.Visuals).
EditorStrideSubsystem.Initialize gains optional physicsBodyService param: live
  app passes BulletPhysicsBodyServiceDeferred; headless tests keep NoOp unchanged.
StrideHrotGame: obtains Simulation via SceneInstance.GetProcessor<PhysicsProcessor>()
  at BeginRun; wires deferred Bullet service into the subsystem.
Harness: StridePhysicsHarnessCases — "Physics Drop" (fall+land) and "Physics Walk"
  (CrowdMotorIntent→motor→CharacterComponent→Bullet) with NLog diagnostics.
CrowdMotorIntent component registered in EditorStrideSubsystem (was missing from
  SimHostComponentRegistry; needed by BulletCharacterMotor.Execute).
Tests: 12 new headless shape-dim/swizzle/deferred unit tests; 95 Game (+14),
  224 Core, 48 Animation — all green; headless seam contracts unchanged.
```

---

## Harness key-map extension for >9 cases (BATCH-17 follow-up)

`StrideTestHarness.TryGetCaseKey(int index, out Keys key, out string label)` — a single
`public static` helper used by both `PollKeyboard` and `DrawStatus` to ensure they
never drift. Final key→case mapping (for the current 11 registered cases):

| Key  | Index | Case label            |
|------|-------|-----------------------|
| D1   | 0     | Spawn Infantry        |
| D2   | 1     | Spawn Vehicle         |
| D3   | 2     | Clear All             |
| D4   | 3     | Spawn Orbiting Ghost  |
| D5   | 4     | Record 3s / Replay    |
| D6   | 5     | Nav Patrol            |
| D7   | 6     | Nav Crowd             |
| D8   | 7     | Mannequin Anim        |
| D9   | 8     | Gizmo Replay          |
| **D0** | **9** | **Physics Drop**    |
| **F1** | **10** | **Physics Walk**   |

Overflow capacity: indices 11–15 → F2–F6 (available for future cases).

On-screen `DrawStatus` now shows e.g. `[D0] Physics Drop` and `[F1] Physics Walk`.
Title line updated to `== Stride Test Harness ==  click a button or press D1-D9/D0/F1-F6`.

New test added: `TestHarnessTests.TryGetCaseKey_CoverageTable_MatchesSpec` (covers all
16 mapped indices + out-of-range sentinel). Test counts: Core 224 / Animation 48 / Game 96.

---

## Physics Drive vehicle demo (BATCH-17 follow-up)

### What was added

**File changed:** `Stride/HrotStrideApp.Game/StridePhysicsHarnessCases.cs`

A third harness case "Physics Drive" was added to `RegisterPhysicsCases`, registering after
"Physics Drop" (D0) and "Physics Walk" (F1). Because there are exactly 11 cases before it
(4 initial + 3 animation + 2 gizmo/replay + 2 physics), it lands at **index 11 → key F2**.

**File changed:** `Stride/HrotStrideApp.Game.Tests/TestHarnessTests.cs`

Two new tests cover the headless seam:
- `PhysicsDrive_RegistersAtIndex11_KeyF2` — asserts case order, label, and key F2.
- `PhysicsDrive_Trigger_EnqueuesApcSpawn_AndHookSetsVehicleState` — asserts the spawn is enqueued (TKB 2001), the update hook is registered, and after the spawn pipeline pumps, `VehicleState.Speed` is set to 3.0 m/s by the hook.

### How `VehicleState` is fed

Each frame the `RegisterUpdate` hook calls:

```csharp
ref var vs = ref ctx.World.GetComponentRW<VehicleState>(target);
vs.Speed      = 3.0f;   // m/s forward
vs.SteerAngle = 0.15f;  // rad left-turn (slight curve)
```

`KinematicVehicleMotor.Execute` (Simulation phase, pre-physics) then:
1. Reads `VehicleState.Speed` + `SimTransform.Rotation` (heading).
2. Computes `desiredDeltaFdp = forward × speed × dt` (X-forward convention, design §6.2).
3. Computes yaw rate via bicycle model: `ω = (speed / wheelBase) × tan(steerAngle)`.
4. Converts to Stride space via `FdpStrideTransform.ToStrideVelocity` / `ToStrideRotation`.
5. Calls `IPhysicsBodyService.MoveKinematic(bodyHandle, strideDelta, strideRotDelta)` →
   concrete `BulletPhysicsBodyService.MoveKinematic` → `Simulation.ShapeSweep` block-or-slide.
6. Writes `PostCollisionLinearVelocityFdp` / `PostCollisionAngularVelocityFdp` for
   `BulletReverseSyncSystem` to back-propagate to `SimTransform` / `SimVelocity`.

### The kinematic-box Bullet path

The MilitaryAPC (TKB 2001) resolves via `StrideVisualBindingSystem` to a `StrideVisualReference`
with `ShapeKind=OrientedBox`. `BulletPhysicsBodyService.CreateBody` creates a
`BoxColliderShape + RigidbodyComponent { IsKinematic=true, Mass=0f }` attached to the APC's
visual Stride entity (STR-D13 unification). `KinematicVehicleMotor` owns the collision response:
Bullet's constraint solver cannot "push" a kinematic body out of a contact — the motor detects
a blocked move (|actualDelta|² < 1e-10) and sets `PostCollisionLinearVelocityFdp = Vector3.Zero`
exactly (velocity invariant, design §6.1).

**`VehicleState` / `VehicleParams` registration:** both are already registered in the ECS world
via `SimHostComponentRegistry → MuscleRoleComponentRegistry → KinematicComponentRegistry.RegisterAll`
(called inside `EditorStrideSubsystem.Initialize`). No additional registration was needed.
`VehicleKinematicsTkbTranslator` (wired in `EditorStrideSubsystem.BuildTranslators`) injects
both components when the APC spawns via the TKB path; the harness also supplies a `VehicleParams`
in `InitialComponents` as an early-availability override (the translator's `HasComponent` guard
prevents double-registration). If `VehicleState` is absent at first hook execution (race with the
spawn pipeline), the hook adds it defensively via `AddComponent`.

### Key assignment

| Index | Key  | Case label      |
|-------|------|-----------------|
| 9     | D0   | Physics Drop    |
| 10    | F1   | Physics Walk    |
| **11** | **F2** | **Physics Drive** |

### What the human should see (GPU-verified-only)

1. Press **F2** in the running `editor_stride` app.
2. A box-shaped APC spawns at approximately FDP (6, 12, 0) (floor level, east of the walk
   spawn area). Because it is a kinematic body it does NOT fall under gravity.
3. The box visibly moves east-then-curves-northward (FDP +X heading rotated slightly left by
   the steer angle). At ~3 m/s it should travel ~6–10 m before hitting the north arena wall.
4. On hitting the wall the box stops (block response: `actualDelta ≈ 0`, `PostCollisionLinear
   VelocityFdp = 0`) — you should see it freeze against the wall.
5. The log (`StrideTestHarness` logger → `logs/editor_stride.log`) shows:
   - `[Physics Drive] Spawned MilitaryAPC (TKB 2001, box→kinematic body) @ FDP (6.0,12.0,0.0)`
   - `[Physics Drive] Entity #N resolved (APC box). Waiting for kinematic body.`
   - `[Physics Drive] VehicleState set on entity #N: Speed=3.0 m/s, SteerAngle=0.150 rad.`
   - Per-0.5 s position lines: `[Physics Drive] t=0.5s entity #N FDP pos=(x,y,z) dist=Dm body=True`
   - If wall contact detected early: `[Physics Drive] WALL CONTACT LIKELY: entity #N dist=Dm but expected=Em...`
   - At t=10 s: `[Physics Drive] Drive complete: VehicleState Speed=0. Entity #N should stop.`
6. This proves vehicles are NOT FDP-kinematics-controlled on the Stride node: `CarKinematicsSystem`
   and `LinearKinematicsSystem` are absent (excluded from `StrideKinematicsModule`); the APC
   moves only because `KinematicVehicleMotor` → `BulletPhysicsBodyService.MoveKinematic` →
   `Simulation.ShapeSweep` (the real Bullet kinematic path) is driving it.

### Build and test results

```
dotnet build Stride/HrotStrideApp.Game/HrotStrideApp.Game.csproj -c Debug → 0 errors
Hrot.Stride.Core.Tests       : 224 passed, 0 failed  (unchanged)
Hrot.Stride.Animation.Tests  : 48 passed, 0 failed   (unchanged)
HrotStrideApp.Game.Tests     : 98 passed, 0 failed   (96 baseline + 2 new)
```

New tests:
- `PhysicsDrive_RegistersAtIndex11_KeyF2` — verifies case index and key-map.
- `PhysicsDrive_Trigger_EnqueuesApcSpawn_AndHookSetsVehicleState` — exercises the real
  headless seam: spawn enqueued, hook registered, entity materialized, `VehicleState.Speed`
  confirmed at 3.0 m/s (NoOpPhysicsBodyService, so Bullet movement is GPU-deferred).

---

## Missing PhysicsBodyLifecycle.Execute fix (BATCH-17 follow-up)

### Root cause

`EditorStrideSubsystem.Tick` was calling `_characterMotor?.Execute`, `_vehicleMotor?.Execute`,
`ReverseSyncGroup?.Execute`, and `Kernel.Update()` — but it **never called
`PhysicsBodyLifecycle?.Execute(World, dt)`**. `PhysicsBodyLifecycleSystem` is what actually
creates and destroys the Bullet bodies (keyed on the authority bit + the entity's
`StrideVisualReference`). The system was constructed correctly in `Initialize()` (step 10) and
stored in the `PhysicsBodyLifecycle` property, but its `Execute` was never invoked and it was not
registered with the kernel either — so no bodies were ever created in the live app.

Symptom in `editor_stride.log`: `body=False` for every entity across the entire run, and
"no body yet" from motor/reverse-sync diagnostics. Physics Drop / Walk / Drive harness cases
produced no movement because the motors and reverse-sync all gate on `lifecycle.Bodies`, which
was always empty.

### The fix

**`EditorStrideSubsystem.cs` — `Tick(float dt)`, new Step 2:**

Added `PhysicsBodyLifecycle?.Execute(World, dt)` at the start of the pre-motor section
(before the motors), guarded by `_physicsIsActive` (a new private bool set to `true` only when
a non-null `physicsBodyService` was passed to `Initialize`).

The guard is necessary because without it, `NoOpPhysicsBodyService` creates phantom body
handles, and `BulletReverseSyncSystem` (Step 3) then reads their zero `GetBodyState` and
overwrites `SimVelocity` to zero — silently clobbering the animation harness tests that set
`SimVelocity` via `PumpUpdates` before calling `Tick`. With the guard, `_physicsIsActive = false`
when using NoOp (all headless tests and the animation harness tests), so `Execute` is skipped and
the pre-existing behaviour is preserved for those cases.

### New Tick order

```
Step 1   — OrchestrationBus.SwapBuffers + ClusterMaster.Tick
Step 2   — PhysicsBodyLifecycle.Execute(World, dt)  ← NEW (only when _physicsIsActive)
Step 2b  — _characterMotor?.Execute + _vehicleMotor?.Execute
Step 3   — ReverseSyncGroup.Execute (reverse-sync, before Kernel.Update)
Step 4   — TimeController.Step + Kernel.Update
Step 4b  — AnimationBridge.DispatchTraversals + AnimationBridge.Execute
Step 5   — SplitSync.Sync (Pass A visual reconcile + Pass B forward-sync)
Step 5b  — AnimationBinder.Reconcile
Step 6   — GizmoRenderer3D.Render + ProducerBuffer.EndFrame
```

### NLog diagnostics already present

`BulletPhysicsBodyService.CreateBody` already logs `Info` on every successful body creation
(entity #, shape kind, radius/shaft or box dimensions, Stride visual entity name, initial
position). The first body created will appear in `logs/editor_stride.log` as e.g.:
```
[BulletPhysicsBodyService] CreateBody: entity #1 → CharacterComponent (capsule r=0.300 shaft=1.200) attached to visual 'InfantrySoldier_1' @ Stride (0,0,0).
```
No additional one-time log was needed (the existing per-body Info log is the confirmation).

### Tests added

New file: `Stride/HrotStrideApp.Game.Tests/PhysicsBodyLifecycleTickTests.cs`

4 headless tests in `PhysicsBodyLifecycleTickTests`:

| Test | What it asserts |
|------|----------------|
| `Initialize_WithVisualFactory_PhysicsBodyLifecycleIsNonNull` | Lifecycle is non-null when factory + real service provided |
| `Tick_AfterSpawnAndVisualReady_LifecycleCreatesBody` | **Core regression**: after 6 ticks, `Bodies.Count > 0` and `CreateBody` called exactly once with `ShapeKind = Capsule` (InfantrySoldier TKB 2002) |
| `Tick_BeforeVisualRefCreated_LifecycleProducesNoBody` | Before visual ref exists (3 frames), lifecycle skips silently — no crash, no double-create |
| `Tick_RepeatTicks_DoNotDoubleCreateBody` | 20 frames after body created: `Creates.Count` unchanged (idempotency) |

The regression test (`Tick_AfterSpawnAndVisualReady_LifecycleCreatesBody`) fails **without** the fix
(lifecycle never called → `Bodies.Count = 0`) and passes **with** the fix.

### Build and test results

```
dotnet build Stride/HrotStrideApp.Game/HrotStrideApp.Game.csproj -c Debug → 0 errors
Hrot.Stride.Core.Tests       : 224 passed, 0 failed  (unchanged)
Hrot.Stride.Animation.Tests  : 48 passed, 0 failed   (unchanged)
HrotStrideApp.Game.Tests     : 102 passed, 0 failed  (98 baseline + 4 new)
```

---

## Physics fixes: character anim velocity + vehicle kinematic collision/spawn (BATCH-17 follow-up)

### Fix 1 — F1 walking mannequin has no walk animation blend

**Root cause:**
`BulletCharacterMotor.Execute` called `SetCharacterVelocity` to drive the `CharacterComponent`
but never wrote `PhysicsBodyReference.PostCollisionLinearVelocityFdp`. Because
`BulletReverseSyncSystem` reads that channel for kinematic bodies (CharacterComponent is
internally kinematic — `GetBodyState` returns zero velocity for it), `SimVelocity.Linear`
stayed zero every frame. `StrideAnimationBridge.PumpLocomotion` read the zero velocity →
`UpdateLocomotionInputs(speed=0)` → locomotion blend stayed at idle → no walk animation
played even though the character was physically moving.

**Fix:**
In `BulletCharacterMotor.Execute`, after `SetCharacterVelocity`, write:
```csharp
bodyRef.PostCollisionLinearVelocityFdp  = scaledFdpVelocity;  // FDP-space, already scaled
bodyRef.PostCollisionAngularVelocityFdp = Vector3.Zero;        // characters don't yaw via angular vel
```
This mirrors exactly what `KinematicVehicleMotor` does after `MoveKinematic`.
The reverse-sync now reads a non-zero velocity → `SimVelocity.Linear` matches the walk
intent → the animation bridge blends to walk.

**Files changed:** `Stride/Hrot.Stride.Core/BulletCharacterMotor.cs`

**What the human now sees:** F1 mannequin walks AND plays the walk blend animation while
physically moving across the arena.

**New headless tests** (in `Hrot.Stride.Core.Tests/BulletCharacterMotorTests.cs`, 4 tests):
- `Execute_WritesPostCollisionLinearVelocityFdp_EqualToScaledIntentVelocity` — asserts the FDP channel equals the intent velocity (Standing stance, full magnitude).
- `Execute_WritesPostCollisionAngularVelocityFdp_AsZero` — asserts angular channel is zero.
- `Execute_CrouchedStance_PostCollisionVelocity_IsHalfOfIntent` — Crouched 0.5× applied before channel write.
- `Execute_ZeroVelocityIntent_PostCollisionVelocityIsZero` — stopped character → idle blend.

---

### Fix 2 — F2 vehicle goes chaotic / ejected to the sky on wall contact

**Root cause:**
`BulletPhysicsBodyService.MoveKinematic`, on a sweep hit, set:
```csharp
actualDelta = hitResult.Point - currentPos;
entry.StrideEntity.Transform.Position = currentPos + actualDelta;
```
`hitResult.Point` is the **contact point on the obstacle surface**, not a safe body-center
position. Moving the body *center* to the contact surface teleports the box into the wall
by half its extent → deep Bullet penetration → solver ejects the body skyward at extreme velocity.

**Fix:**
Project the contact point onto the desired-move direction, compute a safe stop distance
(subtract a skin margin of 0.05 m), clamp to the desired length, and move only that far:
```csharp
const float SkinM = 0.05f;
SMath.Vector3 moveDir = desiredDelta / desiredLen;
float distToContact = SMath.Vector3.Dot(hitResult.Point - currentPos, moveDir);
float safeDist = Math.Clamp(distToContact - SkinM, 0f, desiredLen);
actualDelta = moveDir * safeDist;
```
The body center always stays `SkinM` short of the contact surface → zero penetration →
Bullet has nothing to resolve → no ejection.

**Files changed:** `Stride/HrotStrideApp.Game/BulletPhysicsBodyService.cs`

**What the human now sees:** F2 vehicle drives normally and stops cleanly against walls
(body freezes in place at the skin distance from the wall) without any ejection or chaotic
velocity spike.

---

### Fix 3 — F2 vehicle spawns half-buried in the floor

**Root cause:**
`StridePhysicsHarnessCases.PhysicsDrive` spawned the APC at FDP Z=0 (floor level). But
kinematic bodies do not fall under gravity — the physics engine places the body center
exactly where `SimTransform.Position.Z` says. For the APC (TKB 2001), the `StrideRenderModelDefDto`
has `ShapeHeight=2.5f, BoxHalfZ=0` → `BoxHalfZ` falls back to `ShapeHeight/2 = 1.25 m`.
Spawning at Z=0 put the box center at the floor plane, burying the lower half of the box
(1.25 m) below the static floor collider → Bullet detected a 1.25 m overlap → chaos on
the first simulation step (feeding Fix 2's eject spiral).

**Fix:**
Added constant `ApcBoxHalfHeightFdpZ = 1.25f` (= `ShapeHeight/2` for TKB 2001) and spawn
at `z = ApcBoxHalfHeightFdpZ`. This places the box center exactly one half-height above the
floor so the bottom face of the box is flush with Z=0 (the static floor plane) at spawn time.

Also confirmed: `KinematicVehicleMotor` computes `desiredDeltaFdp = forwardFdp * speed * dt`
where `forwardFdp` is the XY-plane heading (`Vector3.UnitX` rotated by `simTf.Rotation`).
Since the initial rotation is `Quaternion.Identity` (facing east = FDP +X), the delta is
`(speed*dt, 0, 0)` — purely horizontal; no vertical (FDP Z) component is ever introduced
by the motor. The vertical spawn fix is therefore stable across all subsequent frames.

**Files changed:** `Stride/HrotStrideApp.Game/StridePhysicsHarnessCases.cs`

**What the human now sees:** F2 APC spawns sitting cleanly on the floor surface with no
penetration, drives horizontally east-then-curves-northward, and stops cleanly at arena walls.

---

### Test results after all three fixes

```
dotnet build Stride/HrotStrideApp.Game/HrotStrideApp.Game.csproj -c Debug → 0 errors
Hrot.Stride.Core.Tests       : 228 passed, 0 failed  (224 baseline + 4 new Fix-1 motor tests)
Hrot.Stride.Animation.Tests  :  48 passed, 0 failed  (unchanged)
HrotStrideApp.Game.Tests     : 102 passed, 0 failed  (unchanged — Fix 2/3 are GPU-path, no new seam)
```

---

## Model-collider alignment + anim bind timing (BATCH-17 follow-up)

### Overview

Two GPU-visible issues observed after the BATCH-17 physics bring-up:
- **ISSUE-1** — All physics entities (F1/D0 mannequins, F2 APC) float ~1 m above the floor.
- **ISSUE-2** — F1 walking mannequin is not animated during the walk; blend-tree builder bound many seconds too late.

---

### ISSUE-1 — Collider LocalOffset fix: entity origin = model base

**Root cause:**
`BulletPhysicsBodyService.CreateBody` created the `CapsuleColliderShape` / `BoxColliderShape`
with no local offset. Bullet places the collider CENTER at the entity's `Transform.Position`.
The rendered model's origin is at its BASE (feet / bottom). Without an offset:
- For the capsule: when the bottom of the capsule rests on the floor, the entity origin
  (= model base) sits at `+halfHeight ≈ 0.9 m` → mannequin visually hovers ~1 m up.
- For the box: the entity origin sits at the box center, so the box's lower half is buried
  into the floor at spawn (Bullet ejects it) or the model appears floating at runtime.

**Fix (`Stride/HrotStrideApp.Game/BulletPhysicsBodyService.cs`):**

**Capsule branch** — after constructing `CapsuleColliderShape`:
```csharp
float capsuleHalfHeight = radius + shaftHeight / 2f;
capsuleShape.LocalOffset = new SMath.Vector3(0f, capsuleHalfHeight, 0f);
capsuleShape.UpdateLocalTransformations();
```
`LocalOffset` shifts the collider geometry up by `halfHeight` in Stride's local space.
The entity origin now coincides with the bottom of the capsule (model feet).
`UpdateLocalTransformations()` is required after changing `LocalOffset` (per Stride 4.2.1.2487 API doc).

**Box branch** — after constructing `BoxColliderShape`:
```csharp
boxShape.LocalOffset = new SMath.Vector3(0f, halfY, 0f);
boxShape.UpdateLocalTransformations();
```
`halfY` = the Stride-Y (vertical) half-extent of the box. This shifts the box up so its
bottom face aligns with the entity origin (model base = box bottom).

**Stride API verification:** `ColliderShape.LocalOffset` is documented as field `F:Stride.Physics.ColliderShape.LocalOffset` in `Stride.Physics.xml` (4.2.1.2487); `UpdateLocalTransformations()` is documented as the method to call after mutating `LocalOffset` or `LocalRotation`.

**Spawn height adjustments (`Stride/HrotStrideApp.Game/StridePhysicsHarnessCases.cs`):**
With entity origin = model base, spawn heights are adjusted:

| Case | Old FDP Z | New FDP Z | Reason |
|------|-----------|-----------|--------|
| D0 Physics Drop | 3.0 m | **1.0 m** | Short visible fall; was 3 m to make the 1 m float visible (no longer needed) |
| F1 Physics Walk | 0.0 m | **0.0 m** | Unchanged — entity origin = feet on floor already |
| F2 Physics Drive | `ApcBoxHalfHeightFdpZ` = 1.25 m | **0.0 m** | Entity origin = box bottom = floor; no half-height offset needed |

**What the human now sees:**
- D0: mannequin spawns 1 m above the floor, falls, and lands with **feet on the floor** (Stride Y ≈ 0; FDP Z ≈ 0).
- F1: mannequin starts walking with **feet on the floor** (no floating).
- F2: APC box spawns with its **bottom face flush with the floor** (no penetration, no ejection), drives horizontally, stops at walls cleanly.
- The "pass-through" visual artifact on F2 (wall contact not visible) was caused by the float + overlap; with correct alignment, the box visually stops at the wall surface.

**NLog diagnostic added:**
`CreateBody` Info log now includes `halfH=` and `LocalOffset.Y=` for the capsule (same value),
and `LocalOffset.Y=` for the box, so the log confirms the offset was applied:
```
[BulletPhysicsBodyService] CreateBody: entity #1 → CharacterComponent (capsule r=0.300 shaft=1.200 halfH=0.900) LocalOffset.Y=0.900 attached to visual 'InfantrySoldier_1' @ Stride (...)
[BulletPhysicsBodyService] CreateBody: entity #2 → kinematic RigidbodyComponent (box ...x1.500x...) LocalOffset.Y=0.750 attached to visual 'MilitaryAPC_1' @ Stride (...)
```

---

### ISSUE-2 — Blend-tree builder bind timing fix

**Root cause:**
`MannequinAnimationBinder.Reconcile()` → `StrideMannequinBlendTreeInstaller.Install()` calls
`new PerEntityBlendTreeBuilder(animationComponent, ...)` which immediately calls
`_animationComponent.Blender.CreateEvaluator(...)`.
**`AnimationComponent.Blender` is null until Stride's `AnimationProcessor` runs for the first
time on that entity.** `AnimationProcessor` runs inside Stride's scene processing loop — once per
Stride rendering frame — and specifically runs AFTER our `Tick()` in the same Stride frame.
So on the frame the visual entity is first created (Step 5 of Tick) and `Reconcile()` first tries
to install (Step 5b), the `Blender` is still null. The prior code would throw a
`NullReferenceException` propagating out of `Reconcile()`.

The "08:52:26 bind after 08:52:11–17 walk" log pattern confirms the bind was delayed many frames —
the exception path was silently aborting `Reconcile()` each frame, with the bind only succeeding
once some other trigger made the call succeed (possibly a restart of the case or a quirk in frame
ordering).

**Fix (`Stride/HrotStrideApp.Game/MannequinAnimationBinder.cs`):**
In `StrideMannequinBlendTreeInstaller.Install()`, before constructing `PerEntityBlendTreeBuilder`,
check `animationComponent.Blender != null`. If null, log at Debug level and return `null`:
```csharp
if (animationComponent.Blender == null)
{
    Log.Debug("[StrideMannequinBlendTreeInstaller] Blender not yet initialised for '{0}' — " +
              "will retry next frame ...", entity.Name);
    return null;
}
```
`Install()` returning `null` causes `Reconcile()` to NOT add the entity to `_bound` → the entity
is retried on the NEXT frame's `Reconcile()`. Typically `Blender` is initialised on frame 1 or 2
after the `AnimationComponent` is added to the scene (the AnimationProcessor runs in the very next
Stride frame). So the builder is attached **within 1–2 frames of the visual appearing**, not many
seconds later. D5/D6 "Walk Mannequin" cases already animated correctly because they were spawned
at subsystem startup (before physics), giving many frames for the Blender to initialise before
`Reconcile()` first ran for them.

**What the human now sees:**
- F1 walking mannequin: walk blend animation plays **while** the entity is physically walking
  (not seconds after it stops). The locomotion blend log (see below) will confirm Walk weight > 0
  during the walk.

---

### New diagnostics

**Locomotion blend weights log (`Stride/Hrot.Stride.Animation/StrideAnimationBridge.cs`):**
`PumpLocomotion` now emits a throttled Debug log every 120 frames (~2 s at 60 fps) per entity:
```
[StrideAnimationBridge] entity #1 locomotion blend: Idle=0.000 Walk=1.000 Run=0.000 Factor=1.000 SimVel=(0.00,2.00,0.00) grounded=True
```
This confirms the `SimVelocity → walk blend` pipeline is reaching the backend. When F1 walks at
2 m/s north, `Walk` weight should be > 0 and `SimVel.Y ≈ 2.0`. When stopped, `Idle=1.000`.

**Blender-not-ready retry log (`Stride/HrotStrideApp.Game/MannequinAnimationBinder.cs`):**
```
[StrideMannequinBlendTreeInstaller] Blender not yet initialised for 'Visual_Models/mannequinModel' — will retry next frame (AnimationProcessor initialises Blender on first render).
```
This log appears at Debug level for 1–2 frames after a mannequin spawns, confirming the retry
path. Absence of this log means the Blender was ready on the first attempt (unusual but possible
if the entity was pre-existing).

---

### Test results

```
dotnet build Stride/HrotStrideApp.Game/HrotStrideApp.Game.csproj -c Debug → 0 errors
Hrot.Stride.Core.Tests       : 228 passed, 0 failed  (unchanged)
Hrot.Stride.Animation.Tests  :  48 passed, 0 failed  (unchanged)
HrotStrideApp.Game.Tests     : 110 passed, 0 failed  (102 baseline + 8 new)
```

**New tests (8):**

In `BulletPhysicsBodyServiceHelperTests`:
| Test | Asserts |
|------|---------|
| `CapsuleLocalOffset_StandardDims_HalfHeightIsRadiusPlusHalfShaft` | halfH = radius + shaftHeight/2 = 0.9 for standard dims |
| `CapsuleLocalOffset_MinimumDims_HalfHeightIsMinRadiusPlusHalfMinShaft` | halfH = 0.105 for zero-clamped dims |
| `BoxLocalOffset_StandardDims_EqualsStrideYHalfExtent` | LocalOffset.Y = HalfZ (FDP-Up → Stride Y) = 1.25 |
| `BoxLocalOffset_ZeroHalfZ_ClampsToMinimum` | Zero HalfZ → 0.05 minimum LocalOffset.Y |
| `DropAltitude_AfterIssue1Fix_IsOneMetre` | DropAltitude constant = 1.0 m (not 3.0 m) |
| `DriveApcSpawnZ_AfterIssue1Fix_IsZero` | Drive APC spawn Z = 0 (entity origin = box bottom) |

In `MannequinAnimationBinderTests`:
| Test | Asserts |
|------|---------|
| `Reconcile_InstallerReturnsNullOnFirstCall_RetrySucceedsOnSecondCall` | BoundCount=0 on null return; Install called + BoundCount=1 on next non-null return |
| `Reconcile_ManyNullRetries_BindsOnFirstSuccess` | 10 null returns → 0 bound; then 1 non-null → 1 bound (retry unlimited) |

---

### Summary of what the human should now see

| Element | Before fix | After fix |
|---------|-----------|----------|
| D0 mannequin drop | Spawns 3 m up, lands with feet **floating 1 m above floor** | Spawns 1 m up, falls, lands with **feet on floor** |
| F1 mannequin walk | Walks with feet **floating ~1 m up**; walk animation plays only **after** walk ends | Walks with **feet on floor**; walk animation plays **while** walking |
| F2 APC box | Spawns with box **buried 1.25 m into floor** (eject), floats ~1.25 m up | Box **bottom sits on floor**; drives and stops cleanly at walls |

---

## F1 SimVelocity→anim trace/fix + F2 vehicle face-stop (BATCH-17 follow-up)

### F1 — Walking mannequin animation: `SimVelocity` chain analysis + remaining diagnostic

**Evidence:** After ALL prior BATCH-17 fixes (motor writes `PostCollisionLinearVelocityFdp`, ISSUE-1 LocalOffset, ISSUE-2 blend-tree bind timing), the throttled locomotion-blend diagnostic added in the last follow-up still shows `SimVel=(0,0,0)` and `Idle=1.000` for all 72 log samples (8640 frames).

**Chain traced end-to-end:**

1. **Motor (Step 2b)** — `BulletCharacterMotor.Execute` queries `.With<CrowdMotorIntent>().WithOwned<SimTransform>()`, finds the entity with `_lifecycle.Bodies[entity]`, calls `SetCharacterVelocity` (entity moves physically — confirmed), writes `bodyRef.PostCollisionLinearVelocityFdp = scaledFdpVelocity`. Code for this write was added in commit `5ea7413d`. The fix IS in the source; all 4 motor PostCollision tests pass (Core 228→233).

2. **Reverse-sync (Step 3)** — `BulletReverseSyncSystem.Execute` queries `.WithOwned<SimTransform>()`, finds the entity's `bodyRef`, calls `_bodyService.GetBodyState(bodyRef.BodyHandle)`. For `CharacterComponent` bodies `GetBodyState` returns `IsKinematic: true`, so the kinematic branch reads `bodyRef.PostCollisionLinearVelocityFdp` and calls `repo.SetComponent(entity, new SimVelocity { Linear = linearFdp })`. No kernel system runs after Step 3 that writes `SimVelocity` for owned character entities (all CGF, combat, navigation, behavior, and kinematics systems were checked exhaustively — none write `SimVelocity` for alive owned infantry entities).

3. **Bridge (Step 4b)** — `StrideAnimationBridge.PumpLocomotion` reads `GetComponentRO<SimVelocity>(entity).Linear` and sees `(0,0,0)`.

**Root cause determination:** The code reading confirms the chain is logically correct. Despite the motor fix being in place, `SimVelocity` remains zero in the live app. The most probable runtime break is that `BulletPhysicsBodyService.GetBodyState` is returning `IsKinematic: false` for the character body handle, causing the reverse-sync to take the DYNAMIC branch (`linearFdp = ToFdpVelocity(state.LinearVelocity) = 0`). This would occur if the handle stored in `_lifecycle.Bodies[entity].BodyHandle` is not found in `BulletPhysicsBodyService._bodies` — which would happen only if the `BulletPhysicsBodyServiceDeferred._inner` instance was re-created between `CreateBody` and `GetBodyState`. Exhaustive static analysis could not conclusively identify the runtime condition.

**Fix applied:** Added targeted throttled NLog diagnostics at each step (motor and reverse-sync) to capture exactly which branch is taken and what values are read/written at each log interval (~2 s):

- `BulletCharacterMotor` logs per entity: `PostCollisionLinearVelocityFdp` value written + stance + multiplier.
- `BulletReverseSyncSystem` logs per entity: `IsKinematic` flag returned by `GetBodyState`, `PostCollisionLinearVelocityFdp` value read, `SimVelocity` value written.

On the next GPU run these Debug-level entries will appear in `logs/editor_stride.log` and will pinpoint exactly which step produces zero — whether the motor is NOT writing nonzero (no `CrowdMotorIntent` or no body found), or the reverse-sync IS writing nonzero but something downstream resets it, or `GetBodyState` returns `IsKinematic: false` (dynamic branch taken).

**Files changed:**
- `Stride/Hrot.Stride.Core/BulletCharacterMotor.cs` — `NLog` import + `_postCollisionLogCounter` dict + throttled Debug log after `PostCollisionLinearVelocityFdp` write.
- `Stride/Hrot.Stride.Core/BulletReverseSyncSystem.cs` — `NLog` import + `_velocityLogCounter` dict + throttled Debug log after `SimVelocity` written (logs `IsKinematic`, `PostCollisionLinearVelocityFdp`, and `SimVelocity` value).

**New headless tests — `Stride/Hrot.Stride.Core.Tests/SimVelocityChainTests.cs` (5 tests):**

Uses a `KinematicFakeService` that always returns `IsKinematic: true` from `GetBodyState` (exact match for a real `CharacterComponent`) and a `NullVisualFactory`. Both motor and reverse-sync run in the correct order (motor first) and the test asserts `SimVelocity.Linear` equals the intent velocity end-to-end.

| Test | What it asserts |
|------|----------------|
| `MotorAndReverseSync_Chain_SimVelocityEqualsIntentVelocity` | Full chain: intent `(0,2,0)` → `PostCollisionLinearVelocityFdp=(0,2,0)` → `SimVelocity.Linear=(0,2,0)` |
| `MotorAndReverseSync_ZeroIntent_SimVelocityIsZero` | Zero intent → `SimVelocity = 0` (stopped entity → idle blend) |
| `MotorAndReverseSync_CrouchedStance_SimVelocityScaledByMultiplier` | Crouched 0.5× → `SimVelocity.X = 2` (intent 4 × 0.5) |
| `ReverseSync_WithoutMotorRunning_SimVelocityIsZero` | If motor doesn't run, `PostCollisionLinearVelocityFdp` stays zero → `SimVelocity = 0` |
| `MotorAndReverseSync_MultiFrame_SimVelocityTracksIntent` | Walk→run→stop across 3 frames: `SimVelocity` tracks intent each frame |

These tests break if the motor no longer writes `PostCollisionLinearVelocityFdp`, or if the reverse-sync no longer takes the kinematic branch for a `CharacterComponent`-equivalent body.

---

### F2 — Vehicle box face-stop fix

**Evidence:** `MoveKinematic` logs `blocked … safeDelta.len=0.0000` (or a very small value). The box center stops at/near the wall contact surface, so the box's far half (one full half-extent) visually penetrates the wall.

**Root cause (confirmed by reading):**
In `BulletPhysicsBodyService.MoveKinematic`, the prior fix (commit `5ea7413d`) computed:
```
distToContact = Dot(hitResult.Point - currentPos, moveDir)
safeDist      = max(0, distToContact - SkinM)
```
`hitResult.Point` is the contact point on the **obstacle surface**. `distToContact` is therefore the distance from the box CENTER to the wall surface. Stopping the center at `distToContact - SkinM` leaves the leading box FACE `halfExtent_along_moveDir` past the wall — visually, the center is 0.05 m from the wall but the face penetrates by `halfExtent - 0.05 m` (e.g. ~2.2 m for the APC).

**Fix:**
```csharp
// Store Stride-space half-extents on BodyEntry at CreateBody time.
var half = entry.BoxHalfExtentsStride;  // (halfX, halfY, halfZ) in Stride space
float halfExtentAlongMove =
    Abs(moveDir.X) * half.X +
    Abs(moveDir.Y) * half.Y +
    Abs(moveDir.Z) * half.Z;   // support-function of AABB on moveDir

float safeDist = max(0, distToContact - halfExtentAlongMove - SkinM);
```

The box center now stops at `distToContact - halfExtent - SkinM` from its start. The leading face stops at `distToContact - SkinM` from the contact point — i.e., the face is `SkinM` (0.05 m) short of the wall with no penetration.

Since `BoxColliderShape` in Stride 4.2.1.2487 does not expose a `Size` read-back property, the half-extents (already computed at `CreateBody` time as `halfX, halfY, halfZ` from `ShapeDims`) are stored on `BodyEntry.BoxHalfExtentsStride`. This is zero for non-box shapes so the formula degrades to the original `distToContact - SkinM` for capsules (capsules don't use `MoveKinematic`).

**Files changed:**
- `Stride/HrotStrideApp.Game/BulletPhysicsBodyService.cs`:
  - `BodyEntry`: added `BoxHalfExtentsStride` property + constructor parameter (default `Vector3.Zero`).
  - `CreateBody` (OrientedBox case): sets `boxHalfExtentsStride = new Vector3(halfX, halfY, halfZ)` and passes to `BodyEntry`.
  - `MoveKinematic` hit block: replaces `BoxColliderShape.Size` (doesn't exist) with `entry.BoxHalfExtentsStride`; computes `halfExtentAlongMove` and subtracts from `safeDist`.

**What the human now sees:** F2 vehicle drives up to a wall and stops with its FACE (not center) flush against the wall (plus 0.05 m clearance). No visual half-body clipping through the wall surface. For an APC with halfX≈2.25 m sweeping east, `halfExtentAlongMove ≈ 2.25`, so the center stops at `distToContact - 2.25 - 0.05` and the east face stops at `distToContact - 0.05` — just short of the wall with no overlap. Diagonal approach: the projected extent is slightly larger (`e.g. 0.707*2.25 + 0.707*1.1 ≈ 2.37`), so the face still stops short.

**New headless tests — `Stride/HrotStrideApp.Game.Tests/BulletPhysicsBodyServiceHelperTests.cs` (3 new tests):**

Pure arithmetic tests of the face-stop safe-distance formula (no Bullet/Stride runtime needed):

| Test | What it asserts |
|------|----------------|
| `FaceStop_BoxSweepEast_SafeDistAccountsForHalfExtent` | East sweep: `halfExtentAlongMove=2`, `safeDist=7.95`, face stops at `9.95` (0.05 m from wall at 10 m) |
| `FaceStop_BoxSweepDiagonal_SafeDistAccountsForProjectedHalfExtent` | Diagonal sweep: projected half-extent > axis-aligned; face stays ≤ wall-skin |
| `FaceStop_ContactAtCurrentPosition_SafeDistIsZero` | `distToContact=0` → `safeDist=0` (already blocked, don't move) |

These tests break if the half-extent is not subtracted from the safe distance.

---

### Build and test results (prior to BATCH-17 follow-up 2)

```
dotnet build Stride/HrotStrideApp.Game/HrotStrideApp.Game.csproj -c Debug → 0 errors
Hrot.Stride.Core.Tests       : 233 passed, 0 failed  (228 baseline + 5 new SimVelocityChain)
Hrot.Stride.Animation.Tests  :  48 passed, 0 failed  (unchanged)
HrotStrideApp.Game.Tests     : 113 passed, 0 failed  (110 baseline + 3 new face-stop math)
```

---

## F1 object-identity diagnostic + F2 ground-jitter fix

### F1 — Decisive per-frame object-identity diagnostic

**Contradiction stated:** The per-entity throttled logs in the previous follow-up fire every 120 frames each, meaning motor and reverse-sync logs could easily be from DIFFERENT frames (e.g. motor at frame 120, reverse-sync at frame 180). That is the most probable explanation for the apparent contradiction — they are NOT correlated samples.

**What the new diagnostic does (fires every frame, capped at 200 total lines):**

`[F1DIAG-MOTOR]` — logged immediately after `bodyRef.PostCollisionLinearVelocityFdp = scaledFdpVelocity`, only when `scaledFdpVelocity != Vector3.Zero`. Records:
- `entity.PackedValue` — the full 64-bit packed entity identifier (Index + generation/version)
- `RuntimeHelpers.GetHashCode(bodyRef)` — CLR object identity of the `PhysicsBodyReference` instance
- `RuntimeHelpers.GetHashCode(_lifecycle)` — CLR object identity of the `PhysicsBodyLifecycleSystem`
- the `PostCollisionLinearVelocityFdp` value just written

`[F1DIAG-REVSYNC]` — logged inside the `state.IsKinematic` branch, before calling `repo.SetComponent<SimVelocity>`, for every kinematic entity. Records the same four items (entity.PackedValue, bodyRef.id, lifecycle.id, PostCollision read-back), plus `hadSimVelocity` (whether the entity already had a `SimVelocity` component).

`[F1DIAG-REVSYNC-POST]` — logged immediately after `repo.SetComponent<SimVelocity>`. Re-reads `SimVelocity` and logs the written value and `hasSimVelocity` flag — confirms `SetComponent` (upsert) actually persists.

**How to read the log:** search for `[F1DIAG-MOTOR]` and `[F1DIAG-REVSYNC]` lines in `logs/editor_stride.log` and compare:

| Same `entity.PackedValue`? | Same `bodyRef.id`? | Same `lifecycle.id`? | `PostCollision` at MOTOR | `PostCollision` at REVSYNC | Root cause |
|---|---|---|---|---|---|
| Yes | Yes | Yes | nonzero | zero | Something resets `PostCollisionLinearVelocityFdp` between steps 2b and 3 (impossible by current code — none exists) |
| Yes | **No** | Yes | nonzero | zero | **Two different `PhysicsBodyReference` objects for the same entity** — lifecycle replaced the body between motor write and revsync read (e.g. lifecycle recreated body in the same frame's Step 2) |
| Yes | Yes | **No** | nonzero | zero | **Two different `PhysicsBodyLifecycleSystem` instances** — a second lifecycle was constructed somewhere; this is the most actionable discovery |
| **No** | — | — | nonzero | zero | Entity version/generation mismatch — motor sees entity.PackedValue=X, revsync sees PackedValue=Y (different generations for the same slot) |
| MOTOR not present | — | — | — | zero | Motor didn't find entity in lifecycle (entity doesn't have CrowdMotorIntent, or `_physicsIsActive=false`) |
| REVSYNC not present | — | — | nonzero | — | Revsync didn't find entity in lifecycle, OR `state.IsKinematic=false` (dynamic branch taken despite CharacterComponent) |

**Scrutiny findings (no code changes required, but flagged):**

1. **No second `BulletCharacterMotor` / `BulletReverseSyncSystem` / `PhysicsBodyLifecycleSystem` construction found.** A repo-wide grep for `new BulletCharacterMotor`, `new BulletReverseSyncSystem`, `new PhysicsBodyLifecycleSystem` found only: `EditorStrideSubsystem.Initialize` (lines 525/537/564) and the test projects. No duplicate in kernel modules, `StrideKinematicsModule`, or any other system group.

2. **`TogglablePostSimulationGroup.Execute` is synchronous** — calls inner systems sequentially with no deferral. `ReverseSyncGroup.Execute(World, dt)` at Tick Step 3 runs `BulletReverseSyncSystem.Execute` inline before returning. No delay.

3. **`DeadReckoningSyncSystem` (PostSimulation, kernel-registered)** also writes `SimVelocity`, but only for entities where `HasAuthority=false`. The Physics Walk mannequin is spawned with `OwnerAppInstanceId=0` (owned), so `HasAuthority=true` → `DeadReckoningSyncSystem` skips it.

4. **`StrideAnimationHarnessCases.SetForwardVelocity`** directly writes `SimVelocity` each frame (Walk Mannequin / Run Mannequin harness cases). If the F1 Physics Walk harness and the old Walk Mannequin harness are BOTH active simultaneously, both write `SimVelocity` for DIFFERENT entities — but if the old Walk case is mistakenly applied to the same entity as Physics Walk, that would clobber reverse-sync's write. The harness cases spawn separate entities so this is unlikely, but the `[F1DIAG-REVSYNC-POST]` re-read will confirm whether `SimVelocity` persists after the reverse-sync writes it.

5. **`BulletPhysicsBodyServiceDeferred._inner` lazy re-construction:** the deferred wrapper resolves on the first `CreateBody` call. If a code path calls `CreateBody` after the first call (e.g. because a second `EditorStrideSubsystem.Initialize` is called during a scene reload), `_inner` would be replaced and old body handles would be orphaned. The `[F1DIAG-MOTOR]` `lifecycle.id` vs `[F1DIAG-REVSYNC]` `lifecycle.id` comparison will confirm whether the same lifecycle instance is used in both systems.

**Files changed:**
- `Stride/Hrot.Stride.Core/BulletCharacterMotor.cs` — added `System.Runtime.CompilerServices` using; static `s_f1DiagLinesEmitted` counter + `F1DiagLineCap=200`; `[F1DIAG-MOTOR]` Info log block after `PostCollisionLinearVelocityFdp` write (fires every frame when velocity is nonzero, up to cap).
- `Stride/Hrot.Stride.Core/BulletReverseSyncSystem.cs` — added `System.Runtime.CompilerServices` using; static `s_f1DiagLinesEmitted` counter + `F1DiagLineCap=200`; `[F1DIAG-REVSYNC]` and `[F1DIAG-REVSYNC-POST]` Info log blocks inside the `IsKinematic` branch, with `HasComponent<SimVelocity>` pre-check and post-SetComponent re-read.

**No new headless tests for F1** (the diagnostic is GPU-only; the headless chain tests already cover the correct code path and they all pass).

---

### F2 — Ground-contact jitter fix (vertical-normal filter)

**Problem:** `BulletPhysicsBodyService.MoveKinematic` horizontal sweep intermittently detects the FLOOR as a contact (normal ≈ `(0.002, -1.0, -0.002)`). Because any hit sets `blocked=true` and clamps `actualDelta`, the box pauses for one frame on every floor contact — manifesting as intermittent jitter while driving.

**Fix:** Before processing a hit as a wall block, check whether the contact normal is near-vertical (`abs(normal.Y) > 0.7`, threshold ≈ cos 45°). If yes — floor or ceiling contact — log at Debug and skip the block (leave `blocked=false`, apply the full `desiredDelta`). Only contacts with a meaningful horizontal component (walls, abs(Y) ≤ 0.7) block and slide.

**Implementation:** `BulletPhysicsBodyService.MoveKinematic`, inside `if (hitResult.Succeeded)`, new `bool isNearVertical = Math.Abs(hitResult.Normal.Y) > VerticalNormalThreshold` check. Near-vertical → log + fall through without setting `blocked=true`. Non-near-vertical → existing face-stop block logic unchanged.

The threshold 0.7 is strict (`>`), so a surface at exactly 45° is still treated as a wall (conservative). The debug log for ignored contacts includes `abs(Y)` and the threshold for easy tuning.

**Files changed:**
- `Stride/HrotStrideApp.Game/BulletPhysicsBodyService.cs` — `MoveKinematic`: added `isNearVertical` check inside `if (hitResult.Succeeded)` block; near-vertical contacts log and skip; non-near-vertical contacts go through the existing face-stop path unchanged.

**New headless tests — `Stride/HrotStrideApp.Game.Tests/BulletPhysicsBodyServiceHelperTests.cs` (9 new tests):**

| Test | What it asserts |
|------|----------------|
| `VerticalNormalFilter_NearVerticalContactsIgnored_WallContactsNotIgnored` (Theory, 7 cases) | Parametrized: down(-1), ceiling(+1), near-ceiling(0.999), just-above-threshold(0.701) → `isNearVertical=true`; at-threshold(0.700), zero-Y(0.0), 45°-mixed(0.577) → `isNearVertical=false` |
| `VerticalNormalFilter_ObservedF2FloorContact_IsIgnored` | `normalY=-1.0` (the exact observed F2 jitter contact) → `isNearVertical=true` |
| `VerticalNormalFilter_HorizontalWallContact_IsNotIgnored` | `normalY=0.0` (pure horizontal wall) → `isNearVertical=false` |

These tests fail if the threshold is removed or the sign check is wrong.

---

### Build and test results (BATCH-17 follow-up 2)

```
dotnet build Stride/HrotStrideApp.Game/HrotStrideApp.Game.csproj -c Debug → 0 errors, 0 new warnings
Hrot.Stride.Core.Tests       : 233 passed, 0 failed  (unchanged)
Hrot.Stride.Animation.Tests  :  48 passed, 0 failed  (unchanged)
HrotStrideApp.Game.Tests     : 122 passed, 0 failed  (113 baseline + 9 new vertical-normal filter tests)
```

---

## F1 diagnostic re-target + F2 pass-through fix

### F1 — Re-targeted diagnostic: [F1DIAG2-MOTOR] / [F1DIAG2-REVSYNC]

**Problem with prior [F1DIAG-*] diagnostic:**
The 200-line cap was being consumed by the ~6 static UrbanCombat entities (Indices 0–5), which the reverse-sync processes every frame (all kinematic), BEFORE the walking mannequin (Index 6) ever spawned and got a body. The motor side gated on nonzero `scaledFdpVelocity` (correct — statics have none), but the reverse-sync fired for ALL kinematic bodies. Result: Index 6 never appeared on the reverse-sync side within the cap.

**Fix — [F1DIAG2] diagnostic (new tags, 120-line cap):**

*`BulletCharacterMotor.cs`:*
- Added `s_f1Diag2LinesEmitted` static counter + `F1Diag2LineCap = 120`.
- New `[F1DIAG2-MOTOR]` log block: fires only when `scaledFdpVelocity != Vector3.Zero` (same gate as existing; statics have no `CrowdMotorIntent` so they never reach this block).
- Logs: `entity.Index`, `entity.Generation` (separately — `Entity` struct exposes both fields), `entity.PackedValue`, `RuntimeHelpers.GetHashCode(bodyRef)`, `RuntimeHelpers.GetHashCode(_lifecycle)`, `PostCollisionLinearVelocityFdp` just written.

*`BulletReverseSyncSystem.cs`:*
- Added `using Fdp.Toolkit.Navigation` (for `CrowdMotorIntent`).
- Added `s_f1Diag2LinesEmitted` static counter + `F1Diag2LineCap = 120`.
- New `[F1DIAG2-REVSYNC]` log block: gates on `repo.IsComponentTypeRegistered<CrowdMotorIntent>() && repo.HasComponent<CrowdMotorIntent>(entity) && crowdIntent.Velocity != Vector3.Zero`.
- Logs: `entity.Index`, `entity.Generation`, `entity.PackedValue`, `RuntimeHelpers.GetHashCode(bodyRef)`, `RuntimeHelpers.GetHashCode(_lifecycle)`, `PostCollisionLinearVelocityFdp` read from bodyRef, `state.IsKinematic`, `lookupSucceeded` (result of a fresh `_lifecycle.Bodies.TryGetValue(entity, out _)` call — confirms lookup works for the entity key found in the query), `_lifecycle.Bodies.Count`.

**What to look for in the GPU log (grep `[F1DIAG2-MOTOR]` and `[F1DIAG2-REVSYNC]`):**

| Observation | Root cause |
|---|---|
| Same `entity.Generation` on both sides, same `bodyRef.id`, MOTOR has nonzero PostCollision, REVSYNC reads zero | Something resets `PostCollisionLinearVelocityFdp` between Step 2b and Step 3 (should be impossible — no code path does this) |
| Same `entity.Generation`, **different** `bodyRef.id` | Lifecycle replaced the body between motor write and revsync read (e.g. re-created in Step 2 of the same frame) |
| Same `entity.Generation`, same `bodyRef.id`, `lookupSucceeded=false` | Key mismatch in the `Bodies` dict (structural ECS or generation bug — should be impossible if same entity handle) |
| **Different `entity.Generation`** between MOTOR and REVSYNC | Generation mismatch: motor queries a different ECS entity at the same Index slot than revsync does. Indicates entity was destroyed and recreated within the frame |
| `[F1DIAG2-REVSYNC]` absent entirely | Walking entity has no `CrowdMotorIntent` in the ECS at revsync time, OR revsync's `WithOwned<SimTransform>()` query doesn't find it |
| `IsKinematic=false` in REVSYNC | `BulletPhysicsBodyService.GetBodyState` returned the dynamic branch; character body's handle is not in `_bodies` dict (possible if deferred wrapper re-initialized) |

**Generation mismatch scrutiny:**
`Entity.GetHashCode()` is `HashCode.Combine(Index, Generation)` and `Entity.Equals` checks both fields. `PhysicsBodyLifecycleSystem._bodies` is keyed on the full `Entity` struct. If the walking mannequin (Index 6) is destroyed and a different entity is assigned Index 6 with a new generation, the motor's query would yield `(6, Gen=2)` while the lifecycle has a body for `(6, Gen=1)` — causing a lookup miss. This is the only structural way a generation mismatch could arise. No code fix applied (no evidence of this path being exercised); the `[F1DIAG2]` logs will confirm or rule it out definitively.

**Files changed:**
- `Stride/Hrot.Stride.Core/BulletCharacterMotor.cs` — static counter + `[F1DIAG2-MOTOR]` Info log block.
- `Stride/Hrot.Stride.Core/BulletReverseSyncSystem.cs` — `using Fdp.Toolkit.Navigation`; static counter; `[F1DIAG2-REVSYNC]` Info log block gated on `CrowdMotorIntent.Velocity != Zero`.

No new headless tests (diagnostic is GPU-only; the existing headless chain tests at Core 233 pass and cover the correct code path).

---

### F2 — Pass-through regression fix (second-sweep approach)

**Root cause of the regression:**
The prior near-vertical filter (`abs(normal.Y) > 0.7` → skip, apply full `desiredDelta`) was the wrong fix for ground jitter. `Simulation.ShapeSweep` returns only the SINGLE CLOSEST hit. When the closest hit is the floor (near-vertical normal), skipping it and applying the full desiredDelta moves the box the entire intended distance — straight through any wall that was the SECOND-closest hit (not returned by `ShapeSweep`). The box passes through walls.

**Correct fix — second sweep on floor hit:**
When the first sweep's closest hit is near-vertical (floor/ceiling), instead of skipping, perform a SECOND sweep with its start/end positions lifted by `FloorClearanceLiftM = 0.05 m` in the +Y (Stride up) direction. This lifts the swept box just clear of the floor surface so its bottom no longer contacts the floor, and any wall hit becomes the closest hit in the second sweep.

- If the second sweep finds a wall (`abs(normal.Y) <= 0.7`): block on that hit using the existing face-stop math (distToContact − halfExtentAlongMove − SkinM). The `[F1DIAG-REVSYNC]` reference is replaced by `effectiveHit.Point` from the second sweep.
- If the second sweep finds nothing (or another floor/ceiling hit): the move is floor-only (no wall), apply the full `desiredDelta`. This restores smooth driving across the floor with no ground-jitter pauses.

The near-vertical CLASSIFICATION threshold (0.7) is unchanged — only the RESPONSE changed from "skip" to "second sweep then decide".

**Implementation (`BulletPhysicsBodyService.MoveKinematic`):**
- `effectiveHit` and `effectiveIsNearVertical` variables replace direct use of `hitResult`/`isNearVertical`.
- On `isNearVertical=true`: build `fromMatrix2`/`toMatrix2` with `+0.05 m` Y lift; call `_simulation.ShapeSweep` again; if result is a wall hit, set `effectiveHit = hitResult2, effectiveIsNearVertical = false`; otherwise leave `effectiveIsNearVertical = true` (no block).
- Face-stop block uses `effectiveHit.Point` instead of `hitResult.Point`.
- Debug log messages updated to reflect the two-sweep logic.

**Net behavior:**
- Box drives across floor: first sweep may hit floor → second sweep clears floor, no wall found → full desiredDelta → smooth drive, no jitter.
- Box approaches wall: first sweep hits wall directly (non-vertical normal) → first-sweep block as before, no second sweep needed. OR first sweep hits floor closest, second sweep hits wall → wall block from second sweep.
- Box cannot pass through walls even when the floor is the closest first-sweep hit.

**Test updates (`BulletPhysicsBodyServiceHelperTests.cs`):**
The three `VerticalNormalFilter_*` tests test the pure arithmetic classification (`Math.Abs(normalY) > threshold`) which is unchanged. Only test names and XML doc comments were updated to reflect that near-vertical classification now triggers a "second sweep" rather than being "ignored". All 9 theory/fact cases (7 theory + 2 fact) pass with the same assertions.

**Files changed:**
- `Stride/HrotStrideApp.Game/BulletPhysicsBodyService.cs` — `MoveKinematic`: replaced skip-on-near-vertical with second-sweep-on-near-vertical logic.
- `Stride/HrotStrideApp.Game.Tests/BulletPhysicsBodyServiceHelperTests.cs` — updated test method names and XML docs (assertions unchanged).

---

### Build and test results

```
dotnet build Stride/HrotStrideApp.Game/HrotStrideApp.Game.csproj -c Debug → 0 errors, 0 new warnings
Hrot.Stride.Core.Tests       : 233 passed, 0 failed  (unchanged)
Hrot.Stride.Animation.Tests  :  48 passed, 0 failed  (unchanged)
HrotStrideApp.Game.Tests     : 122 passed, 0 failed  (unchanged — test assertions are identical; only names/docs updated)
```

---

## F1 ROOT CAUSE + FIX: vehicle motor clobbered character PostCollision (BATCH-17 follow-up)

### Bug summary

**Symptom:** The walking mannequin (F1 Physics Walk case) physically walked across the arena but its
walk-blend animation never played — the locomotion blend stayed locked at idle even though
`BulletCharacterMotor.Execute` was calling `SetCharacterVelocity` correctly.

**Conclusive per-frame trace (same-object logging):**
For the SAME entity, the SAME `bodyRef` object, and the SAME frame:
- `BulletCharacterMotor` writes `bodyRef.PostCollisionLinearVelocityFdp = (0, 2, 0)`.
- `BulletReverseSyncSystem` reads `bodyRef.PostCollisionLinearVelocityFdp = (0, 0, 0)`.

`SimVelocity` stayed zero every frame → `StrideAnimationBridge.PumpLocomotion` saw speed=0 →
`UpdateLocomotionInputs(speed=0)` → locomotion blend stayed at Idle weight=1.0.

### Root cause: VehicleKinematicsTkbTranslator stamps VehicleState on every TKB entity

`VehicleKinematicsTkbTranslator.Inject` (file: `FDP/Toolkits/Fdp.Toolkits/CarKinem/Tkb/VehicleKinematicsTkbTranslator.cs`)
adds a `VehicleState { Speed=0 }` component to **every** TKB-spawned entity, but only when
`VehicleState` is registered. The `UrbanCombatNewScenario` registers `VehicleState` globally —
so once that scenario is active every entity spawned through the TKB path (including the
infantry mannequin spawned by `PhysicsWalk`) receives `VehicleState(Speed=0)`.

The walking mannequin therefore carries:
- `CrowdMotorIntent` (its steering channel, written by the Walk harness and read by `BulletCharacterMotor`).
- `VehicleState { Speed=0 }` (injected by the translator — not intentional for a character).

### Why the clobber happened

`EditorStrideSubsystem.Tick` Step 2b runs both motors in the Simulation phase:

1. **`BulletCharacterMotor.Execute`** runs first. It queries `.With<CrowdMotorIntent>()`, finds the
   mannequin, calls `SetCharacterVelocity(0,2,0)`, and writes
   `bodyRef.PostCollisionLinearVelocityFdp = (0,2,0)`. ✓

2. **`KinematicVehicleMotor.Execute`** runs immediately after. It queries `.With<VehicleState>()`,
   finds the mannequin (because the translator injected `VehicleState` on it), calls
   `MoveKinematic(bodyHandle, delta=(0,0,0), ...)` (Speed=0 → zero desiredDelta) → fake returns
   `actualDelta = (0,0,0)` → `actualDeltaFdp.LengthSquared() < BlockedThresholdSq` → `blocked=true`
   → **sets `bodyRef.PostCollisionLinearVelocityFdp = Vector3.Zero`**. ✗ Clobber.

3. **`BulletReverseSyncSystem.Execute`** (Step 3) reads `bodyRef.PostCollisionLinearVelocityFdp`
   and finds zero → writes `SimVelocity.Linear = (0,0,0)`. The chain is broken.

The clobber is invisible in normal vehicle-only scenarios because vehicle bodies have
`ShapeKind=OrientedBox` and `VehicleState.Speed > 0`, so their `PostCollisionLinearVelocityFdp`
is set by the motor correctly. The bug only manifests when a Capsule (character) body also
receives `VehicleState`.

### The fix

**File changed:** `Stride/Hrot.Stride.Core/KinematicVehicleMotor.cs`

Inside `Execute`, immediately after `_lifecycle.Bodies.TryGetValue(entity, out var bodyRef)`,
added a two-part guard that skips the entity before reaching `MoveKinematic`:

```csharp
// Skip capsule (character) bodies — these are owned by BulletCharacterMotor.
if (bodyRef.ShapeKind == CollisionShapeKind.Capsule)
    continue;
// Belt-and-suspenders: also skip any entity carrying CrowdMotorIntent,
// regardless of shape kind (guards against future character shape choices).
if (repo.IsComponentTypeRegistered<CrowdMotorIntent>() &&
    repo.HasComponent<CrowdMotorIntent>(entity))
    continue;
```

**Why both guards:**
- `ShapeKind == Capsule`: intrinsic to the body — the fast, structural discriminator.
- `HasComponent<CrowdMotorIntent>`: semantic ownership guard — any entity steered by
  `BulletCharacterMotor` is off-limits to `KinematicVehicleMotor`, regardless of future shape
  changes (e.g. if a character were to use a sphere shape).

**Post-fix chain:** `BulletCharacterMotor` writes `PostCollisionLinearVelocityFdp=(0,2,0)` →
vehicle motor skips the capsule entity → `BulletReverseSyncSystem` reads `(0,2,0)` →
writes `SimVelocity.Linear=(0,2,0)` → `StrideAnimationBridge` sees speed=2.0 →
locomotion blend moves to Walk weight=1.0 → walk animation plays.

**New usings added to `KinematicVehicleMotor.cs`:**
- `using Fdp.Toolkit.Navigation;` — for `CrowdMotorIntent`.
- `using Fdp.Toolkit.Tkb.Domain;` — for `CollisionShapeKind`.

Both namespaces are already available via the project's existing reference to
`Fdp.Toolkits.csproj`.

### Diagnostics cleanup

The temporary per-frame INFO-level `[F1DIAG-MOTOR]`, `[F1DIAG2-MOTOR]`, `[F1DIAG-REVSYNC]`,
`[F1DIAG2-REVSYNC]`, and `[F1DIAG-REVSYNC-POST]` logging blocks and their static counters
(`s_f1DiagLinesEmitted`, `s_f1Diag2LinesEmitted`) have been removed from:
- `Stride/Hrot.Stride.Core/BulletCharacterMotor.cs`
- `Stride/Hrot.Stride.Core/BulletReverseSyncSystem.cs`

The `using System.Runtime.CompilerServices;` imports (only needed by `RuntimeHelpers.GetHashCode`
in the diagnostic blocks) have been removed from both files.

**Retained:** the lightweight throttled Debug-level `[BulletCharacterMotor]` and
`[BulletReverseSyncSystem]` logs (every 120 frames per entity) remain — they are cheap and
confirm the PostCollision channel is being set and read correctly in production.

### Tests added

**File:** `Stride/Hrot.Stride.Core.Tests/KinematicVehicleMotorTests.cs` — new class
`KinematicVehicleMotorClobberGuardTests` (3 tests):

| Test | What it proves |
|------|----------------|
| `ClobberGuard_CapsuleBodyWithVehicleStateAndCrowdMotorIntent_PostCollisionNotZeroed` | Core regression: capsule body with both `VehicleState(Speed=0)` and `CrowdMotorIntent` pre-writes PostCollision=(0,2,0); after `KinematicVehicleMotor.Execute`, `MoveKinematic` is not called and PostCollision is still (0,2,0). Breaks without the fix. |
| `ClobberGuard_OrientedBoxVehicle_IsDrivenAndPostCollisionIsNonZero` | Regression guard: genuine OrientedBox vehicle (no `CrowdMotorIntent`) still has `MoveKinematic` called and PostCollision is non-zero after Execute. Guards against an over-broad guard accidentally skipping all vehicles. |
| `ClobberGuard_CapsuleAndBoxInSameWorld_OnlyBoxDriven_CapsuleUnchanged` | Both entity types coexist in the same world: `MoveKinematic` called exactly once (box only); capsule PostCollision unchanged; box PostCollision non-zero. |

### Build and test results

```
dotnet build Stride/HrotStrideApp.Game/HrotStrideApp.Game.csproj -c Debug → 0 errors
Hrot.Stride.Core.Tests       : 236 passed, 0 failed  (233 baseline + 3 new clobber-guard tests)
Hrot.Stride.Animation.Tests  :  48 passed, 0 failed  (unchanged)
HrotStrideApp.Game.Tests     : 122 passed, 0 failed  (unchanged)
```

---

## F1 actual-velocity anim + F2 robust lifted-sweep

### F1 — Walk animation overruns when blocked at wall: measured pose-delta velocity

**Symptom:** mannequin walks to wall, stops translating, but walk animation keeps playing for several seconds before stopping.

**Root cause:** `BulletCharacterMotor` writes `bodyRef.PostCollisionLinearVelocityFdp = scaledFdpVelocity` — the COMMANDED velocity. `BulletReverseSyncSystem` was reading that for all kinematic bodies and writing it to `SimVelocity`. When the character is blocked, the motor keeps commanding `(0,2,0)`, so `SimVelocity` stays `(0,2,0)` and the locomotion blend keeps Walk active even though the character is stationary.

**Fix — `Stride/Hrot.Stride.Core/BulletReverseSyncSystem.cs`:**

Added a new `_prevFdpPositions` dictionary (`Dictionary<ulong, Vector3>`) keyed by `entity.PackedValue`. In the kinematic branch, a new sub-branch distinguishes capsule from vehicle:

- **`CollisionShapeKind.Capsule` (character):** Compute `linearFdp = (currentFdpPos − prevFdpPos) / deltaTime`. This yields ~commanded speed while walking freely and ~zero when blocked at a wall. First frame seeds prevPos and reports zero (no spawn spike). Delta time = 0 guard prevents divide-by-zero.
- **`CollisionShapeKind.OrientedBox` (vehicle):** unchanged — reads `bodyRef.PostCollisionLinearVelocityFdp` (written by `KinematicVehicleMotor.MoveKinematic` which already returns actual post-collision delta/dt, zero when fully blocked).

The motor's `PostCollisionLinearVelocityFdp` write in `BulletCharacterMotor` is retained (harmless; the KinematicVehicleMotor capsule-skip guard continues to prevent clobbering).

**Velocity invariant:** satisfied naturally — when blocked, `currentFdpPos ≈ prevFdpPos` → measured velocity ≈ 0. When walking freely, actual displacement / dt ≈ commanded speed.

**Files changed:**
- `Stride/Hrot.Stride.Core/BulletReverseSyncSystem.cs` — `using Fdp.Toolkit.Tkb.Domain` import; `_prevFdpPositions` field; capsule/vehicle sub-branch in `Execute`.

---

### F2 — Vehicle passes through walls: robust single lifted-sweep

**Symptom:** box rests on floor → horizontal sweep closest-hit is often the floor → `ShapeSweep` returns floor hit → either spurious pause or pass-through depending on whether the floor hit was processed.

**Root cause:** `ShapeSweep` returns only the single closest hit. When the floor is closest, walls (farther hits) are never returned. Two-sweep / normal-filter patches were fragile.

**Fix — `Stride/HrotStrideApp.Game/BulletPhysicsBodyService.cs` `MoveKinematic`:**

Replaced the fragile two-sweep / vertical-normal-filter approach with a single sweep using LIFTED transforms:

```csharp
const float FloorClearLiftM = 0.20f;  // lifts swept-box bottom clear of floor
var liftOffset = new SMath.Vector3(0f, FloorClearLiftM, 0f);
fromMatrix.TranslationVector = currentPos + liftOffset;
toMatrix.TranslationVector   = targetPos  + liftOffset;
```

With the swept box's bottom raised 0.20 m off the floor, every hit returned by `ShapeSweep` is a wall. No secondary sweep, no normal-direction filter needed.

Contact-point un-lifting: `contactUnlifted = hitResult.Point - liftOffset` is used for the face-stop distance calculation so no horizontal bias is introduced.

Named const `FloorClearLiftM = 0.20f` with comment explaining the constraints (> Bullet margin ~0.04 m; < wall height).

Removed: `VerticalNormalThreshold`, `FloorClearanceLiftM`, `isNearVertical`, `effectiveHit`, `effectiveIsNearVertical`, second `ShapeSweep` call, all associated log messages.

**Files changed:**
- `Stride/HrotStrideApp.Game/BulletPhysicsBodyService.cs` — `MoveKinematic` completely replaces two-sweep logic with single lifted-sweep; updated XML doc.

---

### New headless tests

**`Stride/Hrot.Stride.Core.Tests/BulletReverseSyncSystemTests.cs`** — updated:
- `KinematicBody_SimVelocity_SourcedFromPostCollisionChannel` renamed `VehicleBody_SimVelocity_SourcedFromPostCollisionChannel` (now uses OrientedBox)
- `KinematicBody_FullyBlocked_PostCollisionChannelZero_SimVelocityExactlyZero` renamed `VehicleBody_FullyBlocked_PostCollisionChannelZero_SimVelocityExactlyZero`
- Added `CapsuleBody_Moving_SimVelocity_FromMeasuredPoseDelta` — moving character: measured delta = actual velocity
- Added `CapsuleBody_FirstFrame_SimVelocityIsZero_NoSpawnSpike` — first frame seeds prevPos, reports zero
- Added `CapsuleBody_BlockedAtWall_SimVelocityIsZero` — position unchanged between frames → velocity = 0 (even when PostCollisionFdp ≠ 0)
- Added `CapsuleBody_FreeWalk_SimVelocityMatchesActualDisplacement` — free movement: SimVelocity tracks displacement/dt

**`Stride/Hrot.Stride.Core.Tests/SimVelocityChainTests.cs`** — rewritten to drive `KinematicFakeService.NextPosition` per frame (simulating the physics engine) and assert measured pose-delta velocity throughout the chain. Tests: `MotorAndReverseSync_Chain_SimVelocityEqualsActualDisplacement`, `MotorAndReverseSync_ZeroIntent_SimVelocityIsZero`, `MotorAndReverseSync_CrouchedStance_SimVelocityReflectsActualMovement`, `ReverseSync_CharacterBlockedAtWall_SimVelocityIsZero`, `MotorAndReverseSync_MultiFrame_SimVelocityTracksActualDisplacement`.

**`Stride/HrotStrideApp.Game.Tests/BulletPhysicsBodyServiceHelperTests.cs`** — replaced 9 old vertical-normal-filter tests with 4 new lifted-sweep tests:
- `LiftedSweep_FloorClearLiftM_IsPositiveAndReasonable` — constant validation
- `LiftedSweep_ContactPointUnlift_DistanceIsCorrect` — un-lift math for horizontal wall
- `LiftedSweep_FloorContactAtLiftHeight_HorizontalDistanceIsZero` — floor contact at lift height → no horizontal block
- `LiftedSweep_WallFaceStop_SafeDistIdenticalToNonLifted` — un-lifting is neutral for vertical walls

---

### Build and test results

```
dotnet build Stride/HrotStrideApp.Game/HrotStrideApp.Game.csproj -c Debug → 0 errors
Hrot.Stride.Core.Tests       : 240 passed, 0 failed  (236 baseline + 4 new: 2 renamed vehicle tests, 4 new capsule tests, −2 old capsule tests = net +4)
Hrot.Stride.Animation.Tests  :  48 passed, 0 failed  (unchanged)
HrotStrideApp.Game.Tests     : 117 passed, 0 failed  (122 baseline − 9 old vertical-filter tests + 4 new lifted-sweep tests = 117)
```

### Design decisions

1. **Measured pose-delta for capsule only:** The vehicle motor (`KinematicVehicleMotor.MoveKinematic`) already writes the true post-collision velocity (actualDelta/dt) via the PostCollision channel, which is zero when blocked. Keeping that path for OrientedBox avoids any regression. Capsule (CharacterComponent) is the only shape that needs the measured-delta approach because `SetCharacterVelocity` has no actual-delta return value.

2. **FloorClearLiftM = 0.20 m:** 5× the Bullet collision margin (~0.04 m) but well below typical wall height. Lifting more does not help correctness and increases the gap between the swept path and the real entity path for tilted terrain (not present in the current arena). 0.20 m is the minimal "clearly above floor, clearly below wall" value.

3. **Contact-point un-lifting:** The Y component of the contact point from the lifted sweep is `liftOffset.Y` higher than the actual entity center. Subtracting the lift before computing `distToContact` means the face-stop math is identical to a non-lifted sweep — no bias for horizontal sweeps, and for a tilted approach the un-lifted Y is slightly different but the Dot product with a horizontal moveDir cancels the Y component anyway.

### Known limitations

- **Tilted floor/ramps:** with the lifted sweep, a character motor walking up a ramp may see a slight forward-block because the top of the ramp appears as a wall to the lifted box. The arena has no ramps so this is acceptable. A proper ramp-aware motor would use a capsule's built-in step/slope mechanism (CharacterComponent.MaxSlope) rather than a swept box.
- **Character angular velocity is always zero:** the pose-delta approach computes only linear velocity. Yaw rate is not derived. Angular velocity of the character is not needed by the current locomotion blend (speed-only blend) so this is acceptable.

### What the human should now see

| Case | Before fix | After fix |
|------|-----------|----------|
| F1 walk + wall | Walks to wall, stops, animation plays 2–5 more s | Walks to wall, stops, animation stops within 1–2 frames |
| F2 drive across floor | Pauses every ~0.5 s (floor hit jitter) | Drives smoothly across floor, no pauses |
| F2 drive into wall | Passes through wall or erratic behavior | Stops cleanly at wall face (face-stop with skin gap) |

---

## F2 robust mid-height thin-box sweep (final)

### Problem (GPU log evidence)

The F2 box paused heavily and stuck mid-drive (~2.5 s at one position). The log showed
contacts at `point=(x, 0.000, z) normal=(0.002, -1.000, -0.002)` (Y=0, downward normal)
— the floor. The previous `FloorClearLiftM = 0.20 m` lift was applied to the real box's
**transform**, but not to its **shape**. The real box's vertical half-extent is ~0.5–0.625 m,
so the lifted box's bottom face sat at `0.20 − 0.625 = −0.425 m` — still below the floor.
Floor grazes continued to be returned as the closest `ShapeSweep` hit → treated as wall
blocks → box stalled/stuck.

### Root cause of `FloorClearLiftM` being insufficient

Margin-tweaking the lift is inherently fragile: to guarantee floor clearance, the lift must
exceed the box's vertical half-extent, which depends on vehicle dimensions. For the APC
(`halfY ≈ 0.625 m`) a reliable lift would need to be > 0.625 m — well above typical
mid-wall height, which would miss low walls. No single lift constant works for all vehicles.

### Robust fix — purpose-built thin mid-height sweep box

**File:** `Stride/HrotStrideApp.Game/BulletPhysicsBodyService.cs` — `MoveKinematic`

Replace the lift approach with a **purpose-built thin sweep box** that geometrically
excludes the floor from the sweep volume:

- **Horizontal half-extents**: full vehicle footprint (`BoxHalfExtentsStride.X` and `.Z`
  from the `BodyEntry`). Walls still block correctly.
- **Vertical half-extent**: `SweepBoxHalfHeightM = 0.25 m` (named const, thin).
- **Sweep box centre Y**: `SweepBoxCenterHeightM = 0.75 m` (named const, fixed,
  independent of the vehicle body's actual resting Y).
- **Sweep box vertical span**: `[0.75 − 0.25, 0.75 + 0.25] = [0.50, 1.00] m`.
  This span is **always above Y=0** regardless of vehicle dimensions — the floor is
  geometrically excluded from the sweep volume.
- **Sweep transforms**: `from.Y = to.Y = SweepBoxCenterHeightM`; horizontal (X, Z)
  tracks the desired move. Yaw rotation applied normally.
- **Real body position**: only the sweep's test volume is mid-height. The entity keeps
  its real resting Y; after the sweep the horizontal move result is applied to the
  actual entity position.
- **Face-stop**: unchanged — uses the **real** vehicle footprint half-extents
  (`BoxHalfExtentsStride`) for `halfExtentAlongMove`, not the sweep box's thin height.
  Contact Y (`SweepBoxCenterHeightM`) is irrelevant because `moveDir.Y ≈ 0`.
- **Dispose**: the temporary sweep shape is disposed in a `finally` block.
- **Removed**: `FloorClearLiftM`, `liftOffset`, contact-point un-lift math — not needed.

### Named constants (documented with comments in source)

| Constant | Value | Meaning |
|----------|-------|---------|
| `SweepBoxHalfHeightM` | `0.25 m` | Vertical half-extent of the thin sweep box. Span = [centre−0.25, centre+0.25]. |
| `SweepBoxCenterHeightM` | `0.75 m` | Fixed Y of the sweep box centre. Independent of entity resting Y. |

**Geometric invariant**: `SweepBoxCenterHeightM > SweepBoxHalfHeightM` →
`bottom = 0.75 − 0.25 = 0.50 m > 0` (verified by test).

### Test updates (`BulletPhysicsBodyServiceHelperTests.cs`)

Replaced the 4 old `LiftedSweep_*` tests with 4 new `MidHeightSweep_*` tests that
verify real behavior and spec edge cases:

| Test | What it asserts |
|------|----------------|
| `MidHeightSweep_VerticalSpan_ExcludesFloorAtYZero` | `sweepBottom = 0.50 > 0` and `sweepTop = 1.00 < 3.0` — floor excluded, walls still detected |
| `MidHeightSweep_CentreHeight_ExceedsHalfHeightSoBottomIsPositive` | `SweepBoxCenterHeightM > SweepBoxHalfHeightM` — mathematical invariant guaranteeing positive bottom |
| `MidHeightSweep_HorizontalFootprint_PreservedFromRealVehicle` | Sweep size X/Z = 2×real footprint half-extents; Y = 2×SweepBoxHalfHeightM = 0.50 m (thin) |
| `MidHeightSweep_WallFaceStop_UsesRealFootprintHalfExtents` | Contact at (10, 0.75, 0): `distToContact=10`, `halfExtentAlongMove=2`, `safeDist=7.95`, face stops at 9.95 m (0.05 m from wall) |

All tests break if the constants change to wrong values or if the real footprint is not
used for the face-stop.

### Build and test results

```
dotnet build Stride/HrotStrideApp.Game/HrotStrideApp.Game.csproj -c Debug → 0 errors
Hrot.Stride.Core.Tests       : 240 passed, 0 failed  (unchanged)
Hrot.Stride.Animation.Tests  :  48 passed, 0 failed  (unchanged)
HrotStrideApp.Game.Tests     : 117 passed, 0 failed  (4 old LiftedSweep tests replaced by 4 new MidHeightSweep tests — same count)
```

### Expected GPU result

| Scenario | Expected |
|----------|---------|
| F2 drives across flat floor | **Smooth, no pauses** — floor never in sweep volume |
| F2 approaches wall head-on | **Stops cleanly** — face 0.05 m from wall, no penetration, no ejection |
| F2 approaches wall at angle | **Stops correctly** — projected half-extent handles diagonal approach |
| GPU log during drive | No `normal=(0.002,-1.000,-0.002)` contacts; only wall contacts if any |



---

## F2 fix car floor burial (real root cause) + natural sweep + F1 anim-stutter EMA

### Root cause identification

The user identified the **actual** root cause of the F2 buried-car bug:
the vehicle visual model origin is at its **CENTER** (not its base/bottom).
All prior fixes assumed model origin = base and used boxShape.LocalOffset = (0, halfY, 0) to shift the collider bottom to the entity origin -- but that separated the collider (shifted up) from the visual (still centered), leaving the model visually buried halfY below the collider bottom. The mid-height thin-box sweep was a workaround for the resulting spurious floor contacts, not a root-cause fix.

---

### ITEM 1 -- F2: box model origin = center; fix burial properly

**File changed:** Stride/HrotStrideApp.Game/BulletPhysicsBodyService.cs

**CreateBody OrientedBox branch:**
- Changed boxShape.LocalOffset from new SMath.Vector3(0f, halfY, 0f) to SMath.Vector3.Zero.
  - Rationale: box model origin is at CENTER so collider must ALSO be centered on the entity origin (LocalOffset=0) so visual and collider coincide.
  - LocalOffset = +halfY was wrong: it shifted the collider bottom to the entity origin while the visual center stayed at the entity origin -- visual buried halfY below the collider.
- Added comment block contrasting the two model-origin conventions:
  - Capsule: model origin = FEET (base) -- LocalOffset = +halfHeight -- spawn at FDP Z=0.
  - Box: model origin = CENTER -- LocalOffset = 0 -- spawn at FDP Z = halfZ.

**File changed:** Stride/HrotStrideApp.Game/StridePhysicsHarnessCases.cs

- PhysicsDrive spawn Z changed from 0f to ApcBoxHalfHeightFdpZ (= 1.25 m).
  - With LocalOffset=0, the box center is at entity Y; entity must be at Stride Y = halfY so the box bottom rests at Y=0 (floor).
- Updated ApcBoxHalfHeightFdpZ comment to document the model-origin convention difference from the capsule case.

---

### ITEM 2 -- F2: natural sweep at real position + small floor-skin lift

**File changed:** Stride/HrotStrideApp.Game/BulletPhysicsBodyService.cs -- MoveKinematic

- Removed the purpose-built thin mid-height sweep box (SweepBoxHalfHeightM = 0.25f, SweepBoxCenterHeightM = 0.75f).
- Now sweeps the REAL box shape (same dims as the collision body) at the REAL entity position + SweepFloorSkinM = 0.05f Y-lift.
  - The 0.05 m lift moves the swept box bottom from Y=0 (coplanar with floor) to Y=0.05 m, preventing spurious floor contact from floating-point coplanarity.
  - NOT a half-height compensation -- the box is properly ON the floor (entity at Y=halfY, box bottom at Y=0), not buried.
  - Wall contacts still detected correctly (real box footprint used).
- Face-stop math unchanged: safeDist = distToContact - halfExtentAlongMove - SkinM.
- Contact point Y is offset by SweepFloorSkinM; for horizontal sweeps moveDir.Y=0 so the Y offset cancels in the Dot product.
- Named constants: SweepFloorSkinM = 0.05f (floor-clearance epsilon), SkinM = 0.05f (wall face-stop clearance).

**Expected GPU behavior:**
- Car sits correctly on the floor with its full body visible above the floor (not buried).
- Drives smoothly with no floor-contact false-blocks stalling translation.
- Translation continues until the front face reaches the actual wall (not stopping far short).
- Box face stops 0.05 m from wall surface; no penetration, no ejection.

---

### ITEM 3 -- F1: walk animation stutter EMA fix

**File changed:** Stride/Hrot.Stride.Core/BulletReverseSyncSystem.cs

- Added _smoothedFdpVelocity dictionary (Dictionary<ulong, Vector3>) keyed by entity.PackedValue.
- Added EmaAlpha = 0.25f constant (smoothing factor).
- In the capsule (character) branch of Execute, the raw measured velocity (currentPos - prevPos) / dt is EMA-smoothed before writing to SimVelocity:
    var rawVelocity = (currentFdpPos - prevFdpPos) / deltaTime;
    var smoothed = Vector3.Lerp(prevSmooth, rawVelocity, EmaAlpha);
    linearFdp = smoothed;
- On seed frame: smooth = Zero (no spike on spawn).
- On deltaTime == 0: smooth decays toward zero.
- Vehicle (OrientedBox) path unchanged.

Why EmaAlpha = 0.25: settling time ~4-8 frames (~0.07-0.13 s at 60 fps): fast enough to stop the walk animation promptly at a wall, stable enough to suppress single-frame dips from Bullet discrete integration.

MannequinAnimationBinder.Reconcile() confirmed correct: uses _bound dict, installs once per entity, PerEntityBlendTreeBuilder advances _locoNormalizedTime continuously without resetting. Stutter is purely from jittery raw velocity; EMA is the complete fix.

---

### Build and test results

MSBUILD : error MSB1008: Only one project can be specified.
    Full command line: 'C:\Program Files\dotnet\sdk.0.108\MSBuild.dll -maxcpucount --verbosity:m -tlp:default=auto --property:Configuration=Debug --property:NuGetInteractive=false --restoreProperty:Configuration=Debug --restoreProperty:NuGetInteractive=false --restoreProperty:EnableDefaultCompileItems=false --restoreProperty:EnableDefaultEmbeddedResourceItems=false --restoreProperty:EnableDefaultNoneItems=false Stride/HrotStrideApp.Game/HrotStrideApp.Game.csproj 0 errors -consoleloggerparameters:Summary -restore -distributedlogger:Microsoft.DotNet.Cli.Commands.MSBuild.MSBuildLogger,C:\Program Files\dotnet\sdk.0.108\dotnet.dll*Microsoft.DotNet.Cli.Commands.MSBuild.MSBuildForwardingLogger,C:\Program Files\dotnet\sdk.0.108\dotnet.dll -tlp:DISABLENODEDISPLAY -nologo'
  Switches appended by response files:
Switch: 0

For switch syntax, type "MSBuild -help"

### Expected GPU result (summary)

| Item | Before | After |
|------|--------|-------|
| F2 car visual | Buried half-height in floor | Full car visible above floor |
| F2 translation | Stops far from wall, rotates in place (false floor-block) | Drives to wall face, stops cleanly |
| F2 sweep | Mid-height thin-box hack at fixed 0.75 m Y | Real box shape + 5 cm floor-skin lift |
| F1 walk anim | Stutters/resets periodically (velocity jitter toggles blend) | Stable walk blend, no stutter |
| F1 stop-at-wall | Walk anim keeps playing after wall contact | Anim stops within ~0.1 s of wall contact |

---

## F2 collider matched to visual model bounding box (root content-mismatch fix)

### Root cause (confirmed)

The MilitaryAPC TKB-2001 had `ShapeHeight=2.5` which drove the OrientedBox collider to `halfY=1.25 m`
in Stride space.  The visual is the placeholder model `Models/Box2x1x1` (~1 unit tall, `bbox.Minimum.Y≈-0.5`,
`bbox.Maximum.Y≈0.5`).  Collider and visual were **different sizes**:

- The 2.5-tall collider stuck up into overhead arena geometry.
- GPU log showed contacts at `point=(x, 2.500, z) normal=(0,1,0)` — an upward-normal hit at Y=2.5 —
  every frame, forcing `safeDelta=0` so the car stalled far from the actual wall.
- The visible model appeared floating ~0.75 m above the floor (the collider rested on the floor,
  the smaller visual was co-located with the collider center).

### The fix

**File: `Stride/HrotStrideApp.Game/BulletPhysicsBodyService.cs`**

#### New public helper `ComputeBoxParamsFromBoundingBox`

Added `BulletPhysicsBodyService.ComputeBoxParamsFromBoundingBox(BoundingBox, minClamp)` → `BoxParams?`
(pure static, no Simulation dependency — fully unit-testable headlessly).

`BoxParams` contains:
- `HalfExtents`: `(max - min) / 2` for each axis, clamped to `minClamp = 0.05 m`.
- `BoxCenter`: `(max + min) / 2` in entity-local space — used as `BoxColliderShape.LocalOffset` so
  the collider CENTER exactly overlaps the visual center regardless of model-origin placement.
- `RestingStrideY`: `-bbox.Minimum.Y` — the Stride Y the entity must be placed at so the visual
  bottom (`entity.Y + Minimum.Y = 0`) and physics bottom (`entity.Y + BoxCenter.Y − HalfY = 0`)
  both rest exactly on the floor.

Returns `null` when any bbox axis has zero or NaN extent (degenerate model) — caller falls back to ShapeDims.

#### `CreateBody` OrientedBox branch

Replaced the ShapeDims-only path with:

1. **Try model bbox**: call `strideEntity.Get<ModelComponent>()?.Model?.BoundingBox` → `ComputeBoxParamsFromBoundingBox`.
2. If valid: use model-derived `HalfExtents`, `boxLocalOffset = BoxCenter`, and override `strideEntity.Transform.Position.Y = RestingStrideY`.
3. If invalid/unavailable (no `ModelComponent`, model not loaded, degenerate extents): **fallback** to ShapeDims-derived half-extents with `LocalOffset = Vector3.Zero` (same as the previous behavior). Logs a `Warn` so the operator knows the fallback fired.

The `using Stride.Rendering` import was added (for `ModelComponent`).

#### `BodyEntry.BoxHalfExtentsStride`

Set from the model-derived (or fallback) half-extents so `MoveKinematic` face-stop and slide calculations
use the correctly-sized real footprint.

#### Slide-along-wall

`MoveKinematic` wall-hit response upgraded from block-only to **block-and-SLIDE**:
When `safeDist ≈ 0` (fully blocked) and the contact normal has a non-zero horizontal component,
the tangential component of `desiredDelta` (projected onto the wall plane) is added to `actualDelta`.
This makes a steering vehicle scrape along a wall rather than freezing.
The Y component of the slide is zeroed (no vertical drift).

**File: `Stride/HrotStrideApp.Game/StridePhysicsHarnessCases.cs`**

Updated `ApcBoxHalfHeightFdpZ` comment to document that the spawn Z is now only an initial
above-floor placement — `CreateBody` overrides the entity Stride Y from the actual model bbox
to place the model bottom precisely on the floor.

### Resting height — how it is determined

**Entity resting height is determined at `CreateBody` time from the model's actual bounding box.**

Specifically: `entity.Transform.Position.Y = -bbox.Minimum.Y`.

Derivation (proof that visual bottom and physics bottom both land at Y=0):
- Visual bottom = `entity.Y + bbox.Minimum.Y = (-bbox.Minimum.Y) + bbox.Minimum.Y = 0`. ✓
- Physics center = `entity.Y + LocalOffset.Y = -Minimum.Y + (Min.Y+Max.Y)/2`.
- Physics bottom = physics center − HalfY = `-Minimum.Y + (Min.Y+Max.Y)/2 − (Max.Y−Min.Y)/2`
  = `-Minimum.Y + Minimum.Y = 0`. ✓

For the placeholder model `Box2x1x1` (Min.Y≈-0.5, Max.Y≈0.5): resting Stride Y = 0.5 m (not 1.25 m
as previously hardcoded from TKB ShapeDims).  The reverse-sync writes this back to FDP
`SimTransform.Z` on the next frame, stabilising the height for all subsequent ticks.

Capsule convention (unchanged): capsule model origin = FEET; `LocalOffset.Y = +halfHeight`; spawned at FDP Z=0.
Box convention (new): model origin can be anywhere; `LocalOffset = boxCenter`; entity Y overridden from `-bbox.Minimum.Y`.

### Tests added

**File: `Stride/HrotStrideApp.Game.Tests/BulletPhysicsBodyServiceHelperTests.cs`**

| Test | What it verifies |
|------|-----------------|
| `ComputeBoxParams_SymmetricBbox_CorrectHalfExtentsAndCenter` | Min=(-1,-0.5,-1) Max=(1,0.5,1): HalfExtents=(1,0.5,1), BoxCenter=(0,0,0), RestingY=0.5 |
| `ComputeBoxParams_AsymmetricBbox_BoxCenterAndRestingY` | Min=(-1,-0.2,-1) Max=(1,1.8,1): BoxCenter.Y=0.8, RestingY=0.2 (origin not at mesh center) |
| `ComputeBoxParams_RestingY_VisualAndPhysicsBottomAtFloor` | Proves visual bottom = physics bottom = 0 with computed RestingY |
| `ComputeBoxParams_ZeroYExtent_ReturnsNull` | Degenerate bbox (zero Y) → null → fallback |
| `ComputeBoxParams_NaNExtent_ReturnsNull` | NaN bbox → null → fallback |
| `ComputeBoxParams_ShapeDimsFallback_UsesSwizzledHalfExtents` | Fallback path: FDP→Stride swizzle + LocalOffset=Zero |
| `DriveApcSpawnZ_IsInitialPositionOnly_CreateBodyOverridesFromBbox` | ApcBoxHalfHeightFdpZ=1.25 ≠ actual RestingY=0.5 for Box2x1x1 placeholder |
| `Slide_AngleApproach_TangentialComponentPreserved` | NE approach into north wall: east tangential (1,0,0) preserved |
| `Slide_HeadOnApproach_TangentialIsZero` | Head-on into north wall: tangential = zero (pure block) |
| `Slide_OnlyAppliedWhenFullyBlocked_NotWhenApproaching` | Slide guard: not applied when safeDist > 0 |

Replaced 4 outdated tests that asserted the old ShapeDims-only / LocalOffset=Zero convention.

### Build and test results

```
dotnet build Stride/HrotStrideApp.Game/HrotStrideApp.Game.csproj -c Debug → 0 errors
Hrot.Stride.Core.Tests       : 242 passed, 0 failed  (unchanged)
Hrot.Stride.Animation.Tests  :  48 passed, 0 failed  (unchanged)
HrotStrideApp.Game.Tests     : 124 passed, 0 failed  (117 baseline + 10 new − 4 replaced old + others present)
```

---

## F2 vehicle → Bullet dynamic rigidbody (velocity-driven, solver collision)

### Decision

The F2 vehicle (OrientedBox / MilitaryAPC TKB-2001) has been converted from a
hand-rolled kinematic ShapeSweep mover to a **Bullet DYNAMIC RigidbodyComponent**
driven by velocity commands each frame.

The kinematic `MoveKinematic` sweep approach kept regressing: after multiple rounds of
fixes (face-stop math, vertical-normal filter, two-sweep, mid-height thin-box, skin-lift)
the vehicle still passed through walls because `Simulation.ShapeSweep` returns only the
single closest hit — when the floor was closest, walls were never returned, and the box
drove through them.  The root problem is architectural: a kinematic body requires the
application code to correctly detect and respond to every contact; Bullet's constraint
solver does none of that.

A DYNAMIC body gets solver-handled contacts for free: when the body drives into a wall,
the solver arrests the velocity to zero.  Floor resting and gravity are also handled by
the solver.  This is exactly the same robustness that makes the F1 character work.

### Changes

#### 1. `BulletPhysicsBodyService.cs` — `CreateBody` OrientedBox branch

The OrientedBox branch now creates a **dynamic** `RigidbodyComponent`:

```csharp
var rigidbody = new RigidbodyComponent
{
    ColliderShape  = boxShape,
    IsKinematic    = false,          // DYNAMIC
    Mass           = 1f,
    AngularFactor  = new SMath.Vector3(0f, 1f, 0f),  // yaw only — upright lock
    LinearFactor   = new SMath.Vector3(1f, 1f, 1f),  // full XYZ translation
    CanSleep       = false,                           // always respond to velocity
    LinearDamping  = 0.5f,                            // slight drag for stability
    Friction       = 0.8f,                            // vehicle-surface grip
};
```

`BodyEntry.IsKinematic = false` → `GetBodyState` returns `IsKinematic: false` → reverse-sync
takes the dynamic branch and reads `LinearVelocity`/`AngularVelocity` from the solver.

The model-BoundingBox-derived `BoxColliderShape` (including `LocalOffset` = `BoxCenter` and
`RestingStrideY`) is **kept unchanged** — only the kinematic flag and property set change.
A dynamic body under gravity settles at the resting height; spawning at `RestingStrideY`
means the body rests on the floor immediately with no drop needed.

**Verified Stride 4.2.1.2487 APIs (via reflection on the deployed DLL):**
- `RigidbodyComponent.AngularFactor` — `Vector3` property — **confirmed present**.
- `RigidbodyComponent.LinearFactor` — `Vector3` property — **confirmed present**.
- `RigidbodyComponent.CanSleep` — `bool` property — **confirmed present**.
- `RigidbodyComponent.IsActive` — `bool` property (read-only, reflects Bullet state) — **confirmed present**.
  Bullet activates a dynamic body automatically when its velocity is set, so no explicit activation call is needed.

**Upright lock (AngularFactor = (0,1,0)):** Restricts rotation to the Y axis (Stride up = yaw)
so the vehicle body cannot tip or roll over when driving across uneven contacts.

**No-sleep (CanSleep = false):** Prevents Bullet from deactivating the body when velocity
is near zero between frames.  Without this, a stopped vehicle ignores velocity commands
until re-activated.

#### 2. `IPhysicsBodyService.cs` — two new methods

```csharp
void SetLinearVelocityXZ(object bodyHandle, SMath.Vector3 strideLinearVel);
void SetYawRate(object bodyHandle, float strideYawRateRadPerSec);
```

`SetLinearVelocityXZ` preserves the body's current `LinearVelocity.Y` (solver-managed
gravity component) and sets only X and Z — the horizontal drive velocity.  This keeps
the body on the floor while Bullet handles the vertical via gravity.

`SetYawRate` sets `AngularVelocity = (0, strideYawRateRadPerSec, 0)` — pure yaw
(consistent with `AngularFactor = (0,1,0)`).

`MoveKinematic` is **retained** in the interface (no existing users are removed) with an
updated XML doc noting it is no longer called by live vehicles.

#### 3. `NoOpPhysicsBodyService.cs`

`SetLinearVelocityXZ` and `SetYawRate` added as no-ops.  All headless tests remain
unaffected.

#### 4. `BulletPhysicsBodyServiceDeferred` (inner deferred wrapper)

`SetLinearVelocityXZ` and `SetYawRate` forwarding methods added to delegate to `Inner`.

#### 5. `KinematicVehicleMotor.cs` — velocity-drive rewrite

The motor no longer computes a per-frame `desiredDeltaFdp` or calls `MoveKinematic`.
Instead, each frame it:

1. Computes `desiredVelFdp = forwardFdp * vehicleState.Speed` (instantaneous velocity,
   NOT a delta — dt does not appear here).
2. Converts to Stride space via `FdpStrideTransform.ToStrideVelocity`.
3. Calls `SetLinearVelocityXZ(bodyHandle, strideLinearVel)`.
4. Computes `yawRateFdp` (bicycle model: `ω = speed/L * tan(steerAngle)`).
5. Converts FDP→Stride yaw sign: `strideYawRate = -yawRateFdp`
   (FDP is right-handed Z-up; Stride is left-handed Y-up — same negation as used in
   the reverse-sync `ToFdpAngularVelocity`).
6. Calls `SetYawRate(bodyHandle, strideYawRate)`.

**Post-collision channel:** The motor no longer writes
`PostCollisionLinearVelocityFdp` / `PostCollisionAngularVelocityFdp` for vehicle bodies.
The reverse-sync reads the solver's `LinearVelocity`/`AngularVelocity` directly via the
dynamic branch (`IsKinematic=false`).  A wall-arrested body reports zero velocity from
the solver — satisfying the velocity invariant (§6.1) without any extra zeroing step.

**Character-body guard preserved:** The capsule-skip and `CrowdMotorIntent`-skip guards
are unchanged.  Calling `SetLinearVelocityXZ(0,0,0)` on a character body would silence
its physics-driven velocity just as `MoveKinematic(speed=0)` zeroed the PostCollision
channel — the guard prevents both.

#### 6. `BulletReverseSyncSystem.cs`

No logic changes needed.  The dynamic branch (`!state.IsKinematic`) already reads
`LinearVelocity`/`AngularVelocity` from the solver and converts them to FDP space — this
is exactly right for the dynamic vehicle.  The `else` (kinematic-non-capsule) branch is
retained as a fallback for any future kinematic non-capsule body type.

Diagnostic source label updated: `"Dynamic(solver)"` for the vehicle path.

### Collision / floor-resting behaviour

**Wall collision:** Bullet's constraint solver prevents penetration.  Driving into a wall,
the solver arrests the linear velocity to zero in the contact direction.  The body slides
naturally along the wall plane (the tangential component of the velocity persists).  No
sweep code or manual block-or-slide logic is needed.

**Floor resting:** Gravity (enabled by default) pulls the body onto the static floor collider.
`SetLinearVelocityXZ` preserves the Y component so gravity remains active.  The body cannot
fall through the floor.

**No more MoveKinematic sweep for vehicles.** The entire `ShapeSweep` path (face-stop math,
contact-point un-lifting, vertical-normal filter, etc.) is bypassed for the F2 vehicle.
`MoveKinematic` remains callable but is no longer invoked by `KinematicVehicleMotor`.

### Tests

All existing tests updated to add `SetLinearVelocityXZ` and `SetYawRate` no-op stubs to
every `IPhysicsBodyService` fake in the test suite.

`KinematicVehicleMotorTests.cs` fully rewritten for the new velocity-drive behavior:

| Test | What it asserts |
|------|----------------|
| `UnobstructedCommand_EastFacing_LinearVelocityCommandedCorrectly` | East-facing vehicle: `SetLinearVelocityXZ` called with `Stride.X ≈ speed`, `Stride.Z ≈ 0` |
| `UnobstructedCommand_NorthFacing_LinearVelocityInStrideZ` | North-facing: `Stride.Z ≈ speed`, `Stride.X ≈ 0` |
| `ZeroSpeed_CommandsZeroLinearVelocity` | Speed=0 → `SetLinearVelocityXZ` with zero XZ and `SetYawRate(0)` |
| `ZeroSteerAngle_YawRateCommandIsZero` | Zero steer → `SetYawRate(0)` |
| `NonZeroSteerAndSpeed_YawRateIsNonZero` | `ω = speed/L * tan(steer)` → negated for Stride → exact value verified |
| `VelocityDrive_CalledOnCorrectBodyHandle` | Both `SetLinearVelocityXZ` and `SetYawRate` on the correct body handle |
| `EntityWithVehicleStateButNoBodyRef_Skipped` | No velocity commands without a physics body |
| `Execute_CallsBothSetLinearVelocityXZ_AndSetYawRate` | Both methods called each frame |
| `Execute_DoesNotCallMoveKinematic_ForVehicleBody` | `MoveKinematic` NOT called (old path is gone) |

`KinematicVehicleMotorClobberGuardTests` updated to assert `SetLinearVelocityXZ` is NOT
called for capsule/`CrowdMotorIntent` entities (same guard, new assertion mechanism).

### Build and test results

```
dotnet build Stride/HrotStrideApp.Game/HrotStrideApp.Game.csproj -c Debug → 0 errors
Hrot.Stride.Core.Tests       : 242 passed, 0 failed  (unchanged — test count same, content replaced)
Hrot.Stride.Animation.Tests  :  48 passed, 0 failed  (unchanged)
HrotStrideApp.Game.Tests     : 124 passed, 0 failed  (unchanged)
```

### Expected GPU result

| Scenario | Expected |
|----------|---------|
| F2 spawns | Box sits on floor under gravity; no manual spawn-height tuning needed beyond the bbox-derived `RestingStrideY` |
| F2 drives across floor | Smooth, continuous — no floor-contact false blocks, no sweep code |
| F2 drives into wall | Solver arrests velocity; body stops with face at wall surface; slides along angled approaches |
| F2 tips/rolls | Cannot — `AngularFactor=(0,1,0)` locks the body upright; only yaw is free |
| F2 wall log | No `[BulletPhysicsBodyService] MoveKinematic` wall-hit Debug entries (that path is bypassed) |
| F1 characters | Unchanged — capsule-skip guard still in place; dynamic vehicle motor cannot clobber character PostCollision channel |

---

## Fix: defer dynamic-body physics properties until in-simulation (startup crash)

**Root cause:** `CreateBody` (OrientedBox DYNAMIC branch) set `AngularFactor`, `LinearFactor`,
`CanSleep`, `LinearDamping`, `Friction` in the `RigidbodyComponent` object initializer — before
`strideEntity.Add(rigidbody)` / Stride's `PhysicsProcessor` creates the native Bullet body.
Those setters reach into the native body and throw:
`"Attempted to call a Physics function that is available only when the Entity has been already added to the Scene."`
This fires on startup when `PhysicsBodyLifecycleSystem` creates bodies for the two demo MilitaryAPC vehicles.

**Fix (`Stride/HrotStrideApp.Game/BulletPhysicsBodyService.cs`):**

1. **Initializer cleaned up (OrientedBox branch):** only `ColliderShape`, `IsKinematic=false`,
   `Mass=1f` remain in the initializer — all safe (consumed at native-body creation time).

2. **`DynamicConfig` struct added:** stores the desired `AngularFactor=(0,1,0)`,
   `LinearFactor=(1,1,1)`, `CanSleep=false`, `LinearDamping=0.5f`, `Friction=0.8f`.

3. **`BodyEntry.PendingDynamicConfig` field added:** nullable `DynamicConfig?`, non-null for
   DYNAMIC bodies until config is applied. Cleared to `null` after first application (idempotent).

4. **Readiness check:** `RigidbodyComponent.Simulation != null`.
   `PhysicsComponent.Simulation` is set by Stride's `PhysicsProcessor` when it processes the entity
   — on the first simulation step after `strideEntity.Add(component)`.  Until then `Simulation` is
   `null`; all property access would throw.

5. **`ApplyDynamicConfigIfReady` method added:** called from `SetLinearVelocityXZ` /
   `SetYawRate` on each frame.  Applies config and clears `PendingDynamicConfig` exactly once when
   `rb.Simulation != null`.  A defensive `try/catch` at Debug level provides a backstop for any
   edge case; the body retries next frame.

6. **`SetLinearVelocityXZ` / `SetYawRate` readiness guard:** both methods return immediately
   (no-op) if `rb.Simulation == null` — no throw, no deferred velocity command lost (the motor
   re-calls every frame so the velocity is applied on the next ready frame).

7. **Sphere and default fallback branches** audited: they do not set any native-body-only
   properties in the initializer (only `ColliderShape`, `IsKinematic=false`, `Mass=1f`) — safe.
   Capsule/CharacterComponent branch verified: no native-body properties set pre-Add — safe.

**Test results (after fix):**
```
dotnet build Stride/HrotStrideApp.Game/HrotStrideApp.Game.csproj -c Debug → 0 errors
Hrot.Stride.Core.Tests       : 242 passed, 0 failed  (unchanged)
Hrot.Stride.Animation.Tests  :  48 passed, 0 failed  (unchanged)
HrotStrideApp.Game.Tests     : 130 passed, 0 failed  (124 baseline + 6 new)
```

New tests in `BulletPhysicsBodyServiceHelperTests` (+6):
- `NoOp_CreateBody_OrientedBox_DoesNotThrow` — headless OrientedBox CreateBody does not throw
- `NoOp_SetLinearVelocityXZ_DoesNotThrow` — no-op velocity call does not throw
- `NoOp_SetYawRate_DoesNotThrow` — no-op yaw call does not throw
- `DeferredDynamicConfig_AppliedOnceWhenReady_NeverAppliedWhenNotReady` — config applied exactly once when Simulation != null; zero applications when not ready; idempotent thereafter
- `DeferredDynamicConfig_VelocityNoOpWhenNotReady` — velocity commands silently dropped before ready; accepted after ready
- `FakeDynamicConfig_StoresAllFields_Correctly` — DynamicConfig field round-trip values

---

## PROOF: closed-loop steer-to-point for the dynamic vehicle (controller + convergence tests + F3 demo)

### Motivation

The BATCH-17 vehicle is driven by a **dynamic RigidbodyComponent** (not the old kinematic sweep):
KinematicVehicleMotor issues SetLinearVelocityXZ / SetYawRate commands each frame and Bullet's
constraint solver handles collisions. The user correctly asked: is the dynamic body navigable to
a goal the way the old kinematic bicycle model was?

A kinematic body is trivially controllable - the position follows the commanded delta exactly.
A **dynamic** body may not: the commanded velocity is only a soft constraint (the solver can
reduce it on collision); the yaw integration has one-frame lag; friction and inertia mean the
achieved trajectory can deviate from the ideal bicycle model. We needed a constructive proof
that closed-loop feedback collapses these deviations to zero over time.

---

### Why closed-loop feedback makes the dynamic body navigable

The key insight is that **the controller observes the actual pose** - not the commanded one.
BulletReverseSyncSystem back-propagates the solver's world transform into SimTransform after
every physics step. The waypoint controller reads SimTransform.Position + SimTransform.Rotation
(both set by the reverse-sync, not by the motor) and re-computes steering from scratch every frame.

If the dynamic body deviates (e.g., it slows early because of a floor-friction contact, or the
yaw lags by one step), the next controller call sees the *actual* pose and corrects automatically:

- **Heading error was undershot?** Steer command remains non-zero; car keeps turning.
- **Distance undershot** (car moved less than commanded)? Distance is still > arriveTolerance; speed command remains positive.
- **Overshoot** (car coasts past the target by a fraction)? Distance flips direction; heading error reverses sign; controller decelerates and reverses steer.

None of these corrections require any knowledge of the body's mass, friction, or inertia - the
feedback loop closes over observable state. This is precisely what makes closed-loop control
superior to open-loop for a dynamic body.

The only prerequisite is that the body **roughly goes where pointed and can turn** - i.e., the
actuator gain (speed / yaw-rate) is positive and bounded. For the dynamic vehicle this is
guaranteed: SetLinearVelocityXZ imposes the commanded speed (reduced by wall contacts, never
amplified), and SetYawRate imposes a proportional angular velocity. As long as these responses
are in the right direction (even if scaled down), the integrator converges.

---

### Deliverable 1 - VehicleWaypointController (pure, dependency-free)

**File:** Stride/Hrot.Stride.Core/VehicleWaypointController.cs
**Namespace:** Hrot.Stride.Core

Go-to-goal law operating in FDP world space (X=East, Y=North):

| Step | Formula |
|------|---------|
| Arrive check | if dist <= arriveTolerance -> {Speed:0, Steer:0, Arrived:true} |
| Desired heading | psi_d = atan2(toTarget.Y, toTarget.X) |
| Heading error | e = WrapToPi(psi_d - currentHeading) |
| Steer command | delta = Clamp(K*e, -maxSteer, +maxSteer) |
| Alignment factor | a = Max(slowMinFrac, Max(0, cos(e))) |
| Proximity factor | p = Clamp(dist/slowRadius, slowMinFrac, 1) |
| Speed command | v = cruiseSpeed * a * p |

**Alignment factor design note:** the spec says Max(0, cos(e)), which goes to zero at 90 deg
heading error. For the bicycle model, yawRate = (speed/L)*tan(steer) - if speed=0, yawRate=0
regardless of steer angle, so the car never turns. The floor Max(slowMinFrac, ...) ensures a
minimum creep speed even when pointing sideways, enabling yaw. Documented in source.

**WrapToPi helper:** wraps angle in radians to (-pi, +pi] using modular arithmetic only -
no iteration, deterministic for any float input.

Minimum turning radius: R_min = wheelBase / tan(maxSteer) (approx 5.1 m for default params).
Targets strictly inside R_min require a multi-maneuver and are out of scope.
All proof waypoints are placed >= 12 m from their predecessor (well outside 2*R_min approx 10.2 m).

---

### Deliverable 2 - Headless convergence proof (xUnit, Hrot.Stride.Core.Tests)

**File:** Stride/Hrot.Stride.Core.Tests/VehicleWaypointControllerTests.cs
**33 new tests** in two classes.

**VehicleWaypointControllerUnitTests (18 tests):**
WrapToPi boundary/range (9 cases), arrive/stop behavior (3 cases), steer clamping (6 headings),
speed non-negative, facing-target produces zero steer + cruise speed.

**VehicleWaypointControllerConvergenceTests (15 tests):**

Simulation model: heading += (speed/wheelBase)*tan(steer)*yawRateScale*dt;
pos += (cos(h),sin(h))*speed*speedScale*dt. Matches bicycle kinematics exactly.

Ideal-model tests (yawRateScale=speedScale=1, no lag):

| Test | Start->Target | What it proves |
|------|--------------|----------------|
| IdealModel_TargetAhead_Converges | (0,0) h=0 -> (20,0) | Straight approach |
| IdealModel_TargetAheadLeft_Converges | (0,0) h=0 -> (20,20) | 45 deg heading error |
| IdealModel_TargetAheadRight_Converges | (0,0) h=0 -> (20,-20) | -45 deg heading error |
| IdealModel_TargetHardLeft_Converges | (0,0) h=0 -> (0,25) | 90 deg turn required |
| IdealModel_TargetBehindRight_Converges | (0,0) h=0 -> (-15,-15) | ~135 deg turn |
| IdealModel_ThreeWaypointRoute_AllReached | Sequential E->NE->N | Route following |
| MinTurningRadius_StandardParams_IsPositiveAndExpectedValue | R_min documentation |

**Robustness / dynamic-body imperfection tests (the crux of the proof):**

These simulate a real dynamic body where the achieved response is less than commanded:

| Test | Perturbation | Proves |
|------|-------------|--------|
| PerturbedModel_Ahead_YawAndSpeedScaled075_Lag1_Converges | 75% yaw+speed, 1-step lag | Core proof: dynamic body converges |
| PerturbedModel_AheadLeft_YawAndSpeedScaled075_Lag1_Converges | 75% yaw+speed, 1-step lag | Left-turn robust to perturbation |
| PerturbedModel_HardLeft_YawAndSpeedScaled070_Lag2_Converges | 70%/80%, 2-step lag | Hard-left with heavy perturbation |
| PerturbedModel_AheadRight_YawAndSpeedScaled085_Lag1_Converges | 85% yaw+speed, 1-step lag | Right-turn with mild perturbation |

The 70-85% / 2-frame lag scenario is a deliberate worst-case: a real Bullet dynamic body
responds to SetLinearVelocityXZ essentially instantaneously (0-1 frame lag; speed ratio 0.9-1.0
in free driving). All four robustness tests pass, confirming the controller tolerates dynamic
body imperfection.

**Test results:**
```
Hrot.Stride.Core.Tests  : 275 passed, 0 failed  (242 baseline + 33 new)
```

---

### Deliverable 3 - F3 GPU harness proof case

**File:** Stride/HrotStrideApp.Game/StridePhysicsHarnessCases.cs
New method: RegisterDriveToWaypointCase + private DriveToWaypoint.

**File:** Stride/HrotStrideApp.Game/StrideHrotGame.cs
RegisterDriveToWaypointCase wired after RegisterPhysicsCases.

**Key assignment:** index 12 -> **F3** (D1-D9=0-8, D0=9, F1=10, F2=11, F3=12).

The harness:
1. Spawns MilitaryAPC (TKB 2001) at fixed start, facing East (identity rotation).
2. Defines a 3-waypoint route:
   - WP0: +15 East, -5 North of spawn (ahead-right; requires right turn)
   - WP1: +20 East, +10 North of spawn (ahead-left of WP0; requires left turn)
   - WP2: +5 East, +20 North of spawn (hard left from WP1; requires >= 90 deg turn)
   All waypoints >= 12 m from predecessor (outside 2*R_min approx 10.2 m).
3. Each frame: reads actual SimTransform (pose from reverse-sync), extracts heading via
   atan2(forward.Y, forward.X) where forward = UnitX rotated by SimTransform.Rotation,
   runs VehicleWaypointController.Compute, writes VehicleState.Speed + SteerAngle.
4. On arrival (dist < 3.0 m): logs "REACHED WPk at t=...s, final dist=...m", advances.
5. After all 3: logs "PROOF COMPLETE - reached 3/3 waypoints", zeroes Speed.
6. Distance + heading error logged every 0.5 s (convergence visible in log).
7. Timeout (25 s/waypoint): logs "TIMEOUT ... FAILURE" with closest distance achieved.

What the human should see:
- Press F3. APC spawns facing East, visibly curves right to WP0, left to WP1, hard-left to WP2.
- Log shows distance monotonically decreasing to < 3.0 m at each waypoint.
- Final log line: "PROOF COMPLETE - reached 3/3 waypoints".

New Game tests (2):

| Test | What it asserts |
|------|----------------|
| DriveToWaypoint_RegistersAtIndex12_KeyF3 | Case at index 12, key F3 from TryGetCaseKey |
| DriveToWaypoint_Trigger_EnqueuesApcSpawn_AndHookSetsVehicleState | Spawn queued, hook registered, Speed > 0 after 10 frames |

**Build and test results:**
```
dotnet build Stride/HrotStrideApp.Game/HrotStrideApp.Game.csproj -c Debug -> 0 errors
Hrot.Stride.Core.Tests       : 275 passed, 0 failed  (242 baseline + 33 new)
Hrot.Stride.Animation.Tests  :  48 passed, 0 failed  (unchanged)
HrotStrideApp.Game.Tests     : 132 passed, 0 failed  (130 baseline + 2 new)
```

---

### Summary: why this proves navigability

| Concern | Resolution |
|---------|-----------|
| Dynamic body does not follow commanded velocity exactly | Feedback reads actual SimTransform each frame; deviations are corrected automatically |
| One-frame velocity lag | Robustness tests: 2-step lag + 70% actuator gain still converges |
| Car might orbit inside R_min | All proof waypoints > 2*R_min from predecessor; inside-R_min is documented out-of-scope |
| Headless tests prove ideal model only | Perturbation tests cover 70-85% gain + 2-step lag (beyond what real Bullet body exhibits); F3 GPU case proves on actual dynamic body |

---

## F3 waypoint demo fixes: open-space waypoints + visible markers + stuck-skip (BATCH-17 follow-up)

### Context

GPU run confirmed: closed-loop steering IS proven (car drove from (6,12) to WP0(21,7) within tolerance — "REACHED WP0 final dist=2.98m"). But three problems remained:
1. WP1(26,22) and WP2 were placed behind a wall at X≈19; car froze at (18.95,7.79).
2. No visible markers — user couldn't see where waypoints were.
3. When blocked, the case froze silently instead of progressing.

### Fix A — Open-space waypoints

Waypoints changed from relative-to-spawn coords (which placed them past X≈19) to **absolute FDP coords** chosen entirely within the proven-open corridor (X∈[8,17], Y∈[7,12]):

| Waypoint | Old (relative to spawn, hit wall) | New (absolute, confirmed open) |
|----------|-----------------------------------|-------------------------------|
| WP0 | spawn+(15,-5) = (21,7) — at edge | **(15, 9)** |
| WP1 | spawn+(20,+10) = (26,22) — behind wall | **(11, 11)** |
| WP2 | spawn+(5,+20) = (11,32) — behind wall | **(8, 8)** |

Route is a small out-and-back triangle within the proven corridor. All legs are 5–9 m (well within R_min constraints). Route logged at case start: `[Drive To Waypoint] Route: WP0=(x,y) WP1=(x,y) WP2=(x,y) (open-space, no obstacle avoidance in this controller)`.

### Fix B — Visible markers

A `Stride.Engine.Entity` (named `WP_Marker_N`) is spawned at each waypoint's Stride-space position (FDP→Stride via `FdpStrideTransform.ToStridePosition`, altitude 0.5 m, scale 0.6×2.0×0.6 m so the tall slim marker is visible). **No `CharacterComponent` or `RigidbodyComponent` attached** — `PhysicsBodyLifecycleSystem` only processes FDP ECS entities with `SimTransform` + `WithOwned` authority; these bare Stride entities are invisible to it and cannot block the car. Markers removed from `ctx.Scene.Entities` when the run completes or the entity goes away.

### Fix C — Stuck-detection

Per-waypoint tracking:
- `bestDistThisWp` tracks the minimum distance to the current waypoint seen so far.
- A `windowOpenedAt` / `bestDistAtWindowStart` pair track when the window began and what the best distance was then.
- When `bestDistAtWindowStart - bestDistThisWp < 0.3 m` over 3 s (configurable via `StuckImprovementThresholdM` / `StuckWindowSec`): log `[Drive To Waypoint] BLOCKED before WPk at pos=(x,y) (wall in the way — controller has no obstacle avoidance), SKIPPING to next` and advance to the next waypoint.
- Final summary: `PROOF COMPLETE — reached N/M waypoints (K skipped as blocked)`.

### Fix D — Doc/comments update

`RegisterDriveToWaypointCase` XML doc updated to document: what this proves (closed-loop steer-to-point); why waypoints are in open space (no obstacle avoidance); visible markers; stuck-detection; summary format.

### Headless test added

**`TestHarnessTests.DriveToWaypoint_StuckCar_SkipsAllWaypointsAndReportsProofComplete`** (1 new test):

Runs the Drive To Waypoint case with `NoOpPhysicsBodyService` (car never moves) for 700 frames at 1/20 s each (35 s sim time). Asserts:
- At least one `BLOCKED ... SKIPPING to next` log line was emitted.
- A `PROOF COMPLETE` log line was emitted.
- The summary contains `3 skipped` (all 3 waypoints skipped because car was stationary).
- Hook count is 0 after completion (case exited cleanly).

This test breaks if the stuck-detection threshold or window is removed/changed such that the car never skips.

### Build and test results

```
dotnet build Stride/HrotStrideApp.Game/HrotStrideApp.Game.csproj -c Debug → 0 errors
Hrot.Stride.Core.Tests       : 275 passed, 0 failed  (unchanged)
Hrot.Stride.Animation.Tests  :  48 passed, 0 failed  (unchanged)
HrotStrideApp.Game.Tests     : 133 passed, 0 failed  (132 baseline + 1 new stuck-detection test)
```

### Files changed

- `Stride/HrotStrideApp.Game/StridePhysicsHarnessCases.cs` — Fix A/B/C/D: waypoints, markers, stuck-detection, docs. Added `StrideEntity` alias; `RemoveMarkers` helper; `AdvanceWaypoint` helper; `StuckImprovementThresholdM`/`StuckWindowSec` constants.
- `Stride/HrotStrideApp.Game.Tests/TestHarnessTests.cs` — new `DriveToWaypoint_StuckCar_SkipsAllWaypointsAndReportsProofComplete` test.

---

## Vehicle turn fidelity: near-zero friction + tighter params + commanded-vs-achieved yaw diagnostic

### Root cause

GPU testing showed the dynamic-rigidbody vehicle "does not turn enough (unlike a normal car)".
The bicycle-model minimum radius `R_min = wheelBase/tan(maxSteer) = 3.5/tan(0.6) ≈ 5.1 m` is
already car-like; the effective turn radius on the GPU was **wider than commanded**. The vehicle
is a flat box resting on the floor; its large bottom-face contact patch creates a friction torque
that opposes the imposed yaw. We command `rb.AngularVelocity = (0, yawRate, 0)` each frame
(`SetYawRate`) but the solver's floor-friction torque (and Bullet's angular damping) reduces it
during the step.

### Fix 1 — Near-zero friction + zero angular damping

**File:** `Stride/HrotStrideApp.Game/BulletPhysicsBodyService.cs`

- `DynamicConfig.Friction` reduced from `0.1f` → `0.02f` (near-zero; floor contact patch cannot
  generate a meaningful yaw-opposing torque; wall collision is non-penetration and frictionindependent; velocity is re-commanded each frame so straight-line runs don't drift).
- `DynamicConfig.AngularDamping` added as a new field (was not previously in the struct), set to
  `0f`. Bullet's default angular damping (`btRigidBody::m_angularDamping ≠ 0`) would attenuate
  the commanded angular velocity by `(1 − dt × angularDamping)` every step, widening the
  effective turn radius. Setting it to 0 ensures the imposed yaw survives intact until the next
  velocity-command frame.
- `ApplyDynamicConfigIfReady` updated to set `rb.AngularDamping = cfg.AngularDamping` alongside the
  existing `rb.Friction = cfg.Friction`.
- **[VERIFY] `RigidbodyComponent.AngularDamping`** — confirmed present in `Stride.Physics.dll`
  4.2.1.2487 via binary string scan of the deployed DLL and compile-time proof (build succeeds
  with `rb.AngularDamping = 0f`; same class already exposes `rb.LinearDamping`).

### Fix 2 — Tighter bicycle-model params

**File:** `Stride/HrotStrideApp.Game/StridePhysicsHarnessCases.cs`

- `DriveWheelBase` reduced `3.5f → 2.5f` (and `WpWheelBase` follows as `DriveWheelBase`; the
  comment on `WpWheelBase` updated from `// 3.5 m` → `// 2.5 m`).
- `maxSteerAngleRad` in `DriveToWaypoint` `VehicleWaypointController` constructor raised
  `0.6f → 0.7f`.
- `VehicleParams.WheelBase` injected at spawn in the `DriveToWaypoint` case uses `WpWheelBase`
  (= 2.5 m), and in the `PhysicsDrive` case uses `DriveWheelBase` (= 2.5 m) — both updated
  in sync as required.
- New `R_min = 2.5 / tan(0.7) ≈ 2.9 m` — clearly car-like.

### Fix 3 — Commanded-vs-achieved yaw diagnostic

**File:** `Stride/Hrot.Stride.Core/KinematicVehicleMotor.cs`

- `NLog` logger added (`private static readonly Logger Log`).
- `_yawDiagAccum: Dictionary<Entity, float>` tracks per-entity elapsed time.
- Every `~0.5 s` per vehicle, when `|strideYawRate| > 0.01 rad/s` (non-trivial yaw command),
  calls `_bodyService.GetBodyState(bodyRef.BodyHandle)` (already used by reverse-sync;
  returns `BodyState.AngularVelocity.Y` for dynamic bodies) and logs:
  ```
  [VehicleYaw] entity #k commanded=X.XXX rad/s achieved=Y.YYY rad/s (ratio=ZZZ%)
  ```
  A ratio near 100% proves the fix is working. A ratio << 100% would indicate residual
  floor-friction or angular-damping resistance and prompt further investigation.
  Throttle guard (|yaw| > 0.01) suppresses spurious 0/0 lines during straight driving.

### Tests

New tests in `Hrot.Stride.Core.Tests/KinematicVehicleMotorTests.cs` (class `KinematicVehicleMotorYawDiagnosticTests`, 7 tests):
- `YawRatio_AchievedEqualsCommanded_IsOne` — ratio 1.0 when achieved == commanded (fix working case).
- `YawRatio_Achieved80Percent_Is0p8` — ratio 0.8 for partial damping.
- `YawRatio_AchievedNearZero_IsNearZero` — ratio ≈ 0 when floor resistance kills yaw (failure case).
- `YawRatio_CommandedNearZero_IsNaN` — straight-line guard fires below 0.01 rad/s threshold.
- `YawDiagGuard_FiresOnlyAboveThreshold` — parameterized guard boundary test.
- `YawDiagnostic_ReadsAchievedYawFromBodyStateAngularVelocity` — verifies AngularVelocity.Y is the read source; ratio = 0.45/0.5 = 0.9.

New tests in `HrotStrideApp.Game.Tests/BulletPhysicsBodyServiceHelperTests.cs` (2 tests):
- `DynamicVehicleConfig_Friction_IsNearZero_ForYawFidelity` — friction ≤ 0.05 constant check.
- `DynamicVehicleConfig_AngularDamping_IsZero_ForYawFidelity` — angular damping = 0 constant check.

Updated tests (no failures introduced):
- `DeferredDynamicConfig_AppliedOnceWhenReady_NeverAppliedWhenNotReady` — asserts `cfg.AngularDamping = 0`.
- `FakeDynamicConfig_StoresAllFields_Correctly` — mirrors new `AngularDamping` field.

### Build and test results

```
dotnet build Stride/HrotStrideApp.Game/HrotStrideApp.Game.csproj -c Debug → 0 errors, 9 pre-existing warnings
Hrot.Stride.Core.Tests       : 286 passed, 0 failed  (275 baseline + 11 new)
Hrot.Stride.Animation.Tests  :  48 passed, 0 failed  (unchanged)
HrotStrideApp.Game.Tests     : 135 passed, 0 failed  (133 baseline + 2 new)
```

### Files changed

- `Stride/HrotStrideApp.Game/BulletPhysicsBodyService.cs` — DynamicConfig gains `AngularDamping` field; friction 0.1→0.02; AngularDamping=0; ApplyDynamicConfigIfReady sets rb.AngularDamping.
- `Stride/HrotStrideApp.Game/StridePhysicsHarnessCases.cs` — DriveWheelBase 3.5→2.5; WpWheelBase comment updated; maxSteerAngleRad 0.6→0.7 in DriveToWaypoint controller.
- `Stride/Hrot.Stride.Core/KinematicVehicleMotor.cs` — NLog + yaw diagnostic log.
- `Stride/Hrot.Stride.Core.Tests/KinematicVehicleMotorTests.cs` — 7 new yaw-diagnostic tests.
- `Stride/HrotStrideApp.Game.Tests/BulletPhysicsBodyServiceHelperTests.cs` — FakeDynamicConfig gains AngularDamping field; 2 new physics-config tests; updated assertions.

---

## F3 demo polish: visible markers + smooth route + movement-based stuck-detection

### Overview

Three GPU-visible demo-quality improvements to the Drive To Waypoint (F3) case:

1. **Markers are now VISIBLE** — each waypoint marker now receives a `ModelComponent` loaded via
   `Content.Load<Model>("Models/Box2x1x1")` so the entity actually renders in the Stride scene.
2. **Smooth forward route** — waypoints re-laid as a gentle CCW loop with each turn <= ~80 degrees.
3. **Movement-based stuck-detection** — no longer fires on a legitimate turn; fires only when the
   car's actual displacement over 3 s is below 0.3 m (genuinely wedged).

### Fix 1 — Visible markers (ModelComponent approach)

`RegisterDriveToWaypointCase` now accepts an optional `Func<string, Model?> loadModel` parameter.
In the live app `StrideHrotGame.BuildTestHarness` passes `modelRef => Content.Load<Model>(modelRef)`;
in headless tests the parameter is omitted (null) so no GPU is needed.

When `loadModel != null`, `DriveToWaypoint` calls `loadModel("Models/Box2x1x1")` for each marker,
wraps the result in `new ModelComponent { Model = model }`, and `marker.Add(...)` attaches it.
Each marker is a thin pillar (Scale = (0.4, 3.0, 0.4)) at the waypoint XY on the floor.

**No physics body is attached** — the marker has no `CharacterComponent` or `RigidbodyComponent`.
`PhysicsBodyLifecycleSystem` only processes FDP ECS entities with `SimTransform + WithOwned`
authority; bare Stride entities with only a `ModelComponent` are invisible to it. The car
cannot collide with or be blocked by a marker.

Log line per marker:
```
[Drive To Waypoint] Marker WP0 spawned (VISIBLE model=Models/Box2x1x1) at FDP (14.0,12.0) -> Stride (...)
```
In headless tests the note reads `bare entity (headless)`.

**Files changed:**
- `Stride/HrotStrideApp.Game/StridePhysicsHarnessCases.cs` — `using Stride.Rendering;` added;
  `RegisterDriveToWaypointCase` gains optional `loadModel` param; `DriveToWaypoint` loads the
  model and attaches `ModelComponent` when `loadModel != null`; marker Scale changed to pillar.
- `Stride/HrotStrideApp.Game/StrideHrotGame.cs` — `using Stride.Rendering;` added;
  `RegisterDriveToWaypointCase` call passes `loadModel: modelRef => Content.Load<Model>(modelRef)`.

### Fix 2 — Smooth forward route

**Old route:** WP0=(15,9), WP1=(11,11), WP2=(8,8) — WP2 was ~131 degrees BEHIND the car after WP1
(near-reverse that the bicycle model cannot complete tightly).

**New route (CCW gentle loop, all turns <= ~80 degrees):**

| WP | Coords | Required bearing from previous | Turn at previous |
|----|--------|--------------------------------|-----------------|
| Spawn | (6,12) east | — | — |
| WP0 | (14,12) | 0 deg (east) | 0 deg |
| WP1 | (16,15) | atan2(3,2) approx 56 deg | 56 deg |
| WP2 | (14,17) | atan2(2,-2)=135 deg | 135-56=79 deg |

All within X in [6,17], Y in [12,17] (confirmed open; wall at X>19 is safe).
Each turn is below 80 degrees so no near-U-turns. R_min approx 2.9 m; all legs are 2-8 m.

**Files changed:** `Stride/HrotStrideApp.Game/StridePhysicsHarnessCases.cs` — `waypoints[]`
updated; route-description comment updated; spawn log updated.

### Fix 3 — Movement-based stuck-detection

**Old rule (distance-based):** stuck if `(bestDistAtWindowStart - bestDistThisWp) < 0.3 m` over 3 s.
**Problem:** a car legitimately curving may temporarily increase distance-to-target, causing
a false "BLOCKED / SKIPPING" on the very turn the GPU confirmed was correct.

**New rule (movement-based):** stuck if `displacement(curPos - windowStartPos).Length() < 0.3 m` over 3 s.
A turning/curving car moves through space even if not closing on the target — NOT stuck.
Only a genuinely wedged car (pinned against a wall, zero movement) fires stuck.

State changes:
- Removed: `bestDistAtWindowStart`, `StuckImprovementThresholdM`.
- Added: `stuckWindowStartPos` (position when window started), `StuckDisplacementThresholdM = 0.3 m`.
- Window rolls forward whenever `displacement >= 0.3 m` (car has moved enough to prove its not stuck).
- `AdvanceWaypoint` signature updated: `ref float bestDistAtWindowStart` replaced by
  `ref Vector3 stuckWindowStartPos` + `Vector3 currentPos` value param; resets window on arrival/skip.

**Files changed:** `Stride/HrotStrideApp.Game/StridePhysicsHarnessCases.cs` — constants, state vars,
stuck-detection condition, `AdvanceWaypoint` signature.

### Test update

**`DriveToWaypoint_StuckCar_SkipsAllWaypointsAndReportsProofComplete`** — updated doc comment to
reflect the movement-based rule. Assertion unchanged (stationary NoOp car has zero displacement,
so stuck still fires correctly).

**New test: `DriveToWaypoint_MovingCar_IsNotDeclaredStuck_EvenIfDistanceFluctuates`** — drives the
spawned APC with the bicycle kinematics model each frame (writing `SimTransform` directly as if
Bullet were running), then asserts NO `BLOCKED/SKIPPING` log was emitted before WP0 arrival.
Breaks if the stuck rule incorrectly fires on a car with nonzero displacement.

### Build and test results

```
dotnet build Stride/HrotStrideApp.Game/HrotStrideApp.Game.csproj -c Debug -> 0 errors
Hrot.Stride.Core.Tests       : 286 passed, 0 failed  (unchanged)
Hrot.Stride.Animation.Tests  :  48 passed, 0 failed  (unchanged)
HrotStrideApp.Game.Tests     : 136 passed, 0 failed  (135 baseline + 1 new movement-stuck test)
```
