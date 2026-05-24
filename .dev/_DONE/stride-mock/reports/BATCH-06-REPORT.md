# BATCH-06 Report: SM-010 Refactor IgApplication to Use SharedApplicationBootstrapper

## Status: COMPLETE

## Tasks Executed

### Step 1 — Create IgNodeBootstrapper.cs

**File:** `Hrot/Subsystems/Hrot.IG/IgNodeBootstrapper.cs`

Created `internal sealed class IgNodeBootstrapper : SharedApplicationBootstrapper` with all seven
override hooks:

| Phase | Hook | Implementation |
|-------|------|----------------|
| 2 | RegisterDomainComponents | HrotEnvironment.CreateTkb(), HrotSharedComponentRegistry.RegisterAll, all IG-specific components |
| 3 | BuildSerializer | ScenarioSerializerBuilder("Hrot.IG").Build() |
| 4a | PopulateSystems | empty body (IG is visualization-only) |
| 4b | GetAdditionalModules | StyleResolutionModule, MapCullingModule, MapLayerModule, HistoryTrailModule, EventEffectModule (if !headless) |
| 5 | BuildOrchestration | FdpEventBus, ClusterSlave, NodeOpSlaveTranslator, ListenerRecordReplayController, five cluster handlers, DiagnosticsDumpClusterOpHandler |
| 6a | RegisterSpawningPipeline | GhostDestructionSystem, IgUnitHierarchyModule |
| 6b | RegisterNetworkTranslators | guarded null check; creates NetworkAdapter/CommandGateway; registers CycloneNetworkIngressSystem/Egress/Cleanup; sets NetworkEnabled=true |
| 6d | RegisterApplicationSystems | delegates to ApplicationSystemsRegistrar callback |

Public surface exposed after BootstrapNode returns:
- `bool NetworkEnabled`
- `IIgNetworkAdapter? NetworkAdapter`
- `Hrot.Core.Network.ICommandGateway? CommandGateway`
- `FdpEventBus? OrchestrationBus`
- `NodeOpSlaveTranslator? IgSlaveTranslator`
- `Action<HrotNodeContext>? ApplicationSystemsRegistrar`

### Step 2 — Create IgBootstrapperHelpers.cs

**File:** `Hrot/Subsystems/Hrot.IG/IgBootstrapperHelpers.cs`

Extracted `GhostDestructionSystem` and `IgUnitHierarchyModule` from private nested classes
inside `IgApplication` into `internal sealed` top-level classes so they are accessible
from `IgNodeBootstrapper`. The `SelectionInteractionSystemAdapter` private nested class
remains in `IgApplication` as it is only used there.

### Step 3 — Refactor IgApplication.cs

**File:** `Hrot/Subsystems/Hrot.IG/IgApplication.cs`

Changes:
- Added `private IgNodeBootstrapper? _igBootstrapper;` field
- Added `using Hrot.Common;` for NodeRole
- Replaced `InitializeEmbedded()` body: creates pre-world objects, constructs
  `IgNodeBootstrapper`, sets `ApplicationSystemsRegistrar` lambda (gizmo subsystem,
  selection system, event history capture, canvas menu update), calls
  `_igBootstrapper.BootstrapNode()`, extracts results into existing fields
- Fixed `_geoTransform` assignment: cast `ctx.GeoTransform as WGS84Transform`
- Deleted entire `private void InitializeEcs()` method (was ~150 lines)
- Deleted entire `private void InitializeNetwork(bool, int?)` method (was ~400 lines)
- Removed private nested definitions of `GhostDestructionSystem` and
  `IgUnitHierarchyModule` (moved to IgBootstrapperHelpers.cs)

### Step 4 — Create IgNodeBootstrapperTests.cs

**File:** `Hrot/Subsystems/Hrot.IG.Tests/IgNodeBootstrapperTests.cs`

Six tests for SC_SM010_2, all using reflection to invoke the protected
`GetAdditionalModules()`:

| Test | Assertion |
|------|-----------|
| Headless_ContainsStyleResolutionModule | ✅ Pass |
| Headless_ContainsMapCullingModule | ✅ Pass |
| Headless_ContainsMapLayerModule | ✅ Pass |
| Headless_ContainsHistoryTrailModule | ✅ Pass |
| Headless_DoesNotContainEventEffectModule | ✅ Pass |
| NonHeadless_ContainsEventEffectModule | ✅ Pass |

## Build Results

```
dotnet build Hrot\Subsystems\Hrot.IG\Hrot.IG.csproj --no-incremental
→ Build succeeded.
```

## Test Results

### New tests (SC_SM010_2)
```
Total tests: 6  /  Passed: 6  /  Failed: 0
```

### Hrot.IG.Tests (full suite)
```
Total tests: 387  /  Passed: 319  /  Failed: 68  /  Skipped: 0
```
The 68 failures are pre-existing (identical count on the unmodified baseline).
No regressions introduced.

### Hrot.StrideMock.Tests
```
Total tests: 41  /  Passed: 41  /  Failed: 0
```

## Deviations from Instructions

### ScenarioSerializerBuilder instead of HrotScenarioSerializerFactory
**Instruction:** `=> HrotScenarioSerializerFactory.Build()`
**Actual:** `=> new Fdp.Toolkit.Scenario.ScenarioSerializerBuilder("Hrot.IG").Build()`
**Reason:** `HrotScenarioSerializerFactory` lives in `Hrot.SimHost.Serializers`, which
`Hrot.IG.csproj` does not reference. The equivalent result is achieved via the toolkit
builder.

### IgBootstrapperHelpers.cs extraction
**Instruction:** did not mention extracting GhostDestructionSystem/IgUnitHierarchyModule.
**Actual:** Created `IgBootstrapperHelpers.cs` to expose these as `internal` classes.
**Reason:** The types were private nested classes inside `IgApplication` and therefore
inaccessible from `IgNodeBootstrapper`. Extraction was required to make the bootstrapper
compile.

### WGS84Transform cast
**Instruction:** `_geoTransform = _context.GeoTransform`
**Actual:** `_geoTransform = _context.GeoTransform as WGS84Transform;`
**Reason:** `HrotNodeContext.GeoTransform` is typed as `IGeographicTransform?` but
`_geoTransform` is declared as `WGS84Transform?`; an explicit safe cast is required.
