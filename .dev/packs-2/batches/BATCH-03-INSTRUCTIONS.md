# BATCH-03 Instructions

**Batch:** BATCH-03  
**Tasks:** PACK2-P002, PACK2-E001, DEBT-05  
**Estimated effort:** ~6–8h  
**Prerequisites:** BATCH-02 merged (PACK2-D005 — three egress translators exist in `Hrot.Map.Common/Replication/Egress/`)

---

## Overview

Three coupled-but-mostly-independent tasks:

1. **PACK2-P002** — Create two translator-pack `IEcsModule` composites: `ActuatorIntentsEgressPack` and `EntityStatesIngressPack`.
2. **PACK2-E001** — Scaffold the new `Hrot.ScenarioEditor` project (stub only; no tool logic yet).
3. **DEBT-05** — Add standalone unit tests for `SpawnEntityCommandEgressTranslator` and `DestroyEntityCommandEgressTranslator` (their D005 success criteria 1 & 2 were not met as standalone tests).

> ⚠️ These three tasks are **independent** of each other. You may implement them in any order.
> All three must be complete before submitting the batch report.

---

## Context You Must Read First

1. **Task definitions:** `.dev/packs-2/TASK-DETAIL.md` — read §PACK2-P002 and §PACK2-E001 in full.
2. **Design:** `.dev/packs-2/DESIGN.md` — read §0.B (Translator Pack Composites) and §Phase 2 intro.
3. **Debt memo:** `.dev/packs-2/DEBT-TRACKER.md` — see DEBT-05 for the unit test requirement.
4. **CycloneNetworkModule pattern:** `FDP/ModuleHost/ModuleHost.Network.Cyclone/Modules/CycloneNetworkModule.cs`
   — study how `RegisterSystems()` registers a `CycloneNetworkIngressSystem` and a `CycloneEgressSystem`.
5. **Existing egress translators:** `Hrot.Map.Common/Replication/Egress/` — the three D005 translators
   you are grouping into `ActuatorIntentsEgressPack`.
6. **Existing ingress translators:** `Hrot.Map.Common/Replication/Ingress/` — the translators you are
   grouping into `EntityStatesIngressPack`.
7. **SimHost egress translators:**
   - `Hrot.SimHost/Network/NavigationIntentEgressTranslator.cs` — constructor: `(DdsParticipant, NetworkEntityMap, IGeographicTransform)`
   - `Hrot.SimHost/Network/Egress/WeaponFireIntentEgressTranslator.cs` — constructor: `(DdsParticipant, NetworkEntityMap)`
8. **Test pattern:** `Hrot.Map.Common.Tests/MapRouteTranslatorTests.cs` — the `CapturingWriter<T>` test double pattern.

---

## Task 1 — Create Translator Pack Composites (PACK2-P002)

**Task Definition:** [TASK-DETAIL.md §PACK2-P002](../TASK-DETAIL.md#pack2-p002--create-translator-pack-composite-wrappers-for-feature-switch)

### 1.1 — `ActuatorIntentsEgressPack` (new file in `Hrot.SimHost`)

**File:** `Hrot.SimHost/Translators/ActuatorIntentsEgressPack.cs`  
**Namespace:** `Hrot.SimHost.Translators`  
**Project:** `Hrot.SimHost/Hrot.SimHost.csproj` (no new project reference needed — Hrot.SimHost already references both `ModuleHost.Network.Cyclone` and `Hrot.Map.Common`)

**Implementation requirements:**

This pack groups all **outbound** translators into a single `IEcsModule` unit so the HROT Editor
composition root can hot-plug the entire "send intent commands to SimHost" surface atomically.

```csharp
public class ActuatorIntentsEgressPack : IEcsModule
{
    public string Name => "ActuatorIntentsEgress";
    public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

    private readonly IDescriptorTranslator[] _translators;

    public ActuatorIntentsEgressPack(
        DdsParticipant participant,
        NetworkEntityMap entityMap,
        IGeographicTransform geoTransform,
        FdpEventBus eventBus)
    {
        _translators = new IDescriptorTranslator[]
        {
            new NavigationIntentEgressTranslator(participant, entityMap, geoTransform),
            new WeaponFireIntentEgressTranslator(participant, entityMap),
            new SpawnEntityCommandEgressTranslator(participant, eventBus, geoTransform),
            new UpdateEntityCommandEgressTranslator(participant, eventBus, entityMap, geoTransform),
            new DestroyEntityCommandEgressTranslator(participant, eventBus),
        };
    }

    public void RegisterSystems(ISystemRegistry registry)
    {
        registry.RegisterSystem(new CycloneEgressSystem(_translators));
    }

    public void Tick(ISimulationView view, float deltaTime) { }
}
```

> **Note:** `CycloneEgressSystem` is in `ModuleHost.Network.Cyclone.Systems` namespace and already
> resolvable from `Hrot.SimHost`. It calls `ScanAndPublish(view)` on each translator in the
> `SystemPhase.Export` phase.

**Required usings:**
```csharp
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.NetworkSpawning.Events;
using Hrot.Map.Common.Replication.Egress;
using Hrot.SimHost.Network;
using Hrot.SimHost.Network.Egress;
using ModuleHost.Core.Abstractions;
using ModuleHost.Network.Cyclone.Systems;
using CycloneDDS.Runtime;
```

---

### 1.2 — `EntityStatesIngressPack` (new file in `Hrot.Map.Common`)

**File:** `Hrot.Map.Common/Translators/EntityStatesIngressPack.cs`  
**Namespace:** `Hrot.Map.Common.Translators`  
**Project:** `Hrot.Map.Common/Hrot.Map.Common.csproj` (no new project references needed — already has `ModuleHost.Network.Cyclone`)

**Implementation requirements:**

This pack groups all **inbound** visual and structural translators required for a complete 2D
operational picture when the HROT Editor connects to a remote SimHost (External mode).

```csharp
public class EntityStatesIngressPack : IEcsModule
{
    public string Name => "EntityStatesIngress";
    public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

    private readonly IDescriptorTranslator[] _translators;

    public EntityStatesIngressPack(
        DdsParticipant? participant,
        NetworkEntityMap entityMap,
        FdpEventBus eventBus,
        GhostCreationSystem ghostCreationSystem,
        IGeographicTransform geoTransform)
    {
        _translators = new IDescriptorTranslator[]
        {
            new EntityMasterIngressTranslator(participant, entityMap, eventBus, ghostCreationSystem),
            new GeoSpatialIngressTranslator(participant, entityMap, geoTransform, ghostCreationSystem),
            new EntityInfoIngressTranslator(participant, entityMap, eventBus, ghostCreationSystem),
            new MapVisualOverlayIngressTranslator(participant, entityMap, geoTransform, ghostCreationSystem),
            new MapRouteIngressTranslator(participant, entityMap, geoTransform),
            new EntityDamageIngressTranslator(participant, entityMap, ghostCreationSystem),
        };
    }

    public void RegisterSystems(ISystemRegistry registry)
    {
        registry.RegisterSystem(new CycloneNetworkIngressSystem(_translators));
    }

    public void Tick(ISimulationView view, float deltaTime) { }
}
```

> **Note:** `CycloneNetworkIngressSystem` is defined at the bottom of
> `FDP/ModuleHost/ModuleHost.Network.Cyclone/Modules/CycloneNetworkModule.cs` in the
> `ModuleHost.Network.Cyclone.Modules` namespace. It calls `PollIngress(cmd, view)` on each
> translator in `SystemPhase.Input`. It takes `IDescriptorTranslator[]` only (no DdsParticipant
> parameter — translators own their own DDS readers).

**Required usings:**
```csharp
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Systems;
using Hrot.Map.Common.Replication.Ingress;
using ModuleHost.Core.Abstractions;
using ModuleHost.Network.Cyclone.Modules;   // CycloneNetworkIngressSystem
using CycloneDDS.Runtime;
```

---

### 1.3 — Success Criteria (PACK2-P002)

1. *(Build)* Both new composites compile with zero errors.
2. *(Integration — ActuatorIntentsEgressPack)* Write `ActuatorIntentsEgressPackTests.cs` in
   `Hrot.SimHost.Tests/`:
   - Instantiate the pack with a test kernel.
   - Publish a `SpawnEntityCommand` to a test `FdpEventBus`.
   - Tick the kernel once so `CycloneEgressSystem` runs.
   - Assert a `CreateEntityRequest` was written by the `SpawnEntityCommandEgressTranslator`
     (use the same `CapturingWriter<T>` stub pattern from `MapRouteTranslatorTests`).
   - At minimum 2 tests: one for spawn, one for destroy passthrough.
3. *(Integration — EntityStatesIngressPack)* Write `EntityStatesIngressPackTests.cs` in
   `Hrot.Map.Common.Tests/`:
   - Instantiate the pack with `null` participant (all translators support null for unit testing).
   - Assert the pack name is `"EntityStatesIngress"`.
   - Assert `RegisterSystems()` does not throw.
   - At minimum 1 smoke-test (instantiation + RegisterSystems succeeds).

> **Note on test location:** `ActuatorIntentsEgressPackTests.cs` must go in `Hrot.SimHost.Tests`
> because `Hrot.Map.Common.Tests` does not reference `Hrot.SimHost`. `EntityStatesIngressPackTests.cs`
> goes in `Hrot.Map.Common.Tests`.

---

## Task 2 — Scaffold `Hrot.ScenarioEditor` Project (PACK2-E001)

**Task Definition:** [TASK-DETAIL.md §PACK2-E001](../TASK-DETAIL.md#pack2-e001--scaffold-hrotscenarioeditor-project)

### 2.1 — Create the project file

**File:** `Hrot.ScenarioEditor/Hrot.ScenarioEditor.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\FDP\Kernel\Fdp.Kernel\Fdp.Kernel.csproj" />
    <ProjectReference Include="..\FDP\ModuleHost\ModuleHost.Core\ModuleHost.Core.csproj" />
    <ProjectReference Include="..\FDP\Toolkits\FDP.Toolkit.NetworkSpawning\FDP.Toolkit.NetworkSpawning.csproj" />
    <ProjectReference Include="..\FDP\Toolkits\FDP.Toolkit.Vis2D\FDP.Toolkit.Vis2D.csproj" />
    <ProjectReference Include="..\Hrot.Map.Common\Hrot.Map.Common.csproj" />
    <ProjectReference Include="..\Hrot.Common\Hrot.Common.csproj" />
  </ItemGroup>

</Project>
```

> ⚠️ **Do NOT add:** any `CycloneDDS.*` or `Hrot.NED` project references. The ScenarioEditor
> must remain network-agnostic. The Dependency Check success condition (SC2) will verify this.

### 2.2 — Create the module stub

**File:** `Hrot.ScenarioEditor/ScenarioEditorModule.cs`

```csharp
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;

namespace Hrot.ScenarioEditor;

/// <summary>
/// Entry-point <see cref="IEcsModule"/> for the Scenario Editor shared interaction logic.
///
/// <para>
/// This stub will be populated in <c>PACK2-E002</c> (tool migration) and
/// <c>PACK2-E003</c> (render layer migration).
/// </para>
/// </summary>
public class ScenarioEditorModule : IEcsModule
{
    public string Name => "ScenarioEditor";
    public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

    public void RegisterSystems(ISystemRegistry registry)
    {
        // Populated in PACK2-E002 (tool systems) and PACK2-E003 (render layer).
    }

    public void Tick(ISimulationView view, float deltaTime) { }
}
```

### 2.3 — Add to solution

Run from the workspace root:
```
dotnet sln IOS-IG-SimHost.sln add Hrot.ScenarioEditor/Hrot.ScenarioEditor.csproj
```

### 2.4 — Success Criteria (PACK2-E001)

1. *(Build)* `dotnet build Hrot.ScenarioEditor` succeeds with zero errors and zero warnings.
2. *(Dependency check)* `dotnet list Hrot.ScenarioEditor package` or inspection of the `.csproj`:
   no `CycloneDDS` or `Hrot.NED` in direct or transitive references.
3. *(Unit test)* Create `Hrot.ScenarioEditor.Tests/ScenarioEditorModuleTests.cs` with a minimal
   test project (xUnit, net8.0):
   - Instantiate `new ScenarioEditorModule()`.
   - Call `RegisterSystems(new TestSystemRegistry())` where `TestSystemRegistry` is a stub
     that accepts any `IEcsModuleSystem`.
   - Assert no exception is thrown and `module.Name == "ScenarioEditor"`.

> **Test project:** Create `Hrot.ScenarioEditor.Tests/Hrot.ScenarioEditor.Tests.csproj` as a
> standard xUnit test project (mirroring `Hrot.SimHost.Tests` as a template). Add it to the
> solution. Reference `Hrot.ScenarioEditor`.

---

## Task 3 — Standalone Unit Tests for New Egress Translators (DEBT-05)

**Debt:** DEBT-05 — D005 success criteria 1 & 2 require standalone unit tests for
`SpawnEntityCommandEgressTranslator` and `DestroyEntityCommandEgressTranslator`.

**Test project:** `Hrot.Map.Common.Tests` — already has `InternalsVisibleTo` visibility to
`Hrot.Map.Common` so internal (testable) constructors are accessible.

### 3.1 — `SpawnEntityCommandEgressTranslatorTests.cs`

**File:** `Hrot.Map.Common.Tests/Replication/Egress/SpawnEntityCommandEgressTranslatorTests.cs`

**Required tests (minimum 2):**

**Test 1 — Standard path: SpawnEntityCommand without prebuilt request**
```
SpawnEntityCommand_IsConsumed_WritesOneCreateEntityRequest_WithMatchingRequestId()
  1. Create FdpEventBus (real).
  2. Create CapturingWriter<CreateEntityRequest>.
  3. Create translator using internal testable ctor:
     new SpawnEntityCommandEgressTranslator(writer, bus, geoTransform: null)
  4. Publish SpawnEntityCommand with TkbType = "TestVehicle" and RequestId = some Guid.
  5. Call translator.PollIngress(cmd, view) — use a null/stub IEntityCommandBuffer and
     a null/stub ISimulationView (both are unused by this translator's PollIngress).
  6. Assert: writer.Publishes.Count == 1.
  7. Assert: writer.Publishes[0].RequestId == the published command's RequestId.
```

> **Note:** `IEntityCommandBuffer` and `ISimulationView` are not used by `PollIngress` in the
> egress translators (they are bus-event-driven). You may pass `null!` for both since the
> method doesn't dereference them.

**Test 2 — Side-channel path: prebuilt CreateEntityRequest bypasses standard build**
```
SpawnEntityCommand_WithPrebuilt_WritesPrebuiltRequest_NotNewlyBuiltOne()
  1. Create prebuilt CreateEntityRequest with distinct EntityId = 999999.
  2. Create translator with tryGetPrebuilt delegate that returns the prebuilt for any ID.
  3. Publish SpawnEntityCommand.
  4. Call PollIngress.
  5. Assert: writer.Publishes[0].EntityId == 999999 (the prebuilt's value, not default).
```

**Testable constructor signature** (internal — visible via InternalsVisibleTo):
```csharp
internal SpawnEntityCommandEgressTranslator(
    IDdsWriter<CreateEntityRequest> writer,
    FdpEventBus eventBus,
    IGeographicTransform? geoTransform,
    Func<Guid, CreateEntityRequest?>? tryGetPrebuilt = null)
```

---

### 3.2 — `DestroyEntityCommandEgressTranslatorTests.cs`

**File:** `Hrot.Map.Common.Tests/Replication/Egress/DestroyEntityCommandEgressTranslatorTests.cs`

**Required tests (minimum 1):**

**Test — DestroyEntityCommand writes DeleteEntityRequest with matching NetworkId**
```
DestroyEntityCommand_IsConsumed_WritesOneDeleteEntityRequest_WithMatchingEntityId()
  1. Create FdpEventBus.
  2. Create CapturingWriter<DeleteEntityRequest>.
  3. Create translator:
     new DestroyEntityCommandEgressTranslator(writer, bus)
  4. Publish DestroyEntityCommand with NetworkId = 42L.
  5. Call PollIngress(null!, null!).
  6. Assert: writer.Publishes.Count == 1.
  7. Assert: writer.Publishes[0].EntityId == 42.
```

**Testable constructor signature:**
```csharp
internal DestroyEntityCommandEgressTranslator(
    IDdsWriter<DeleteEntityRequest> writer,
    FdpEventBus eventBus)
```

**Note on `CreateEntityRequest` and `DeleteEntityRequest`:** These are in `Hrot.NED.Messages`.
`Hrot.Map.Common.Tests` already references `Hrot.Map.Common` which transitively brings in
`Hrot.NED`. Both types should be available without adding new project references.

**Note on `FdpEventBus`:** Use `new FdpEventBus()` — it is a concrete instantiable class from
`Fdp.Kernel` (or `Fdp.Interfaces`). Look at any existing IG test that uses `FdpEventBus`.

---

## Verification Checklist

Before writing the batch report, confirm all of the following:

### Build
- [ ] `dotnet build IOS-IG-SimHost.sln --no-incremental` → **0 errors**

### Tests
- [ ] `dotnet test Hrot.Map.Common.Tests --no-build` → all pass (≥ 97 tests including 3 new)
- [ ] `dotnet test Hrot.SimHost.Tests --no-build` → all pass (new ActuatorIntentsEgressPackTests included)
- [ ] `dotnet test Hrot.ScenarioEditor.Tests --no-build` → all pass (ScenarioEditorModuleTests)
- [ ] `dotnet test Hrot.ClusterRunner.Integration.Tests --no-build` → no new failures beyond pre-existing

### Dependency checks
- [ ] `Hrot.ScenarioEditor.csproj` contains NO `CycloneDDS` or `Hrot.NED` references

---

## Files Produced

| File | Change |
|------|--------|
| `Hrot.SimHost/Translators/ActuatorIntentsEgressPack.cs` | New |
| `Hrot.Map.Common/Translators/EntityStatesIngressPack.cs` | New |
| `Hrot.SimHost.Tests/ActuatorIntentsEgressPackTests.cs` | New |
| `Hrot.Map.Common.Tests/Replication/Egress/EntityStatesIngressPackTests.cs` | New |
| `Hrot.Map.Common.Tests/Replication/Egress/SpawnEntityCommandEgressTranslatorTests.cs` | New |
| `Hrot.Map.Common.Tests/Replication/Egress/DestroyEntityCommandEgressTranslatorTests.cs` | New |
| `Hrot.ScenarioEditor/Hrot.ScenarioEditor.csproj` | New |
| `Hrot.ScenarioEditor/ScenarioEditorModule.cs` | New |
| `Hrot.ScenarioEditor.Tests/Hrot.ScenarioEditor.Tests.csproj` | New |
| `Hrot.ScenarioEditor.Tests/ScenarioEditorModuleTests.cs` | New |
| `IOS-IG-SimHost.sln` | Add Hrot.ScenarioEditor + Tests projects |

> The `.dev/packs-2/` batch-tracking files are created/updated by the dev lead, not by you.

---

## Batch Report

Submit a `BATCH-03-REPORT.md` in `.dev/packs-2/reports/` when done. Include:

1. Task completion table (PACK2-P002, PACK2-E001, DEBT-05).
2. Test counts.
3. Answers to these questions:
   - **Q1:** `CycloneNetworkIngressSystem` (used by `EntityStatesIngressPack`) is defined inside
     `CycloneNetworkModule.cs`. Did you use it directly, or did you use `CycloneIngressSystem`
     from `CycloneIngressSystem.cs`? What was your reasoning?
   - **Q2:** Did `ActuatorIntentsEgressPack` need any new project references in
     `Hrot.SimHost.csproj`, or were all needed types already reachable?
   - **Q3:** For `ScenarioEditorModule`, did `FDP.Toolkit.Vis2D` introduce any transitive
     references to `Hrot.NED` or `CycloneDDS`? If so, how did you resolve it?
   - **Q4:** Did you encounter any issues with `null` participant in `EntityStatesIngressPack`
     unit tests? How were null reader creations handled?
4. Suggested git commit message.
