# BATCH-04: Create Hrot.Network.Orchestration and Hrot.Core (Phase 2 Foundation)

**Batch Number:** BATCH-04
**Tasks:** TASK-P2-003 (Hrot.Network.Orchestration), TASK-P2-001 (Hrot.Core, partial)
**Phase:** Phase 2 — Hrot Layer Consolidation
**Estimated Effort:** 16–20 hours
**Priority:** HIGH
**Dependencies:** BATCH-01, BATCH-02, BATCH-03 (all Phase 1 FDP assemblies must exist)

---

## Onboarding & Workflow

### Developer Instructions

This batch creates the two core Hrot-layer assemblies. It must be done in order:

**Step A first:** Create `Hrot.Network.Orchestration` (it defines orchestration DDS types
that `Hrot.Core` consumes via the `IOrchestrationTranslator` marker interface).

**Step B second:** Create `Hrot.Core` by absorbing `Hrot.Common`, `Hrot.Map.Definitions`,
and the NON-NED parts of `Hrot.Map.Common`.

**Important:** The NED-referencing replication translators in `Hrot.Map.Common/Replication/`,
`Hrot.Map.Common/Commands/NedCommandGateway.cs`, and `Hrot.Map.Common/Translators/`
(which reference `Hrot.NED.Descriptors.*`) are NOT moved in this batch — they will
move to `Hrot.Network.NED` in BATCH-06. For this batch, they remain in a slimmed-down
`Hrot.Map.Common.csproj` that references `Hrot.Core` and `Hrot.NED`.

### Required Reading (IN ORDER)

1. **Task Definitions:**
   - `.dev/modular-2/TASK-DETAIL.md#task-p2-003-create-hrotnetworkorchestration`
   - `.dev/modular-2/TASK-DETAIL.md#task-p2-001-create-hrotcore`
2. **Design Document:** `.dev/modular-2/DESIGN.md`
3. **Previous Review:** `.dev/modular-2/reviews/BATCH-01-REVIEW.md`

### Source Code Locations

- **Source to absorb into Hrot.Network.Orchestration:**
  - `Hrot.NED/Orchestration/OrchestrationMessages.cs`
  - `Hrot.Common/Orchestration/NodeOpSlaveTranslator.cs`
  - `Hrot.Common/Orchestration/OrchestrationObserverTranslator.cs`
  - `Hrot.Common/Orchestration/ClusterOpEgressTranslator.cs`
  - `Hrot.Common/Orchestration/IClusterOpHandler.cs`
  - `Hrot.Common/Orchestration/ITickableClusterOpHandler.cs`
  - `Hrot.Common/Orchestration/HrotHandlerAdapter.cs`
  - `Hrot.Common/Orchestration/Handlers/PreviewClusterOpHandler.cs`
  - `Hrot.Common/Orchestration/ClusterStateChangedEvent.cs`
  - `Hrot.Common/Orchestration/ListenerRecordReplayController.cs`

- **Source remaining in Hrot.Core (from Hrot.Common):**
  - `Abstractions/INedReplicationModule.cs` → replaced by `IReplicationModule` (new file)
  - `Components/*`, `Events/*`, `Scenario/*`, `Systems/*`
  - `Infrastructure/HrotNodeBuilder.cs` (refactored — see Task B5)
  - `Infrastructure/HrotNodeConfig.cs` (updated — see Task B4)
  - `Infrastructure/HrotNodeContext.cs` (updated — see Task B4)
  - `Infrastructure/DdsIdAllocatorHelper.cs` → MOVES TO `Hrot.Network.Orchestration`
  - `NodeRole.cs`

- **Source from Hrot.Map.Common that goes to Hrot.Core:**
  - `Components/*`, `Config/*`, `Events/*`, `Helpers/*`
  - `HrotEnvironment.cs`, `HrotSerializerOptions.cs`, `HrotSharedComponentRegistry.cs`
  - `MapConfig.cs`, `PackRole.cs`, `RouteTkbExtensions.cs`
  - `Scenario/*`, `Services/*`, `Systems/*`
  - `Dds/DdsWriterAdapter.cs`, `Dds/IDdsWriter.cs` (check if NED-free first)

- **Source from Hrot.Map.Common that STAYS in old Hrot.Map.Common temporarily:**
  - `Replication/**/*.cs` — ALL files that reference `Hrot.NED.*`
  - `Commands/NedCommandGateway.cs` — references NED types
  - `Translators/SharedTranslatorPack.cs` — references NED types (check)
  - `Translators/KinematicTranslatorPack.cs` — references NED types (check)
  - `Translators/EntityStatesIngressPack.cs` — references NED types (check)

- **Source from Hrot.Map.Definitions going to Hrot.Core:**
  - ALL files (no NED references in this project)

### Report Submission

When done, submit your report to: `.dev/modular-2/reports/BATCH-04-REPORT.md`

---

## Context

After Phase 1, the FDP assembly graph is complete (Fdp.Core, Fdp.Engine, Fdp.Presentation,
Fdp.Network.Cyclone). This batch creates the Hrot layer foundation that the subsystems will
build on in subsequent batches.

The key architectural constraint: `Hrot.Core` must have ZERO project references to
`Hrot.NED`, `Hrot.Network.NED`, `Hrot.Network.BDC`, `Hrot.Network.Orchestration`, or
`Fdp.Network.Cyclone`. It only references `Fdp.Core`, `Fdp.Engine`, and the
`CycloneDDS.Runtime` package.

---

## Tasks

### Part A: Create Hrot.Network.Orchestration (TASK-P2-003)

#### A1 — Identify source files

From `Hrot.NED/Orchestration/OrchestrationMessages.cs`:
- Contains: `ClusterState` enum, `ClusterOpType` enum, `NodeOpType` enum,
  `SystemStateTopic`, `NodeOpCommand`, `NodeOpStatus`, `NodeHeartbeat`,
  `NodeHeartbeatAck`, `ClusterOpRequest` and related DDS schema types.

From `Hrot.Common/Orchestration/`:
- `NodeOpSlaveTranslator.cs`
- `OrchestrationObserverTranslator.cs`
- `ClusterOpEgressTranslator.cs`
- `IClusterOpHandler.cs`
- `ITickableClusterOpHandler.cs`
- `HrotHandlerAdapter.cs`
- `Handlers/PreviewClusterOpHandler.cs`
- `ClusterStateChangedEvent.cs`
- `ListenerRecordReplayController.cs`

From `Hrot.Common/Infrastructure/DdsIdAllocatorHelper.cs`:
- Move to `Hrot.Network.Orchestration` (it uses `DdsIdAllocator` from `Fdp.Network.Cyclone`)

#### A2 — Create `Hrot.Network.Orchestration/Hrot.Network.Orchestration.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\FDP\Kernel\Fdp.Core\Fdp.Core.csproj" />
    <ProjectReference Include="..\FDP\Toolkits\Fdp.Engine\Fdp.Engine.csproj" />
    <ProjectReference Include="..\FDP\ModuleHost\Fdp.Network.Cyclone\Fdp.Network.Cyclone.csproj" />
    <!-- Hrot.Core reference added AFTER Hrot.Core exists (circular-safe: Orch -> Core) -->
  </ItemGroup>
  <Import Project="..\FDP\ExtDeps\FastCycloneDds\tools\CycloneDDS.CodeGen\CycloneDDS.targets" />
</Project>
```

**After Hrot.Core exists,** add `<ProjectReference Include="..\Hrot.Core\Hrot.Core.csproj" />`.

#### A3 — Move files

Move the files listed in A1 to `Hrot.Network.Orchestration/`.
Maintain similar subfolder structure: `Orchestration/` for DDS schemas,
`Translation/` for translators, `Handlers/` for handlers.

Preserve all original namespaces (`Hrot.NED.Descriptors.Orchestration` for the
DDS schema types, `Hrot.Common.Orchestration` for translators/handlers).

#### A4 — Add IOrchestrationTranslator to Hrot.Core (done in Part B)

`NodeOpSlaveTranslator` must implement `IOrchestrationTranslator` from `Hrot.Core`.
After creating Hrot.Core (Part B), add the interface declaration:

```csharp
// in Hrot.Core, namespace Hrot.Core.Orchestration or Hrot.Common.Infrastructure
/// <summary>Marker interface for the orchestration channel translator (cluster management DDS).</summary>
public interface IOrchestrationTranslator : IDisposable
{
    void Update(); // forward the tick for DDS read/publish cycle
}
```

Then update `NodeOpSlaveTranslator` to implement `IOrchestrationTranslator`.

---

### Part B: Create Hrot.Core (TASK-P2-001)

#### B1 — Categorize Hrot.Map.Common files

Before starting, run this check to identify which files reference NED types:

Search for `using Hrot.NED` in `Hrot.Map.Common/**/*.cs`.

Files WITH `using Hrot.NED.*` → they STAY in old `Hrot.Map.Common` for now.
Files WITHOUT `using Hrot.NED.*` → they move to `Hrot.Core`.

Also check `Hrot.Map.Common/Dds/DdsWriterAdapter.cs` — if it is a generic wrapper
that does not reference `Hrot.NED` types, it can move to `Hrot.Core`.

#### B2 — Create `Hrot.Core/Hrot.Core.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\FDP\Kernel\Fdp.Core\Fdp.Core.csproj" />
    <ProjectReference Include="..\FDP\Toolkits\Fdp.Engine\Fdp.Engine.csproj" />
    <!-- CycloneDDS.Runtime as a pragmatic base: DdsParticipant can be accepted/stored -->
    <ProjectReference Include="..\FDP\ExtDeps\FastCycloneDds\src\CycloneDDS.Runtime\CycloneDDS.Runtime.csproj" />
  </ItemGroup>
  <ItemGroup>
    <!-- From Hrot.Common.csproj -->
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
      <_Parameter1>Hrot.SimHost.Tests</_Parameter1>
    </AssemblyAttribute>
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
      <_Parameter1>Hrot.Editor.Tests</_Parameter1>
    </AssemblyAttribute>
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
      <_Parameter1>Hrot.Network</_Parameter1>
    </AssemblyAttribute>
    <!-- From Hrot.Map.Common.csproj -->
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
      <_Parameter1>Hrot.IG.Tests</_Parameter1>
    </AssemblyAttribute>
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
      <_Parameter1>Hrot.Map.Common.Tests</_Parameter1>
    </AssemblyAttribute>
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
      <_Parameter1>Hrot.ClusterRunner.Integration.Tests</_Parameter1>
    </AssemblyAttribute>
    <!-- Additional: Core.Tests for future -->
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
      <_Parameter1>Hrot.Core.Tests</_Parameter1>
    </AssemblyAttribute>
  </ItemGroup>
</Project>
```

**CRITICAL:** `Hrot.Core.csproj` must have ZERO project references to:
`Hrot.NED`, `Hrot.Network.NED`, `Hrot.Network.BDC`, `Hrot.Network.Orchestration`,
`Fdp.Network.Cyclone`, `Hrot.Map.Common`, `Hrot.Common`.

#### B3 — Move files from Hrot.Common to Hrot.Core

Move (except the Orchestration/ folder which went to Hrot.Network.Orchestration
and except `DdsIdAllocatorHelper.cs` which also went there):
- `Abstractions/INedReplicationModule.cs` → DO NOT COPY to Hrot.Core — replace with
  `IReplicationModule.cs` (see B4)
- `Components/**/*.cs` → `Hrot.Core/Components/Common/`
- `Events/**/*.cs` → `Hrot.Core/Events/Common/`
- `Infrastructure/HrotNodeBuilder.cs` → `Hrot.Core/Infrastructure/` (REFACTORED — see B5)
- `Infrastructure/HrotNodeConfig.cs` → `Hrot.Core/Infrastructure/` (UPDATED — see B5)
- `Infrastructure/HrotNodeContext.cs` → `Hrot.Core/Infrastructure/` (UPDATED — see B5)
- `NodeRole.cs` → `Hrot.Core/`
- `Scenario/**/*.cs` → `Hrot.Core/Scenario/Common/`
- `Systems/**/*.cs` → `Hrot.Core/Systems/Common/`

Preserve namespaces (`Hrot.Common`, `Hrot.Common.Infrastructure`, etc.).

#### B4 — Add new interfaces to Hrot.Core

**File:** `Hrot.Core/Abstractions/IReplicationModule.cs` (NEW)

```csharp
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;
using FDP.Toolkit.Replication.Systems;
namespace Hrot.Common.Abstractions; // preserve old namespace for callers

/// <summary>
/// Protocol-neutral interface for the network replication subsystem.
/// Replaces the NED-specific INedReplicationModule.
/// </summary>
public interface IReplicationModule : IEcsModule
{
    GhostCreationSystem? GhostCreationSystem { get; }
    bool DriveFromNetwork { get; }
}
```

Note: Use namespace `Hrot.Common.Abstractions` to preserve callers without changes.

**File:** `Hrot.Core/Orchestration/IOrchestrationTranslator.cs` (NEW)

```csharp
namespace Hrot.Common.Infrastructure; // keep in same namespace as HrotNodeConfig

/// <summary>Marker interface for the cluster management DDS translator.
/// Implemented by NodeOpSlaveTranslator in Hrot.Network.Orchestration.</summary>
public interface IOrchestrationTranslator : IDisposable
{
    /// <summary>Called each frame to pump DDS reads and bus publishes.</summary>
    void Update();
}
```

#### B5 — Refactor HrotNodeConfig and HrotNodeContext

**HrotNodeConfig.cs** — add three new properties:

```csharp
using CycloneDDS.Runtime;
using ModuleHost.Core.Network.Interfaces; // INetworkIdAllocator is in Fdp.Core

/// <summary>The DDS participant created by the Composition Root. Null in headless mode.</summary>
public DdsParticipant? Participant { get; set; }

/// <summary>The orchestration channel translator created by the Composition Root.
/// Typed as IOrchestrationTranslator so Hrot.Core has no compile-time dependency on
/// Hrot.Network.Orchestration. The Composition Root casts NodeOpSlaveTranslator to this.</summary>
public IOrchestrationTranslator? OrchestrationTranslator { get; set; }

/// <summary>The DDS ID allocator created by the Composition Root. Null in headless mode.
/// Typed as INetworkIdAllocator (from Fdp.Core) so Hrot.Core avoids Fdp.Network.Cyclone ref.</summary>
public INetworkIdAllocator? IdAllocator { get; set; }
```

Remove the `DomainId` property from `HrotNodeConfig` since the participant is now provided
from outside (the domain ID was only needed to call `HrotEnvironment.CreateParticipant()`).
Actually keep `DomainId` if it is used for purposes other than participant creation.

**HrotNodeContext.cs** — update two properties:

1. Change `NodeOpSlaveTranslator? SlaveTranslator` to `IOrchestrationTranslator? SlaveTranslator`
   (keep the same property name for less churn, or rename to `OrchestrationTranslator` — your choice)

2. Change `INedReplicationModule? NedReplication` to `IReplicationModule? Replication`
   (rename the property; update all callers: search for `.NedReplication`)

3. Change `DdsIdAllocator? IdAllocator` to `INetworkIdAllocator? IdAllocator`

After these changes, update the `using` directives in `HrotNodeContext.cs`:
- Remove `using Hrot.Common.Orchestration;` (NodeOpSlaveTranslator is now gone)
- Remove `using ModuleHost.Network.Cyclone.Services;` (DdsIdAllocator gone)
- Remove `using FDP.Toolkit.Replication.Systems;` IF GhostCreationSystem is removed
  (check if it's used — can be null; if it's referenced nowhere after the type change,
  remove it; otherwise keep and type it as `object?`)

#### B6 — Refactor HrotNodeBuilder.cs

Key changes:
1. Remove `using Hrot.Common.Orchestration;` and `using Hrot.NED.Descriptors.Orchestration;`
2. Remove `using ModuleHost.Network.Cyclone.Services;` (no more DdsIdAllocator creation)
3. Replace `participant = HrotEnvironment.CreateParticipant(_config.DomainId);` with
   `var participant = _config.Participant;`
4. Remove the block that creates `NodeOpSlaveTranslator` — replace with
   `var slaveTranslator = _config.OrchestrationTranslator;`
5. Remove the DdsIdAllocator creation block — replace with
   `var idAllocator = _config.IdAllocator;`
6. Remove `DdsIdAllocatorHelper.EnsureRouting(...)` call (moved to composition root)
7. Update the returned `HrotNodeContext` to use: `SlaveTranslator = slaveTranslator`,
   `IdAllocator = idAllocator`

The builder should still wrap `participant` in `participant.EnableSenderTracking(...)` if
`participant != null`, since that uses only `CycloneDDS.Runtime` which is allowed.

#### B7 — Move files from Hrot.Map.Definitions to Hrot.Core

Move ALL files from `Hrot.Map.Definitions/` to `Hrot.Core/MapDefinitions/`.
Preserve namespace (`Hrot.Map.Definitions`).

#### B8 — Partial move from Hrot.Map.Common

Move ONLY the files WITHOUT `using Hrot.NED.*` references to `Hrot.Core/`:
- `Components/**/*.cs` → `Hrot.Core/Components/Map/`
- `Config/**/*.cs` → `Hrot.Core/Config/`
- `Events/**/*.cs` → `Hrot.Core/Events/Map/`
- `Helpers/**/*.cs` → `Hrot.Core/Helpers/`
- `HrotEnvironment.cs` → `Hrot.Core/`
- `HrotSerializerOptions.cs` → `Hrot.Core/`
- `HrotSharedComponentRegistry.cs` → `Hrot.Core/`
- `MapConfig.cs` → `Hrot.Core/`
- `PackRole.cs` → `Hrot.Core/`
- `RouteTkbExtensions.cs` → `Hrot.Core/`
- `Scenario/**/*.cs` → `Hrot.Core/Scenario/Map/`
- `Services/**/*.cs` → `Hrot.Core/Services/`
- `Systems/**/*.cs` → `Hrot.Core/Systems/Map/`
- `Dds/DdsWriterAdapter.cs`, `Dds/IDdsWriter.cs` → IF NED-free, move to `Hrot.Core/Dds/`

Files with NED references that REMAIN in old `Hrot.Map.Common` (now a stub project):
- `Replication/**/*.cs`
- `Commands/NedCommandGateway.cs`
- `Translators/SharedTranslatorPack.cs` (if NED-referencing)
- `Translators/KinematicTranslatorPack.cs` (if NED-referencing)
- `Translators/EntityStatesIngressPack.cs` (if NED-referencing)

#### B9 — Update stub Hrot.Map.Common.csproj

The remaining `Hrot.Map.Common.csproj` should be updated to:
- Reference `Hrot.Core` (instead of the old individual projects)
- Reference `Hrot.NED` (for the NED types the remaining files use)
- Reference `Fdp.Network.Cyclone` (for DDS writers in translators)
- Remove references to `Hrot.Map.Definitions`, `Hrot.Common` (those are now in `Hrot.Core`)

**This stub lives until BATCH-06 creates Hrot.Network.NED.**

#### B10 — Consolidate test projects

Create `Hrot.Core.Tests/Hrot.Core.Tests.csproj` merging:
- `Hrot.Common.Tests` (if it exists — check git)
- `Hrot.Map.Common.Tests`
- `Hrot.Map.Definitions` has no test project

Place `Hrot.Map.Common.Tests` files in `Hrot.Core.Tests/MapCommon/` subdirectory.

**Important:** Some tests may reference the NED-linked types (translators). These tests
should be LEFT IN a temporary `Hrot.Map.Common.Tests` stub project that references
`Hrot.Map.Common` (the stub). Only move the non-NED tests to `Hrot.Core.Tests`.

OR: Move ALL test files to `Hrot.Core.Tests` and update references for tests that
use translators to reference `Hrot.Map.Common` transitively.

#### B11 — Update project references across the solution

Search for all references to `Hrot.Common.csproj`, `Hrot.Map.Common.csproj`,
`Hrot.Map.Definitions.csproj` in all `.csproj` files.
Replace with `Hrot.Core.csproj` where appropriate.

BUT: Projects that specifically need the NED-linked translators (like `Hrot.SimHost`,
`Hrot.IG`, `Hrot.CGF`) should ALSO/STILL reference `Hrot.Map.Common` (the stub) until
BATCH-06. After BATCH-06, those references switch to `Hrot.Network.NED`.

#### B12 — Update solution files

Add `Hrot.Core`, `Hrot.Core.Tests`, `Hrot.Network.Orchestration` to both solution files.
Remove `Hrot.Common`, `Hrot.Map.Definitions` (fully absorbed).
Keep `Hrot.Map.Common` (stub) in the solution for now.

---

## Mandatory Workflow: Test-Driven Task Progression

**Before starting:**
```
dotnet build IOS-IG-SimHost.sln
```

**After Task A (Hrot.Network.Orchestration):**
```
dotnet build IOS-IG-SimHost.sln
```

**After Task B (Hrot.Core):**
```
dotnet build IOS-IG-SimHost.sln
dotnet test Hrot.Core.Tests/Hrot.Core.Tests.csproj
```

**Final verification:**
```
dotnet build IOS-IG-SimHost.sln
dotnet test IOS-IG-SimHost.sln --filter "FullyQualifiedName~Hrot.Core|FullyQualifiedName~Hrot.Map.Common"
```

---

## Testing Requirements

- `Hrot.Core.csproj` must have ZERO project references to any NED/BDC adapter assembly.
  Verify with: `dotnet list Hrot.Core reference`
- `Hrot.Network.Orchestration.csproj` must have ZERO project references to `Hrot.Network.NED`
  or `Hrot.Network.BDC`.
- All tests that were passing before this batch must continue to pass.
- The pre-existing test failures (24 Hrot.SimHost.Tests, 7 Hrot.IG.Tests,
  4 Hrot.ClusterRunner.Tests from DEBT-001) are expected to remain failing.

---

## Report Requirements

Submit `.dev/modular-2/reports/BATCH-04-REPORT.md` covering:

1. **What was done:** Which files moved where.
2. **Issues encountered:** Any namespace conflicts, circular dependencies, failed approaches.
3. **Weak points spotted:** Observations.
4. **Design decisions beyond spec:** In particular, document any decision about which
   Hrot.Map.Common files had NED references and which did not.
5. **Test results:** Output of `dotnet build` and key test runs.
6. **Files changed list:** All modified `.csproj` and solution files.
