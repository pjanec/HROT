# Task Details — Integration Troubleshooting & Architecture Hardening

**Version:** 1.0  
**Date:** 2026-02-27

**Design Reference:** [DESIGN-Integration-Troubleshooting.md](./DESIGN-Integration-Troubleshooting.md)

---

## Table of Contents

### Phase 1 — Integration Bug Fixes
- [INTS-P1-001 — Register TKB Catalog in SimHost and IG](#ints-p1-001--register-tkb-catalog-in-simhost-and-ig)
- [INTS-P1-002 — Fix SimHost Vehicle Spawning to Use SpawnEntityCommand](#ints-p1-002--fix-simhost-vehicle-spawning-to-use-spawnentitycommand)
- [INTS-P1-003 — Replace NullDdsWriter with DdsWriterAdapter in IOS](#ints-p1-003--replace-nullddswriter-with-ddswriteradapter-in-ios)
- [INTS-P1-004 — Add PassthruCentralNode to ImGui DockSpace](#ints-p1-004--add-passthrucentralnode-to-imgui-dockspace)
- [INTS-P1-005 — Wire IG-to-IOS Map Event Translators](#ints-p1-005--wire-ig-to-ios-map-event-translators)

### Phase 2 — Architecture Consolidation
- [INTS-P2-006 — Implement HrotEnvironment Bootstrapper](#ints-p2-006--implement-hrotenvironment-bootstrapper)
- [INTS-P2-007 — Fix SubsystemOrchestrator Headless Logic](#ints-p2-007--fix-subsystemorchestrator-headless-logic)
- [INTS-P2-008 — Refactor IgApplication to Use HrotEnvironment](#ints-p2-008--refactor-igapplication-to-use-hrotenvironment)
- [INTS-P2-009 — Refactor SimHostApp to Use HrotEnvironment](#ints-p2-009--refactor-simhostapp-to-use-hrotenvironment)
- [INTS-P2-010 — Refactor IosSubsystem to Use HrotEnvironment](#ints-p2-010--refactor-iossubsystem-to-use-hrotenvironment)

### Phase 3 — Debug Instrumentation & End-to-End Validation
- [INTS-P3-011 — Trace Logging: SimHost Entity Spawn (Flow 1)](#ints-p3-011--trace-logging-simhost-entity-spawn-flow-1)
- [INTS-P3-012 — Trace Logging: IG Entity Ingress & Render (Flow 2)](#ints-p3-012--trace-logging-ig-entity-ingress--render-flow-2)
- [INTS-P3-013 — Trace Logging: IG Map Drawings & IOS Interactions (Flows 3–6)](#ints-p3-013--trace-logging-ig-map-drawings--ios-interactions-flows-36)
- [INTS-P3-014 — Integration Test: End-to-End Entity Lifecycle](#ints-p3-014--integration-test-end-to-end-entity-lifecycle)

---

## INTS-P1-001 — Register TKB Catalog in SimHost and IG

**Design Reference:** [Root Cause RC-1](./DESIGN-Integration-Troubleshooting.md#rc-1-missing-tkb-blueprint-registrations)

### Scope
**Included:**
- Modify `Hrot.SimHost` — add `BdcTkbCatalog.RegisterAll(tkbDb)` call in `SimHostApp.OnLoad` (or equivalent initialisation path) immediately after `TkbDatabase` is instantiated.
- Modify `Hrot.IG` — add the same call in `IgApplication.InitializeNetwork` after `TkbDatabase` is instantiated.

**Excluded:**
- Do not yet refactor construction to use `HrotEnvironment` (that is INTS-P2-008/009).
- Do not change the TKB catalog content.

### Constraints
- The registration call must occur **before** the first `WorldUpdate()` / `SystemPhase.Simulation` tick, otherwise the first `SpawnEntityCommand` frame runs with an empty database.
- `Hrot.Map.Definitions` must be referenced by both projects; verify their `.csproj` files already contain this reference.

### Success Conditions

**Test 1 — SimHost TKB resolves on first spawn attempt**
- Setup: Instantiate `SimHostApp` in test isolation with a real `TkbDatabase`.
- Action: Publish a `SpawnEntityCommand` with `TkbType = Hrot.Map.Common.TkbEntityTypes.Truck_HMMWV`.
- Assert: `NetworkSpawningSystem` does **not** log `"[NS] Unknown TkbType"`. The entity is created and has `EntityMaster` set.

**Test 2 — IG TKB resolves on first ghost spawn attempt**
- Setup: Instantiate `IgApplication` with in-process DDS (test domain 10).
- Action: Inject an `EntityMaster` sample with `TkbType = TkbEntityTypes.Tank_M1Abrams` via a DDS writer.
- Assert: IG's `EntityMasterTranslator` spawns a ghost entity. No `"[NS] Unknown TkbType"` log line emitted.

---

## INTS-P1-002 — Fix SimHost Vehicle Spawning to Use SpawnEntityCommand

**Design Reference:** [Root Cause RC-2](./DESIGN-Integration-Troubleshooting.md#rc-2-simhost-spawns-local-only-invisible-cars)

### Scope
**Included:**
- Refactor `Hrot.SimHost/UI/SimHostScenarioManager.cs` → `SpawnVehicle()`.
- Replace the direct `_repo.CreateEntity()` + manual component attachment with a `SpawnEntityCommand` published to the ECS event bus.
- Map the existing `VehicleClass` enum to canonical `TkbEntityTypes` constants.

**Excluded:**
- Do not modify `NetworkSpawningSystem` internals.
- Do not change the visual simulation loop in SimHost's standalone window.

### Constraints
- `TkbType` mapping:

| `VehicleClass` | `TkbEntityTypes` constant |
|---|---|
| `Tank` | `TkbEntityTypes.Tank_M1Abrams` |
| `Pedestrian` | `TkbEntityTypes.Infantry_Rifleman` |
| `PersonalCar` (default) | `TkbEntityTypes.Truck_HMMWV` |

- `NetworkId` must be set to `0` (auto-allocate via `DdsIdAllocator`).
- `OwnerNodeId` must be `SimHostNetworkConstants.LocalNodeId` (or the configured local node id).
- `InitType` must be `ReliableInitType.AllPeers`.
- `InitialComponents` list must include a `SimTransform` constructed from the `position` and `heading` vectors.

### Success Conditions

**Test 1 — SpawnVehicle publishes SpawnEntityCommand**
- Setup: Create `SimHostScenarioManager` with a mock `IEventBus` recorder.
- Action: Call `SpawnVehicle(new Vector2(100, 200), Vector2.UnitX, VehicleClass.Tank)`.
- Assert: Exactly one `SpawnEntityCommand` is recorded on the bus with `TkbType == TkbEntityTypes.Tank_M1Abrams`, `NetworkId == 0`, and an `InitialComponents` entry of type `SimTransform` with `Position.X == 100`.

**Test 2 — Spawned entity receives NetworkIdentity**
- Setup: Run `SimHostApp.OnLoad()` with a real in-process ECS world; call `SpawnVehicle()`.
- Action: Advance one world tick past `EntityLifecycleModule`.
- Assert: The resulting entity has `NetworkIdentity`, `NetworkOwnership`, and `EntityMaster` components attached (verified via `_repo.Query<NetworkIdentity>()`).

**Test 3 — WorldPosEgressTranslator publishes the entity**
- Setup: Same as Test 2 but with a mock DDS writer capturing published samples.
- Action: Advance enough ticks for the ELM to promote the entity to Active.
- Assert: At least one `WorldPos` sample is written to the mock writer with the expected `Lat`/`Lon` coordinates derived from the spawn position.

---

## INTS-P1-003 — Replace NullDdsWriter with DdsWriterAdapter in IOS

**Design Reference:** [Root Cause RC-3](./DESIGN-Integration-Troubleshooting.md#rc-3-ios-uses-null-stub-writers)

### Scope
**Included:**
- Implement `DdsWriterAdapter<T> : IDdsWriter<T>` class.  
  Suggested location: `Hrot.Map.Common/Dds/DdsWriterAdapter.cs`.
- Replace `NullDdsWriter` usage in `Hrot.ExCon/Program.cs`.
- Replace `NullDdsWriter` usage in `Hrot.ClusterRunner/Services/IosSubsystem.cs`.

**Excluded:**
- `NullDdsWriter` itself is **not** deleted; it remains available for unit test isolation.
- Do not modify `IDdsWriter<T>` interface.
- Do not change the four topic names used by IOS writers.

### Constraints
- `DdsWriterAdapter<T>` constructor signature: `(DdsParticipant participant, string topicName)`.
- Wrapped `CycloneDDS.Runtime.DdsWriter<T>` must be disposed when the adapter is disposed.
- The adapter must propagate any write exceptions (do not swallow them silently).

### Success Conditions

**Test 1 — DdsWriterAdapter calls underlying writer**
- Setup: Create a `DdsWriterAdapter<MapInteractionConfig>` with a mock/in-process participant.
- Action: Call `adapter.Write(new MapInteractionConfig { ... })`.
- Assert: The mock writer records exactly one written sample with the expected field values.

**Test 2 — IOS Program.cs uses DdsWriterAdapter at runtime**
- Setup: Run `Hrot.ExCon` standalone with a test DDS participant (domain 10).
- Action: Activate "New unit…" and confirm.
- Assert: A `CreateEntityRequest` sample is emitted on DDS domain 10 (captured by a test subscriber). Previously this step produced zero DDS traffic.

**Test 3 — DdsWriterAdapter disposes cleanly**
- Setup: Construct and immediately dispose a `DdsWriterAdapter<CreateEntityRequest>`.
- Assert: No exception is thrown; the underlying `DdsWriter<T>` dispose path executes once.

---

## INTS-P1-004 — Add PassthruCentralNode to ImGui DockSpace

**Design Reference:** [Root Cause RC-4](./DESIGN-Integration-Troubleshooting.md#rc-4-imgui-dockspace-blocks-map-input)

### Scope
**Included:**
- Modify `Hrot.ExCon/IosMock.cs` → `DrawUI()`: change the `ImGui.DockSpaceOverViewport(0)` call.

**Excluded:**
- No other ImGui layout changes.

### Constraints
- The new call must be:
  ```csharp
  ImGui.DockSpaceOverViewport(0, ImGui.GetMainViewport(), ImGuiDockNodeFlags.PassthruCentralNode);
  ```
- This must not break panel docking; only the empty centre area should pass through.

### Success Conditions

**Test 1 — Map pans when IG + IOS panels are visible**
- Setup: Run Runner with `-m all`.
- Action: Left-click-drag the map in the empty centre area (not over any panel).
- Assert: The Raylib `MapCanvas` camera translates such that the map pans accordingly. Previously the map did not move.

**Test 2 — ImGui panels still capture mouse correctly**
- Setup: Same runner session.
- Action: Left-click a button inside the ORBAT panel.
- Assert: `ImGui.GetIO().WantCaptureMouse == true` during that click; the map does not pan simultaneously.

---

## INTS-P1-005 — Wire IG-to-IOS Map Event Translators

**Design Reference:** [Root Cause RC-5](./DESIGN-Integration-Troubleshooting.md#rc-5-ig-to-ios-map-event-bridge-is-missing)

### Scope
**Included:**
- Register an egress translator in `IgApplication` that publishes `MapClickEvent` to DDS after `StandardInteractionTool` / `CreationTool` fires the event.
- Register an egress translator in `IgApplication` that publishes `CreateEntityRequest` to DDS (forwarded to SimHost).
- Instantiate and inject `BdcCommandGateway` into `IgApplication` network initialisation.
- Update `MiniIosPanelState` to route spawn requests through the gateway (publish `CreateEntityRequest` to SimHost) rather than publishing a local `SpawnEntityCommand`.

**Excluded:**
- Do not change IOS-side listeners (those are already wired; the problem is IG not publishing).
- Do not add right-click context menu logic here (separate scope).

### Constraints
- The `BdcCommandGateway` must be constructed with the same `DdsParticipant` used by the rest of `IgApplication` (not a new participant).
- `MapClickEvent` must include `InteractionContextId` matching the context set by the IOS `MapInteractionConfig`.
- If `enableNetwork == false` (IG running standalone for UI-only development), the gateway and translator registrations must be skipped gracefully.

### Success Conditions

**Test 1 — Map left-click produces DDS MapClickEvent**
- Setup: Run `IgApplication` with network enabled; subscribe a test DDS reader to the `MapClickEvent` topic.
- Action: Simulate a left-click on the map canvas at world position `(500, 300)`.
- Assert: Test DDS reader receives exactly one `MapClickEvent` with `WorldX ≈ 500`, `WorldY ≈ 300` within 500 ms.

**Test 2 — MiniIOS Spawn publishes CreateEntityRequest via DDS**
- Setup: Same setup as Test 1 but subscribe a test reader to `CreateEntityRequest`.
- Action: Click "Spawn" in the Mini IOS panel.
- Assert: Test reader receives a `CreateEntityRequest` with valid non-zero `TkbType`. No local ghost entity is created directly in the IG ECS.

**Test 3 — Spawn without network enabled does not throw**
- Setup: Initialise `IgApplication` with `enableNetwork = false`.
- Action: Click "Spawn" in the Mini IOS panel.
- Assert: No exception is thrown; a log message indicates network is disabled.

---

## INTS-P2-006 — Implement HrotEnvironment Bootstrapper

**Design Reference:** [Decision 2 — HrotEnvironment](./DESIGN-Integration-Troubleshooting.md#decision-2-hrotenvironment-shared-bootstrapper)

### Scope
**Included:**
- Create `Hrot.Map.Common/HrotEnvironment.cs` with three static factory methods: `CreateTkb()`, `CreateGeoTransform()`, `CreateParticipant(int domainId)`.

**Excluded:**
- Callers are updated in INTS-P2-008/009/010; do not modify callers in this task.
- Do not add any state to `HrotEnvironment` (must remain purely static / stateless factories).

### Constraints
- `CreateTkb()` must call `BdcTkbCatalog.RegisterAll(tkb)` before returning.
- `CreateGeoTransform()` must set origin to `(52.52, 13.405, 0.0)` (Berlin) as the project default; if a production-configurable origin is needed later it is added via an overload, not by mutating this method's behaviour.
- `CreateParticipant(int domainId)` must cast `domainId` to `uint` before passing to `DdsParticipant`.

### Success Conditions

**Test 1 — CreateTkb returns a populated database**
- Setup/Action: `var tkb = HrotEnvironment.CreateTkb();`
- Assert: `tkb.TryGetTemplate(TkbEntityTypes.Tank_M1Abrams, out _) == true`.
- Assert: `tkb.TryGetTemplate(TkbEntityTypes.Infantry_Rifleman, out _) == true`.

**Test 2 — CreateGeoTransform returns Berlin origin**
- Setup/Action: `var t = HrotEnvironment.CreateGeoTransform();`
- Assert: Converting `(0, 0, 0)` → WGS84 yields latitude ≈ 52.52, longitude ≈ 13.405 (±0.001 degrees).

**Test 3 — CreateParticipant accepts valid domain IDs**
- Setup/Action: `var p = HrotEnvironment.CreateParticipant(10);`
- Assert: `p.DomainId == 10`. Object is non-null.

---

## INTS-P2-007 — Fix SubsystemOrchestrator Headless Logic

**Design Reference:** [Decision 1 — IG is the Sole Map Owner](./DESIGN-Integration-Troubleshooting.md#decision-1-ig-is-the-sole-map-owner-in--m-all-mode)

### Scope
**Included:**
- Modify `Hrot.ClusterRunner/Services/SubsystemOrchestrator.cs` → `Initialize()`.
- Add detection of IG subsystem presence.
- Force SimHost's `SubsystemConfig.Headless = true` when IG is present.

**Excluded:**
- Do not change window creation logic.
- Do not modify individual subsystem `Initialize(SubsystemConfig)` implementations.

### Constraints
- The subsystem name comparison (`subsystem.Name == "IG"` / `"SimHost"`) must be case-sensitive and match the names already assigned in `SubsystemRegistrar` or equivalent.
- If IOS is also present, it must continue to render its ImGui panels unaffected.

### Success Conditions

**Test 1 — SimHost forced headless when IG present**
- Setup: Construct `SubsystemOrchestrator` with three mock subsystems: `IG`, `SimHost`, `IOS`.
- Action: Call `Initialize()`.
- Assert: The `SubsystemConfig` passed to `SimHost.Initialize()` has `Headless == true`. The config passed to `IG.Initialize()` has `Headless == false`.

**Test 2 — SimHost NOT forced headless when IG absent**
- Setup: Construct with `SimHost` and `IOS` only.
- Action: Call `Initialize()`.
- Assert: The config passed to `SimHost.Initialize()` has `Headless == false` (inherits the global flag, default `false` for graphical mode).

---

## INTS-P2-008 — Refactor IgApplication to Use HrotEnvironment

**Design Reference:** [Phase 2 Task INTS-P2-008](./DESIGN-Integration-Troubleshooting.md#task-ints-p2-008--refactor-igapplication-to-use-hrotenvironment)

### Scope
**Included:**
- In `Hrot.IG/IgApplication.cs` → `InitializeNetwork()`: replace inline `new TkbDatabase()` + manual registration, `new WGS84Transform()` + origin set, and `new DdsParticipant(...)` with the corresponding `HrotEnvironment.*` calls.

**Excluded:**
- Do not change any system/module registration logic.
- Do not change network constants (`IgNetworkConstants.DdsDomain`).

### Constraints
- Behaviour must be identical; only the construction calls change.
- All existing `IgApplication` unit tests must continue to pass.

### Success Conditions

**Test 1 — Regression**
- Assert: All existing `Hrot.IG.Tests` pass without modification after this refactor.

**Test 2 — TKB is populated at first tick**
- Assert: `world.GetSingleton<ITkbDatabase>().TryGetTemplate(TkbEntityTypes.Tank_M1Abrams, out _) == true` after `IgApplication` initialisation.

---

## INTS-P2-009 — Refactor SimHostApp to Use HrotEnvironment

**Design Reference:** [Phase 2 Task INTS-P2-009](./DESIGN-Integration-Troubleshooting.md#task-ints-p2-009--refactor-simhostapp-to-use-hrotenvironment)

### Scope
**Included:**
- In `Hrot.SimHost`: replace inline construction of `TkbDatabase`, `WGS84Transform`, and `DdsParticipant` in `SimHostApp.OnLoad` with `HrotEnvironment.*` calls.

**Excluded:**
- Do not change the SimHost-specific network constants or module registrations.

### Constraints
- Same as INTS-P2-008.

### Success Conditions

**Test 1 — Regression**
- Assert: All existing `Hrot.SimHost.Tests` pass without modification.

---

## INTS-P2-010 — Refactor IosSubsystem to Use HrotEnvironment

**Design Reference:** [Phase 2 Task INTS-P2-010](./DESIGN-Integration-Troubleshooting.md#task-ints-p2-010--refactor-iossubsystem-to-use-hrotenvironment)

### Scope
**Included:**
- In `Hrot.ClusterRunner/Services/IosSubsystem.cs` → `Initialize()`: replace inline construction with `HrotEnvironment.*` and wire `DdsWriterAdapter<T>` (INTS-P1-003 must be completed first).

**Excluded:**
- Do not change `Hrot.ExCon/Program.cs` here (handled in INTS-P1-003).

### Constraints
- Depends on: INTS-P1-003 (DdsWriterAdapter available), INTS-P2-006 (HrotEnvironment available).

### Success Conditions

**Test 1 — IosSubsystem initialises without throwing**
- Setup: Run `IosSubsystem.Initialize()` with a real DDS participant (domain 10) in an integration test harness.
- Assert: No exception; subsystem enters `Ready` state.

**Test 2 — Regression**
- Assert: All existing `Hrot.ExCon.Tests` pass.

---

## INTS-P3-011 — Trace Logging: SimHost Entity Spawn (Flow 1)

**Design Reference:** [Flow 1 Diagram](./DESIGN-Integration-Troubleshooting.md#flow-1-simhost-creates-a-network-published-vehicle)

### Scope
**Included:**
- Add `[TRACE]`-prefixed `Console.WriteLine` or `ILogger.LogDebug` calls at the four boundary points listed below.

**Excluded:**
- Do not add logging to tight inner loops (e.g., per-tick spatial queries) — only entry points.
- Do not add logging in production release builds (guarded by `#if DEBUG` or log level filter).

### Trace Points

| Location | Message Template |
|---|---|
| `SimHostScenarioManager.SpawnVehicle()` | `"[TRACE-SH] SpawnVehicle: Requesting TkbType={tkbType} at ({x},{y})"` |
| `NetworkSpawningSystem.ProcessSpawn()` | `"[TRACE-SH] ProcessSpawn: NetworkId={networkId} TkbType={cmd.TkbType}"` |
| `EntityLifecycleModule` — ACK promotion | `"[TRACE-SH] ELM: Entity {entity.Index} promoted to Active"` |
| `EntityMasterTranslator.ScanAndPublish()` | `"[TRACE-SH] Egress: Writing EntityMaster for NetID={netId}"` |
| `WorldPosEgressTranslator.ScanAndPublish()` | `"[TRACE-SH] Egress: Writing WorldPos for NetID={netId} pos=({lat},{lon})"` |

### Success Conditions

**Test 1 — All five trace lines appear on spawn**
- Setup: Run `SimHostApp` standalone; redirect stdout/log output to a string sink.
- Action: Trigger `SpawnVehicle()`.
- Assert: The string sink contains all five `[TRACE-SH]` messages in the expected order within 2 seconds.

---

## INTS-P3-012 — Trace Logging: IG Entity Ingress & Render (Flow 2)

**Design Reference:** [Flow 2 Diagram](./DESIGN-Integration-Troubleshooting.md#flow-2-ig-receives-and-renders-entities-from-simhost)

### Scope
**Included:**
- Add trace at ingress and style resolution boundaries.
- Render trace must be guarded by a specific-entity-ID filter to prevent per-frame spam.

### Trace Points

| Location | Message Template |
|---|---|
| `EntityMasterTranslator.ProcessSample()` (IG) | `"[TRACE-IG] Ingress: EntityMaster NetID={master.EntityId} → Ghost spawn"` |
| `WorldPosTranslator.Decode()` (IG) | `"[TRACE-IG] Ingress: WorldPos Entity={entity.Index} Lat={lat} Lon={lon}"` |
| `StyleResolutionSystem.Execute()` | `"[TRACE-IG] Style: Resolved Entity={entity.Index} Texture={style.TextureName}"` |
| `SstVisualizerAdapter.Render()` | `"[TRACE-IG] Render: Drawing Entity={entity.Index} at ({x},{y})"` *(first render only, or filtered by debug EntityId)* |

### Success Conditions

**Test 1 — All four trace lines appear after receiving EntityMaster + WorldPos**
- Setup: Run `IgApplication` with a test DDS writer; capture log output.
- Action: Write an `EntityMaster` followed by a `WorldPos` sample for the same `NetID`.
- Assert: The log contains all four `[TRACE-IG]` messages within 3 seconds.

---

## INTS-P3-013 — Trace Logging: IG Map Drawings & IOS Interactions (Flows 3–6)

**Design Reference:** [Flows 3–6](./DESIGN-Integration-Troubleshooting.md#flow-3-ig-creates-network-distributed-map-drawings)

### Scope
**Included:**
- Trace for `BdcCommandGateway.CreateEntityAsync` / request completion.
- Trace for `CreateEntityRequestSystem.ProcessRequest` / `SendErrorAck`.
- Trace for `IosLogic.StartPlacementMode` / `ProcessClickEvents`.
- Trace for `RequestTransactionManager.CompleteRequest` / `CheckTimeouts`.
- Trace for `MapInteractionConfig` ingress handler in IG.

### Trace Points (selected; add all listed in design talk)

| Location | Message Template |
|---|---|
| `BdcCommandGateway.CreateEntityAsync()` | `"[TRACE-GW] Sending CreateEntityRequest ID={requestId}"` |
| `CreateEntityRequestSystem.ProcessRequest()` | `"[TRACE-SH] Received CreateEntityRequest {requestId} TkbType={tkbType}"` |
| `CreateEntityRequestSystem.SendErrorAck()` | `"[TRACE-SH] ERROR: Rejecting Request {requestId} Code={errorCode}"` |
| `IosLogic.StartPlacementMode()` | `"[TRACE-IOS] Placement Mode ON. ContextId={contextId} TKB={tkbType}"` |
| `IosLogic.ProcessClickEvents()` | `"[TRACE-IOS] MapClickEvent ContextId={evt.ContextId} (expected {activeContextId})"` |
| `RequestTransactionManager.CompleteRequest()` | `"[TRACE-IOS] TxMgr Request {requestId} completed Success={success}"` |
| `RequestTransactionManager.CheckTimeouts()` | `"[TRACE-IOS] WARNING: Request {id} timed out"` |

### Success Conditions

**Test 1 — IOS placement flow produces expected trace sequence**
- Setup: Run integrated IOS + SimHost (in-process); capture log output.
- Action: Call `IosLogic.StartPlacementMode()`; simulate a `MapClickEvent` reply.
- Assert: Log contains `[TRACE-IOS] Placement Mode ON` then `[TRACE-IOS] MapClickEvent` in order.

---

## INTS-P3-014 — Integration Test: End-to-End Entity Lifecycle

**Design Reference:** All phases — validates the complete fix stack.

### Scope
**Included:**
- A single `[TestMethod]` (MSTest) in `Hrot.SimHost.Integration.Tests` (or new `Hrot.Integration.Tests` project).
- Test uses real DDS on domain 10 and real in-process ECS worlds for `SimHostApp` and `IgApplication`.
- Test verifies: spawn in SimHost → publish on DDS → ghost spawned in IG → style resolved → entity visible.

**Excluded:**
- Do not start a full Raylib window in CI; both apps must run headless in the test.
- Do not assert pixel rendering; assert ECS component state only.

### Constraints
- Test must complete within 10 seconds (CI timeout).
- After the test, DDS participants and Raylib (headless) must be cleanly disposed to avoid port conflicts between test runs.
- Use domain 10 to avoid interfering with production domain 0.

### Success Conditions

**Test 1 — Full spawn-to-render pipeline**
- Setup:
  1. Start SimHost headless on domain 10.
  2. Start IG headless on domain 10. 
  3. Both use `HrotEnvironment.CreateTkb()`.
- Action: Publish `SpawnEntityCommand` (`TkbType = TkbEntityTypes.Truck_HMMWV`, position `(1000, 2000)`) to SimHost.
- Assert (within 5 s):
  - SimHost entity has `NetworkIdentity`, `EntityMaster`, `WorldPos` components.
  - IG ghost entity exists with matching `NetworkId`.
  - IG ghost has `WorldPos` component with `Latitude` and `Longitude` non-zero.
  - IG ghost has a resolved `StyleComponent` (not null / default-empty).

**Test 2 — DDS domain isolation**
- Assert: Running the same test sequence on domain 0 produces **no** cross-contamination in domain 10 readers (entity count on domain 10 does not increase from domain 0 spawns).
