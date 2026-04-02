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
| TD-001 | P3 | 🔴 Open | BATCH-01 | TC2-P2-T3: Wire slave time controllers into SimHost/IG `ClusterUiCache`. Requires verifying `ModuleHostKernel.GetTimeController()` availability at `_uiCache` construction point in each subsystem. | BATCH-02 or later |

