# DEBT-TRACKER.md — packs-2

**Project:** Scenario Editor Pack & HROT Editor Refactoring  
**Updated:** 2025-07-16

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
| DEBT-03 | P2 | `_prebuiltRequests` in `MapCommandController` is unbounded — no TTL or eviction. Under sustained network outage, grows without limit. | BATCH-02 report Q4 | BATCH-04 |
| DEBT-04 | P2 | `NetworkSpawningSystem` in the IG consumes EGRESS `SpawnEntityCommand` events, potentially creating spurious ghost entities. Architecturally fragile; requires INGRESS/EGRESS bus partition or discriminant field. | BATCH-02 report Q4 | BATCH-05 |
| DEBT-05 | P2 | No standalone unit test files for `SpawnEntityCommandEgressTranslator` or `DestroyEntityCommandEgressTranslator`. D005 success criteria 1 & 2 covered only by integration-level tests. Add `SpawnEntityCommandEgressTranslatorTests.cs` and `DestroyEntityCommandEgressTranslatorTests.cs` to `Hrot.Map.Common.Tests`. | BATCH-02 review | BATCH-03 |
| DEBT-06 | P3 | `UpdateEntityCommandEgressTranslator` silently drains `UpdateEntityCommand` with `RoutePlan` without DDS write (intentional but invisible). Must re-evaluate if route editing moves to a new module in Phase 2. | BATCH-02 report Q4 | Backlog |

---

## Resolved Debt Items

*(None yet)*
