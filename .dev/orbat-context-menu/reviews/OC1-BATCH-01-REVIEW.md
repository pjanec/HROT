# OC1-BATCH-01 Review

**Batch:** OC1-BATCH-01
**Reviewer:** Development Lead
**Date:** 2026-03-22
**Status:** ✅ APPROVED

---

## Summary

The developer successfully fixed four blocking authoring pipeline and deletion bugs (Phase 0) and added the fundamental `CMD_DRAW_PERSONAL_ROUTE` command contract (Phase 1). Code is clean and well-tested, addressing both the symptoms and the root architectural gaps (event unsubscription in Dispose, relaxing incorrect static guards in test paths). No issues found. 

---

## Issues Found

No issues found.

---

## Verdict

**Status:** APPROVED

**All requirements met. Ready to merge.**

---

## 📝 Commit Message

```
fix: route/area authoring and entity deletion pipelines (OC1-BATCH-01)

Completes OC1-B001, OC1-B002, OC1-B003, OC1-B004, OC1-C001

Resolves blocking issues in the IG/IOS authoring workflows and inspector state
to unblock the ORBAT Context Menu features.

IG (MapCommandController / IgApplication):
- Fix Route tool activation (OC1-B001): calls BeginAreaAuthoringSession before pushing PointSequenceTool.
- Fix Area tool testability and coordinate logic (OC1-B003): relaxes _createEntityDdsWriter guard.

IOS (ConfigPanel / IosLogic):
- Fix Layers (OC1-B002): Renamed checkbox to "Routes" (preserving road_graphs JSON schema).
- Fix Inspector (OC1-B004): Subscribes to Repo.EntityDeleted in constructor, unsubscribes in Dispose, clearing SelectedEntityId.

DDS (DataModel):
- Adds CMD_DRAW_PERSONAL_ROUTE to CommandType (OC1-C001).

Tests: 16 new unit/integration tests, extensive verification of coordinate math and event lifetimes.

Related: docs/orbat-context-menu/OC1-TASK-DETAIL.md, docs/orbat-context-menu/OC1-DESIGN.md
```

---

**Next Batch:** Preparing next batch (OC1-BATCH-02)
