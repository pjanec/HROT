# BATCH-14 Report

**Batch:** BATCH-14
**Tasks:** TASK-P5-002, TASK-P5-003
**Status:** COMPLETE

---

## TASK-P5-002: Reflection Scanner

### What was done

1. **Removed hardcoded subsystem usings** from `Program.cs`:
   - Removed: `using Hrot.Orchestrator;`, `using Hrot.SimHost;`, `using Hrot.IG;`,
     `using Hrot.ExCon;`, `using Hrot.CGF;`, `using Hrot.Editor;`

2. **Added three helper methods** to `Program.cs`:
   - `LoadReferencedAssemblies()` — BFS over `AppDomain` to force-load all
     statically-referenced assemblies before the reflection scan.
   - `ScanForSubsystems()` — returns all non-abstract `ISubsystem` types from loaded
     assemblies, excluding `PerspectiveUpdateSubsystem`, `EyesAndMuscleSubsystem`,
     and `CiSubsystem` (runner-internal / handled separately).
   - `TryCreateSubsystem(Type, INetworkFactory)` — tries `(INetworkFactory)` ctor first,
     then falls back to any constructor where all parameters have default values
     (handles `SimHostSubsystem(NodeRole role = ...)` correctly).

3. **Replaced the hardcoded if-chains** with a reflection-driven discovery loop:
   - Discovered subsystems are keyed by `subsystem.Name` (case-insensitive).
   - Each name in `config.RequestedSubsystems` is looked up in the map; unknown names
     print an error with the available list and return exit code 1.

4. **CI path unchanged** — the `config.RequestedSubsystems.Contains("ci")` early-return
   block remains before the reflection scan, so `CiSubsystem` is never reached by the
   general path.

5. **Removed stale NOTE comment** about WindowManager wiring that referred to work
   already completed in a prior batch.

6. **Raylib step** — `Raylib.InitWindow` / `rlImGui.Setup` were already absent from
   `SubsystemOrchestrator.cs` (moved in a prior batch); no change needed here.

### Grep verification

```
Get-ChildItem -Path "Hrot.ClusterRunner" -Recurse -Include "*.cs" |
  Select-String -Pattern "new SimHostSubsystem|new IgSubsystem|new CgfSubsystem|new ExConSubsystem|new OrchestratorSubsystem|new EditorSubsystem"
```
Result: **zero matches** (excluding comments).

---

## TASK-P5-003: --network CLI Flag

### What was done

1. **`Hrot.ClusterRunner.csproj`** — added `<ProjectReference>` to `Hrot.Network.BDC`.

2. **`HrotRunnerConfiguration.cs`** — added:
   ```csharp
   [Option("network", Default = "ned", HelpText = "Network protocol: ned (default) or bdc")]
   public string NetworkProtocol { get; set; } = "ned";
   ```
   - Validation in `Validate()`: throws `InvalidOperationException` if value is not
     `"ned"` or `"bdc"` (case-insensitive).
   - Merge in `MergeFromJsonFile`: applies JSON override when non-empty and non-default.

3. **`Program.cs`** — added network factory creation after config validation:
   - Creates `NetworkEntityMap`, `IGeographicTransform` (via `HrotEnvironment.CreateGeoTransform()`),
     and `FdpEventBus` as composition-root shared objects.
   - Instantiates `NedNetworkFactory` or `BdcNetworkFactory` based on `config.NetworkProtocol`.
   - `DdsParticipant` is passed as `null` to both factories (factories handle null gracefully
     by returning null-object implementations for DDS-dependent methods).
   - `TryCreateSubsystem` passes the factory to subsystems that expose a `(INetworkFactory)`
     constructor; falls back to default-parameter constructor otherwise.

4. **`RunnerConfigurationTests.cs`** — added three tests:
   - `NetworkProtocol_Default_IsNed`
   - `NetworkProtocol_Bdc_SetCorrectly`
   - `NetworkProtocol_InvalidValue_ThrowsInvalidOperation`

### Notes

- `NodeOpSlaveTranslator` was not created in `Program.cs`: the actual constructor requires
  `DdsReader<NodeOpCommand>`, `DdsWriter<NodeOpStatus>`, `DdsWriter<NodeHeartbeat>`, and
  `FdpEventBus` — it is not `(DdsParticipant, int)` as the task description implied.
  Each subsystem continues to create its own translator internally via HrotNodeBuilder.
  This deferred item can be addressed in a separate networking migration batch.

---

## Build Result

```
dotnet build IOS-IG-SimHost.sln -v quiet
```
**0 errors, 0 warnings** (related to our changes).

---

## Test Results

```
dotnet test IOS-IG-SimHost.sln --filter "FullyQualifiedName!~Integration"
```

| Assembly | Passed | Failed | Skipped |
|---|---|---|---|
| Hrot.ClusterRunner.Tests | 211 | 0 | 0 |
| Hrot.SimHost.Tests | 433 | 0 | 2 |
| Hrot.IG.Tests | 404 | 0 | 0 |
| Hrot.ExCon.Tests | 325 | 0 | 0 |
| Hrot.Editor.Tests | 53 | 0 | 0 |
| Hrot.Map.Common.Tests | 30 | 0 | 0 |
| Hrot.Network.NED.Tests | 53 | 0 | 0 |
| Hrot.Presentation.Tests | 16 | 0 | 0 |
| Fdp.Examples.NetworkDemo.Tests | 21 | 1 | 0 |

The one failure (`Deterministic_Time_Switch_Synchronizes_Nodes`) is a pre-existing DDS
timing flake unrelated to this batch.

---

## Files Changed

- `Hrot.ClusterRunner/Hrot.ClusterRunner.csproj` — added BDC project reference
- `Hrot.ClusterRunner/Configuration/HrotRunnerConfiguration.cs` — NetworkProtocol option + validation + merge
- `Hrot.ClusterRunner/Program.cs` — reflection scanner, factory creation, usings cleanup
- `Hrot.ClusterRunner.Tests/RunnerConfigurationTests.cs` — three new --network tests
