# BATCH-S2-L — Hosted-editor time pause (bug B): freeze sim when not in Continuous mode

## Goal
Make the hosted Stride editor honor pause/run like the standalone editor:
- **Edit mode (TimeController Deterministic / paused):** sim time frozen AND entities do not move.
- **Preview/running (TimeController Continuous):** sim time advances and entities move.

Drag + reposition must STILL work while paused (the physics bracket lifecycle/reposition/reverse-sync
keep running every frame; only the sim-advancing motors are gated).

## Background (why a one-line gate is not enough)
1. The hosted `PreKernelUpdateHook` calls `_editor.TimeController.Step(dt)` UNCONDITIONALLY every
   frame → FDP sim-time always advances (can't pause). Standalone never manually Steps; the
   controller self-advances in Continuous (wall-clock) and freezes in Deterministic.
2. The Stride vehicle/character are driven by the **physics-bracket motors**, which run every frame
   on the frame `dt` and ignore FDP sim-time. So even with sim-time frozen they keep driving the
   body (and Stride's Bullet steps on its own loop). We must gate the motors on run-state too.
3. Stride exposes no API to pause its Bullet step. Accepted semantics (user-approved):
   **"pause" = command zero velocity to all bodies each paused frame.** Grounded bodies freeze
   cleanly; an airborne body would still settle under gravity (accepted edge case).

Run-state signal: `MasterSyncController.GetMode()` (FDP/Toolkits/.../Time/Controllers/MasterSyncController.cs:156)
returns `TimeMode.Continuous` when running and `TimeMode.Deterministic` when paused (Stepping).
`simRunning = (TimeController.GetMode() == TimeMode.Continuous)`.

## Scope — SIX FILES

### File 1: `Stride/HrotStrideApp.Game/EditorStrideSubsystem.cs`
**1a.** In the hosted `PreKernelUpdateHook` (lines 1018-1033): REMOVE the unconditional time step.
Delete the line `_editor.TimeController.Step(dt);` (line 1022). Keep the `_stepSw` timing wrapper
but it now wraps nothing (or remove the A-block stopwatch lines if cleaner — but do NOT remove the
other stopwatches). Then compute run-state and pass it to the bracket:
```csharp
_editor.PreKernelUpdateHook = dt =>
{
    // A: (BATCH-S2-L) time is no longer force-stepped here. The TimeController self-advances
    // via Kernel.Update()->controller.Update() — Continuous (preview) runs, Deterministic
    // (edit) stays frozen — exactly like the standalone editor.
    bool simRunning = _editor.TimeController.GetMode() == TimeMode.Continuous;

    // B: physics bracket pre-kernel (lifecycle + reposition + reverse-sync ALWAYS run;
    // the sim-advancing motors run only when simRunning).
    _bracketPreSw.Restart();
    _physicsBracket.RunPreKernelStep(World, dt, simRunning);
    _bracketPreSw.Stop();

    _kernelSw.Restart();
};
```
Add a `using` for the `TimeMode` enum's namespace (find it — likely `Fdp.ModuleHost.Time`; the
file already references the controller, so resolve the correct namespace).

**1b.** The OFF-path `Tick` call site (line ~1152: `_physicsBracket.RunPreKernelStep(World, dt);`):
this path keeps its own behavior. Pass `simRunning: true` so the OFF/mock path is UNCHANGED:
`_physicsBracket.RunPreKernelStep(World, dt, simRunning: true);`
Do NOT remove the OFF-path's `TimeController.Step(dt)` (line ~1156) — out of scope.

### File 2: `Stride/Hrot.Stride.Core/StridePhysicsBracket.cs`
Change `RunPreKernelStep(EntityRepository world, float dt)` to
`RunPreKernelStep(EntityRepository world, float dt, bool simRunning = true)` (defaulted so existing
callers/tests still compile). Inside (the pre-kernel order, ~lines 158-184):
- Step 2 `PhysicsBodyLifecycle?.Execute` — ALWAYS (unchanged; drag/reposition must work while paused).
- Step 2b VehicleNavIntent — gate: `if (simRunning) VehicleNavIntentSystem?.Execute(world, dt);`
  (when paused, don't advance navigation/steering/corners or the stuck guard).
- `CharacterMotor?.Execute(world, dt, simRunning);` — pass simRunning (see File 4).
- `VehicleMotor?.Execute(world, dt, simRunning);` — pass simRunning (see File 3).
- Step 3 `ReverseSyncGroup.Execute(world, dt);` — ALWAYS (unchanged; bodies' resolved pose still
  flows to SimTransform, and this records the reposition baseline).
Keep all the existing per-substep Stopwatch timing exactly as-is.

### File 3: `Stride/Hrot.Stride.Core/KinematicVehicleMotor.cs`
Change `Execute(ISimulationView view, float deltaTime)` to
`Execute(ISimulationView view, float deltaTime, bool simRunning = true)`.

Inside the per-entity loop, AFTER the existing character-body guards (the `Capsule` skip and the
`CrowdMotorIntent` skip — keep those) but BEFORE reading `VehicleState`/computing velocity, add the
paused branch:
```csharp
// BATCH-S2-L: when the sim is paused (edit mode, not Continuous), do NOT drive the vehicle.
// Command zero velocity + zero yaw so a mid-drive body stops and stays put. We still CALL the
// body service (not skip it) so the deferred dynamic-config / initial-pose-slam path keeps
// running while paused (it is driven by SetLinearVelocityXZ -> ApplyDynamicConfigIfReady).
if (!simRunning)
{
    _bodyService.SetLinearVelocityXZ(bodyRef.BodyHandle, SMath.Vector3.Zero);
    _bodyService.SetYawRate(bodyRef.BodyHandle, 0f);
    continue;
}
```
(`SMath` is the existing `Stride.Core.Mathematics` alias in this file.) Leave the existing
`if (deltaTime <= 0f) return;` guard and everything else unchanged.

### File 4: `Stride/Hrot.Stride.Core/BulletCharacterMotor.cs`
Change `Execute(ISimulationView view, float deltaTime)` to
`Execute(ISimulationView view, float deltaTime, bool simRunning = true)`.
In the per-entity loop, before driving the character from its intent, add the paused branch that
commands zero character velocity and `continue`s:
```csharp
// BATCH-S2-L: paused (edit mode) — freeze the character, don't advance it.
if (!simRunning)
{
    _bodyService.SetCharacterVelocity(bodyRef.BodyHandle, /* zero vector in the type this method takes */);
    continue;
}
```
Use the correct zero-vector type for `SetCharacterVelocity` (check its signature — Stride
`Vector3.Zero` or `System.Numerics.Vector3.Zero`). Match the field name the motor uses for the body
service and the body-ref handle (mirror how the vehicle motor references `_bodyService` and
`bodyRef.BodyHandle`). Keep all existing guards/logic otherwise.

### File 5: `Stride/HrotStrideApp.Game/StrideHrotGame.cs`
At the self-test registration (line ~777):
`StrideSelfTest.RegisterIfEnabled(harnessCtx, emap, this);`
pass the TimeController so the harness can start/stop time:
`StrideSelfTest.RegisterIfEnabled(harnessCtx, emap, this, _editorSubsystem.TimeController);`
(`EditorStrideSubsystem` exposes `public MasterSyncController TimeController { get; }`. Confirm the
exact property name and use it.)

### File 6: `Stride/HrotStrideApp.Game/StrideSelfTest.cs`
Add a paused-freeze check, then resume, then the existing drive check.

**6a.** Add a `MasterSyncController` dependency:
- Add `using` for its namespace (`Fdp.Toolkit.Time.Controllers` — verify).
- Add field `private readonly MasterSyncController _timeController;`.
- Add param to the constructor AND to `RegisterIfEnabled` (last param), wire it through, null-check it.

**6b.** New constants (near the other timing/param constants):
```csharp
private const int   PausedSettleFrames    = 120;  // frames to confirm no motion while paused
private const float PausedFreezeTolerance = 1.0f; // metres: movement <= this while paused => frozen
```
New verdict fields:
```csharp
private bool  _pausedFreeze;
private float _pausedDistMoved;
```

**6c.** Insert phases between `CheckB` and `DrivingSettle`. New `Phase` enum members in order:
`... CheckB, DriveIssue, PausedSettle, CheckPaused, Resume, DrivingSettle, CheckDrive, Done`.
Wire them in the `Tick` switch.

Flow:
- `TickDriveIssue` (KEEP as-is — it sets the DirectPoint NavigationIntent IntentId=1). Because the
  sim is PAUSED here, the bracket skips VehicleNavIntent, so the vehicle must NOT move yet.
  Change its terminal transition from `Phase.DrivingSettle` to `Phase.PausedSettle`.
- `TickPausedSettle`: wait `PausedSettleFrames`, logging `[SELFTEST] paused frame=.. pos=(..)` every
  `TrackLogInterval`. Then transition to `CheckPaused`.
- `TickCheckPaused`: read position; `_pausedDistMoved = distance(pos, PosB)` (FDP X,Y);
  `_pausedFreeze = _pausedDistMoved <= PausedFreezeTolerance`; log
  `[SELFTEST] CHECK_PAUSED end=(..) distMovedWhilePaused=.. -> PASS/FAIL`. Transition to `Resume`.
- `TickResume`: call `_timeController.SwitchToContinuous();` log
  `[SELFTEST] RESUME → SwitchToContinuous (sim time now running)`. Transition to `DrivingSettle`.
  (Use SwitchToContinuous directly rather than PreviewController.EnterPreviewMode — we are testing
  the time-gating mechanism, and EnterPreviewMode would trigger preview scene (re)loading that could
  disturb the manually-spawned harness entity.)
- `TickDrivingSettle` / `TickCheckDrive`: UNCHANGED (the vehicle now moves because Continuous → sim
  runs → bracket drives it).

**6d.** Add `pausedFreeze` to the RESULT line in `WriteSummaryAndExit`:
`... repos={..} pausedFreeze={(_pausedFreeze ? "PASS" : "FAIL")} drive={..} ... pausedDistMoved={_pausedDistMoved:F2} ...`

## Constraints
- Defaulted `simRunning = true` params everywhere so existing callers/tests compile unchanged.
- Do NOT touch position-authority logic, the reposition baseline (S2-K), or preview/edit-mode
  beyond what's listed. Do NOT remove the OFF-path force-step.
- Lifecycle + reposition + reverse-sync MUST keep running every frame regardless of simRunning.

## Acceptance (lead verifies via harness)
- Builds clean (`Stride/HrotStrideApp.sln`).
- `STRIDE_SELFTEST=1` RESULT shows `initialHold=PASS repos=PASS pausedFreeze=PASS drive=PASS`:
  - `initialHold`/`repos` still PASS even though those phases now run while paused (spawn, settle,
    drag/reposition work with sim frozen).
  - `pausedFreeze=PASS`: after the intent is issued while paused, `distMovedWhilePaused` ≈ 0.
  - `drive=PASS`: after `SwitchToContinuous`, the vehicle moves toward D (distMoved >> 0).
