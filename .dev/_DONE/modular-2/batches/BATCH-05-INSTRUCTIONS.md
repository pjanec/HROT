# BATCH-05: Create Hrot.Presentation and Add INetworkFactory to Hrot.Core (Phase 2+3 Foundation)

**Batch Number:** BATCH-05
**Tasks:** TASK-P2-002 (Hrot.Presentation), TASK-P3-001 (INetworkFactory + neutral contracts)
**Phase:** Phase 2 completion + Phase 3 foundation
**Dependencies:** BATCH-04 (Hrot.Core must exist)

---

## Context

After BATCH-04, `Hrot.Core` exists with all map and common types.
This batch has two parts:

**Part A:** Create `Hrot.Presentation` by absorbing `Hrot.UI.Common` and `Hrot.ScenarioEditor`.
  Requires removing NED type refs from `Hrot.UI.Common` first by adding neutral DTOs to Hrot.Core.

**Part B:** Add `INetworkFactory` and related contract interfaces to `Hrot.Core`
  (no implementation — these are contract-only additions).

---

## PART A: Create Hrot.Presentation

### A1 — Add neutral mission types to Hrot.Core

Create `Hrot.Core/Mission/MissionTypes.cs`:

```csharp
namespace Hrot.Core.Mission;

/// <summary>Force affiliation for spawn and display.</summary>
public enum eForceIdentifier
{
    FORCE_UNKNOWN  = 0,
    FORCE_FRIENDLY = 1,
    FORCE_OPPOSING = 2,
    FORCE_NEUTRAL  = 3,
}

/// <summary>Lifecycle state of a mission task.</summary>
public enum eTaskState
{
    TASK_PLANNED,
    TASK_ACTIVE,
    TASK_DONE,
    TASK_FAILED,
    TASK_SKIPPED,
}

/// <summary>Imperative mission control command discriminator.</summary>
public enum eMissionCommandType
{
    CMD_JUMP_TO_TASK,
    CMD_APPEND_TASK,
    CMD_INSERT_TASK,
    CMD_REPLACE_MISSION,
    CMD_ABORT_ALL,
}

/// <summary>Condition that triggers a task transition.</summary>
public sealed class MissionTrigger
{
    /// <summary>Trigger type name, e.g. "TimerElapsed".</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>JSON parameter string (schema-validated by the engine).</summary>
    public string Params { get; set; } = string.Empty;
}

/// <summary>Single step in a mission plan.</summary>
public sealed class MissionTask
{
    public Guid TaskId { get; set; }
    public string ExecutingEngine { get; set; } = string.Empty;
    public string BehaviorId { get; set; } = string.Empty;
    public string BehaviorParams { get; set; } = string.Empty;
    public List<MissionTrigger> Triggers { get; set; } = new();
    public eTaskState State { get; set; }
}

/// <summary>Ordered sequence of mission tasks for a single entity.</summary>
public sealed class MissionPlan
{
    public Guid ActiveTaskId { get; set; }
    public List<MissionTask> Tasks { get; set; } = new();
}
```

Create `Hrot.Core/Mission/GeoPoint.cs`:

```csharp
namespace Hrot.Core.Mission;

/// <summary>Geographic position in geodetic coordinates.</summary>
public struct GeoPoint
{
    /// <summary>Latitude in degrees.</summary>
    public double Latitude;

    /// <summary>Longitude in degrees.</summary>
    public double Longitude;

    /// <summary>Altitude in meters above reference ellipsoid.</summary>
    public double Altitude;

    public GeoPoint(double lat, double lon, double alt = 0)
    {
        Latitude  = lat;
        Longitude = lon;
        Altitude  = alt;
    }
}
```

### A2 — Update Hrot.UI.Common source files to use neutral types

Update these 4 files (change `using` directives, change type names):

**Hrot.UI.Common/Facades/IMapPickService.cs**
- Remove `using Hrot.NED.Common;`
- Add `using Hrot.Core.Mission;`
- Change `Task<GeoPoint>` → `Task<Hrot.Core.Mission.GeoPoint>` (or just `Task<GeoPoint>` if you add `using Hrot.Core.Mission;`)

**Hrot.UI.Common/Facades/IMissionEditorService.cs**
- Remove `using Hrot.NED.Descriptors;` and `using Hrot.NED.Messages;`
- Add `using Hrot.Core.Mission;`
- Change `MissionPlan` → `Hrot.Core.Mission.MissionPlan` (use full name or with using alias)
- Change `eMissionCommandType` → `Hrot.Core.Mission.eMissionCommandType`

**Hrot.UI.Common/Panels/MissionPanel.cs**
- Remove `using Hrot.NED.Descriptors;`, `using Hrot.NED.Messages;`, `using Hrot.NED.Common;`
- Add `using Hrot.Core.Mission;`
- Change `MissionPlan` → `Hrot.Core.Mission.MissionPlan`
- Change `MissionTask` → `Hrot.Core.Mission.MissionTask`
- Change `MissionTrigger` → `Hrot.Core.Mission.MissionTrigger`
- Change `eTaskState` → `Hrot.Core.Mission.eTaskState`
- Change `GeoPoint` → `Hrot.Core.Mission.GeoPoint`
- Change `eMissionCommandType` → `Hrot.Core.Mission.eMissionCommandType`
- NOTE: The panel creates MissionPlan directly: `new MissionPlan { Tasks = new List<MissionTask>() }`
  and `new MissionTask { ... }`. Use the Hrot.Core.Mission versions.

**Hrot.UI.Common/Panels/SpawnerPanel.cs**
- Remove `using Hrot.NED.Descriptors;`
- Add `using Hrot.Core.Mission;`
- Change `eForceIdentifier.FORCE_FRIENDLY` → `Hrot.Core.Mission.eForceIdentifier.FORCE_FRIENDLY`
  (or add `using Hrot.Core.Mission;` so `eForceIdentifier` resolves directly)

After these changes, update `Hrot.UI.Common/Hrot.UI.Common.csproj` to REMOVE the Hrot.NED reference
and ADD a reference to Hrot.Core:
```xml
<ProjectReference Include="..\Hrot.Core\Hrot.Core.csproj" />
```
Remove: `<ProjectReference Include="..\Hrot.NED\Hrot.NED.csproj" />`
Keep: `<ProjectReference Include="..\FDP\Framework\Fdp.Presentation\Fdp.Presentation.csproj" />`
Keep: `<ProjectReference Include="..\Hrot.Map.Definitions\Hrot.Map.Definitions.csproj" />` (thin stub → transitive to Hrot.Core)

### A3 — Update ExCon adapters to map NED ↔ neutral types

**Hrot.ExCon/ExConPanelAdapters.cs**

The `ExConMissionShim` currently implements `IMissionEditorService` (external, now uses neutral types) 
but delegates to `Services.IMissionEditorService` (internal, still uses NED types).
Add mapping between the two.

The `ExConMapPickShim` implements `IMapPickService` (external, now returns `Hrot.Core.Mission.GeoPoint`)
but delegates to `Services.IMapPickService` (internal, still returns `Hrot.NED.Common.GeoPoint`).
Add conversion.

Changes needed:
1. Add `using Hrot.Core.Mission;` at the top
2. Keep `using Hrot.NED.Descriptors;`, `using Hrot.NED.Messages;`, `using Hrot.NED.Common;`
   (ExCon still uses NED types internally)
3. In `ExConMissionShim`:
   - Change `(MissionPlan? Plan, long Version) GetMissionSnapshot(long entityId)` signature
     to return `(Hrot.Core.Mission.MissionPlan? Plan, long Version)` - map from NED:
     ```csharp
     public (Hrot.Core.Mission.MissionPlan? Plan, long Version) GetMissionSnapshot(long entityId)
     {
         var (nedPlan, version) = _inner.GetMissionSnapshot(entityId);
         return (nedPlan.HasValue ? MapToNeutral(nedPlan.Value) : null, version);
     }
     ```
   - Change `CommitMissionAsync` to accept `Hrot.Core.Mission.MissionPlan` - convert to NED before delegating:
     ```csharp
     public async Task<UiMissionCommitResult> CommitMissionAsync(
         long entityId, Hrot.Core.Mission.MissionPlan plan, long baseVersion)
     {
         var r = await _inner.CommitMissionAsync(entityId, MapToNed(plan), baseVersion).ConfigureAwait(false);
         return new UiMissionCommitResult(r.Success, r.NewVersion, r.ErrorMessage);
     }
     ```
   - Change `SendControlCommandAsync` to accept `Hrot.Core.Mission.eMissionCommandType`:
     ```csharp
     public async Task<UiMissionCommitResult> SendControlCommandAsync(
         long entityId, Hrot.Core.Mission.eMissionCommandType type, Guid taskId)
     {
         var r = await _inner.SendControlCommandAsync(entityId,
             (Hrot.NED.Messages.eMissionCommandType)type, taskId).ConfigureAwait(false);
         return new UiMissionCommitResult(r.Success, r.NewVersion, r.ErrorMessage);
     }
     ```
   - Add private static mapping helpers:
     ```csharp
     private static Hrot.Core.Mission.MissionPlan MapToNeutral(Hrot.NED.Descriptors.MissionPlan p)
         => new()
         {
             ActiveTaskId = p.ActiveTaskId,
             Tasks = p.Tasks?.Select(MapToNeutral).ToList() ?? new(),
         };

     private static Hrot.Core.Mission.MissionTask MapToNeutral(Hrot.NED.Descriptors.MissionTask t)
         => new()
         {
             TaskId          = t.TaskId,
             ExecutingEngine = t.ExecutingEngine,
             BehaviorId      = t.BehaviorId,
             BehaviorParams  = t.BehaviorParams,
             Triggers        = t.Triggers?.Select(x => new Hrot.Core.Mission.MissionTrigger
                               { Type = x.Type, Params = x.Params }).ToList() ?? new(),
             State           = (Hrot.Core.Mission.eTaskState)t.State,
         };

     private static Hrot.NED.Descriptors.MissionPlan MapToNed(Hrot.Core.Mission.MissionPlan p)
         => new()
         {
             ActiveTaskId = p.ActiveTaskId,
             Tasks = p.Tasks?.Select(MapToNed).ToList() ?? new(),
         };

     private static Hrot.NED.Descriptors.MissionTask MapToNed(Hrot.Core.Mission.MissionTask t)
         => new()
         {
             TaskId          = t.TaskId,
             ExecutingEngine = t.ExecutingEngine,
             BehaviorId      = t.BehaviorId,
             BehaviorParams  = t.BehaviorParams,
             Triggers        = t.Triggers?.Select(x => new Hrot.NED.Descriptors.MissionTrigger
                               { Type = x.Type, Params = x.Params }).ToList() ?? new(),
             State           = (Hrot.NED.Descriptors.eTaskState)t.State,
         };
     ```
4. In `ExConMapPickShim`:
   - Change `PickLocationAsync` to return `Task<Hrot.Core.Mission.GeoPoint>`:
     ```csharp
     public async Task<Hrot.Core.Mission.GeoPoint> PickLocationAsync(CancellationToken ct = default)
     {
         var p = await _inner.PickLocationAsync(ct).ConfigureAwait(false);
         return new Hrot.Core.Mission.GeoPoint(p.Latitude, p.Longitude, p.Altitude);
     }
     ```

### A4 — Update Hrot.Editor adapters

**Hrot.Editor/Adapters/EditorMapPickAdapter.cs**
- Remove `using Hrot.NED.Common;`
- Add `using Hrot.Core.Mission;`
- Change return type of `PickLocationAsync` to `Task<Hrot.Core.Mission.GeoPoint>`
- The internal TaskCompletionSource likely uses `TaskCompletionSource<GeoPoint>` (NED) internally.
  Read the file: find `TaskCompletionSource<GeoPoint>` and change to `TaskCompletionSource<Hrot.Core.Mission.GeoPoint>`
  OR keep internal NED GeoPoint but convert on return.

**Hrot.Editor/Adapters/EditorMissionService.cs**
- Remove `using Hrot.NED.Descriptors;`, `using Hrot.NED.Messages;`
- Add `using Hrot.Core.Mission;`
- Change method signatures to use neutral types
- Internal logic may still use NED types from FDP.Toolkit.Behavior components
  (e.g. `MissionControlIntent` which may use NED types). 
  For those cases: keep using NED types internally and map at the interface boundary.
  Add the same mapping helpers as in ExConPanelAdapters.cs (or share via a static helper class).

IMPORTANT: Read the actual content of both Editor adapter files before making changes.
The implementation details (how maps/tcs are set up) require careful reading.

### A5 — Create target Hrot.Presentation project

**Create `Hrot.Presentation/Hrot.Presentation.csproj`:**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>
  <ItemGroup>
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
      <_Parameter1>Hrot.IG.Tests</_Parameter1>
    </AssemblyAttribute>
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
      <_Parameter1>Hrot.ScenarioEditor.Tests</_Parameter1>
    </AssemblyAttribute>
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
      <_Parameter1>Hrot.Presentation.Tests</_Parameter1>
    </AssemblyAttribute>
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
      <_Parameter1>Hrot.ClusterRunner.Integration.Tests</_Parameter1>
    </AssemblyAttribute>
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Hrot.Core\Hrot.Core.csproj" />
    <ProjectReference Include="..\Hrot.Map.Common\Hrot.Map.Common.csproj" />
    <ProjectReference Include="..\FDP\Kernel\Fdp.Core\Fdp.Core.csproj" />
    <ProjectReference Include="..\FDP\Toolkits\Fdp.Engine\Fdp.Engine.csproj" />
    <ProjectReference Include="..\FDP\Framework\Fdp.Presentation\Fdp.Presentation.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="NLog" Version="5.2.8" />
    <PackageReference Include="Raylib-cs" Version="7.0.2" />
    <PackageReference Include="rlImgui-cs" Version="3.2.0" />
  </ItemGroup>
</Project>
```

### A6 — Move files

Move ALL .cs files from `Hrot.UI.Common/` to `Hrot.Presentation/` (maintaining subfolder structure).
Move ALL .cs files from `Hrot.ScenarioEditor/` to `Hrot.Presentation/` (maintaining subfolder structure,
use `ScenarioEditor/` prefix to avoid conflicts: `Hrot.Presentation/ScenarioEditor/`).

Preserve all namespaces (`Hrot.UI.Common`, `Hrot.ScenarioEditor.*`).

After moving, delete the old `Hrot.UI.Common/` and `Hrot.ScenarioEditor/` directories
(or keep empty project stubs if needed for backward compatibility — prefer deleting).

### A7 — Update project references in callers

Projects that reference `Hrot.UI.Common` or `Hrot.ScenarioEditor` → switch to `Hrot.Presentation`:

- `Hrot.ClusterRunner.Integration.Tests`
- `Hrot.ClusterRunner`
- `Hrot.Editor` (references BOTH — replace both with single Hrot.Presentation ref)
- `Hrot.ExCon.Tests`
- `Hrot.ExCon`
- `Hrot.IG`
- `Hrot.ScenarioEditor.Tests`
- `Hrot.SimHost`

For each: find `<ProjectReference ... Hrot.UI.Common ... />` and `<ProjectReference ... Hrot.ScenarioEditor ... />`
and replace with `<ProjectReference Include="..\Hrot.Presentation\Hrot.Presentation.csproj" />`
(use correct relative path from each .csproj location).

### A8 — Create Hrot.Presentation.Tests

Create `Hrot.Presentation.Tests/Hrot.Presentation.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.5.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Hrot.Presentation\Hrot.Presentation.csproj" />
    <ProjectReference Include="..\Hrot.IG\Hrot.IG.csproj" />
    <ProjectReference Include="..\Hrot.Map.Common\Hrot.Map.Common.csproj" />
    <ProjectReference Include="..\Hrot.Core\Hrot.Core.csproj" />
    <ProjectReference Include="..\FDP\Kernel\Fdp.Core\Fdp.Core.csproj" />
  </ItemGroup>
</Project>
```

Move ALL test files from `Hrot.ScenarioEditor.Tests/` to `Hrot.Presentation.Tests/`.
Also update `Hrot.ScenarioEditor.Tests.csproj` reference in the solution to `Hrot.Presentation.Tests`.

### A9 — Update solution file

In `IOS-IG-SimHost.sln`:
- Add `Hrot.Presentation` (new GUID: `{D4E5F6A7-B8C9-0123-DEF0-123456789003}`)
- Add `Hrot.Presentation.Tests` (new GUID: `{E5F6A7B8-C9D0-1234-EF01-234567890104}`)
- Remove `Hrot.UI.Common` entry
- Remove `Hrot.ScenarioEditor` entry
- Remove `Hrot.ScenarioEditor.Tests` entry (replaced by Hrot.Presentation.Tests)

---

## PART B: Add INetworkFactory Interfaces to Hrot.Core (TASK-P3-001)

### B1 — Create `Hrot.Core/Network/INetworkFactory.cs`

```csharp
using Hrot.Common.Abstractions;

namespace Hrot.Core.Network;

/// <summary>
/// Factory that creates all protocol-specific network infrastructure for a simulation node.
/// Implemented by Hrot.Network.NED (NedNetworkFactory) and Hrot.Network.BDC (BdcNetworkFactory).
/// </summary>
public interface INetworkFactory
{
    /// <summary>Creates the replication module that synchronises entity state over the network.</summary>
    IReplicationModule CreateReplicationModule();

    /// <summary>Creates the command gateway for sending mission control commands.</summary>
    ICommandGateway CreateCommandGateway();

    /// <summary>Creates the egress writers for ExCon-originated entity lifecycle commands.</summary>
    IExConEgressWriters CreateExConEgressWriters();
}
```

### B2 — Create `Hrot.Core/Network/ICommandGateway.cs`

```csharp
namespace Hrot.Core.Network;

/// <summary>
/// Neutral interface for mission-control commands from ExCon/Editor to CGF.
/// Replaces the NED-specific INedCommandGateway from Hrot.Map.Common.
/// </summary>
public interface ICommandGateway : IDisposable
{
    /// <summary>Sends a create-entity request and returns the assigned entity ID.</summary>
    Task<int> CreateEntityAsync(CreateEntityCommand cmd, CancellationToken ct = default);

    /// <summary>Sends an update-descriptor request.</summary>
    Task SendUpdateDescriptorAsync(UpdateEntityDescriptorCommand cmd, CancellationToken ct = default);

    /// <summary>Sends a mission-control request (replace/jump/abort) to the CGF.</summary>
    Task SendMissionControlRequestAsync(MissionControlCommand cmd, CancellationToken ct = default);
}
```

### B3 — Create `Hrot.Core/Network/IExConEgressWriters.cs`

```csharp
namespace Hrot.Core.Network;

/// <summary>
/// Aggregate of neutral write operations for ExCon-originated entity lifecycle commands.
/// Replaces individually injected IDdsWriter&lt;NedWireType&gt; fields in ExConLogic.
/// </summary>
public interface IExConEgressWriters : IDisposable
{
    /// <summary>Publishes a map interaction configuration update.</summary>
    void WriteMapConfig(MapConfigDto config);

    /// <summary>Publishes a delete-entity command.</summary>
    void WriteDeleteEntity(int entityId);

    /// <summary>Publishes a create-entity command.</summary>
    void WriteCreateEntity(CreateEntityCommand cmd);

    /// <summary>Publishes a generic map command request.</summary>
    void WriteMapCommand(MapCommandDto cmd);
}
```

### B4 — Create neutral command DTOs in `Hrot.Core/Network/`

Create `Hrot.Core/Network/Commands.cs`:

```csharp
namespace Hrot.Core.Network;

/// <summary>Protocol-neutral create-entity command.</summary>
public sealed class CreateEntityCommand
{
    public long TkbType { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Altitude { get; set; }
    public string? PropertiesJson { get; set; }
    public int ForceId { get; set; }
}

/// <summary>Protocol-neutral update-entity-descriptor command.</summary>
public sealed class UpdateEntityDescriptorCommand
{
    public int EntityId { get; set; }
    public string DescriptorJson { get; set; } = string.Empty;
    public long BaseVersion { get; set; }
}

/// <summary>Protocol-neutral mission-control command (wrapper for a mission plan or imperative).</summary>
public sealed class MissionControlCommand
{
    public int EntityId { get; set; }
    public Hrot.Core.Mission.eMissionCommandType CommandType { get; set; }
    public Hrot.Core.Mission.MissionPlan? Plan { get; set; }
    public Guid TaskId { get; set; }
    public long BaseVersion { get; set; }
}

/// <summary>Protocol-neutral map config DTO.</summary>
public sealed class MapConfigDto
{
    public string ConfigJson { get; set; } = string.Empty;
}

/// <summary>Protocol-neutral map command DTO.</summary>
public sealed class MapCommandDto
{
    public string CommandJson { get; set; } = string.Empty;
}
```

---

## Mandatory Verification

**After Part A changes (before moving files):**
```
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build IOS-IG-SimHost.sln -v q 2>&1 | Select-String "error" | Select-Object -First 20
```

**After complete Batch:**
```
dotnet build IOS-IG-SimHost.sln
dotnet test Hrot.Presentation.Tests/Hrot.Presentation.Tests.csproj
dotnet test Hrot.ScenarioEditor.Tests/Hrot.ScenarioEditor.Tests.csproj  # should still exist or be removed
```

**Verify constraint (Hrot.Presentation must not reference Hrot.NED):**
```
dotnet list Hrot.Presentation/Hrot.Presentation.csproj reference
```
Must NOT contain Hrot.NED.

---

## Report

Submit to: `.dev/modular-2/reports/BATCH-05-REPORT.md`

---

## Critical Implementation Notes

1. Read EditorMissionService.cs and EditorMapPickAdapter.cs fully before editing them.
2. The enum casts (e.g. `(Hrot.NED.Descriptors.eTaskState)neutralState`) only work because
   the enum values have the same integer backing. Verify this is the case before using casts.
3. `MissionPlan` in Hrot.NED is a `struct` — use `.HasValue` when checking nullable structs.
   The neutral `MissionPlan` is a `class` — just use null-check.
4. Do NOT change any namespace in any moved file.
5. `eForceIdentifier` enum values in Hrot.Core.Mission must have the same integer values as
   `Hrot.NED.Descriptors.eForceIdentifier` (FORCE_UNKNOWN=0, FORCE_FRIENDLY=1, FORCE_OPPOSING=2, FORCE_NEUTRAL=3).
   This allows a simple cast `(eForceIdentifier)nedEnum` if needed.
6. Build after EACH of Parts A1-A4 to catch errors early.
