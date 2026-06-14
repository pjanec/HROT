# BATCH-S2-STEP — Advance Stride physics on a deterministic step

## Problem
In Deterministic/Stepping mode, clicking Step advances the brain (sim time increments) but the Stride
physics is frozen — cars don't move. Cause: the hosted pre-kernel hook gates physics with
`simRunning = GetMode()==Continuous`. `Stepping` reports `Deterministic`, so physics is frozen even on
the frame a step is granted. The editor's Step button calls `TimeController.Step(1/60)` directly, which
(in Stepping) increments the controller frame number and advances the clock (`MasterSyncController.Step`
@196). So the signal "a step advanced the sim this frame" = the controller's frame number changed.

## Fix — ONE FILE: `Stride/HrotStrideApp.Game/EditorStrideSubsystem.cs`
In the hosted pre-kernel hook (`PreKernelUpdateHook`, ~line 1016-1029) where
`bool simRunning = _editor.TimeController.GetMode() == TimeMode.Continuous;` is computed:

1. Add a field near the other hook state:
```csharp
// BATCH-S2-STEP: last sim frame number seen by the physics gate, to detect a deterministic step advance.
private long _lastSimFrameNumber = -1;
```

2. Replace the simRunning computation with advance-detection that KEEPS Continuous behavior identical and
   ADDS the step case:
```csharp
var timeMode  = _editor.TimeController.GetMode();
long curFrame = _editor.TimeController.GetCurrentState().FrameNumber; // current controller frame number
// In Deterministic (Stepping) mode, a granted Step() bumps the frame number — treat that frame as
// "advancing" so physics runs exactly one step. Continuous is unchanged (always running).
bool steppedThisFrame = timeMode != TimeMode.Continuous && curFrame != _lastSimFrameNumber;
_lastSimFrameNumber = curFrame;
bool simRunning = timeMode == TimeMode.Continuous || steppedThisFrame;

// On a deterministic step, advance physics by the fixed step delta (not the wall dt) so the step is
// deterministic and not over-integrated when the editor was idle.
float physicsDt = steppedThisFrame ? StepFixedDeltaSeconds : dt;
_physicsBracket.RunPreKernelStep(World, physicsDt, simRunning);
```
   (Keep the rest of the hook — the using/log — intact. The existing call passed `dt`; now pass `physicsDt`.)

3. Add the fixed-step constant (verify the real source — `TimeConfig.FixedDeltaSeconds` defaults to 1/60;
   if the controller/config exposes it, read that instead of hardcoding):
```csharp
private const float StepFixedDeltaSeconds = 1f / 60f; // matches EditorTimeTransport Step(1/60) + TimeConfig default
```

## Verify (before relying on it)
- `_editor.TimeController.GetCurrentState().FrameNumber` returns the controller's current frame number and
  is a pure read (no side effects). If `GetCurrentState()` is not the right accessor, find the one that
  exposes the live `_frameNumber` (it's incremented in `Step()` @196 and per-frame in `UpdateContinuous`).
- The editor's Step is `EditorTimeTransportAdapter.Step(1/60)` → `TimeController.Step(1/60)` (Stepping).
  Confirm a single Step bumps FrameNumber by exactly 1 so `steppedThisFrame` is true for exactly one tick.
- Confirm this hook is the HOSTED path (STRIDE_HOST_REAL_EDITOR) the user runs. Do NOT change the OFF-path
  `Tick()` (it passes `simRunning: true` already) unless it has the same freeze — if it does, apply the
  same detection there; otherwise leave it.
- Continuous mode must behave EXACTLY as today (simRunning true every frame, dt = wall dt).

## Constraints
- ONE file. Only the pre-kernel hook's simRunning/dt computation + the field/const. Don't touch the motors,
  the bracket internals, the reverse-sync, or the time controller.

## Acceptance
- Builds clean.
- (User) In Deterministic/edit mode, clicking Step advances the sim AND moves the cars/mannequins by one
  fixed step each click (physics runs for the granted step). Holding/repeating Step keeps advancing.
  Continuous (preview/run) is unchanged. Paused with no step = frozen (unchanged).
