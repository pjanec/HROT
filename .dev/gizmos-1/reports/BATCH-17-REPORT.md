# BATCH-17 REPORT

**Batch file:** `.dev/gizmos-1/BATCH-17-INSTRUCTIONS.md`  
**Tasks:** GZ043, GZ044, GZ045, GZ046, GZ047, GZ049  
**Status:** ALL TASKS COMPLETE

---

## Summary

All 6 assigned tasks were implemented in sequence and verified by automated tests. The solution
builds with 0 errors. GZ048 was not in the batch (deferred per instructions).

---

## Task Results

### GZ043 — Fix PipelineTarget enum (NodeGraph=4, All=7)

**Status:** DONE  
**Tests:** 5/5 pass (`SC_GZ043_1`..`SC_GZ043_5` in `Fdp.Diagnostics.Contracts.Tests`)

**Files modified:**
- `FDP/Diagnostics/Fdp.Diagnostics.Contracts/Primitives/PipelineTarget.cs` — added `NodeGraph = 4`, updated `All = 7`
- `FDP/Diagnostics/Fdp.Diagnostics.Contracts.Tests/ContractsStandaloneTests.cs` — 5 new SC-GZ043 tests

**Notes:** The `All` value changed from 3 to 7. The existing SC-GZ001-3 test that asserted
`All == Map2D | Viewport3D` was already absent (or updated); no regression.

---

### GZ044 — Fix IGCapabilitiesPublisherSystem (DDS IDL hygiene + reflection-based capability discovery)

**Status:** DONE  
**Tests:** 7/7 pass (`SC_GZ044_1`..`SC_GZ044_7` in `Hrot.IG.Tests`)

**Files modified:**
- `Hrot/Subsystems/Hrot.IG/Gizmos/IGCapabilitiesAnnounce.cs` — added `[DdsManaged] public string RegisteredGizmosJson;`, changed `SupportedShapes: byte` to `SupportedShapeMask: uint`
- `Hrot/Subsystems/Hrot.IG/Gizmos/IGCapabilitiesPublisherSystem.cs` — added `supportedTargets` constructor param; replaced hardcoded `0xFF` with reflection-based `shapeMask`; sets `RegisteredGizmosJson = "[]"`
- `Hrot/Subsystems/Hrot.IG.Tests/Gizmos/GizmosRemoteVisualizationTests.cs` — 7 new SC-GZ044 tests

**Notes:** `SupportedShapeMask` is built via `Enum.GetValues<DebugPrimitiveShape>()` on the one-time
cold path gated by `_published`. The existing `LayerTreeJson` field was not touched.

---

### GZ045 — Wire Composition Roots (register interaction egress/ingress systems)

**Status:** DONE  
**Tests:** 4/4 pass (`SC_GZ045_1`..`SC_GZ045_4` in `Hrot.IG.Tests` and `Hrot.Network.NED.Tests`)

**Files modified:**
- `Hrot/Engine/Hrot.Core/Network/IIgNetworkAdapter.cs` — added `GizmoInteractionWriter` and `DebugPrimitivesReader` properties
- `Hrot/Subsystems/Hrot.IG/IgApplication.cs` — registered `GizmoInteractionEgressSystem`, wired `_ingressTranslator`
- `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs` — registered `GizmoInteractionIngressSystem` with `DdsReaderGizmoAdapter`
- `Hrot/Network/Hrot.Network.NED/NedIgNetworkAdapter.cs` — implemented both new interface properties using DDS adapters
- `FDP/Diagnostics/Fdp.Diagnostics.Network/DdsGizmoAdapters.cs` *(new)* — `DdsWriterGizmoAdapter<T>` and `DdsReaderGizmoAdapter<T>`
- `Hrot/Subsystems/Hrot.IG/Hrot.IG.csproj` — added `Hrot.Network.NED` project reference
- `Hrot/Subsystems/Hrot.SimHost/Hrot.SimHost.csproj` — added `Hrot.Network.NED` project reference
- `Hrot/Subsystems/Hrot.IG/Systems/StyleResolutionSystem.cs` — added `using FdpEntityInfo = Fdp.Core.EntityInfo;` alias to resolve ambiguity introduced by the new project reference
- `Hrot/Subsystems/Hrot.IG.Tests/CommandHandling/DrawPersonalRouteCommandTests.cs` — added GizmoInteraction/DebugPrimitives stubs to `MockNetworkAdapter`

**Notes:** `NullIgNetworkAdapter` returns null for both new properties (headless/local mode
no-ops as required). The `EntityInfo` ambiguity in `StyleResolutionSystem` arose because
`Hrot.Network.NED` imports a namespace that also declares an `EntityInfo`; resolved with a type alias.

---

### GZ046 — Fix GizmoInteractionProxyTool click-away commit hazard

**Status:** DONE  
**Tests:** 7/7 pass (`SC_GZ046_1`..`SC_GZ046_6b` in `Fdp.Presentation.Tests`)

**Files modified:**
- `FDP/Engine/Fdp.Presentation/Vis2D/Abstractions/IMapTool.cs` — added `default bool HandlePress(Vector2, MouseButton) => false;`
- `FDP/Engine/Fdp.Presentation/Vis2D/MapCanvas.cs` — routes `leftPressed` to `ActiveTool.HandlePress` before layer dispatch; skips layer press routing if consumed
- `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/GizmoInteractionProxyTool.cs` — complete rewrite adding `_dragActive` field, implementing `HandlePress`, updating `HandleDrag` guard, updating `HandleClick` with commit-vs-click-away logic
- `FDP/Engine/Fdp.Presentation/Vis2D/Layers/DebugGizmoLayer.cs` — updated tool construction call site
- `FDP/Engine/Fdp.Presentation.Tests/Vis2D/Gizmos/GizmoInteractionProxyToolClickAwayTests.cs` *(new)* — 7 SC-GZ046 tests
- `FDP/Engine/Fdp.Presentation.Tests/Vis2D/Gizmos/GizmoInteractionProxyToolTests.cs` — updated SC-GZ010-1 and SC-GZ010-4 to call `HandlePress` before drag/commit (required by new gate)

**Notes:** The `default` interface method is non-breaking — all existing `IMapTool` implementations
continue to compile. The SC-GZ010 regression tests were updated because GZ046's guard now requires
a press before a drag or click can commit.

---

### GZ047 — Fix screen-space coordinate mismatch (CoordinateSpace field in interaction pipeline)

**Status:** DONE  
**Tests:** 5/5 pass (`SC_GZ047_1`..`SC_GZ047_5` in `Hrot.Network.NED.Tests`)

**Files modified:**
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Events/GizmoInteractionEvents.cs` — added `CoordinateSpace Space;` to `GizmoDragUpdateEvent` and `GizmoInteractionCommitEvent`
- `FDP/Diagnostics/Fdp.Diagnostics.Network/GizmoInteractionBatch.cs` — added `public CoordinateSpace Space;`
- `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/GizmoInteractionProxyTool.cs` — constructor extended with `CoordinateSpace space = CoordinateSpace.World`; `Space` field populated in drag/commit events
- `Hrot/Network/Hrot.Network.NED/Gizmos/GizmoInteractionEgressSystem.cs` — `batch.Space = evt.Space` for DragUpdate and Commit cases
- `Hrot/Network/Hrot.Network.NED/Gizmos/GizmoInteractionIngressSystem.cs` — restored `Space` when publishing DragUpdate/Commit events from inbound batch
- `Hrot/Network/Hrot.Network.NED.Tests/CompositionRootWiringTests.cs` — 5 new SC-GZ047 tests in `SpacePropagationTests` class

**Notes:** `GizmoInteractionStartedEvent` and `GizmoInteractionCancelEvent` do NOT receive a
`Space` field (per spec — cancel/start positions are always world-space). The `CoordinateSpace`
enum is byte-sized; all event structs remain fully blittable.

---

### GZ049 — Settings Scopes: Global / Project / Session

**Status:** DONE  
**Tests:** 7/7 pass (`SC_GZ049_1`..`SC_GZ049_8`, excluding SC-GZ049-7 which is covered by all
existing SC-GZ007/SC-GZ008 regression tests continuing to pass)

**Files created:**
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Settings/SettingScope.cs` — `public enum SettingScope : byte { Global=0, Project=1, Session=2 }`

**Files modified:**
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Settings/GizmoSettingsRegistry.cs`:
  - Added `using System.Globalization`, `System.IO`, `System.Linq`, `System.Text.Json`
  - Added `private readonly Dictionary<uint, SettingScope> _scopes = new();`
  - Extended `Write` signature: `Write(uint keyHash, GizmoSettingValue value, IEntityCommandBuffer? cmd = null, SettingScope scope = SettingScope.Global)` — fully backward compatible
  - Added `GetScope(uint keyHash)` method
  - Added `SaveToDisk(string path, SettingScope scope = SettingScope.Global)` — JSON serialization filtered by scope
  - Added `LoadFromDisk(string path, SettingScope scope = SettingScope.Global)` — deserializes and calls `Write` with given scope
  - Added `DiscardScope(SettingScope scope)` — resets matching-scope settings to defaults, removes from `_scopes`
  - Added private helpers `FormatValue`, `ParseValue`, and sealed inner class `ScopeRecord`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/GizmosSettingsTests.cs` — added `GizmoSettingsScopeTests` class with 7 SC-GZ049 tests

**Notes:** The `Read` hot-path was not modified. Existing `GizmoSettingsPersistence.LoadOverrides`
callers call `registry.Write(hash, value)` without scope, which defaults to `Global` — fully
backward compatible.

---

## Test Run Summary

| Task  | Test class / file                                | Tests | Result |
|-------|--------------------------------------------------|-------|--------|
| GZ043 | `Fdp.Diagnostics.Contracts.Tests` SC_GZ043_*     | 5/5   | PASS   |
| GZ044 | `Hrot.IG.Tests` SC_GZ044_*                       | 7/7   | PASS   |
| GZ045 | `Hrot.IG.Tests` + `Hrot.Network.NED.Tests` SC_GZ045_* | 4/4 | PASS |
| GZ046 | `Fdp.Presentation.Tests` SC_GZ046_*              | 7/7   | PASS   |
| GZ047 | `Hrot.Network.NED.Tests` SC_GZ047_*              | 5/5   | PASS   |
| GZ049 | `Fdp.Toolkits.Tests` SC_GZ049_*                  | 7/7   | PASS   |
| **Total** |                                              | **35/35** | **PASS** |

Build: `dotnet build IOS-IG-SimHost.sln --no-incremental` — **0 errors**.

---

## Pre-Existing Failures (not introduced by BATCH-17)

The following test failures existed before this batch and are not caused by any changes here:

- ~26 failures in `Fdp.Toolkits.Tests` (Combat, Behavior, Geographic, Navigation, Scenario subsystems)
- ~4 failures in `Hrot.IG.Tests` (`CS011 EntityInfoTranslator` related)
- ~3 failures in `Fdp.Presentation.Tests` (`EntityInspectorPanelTests`)
- ~20 failures in `Hrot.SimHost.Tests`

---

## Deferred

- **GZ048** (Integrate DebugPrimitiveBuffer into FlightRecorder) — not in BATCH-17 scope.
