# BATCH-11 Report: Decouple IG, CGF, and Orchestrator from NED

**Batch:** BATCH-11  
**Tasks:** TASK-P4-003  
**Date:** April 12, 2026  
**Status:** Partial (Phases 1, 5, 6 partial; Phases 2, 3 done; Phases 4, 7 partial/skipped)

---

## Phase Summary

| Phase | Status | Notes |
|-------|--------|-------|
| Phase 1: Decouple Hrot.CGF from NED | PARTIAL | Orchestration ref added; NED ref kept (see blocker) |
| Phase 2: Move NedStatusCode to Orchestration | DONE | Enum moved; NED now references Orchestration |
| Phase 3: Decouple Hrot.Orchestrator from NED | DONE | NED ref removed, Orchestration ref added |
| Phase 4: Move IG Translator Files to NED/IG/ | PARTIAL | 3 of 5 moved; 2 blocked by circular deps |
| Phase 5: IIgTranslators Interface + Factory | PARTIAL | Interface + factory done; NedIgTranslators covers only 3 translators |
| Phase 6: IgApplication wiring | PARTIAL | Factory wired for 3 translators; ContextActionsUpdate stays direct |
| Phase 7: Remove NED ref from Hrot.IG.csproj | SKIPPED | Multiple non-translator NED usages remain (see blockers) |

---

## Detailed Phase Descriptions

### Phase 1 (PARTIAL): Hrot.CGF NED decoupling

`Hrot.Network.Orchestration` reference was added to `Hrot.CGF.csproj`. The `Hrot.Network.NED`
reference was NOT removed because `CgfLogicPack.cs` instantiates `MissionControlExecutionSystem`,
which has namespace `Hrot.Common.Systems` but is defined in `Hrot.Network.NED/Systems/`. This
type uses `Hrot.NED.Descriptors.MissionTrigger` (NED-specific DDS type) so it cannot be moved
to `Hrot.Common` without adding NED as a Common dependency.

**Blocker:** `MissionControlExecutionSystem` is physically in NED but logically a pure-ECS system
(namespace `Hrot.Common.Systems`). Requires a dedicated batch to move the file and its DDS type
dependencies to the appropriate assembly.

### Phase 2 (DONE): NedStatusCode moved to Orchestration

`NedStatusCode` enum was cut from `Hrot.Network.NED/GenericMessages.cs` and appended to
`Hrot.Network.Orchestration/Orchestration/OrchestrationMessages.cs` under a new
`namespace Hrot.NED.Messages { ... }` block. A stub comment was left in `GenericMessages.cs`
to document the move. `Hrot.Network.NED.csproj` now references `Hrot.Network.Orchestration`
so all existing NED code using `NedStatusCode` continues to compile with no `using` changes.

### Phase 3 (DONE): Hrot.Orchestrator decoupled from NED

`Hrot.Network.NED` project reference removed from `Hrot.Orchestrator.csproj`. Added
`Hrot.Network.Orchestration` reference. All Orchestrator NED usages (`NodeOpCommand`,
`NodeOpStatus`, `NodeHeartbeat`, `AssetInventoryTopic`, `NedStatusCode`) were already in
`Hrot.Network.Orchestration` or moved there in Phase 2. Zero code changes required.

### Phase 4 (PARTIAL): IG Translator Files moved to NED/IG/

**Created `Hrot.Network.NED/IG/` directory** with these files (namespace `Hrot.Network.NED.IG`):
- `IgMissionIngressTranslator.cs` (moved from `Hrot.IG/Translators/`)
- `GroundClampingOverrideTranslator.cs` (moved from `Hrot.IG/Translators/`)
- `AudioTargetDetectedIngressTranslator.cs` (moved from `Hrot.IG/Translators/`)

**Not moved (circular dep blockers):**

1. `WeaponFireIngressTranslator.cs`: publishes `IgWeaponFireEvent` (defined in `Hrot.IG/IgEvents.cs`).
   Moving to NED would require NED to reference Hrot.IG, which is circular since IG references NED.

2. `ContextActionsUpdateTranslator.cs`: uses `Hrot.IG.ContextActionsUpdate` and
   `Hrot.IG.Components.ContextAction` (both defined in Hrot.IG) as well as the DDS
   `Hrot.NED.Messages.ContextActionsUpdate`. Same circular dep issue.

**Resolution path:** Move `IgWeaponFireEvent`, `ContextActionsUpdate`, and `ContextAction` to
`Hrot.Common` (they have no IG-specific dependencies). Then both NED and IG can reference Common
for these types and the translators can be moved.

### Phase 5 (PARTIAL): IIgTranslators interface + factory

Created `Hrot.Core/Network/IIgTranslators.cs` with:
- `IIgTranslators` interface (GetTranslators with all params passed at call time)
- `NullIgTranslators` public stub (returns empty list)

Added `IIgTranslators CreateIgTranslators()` to `INetworkFactory.cs`.

Created `Hrot.Network.NED/IG/NedIgTranslators.cs` (public, not internal as originally
specified — required for direct use in IgSubsystem without full factory construction).
NedIgTranslators covers the 3 moved translators only (IgMission, GroundClamping, AudioTargetDetected).

Added `CreateIgTranslators()` to `NedNetworkFactory` and `BdcNetworkFactory`.

### Phase 6 (PARTIAL): IgApplication wiring

Added `IIgTranslators? _igTranslatorsProvider` field to `IgApplication`.
Added optional `igTranslatorsProvider` parameter to `InitializeEmbedded`.
Replaced the 3 direct translator instantiations with factory call:
```csharp
if (!_headless)
{
    if (_igTranslatorsProvider != null)
    {
        foreach (var t in _igTranslatorsProvider.GetTranslators(...))
            customTranslators.Add(t);
    }
}
```

`ContextActionsUpdateTranslator` remains instantiated directly (stays in `Hrot.IG.Translators`).

Updated `IgSubsystem.Initialize()` to pass `new NedIgTranslators()` to `InitializeEmbedded`.

### Phase 7 (SKIPPED): Remove NED ref from Hrot.IG.csproj

Hrot.IG still references NED due to extensive non-translator NED usages:
- `ContextActionsUpdateTranslator` (Hrot.IG.Translators) — uses `Hrot.NED.Messages.ContextActionsUpdate`
- `WeaponFireIngressTranslator` (Hrot.IG.Translators) — uses `Hrot.NED.Messages.WeaponFire`, `IgWeaponFireEvent`
- `MiniExConPanelState.cs` — uses `Hrot.NED.Descriptors.EntityInfo`, `Hrot.NED.Messages.AttributeRecord`
- `MapCommandController.cs` — uses `Hrot.NED.Messages.MapCommandRequest`, `Hrot.NED.Common`
- `ContextMenuSystem.cs` — uses `Hrot.NED.Messages.ContextMenuRequest`
- `IgCapabilitiesPublisher.cs` — uses `Hrot.NED.Descriptors.IGCapabilitiesAnnounce`
- `IgApplication.cs` — uses `Hrot.NED.Descriptors.EntityInfo`, `Hrot.NED.Messages.AttributeRecord`

Phase 7 requires a dedicated batch to either move IG-event types to Hrot.Common (for translator
circular deps) or refactor the IG code to use neutral protocol types for DDS descriptors.

---

## Reference Verification

```
dotnet list Hrot.IG/Hrot.IG.csproj reference
--------------------
..\Hrot.Common\Hrot.Common.csproj
..\Hrot.Network.NED\Hrot.Network.NED.csproj   <-- still present (Phase 7 skipped)
..\Hrot.Map.Definitions\Hrot.Map.Definitions.csproj
..\Hrot.Presentation\Hrot.Presentation.csproj
..\FDP\Kernel\Fdp.Core\Fdp.Core.csproj
..\FDP\ModuleHost\Fdp.Network.Cyclone\Fdp.Network.Cyclone.csproj
..\FDP\Framework\Fdp.Presentation\Fdp.Presentation.csproj

dotnet list Hrot.CGF/Hrot.CGF.csproj reference
--------------------
..\Hrot.Network.Orchestration\Hrot.Network.Orchestration.csproj   <-- ADDED
..\Hrot.Network.NED\Hrot.Network.NED.csproj   <-- kept (Phase 1 partial)
..\Hrot.Common\Hrot.Common.csproj
..\FDP\ExtDeps\FastCycloneDds\src\CycloneDDS.Runtime\CycloneDDS.Runtime.csproj
..\FDP\Kernel\Fdp.Core\Fdp.Core.csproj

dotnet list Hrot.Orchestrator/Hrot.Orchestrator.csproj reference
--------------------
..\Hrot.Common\Hrot.Common.csproj
..\Hrot.Network.Orchestration\Hrot.Network.Orchestration.csproj   <-- ADDED
..\FDP\Kernel\Fdp.Core\Fdp.Core.csproj
..\FDP\ModuleHost\Fdp.Network.Cyclone\Fdp.Network.Cyclone.csproj
..\FDP\Toolkits\Fdp.Engine\Fdp.Engine.csproj
```

**Result:** Only `Hrot.Orchestrator` fully removed NED. CGF and IG still reference NED.

---

## Build Result

```
dotnet build IOS-IG-SimHost.sln -v quiet
    20 Warning(s)   (pre-existing, no new warnings introduced)
    0 Error(s)
```

**BUILD: 0 errors.**

---

## Test Results (key projects, run individually)

| Project | Passed | Failed | Total |
|---------|--------|--------|-------|
| Hrot.SimHost.Tests | 433 | 0 | 433 |
| Hrot.IG.Tests | 421 | 0 | 421 |
| Hrot.Network.NED.Tests | 54 | 0 | 54 |
| Hrot.Orchestrator.Tests | 88 | 0 | 88 |
| Hrot.ClusterRunner.Tests | 211 | 0 | 211 |
| Hrot.ExCon.Tests | 324 | 0 | 324 |

**Note:** Full solution parallel test runs showed intermittent failures in unrelated projects
(`Fdp.Examples.Scenarios.Tests`, `Fdp.Core.Tests`) due to native DLL loading contention under
parallel execution. All tests pass when run in isolation.

---

## Deferred Items

### DEBT-1: Complete CGF decoupling from NED
**Priority:** P2  
**Blocker:** `MissionControlExecutionSystem` lives in `Hrot.Network.NED/Systems/` with namespace
`Hrot.Common.Systems` but uses `Hrot.NED.Descriptors.MissionTrigger` (NED-specific).  
**Fix:** Move `MissionControlExecutionSystem` to `Hrot.Common/Systems/` and move its NED type
dependencies (`MissionTrigger`) to a protocol-neutral location.  
**Success:** `dotnet list Hrot.CGF reference` shows no `Hrot.Network.NED`.

### DEBT-2: Complete IG decoupling from NED (Phase 7)
**Priority:** P2  
**Root cause:** Multiple non-translator files in Hrot.IG use NED message/descriptor types:
- `ContextActionsUpdateTranslator` and `WeaponFireIngressTranslator` use IG event types
  (`ContextActionsUpdate`, `ContextAction`, `IgWeaponFireEvent`) that create circular deps if
  translators are moved to NED.
- `MiniExConPanelState`, `MapCommandController`, `ContextMenuSystem`, `IgCapabilitiesPublisher`,
  and `IgApplication` directly use NED DDS descriptor/message types.  
**Fix:**
  1. Move `IgWeaponFireEvent`, `ContextActionsUpdate`, `ContextAction` to `Hrot.Common`
  2. Move remaining 2 translators to `Hrot.Network.NED/IG/`
  3. Refactor IG files that use DDS types to use neutral types or receive them via injection
**Success:** `dotnet list Hrot.IG reference` shows no `Hrot.Network.NED`.
