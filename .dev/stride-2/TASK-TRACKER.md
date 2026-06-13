# Stride-2 Task Tracker — Editor-hosts-sim / Stride-as-muscle

**Goal:** Pivot from "EditorStrideSubsystem hosts a mirrored sim" to "the *real* Hrot.Editor hosts the sim; Stride supplies muscle `IEcsModule`s + 3D view + (later) editor UI in a second window." One shared `EntityRepository`, no loopback DDS in-process. Architecture-blessed seam = standard `IEcsModule` injection (verified: `StrideNodeBootstrapper` 4-slot ctor; `SimHostMode.Internal/External` feature switch). See ONBOARDING.md + memory `project_stride_muscle_seam_already_scaffolded`.

**Working agreements:** dev via sonnet (user token-constrained); lead reviews diffs + commits; GPU-only verification by user (no headless GPU); headless-green ≠ live-works (trace real path, faithful assembled tests).

## Brain↔muscle contract (the seam)
- Intents brain→muscle (immutable input to muscle): `NavigationIntent`, `EntityMission`, stance/anim intents, `WeaponFireIntent`, `EqsSensor`.
- Authoritative muscle→brain output: `SimTransform/SimVelocity/WorldPos`, `NavigationStatus` (Arrived/frustration), `CrowdMotorIntent`, `NavState`, `EntityDamage`, perception events (`TargetHeardEvent`/`SensorTrackStateEvent`), `EqsResultEvent` (local, offline Path B).
- Required local singletons a Stride muscle must supply: `INavmeshProvider`, `IDtCrowdProvider`, `IPathRegistry`, `SpatialGridData` (SpatialHashSystem), `EqsResultPool`, `EqsSolverGlobalState`. (`IEqsTemplateRegistry` does NOT exist; cover/path test-registries are test-only.)

## Stages
- [~] **Stage 1 — Modularize Stride muscle into `IEcsModule`s.** Code-complete (commit pending GPU). Extracted `StrideMuscleModuleSet` (kernel-resident: kinematics/combat/damage/nav-bridge/vehicle-nav-intent) + `StridePhysicsBracket` (host-driven: lifecycle/motors/reverse-sync/forward-sync, ordered RunPreKernelStep/RunPostKernelStep). Pure extraction, behavior-identical. Build 0 err, 585 tests green. **⛳ AWAITING user GPU-verify F1–F7 unchanged.** Note: muscle set exposes raw systems via `EditorStrideSimulationModule` adapter; Stage 3 adds `ToModuleList()` for MuscleModuleFactory.
- [x] **Stage 2 — Editor Internal-muscle module-set injectable.** DONE (421d9514). `EditorSubsystem.MuscleModuleFactory` additive seam; default = SimHost set. Build 0 err; 15 editor tests pass; clusterrunner -m editor behavior-identical.
- [~] **Stage 3 — Run real editor in Stride app.** Decomposed:
  - [x] **3a** — additive pre/post-kernel host hooks in `EditorSubsystem.Update` (`PreKernelUpdateHook`/`PostKernelUpdateHook`, default null). CPU-verified, 15 editor tests pass.
  - [ ] **3b** — `EditorStrideSubsystem` hosts the real `EditorSubsystem` (Headless): inject Stride muscle via `MuscleModuleFactory` (+`ToModuleList()`), drive `StridePhysicsBracket` via 3a hooks, repoint Stride view systems (visual/animation/gizmo/selection) at editor.World, delete hand-built kernel. ⛳ GPU gate F1–F7.
  - **⚠ 3b risk:** `EditorSubsystem.Update` does NOT call `TimeController.Step` (EditorStrideSubsystem did). Must resolve the editor's time-advance model so the sim ticks under Stride's loop.
- [ ] **Stage 4 — Editor UI in window #2.** WindowManager + DrawUI over shared world; panels incrementally. ⛳ user GPU gate per panel group.
- [ ] **Stage 5 — Later.** Stride perception + EQS `IEcsModule`s; networked muscle node via `StrideNodeBootstrapper` + `SimHostMode.External`; 3D-pick ↔ editor-selection sync.

## Batches
- [x] BATCH-S2-A (Stage 2): additive injectable Internal-muscle seam in editor. (sonnet) → 421d9514
- [x] BATCH-S2-B (Stage 1): modularize Stride muscle + host physics-bracket. (sonnet) → edba115a, GPU-verified ✅
- [x] BATCH-S2-C (Stage 3a): pre/post-kernel host hooks in EditorSubsystem.Update. (sonnet) → CPU-verified
- [ ] BATCH-S2-D (Stage 3b): EditorStrideSubsystem hosts real EditorSubsystem. (sonnet, GPU gate)
