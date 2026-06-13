# Stride-2 Task Tracker — Editor-hosts-sim / Stride-as-muscle

**Goal:** Pivot from "EditorStrideSubsystem hosts a mirrored sim" to "the *real* Hrot.Editor hosts the sim; Stride supplies muscle `IEcsModule`s + 3D view + (later) editor UI in a second window." One shared `EntityRepository`, no loopback DDS in-process. Architecture-blessed seam = standard `IEcsModule` injection (verified: `StrideNodeBootstrapper` 4-slot ctor; `SimHostMode.Internal/External` feature switch). See ONBOARDING.md + memory `project_stride_muscle_seam_already_scaffolded`.

**Working agreements:** dev via sonnet (user token-constrained); lead reviews diffs + commits; GPU-only verification by user (no headless GPU); headless-green ≠ live-works (trace real path, faithful assembled tests).

## Brain↔muscle contract (the seam)
- Intents brain→muscle (immutable input to muscle): `NavigationIntent`, `EntityMission`, stance/anim intents, `WeaponFireIntent`, `EqsSensor`.
- Authoritative muscle→brain output: `SimTransform/SimVelocity/WorldPos`, `NavigationStatus` (Arrived/frustration), `CrowdMotorIntent`, `NavState`, `EntityDamage`, perception events (`TargetHeardEvent`/`SensorTrackStateEvent`), `EqsResultEvent` (local, offline Path B).
- Required local singletons a Stride muscle must supply: `INavmeshProvider`, `IDtCrowdProvider`, `IPathRegistry`, `SpatialGridData` (SpatialHashSystem), `EqsResultPool`, `EqsSolverGlobalState`. (`IEqsTemplateRegistry` does NOT exist; cover/path test-registries are test-only.)

## Stages
- [ ] **Stage 1 — Modularize Stride muscle into `IEcsModule`s.** Carve EditorStrideSubsystem's muscle (Bullet physics+motors+reverse-sync, DotRecast nav) into clean modules; encapsulate the host-side physics bracket (motor-push → external Bullet step → reverse-sync) behind a small reusable interface. **GPU-verify F1–F7 unchanged.** ⛳ user GPU gate.
- [ ] **Stage 2 — Editor Internal-muscle module-set injectable.** Additive seam in EditorSubsystem/EditorApplication; default = today's SimHost set. **CPU-verify `clusterrunner -m editor` unchanged.** (autonomous)
- [ ] **Stage 3 — Run real editor in Stride app.** Inject Stride muscle modules; delete EditorStrideSubsystem's hand-built kernel; one shared repo. ⛳ user GPU gate (F1–F7 through real editor kernel).
- [ ] **Stage 4 — Editor UI in window #2.** WindowManager + DrawUI over shared world; panels incrementally. ⛳ user GPU gate per panel group.
- [ ] **Stage 5 — Later.** Stride perception + EQS `IEcsModule`s; networked muscle node via `StrideNodeBootstrapper` + `SimHostMode.External`; 3D-pick ↔ editor-selection sync.

## Batches
- [ ] BATCH-S2-A (Stage 2): additive injectable Internal-muscle seam in editor. (sonnet)
- [ ] BATCH-S2-B (Stage 1): modularize Stride muscle + host physics-bracket interface. (sonnet, GPU gate)
