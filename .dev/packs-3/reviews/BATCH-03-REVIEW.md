# BATCH-03 Review — Zone Service, Load Handlers, Save & Integration Test

**Batch:** BATCH-03  
**Reviewer:** Dev Lead  
**Review Date:** 2026-04-05  
**Verdict:** ✅ APPROVED

---

## Summary

All 4 tasks delivered. Solution builds clean (0 errors). All new tests pass. The full
Phase 2 zone-loading pipeline is end-to-end proven by `ZoneScenarioLoadIntegrationTests`.

---

## Scope Check

| Task | Implemented | Verified |
|------|-------------|---------|
| PACK3-Z003: `IZoneManagerService` + `ZoneManagerService` | ✅ | ✅ 4 unit tests (105/105 Hrot.Map.Common.Tests) |
| PACK3-Z004: `HrotScenarioLoadHandler` + `HrotEditLoadHandler` | ✅ | ✅ 2 unit tests + EditorFileIO regression (5/5) |
| PACK3-Z005: `ScenarioFileService.SaveScenario` with zones | ✅ | ✅ 2 unit tests (16/16 ScenarioEditor.Tests) |
| PACK3-Z006: `ZoneScenarioLoadIntegrationTests` (8 assertions) | ✅ | ✅ 47 ms, zero DDS calls |

---

## Design Alignment

- **PACK3-Z003**: `IZoneManagerService` interface shape matches spec. `ZoneManagerService` disposes using
  `ref var` pattern (correct for struct value type). Obstacles spawned with `PhysicsCollider` +
  `SimTransform`. `GetActiveZones` returns snapshot of last loaded zones. ✅
- **PACK3-Z004**: Single JSON parse pattern correctly implemented (`JsonNode.Parse` → `dom` →
  `dom.Deserialize<HrotScenarioEnvelopeDto>` → `_serializer.Deserialize(repo, dom)`).
  Null/absent `Zones` is a no-op. ✅
- **PACK3-Z005**: `SaveScenario` builds `HrotScenarioEnvelopeDto` with `WhenWritingNull` —
  empty zones dict → `Zones` key omitted from JSON. `ValidateSubsystemType` updated for
  camelCase output. ✅
- **PACK3-Z006**: All 8 spec assertions present and verified by test run (47 ms). ✅

---

## Notable Design Decision — Value-Type Dispose

The developer's choice to use `ref var existingRoad = ref existingZed.RoadNetwork` to dispose
the `RoadNetworkBlob` in the actual singleton backing store (rather than a defensive copy) is
the only correct approach for a struct with `NativeArray` fields. The spec test was
"unobservable via copy" and the developer adapted the test accordingly. The production
implementation is correct. ✅

---

## Issues Found During Review

### P3 (deferred)

1. **Multi-zone road network conflict**: Multiple zones with different `RoadNetworkPath` values
   clobber each other in the singleton (last one wins). Spec says one road network per zone —
   this is by design and acceptable, but should be documented.
2. **`ScenarioFileService.LoadScenario` silent zone fallback**: If injected without
   `IZoneManagerService`, zone data is silently discarded. A warning log would improve
   diagnosability.

---

## Debt Tracker Entries

| Priority | Description | Source |
|----------|-------------|--------|
| P3 | `ScenarioFileService.LoadScenario` silently discards zone data when no `IZoneManagerService` is injected. Add a warning log. | BATCH-03 review |

---

## Git Commit Verification

Developer self-committed: `61c43f2` and `63fb379`. All BATCH-03 changes confirmed in repo.

---

## Next Actions

- ✅ BATCH-03 committed by developer
- ✅ Update DEBT-TRACKER.md with P3 entry
- ✅ Mark PACK3-Z003, Z004, Z005, Z006 done in TASK-TRACKER.md (confirm already updated)
- ➡️ Create BATCH-04: PACK3-A001–A005 (Phase 3 ACL Backdoor Elimination) + PACK3-N004 (NetworkGateway Integration Test)
