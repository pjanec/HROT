# BATCH-02 REPORT — EQS Result Pipeline + Lifecycle Nodes

**Batch:** BATCH-02  
**Status:** COMPLETE — all tasks implemented, solution builds clean, all 7 new EQS tests pass.

---

## Tasks Completed

### Corrective-0 — P1 Type Fix (`EqsResultPool.cs`)

**Problem:** `EqsResultEvent.Epoch` and `RefreshTick` were typed as `int`, causing a type mismatch with `EqsSensor.Epoch` (`uint`) and `EqsResultTopic` (`uint`).

**Fix:** Changed both fields to `uint` in `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsResultPool.cs`.

**Verification:** All 7 pre-existing EQS pool tests (`EqsResultPoolTests` + `EqsComponentLayoutTests`) pass.

---

### TASK-EQS-004 — `EqsResultUpdateSystem` + Managed Event

**New files:**
- `Hrot/Subsystems/Hrot.SimHost/Systems/EqsResultUpdateEvent.cs` — managed event class carrying `Entity Observer`, `uint Epoch`, `uint RefreshTick`, `List<EqsResultEntry> Results`.
- `Hrot/Subsystems/Hrot.SimHost/Systems/EqsResultUpdateSystem.cs` — Brain-tier simulation system consuming both paths (Path A: managed DDS-bridged event, Path B: unmanaged offline solver event). Handles staleness guard (`evt.Epoch != sensor.Epoch`), ensures `LastUpdateTick > 0` for `IsReady`, writes via `GetSpanRW()` to avoid the `[InlineArray]` defensive-copy trap.

**Modified files:**
- `Hrot/Subsystems/Hrot.SimHost/CognitiveComponentRegistry.cs` — added `RegisterComponent<EqsSensor>()`, `RegisterComponent<EqsCognitiveBuffer>()`, `RegisterManagedEvent<EqsResultUpdateEvent>()`.
- `Hrot/Subsystems/Hrot.SimHost/SimHostCoreLogicPack.cs` — added `simList.Add(new EqsResultUpdateSystem())` to `SimulationSystems`.

**Tests (3 tests in `Eqs/EqsResultUpdateSystemTests.cs`):**
- T1: `EqsResultUpdateSystem_StaleEpoch_IgnoresEvent` — stale epoch event must NOT create buffer.
- T2: `EqsResultUpdateSystem_MatchingEpoch_PopulatesBuffer` — matching epoch writes count + values.
- T3: `EqsResultUpdateSystem_GetSpanRW_WritesPersist` — verifies `[InlineArray]` write survives return.

---

### TASK-EQS-005 — `EqsSolverSystem` (Phase 1 Stub) + `EqsModule` Wiring

**New file:**
- `Hrot/Subsystems/Hrot.SimHost/Systems/EqsSolverSystem.cs` — Phase 1 stub solver. Queries all `EqsSensor + NetworkIdentity` entities and emits one `EqsResultEvent` per entity with `EntryCount = 0` and `RefreshTick = view.Tick + 1` (ensures `IsReady = true` even at tick 0).

**Modified files:**
- `Hrot/Subsystems/Hrot.SimHost/Modules/EqsModule.cs` — replaced `AreaQuerySolverSystem` driver with `EqsSolverSystem`. Module still runs at `SlowBackground(10)`.
- `Hrot/Subsystems/Hrot.SimHost/NavigationSolverComponentRegistry.cs` — added `world.SetSingleton(new EqsResultPool { Results = new NativeArray<EqsResult>(EqsResultPool.PoolCapacity, ...) })` and `world.RegisterEvent<EqsResultEvent>()`.
- `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/EditorHarness.cs` — added `Kernel.RegisterModule(new Hrot.SimHost.Modules.EqsModule())` before `Kernel.Initialize()`.

**Test (1 test in `Eqs/EqsSolverSystemTests.cs`):**
- T4: `EqsSolverSystem_Phase1Stub_PopulatesBufferAfterSolverFires` — creates entity with `EqsSensor + NetworkIdentity`, pumps until `EqsCognitiveBuffer.IsReady`, asserts `Count == 0`.

---

### TASK-EQS-006 — `EqsLifecycleNodes` BTree Nodes

**New file:**
- `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/EqsLifecycleNodes.cs` — contains:
  - `EqsParams` struct (`BlueprintId`, `SearchRadius`, `ThreatThreshold`, `FactionFilter`).
  - `Action_MaintainEqsSensor` — adds `EqsSensor` on first tick; increments `Epoch` only when params change; always returns `Running`.
  - `Deactivate_MaintainEqsSensor` — `[BTreeDeactivator("...@0")]` removes both `EqsSensor` and `EqsCognitiveBuffer` on branch abort.
  - `Action_WaitForSensor` — returns `Running` until `EqsCognitiveBuffer.IsReady`; then `Success`.

**Tests (3 tests in `Eqs/EqsLifecycleNodesTests.cs`):**
- T5: `EqsLifecycleNodes_WaitForSensor_ReturnsSuccessWhenReady` — no buffer → Running; buffer with `LastUpdateTick=0` → Running; `LastUpdateTick=1` → Success.
- T6: `EqsLifecycleNodes_Deactivator_RemovesComponentsOnAbort` — deactivator removes `EqsSensor` and `EqsCognitiveBuffer`.
- T7: `EqsLifecycleNodes_MaintainSensor_EpochIncrementsOnlyOnParamChange` — epoch stays at 1 on identical params, increments to 2 on `SearchRadius` change, stays at 2 afterward.

---

## Ancillary Changes

**`DisposeEqsSingletons` helpers updated** in 5 pre-existing test files to dispose the new `EqsResultPool` singleton (prevents `NativeArray` leak):
- `HillAttackIntegrationTests.cs`
- `HillAttackNodeTests.cs`
- `EqsModuleTests.cs`
- `AreaQueryTranslatorTests.cs`
- `AreaQueryBatchDataTests.cs`

---

## Build & Test Summary

| Suite | Command | Result |
|---|---|---|
| FDP EQS pool tests | `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/ --filter "FullyQualifiedName~EqsResultPool|FullyQualifiedName~EqsComponentLayout"` | 7/7 passed |
| New EQS integration tests | `dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/ --filter "FullyQualifiedName~Eqs"` | 7/7 passed |
| `Hrot.SimHost` project | `dotnet build Hrot/Subsystems/Hrot.SimHost/Hrot.SimHost.csproj -v quiet` | Build succeeded, 0 errors |
| `Hrot.AI.Behaviors` project | `dotnet build Hrot/Subsystems/Hrot.AI.Behaviors/Hrot.AI.Behaviors.csproj -v quiet` | Build succeeded, 0 errors |

Pre-existing test failures in `Hrot.SimHost.Tests` (32 failed, e.g. `SC_HA011_4`, `HullDownAttackParams_Is40Bytes`) were verified to exist on the unmodified `main` branch and are not introduced by this batch.

---

## Key Design Decisions

1. **`Span<T>` as `Action<>` type parameter is not allowed** (CS9244). Inlined the buffer-write logic in both Path A and Path B branches instead of using a shared lambda helper. This is idiomatic for hot-path ECS code.

2. **`EqsResultUpdateEvent` registered in `CognitiveComponentRegistry`** (not `NavigationSolverComponentRegistry`) — it is a Brain-tier managed event, not a Muscle-tier solver concern.

3. **`EqsSolverSystem` uses `view.Tick + 1` for `RefreshTick`** — ensures `IsReady = (LastUpdateTick > 0)` evaluates to `true` even when the first solver run fires at simulation tick 0.

4. **`EqsModule` now references `EqsSolverSystem` directly** (instead of `Systems.EqsSolverSystem` via qualified name) using a `using Hrot.SimHost.Systems;` directive, matching the style of other modules.
