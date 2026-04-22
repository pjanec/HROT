# Time Control Phase 2 — Technical Debt Tracker

**Project:** `time-ctrl-2`  
**Updated:** 2026-04-02

---

## Legend

| Priority | Meaning |
|----------|---------|
| P1 | Blocking — must be fixed in next batch (never sits here long) |
| P2 | Important — should be fixed within 1-2 batches |
| P3 | Nice-to-have — address when convenient |

| Status | Meaning |
|--------|---------|
| 🔴 Open | Not yet addressed |
| 🟡 In Progress | Scheduled for upcoming batch |
| ✅ Resolved | Fixed and verified |

---

## Items

| ID | Priority | Status | Source Batch | Description | Target Batch |
|----|----------|--------|--------------|-------------|---|
| TD-001 | P3 | 🔴 Open | BATCH-01 | TC2-P2-T3: Wire slave time controllers into SimHost/IG `ClusterUiCache`. N/A — neither SimHost nor IG has a `ClusterUiCache`. Closed as not applicable. | N/A |
| TD-002 | P3 | 🔴 Open | BATCH-02 | Dead interface surface on `IExConLogic`: `MasterSimTime`, `MasterWallTicks`, `MasterTimeScale`, `IsPaused` have zero live consumers. Remove in a future cleanup. | Future |
| TD-003 | P2 | 🔴 Open | BATCH-02 | `FdpEventBus` instances created in subsystems are set to null in Shutdown without calling `Dispose()`. Resource leak. | Future |
| TD-004 | P3 | 🔴 Open | BATCH-02 | `IDescriptorTranslator.PollIngress(null!, null!)` null-forgiving args is a compiler lie. Translators should expose a no-arg overload or accept nullable params. | Future |
| TD-005 | P3 | 🔴 Open | BATCH-02 | TC2-P3-T2-SC3 in-process relay mode-switch test not implemented. Low-priority addition to ExConSubsystemTests. | Future |

