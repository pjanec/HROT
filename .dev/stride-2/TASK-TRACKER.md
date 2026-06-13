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
  - [x] **3b-1** — muscle adapter `StrideMuscleModuleSet.ToEditorModuleList()` (+`StrideMuscleModule` matching today's phase order) + faithful headless test booting the REAL EditorSubsystem(Headless) with Stride muscle injected, driving nav frame-locked. **CPU-verified: 4 new tests (SI1–SI4) pass, 589 total.** Real editor boots headless w/ muscle ✅.
  - **Time model RESOLVED:** keep editor Deterministic; host calls `editor.TimeController.Step(dt)` in `PreKernelUpdateHook` (frame-locked), then `editor.Update(dt)` ticks kernel.
  - **3b-2 blockers surfaced (need additive editor seams, CPU-verifiable):** (1) host must register `CrowdAgent`/`CrowdMotorIntent`/`NavAgentProfile` (done in factory lambda — no editor change); (2) need PUBLIC access to editor World/Kernel/TimeController + a public entity-spawn path (test used reflection on `_requestSource`/World/Kernel — production must not).
  - [x] **3b-2a** — public host surface on EditorSubsystem: `World`/`Kernel`/`TimeController`/`PreviewController`/`EditorLogic` promoted internal→public + new `EntityCreationRequestSource` accessor; headless boot test de-reflected. CPU-verified (589 + 15 editor tests; pre-existing FileMenuHasSaveCommands fail unrelated).
  - [x] **3b-2b GPU-VERIFIED** (2026-06-13 16:52 log): hosted mode ENABLED, navmesh baked, F2/F4/F5 simulate correctly through real editor kernel, no errors, flagged risks (TkbDb/navmesh singleton) did not materialize. — live rewire, FLAG-GATED (`STRIDE_HOST_REAL_EDITOR=1`, default OFF). Hosted path: `EditorStrideSubsystem.InitializeHosted` constructs real `EditorSubsystem`(Headless), injects Stride muscle via `MuscleModuleFactory`, repoints World/Kernel/TimeController/ScenarioSource to editor's, drives bracket via `PreKernelUpdateHook` (Step+pre), `TickHosted` preserves post-kernel order (anim→fwd-sync→gizmo→sel). Spawn routes via `_editor.EntityCreationRequestSource`. OFF path byte-identical. Build 0 err; **591 tests pass (2 new hosted SI-HM-1/2).** **⛳ AWAITING user GPU-verify the ON path** (set flag, check F1–F7/C/gizmos). Then flip default + delete OFF path (Stage 3c).
  - **3b-2b GPU risks:** dup TkbDb instance (visual resolution), navmesh singleton double-set, editor's extra headless managers. All log-observable.
- [~] **Stage 4 — Editor UI in window #2.** Incremental panel bring-up over shared hosted-editor world. `_headless` gates ALL UI (canvas+adapters+panels+RegisterWindows together); panels split pure-ImGui (Toolbar/Orbat/Preview) vs canvas-dependent (Spawner/Mission/Config/Zone/SharedOrbat).
  - [~] **4.1** — pure-ImGui Toolbar + Orbat panels in 2nd window via `StrideEditorUiHost` (calls `EditorToolbarPanel/EditorOrbatPanel.DrawContent(logic)` directly; wrappers are internal). Added `EditorStrideSubsystem.HostedEditorLogic`. Simple inspector kept under collapsing header. Gated: off path byte-identical. Build 0 err; 595 tests (4 new). **⛳ AWAITING user GPU-verify** (flags on → toolbar+orbat render, orbat reflects live entities). No editor change needed.
  - [ ] **4.2** — 2D map canvas in 2nd window (editor `Headless=false` + split the `_headless` gate; canvas render + input in Stride raylib ctx).
  - [ ] **4.3** — canvas-dependent panels via real `WindowManager` + `editor.RegisterWindows` (converge on reuse, drop direct-draw).
- [ ] **Stage 5 — Later.** Stride perception + EQS `IEcsModule`s; networked muscle node via `StrideNodeBootstrapper` + `SimHostMode.External`; 3D-pick ↔ editor-selection sync.

## Batches
- [x] BATCH-S2-A (Stage 2): additive injectable Internal-muscle seam in editor. (sonnet) → 421d9514
- [x] BATCH-S2-B (Stage 1): modularize Stride muscle + host physics-bracket. (sonnet) → edba115a, GPU-verified ✅
- [x] BATCH-S2-C (Stage 3a): pre/post-kernel host hooks in EditorSubsystem.Update. (sonnet) → CPU-verified
- [ ] BATCH-S2-D (Stage 3b): EditorStrideSubsystem hosts real EditorSubsystem. (sonnet, GPU gate)
