# BATCH-01 Review

**Status:** ✅ APPROVED  
**Reviewer:** Dev Lead  
**Date:** 2026-04-05

---

## Scope Check

All three tasks delivered and verified:
- **EDIT1-L001** — `Hrot.UI.Common` project with all 9 interfaces and 3 DTOs ✅
- **EDIT1-L002** — `DoctrineCatalog` + 5 new `TkbEntityTypes` constants ✅
- **EDIT1-L003** — `DoctrineRegistry.GetRegisteredNames()` ✅

## Design Alignment

- Port interfaces correctly named, namespaced under `Hrot.UI.Common.Facades`, zero `Hrot.ExCon` / DDS references ✅
- `IMapPickService` has exactly 3 methods (`PickLocationAsync`, `PickEntityAsync`, `PickAreaEntitiesAsync`) ✅
- `IOrbatController` has `RequestEmbark(int, int)` and `RequestDisembark(int)` ✅
- `DoctrineCatalog` uses static readonly backing fields — no per-call allocation ✅
- `TkbEntityTypes` constants `CivilianPedestrian`–`Insurgent` at 501–505 (non-colliding) ✅

## Test Quality

8 tests — all meaningful behavior checks, no shallow "can create object" tests.  
`ReferenceEquals` test for allocation invariant is excellent.  
Pre-existing 3 failures in `Hrot.ClusterRunner.Tests` are confirmed unrelated (timing tests). ✅

## Debt Items Recorded

| P Level | Description | Target |
|---------|-------------|--------|
| P3 | `TkbEntityTypes` in `Hrot.Map.Definitions` uses `Hrot.Map.Common` namespace — misleading | Cleanup pass |
| P3 | `DoctrineRegistry` has no `Freeze()` guard against post-startup registrations | Future hardening |

## Suggested Commit Message

Used: `feat(edit-1/BATCH-01): Hrot.UI.Common foundation + DoctrineCatalog + DoctrineRegistry.GetRegisteredNames`

---

## Next Batch: BATCH-02 — Panel Migration (Phase 1) + New Panels (Phase 2)

Tasks to include: EDIT1-P001, EDIT1-P002, EDIT1-P003, EDIT1-N001, EDIT1-N002, EDIT1-N003, EDIT1-N004
