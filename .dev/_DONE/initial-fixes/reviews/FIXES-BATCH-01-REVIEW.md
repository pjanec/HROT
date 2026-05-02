# FIXES-BATCH-01 Review

**Batch:** FIXES-BATCH-01
**Reviewer:** Development Lead
**Date:** 2026-02-26
**Status:** ✅ APPROVED

---

## Summary

The developer successfully addressed the architecture deviations across SimHost and IG, and hooked up the UI panels. Modifying the `DerRepo` to hold `LocalNodeId` was not explicitly requested in the original task, but was correctly applied to support cosmetic UI features. All requirements have been satisfied.

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
fix: architecture bugs and UI panel wiring (FIXES-BATCH-01)

Completes TASK-IF001, TASK-IF002, TASK-IF003, TASK-IF004, TASK-IF005, TASK-IF006, TASK-IF007, TASK-IF008

Corrects SimHost validation errors by stripping VehicleState descriptors from the mapping phase, increments behavior ID per change, and publishes EntityMaster on the DDS interface. Adjusts ghost node tagging logic within IG so that remote entity dead reckoning correctly takes over. Finally, completes UI panel lifecycle wiring across the IG and IOS applications.

Hrot.SimHost:
- Eliminated implicit VehicleState inclusion on entities
- Fixes MissionAdapterSystem behavior caching
- Adds direct AutoCycloneTranslator to publish EntityMaster DDS topic

Hrot.IG:
- Overwrites EntityMasterTranslator ownership tag logic to ghost 0 
- Introduces TransformSyncSystem interpolation registration
- Migrates the CreationTool to DDS instead of the local FdpEventBus
- Hooks ImGui panels to the application loop, gating Input actions behind want capture requests

Hrot.ExCon:
- Activates code for all panel render methods and resolves IosMock ImGui instantiation quirks
- Restores cosmetics for `LocalNodeId` relying on DER library extensions

Tests: 6 integration and unit tests covering structure, payload format, mapping overrides, and UI render gating
```

---

**Next Batch:** Preparing next batch
