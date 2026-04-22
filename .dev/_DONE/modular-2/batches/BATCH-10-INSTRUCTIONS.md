# BATCH-10: Complete SimHost NED Cleanup + Decouple IG and CGF from NED (TASK-P4-002 finish + TASK-P4-003)

**Batch Number:** BATCH-10
**Tasks:** DEBT-009 (P4-002 completion), TASK-P4-003

**Phase:** Phase 4 (continued)
**Estimated Effort:** 10-14 hours
**Priority:** HIGH
**Dependencies:** BATCH-09 committed

---

## Onboarding

1. `.dev/modular-2/DESIGN.md` -- Architecture overview, Phase 4.
2. `.dev/modular-2/TASK-DETAIL.md` -- TASK-P4-003.
3. `.dev/modular-2/DEBT-TRACKER.md` -- especially DEBT-009.
4. `Hrot.Network.NED/SimHost/` -- reference for already-moved translator files.
5. `Hrot.Network.Orchestration/` -- already contains orchestration types (NodeOpCommand, NodeHeartbeat, etc.)

---

## Current State (read before starting)

### DEBT-009: Duplicate translator files

BATCH-09 created NEW versions of all translator files in `Hrot.Network.NED/SimHost/` but
did NOT delete the ORIGINALS from `Hrot.SimHost/Network/`. Now both copies exist:

**Already moved (new copy in NED/SimHost/, original NOT yet deleted from SimHost/):**
- `Hrot.SimHost/Network/Egress/AudioTargetDetectedEgressTranslator.cs`
- `Hrot.SimHost/Network/Egress/DamageAssessedEgressTranslator.cs`
- `Hrot.SimHost/Network/Egress/MissionControlAckEgressTranslator.cs`
- `Hrot.SimHost/Network/Egress/MunitionDetonationEgressTranslator.cs`
- `Hrot.SimHost/Network/Egress/WeaponFireIntentEgressTranslator.cs`
- `Hrot.SimHost/Network/Egress/WeaponFireNotificationEgressTranslator.cs`
- `Hrot.SimHost/Network/Ingress/EntityHitDamageIngressTranslator.cs`
- `Hrot.SimHost/Network/Ingress/MissionControlIngressTranslator.cs`
- `Hrot.SimHost/Network/Ingress/MunitionDetonationIngressTranslator.cs`
- `Hrot.SimHost/Network/Ingress/WeaponFireRequestIngressTranslator.cs`
- `Hrot.SimHost/Network/SimHostAuxiliaryTranslatorPack.cs`

**NOT moved at all (still only in SimHost, need to go to NED/SimHost/):**
- `Hrot.SimHost/Network/BrainPathfindingTranslatorPack.cs`
- `Hrot.SimHost/Network/BrainPerceptionTranslatorPack.cs`
- `Hrot.SimHost/Network/PathfindingTranslators.cs`
- `Hrot.SimHost/Network/PerceptionTranslators.cs`
- `Hrot.SimHost/Network/SimPathfindingTranslatorPack.cs`
- `Hrot.SimHost/Network/SimPerceptionTranslatorPack.cs`

`NodeBootstrapper.cs` in `Hrot.SimHost` calls these pathway pack files directly.
`SimHostApp.cs` now uses `networkFactory.CreateSimHostAuxiliaryTranslators()` for the
combat/mission translators (GOOD), but does NOT route pathfinding/perception through factory.

### CGF still references Hrot.Network.NED

`Hrot.CGF.csproj` has `<ProjectReference Include="..\Hrot.Network.NED\...">`.
`CgfApplication.cs` uses:
- `using Hrot.NED.Descriptors.Orchestration;` -- orchestration types (but these are already
  in `Hrot.Network.Orchestration/Orchestration/OrchestrationMessages.cs` with kept namespace)
- `using Hrot.NED.Messages;` -- check what messages are used
- `using CycloneDDS.Runtime;` -- DDS participant (still needed for CgfApplication)

### IG still references Hrot.Network.NED (transitively via Hrot.IG.csproj)

`IgApplication.cs` uses NED types extensively for entity state replication translators.
These should be replaced with `INetworkFactory.CreateReplicationModule()`.

---

## Phase 1: Complete DEBT-009 -- Finish P4-002 Translator Cleanup

### 1a: Move remaining pack files to Hrot.Network.NED/SimHost/

Create these files in `Hrot.Network.NED/SimHost/`:
- `BrainPathfindingTranslatorPack.cs` (copy from SimHost, change namespace)
- `BrainPerceptionTranslatorPack.cs` (copy from SimHost, change namespace)
- `PathfindingTranslators.cs` (copy from SimHost, change namespace)
- `PerceptionTranslators.cs` (copy from SimHost, change namespace)
- `SimPathfindingTranslatorPack.cs` (copy from SimHost, change namespace)
- `SimPerceptionTranslatorPack.cs` (copy from SimHost, change namespace)

Change namespaces from `Hrot.SimHost.Network` to `Hrot.Network.NED.SimHost`.

### 1b: Delete ALL old translator files from Hrot.SimHost/Network/

After creating NED copies in step 1a, delete from `Hrot.SimHost/Network/`:
- All files in `Hrot.SimHost/Network/Egress/` (directory becomes empty)
- All files in `Hrot.SimHost/Network/Ingress/` (directory becomes empty)
- `BrainPathfindingTranslatorPack.cs`
- `BrainPerceptionTranslatorPack.cs`
- `PathfindingTranslators.cs`
- `PerceptionTranslators.cs`
- `SimHostAuxiliaryTranslatorPack.cs`
- `SimPathfindingTranslatorPack.cs`
- `SimPerceptionTranslatorPack.cs`

Also delete the now-empty `Ingress/` and `Egress/` directories.

### 1c: Update NedNetworkFactory to include pathfinding and perception translators

The `NedSimHostAuxiliaryTranslators` calls `SimHostAuxiliaryTranslatorPack.Create()` which
creates combat/mission translators. But the pathfinding and perception translators are
created by `BrainPathfindingTranslatorPack`, `SimPathfindingTranslatorPack`, etc.

Extend `INetworkFactory` in `Hrot.Core/Network/INetworkFactory.cs` with:
```csharp
/// <summary>Creates the pathfinding network translators for the given node role.</summary>
ISimHostPathfindingTranslators CreateSimHostPathfindingTranslators();

/// <summary>Creates the perception network translators for the given node role.</summary>
ISimHostPerceptionTranslators CreateSimHostPerceptionTranslators();
```

Define `ISimHostPathfindingTranslators` and `ISimHostPerceptionTranslators` in
`Hrot.Core/Network/` (similar to `ISimHostAuxiliaryTranslators` -- `RegisterOn(ModuleHostKernel)`).

Create `Hrot.Network.NED/SimHost/NedSimHostPathfindingTranslators.cs` and
`Hrot.Network.NED/SimHost/NedSimHostPerceptionTranslators.cs` that wrap the
respective pack classes and implement `RegisterOn()`.

Add null stubs in `NedNetworkFactory` and `BdcNetworkFactory`.

### 1d: Update NodeBootstrapper to use factory

In `Hrot.SimHost/NodeBootstrapper.cs`, find where `BrainPathfindingTranslatorPack.Create()`,
`SimPathfindingTranslatorPack.Create()`, `BrainPerceptionTranslatorPack.Create()`,
`SimPerceptionTranslatorPack.Create()` are called.

Replace with calls from the injected `INetworkFactory`:
```csharp
factory.CreateSimHostPathfindingTranslators().RegisterOn(kernel);
factory.CreateSimHostPerceptionTranslators().RegisterOn(kernel);
```

Note: `NodeBootstrapper` currently does NOT have an `INetworkFactory` parameter.
Check if it needs to be added or if there's a better way to inject it.
See how `SimHostApp` passed the `NedNetworkFactory` instance -- extend NodeBootstrapper
to accept `INetworkFactory?` as an optional param (null = no factory, skip registration).

### 1e: Remove NED project reference from Hrot.SimHost.csproj

After 1a-1d, run:
```
Get-ChildItem -Path "Hrot.SimHost" -Recurse -Include "*.cs" |
  Select-String "Hrot\.NED\.|Hrot\.Network\.NED|Hrot\.NED " | Select-Object Path, Line
```

If there are no remaining usages, remove from `Hrot.SimHost/Hrot.SimHost.csproj`:
```xml
<ProjectReference Include="..\Hrot.Network.NED\Hrot.Network.NED.csproj" />
```

Build and verify. **This completes TASK-P4-002.**

---

## Phase 2: Decouple CGF from NED (TASK-P4-003 partial)

### 2a: Understand what CGF actually uses from Hrot.Network.NED

Run this to find all NED usages in Hrot.CGF source (not obj/):
```
Get-ChildItem -Path "Hrot.CGF" -Recurse -Include "*.cs" -Exclude "*/obj/*" |
  Select-String "Hrot\.NED\." | Select-Object Path, Line
```

Expected findings:
- `CgfApplication.cs` uses `Hrot.NED.Descriptors.Orchestration.*` (already in
  `Hrot.Network.Orchestration` with the same namespace -- just need to remove NED ref)
- `CgfApplication.cs` uses `Hrot.NED.Messages.*` -- check which messages

### 2b: Verify orchestration types are accessible via Hrot.Network.Orchestration

`Hrot.CGF` currently references `Hrot.Common` which references `Hrot.Network.Orchestration`.
So `Hrot.NED.Descriptors.Orchestration.*` types (NodeOpCommand etc.) should already be
accessible via the `Hrot.Network.Orchestration` transitive dependency.

Verify by temporarily removing `Hrot.Network.NED` from `Hrot.CGF.csproj` and checking
whether the orchestration-related build errors disappear with `Hrot.Network.Orchestration`
still available transitively.

### 2c: Fix any remaining NED usages in CgfApplication.cs

`CgfApplication.cs` has `using Hrot.NED.Messages;`. Identify what types from
`Hrot.NED.Messages` are used in `CgfApplication.cs`.

If they are time-sync/cluster types that are in `Hrot.Network.Orchestration`, no change needed
to the using directive -- just ensure the type comes from `Hrot.Network.Orchestration`,
not `Hrot.Network.NED`.

If any types in `CgfApplication.cs` are genuine simulation data NED types
(EntityMaster, CreateEntityRequest, etc.) -- those should NOT be in CgfApplication
and must be moved to `CgfSubsystem.cs` (in ClusterRunner) which is allowed to
reference NED.

### 2d: Remove Hrot.Network.NED reference from Hrot.CGF.csproj

Once all NED usages in `Hrot.CGF` source files are resolved:
1. Remove `<ProjectReference Include="..\Hrot.Network.NED\Hrot.Network.NED.csproj" />`
   from `Hrot.CGF/Hrot.CGF.csproj`
2. Build and fix any remaining errors

The moved systems (`CreateEntityRequestSystem`, `DeleteEntityRequestSystem`,
`NedRequestFinalizationSystem`) use only `Hrot.Core.Network` types -- they are already
NED-free and will compile without the NED reference.

---

## Phase 3: Decouple IG from NED (TASK-P4-003 continued)

### 3a: Audit IG's NED usages

Run:
```
Get-ChildItem -Path "Hrot.IG" -Recurse -Include "*.cs" -Exclude "*/obj/*" |
  Select-String "Hrot\.NED\." | Select-Object Path, Line
```

`IgApplication.cs` uses NED types for:
- Entity state replication translators (EntityMaster, EntityInfo, etc.)
- NED-specific translator packs (`EntityStatesPack`, etc.)

### 3b: Replace direct NED replication with INetworkFactory.CreateReplicationModule()

In `IgApplication.cs`, find where `NedReplicationModule` is constructed or where
NED-specific translator packs are created and registered.

The goal is to hide this behind `INetworkFactory`. Looking at `SimHostApp.cs` as a
reference implementation:
- `HrotNodeBuilder.WithReplication(role)` creates the replication module
- `_context.NedReplication` provides the module

`IgApplication` should use the same `HrotNodeBuilder` pattern (which it might already do
for part of it). Check `IgApplication.cs` for `HrotNodeBuilder` usage.

If `IgApplication` already uses `HrotNodeBuilder` + `WithReplication()`:
- The replication module is already hidden behind `HrotNodeContext.NedReplication`
- The remaining NED usages are likely specific translators not yet moved

If not, extend `IgApplication` to use `HrotNodeBuilder` for replication.

### 3c: Move any IG-specific NED translators to Hrot.Network.NED/IG/

Any translator files in `Hrot.IG/` that use NED types should be moved to
`Hrot.Network.NED/IG/`. Create the directory if needed.

Check: `Hrot.IG/Network/` -- does it exist? What's in it?

### 3d: Create ICgfNetworkTranslators / IIgNetworkTranslators factory methods if needed

If IG has auxiliary translators beyond the replication module, add factory methods:
```csharp
ISimHostAuxiliaryTranslators CreateIgAuxiliaryTranslators();  // or a named type
```

Or include them in `CreateReplicationModule()` if they're always needed together.

### 3e: Remove Hrot.Network.NED reference from Hrot.IG.csproj

Once all NED usages are resolved, remove the NED project reference from
`Hrot.IG/Hrot.IG.csproj` and verify build.

---

## Phase 4: Final Build and Test Pass

```powershell
dotnet build IOS-IG-SimHost.sln -v quiet
dotnet test Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj
dotnet test Hrot.SimHost.Integration.Tests/Hrot.SimHost.Integration.Tests.csproj
dotnet test Hrot.ClusterRunner.Integration.Tests/Hrot.ClusterRunner.Integration.Tests.csproj
```

---

## Key Notes

- `Hrot.CGF.csproj` currently references `Hrot.Network.NED`. After this batch, it should
  reference only `Hrot.Common`, `Hrot.Core`, `CycloneDDS.Runtime`, `Fdp.Core`.
- `Hrot.IG.csproj` currently references `Hrot.Network.NED`. After this batch, it should
  not reference NED directly.
- The `Hrot.Network.NED` reference that `Hrot.CGF` needs may be ONLY for orchestration
  types -- but those are already in `Hrot.Network.Orchestration` (same namespace).
  If removing the NED ref causes zero compilation errors, we're done.
- `CgfSubsystem.cs` (in ClusterRunner) will KEEP its NED reference via ClusterRunner,
  since it creates `NedEntityCreationRequestSource` etc.
- The pathfinding/perception pack files that need to move to NED all use `DdsParticipant`
  and NED-specific types -- they belong in `Hrot.Network.NED/SimHost/`.
- `NodeBootstrapper.cs` is in `Hrot.SimHost` -- once these packs are gone from SimHost,
  NodeBootstrapper needs a way to call them. Inject `INetworkFactory` via a new optional
  parameter or through `HrotNodeContext`.
