# BATCH-06 Report

## Implementation Summary

### STR-P1-T5: BulletReverseSyncSystem (+ IPhysicsBodyService.GetBodyState)

**New files:**
- `Stride/Hrot.Stride.Core/BulletReverseSyncSystem.cs` — post-physics system (PostSimulation phase) that reads `IPhysicsBodyService.GetBodyState` for every `.WithOwned<SimTransform>()` entity that has a `PhysicsBodyReference`, then writes `SimTransform.Position/.Rotation` (via `FdpStrideTransform.ToFdpPosition/ToFdpRotation`) and `SimVelocity.Linear/.Angular` into the ECS.
- `Stride/Hrot.Stride.Core/NoOpPhysicsBodyService.cs` — no-op `IPhysicsBodyService` for use in `editor_stride` until the concrete `BulletPhysicsBodyService` lands (STR-D11). Returns identity pose + zero velocity from `GetBodyState`.

**Modified files:**
- `Stride/Hrot.Stride.Core/IPhysicsBodyService.cs` — extended with two additions:
  - `BodyState GetBodyState(object bodyHandle)` method (new interface member, STR-P1-T5)
  - `BodyState` record struct (new type, defined in the same file alongside `KinematicMoveResult`)
- All three existing test fakes (`PhysicsBodyLifecycleSystemTests`, `BulletCharacterMotorTests`, `KinematicVehicleMotorTests`) updated to stub `GetBodyState`.

**Tests:** `Stride/Hrot.Stride.Core.Tests/BulletReverseSyncSystemTests.cs` — 10 new tests.

**STR-D5 resolved:** `BulletReverseSyncSystem` is wrapped in a `TogglablePostSimulationGroup` (tests SC5/SC6 prove this).

---

### STR-P1-T6: SplitAuthorityStrideSyncScript

**New files:**
- `Stride/Hrot.Stride.Core/SplitAuthorityStrideSyncScript.cs` — authority-forked sync that calls `StrideVisualBindingSystem.SyncExistenceOnly(world)` for Pass A (appear/disappear), then queries `.WithoutOwned<SimTransform>()` for Pass B and calls `_factory.UpdatePose` for each non-owned entity with a live visual.

**Modified files:**
- `Stride/Hrot.Stride.Core/StrideVisualBindingSystem.cs` — added `SyncExistenceOnly(EntityRepository)` method that performs the two-pass differ (destroy stale + create new visuals) without calling `UpdatePose` for already-existing entities. This separates existence management from transform direction so `SplitAuthorityStrideSyncScript` can own the authority-forked update step.

**Tests:** `Stride/Hrot.Stride.Core.Tests/SplitAuthorityStrideSyncScriptTests.cs` — 7 new tests.

**P0 forward-sync seam:** `EditorStrideSubsystem.Tick` now uses `SplitSync?.Sync(World)` instead of `VisualBindingSystem?.Sync(World)`.

---

### STR-P1-T7: Fixed timestep + reverse-sync ordering

**Modified files:**
- `Stride/HrotStrideApp.Game/EditorStrideSubsystem.cs` — major update:
  - Added fields: `PhysicsBodyService`, `PhysicsBodyLifecycle`, `ReverseSyncGroup`, `SplitSync`, `_characterMotor`, `_vehicleMotor`.
  - `Initialize` steps 10–13: wires `NoOpPhysicsBodyService` → `PhysicsBodyLifecycleSystem` → motors (`BulletCharacterMotor`, `KinematicVehicleMotor`) → `BulletReverseSyncSystem` in `TogglablePostSimulationGroup` → `SplitAuthorityStrideSyncScript`.
  - `Tick` reordered to: (1) orch pump, (2) motors, (3) `ReverseSyncGroup.Execute` **before** `Kernel.Update()`, (4) `TimeController.Step` + `Kernel.Update()`, (5) `SplitSync.Sync`.
  - `Dispose` calls `PhysicsBodyLifecycle?.DestroyAll()`.

**Tests:** `Stride/HrotStrideApp.Game.Tests/ReverseSyncOrderingTests.cs` — 9 new tests.

---

## Design Decisions

### TogglablePostSimulationGroup API and wrapping

`TogglablePostSimulationGroup(string name, params IEcsModuleSystem[] innerSystems)` — takes a name and variadic inner systems. `Enabled { get; set; }` (default `true`). `Execute(ISimulationView view, float deltaTime)` is a no-op when `Enabled = false`.

The `BulletReverseSyncSystem` is wrapped as:
```csharp
new TogglablePostSimulationGroup("BulletReverseSync", reverseSync)
```
The group is **NOT registered with the kernel** — it is driven manually in `EditorStrideSubsystem.Tick` **before** `Kernel.Update()`. This is the key ordering decision: registering it as a kernel PostSimulation system would fire it *inside* `Kernel.Update()`, after Simulation-phase consumers have already run, producing a one-frame lag. Manual pre-kernel invocation is the clean solution.

### IPhysicsBodyService.GetBodyState shape

```csharp
readonly record struct BodyState(
    SMath.Vector3    Position,
    SMath.Quaternion Rotation,
    SMath.Vector3    LinearVelocity,
    SMath.Vector3    AngularVelocity,
    bool             IsKinematic);
```

All values are in **Stride world space** (Y-up, left-handed). `BulletReverseSyncSystem` converts them to FDP space via `FdpStrideTransform.ToFdp*`.

**Dynamic-vs-kinematic velocity sourcing:**
- `IsKinematic = false` (dynamic `RigidbodyComponent`): the concrete `BulletPhysicsBodyService` reads `RigidbodyComponent.LinearVelocity / .AngularVelocity`. A collision-arrested dynamic body reports zero velocity directly from the Bullet solver — no extra zeroing needed.
- `IsKinematic = true` (kinematic character/vehicle): the solver does not produce velocity. `BulletReverseSyncSystem` reads `PhysicsBodyReference.PostCollisionLinearVelocityFdp` / `.PostCollisionAngularVelocityFdp` (already in FDP space; no conversion). These were written by `BulletCharacterMotor` / `KinematicVehicleMotor` after each frame's kinematic move, with an exact-zero guarantee on full block.

### Zero-on-arrest invariant

**Test `CollisionArrest_ZeroVelocityFromSolver_SimVelocityWrittenExactlyZero_NoStale`** (T5-SC3):
1. Frame 1: fake returns `LinearVelocity = (5, 0, 0)` → `SimVelocity.Linear` is non-zero (asserted).
2. Frame 2: fake returns `LinearVelocity = (0, 0, 0)` → `SimVelocity.Linear` asserted as exactly `(0, 0, 0)` — not the stale frame-1 value.

This works because `BulletReverseSyncSystem` unconditionally overwrites `SimVelocity` every frame via `repo.SetComponent(entity, new SimVelocity { Linear = ..., Angular = ... })`. There is no "only write if changed" guard that would leave a stale value.

For kinematic bodies: **test `KinematicBody_FullyBlocked_PostCollisionChannelZero_SimVelocityExactlyZero`** (T5-SC4b) sets both `PostCollision*` fields to `Vector3.Zero` and asserts the written `SimVelocity` is exactly zero.

### Same-frame ordering test

**Test `ReverseSync_ManualBeforeKernelUpdate_ProbeSeesReverseSyncedPosition_SameFrame`** (T7-SC1 core):

Setup: a standalone `EntityRepository` with one owned entity, a scripted `IPhysicsBodyService` returning Stride position `(10, 0, 20)`, a `BulletReverseSyncSystem` in a `TogglablePostSimulationGroup`, and a `ProbeCaptureSystem` registered in the kernel's Simulation phase.

Sequence:
```
reverseSyncGroup.Execute(world, dt);   // writes SimTransform.Position = ToFdpPosition(10,0,20) = (10,20,0)
timeController.Step(dt);
kernel.Update();                        // Simulation phase: ProbeCaptureSystem reads SimTransform
```

Assertion: `probe.CapturedPositions[0].X == 10`, `.Y == 20`, `.Z == 0` — the reverse-synced value is read the same frame.

**Negative test `ReverseSync_AfterKernelUpdate_ProbeSeesStalePosition_OneFRAMELag`** demonstrates that reversing the order (kernel first, then reverse-sync) leaves the probe with the stale pre-reverse-sync value.

### IPhysicsBodyService wired into editor_stride

`NoOpPhysicsBodyService` — a no-op implementation in `Hrot.Stride.Core`. All lifecycle calls are accepted without creating Bullet bodies. `GetBodyState` returns identity pose + zero velocity. Rationale: the concrete `BulletPhysicsBodyService` requires a live `Stride.Physics.Simulation` (owned by `PhysicsProcessor`, internal to `Stride.Physics` — STR-D11). The no-op enables the complete wiring and ordering invariant to be proven headlessly. Motors execute harmlessly (no-op physics service accepts their calls). Documented in `NoOpPhysicsBodyService.cs` XML docs and in this report.

### GameSettings fixed-timestep location

`Stride/Assets/GameSettings.sdgamesettings` → `Stride.Physics.PhysicsSettings` block:
```yaml
- !Stride.Physics.PhysicsSettings,Stride.Physics
    Flags: None
    FixedTimeStep: 0.016666668    # 1/60 s
    MaxTickDuration: 0.008333334  # 1/120 s sub-step cap
    Gravity: {X: 0.0, Y: -10.0, Z: 0.0}
```
Stride's `PhysicsProcessor` reads this at startup via `GameSettings.GetConfiguration<PhysicsSettings>()`. The `StrideHostLoopDriver` (BATCH-02) independently implements the same fixed-dt accumulator logic for driving the FDP sim clock from the external host loop — confirmed working in `FixedClock_SimAdvancesOnFixedStep_IndependentOfRenderRate` (T7-SC2).

---

## Deviations

### TogglablePostSimulationGroup NOT registered with kernel

**Spec says:** "Wrapped in `TogglablePostSimulationGroup`; Enabled=false ⇒ no writes (replay severability)."

**What was done:** The group is driven manually in `Tick()` before `Kernel.Update()`, not registered as a kernel PostSimulation system.

**Why:** Registering as a kernel system fires it inside `Update()`, after Simulation-phase consumers, producing a one-frame lag — directly contradicting the same-frame invariant in §8.3 and the batch spec ("reverse-sync must run before `Kernel.Update()`"). The manual pre-kernel invocation achieves both the ordering invariant AND replay severability (the group's `Enabled` flag still works).

**Benefit:** Clean implementation of the ordering invariant with no workaround required.

**Risk:** P5's `ReferenceReplayLoadHandler` integration must call `ReverseSyncGroup.Enabled = false` (not toggle a kernel-registered group). This is exposed as a public property — no additional API needed.

### SyncExistenceOnly added to StrideVisualBindingSystem

**Why:** `StrideVisualBindingSystem.Sync` calls `UpdatePose` for all entities (both owned and non-owned). `SplitAuthorityStrideSyncScript` needs Pass A (exist/disappear) without the pose update step, so it can do the authority-forked Pass B itself. Adding `SyncExistenceOnly` is the clean separation.

**Benefit:** `StrideVisualBindingSystem.Sync` remains available for the P0 path; `SyncExistenceOnly` enables the split-authority pattern without modifying existing tests.

### Motors stored as private fields, called manually in Tick

**Why:** Motors need `Simulation`-phase execution before the physics step. `Kernel.Initialize()` was already called before step 10–11 (motor wiring), so they cannot be registered via `kernel.RegisterModule`. Rather than restructure the entire `Initialize` method (risky, touches P0/P1 invariants), motors are stored as `_characterMotor` / `_vehicleMotor` and called manually in `Tick()` at the correct pre-physics slot.

**Risk / follow-up (STR-D11):** At GPU bring-up, motors should be moved to pre-`Kernel.Initialize()` registration when the concrete physics service is available. Documented with a TODO comment in `Initialize`.

---

## Test Results

```
Stride/Hrot.Stride.Core.Tests:
  Passed!  - Failed: 0, Passed: 126, Skipped: 0, Total: 126
  (Prior: 109; New in BATCH-06: 17 — 10×T5 + 7×T6)

Stride/Hrot.Stride.Animation.Tests:
  Passed!  - Failed: 0, Passed: 4, Skipped: 0, Total: 4
  (Unchanged from prior batches)

Stride/HrotStrideApp.Game.Tests:
  Passed!  - Failed: 0, Passed: 33, Skipped: 0, Total: 33
  (Prior: 24; New in BATCH-06: 9×T7)

TOTAL: 163 passed, 0 failed
```

Build: `Build succeeded. 24 Warning(s) 0 Error(s)` — all 24 warnings are pre-existing NU1608 NuGet version constraint warnings; no new warnings introduced.

---

## Developer Insights

### Key ordering discovery
The "reverse-sync before `Kernel.Update()`" requirement cannot be satisfied by registering a PostSimulation system with the kernel — PostSimulation runs *inside* `Update()`, after Simulation-phase consumers have already run. The only correct approach is manual pre-kernel invocation. The negative test (`ReverseSync_AfterKernelUpdate_ProbeSeesStalePosition_OneFRAMELag`) explicitly documents this failure mode.

### NoOpPhysicsBodyService writes identity pose
With the no-op service wired, `BulletReverseSyncSystem` will write identity position `(0, 0, 0)` and identity rotation to every owned entity every frame (because `GetBodyState` returns `Position = Vector3.Zero`). In Mode 1 tests with real entities, this means owned entities appear to snap to the origin each frame. This is expected and acceptable until `BulletPhysicsBodyService` is wired. In integration tests, the scripted fake overrides this behavior.

### StrideVisualBindingSystem.Sync still calls UpdatePose for all entities
The P0-era `StrideVisualBindingSystem.Sync` still updates poses for all entities (both owned and non-owned). In the new `Tick` flow, `SplitSync.Sync(World)` calls `SyncExistenceOnly` (no pose updates) + Pass B (non-owned only). The P0 path (`VisualBindingSystem?.Sync`) is no longer called in `Tick` when a factory is provided — effectively dead code, but retained for any code paths that may call `VisualBindingSystem.Sync` directly.

### STR-D12 still open
`CrowdAgentUpdateSystem` still integrates `SimTransform.Position` (velocity×dt). This was noted as deferred to P2-T4. With the reverse-sync now active (even no-op), owned crowd entities would have their positions written twice: once by `CrowdAgentUpdateSystem` and once by `BulletReverseSyncSystem` (to zero/identity with the no-op). The no-op service's zero-write "wins" each frame. This is harmless for headless tests but would cause issues with real Bullet — still deferred per STR-D12.

---

## Known Issues

1. **STR-D11 (open):** `BulletPhysicsBodyService` — concrete Bullet implementation — remains deferred to GPU bring-up. All Phase-1 behaviors still only seam-tested:
   - `PhysicsBodyLifecycleSystem.Execute` — tested with `RecordingFakePhysicsBodyService`; never run against a live `PhysicsProcessor` / Bullet `Simulation`.
   - `BulletCharacterMotor.Execute` — tested with scripted fake; `SetCharacterVelocity`/`IsGrounded`/`Jump` never called on a real `CharacterComponent`.
   - `KinematicVehicleMotor.Execute` — tested with scripted fake; `MoveKinematic` never called against a real Bullet kinematic body.
   - `BulletReverseSyncSystem.Execute` — tested with scripted fake; `GetBodyState` never called on a real `RigidbodyComponent`/`CharacterComponent`.
   - `StrideVisualBindingSystem.SyncExistenceOnly` and `StrideVisualBindingSystem.Sync` — tested with `NullVisualFactory`; `Content.Load<Model>` and `ModelComponent` creation never tested against a real `GraphicsDevice`.
   - The complete reverse-sync + motors + visual binding pipeline — exercised through `EditorStrideSubsystem` tests but with no-op/fake services throughout.

2. **STR-D12 (open):** `CrowdAgentUpdateSystem` still integrates `SimTransform.Position` under P1 — deferred to P2-T4.

3. **STR-D9/STR-D10 (open):** Procedural visuals are mesh-less; `StrideVisualFactory.CreateModelVisual` swallows load failures silently.

4. **Motors not registered with kernel (deviation):** `BulletCharacterMotor` and `KinematicVehicleMotor` are called manually in `Tick`. Re-register via `kernel.RegisterModule` before `Kernel.Initialize()` when the concrete service lands (STR-D11).

---

## Suggested Commit Message

```
feat(stride): BulletReverseSyncSystem + SplitAuthorityStrideSyncScript + reverse-sync ordering (BATCH-06)

Completes STR-P1-T5, STR-P1-T6, STR-P1-T7. Resolves STR-D5.
- IPhysicsBodyService.GetBodyState (BodyState record: pose+linVel+angVel+IsKinematic)
- BulletReverseSyncSystem: writes owned pose+velocity (FdpStrideTransform swizzle);
  dynamic velocity from GetBodyState; kinematic from PostCollision* channel;
  zero-on-arrest invariant proven; TogglablePostSimulationGroup wrapping (STR-D5).
- NoOpPhysicsBodyService: no-op for editor_stride until GPU bring-up (STR-D11)
- SplitAuthorityStrideSyncScript: Pass A reconciliation via StrideVisualBindingSystem.
  SyncExistenceOnly; Pass B forward-syncs .WithoutOwned<SimTransform>() only
- EditorStrideSubsystem: motors+lifecycle+reverse-sync wired; Tick reordered:
  motors → ReverseSyncGroup.Execute BEFORE Kernel.Update() → SplitSync.Sync
- GameSettings fixed-timestep: Stride/Assets/GameSettings.sdgamesettings
  PhysicsSettings.FixedTimeStep=0.016666668 (1/60s)
Tests: 163 total (126 Core incl. 17 new T5/T6, 4 Animation, 33 Game incl. 9 new T7).
Concrete BulletPhysicsBodyService still deferred (STR-D11).
```
