# BATCH-01 Review

**Batch:** BATCH-01 (BSA-102 — Unified attach/detach seam in core)  
**Reviewer:** Development Lead  
**Date:** 2026-06-09  
**Status:** ✅ APPROVED

---

## Summary

Created `BlueprintInstanceService` in `Fdp.Toolkits.Blueprints` (core) with `AttachToEntity` (by `blueprintId`) and `DetachFromEntity`. Reduced editor `BlueprintAttachService` to a 14-line forwarder. All 22 relevant tests pass, 0 net-new failures.

---

## Issues Found

No issues found. The implementation matches the design spec exactly.

---

## Test Quality Assessment

**Tests verified by running and reading source code.** All 22 tests pass (15 core/forwarder + 7 RunBlueprintOnEntityCommand).

| Test | Verifies | Quality |
|------|----------|---------|
| SC1 FreshAttach | Attach allocates slot + InitDefault zeroes Count | ✅ Concrete value assertion |
| SC2 SecondCall | Idempotent re-attach → AlreadyAttached, single slot | ✅ Count assertion |
| SC3 UnregisteredId | NotRegistered + no tier component added | ✅ Status + HasComponent |
| SC4 LibraryKind | NotInstanceKind + no tier component | ✅ Status assert |
| SC5 Detach+DenseCompact | Attach A,B,C; detach B → count=2, A/C present, B absent | ✅ Multi-step integration |
| SC6 AbsentDetach | Returns false, no throw | ✅ Bool assert |
| SC7 Attach→Tick | E2E with BlueprintTestFixture, counter advances | ✅ Real production path |
| SC8 Forwarder=Core | Separate entities, both paths produce identical results | ✅ Cross-seam comparison |

All tests drive real production paths, assert concrete values, no mocks for the unit under test.

8 pre-existing failures confirmed unrelated (compiler golden/PDB/ALC/allocation-free/perf tests — none reference `BlueprintAttachService` or `BlueprintInstanceService`).

---

## Verdict

**✅ APPROVED.** All requirements met. Ready to merge.

---

## 📝 Commit Message

```
feat: BSA-102 move attach/detach seam to core (BlueprintInstanceService)

Create BlueprintInstanceService in Fdp.Toolkits.Blueprints with:
- AttachToEntity(world, registry, blueprintId, entity) → BlueprintAttachResult
- DetachFromEntity(world, blueprintId, entity) → bool (dense-compacts)
- BlueprintAttachStatus enum + BlueprintAttachResult record (moved from editor)
- ChooseTier(stateSize) → BlackboardTier

Reduce Hrot.Blueprints.Editor BlueprintAttachService to thin forwarder:
  BlueprintIdHash.Compute(asset.AssetId) → core seam.

No assembly reference from Fdp.Toolkits to Hrot.Blueprints.Editor.
All type references migrated transparently (11 callers, 0 code changes needed).

Tests: 22 new/updated tests (8 core seam + 7 forwarder regression + 7
command integration), all passing. 0 net-new test failures.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
```

---

**Next Batch:** BSA-101 + BSA-202 (mark blackboard components `NoSave` + `BlueprintStateTranslator`) — per implementation order: "(BSA-101 + BSA-202 together)"
