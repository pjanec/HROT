# BATCH-02: External host loop + `editor_stride` composition skeleton
**Tasks:** STR-P0-T5, STR-P0-T6   **Phase:** P0 (Scaffolding)   **Est:** ~10–12h
**Dependencies:** BATCH-01 (projects + `FdpStrideTransform` exist; `HrotStrideApp.Game` references `Hrot.Stride.Core`, `Hrot.Stride.Animation`, `Hrot.StrideMock`).

Goal of this batch: (T5) drive Stride from an **external host loop** via `Game.Tick()` with a deterministic fixed-timestep clock, and (T6) stand up the `editor_stride` **composition skeleton** — one shared world + the in-process `ClusterMaster`/`ClusterSlave` orchestration pair — that boots headless and spawns owned entities. This is the "app composes & boots headless" milestone; the visual binding (T7) and the full end-to-end render smoke (T8) are BATCH-03.

There is **no Corrective Task 0** — BATCH-01 was approved with no P1 issues.

## Onboarding (read in order, before any code)
1. `.dev/.guides/DEV-GUIDE_claude.md` — working contract (test-quality section is binding).
2. `.dev/stride-1/Stride-Integration_v0_3.md` §8.1 (composition model — the spec for T6), §8.2 (reuse boundary), §8.3 (host loop & threading — the spec for T5), §1.1 (the bootstrapper seam), §15 item 10 (the confirmed offline-composition symbol list).
3. `.dev/stride-1/TASK-DETAIL.md` — STR-P0-T5, STR-P0-T6 (success conditions are authoritative).
4. `.dev/stride-1/reviews/BATCH-01-REVIEW.md` — context + open debt (STR-D1 CycloneDDS, STR-D4 asset-compile-real-proof).

Use the **codebase-memory MCP first** (project `D-Work-IOS-IG-SimHost-FDP`). Fall back to Read/Grep only for raw text.

### Verified facts & exact references (don't re-derive)
- **The reused seam** is `Hrot.StrideMock.StrideNodeBootstrapper` — [StrideNodeBootstrapper.cs](../../../Hrot/Subsystems/Hrot.StrideMock/StrideNodeBootstrapper.cs). Key surface: `BootstrapNode(HrotNodeConfig, NodeRole, INetworkFactory) → HrotNodeContext`; `void Tick(float dt)` (advances `SlaveTranslator?.Tick()` → `ClusterSlave.Tick()` → `Context.Kernel.Update(dt)` → `EventBus.SwapBuffers()`); exposes `Context`, `SimGroup`, `PostSimGroup`, `ProducerBuffer`, `ConsumerBuffer`, `Camera`, and an `ApplicationSystemsRegistrar` hook. Domain modules (kinematics/perception/combat/navigation) are injected via its **constructor**. `StrideNodeBootstrapper.Role = MuscleGround | Perception | NavigationSolver | ImageGenerator`.
- **The composition to mirror** is `Hrot.Editor.EditorSubsystem` — [EditorSubsystem.cs](../../../Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs). It is ~1400 lines and heavily coupled to Raylib/WinForms/ImGui/replay UI — **do NOT port it wholesale**. Extract only the simulation+orchestration core proving T6's success conditions. The exact lines that establish that core (read them):
  - `const int EditorNodeId = 0;` (line 149); `INetworkFactory = new OfflineNetworkFactory()` (162).
  - `_world = new EntityRepository(); _orchestrationBus = new FdpEventBus(); OrchestrationEventRegistry.RegisterAll(_orchestrationBus); OrchestratorEventRegistry.RegisterInternalEvents(_orchestrationBus); _kernel = new ModuleHostKernel(_world, accumulator);` (456–461) — **note the orchestration bus is a distinct `FdpEventBus`, not `_world.Bus`.**
  - `new ClusterSlave(EditorNodeId, "Editor", _orchestrationBus)` (581); `new NetworkSpawningSystem(tkbDb, elm, entityMap, idAllocator, localNodeId: EditorNodeId, translators)` (612); `new OrchestrationLogicPack(clusterSlave)` (674).
  - `var offlineConfig = new ClusterConfiguration { Mandatory = Array.Empty<string>() }; _clusterMaster = new ClusterMaster(_orchestrationBus, offlineConfig);` (1091–1092).
  - Per-frame orchestration pump: `_orchestrationBus.SwapBuffers(); _clusterMaster.Tick();` (1373–1374).
- **Headless boot-test model:** `EditorHarness` + `EditorSubsystemBootTests` in [Hrot.ClusterRunner.Integration.Tests](../../../Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/EditorSubsystemBootTests.cs) — mirror this style for the `EditorStrideSubsystem` boot test (construct headless, assert core properties, pump frames without throwing).
- `OfflineNetworkFactory` lives in `Hrot.Editor` ([OfflineNetworkFactory.cs](../../../Hrot/Subsystems/Hrot.Editor/OfflineNetworkFactory.cs)); `ClusterMaster`/`ClusterConfiguration`/`OrchestrationLogicPack` in `Hrot.Orchestrator`; `ClusterSlave` in `Fdp.Toolkits`; `NetworkSpawningSystem` in `Fdp.Toolkits`; `ModuleHostKernel` in `Fdp.ModuleHost`. Add the `ProjectReference`s these require to `HrotStrideApp.Game` (the Raylib/WinForms tag-alongs are accepted per §1.1/§8.3). [VERIFY] which assembly exposes `CgfLogicPack` and `SimHostCoreLogicPack` and reference them.
- Authority query helpers `.WithOwned<T>()` / `.WithoutOwned<T>()` are in `Fdp.Core.QueryBuilder` (confirmed); authority lives in `EntityMetadataCold.AuthorityMask`.

**Complete the tasks in sequence; do NOT start T6 until T5 is implemented, its tests are written, and ALL tests (incl. BATCH-01's) pass.** Work autonomously; run builds/tests and fix root causes to completion. Only stop on a genuine breaking design flaw or unrecoverable blocker — e.g. if T6's minimal composition genuinely cannot boot without dragging in the entire editor UI stack, document precisely what blocks it and what the minimal viable composition is, then stop.

---

## Tasks

### Task 1: `StrideHrotGame` external host loop (STR-P0-T5)
**File:** `Stride/HrotStrideApp.Game/StrideHrotGame.cs` (NEW) + a testable host-loop driver (see below). Spec: design §8.3.
`StrideHrotGame : global::Stride.Engine.Game` — the process's Stride game, driven from an **external** host loop via `Game.Tick()` with the internal `ThreadThrottler` disabled, pumping the Stride window's OS events each iteration. **[VERIFY]** on Stride **4.2.1.2487**: (a) the exact API to drive an external main loop (`Game.Tick()` vs `Run()`/`RunCallback`), (b) how to disable the internal throttler / vsync in external-loop mode, (c) the SDL2 Windows event-pump call (the analog of `Application.DoEvents()`). Record what you find.

Because a full Stride `Game` requires a GraphicsDevice (not available in headless CI), **factor the deterministic fixed-timestep clock into a separate, pure, testable driver** — e.g. `StrideHostLoopDriver` in `Hrot.Stride.Core` (or in the Game project) — that, given a tick callback and an elapsed-time source, calls the callback exactly the right number of times with a **fixed** dt regardless of how much wall-clock/render time elapsed. `StrideHrotGame` wires this driver to `Game.Tick()` + the OS event pump + `StrideNodeBootstrapper.Tick(dt)`. Keep the GPU/window bring-up out of the driver so the driver is unit-testable.

**Tests required** (unit, headless — in `Hrot.Stride.Core.Tests` or a new Game-test project, your call):
- Driving the host-loop driver for a known wall-clock span yields a **deterministic** number of fixed-dt simulation ticks (e.g. 1.0 s at 60 Hz fixed step → 60 ticks), and the **simulation clock advances by `nTicks × fixedDt` independent of the render-frame count** (feed it irregular/large render frame gaps and assert the sim-tick count/clock is governed by the fixed step, not the render cadence). Assert real counts/values.
- A leftover-time / accumulator case: a partial step does not produce an extra tick and is carried over.
- (Document, don't unit-test the GPU path:) note in the report how `StrideHrotGame` itself was verified to construct/advance (manual run or deferred to T8 smoke).

### Task 2: `EditorStrideSubsystem` composition skeleton (STR-P0-T6)
**File:** `Stride/HrotStrideApp.Game/EditorStrideSubsystem.cs` (NEW). Spec: design §8.1–§8.2.
Build the **minimal headless composition** that mirrors `EditorSubsystem`'s simulation+orchestration core (the lines cited above), **stripped of** the Raylib/WinForms/ImGui editor panels, AI-hot-reload, breakpoints, replay-UI, culling/style modules (those are P5 / not needed for P0). It must establish:
- one shared `EntityRepository` + simulation `FdpEventBus` (`world.Bus`) + `ModuleHostKernel`;
- `OfflineNetworkFactory`;
- the Brain logic (`CgfLogicPack`) **and** the Muscle logic for P0 — use the existing `SimHostCoreLogicPack` (its FDP integrators are the P0 movement stub; design §14 step 0). Leave a clearly-named seam/comment where `StrideKinematicsModule` (P1, STR-P1-T1) will replace the SimHost kinematics. (Do **not** build `StrideKinematicsModule` now — it's P1.)
- `NetworkSpawningSystem` with `localNodeId = 0` and the spawn pipeline (ELM + `CreateEntityRequestSystem` + a scenario/spawn request source) sufficient to spawn entities through the Brain path and stamp `OwnerNodeId = 0`;
- a **separate** orchestration `FdpEventBus` (`_orchestrationBus`) — distinct instance from `world.Bus` — with `OrchestrationEventRegistry.RegisterAll` + `OrchestratorEventRegistry.RegisterInternalEvents`;
- in-process `new ClusterSlave(0, "Editor", _orchestrationBus)` wrapped in `OrchestrationLogicPack` and registered with the kernel;
- `new ClusterMaster(_orchestrationBus, new ClusterConfiguration { Mandatory = Array.Empty<string>() })`;
- a per-frame pump that does `_orchestrationBus.SwapBuffers(); _clusterMaster.Tick();` alongside the kernel tick (mirroring EditorSubsystem 1373–1374), and an `Initialize()`/`Tick(dt)` surface usable from a headless harness.

You may **reuse** `OfflineNetworkFactory` and the spawn-pipeline component types directly (don't re-implement). Prefer composing the existing `StrideNodeBootstrapper` for the Muscle node where it cleanly fits, **or** wire the kernel directly mirroring `EditorSubsystem` — choose whichever yields the minimal correct composition for the success conditions, and explain the choice in the report.

**Tests required** (integration, headless — model on `EditorSubsystemBootTests`; new test project e.g. `Stride/HrotStrideApp.Game.Tests/` or reuse an existing integration test project that can reference the Game assembly — [VERIFY] the cleanest place and document it):
- **Boots headless without throwing:** `world`, `kernel`, and a time-controller are created and non-null after `Initialize()`.
- **Separate bus:** assert `_orchestrationBus` is a *different `FdpEventBus` instance* from `world.Bus` (reference inequality), per §8.1.
- **ClusterMaster latch + Standby:** with `Mandatory = Array.Empty<string>()`, after init/first tick the master has released its bootstrap latch and the observed initial cluster state is `Standby`. Assert the actual state value (consult `ClusterMaster`/the cluster-state API for how `Standby` is observed — [VERIFY]).
- **Owned-from-birth spawn:** spawn an entity through the Brain spawn path (`SpawnEntityCommand` / `CreateEntityRequestSystem` with `OwnerNodeId = 0`); after the spawn tick, assert the entity is **`.WithOwned<SimTransform>()`** (authority granted instantly, no deferred handshake) and that its `OwnerNodeId`/authority reflects node 0. This is the core invariant — assert it via the real query helper against the real repository, not a flag.
- Pump N frames (e.g. 60) after a spawn without throwing.

---

## Success Criteria
- [ ] STR-P0-T5: external-loop driver implemented + wired into `StrideHrotGame`; host-loop driver tests prove deterministic fixed-dt tick count and sim-clock independence from render rate; the Stride external-loop / throttler / SDL2-event-pump APIs are [VERIFY]'d and documented.
- [ ] STR-P0-T6: `EditorStrideSubsystem` boots headless; orchestration bus ≠ world bus; `ClusterMaster` releases latch + initial state `Standby`; Brain-path spawn yields `OwnerNodeId=0` + `.WithOwned<SimTransform>()` from birth; pumps frames without throwing.
- [ ] Full test suite green (BATCH-01 + BATCH-02); `HrotStrideApp.Game` builds clean; no new warnings beyond pre-existing NU1608; report submitted.

## Report Requirements (`reports/BATCH-02-REPORT.md`)
Answer: the exact Stride 4.2.1.2487 external-loop / throttler-disable / SDL2-event-pump APIs you found (with symbol names) and any deviation from §8.3's assumptions; how you kept the host-loop driver testable without a GPU and how (if at all) you verified `StrideHrotGame` itself advances; the minimal-composition decision for T6 (reuse `StrideNodeBootstrapper` vs direct kernel wiring) and exactly which `EditorSubsystem` pieces you deliberately omitted and why; how `Standby` cluster state is observed in the test; which assemblies you added as references to `HrotStrideApp.Game` and any tag-along/Raylib/WinForms friction; whether the CycloneDDS codegen workaround (STR-D1) held; weak points; suggested one-line commit message. Report actual test counts/output. Do NOT ask comprehension questions.
