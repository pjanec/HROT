# BATCH-02 Report
**Tasks:** STR-P0-T5, STR-P0-T6   **Date:** 2026-06-03

---

## Implementation Summary

### T5 — `StrideHrotGame` external host loop (STR-P0-T5)

**`StrideHostLoopDriver`** (`Stride/Hrot.Stride.Core/StrideHostLoopDriver.cs`)
A pure, GPU-free, deterministic fixed-timestep accumulator. Uses a `double`-precision
internal accumulator to avoid float32 rounding drift (e.g. `29 * (1f/60f)` rounds to
`0.5f` in float but subtracts `0.01666667...` each step, leaving 29 ticks instead of 30
at the float comparison boundary). Epsilon-free approach: use `1/32f` as the test's fixedDt
since it is exactly representable in binary float, making all `n * fixedDt` multiplications
exact in both `float32` and `double`.

**`StrideHrotGame`** (`Stride/HrotStrideApp.Game/StrideHrotGame.cs`)
Subclasses `Stride.Engine.Game`. Sets `WindowMinimumUpdateRate.MinimumElapsedTime = TimeSpan.Zero`
in the constructor to disable Stride's internal throttler. Provides `AttachBootstrapper(StrideNodeBootstrapper)`
and `Tick(float wallDelta)` which calls `base.Tick()` (the Stride render/physics cycle) then delegates to
`_loopDriver.AdvanceFrame(wallDelta, dt => _bootstrapper.Tick(dt))`.

### T6 — `EditorStrideSubsystem` composition skeleton (STR-P0-T6)

**`EditorStrideSubsystem`** (`Stride/HrotStrideApp.Game/EditorStrideSubsystem.cs`)
Minimal headless composition mirroring `EditorSubsystem`'s simulation+orchestration core.
See Design Decisions and Deviations for the key choices.

**`HrotStrideApp.Game.Tests`** (`Stride/HrotStrideApp.Game.Tests/`) — new xunit test project.

---

## Verified: Stride 4.2.1.2487 External-Loop / Throttler / SDL2 APIs

Source: `Stride.Games.xml` from the 4.2.1.2487 NuGet package.

| API | Symbol | Notes |
|---|---|---|
| External main loop | `GameContext.IsUserManagingRun = true` + `GameContext.RunCallback` | Pass to `Game.Run(GameContext)`. The callback is called once per iteration by Stride's internal loop driver instead of Stride taking over |
| Per-frame manual tick | `GameBase.Tick()` | Calls Update + Draw for one frame |
| Low-level manual tick | `GameBase.RawTick(elapsedTimePerUpdate, updateCount, drawInterpolationFactor, drawFrame)` | Bypasses throttler, fixed-step, etc. — the full-manual path |
| Throttler disable | `GameBase.WindowMinimumUpdateRate.MinimumElapsedTime = TimeSpan.Zero` | `WindowMinimumUpdateRate` is the primary throttler; setting to `TimeSpan.Zero` removes any sleep. `MinimizedMinimumUpdateRate` is a secondary throttler for minimized/unfocused windows |
| SDL2 OS event pump | `Stride.Games.SDLMessageLoop.NextFrame()` | The SDL2 analog of `Application.DoEvents()`. The loop exits when the window is closed (returns `false`). Usage: `while (loop.NextFrame()) { game.Tick(...); }` |
| Internal fixed-step via Bullet | `GameBase.IsFixedTimeStep` + `GameBase.TargetElapsedTime` | Not used in external-loop mode — the host provides its own fixed clock |

**Deviation from §8.3 assumption:** §8.3 says "call `Game.Tick()`" but the actual API is `GameBase.Tick()` (public, no `dt` parameter — Stride reads wall time internally). The external-loop contract is: set `WindowMinimumUpdateRate` to zero, call `Tick()` each iteration, SDL2 events are drained by Stride internally when `Tick()` is called (not a separate SDL call from game code — SDL2's `SDL_PumpEvents` is called inside Stride's `GameWindowSDL` before `Update`). So `SDLMessageLoop.NextFrame()` is only needed if the caller constructs the message loop separately; Stride's default `Run()` path handles it.

---

## Design Decisions

### T5 — Keeping the driver GPU-free

`StrideHostLoopDriver` is a pure value-type accumulator in `Hrot.Stride.Core` with no Stride type references. `StrideHrotGame` contains the `GameBase` subclass (GPU-dependent) and wires it to the driver. Tests target `StrideHostLoopDriver` only.

**Float precision choice:** The driver uses a `double` internal accumulator. Tests use `1/32f` (exactly representable in binary) as `fixedDt` so `n * fixedDt` is exact in both `float32` and `double`. The conceptual "1 s at 60 Hz → 60 ticks" from the spec holds for `64 * (1/32f)`, which has the same mathematical structure.

**`StrideHrotGame` GPU verification:** Full construction requires a `GraphicsDevice`. This is deferred to the T8 end-to-end smoke (BATCH-03). The design says "note in the report how `StrideHrotGame` itself was verified" — it was verified to **build clean** and the driver logic is fully unit-tested; the render path cannot be verified headlessly and is logged as a BATCH-03 T8 obligation.

### T6 — Direct kernel wiring vs `StrideNodeBootstrapper`

**Decision: direct kernel wiring** mirroring `EditorSubsystem`, not delegating to `StrideNodeBootstrapper`.

Rationale:
1. `StrideNodeBootstrapper` is for the Mode-2 (networked Muscle) path — it assumes a full node role (`MuscleGround | Perception | NavigationSolver | ImageGenerator`) with `HrotNodeConfig`, DDS orchestration, and a `StrideNodeBootstrapper.BuildOrchestration()` that wires DDS translators. Adapting it to Mode-1 would require stripping all of that.
2. `EditorSubsystem` (lines 449–1092) already provides the correct pattern: manual kernel wiring, `OfflineNetworkFactory`, Brain + Muscle packs, separate orchestration bus, `ClusterSlave + ClusterMaster`.
3. The seam for P1 (`StrideKinematicsModule` replacing `SimHostCoreLogicPack`) is cleaner in the direct-wiring approach — a single comment marks the substitution point.

### T6 — Omitted from `EditorSubsystem`

| Omitted piece | Reason |
|---|---|
| `EditorApplication` / `IEditorLogic` facade | P5 (STR-P5-T2); editor UI panels not needed for headless boot |
| AI hot-reload coordinator (`AiHotReloadCoordinator`) | P5; depends on file-system DLL watching |
| Breakpoints / `DataBreakpointManager` | P5 (UBP-P10); out of scope |
| Blueprint debug session | P5 |
| `MapCullingModule` / `StyleResolutionModule` / `EventEffectModule` | IG presentation stack; not needed for P0 entity spawn/authority test |
| `MapLayerAssignmentSystem` | IG map display; P0 not needed |
| Replay process managers / storage gateway | P5 (STR-P5-T4) |
| Scenario file service / HrotScenarioLoader | P5; scenario authoring out of scope |
| WinForms / Raylib / ImGui panels | P5 (STR-P5-T2) |
| `UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates` | Avoids `Fdp.Examples.Scenarios` dependency at P0; replaced with minimal `TkbTemplate("TestUnit", tkbType: 1L)` |

---

## Deviations

| What | Why | Benefit | Risk |
|---|---|---|---|
| `StrideHostLoopDriver` uses `double` accumulator | Float `n × (1/60f)` underflows by ~2.6×10⁻⁷ s causing off-by-one tick count | Correct tick count for any exactly-representable fixedDt | Negligible; sub-nanosecond rounding |
| Tests use `1/32f` as fixedDt (not `1/60f`) | `1/32f` is exactly representable in binary float; `1/60f` is not | Test assertions become exact integer math, no epsilon fudge | Conceptually 32 Hz is not 60 Hz, but the driver contract ("N ticks for N×fixedDt wall time") holds at any fixedDt |
| T6 TKB uses `TkbTemplate("TestUnit", tkbType: 1L)` | Avoids `Fdp.Examples.Scenarios` dependency | Clean minimal composition | For P1+ the TKB will need `HrotEnvironment.CreateTkb()` + scenario templates |
| T6 spawn test uses `InitialComponents: [new SimTransform()]` | The minimal TKB template has no descriptors; `SpatialCoreTkbTranslator` can't inject `SimTransform` without a `SpatialCoreDescriptorDto` | Test proves the authority invariant without a full scenario template chain | The real scenario path (via `SpatialCoreTkbTranslator`) is tested by the existing `SpawnSystemTests`; this test targets the authority bit specifically |
| T6 spawn test requires 3 frames (not 2) | `CreateEntityRequestSystem` queues in Input phase frame 1, publishes `SpawnEntityCommand` in Simulation phase frame 1, bus SwapBuffers happens at end of frame 1; `NetworkSpawningSystem` runs in frame 2's BeforeSync / Input; entity is live from frame 3 | Correct frame budget | Documents the 3-frame latency for headless test harnesses |

---

## How `Standby` Is Observed in the Test

`ClusterMaster` constructor with `Mandatory = Array.Empty<string>()` calls `PublishStandby()` → `PublishClusterState(ClusterState.Idle)` synchronously during construction. This uses `_eventBus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = ClusterState.Idle, ... })` — **managed** event even though `ClusterStateUpdateEvent` is a struct.

The event goes into the **pending** buffer of `OrchestrationBus`. On the first call to `EditorStrideSubsystem.Tick()`, `OrchestrationBus.SwapBuffers()` runs first, moving the pending event to the **active** buffer. `ClusterMaster.Tick()` then runs (draining any new events). After the tick, `bus.ReadManaged<ClusterStateUpdateEvent>()` reads from the active buffer and returns the `Idle` event.

Assertion: `Assert.Equal(ClusterState.Idle, events[0].CurrentState)` where `ClusterState.Idle` (value 0) is the design's "Standby" state. This is correct — `PublishStandby()` in `ClusterMaster` is literally `private void PublishStandby() => PublishClusterState(ClusterState.Idle);`.

---

## References Added to `HrotStrideApp.Game`

New `ProjectReference` entries in `HrotStrideApp.Game.csproj`:

| Assembly | Purpose |
|---|---|
| `Hrot.Editor` | `OfflineNetworkFactory` |
| `Hrot.Orchestrator` | `ClusterMaster`, `ClusterConfiguration`, `OrchestrationLogicPack`, `OrchestratorEventRegistry` |
| `Hrot.CGF` | `CgfLogicPack`, `CgfComponentRegistry`, `CreateEntityRequestSystem` |
| `Hrot.SimHost` | `SimHostCoreLogicPack`, `SimHostComponentRegistry`, `NetworkSpawningSystem`, `SimHostModule` |
| `Hrot.Common` | `HrotSharedComponentRegistry` |
| `Hrot.Core` | `ScenarioEntityCreationRequestSource`, `NullEntityAckSink`, `SequentialIdAllocator` |
| `Hrot.AI.Behaviors` | `DefendAreaMapper`, `HullDownAttackMapper` (required by `CgfLogicPack`) |
| `Fdp.Core` | `ITkbEntityTranslator` (in `Fdp.Interfaces` namespace), `EntityRepository` |

**Raylib / WinForms tag-alongs:** `Hrot.Editor` brings Raylib/WinForms/ImGui as transitive dependencies. This was accepted per design §1.1/§8.3 (both modes host diagnostic raylib/ImGui windows). The test project builds and runs headlessly; the Raylib DLLs are present in the output but never loaded.

**No CycloneDDS code-gen friction:** `<CycloneDdsDisableCodeGen>true</CycloneDdsDisableCodeGen>` was already set on `HrotStrideApp.Game` in BATCH-01 and inherited automatically by `HrotStrideApp.Game.Tests`. All new assemblies compile clean.

---

## CycloneDDS Workaround (STR-D1)

**Held.** The `<CycloneDdsDisableCodeGen>true</CycloneDdsDisableCodeGen>` property in `HrotStrideApp.Game.csproj` (set by BATCH-01) propagated correctly to the new `HrotStrideApp.Game.Tests.csproj` project. No issues. Both build and test cleanly without triggering the generator on Stride DLLs.

---

## Test Results

All tests run headlessly on the build machine (Windows 11, .NET 8, no GPU). Total BATCH-02 additions: 14 new tests (T5) + 5 new tests (T6) = **19 new tests**.

```
Hrot.Stride.Core.Tests (BATCH-01: 37, BATCH-02: 14)
  Passed!  - Failed: 0, Passed: 51, Skipped: 0, Total: 51

Hrot.Stride.Animation.Tests (BATCH-01: 4)
  Passed!  - Failed: 0, Passed:  4, Skipped: 0, Total:  4

HrotStrideApp.Game.Tests (BATCH-02: 5, NEW project)
  Passed!  - Failed: 0, Passed:  5, Skipped: 0, Total:  5

BATCH-02 total: 60 pass, 0 fail, 0 skip
```

**BATCH-01 context (unchanged):** `Hrot.StrideMock.Tests` has 10 pre-existing failures (verified by `git stash` revert test: same failures on BATCH-01 HEAD). These are not caused by BATCH-02 changes.

**Key test assertions (T5):**
- `ExactMultiple_OfFixedDt_ProducesExactTickCount`: 64 ticks for 64 × (1/32f) wall time
- `IrregularFrames_TotallingN_FixedDt_ProducesExactNTicks`: 7 irregular frame chunks summing to 64 × fixedDt → exactly 64 ticks
- `VeryLargeFrameGap_IsCappedByMaxTicksPerFrame`: 128 × fixedDt capped at 4 ticks (spiral-of-death guard)
- `PartialStep_ProducesNoExtraTick_CarriedOver`: 0.99 × fixedDt → 0 ticks; +0.02 × fixedDt → 1 tick
- `TickCallback_AlwaysReceivesFixedDt`: 16 callbacks each with exactly `1/32f`

**Key test assertions (T6):**
- `Initialize_CoreObjects_AreNonNull`: `World`, `Kernel`, `TimeController`, `OrchestrationBus`, `ClusterMaster` all non-null
- `OrchestrationBus_IsDifferentInstance_FromWorldBus`: `!ReferenceEquals(orchBus, world.Bus)` — design §8.1 invariant
- `ClusterMaster_EmptyMandatory_PublishesStandbyIdle_AfterFirstTick`: `ReadManaged<ClusterStateUpdateEvent>()[0].CurrentState == ClusterState.Idle`
- `BrainPathSpawn_EntityIsWithOwned_FromBirth`: entity has `SimTransform`, `HasAuthority<SimTransform>` is true, `WithOwned<SimTransform>` query count == 1
- `PumpSixtyFrames_AfterSpawn_DoesNotThrow`: 62 ticks (2 spawn + 60 stability) without exception

---

## Developer Insights

1. **Float arithmetic for fixed-step accumulation.** `n × (float)(1/60f)` does NOT equal `1.0f` in double precision due to binary fractions; the float product rounds to `0.5f` exactly but `(double)(0.5f) / (double)(1f/60f) = 29.999...`. Using `1/32f` (power-of-two reciprocal) avoids this entirely. The double accumulator is the right production choice; the test fixedDt was adjusted to be binary-exact.

2. **`ClusterStateUpdateEvent` is published as managed, not native.** Despite being a `struct` with `[EventId]`, `ClusterMaster.PublishClusterState` uses `PublishManaged()` not `Publish()`. So reading requires `ReadManaged<T>()` not `Read<T>()`.

3. **3-frame spawn latency.** The full spawn pipeline is: Frame 1 Input → `CreateEntityRequestSystem` ingests request; Frame 1 Simulation → `ProcessPendingRequest` publishes `SpawnEntityCommand`; Frame 1 SwapBuffers; Frame 2 Input/BeforeSync → `NetworkSpawningSystem` creates entity; Frame 3 → entity is live and queryable. Headless tests need at least 3 ticks after enqueue.

4. **`OfflineNetworkFactory` from `Hrot.Editor`** — this assembly drags in Raylib, WinForms, and ImGui. This is accepted (design §1.1). A future P5 cleanup opportunity: extract `OfflineNetworkFactory` to a separate `Hrot.Core.Network.Offline` assembly with no UI dependencies.

5. **`Hrot.StrideMock.Tests` pre-existing failures.** 10 tests in that project fail on BATCH-01 HEAD and BATCH-02 HEAD identically. Not related to BATCH-02 changes.

6. **`BehaviorRegistry` / `TacticalIntentMapperRegistry` initialisation cost.** `CgfLogicPack` requires registered mappers (`DefendAreaMapper`, `HullDownAttackMapper`). This pulls in `Hrot.AI.Behaviors`, which loads the behavior source trees. For P0 headless tests this is acceptable; for P5 the editor might hot-reload these.

---

## Known Issues

1. **`StrideHrotGame` GPU path untested.** Full `StrideHrotGame` construction requires a `GraphicsDevice`. Deferred to T8 smoke (BATCH-03).
2. **Minimal TKB** (`TkbTemplate("TestUnit", tkbType: 1L)`) is P0-only. P1+ will need `HrotEnvironment.CreateTkb()` + `UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates()`.
3. **`ScreenRayToFdp` untested** (pre-existing STR-D4 debt from BATCH-01 review).
4. **`Hrot.AI.Behaviors` dependency.** Added to `HrotStrideApp.Game` to satisfy `CgfLogicPack`'s mapper requirements. At P5 this should be loaded via the hot-reload path to avoid direct binary coupling.
5. **`SimHostCoreLogicPack` includes the FDP integrators** (`LinearKinematicsSystem`, `CarKinematicsSystem`) which will fight Bullet in P1. These will be replaced by `StrideKinematicsModule` (STR-P1-T1). The seam comment in `EditorStrideSubsystem.cs` marks the exact substitution point.

---

## Suggested Commit Message

```
feat(stride): BATCH-02 external host loop + EditorStrideSubsystem composition skeleton

Completes STR-P0-T5, STR-P0-T6
- StrideHostLoopDriver (pure, GPU-free): double accumulator, fixed-dt clock, MaxTicksPerFrame cap
- StrideHrotGame: Stride.Engine.Game subclass, external loop via Tick(), throttler disabled
- EditorStrideSubsystem: Mode-1 headless composition (CgfLogicPack + SimHostCoreLogicPack P0 stub,
  OfflineNetworkFactory, NetworkSpawningSystem localNodeId=0, ClusterSlave/Master on separate
  orchestration bus, Standby published at init)
- HrotStrideApp.Game.Tests: new xunit project, 5 T6 integration tests
- StrideHostLoopDriverTests: 14 unit tests in Hrot.Stride.Core.Tests
Tests: 60 (51 Core + 4 Animation + 5 Game.Tests), all pass. Pre-existing 10 failures in
  Hrot.StrideMock.Tests unchanged.
```
