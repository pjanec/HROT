# BATCH-16 Report — Modular-2 Phase: IG Network Decoupling

**Date:** 2026-01-01  
**Batch:** BATCH-16  
**Status:** Completed (Task 19 partially blocked — see notes)

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
| 18 | `IgApplication.cs` — 4 compile errors fixed, `_networkAdapter` introduced | Partial |
| 19 | Remove Hrot.Network.NED from `Hrot.IG.csproj` | **Blocked** |
| 20 | P3 Debt: DEBT-003 (ps1 deleted), DEBT-004 (verified), DEBT-007 (DDS test collection) | Done |

---

## Test Results

- `Hrot.SimHost.Tests`: 424 passed, 0 failed
- `Hrot.IG.Tests`: 422 passed, 0 failed
- `Hrot.Network.NED.Tests`: 54 passed, 0 failed
- Solution build: 0 errors (excluding environment-locked `Fdp.Examples.UrbanCombat`)

---

## Issues Encountered

### 1. DEBT-008: UpdateEntityDescriptorRequest lacks DescriptorJson
The BATCH-16 instructions said to add `DescriptorJson = cmd.DescriptorJson` to
`NedTranslationHelper.ToUpdateDescriptorRequest`, but the DDS type `UpdateEntityDescriptorRequest`
has `Payload` (EntityDescriptorUnion), not `DescriptorJson`. The field doesn't exist.
Resolution: Added a TODO comment; DEBT-008 remains open with a note about the required
JSON-to-EntityDescriptorUnion translation.

### 2. Task 19 Blocked: OrchestratePersonalRouteAsync deeply uses NED types
`IgApplication.OrchestratePersonalRouteAsync` constructs a complex `CreateEntityRequest`
with `EntityDescriptorUnion` list (EntityMaster, WorldPos, MapRoute, EntityInfo). The neutral
`CreateEntityCommand` only supports flat entity creation (TkbType, lat/lon/alt, ForceId) and
cannot represent route entity creation with multi-descriptor data.

`DrawPersonalRouteCommandTests.MockGateway` implements `INedCommandGateway` and tracks NED
`CreateEntityRequest` / `MissionControlRequest`, asserting descriptor contents.

Additionally, `SendGeoSpatialUpdate` uses `INedCommandGateway.SendUpdateDescriptor(UpdateEntityDescriptorRequest)`
which carries the full `WorldPos` payload. The neutral `ICommandGateway.SendUpdateDescriptorAsync`
stub in `NedCommandGateway` does not populate the `Payload` field (DEBT-008).

These blocks mean full NED removal from Hrot.IG requires a dedicated batch with:
- Extending neutral DTOs for complex route entity creation
- Fixing the JSON-to-EntityDescriptorUnion translation in NedTranslationHelper
- Updating DrawPersonalRouteCommandTests to track neutral types

### 3. IgCapabilitiesPublisher missing closing brace
The previous write of IgCapabilitiesPublisher.cs (Task 16) had a missing `}` closing the
try block. Fixed in this batch.

### 4. MissionTrigger ambiguity in test files
Both `FDP.Toolkit.Behavior.Components.MissionTrigger` and `Hrot.Core.Mission.MissionTrigger`
exist. Resolved by using fully qualified type `Hrot.Core.Mission.MissionTrigger`.

---

## Design Decisions

1. **Minimal Task 18**: Rather than full NED removal from IgApplication, added `_networkAdapter`
   field and fixed the 4 specific compile errors caused by Tasks 14-16. This keeps the
   `DrawPersonalRoute` command path working and avoids breaking existing tests.

2. **FakeCacheMissCallback**: Replaced `FakeDdsWriter<ContextMenuRequest>` in tests with a
   tuple-capturing callback. Tuple named properties match original field names so assertions
   remain readable.

3. **IgApplication._networkAdapter + existing DDS fields**: Both coexist temporarily.
   `_networkAdapter` handles capabilities publishing and the neutral write callbacks.
   Remaining DDS fields (`_clickWriter`, `_selectionWriter`, etc.) are used by code paths
   not yet neutralized.

---

## Weak Points Spotted

1. **NedTranslationHelper.ToUpdateDescriptorRequest incomplete**: DEBT-008 remains unresolved.
   The method sets `DescriptorType = dtEntityMaster` hardcoded even for geo updates. The
   `Payload` field is never populated. Any code using `SendUpdateDescriptorAsync` through the
   neutral `ICommandGateway` interface will send an incomplete update.

2. **OrchestratePersonalRouteAsync dual-path complexity**: This method constructs NED
   descriptors directly, bypassing the neutral gateway. The gap between `CreateEntityCommand`
   (flat) and `CreateEntityRequest` (multi-descriptor) needs architectural decision.

3. **_contextMenuRequestWriter and _mapCommandAckWriter still initialized**: These DDS
   writers are now unused (callbacks use `_networkAdapter` instead) but still allocated in
   `InitializeNetwork`. They can be removed in the next batch.
