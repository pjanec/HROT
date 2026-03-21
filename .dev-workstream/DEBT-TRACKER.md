# Technical Debt Tracker

This document tracks P2 and P3 technical debt, refactoring opportunities, and deferred minor issues discovered during development and reviews. P1 critical issues are fixed immediately in the next batch.

| Status | Priority | Category | Source Batch | Description | Target Fix |
|---|---|---|---|---|---|
| ✅ | P3 | Testing | WCR-BATCH-02 | `EntityRepository` starts with non-zero, unpredictable `GlobalVersion` when setup requires component registration/creation. Add a way to reset version or specify `BaseVersion` to make tests robust without reading the actual starting tick. | WCR-BATCH-03 |
| ✅ | P2 | Safety | WCR-BATCH-02 | `SteppedMasterController._slaveNodeIds` is a mutable `HashSet` passed by ref. The caller can mutate this set after construction. Suggest defensive copy or switching interface to `IReadOnlySet`. | WCR-BATCH-03 |
| ✅ | P3 | Precision | WCR-BATCH-02 | `MasterTimeController` derives wall ticks implicitly using `elapsedSeconds * Stopwatch.Frequency`. Causes float-multiplication drift over long durations. Should read `_wallClock.ElapsedTicks` directly. | WCR-BATCH-03 |
| ✅ | P2 | Architecture | WCR-BATCH-01 | `RecordingReader` duplicates outer frame header parsing logic from `BuildFrameIndex`/`ApplyFrame`. Introduce a shared `FrameOuterHeader` unmanaged struct to eliminate layout drift when the binary format changes. | Completed by Lead |
| ✅ | P3 | Architecture | WCR-BATCH-01 | Uncompressed payload header parsing is duplicated across `ProcessBuffer`, `PlaybackController.ApplyFrame`, and `PlaybackSystem.ApplyFrame` with hardcoded offsets (e.g. 9). Should be extracted into constants like `PayloadLayout.WallClockTicksOffset`. | Completed by Lead |
| ✅ | P3 | Testing | WCR-BATCH-01 | `FlightRecorderIntegrationTests` manually parses the binary format with byte arithmetic. Fragile to format changes. Tests should use `RecordingReader` and `PlaybackController` APIs to assert semantically. | Completed by Lead |
| ✅ | P3 | Performance | WCR-BATCH-01 | `PlaybackController.BuildFrameIndex` allocates `new FrameMetadata` for every frame and inserts into a `List<FrameMetadata>` without upfront capacity, causing repeated list growth. Pre-allocate capacity based on file size estimate. | WCR-BATCH-03 |
|   | P2 | Architecture | BATCH-01     | `ModuleHostKernel.Update(float deltaTime)` can desync `_timeController` state. It's a legacy overload that should be marked `[Obsolete]` or removed entirely.         | ✅ Completed in BATCH-03 |
|   | P3 | Architecture | BATCH-02     | `NLog.MappedDiagnosticsContext` is obsolete in NLog 5.x. Migrate to `ScopeContext.PushProperty` with `${scopeproperty:scenario}`.        | ✅ Completed in BATCH-03 |
|   | P2 | Architecture | BATCH-03     | `CarKinematicsSystem.KinematicsMode.None` unconditionally sets `HasArrived=1`, even when targeting speed 0 initially. Should require `TargetSpeed > 0 && dist <= radius`. | ✅ Completed in BATCH-04 |
|   | P3 | Physics      | BATCH-03     | RVO lateral avoidance force is fixed-magnitude and scales poorly at high simulation speeds. Replace with a velocity-relative lateral bias. | BATCH-05     |
|   | P3 | Performance  | BATCH-03     | `SpeedController.CalculateAcceleration` calculates every tick instead of early exiting when `abs(speedError) < 0.001f`. | ✅ Completed in BATCH-04 |
|   | P3 | Architecture | BATCH-04     | FastBTree's `Selector` optimisation permanently blocks sequence re-evaluation. Requires documentation or a new `ReactiveSelector`. | BATCH-05     |
|   | P2 | Architecture | BATCH-04     | Memory leak in test environments: `PhysicsToolkitModule.Initialize()` allocates `NativeArray` but `EntityRepository` does not free it on dispose. | ✅ Completed in BATCH-05 |
|   | P3 | Performance  | BATCH-04     | `SpatialHashSystem` adds ALL entities (including non-collidable shooters) to the grid. Implement `PhysicsCollider` filter check. | ✅ Completed in BATCH-05 |
|   | P3 | Performance  | BATCH-05     | `LocalGridBuilderSystem` rebuilds `SpatialHashGrid` completely from scratch. Implement dirty-flag incremental updates for 100+ entity scales. | BATCH-06     |
|   | P2 | Architecture | BATCH-05     | Using `FlushEcbAndSwap` forces a global bus flush which could prematurely advance non-perception events in production pipelines. Design dedicated event bus or isolated reentrant snapshot logic for `AutonomousPerceptionModule`. | BATCH-06     |
| ✅ | P3 | Memory       | BD1-BATCH-01 | `BTreeTickSystem._publishedTerminalForInstanceId` dictionary is never pruned when entities are destroyed, leading to a memory leak in long-running simulations. | BD1-BATCH-02 |
| ✅ | P3 | Architecture | BD1-BATCH-01 | `MissionDirectorSystem` still directly mutates `DoctrineState` for triggers other than `DoctrineFinished`. This dual-write pattern breaks single ownership and should be delegated to `DoctrineIngressSystem` like the clear event. | BD1-BATCH-02 |
|   | P3 | Architecture | BD1-BATCH-02 | `MissionDirectorSystem` publishing `AssignDoctrineHashEvent` introduces a one-frame delay for doctrine activation. `MissionAdapterSystem` acts as a redundant write. Document or unify this flow. | BD1-BATCH-04 |
|   | P3 | Performance  | BD1-BATCH-03 | `ComponentReflector` byte cache diffing uses `Marshal.AllocHGlobal` every frame. Optimise to use a pooled `NativeArray<byte>` or `stackalloc` for small structs to eliminate native heap churn. | BD1-BATCH-04 |
| ✅ | P2 | Testing      | BD1-BATCH-03 | `EntityMission_MovesEntity` integration test is failing due to missing mission pipeline wiring in the `SimHostInstance` test harness. Pre-existing issue masked by CQRS split. | BUG2-BATCH-02 |
|   | P3 | Testing      | BD1-BATCH-03 | `FDP.Toolkit.ImGui.Tests` crashes when run in parallel with other assemblies due to native ImGui library loading conflict. Requires test isolation config. | BD1-BATCH-04 |
| ✅ | P3 | Architecture | BUG1-BATCH-01 | `UpdateEntityDescriptorRequestSystem` creates DDS objects internally making unit testing hard. Inject the ack writer via constructor. | BUG1-BATCH-02 |
| ✅ | P3 | Architecture | BUG1-BATCH-01 | `translators` list in `SimHostApp.OnLoad()` includes `MissionIngressTranslator` which doesn't need to be disposed. Separate egress. | BUG1-BATCH-02 |
| ✅ | P2 | Architecture | BUG1-BATCH-01 | IOS subsystem app does not receive the node-id pass-through from orchestrator like IG and SimHost do. | BUG1-BATCH-02 |
| ✅ | P2 | Testing      | BUG1-BATCH-01 | Fix pre-existing IG test failures in `Bagira.IG.Tests.EditToolTests` and `TraceLoggingTests`. | BUG1-BATCH-02 |
|   | P1 | Architecture | BUG1-BATCH-02 | `DoctrineFinished` string trigger from IOS is not implemented in `MissionControlRequestSystem` parser, falling back to `TimerElapsed` 0s causing vehicles not to move. | BUG1-BATCH-03 |
|   | P3 | Testing      | BUG1-BATCH-02 | `SimHostApp.OnLoad()` translator separation lacks test coverage. | BUG1-BATCH-03 |
|   | P3 | Architecture | BUG1-BATCH-02 | `IosSubsystem` node-id isn't fully plumbed into `IosMock.InitializeEmbedded` yet. | BUG1-BATCH-03 |
|   | P3 | Testing      | BUG1-BATCH-02 | No integration test covering the full `HandleAbort → SendControlCommandAsync → OnAckReceived → CommitInFlight = false` round-trip. | BUG1-BATCH-03 |
| ✅ | P3 | Architecture | BUG2-BATCH-01 | The duplicate-copy of `ResolveTrigger` logic between `MissionControlRequestSystem` and `EntityMissionIngressTranslator` is tech debt. Target: Consolidate into a shared static helper in `Bagira.Map.Common`. | BUG2-BATCH-02 |
| ✅ | P2 | Architecture | BUG2-BATCH-02 | `SimHostInstance.Tick()` multi-swap architecture diverges from production `SimHostApp.OnUpdate()` causing silent event losses. Align or document. | DEBT-BURNDOWN-01 |
| ✅ | P3 | Testing      | BUG2-BATCH-02 | Pre-existing `FDP.Toolkit.Replay.Tests` failures (2 async timing tests) indicate race conditions in the recording module teardown path. | DEBT-BURNDOWN-01 |
| ✅ | P1 | Testing      | BUG2-BATCH-02 | `Fdp.Examples.UrbanCombat.Tests` access violation crash in native code requires interop debugging. | DEBT-BURNDOWN-01 |
| ✅ | P3 | Architecture | BUG2-BATCH-02 | `SimHostInstance` duplicate `RegisterComponent<MissionAdapterState>()` is harmless but should be cleaned up. | DEBT-BURNDOWN-01 |
| ✅ | P2 | Architecture | ROUTES1-BATCH-01 | `RoutePlan.Version` has no enforced write path, putting burden on callers to remember to increment it. Add an explicit Mutate/SetWaypoints method. | ROUTES1-BATCH-02 |
| ✅ | P3 | Performance | ROUTES1-BATCH-01 | `MapRouteEgressTranslator` tracks per-entity versions in a Dictionary causing GC pressure and O(n) lookups. Refactor to a secondary `RouteEgressMeta` component. | ROUTES1-BATCH-03 |
| ✅ | P2 | Performance | ROUTES1-BATCH-01 | `MapRouteIngressTranslator` linearly re-scans `_pendingRoutes` every tick. Should use `NetworkEntityMap` registration callback. | ROUTES1-BATCH-03 |
| ✅ | P2 | Performance | ROUTES1-BATCH-01 | `BuildRoutePlan` allocates a new `List<RouteWaypoint>` per sample. Switch to a pooled list or blittable array for high-frequency updates. | ROUTES1-BATCH-03 |
| ✅ | P3 | Performance | ROUTES1-BATCH-02 | `World.Query().With<SelectionState>().Build()` inside `OnCanvasWorldClick` constructs a new query object every time. Should cache the query to avoid allocations. | ROUTES1-BATCH-03 |
| ✅ | P2 | Architecture | ROUTES1-BATCH-02 | `RouteTrajectorySyncSystem` does not yet notify the kinematic layer to re-plan when a personal route's trajectory ID changes. Needs integration with `NavState.TrajectoryId`. | ROUTES1-BATCH-03 |
| ✅ | P2 | Safety      | ROUTES1-BATCH-02 | `ActivateRouteAuthoringTool` accesses `_geoTransform` which may be `null` in edge cases where no geographic origin is configured. Requires a guard or fallback error message. | ROUTES1-BATCH-03 |
| ✅ | P2 | Safety      | ROUTES1-BATCH-03 | `IgApplication`'s `RouteEditTool` commit handler does not check `World.IsAlive(routeEntity)`, crashing if the route is destroyed mid-edit. | ROUTES1-BATCH-04 |
| ✅ | P3 | UX          | ROUTES1-BATCH-03 | `WaypointEditorPanel` can capture stale UI float inputs if a user commits the route during an active ImGui widget edit. | ROUTES1-BATCH-04 |
| ✅ | P3 | Safety      | ROUTES1-BATCH-03 | `RouteRenderLayer.Draw` invokes `plan.Waypoints.Count` without guarding `plan.Waypoints` against null. Use `?.Count ?? 0`. | ROUTES1-BATCH-04 |
| ✅ | P3 | Performance | ROUTES1-BATCH-03 | `RouteContextSystem` rebuilds `vehicleQuery` and `routeQuery` every tick. Cache these in `OnCreated()`. | ROUTES1-BATCH-04 |
| ✅ | P3 | Performance | ROUTES1-BATCH-03 | `SimHostTrajectoryLayer` rebuilds `routeQuery` inside `Draw()` every frame. Store as cached member variable. | ROUTES1-BATCH-04 |
| ✅ | P3 | Performance | ROUTES1-BATCH-03 | `WaypointEditorPanel` maps `wp.ExtensionJson` to string on every frame. Track `_lastWpIndex` to diff buffer updates. | ROUTES1-BATCH-04 |
