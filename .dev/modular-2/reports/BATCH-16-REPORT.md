# BATCH-16 Report — Modular-2 Phase: IG Network Decoupling

**Date:** 2026-01-01  
**Batch:** BATCH-16 (+ BATCH-17 Priority 1 completion)
**Status:** Task 18 complete; Task 19 partially complete (NED reference remains pending full factory refactor)

---

## Tasks Completed

| ID | Description | Status |
|----|-------------|--------|
| 1 | `MissionCommandPayload` in `Hrot.Core/Mission/MissionTypes.cs` | Done |
| 2 | `MissionControlCqrsEvents.cs` in `Hrot.Core/Events/` | Done |
| 3 | `MissionTriggerHelper.cs` in `Hrot.Core/Mission/` | Done |
| 4 | `MissionControlIngressTranslator.cs` updated | Done |
| 5 | `MissionControlExecutionSystem.cs` moved to `Hrot.Common/Systems/` | Done |
| 6 | Hrot.Network.NED removed from `Hrot.CGF.csproj` | Done |
| 7 | SimHost test files updated (`MissionControlExecutionSystemTests.cs`, `MissionControlRequestSystemFollowRouteTests.cs`, `MissionControlRequestSystemTests.cs`) | Done |
| 8 | `IIgNetworkAdapter` + `NullIgNetworkAdapter` in `Hrot.Core/Network/` | Done |
| 9 | `NedIgNetworkAdapter` in `Hrot.Network.NED/IG/` | Done |
| 10 | `CreateIgNetworkAdapter` added to `INetworkFactory` | Done |
| 11 | All 4 factory implementations updated | Done |
| 12 | `IgCommonEvents.cs` + `ContextAction.cs` moved to `Hrot.Common` | Done |
| 13 | Both IG translators moved to `Hrot.Network.NED/IG/`; `NedIgTranslators` updated | Done |
| 14 | `MapCommandController.cs` — neutral `Action<MapCommandAckDto>` callback | Done |
| 15 | `ContextMenuSystem.cs` — neutral `Action<Guid, int, IReadOnlyList<int>>` callback | Done |
| 16 | `IgCapabilitiesPublisher.cs` — uses `IIgNetworkAdapter` | Done |
| 17 | `MiniExConPanelState.cs` — uses `ICommandGateway?` throughout | Done |
| 18 | `IgApplication.cs` — full refactor: `Hrot.NED.*` usings removed, DDS fields removed, neutral adapter pattern throughout, `OrchestratePersonalRouteAsync` uses `IIgNetworkAdapter.CreateRouteEntityAsync`, `SendGeoSpatialUpdate` uses neutral JSON path | Done |
| 19 | Remove Hrot.Network.NED from `Hrot.IG.csproj` | **Not yet done** (see notes) |
| 20 | P3 Debt: DEBT-003 (ps1 deleted), DEBT-004 (verified), DEBT-007 (DDS test collection) | Done |

### BATCH-17 Priority 1 completions (applied in this session)

| ID | Description | Status |
|----|-------------|--------|
| B17-1 | `IIgNetworkAdapter.CreateRouteEntityAsync` added to interface + `NullIgNetworkAdapter` | Done |
| B17-2 | `NedIgNetworkAdapter.CreateRouteEntityAsync` implemented (builds NED 4-descriptor request) | Done |
| B17-3 | `OrchestratePersonalRouteAsync` refactored to use `CreateRouteEntityAsync` + neutral `MissionControlCommand` | Done |
| B17-4 | `DrawPersonalRouteCommandTests` rewritten with `MockNetworkAdapter : IIgNetworkAdapter` | Done |
| B17-5 | `TestHook_SetNetworkAdapter` added to `IgApplication` | Done |
| B17-6 | `SendGeoSpatialUpdate` uses neutral JSON path; `NedTranslationHelper.ToUpdateDescriptorRequest` parses JSON (DEBT-008 fixed) | Done |
| B17-6a | Test hooks `TestHook_InjectGeoSpatialDescriptor` and `TestHook_InjectEntityMasterDescriptor` changed to neutral `(int, double, ...)` signatures | Done |
| B17-7t | Test files updated for neutral hook signatures: `SetViewCommandTests`, `SetSelectionCommandTests`, `GhostPromotionTests`, `ContinuousDragTests` | Done |

---

## Test Results

- `Hrot.SimHost.Tests`: 424 passed, 0 failed
- `Hrot.IG.Tests`: 422 passed, 0 failed
- `Hrot.Network.NED.Tests`: 54 passed, 0 failed
- Solution build: 0 errors

---

## Issues Encountered

### 1. DEBT-008 (NedTranslationHelper.ToUpdateDescriptorRequest) — RESOLVED
`NedTranslationHelper.ToUpdateDescriptorRequest` now parses the `DescriptorJson` field
from `UpdateEntityDescriptorCommand` to build the `WorldPos` `EntityDescriptorUnion` payload.
JSON format: `{"type":"WorldPos","entityId":NNN,"lat":D,"lon":D,"alt":D}`.

### 2. Task 19 NOT completed: IgApplication still has NED production dependencies
BATCH-17 Priority 1 (Tasks 1-6) unblocked `OrchestratePersonalRouteAsync` and
`SendGeoSpatialUpdate`, completing the neutral-type refactor for those paths.
However, `IgApplication.cs` still has NED references that prevent csproj removal:

- `using Hrot.Network.Infrastructure;` for `.WithReplication()` (line 594) and
  `.BindReplicationParticipant()` (line 763) — both NED extension methods
- `new Hrot.Network.NED.IG.NedIgNetworkAdapter(...)` direct instantiation (line 784)
- `Hrot.Map.Common.Replication.Egress.*Translator` (lines 809-811) — three NED egress translators
- `IgSubsystem.cs` still creates `new Hrot.Network.NED.IG.NedIgTranslators()` directly

Removing `Hrot.Network.NED` from `Hrot.IG.csproj` would require:
1. Adding `INetworkFactory?` parameter to `InitializeEmbedded` (BATCH-16 Step A)
2. Moving `NedIgNetworkAdapter` creation to `networkFactory.CreateIgNetworkAdapter(...)` (Step B)
3. Replacing `.WithReplication()` with factory-provided replication setup (Step B 2nd part)
4. Moving egress translators into `NedIgTranslators.GetTranslators` (BATCH-16 Step D/I)
5. Updating `IgSubsystem.cs` per BATCH-16 Step K to pass `NedNetworkFactory` from outside
6. Removing dead `using` statements for `Hrot.Map.Common.Commands/Replication/Replication.Ingress`

This scope is tracked as DEBT-011 and should be done in the next batch iteration.

### 3. IgCapabilitiesPublisher missing closing brace (RESOLVED in prior session)
The previous write of IgCapabilitiesPublisher.cs (Task 16) had a missing `}` closing the
try block. Fixed in this batch.

### 4. MissionTrigger ambiguity in test files (RESOLVED in prior session)
Both `FDP.Toolkit.Behavior.Components.MissionTrigger` and `Hrot.Core.Mission.MissionTrigger`
exist. Resolved by using fully qualified type `Hrot.Core.Mission.MissionTrigger`.

---

## Design Decisions

1. **Task 18 full completion**: All `Hrot.NED.*` type usages removed from `IgApplication.cs`.
   DDS reader/writer fields replaced with `_networkAdapter` polling. `OrchestratePersonalRouteAsync`
   uses `IIgNetworkAdapter.CreateRouteEntityAsync` + neutral `MissionControlCommand`. Test hooks
   use neutral `(int entityId, double lat, ...)` signatures instead of NED struct parameters.

2. **BATCH-17 Tasks 1-6 implemented proactively**: These tasks directly resolve the BATCH-16 blocker
   and were applied as part of completing Task 18. `IIgNetworkAdapter.CreateRouteEntityAsync` extends
   the neutral interface; `NedIgNetworkAdapter` implements it by building the NED multi-descriptor
   `CreateEntityRequest`. This keeps NED-specific logic in the NED layer.

3. **Neutral test hook signatures**: `TestHook_InjectGeoSpatialDescriptor(int, double, double, double, float)`
   and `TestHook_InjectEntityMasterDescriptor(int, long, ulong)` avoid leaking NED descriptor types
   into test callers while preserving full test coverage.

4. **Task 19 deferred**: Complete removal of NED from `Hrot.IG.csproj` requires implementing the full
   `INetworkFactory` pattern for `InitializeEmbedded` (BATCH-16 Steps A, B, K), moving egress translators
   into the translator provider (Steps D/I), and handling the `.WithReplication()` extension method.
   This is tracked as DEBT-011. The Hrot.NED.* using directives are gone; remaining dependencies are
   infrastructure-level (factory pattern completion).

---

## Weak Points Spotted

1. **IgApplication still has NED production dependencies**: See Issue 2 above. DEBT-011 tracks the
   remaining factory pattern work needed before Task 19 can complete.

2. **`using Hrot.Map.Common.Commands/Replication/Replication.Ingress` dead imports**: These `using`
   statements in `IgApplication.cs` and `MiniExConPanelState.cs` reference namespaces defined in
   `Hrot.Network.NED.dll`. They are not referenced by any active code (dead imports left from before
   Task 18 refactoring). They do not cause errors when NED is in the csproj, but will need to be
   removed when Task 19 is completed.

3. **`Hrot.IG.Tests` still references `Hrot.Network.NED`**: The test project keeps NED reference for
   `TkbEntityTypes` constants used in assertions. This is expected and acceptable.
