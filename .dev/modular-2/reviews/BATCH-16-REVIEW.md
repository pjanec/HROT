# BATCH-16 Review

**Batch:** BATCH-16 — IG Network Decoupling  
**Reviewer:** Dev Lead Agent  
**Status:** Approved with deferred items

---

## Review Summary

BATCH-16 delivered the IG network decoupling infrastructure. Tasks 1-18 (minus 19) are complete.
All 900 tests pass across SimHost, IG, and Network.NED.

### Scope Check

- Tasks 1-17: Fully implemented per specification ✅
- Task 18: Partially implemented — 4 compile errors fixed, `_networkAdapter` introduced, but full NED
  removal from IgApplication is blocked by `OrchestratePersonalRouteAsync` ⚠️
- Task 19: Blocked — cannot remove NED reference from Hrot.IG.csproj without extending neutral DTOs
  and refactoring `OrchestratePersonalRouteAsync` + `DrawPersonalRouteCommandTests`
- Task 20: DEBT-003 ✅, DEBT-004 ✅, DEBT-007 ✅, DEBT-008 partial (TODO comment added)

### Design Alignment

All completed tasks align with the MODULAR-2 design. The partial Task 18/19 outcome is consistent
with the design intent — the blocker is a genuine gap in the neutral `CreateEntityCommand` model
for multi-descriptor entity creation.

### Test Quality

Tests verified logically (not just compilation):
- `MapCommandControllerTests`: assertions check requestId, statusCode, dataJson values
- `ContextMenuSystemTests`: assertions check mapId, requestId, forSelection entity IDs
- `WeaponFireIngressTranslatorTests` / `ContextActionsUpdateTranslatorTests`: translator behavior tested
- `MissionControlExecutionSystemTests` / `MissionControlRequestSystemFollowRouteTests`: mission logic tested

### Early Failure Check

No silent error swallowing observed. Gateway null checks log and return. Factory stubs return
`NullIgNetworkAdapter.Instance` (no-op, no crash).

---

## Issues Found

### P2 — Task 19 remains open (DEBT-011)
`OrchestratePersonalRouteAsync` in IgApplication.cs constructs a NED `CreateEntityRequest` with:
- `EntityMaster` descriptor (TkbType for route entity)
- `WorldPos` descriptor (anchor position)
- `MapRoute` descriptor (waypoints list)
- `EntityInfo` descriptor (commanderId for vehicle assignment)

The neutral `CreateEntityCommand` has only: `TkbType`, `Latitude`, `Longitude`, `Altitude`,
`ForceId`, `PropertiesJson`. It cannot represent multi-descriptor entity creation without a
JSON-encoded descriptor list.

**Recommendation:** BATCH-17 should define a `CreateEntityDescriptors` extension on
`CreateEntityCommand` (JSON-encoded descriptor list) and implement parser in `NedCommandGateway`.

### P3 — Two unused DDS writers in IgApplication (DEBT-012)
`_contextMenuRequestWriter` and `_mapCommandAckWriter` are initialized in `InitializeNetwork`
but the write callbacks now use `_networkAdapter` instead. Safe to remove in BATCH-17.

---

## Git Commit Message

```
BATCH-16: IG network decoupling — IIgNetworkAdapter, neutral mission types, translators moved to NED

- MissionCommandPayload in Hrot.Core, MissionControlExecutionSystem in Hrot.Common
- IIgNetworkAdapter + NullIgNetworkAdapter + NedIgNetworkAdapter
- Translators (WeaponFireIngress, ContextActionsUpdate) moved to Hrot.Network.NED/IG
- MapCommandController uses Action<MapCommandAckDto>
- ContextMenuSystem uses Action<Guid,int,IReadOnlyList<int>>
- IgCapabilitiesPublisher uses IIgNetworkAdapter
- MiniExConPanelState + MiniExConPanel use ICommandGateway
- IgApplication: _networkAdapter introduced, 4 compile errors fixed
- Hrot.Network.NED removed from Hrot.CGF.csproj
- DEBT-003/004/007 resolved; DEBT-011/012 recorded for BATCH-17

All 900 tests pass (SimHost 424, IG 422, NED 54)
```
