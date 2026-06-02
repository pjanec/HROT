# BATCH-14 Report — Locomotion bridge + montage dispatch (Phase 4 complete)

**Tasks:** STR-P4-T3, STR-P4-T4 — both complete. All Stride tests green; full solution builds clean; no shared-code touched (zero baseline regression by construction).

## Implementation Summary

### STR-P4-T3 — locomotion bridge wired into editor_stride
New file `Stride/Hrot.Stride.Animation/StrideAnimationBridge.cs` — the headless, testable
analogue of DD-1 §10's `AnimationRuntimeBridgeSystem`, adapted to the editor_stride world
(which does **not** run the DD-4 `AnimationTkbTranslator`, so there are no
`CharacterAnimationDefRuntime`/`AnimationExecutorState` components to read). Per
`Execute(world, dt)` it:
1. **Reconciles backend registration** with the live mannequin set — `RegisterEntity` on appear,
   `UnregisterEntity` on death/disappearance (register/unregister on appear/death).
2. **Pumps locomotion inputs** — reads `SimTransform`+`SimVelocity`, maps FDP planar velocity
   (X=east, Y=north) to the backend's two horizontal axes + vertical, and calls
   `StrideAnimationBackend.UpdateLocomotionInputs` so the backend blends idle→walk→run by speed.
3. **Advances in-flight jump sequences** and **ticks the backend** once.

Wired into `EditorStrideSubsystem.Initialize` (step 14): constructs the real
`StrideAnimationBackend` (the `IAnimationBackend`) + the bridge, exposed as
`AnimationBackend`/`AnimationBridge` properties. Driven manually in `Tick()` immediately after
`Kernel.Update()` (DD-1 §10 phase placement — reads post-physics `SimTransform`/`SimVelocity`).

### STR-P4-T4 — montage dispatch
The bridge reads `OffMeshTraversalStartedEvent` (the off-mesh-link seam) and, for a
`TraversalKind.Jump`, plays the **Jump_Start → Jump_Loop → Jump_End** chain on the montage slot
via `PlayMontageOnSlot`, advancing to the next phase when the backend reports the slot finished
(`IsAnySlotActive == false`). `EditorStrideSubsystem.Tick` feeds the bridge the bus events:
`((ISimulationView)World).ReadEvents<OffMeshTraversalStartedEvent>()` then
`AnimationBridge.DispatchTraversals(...)`.

### Harness — Walk / Run / Jump (required this phase)
New file `Stride/HrotStrideApp.Game/StrideAnimationHarnessCases.cs`, registered in
`StrideHrotGame.BuildTestHarness` after the four BATCH-12 cases:
- **"Walk Mannequin"** — spawns an InfantrySoldier (2002) and, via `ctx.RegisterUpdate`, each
  frame sets `SimVelocity` to ~1.5 m/s forward and advances `SimTransform` (physics is NoOp) for
  6 s; the bridge feeds that velocity → backend blends to **Walk**. Logs the live Idle/Walk/Run
  weights.
- **"Run Mannequin"** — same at ~4 m/s → **Run**.
- **"Trigger Jump"** — publishes an `OffMeshTraversalStartedEvent{Jump}` for the newest mannequin
  **and** calls `bridge.TriggerJump` directly (works without the nav stack wired), starting the
  Jump_Start montage on the slot. Logs the slot state.

Each case logs via NLog (through `TestHarnessContext.Log`, which routes to the
`StrideTestHarness` NLog logger → `logs/editor_stride.log`).

## [VERIFY] results

**Backend ↔ AnimationComponent connection (the cleanest seam).** Registration is keyed by the
**FDP entity**, owned by the bridge: `RegisterEntity((uint)entity.Index, tkbType)` on appear,
producing the per-entity `AnimationBackendHandle`. The **GPU-bound** `AnimationComponent`
attachment is decoupled from registration: `StrideVisualFactory.CreateModelVisual` creates the
`AnimationComponent` on the skinned mannequin and the visual binding records the Stride entity in
`StrideVisualReference.VisualHandle`. The completing step (GPU-deferred) is: on visual appear,
resolve the same FDP entity → its `StrideVisualReference.VisualHandle` (the Stride entity carrying
the `AnimationComponent`) → load the `Animations/*` clips → construct `PerEntityBlendTreeBuilder`
→ `backend.AttachBlendTreeBuilder(handle, builder)`. The bridge exposes `TryGetHandle(entity, out
handle)` precisely so that GPU step can fetch the handle for a given FDP entity. This keeps the
**decision logic** (which clips, what weight, register/unregister) fully headless and tested,
while the only GPU-bound bit (creating evaluators from the `Blender` + pose composition) stays in
`PerEntityBlendTreeBuilder` — which needs a `GraphicsDevice` and is therefore human-verified.

**Animation module registration in editor_stride.** There is no `AnimationMuscleModule` in this
composition — editor_stride builds its system list explicitly (see `EditorStrideSubsystem`). The
backend + bridge are not kernel systems; they are owned by the subsystem and driven manually in
`Tick()` after `Kernel.Update()` (the same manual-drive pattern the subsystem already uses for the
reverse-sync group and split-authority sync, and the correct DD-1 §10 placement to read
post-kernel `SimVelocity`). The `StrideAnimationBackend` is the real `IAnimationBackend` (no fake
in editor_stride).

**Montage-dispatch seam.** `OffMeshLinkDetectionSystem` deliberately does **not** reference the
animation assembly; it publishes `OffMeshTraversalStartedEvent` on the bus (its documented egress,
EventId 2035). The bridge is the Hrot-side consumer that turns that event into
`PlayMontageOnSlot`. (`OffMeshLinkDetectionSystem` itself is not registered in editor_stride —
nav crowd isn't wired here — so the harness "Trigger Jump" both publishes the event and calls
`bridge.TriggerJump` directly to exercise the same code path live.)

## How the bridge walk/run/idle selection is tested via SimVelocity
`StrideAnimationBridgeTests` (in `Hrot.Stride.Animation.Tests`) builds a real `EntityRepository`,
spawns a mannequin, sets `SimVelocity.Linear`, calls `bridge.Execute(world, dt)`, then asserts
the backend's blend via `backend.QueryLocomotion(handle)` — i.e. the weights are read **after the
bridge derived them from SimVelocity**, not by calling the backend directly:
- rest (zero velocity) → `Idle≈1, Walk=0, Run=0`;
- `WalkSpeed` (1.5 m/s) → `Walk≈1`, Walk > Idle and Walk > Run;
- intermediate (0.8 m/s) → Walk and Idle both in (0,1), Run=0, weights sum to 1;
- `RunSpeed` (4.0 m/s) → `Run≈1`, Run > Walk and Run > Idle;
- a Walk→Run→rest sequence through one bridge transitions the dominant weight each step.

The Game.Tests integration tests drive the **actual wired subsystem**: spawn via the harness Walk
/ Run case, pump `sut.Tick` + `ctx.PumpUpdates`, then assert `AnimationBackend.QueryLocomotion`
shows Walk-dominant / Run-dominant — proving the end-to-end path (harness → SimVelocity → bridge
→ backend) in editor_stride.

## Montage sequencing test
`JumpSequence_AdvancesStart_Loop_End_ThenCompletes` triggers a jump, then ticks the bridge in
0.25 s steps recording which montage id is active on slot 0, asserting the exact order
`{Jump_Start, Jump_Loop, Jump_End}` then `ActiveJumpCount == 0`.
`DispatchTraversal_Jump_StartsMontage_OnSlot` asserts `IsAnySlotActive` + the slot's `MontageHash`
is the Jump_Start id; `DispatchTraversal_NonJumpKind_DoesNotStartMontage` asserts a Climb is
ignored.

## Shared-system change + baseline-regression check
**No shared `Fdp.Toolkits` or `Hrot.MuscleCharacter.Animation` system was modified.** All changes
are editor_stride-app-local: 2 edited files (`EditorStrideSubsystem.cs`, `StrideHrotGame.cs`) and
4 new files, all under `Stride/`. Therefore the SimHost (38 fail/573 pass),
Fdp.Examples.Scenarios (25 fail/43 pass), and anim-subsystem baselines cannot regress from this
batch. Verified anyway: `Hrot.MuscleCharacter.Animation.Tests` = **195 passed / 0 failed**
(unchanged baseline); full `IOS-IG-SimHost.sln` builds with **0 errors**.

## GPU-deferred (NOT claimed working)
Actual skeletal playback. The `PerEntityBlendTreeBuilder` (creating `AnimationClipEvaluator`s from
the `Blender`, installing the `BlendTreeBuilder`, composing the pose) needs a `GraphicsDevice` and
runs only inside the live Stride window; it is **not** attached by this batch (no clip-load +
attach step). The bridge drives the backend's *headless* blend/slot state and would push it into
the builder via `SetLocomotion`/`SetMontage` once attached. No claim is made that the mannequin
visibly walks/runs/jumps on the GPU — that is the human-run verification (the harness exists to
make it observable: Walk/Run move the visual via forward-sync and log the blend weights; Jump logs
the slot state).

## Test Results
Full Stride solution (`Stride/HrotStrideApp.sln`) — all green:
- `Hrot.Stride.Animation.Tests`: **41** (28 prior + **13 new** bridge tests: register/unregister
  reconciliation, idle/walk/run-from-SimVelocity, jump dispatch + Start/Loop/End sequencing).
- `Hrot.Stride.Core.Tests`: **215** (unchanged).
- `HrotStrideApp.Game.Tests`: **65** (58 prior + **7 new** end-to-end: backend/bridge wired,
  register-on-spawn, unregister-on-death, Walk/Run cases drive the blend through the subsystem,
  Trigger Jump starts the slot montage, case-order).
Cross-check: `Hrot.MuscleCharacter.Animation.Tests` = **195** (baseline unchanged).
Full `IOS-IG-SimHost.sln`: builds clean, **0 errors**.

## Design Decisions
- **Bridge lives in `Hrot.Stride.Animation`, not as a kernel `IEcsModuleSystem`.** editor_stride
  composes systems explicitly and drives the reverse-sync/split-sync manually in `Tick()`; the
  animation bridge follows that same manual-drive pattern so it runs at the right point (after the
  kernel reads post-physics SimVelocity) and so its logic is unit-testable against a bare
  `EntityRepository` without the kernel.
- **"Animated" = has `CharacterAnimationDefDto`.** The bridge takes an `isAnimatedClass`
  predicate; the subsystem implements it as "the TKB template carries a `CharacterAnimationDefDto`"
  (attached by STR-P4-T2 to InfantrySoldier/Insurgent). This avoids hard-coding TKB ids and
  reuses the existing descriptor.
- **Jump advance is event-driven on slot completion**, not a fixed timer — the backend's slot
  state machine owns the timing; the bridge chains the next phase when the slot goes idle.
- **Trigger Jump fires both paths** (publish event + direct `TriggerJump`) so the case works with
  or without the nav stack, and exercises the real event-seam shape.

## Deviations
- The DD-1 §10 `AnimationRuntimeBridgeSystem` (in `Hrot.MuscleCharacter.Animation`) is **not**
  reused directly: it requires `CharacterAnimationDefRuntime`+`AnimationExecutorState` ECS
  components that editor_stride never injects (no `AnimationTkbTranslator` in this composition).
  A dedicated `StrideAnimationBridge` implements the same responsibilities against the
  editor_stride entity model. **Benefit:** no need to stand up the full DD-4 component-injection
  pipeline just to demo locomotion in Mode 1. **Risk:** two bridge implementations exist; the
  Stride one is clearly scoped to editor_stride and documented as such.

## Known Issues / Weak Points
- The GPU clip-load + `PerEntityBlendTreeBuilder` attach step is not implemented (GPU-deferred,
  above). `TryGetHandle` is the seam left for it.
- `OffMeshLinkDetectionSystem` is not registered in editor_stride (nav crowd unwired), so jumps
  are only fired by the harness today; once nav is wired the same event path drives them.
- Locomotion uses the planar (X,Y) FDP velocity magnitude; vertical (Z) feeds grounded only. Aim
  layer is tracked by the backend but not GPU-applied (unchanged from BATCH-13).

## Suggested Commit Message
`feat(stride): locomotion bridge + jump montage dispatch wired into editor_stride + Walk/Run/Jump harness (BATCH-14, STR-P4-T3/T4)`
