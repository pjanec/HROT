# DEBT-TRACKER.md — packs-2

**Project:** Scenario Editor Pack & HROT Editor Refactoring  
**Updated:** 2026-04-04

---

## Legend

| Priority | Meaning |
|----------|---------|
| P1 | Blocking — must fix in current or immediate next batch |
| P2 | Important — should fix within 2 batches |
| P3 | Minor — fix when in the area |

✅ = Resolved

---

## Open Debt Items

| ID | Priority | Description | Source | Target Batch |
|----|----------|-------------|--------|-------------|
| DEBT-01 | P3 | `CgfLogicPackTests` and `OrchestrationLogicPackTests` live in `Hrot.SimHost.Tests`. Should move to dedicated test projects when those exist. | BATCH-01 review | Backlog |
| DEBT-02 | P3 | `CgfLogicPack.RegisterSystems(ISystemRegistry)` and `SimHostCoreLogicPack.RegisterSystems(ISystemRegistry)` are no-ops. Must document that callers must use the `SystemGroup` overload, or these packs will silently register nothing if the kernel path changes. | BATCH-01 report Q5 | BATCH-02 |

---

## Resolved Debt Items

*(None yet)*
