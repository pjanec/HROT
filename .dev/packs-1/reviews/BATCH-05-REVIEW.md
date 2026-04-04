# BATCH-05 Review

**Batch:** BATCH-05
**Reviewer:** Dev Lead
**Date:** 2025-07-15
**Verdict:** ✅ APPROVED

---

## Review Checklist

| Area | Status | Notes |
|------|--------|-------|
| PACK-E001 implementation | ✅ Pass | `ClusterScenarioPanel` DDS writer removed; `FdpEventBus` + `ClusterOpIntent` pipeline wired |
| PACK-E002 implementation | ✅ Pass | `MissionEditorService` DDS writer/reader removed; full bus pipeline implemented |
| New translators | ✅ Pass | `ClusterOpEgressTranslator`, `MissionControlEgressTranslator`, `MissionControlAckIngressTranslator` all cleanly ACL-scoped |
| `MissionControlCqrsEvents` relocation | ✅ Pass | Canonical location moved to `Hrot.Common/Events/`; `Hrot.SimHost/Events/` contains `global using` re-exports |
| `ClusterCqrsEvents` extension | ✅ Pass | `ClusterOpIntent [EventId(9018)]` added without collision |
| Test coverage | ✅ Pass | ClusterRunner 179/182, ExCon 347/347 |
| Pre-existing failures | ✅ Acknowledged | 3 DDS-timing failures pre-date this work; documented since BATCH-01 |

---

## Per-Task Review

### PACK-E001 — ClusterScenarioPanel DDS Removal

**Verdict: APPROVED**

Core objective achieved: `DdsWriter<ClusterOpRequest>` constructor parameter and field removed; `SendRequest(string, string)` now publishes `ClusterOpIntent` to `FdpEventBus`.

**Accepted deviation:** `ClusterScenarioPanel.cs` line 132 retains a `System.Text.Json.JsonDocument.Parse(metaJsonContent)` call inside the private `GetReplayDuration()` method. This reads local scenario `meta.json` file metadata (replay duration for UI display) — it has no relation to DDS command serialization. The spec intent was to remove the JSON serialization of DDS command payloads (`PayloadJson` on the egress path). This local metadata file-read is an unrelated utility and its removal was not required.

The XML doc comment on line 19 references `DdsWriter{T}` textually — this is pre-existing documentation that should be updated in a future housekeeping pass (not blocking).

### PACK-E002 — MissionEditorService DDS Removal

**Verdict: APPROVED**

`MissionEditorService.cs` has zero DDS references (`IDdsWriter`, `DdsWriter`, `DdsReader`, `DdsParticipant` — all clear). Full bus pattern implemented. `IMissionEditorService` contract cleaned of `OnAckReceived(MissionControlAck)`. All 347 ExCon tests pass.

---

## Test Results Summary

| Project | Passed | Failed | Total | Notes |
|---------|--------|--------|-------|-------|
| `Hrot.ClusterRunner.Tests` | 179 | 3 | 182 | 3 pre-existing DDS-timing failures |
| `Hrot.ExCon.Tests` | 347 | 0 | 347 | ✅ |

---

## Debt Items from This Batch

No new P2 items. Translator implementations are clean.

---

## Decision

All BATCH-05 tasks are complete and verified. Committing and proceeding to BATCH-06 (Phase 7).
