# BATCH-04 Review

**Batch:** BATCH-04
**Tasks:** DEBT-006 (corrective), PACK-C001, PACK-C002
**Verdict:** ✅ APPROVED

---

## Verification Summary

| Project | Result |
|---------|--------|
| `Hrot.Orchestrator.Tests` | ✅ 0 failed / 88 passed |
| `Hrot.ClusterRunner.Tests` | ✅ 192/195 (3 pre-existing DDS-timing failures unchanged) |
| `dotnet build IOS-IG-SimHost.sln` | ✅ 0 errors |

## Task Verification

### DEBT-006 Corrective ✅
- `MissionControlRequestSystem.cs` deleted; zero workspace references found.

### PACK-C001 ✅
- `ClusterMaster.cs` — DDS references are **comments only** (one XML doc comment on line 398); all code is bus-based.
- `AssetInventoryUpdateEvent [EventId(9017)]` defined and published from `PublishAssetInventory()`.
- `ClusterOpMasterTranslator` consumes `AssetInventoryUpdateEvent` → DDS write.
- 88/88 orchestrator tests passing, including ACK processing and fan-out logic.

### PACK-C002 ✅
- `ClusterUiCache.cs` — zero `DdsReader`/`DdsWriter`/`DdsParticipant` references (grep confirmed).
- `SystemStateUpdateEvent [EventId(9016)]` defined.
- `OrchestrationObserverTranslator` created in `Hrot.Common/Orchestration/`.
- ExCon wiring updated to 3-component pattern.

## Accepted Design Decisions

1. **`NodeOpSlaveTranslator.DeserializeNodePayload` made `internal static`** — avoids duplication
   in `OrchestrationObserverTranslator`. Strictly additive.
2. **`ForgetEpisode` case added to `DeserializeNodePayload`** — fixes a missing case in the switch,
   strictly a bug fix.
3. **`OrchestrationObserverTranslator` uses `PublishManaged` for string-bearing events** — correct
   since `NodeHeartbeatEvent.SubsystemName` is a managed string.

## Issues / Debt Recorded

### P3 — OrchestratorSubsystem SwitchTimeMode bridge (DEBT-010)
`OrchestratorSubsystem.Update()` bridges `SwitchTimeModeEvent` between two buses per-frame.
If unified into a single bus, the extra copy could be eliminated. Low priority.

### P3 — `OrchestrationObserverTranslator.Tick()` parses JSON per frame (DEBT-011)
`DeserializeStringArray()` runs on every tick even if inventory unchanged. A version/hash
check could short-circuit. Not on a hot path. Low priority.

---

## Suggested Git Commit Messages

### Main repo
```
feat(packs-1): BATCH-04 — Phase 5 Orchestration CQRS Cleanup

DEBT-006: Delete vestigial MissionControlRequestSystem
PACK-C001: Purify ClusterMaster — remove DDS constructors; add AssetInventoryUpdateEvent (9017);
           update ClusterOpMasterTranslator
PACK-C002: Purify ClusterUiCache (zero DDS); create OrchestrationObserverTranslator;
           add SystemStateUpdateEvent (9016); update ExCon wiring

Tests: Orchestrator 88/88, ClusterRunner 192/195 (3 pre-existing DDS-timing failures).
```
