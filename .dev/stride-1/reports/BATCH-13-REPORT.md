# BATCH-13 Report — StrideAnimationBackend + demo animation content (Phase 4 start)

**Tasks:** STR-P4-T1, STR-P4-T2 — both complete. All tests green.

## Implementation Summary

### STR-P4-T1 — real `StrideAnimationBackend` + `PerEntityBlendTreeBuilder`
Replaced the BATCH-01 P0 stub (`Stride/Hrot.Stride.Animation/StrideAnimationBackend.cs`,
17 members previously throwing `NotImplementedException`) with a real, full
`IAnimationBackend` modeled on `FakeAnimationBackend`'s semantics:
- Generation-safe per-entity handle pool (`Entry[]` + free-index stack; stale handles never
  resolve to a reused slot).
- `UpdateLocomotionInputs(handle, velX, velZ, velY, grounded)` (non-interface, signature mirrors
  `FakeAnimationBackend`) recomputes the idle/walk/run blend immediately.
- 8-slot montage state machine: blend-in ramp, blend-out window, natural completion, play-rate
  scaling, notify-marker crossings (fire-once mask), `StopMontageOnSlot` forces the blend-out
  window (does not hard-clear — mirrors DD-Fake §3.3).
- Stance transitions, aim layer state, footstep cadence (0.9 m stride, min 0.3 m/s), metrics,
  both `DrainNotifies` overloads.

New files:
- `Stride/Hrot.Stride.Animation/LocomotionBlend.cs` — the **testable seam**:
  `LocomotionBlend.FromSpeed(speed)` → `LocomotionBlendWeights { Idle, Walk, Run, LowerClip,
  UpperClip, Factor }`. Pure, no Stride types.
- `Stride/Hrot.Stride.Animation/PerEntityBlendTreeBuilder.cs` — the **GPU-bound seam**:
  `: Stride.Engine.IBlendTreeBuilder`, modeled on the template `AnimationController`.

Root-motion hooks (DD-1 §19) intentionally **not implemented** per design.

### STR-P4-T2 — mannequin `CharacterAnimationDefDto`
`UrbanCombatNewScenario.BuildMannequinAnimationDef()` (in
`FDP/Examples/Fdp.Examples.Scenarios/Integrated/UrbanCombatNewScenario.cs`) authors the
descriptor per the DD-4 schema and attaches it to InfantrySoldier (2002) + Insurgent (2003) in
**both** registration paths (the instance `RegisterInfantrySoldier`/`RegisterInsurgent` and the
static `RegisterUrbanCombatTkbTemplates` used by `EditorStrideSubsystem`). Added the
`Hrot.MuscleCharacter.Animation` project reference to `Fdp.Examples.Scenarios` (no cycle —
that assembly only depends on `Fdp.Core` + `Fdp.Toolkits`).

Content: Slots `Locomotion`(0)/`FullBody`(100); Montages `Idle`/`Walk`/`Run` on slot 0 +
`Jump_Start`/`Jump_Loop`/`Jump_End` on slot 100, all `AssetRef = "Animations/<Name>"`;
`SupportedStances = {Standing, Crouched}`; footstep notify markers on Walk/Run/Jump_End;
`AimConfig = null` (mannequin has no aim-offset rig this pass).

## Testable-vs-GPU split (the core seam)

| Concern | Where | Tested how |
|---|---|---|
| speed → idle/walk/run weights + which two clips blend + factor | `LocomotionBlend` (pure) | headless `[Theory]` per threshold |
| montage slot state machine (active/blend-out/complete, notifies, play-rate) | `StrideAnimationBackend` (pure, no `GraphicsDevice`) | headless behavioral tests |
| registration / stale-handle / pool exhaustion / stance / footsteps / metrics | `StrideAnimationBackend` | headless behavioral tests |
| **creating `AnimationClipEvaluator`s, installing `BlendTreeBuilder`, `AnimationComponent` pose composition** | **`PerEntityBlendTreeBuilder` (GPU-bound)** | **NOT unit-tested — human run + BATCH-14** |

The backend computes all weights/phases headlessly each `Tick`, then (only if a
`PerEntityBlendTreeBuilder` was attached via `AttachBlendTreeBuilder`) pushes that state into the
builder via `SetLocomotion`/`SetMontage`. In headless tests no builder is attached, so the full
behavioral surface runs with zero Stride GPU dependency.

## Stride 4.2.1.2487 `AnimationComponent` blend API ([VERIFY] result)

Confirmed by reflecting the installed Stride.Engine assembly (4.2.1.2442 on this machine; same
4.2.1 API the csproj pins to 2487):
- `Stride.Engine.IBlendTreeBuilder.BuildBlendTree(List<AnimationOperation> blendStack)` — note
  the interface lives in **`Stride.Engine`** (not `Stride.Animations`), and the param is `List<>`
  (Stride's `FastList<AnimationOperation>` derives from `List<>`; DD-1 §15.1 says `FastList`).
- `AnimationComponent.BlendTreeBuilder` (settable), `.Blender.CreateEvaluator(AnimationClip)`,
  `.Blender.ReleaseEvaluator(evaluator)`, `.Blender.Compute(List<AnimationOperation>, out AnimationClipResult)`.
- `AnimationOperation.NewPush(evaluator, TimeSpan)`, `NewBlend(CoreAnimationOperation, float)`,
  `NewPop(evaluator, TimeSpan)`.
- `CoreAnimationOperation { Blend, Add, Subtract }`.

`PerEntityBlendTreeBuilder.BuildBlendTree` pushes the lower locomotion clip, the upper, a
`NewBlend(Blend, factor)`, then (if a montage slot is active) the montage clip + a second
`NewBlend(Blend, montageWeight)` on top — exactly the template `AnimationController` shape.

## Where the descriptor is authored + attachment

`Fdp.Examples.Scenarios/Integrated/UrbanCombatNewScenario.cs` — alongside the
`StrideRenderModelDefDto` (which is what §6.5/§12 author there). Attached to InfantrySoldier(2002)
and Insurgent(2003) in all four `AddDescriptor` sites. `EditorStrideSubsystem` already calls
`RegisterUrbanCombatTkbTemplates(TkbDb)`, so the editor_stride TkbDb now carries it.

## speed→blend-weight thresholds (m/s) + tests

- `speed <= 0.1` (IdleSpeed) → pure Idle.
- `0.1 < speed < 1.5` (WalkSpeed) → Idle↔Walk, `factor = sqrt((speed-0.1)/1.4)` (sqrt-skew toward
  Walk, as the template does to avoid foot-slide).
- `speed == 1.5` → pure Walk.
- `1.5 < speed < 4.0` (RunSpeed) → Walk↔Run, linear `factor = (speed-1.5)/2.5`.
- `speed >= 4.0` → pure Run (clamped).

Tested: `[Theory]` asserting exact Idle/Walk/Run at 0/0.1/1.5/4.0/6.0 m/s (and that the three
always sum to 1); the sqrt-skew midpoint (factor ≈ 0.7071, Walk > 0.5); the linear Walk→Run
midpoint (0.5/0.5); `UpdateLocomotionInputs` deriving the blend from a (2.4, 3.2)→4.0 m/s planar
velocity.

## Can it wire into editor_stride now, or does it need BATCH-14?

**Needs BATCH-14's locomotion bridge to be visible.** The backend is contract-complete and
attachable today (`AttachBlendTreeBuilder`), but nothing yet (a) creates the
`PerEntityBlendTreeBuilder` from a real `AnimationComponent` when a humanoid visual is bound, nor
(b) feeds `SimVelocity`→`UpdateLocomotionInputs` each tick. That wiring (the
`AnimationRuntimeBridgeSystem` reading physics-sourced velocity, DD-1 §10 / §6.4) + the visible
walk/run/jump harness cases are BATCH-14. The descriptor's `Animations/*` URLs must also be
resolved to actual `AnimationClip`s and handed to the builder (asset-load step) in that bridge.

## Test Results

Full Stride solution (`Stride/HrotStrideApp.sln`), all green:
- `Hrot.Stride.Animation.Tests`: **28** (4 prior contract + 24 new behavioral).
- `HrotStrideApp.Game.Tests`: **58** (48 prior + 10 new T2 mannequin-descriptor).
- `Hrot.Stride.Core.Tests`: **215** (unchanged).

Cross-checked unaffected suites: `Hrot.MuscleCharacter.Animation.Tests` **195** pass. Full
`IOS-IG-SimHost.sln` builds clean (0 errors; only pre-existing obsolete/nullable warnings in
unrelated test projects — my new files emit 0 warnings).

## Design Decisions
- Locomotion clips authored as slot-0 montages so all six `Animations/*` AssetRefs live in one
  `MontageDict` and resolve uniformly; the backend *blends* the locomotion ones via the blend
  tree rather than playing them as one-shot montages.
- Absolute-speed thresholds (m/s) instead of the template's normalized 0..1 input, because the
  backend receives physics-sourced velocity. Documented constants on `LocomotionBlend`.
- `PerEntityBlendTreeBuilder` is `public` (not `internal`) so the BATCH-14 bridge can construct it;
  Stride types still never appear on the `IAnimationBackend` surface (DD-1 §16 honored).

## Deviations
- DD-1 §15.1 names the builder param `FastList<AnimationOperation>`; the actual 4.2.1
  `IBlendTreeBuilder.BuildBlendTree` param is `List<AnimationOperation>` (FastList derives from it).
  Used `List<>` to match the real interface. **Benefit:** compiles against the real package.
  **Risk:** none.
- Added `Hrot.MuscleCharacter.Animation` as a project reference of `Fdp.Examples.Scenarios`.
  **Benefit:** lets the demo scenario author the descriptor. **Risk:** minimal (no cycle).

## Known Issues / Weak Points
- Montage→slot resolution in `PlayMontageOnSlot` lands everything on slot 0 deterministically (so
  the state machine is testable); honoring the descriptor's per-montage `Slot` (0 vs 100) at the
  backend is deferred to when the dispatcher/bridge feeds baked data in (BATCH-14).
- The aim layer is tracked but not GPU-applied (no additive layering in the blend tree this pass).
- Locomotion phase is a deterministic normalized cycle in the headless backend; real clip-duration
  phase advance happens GPU-side in the builder. No skeletal playback is claimed — that is GPU and
  unverified here.

## Suggested Commit Message
`feat(stride): real StrideAnimationBackend + PerEntityBlendTreeBuilder + mannequin CharacterAnimationDefDto (BATCH-13, STR-P4-T1/T2)`
