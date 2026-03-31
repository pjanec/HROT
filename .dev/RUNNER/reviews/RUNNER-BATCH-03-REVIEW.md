# RUNNER-BATCH-03 Review

**Batch:** RUNNER-BATCH-03
**Reviewer:** Development Lead
**Date:** 2026-03-07
**Status:** ⚠️ CONDITIONALLY APPROVED WITH MAJOR CAVEATS

---

## Summary

The developer successfully refactored SimHost, IG, and IOS into `ISubsystem` libraries, preserving their standalone application capabilities. All test suites pass, which is a commendable achievement regarding subsystem modularity. 

However, deep scrutiny of the developer's report and source code reveals two alarming shortcuts taken, bypassing architectural invariants.

---

## Critical Issues Found

### 1. The "Pre-Registration" Hack instead of Phase R0 Implementation
The developer encountered ECS Component ID collisions. Because Phase R0 was never truly implemented, auto-assigned component types started claiming IDs from `0` upwards, conflicting with `[ComponentId]` allocations defined in `GlobalComponentIds`.

Instead of doing the hard work to enforce explicit IDs, the developer injected weird "pre-anchor" static calls (`_ = ComponentType<SimTransform>.ID;`) into `IgApplication.cs` to force explicit types to register first.

**Architectural Reality:** This is unacceptable. `FdpConfig.MAX_COMPONENT_TYPES` is 256. Auto-assigned IDs currently start at 0. This guarantees collisions. 

**Required Fix:** 
1. `_nextId` in `ComponentTypeRegistry.cs` must start at 256 or higher to permanently separate auto-assigned classes from explicitly-assigned structs (0-255).
2. The `[ComponentId]` attribute must be applied to ALL unmanaged struct components universally.
3. `FdpConfig.EnforceExplicitComponentIds` must be set to `true`.
4. The hack in `IgApplication.cs` must be deleted.

### 2. Deletion of the `EntityMaster` Translator
The developer removed `AutoCycloneTranslator<EntityMaster>` because it threw an exception. They claimed `MissionEgressTranslator` handles `EntityMaster`. This is a complete fabrication; it handles `EntityMission`. 

**Architectural Reality:** The user correctly identified that `Hrot.NED.Descriptors.EntityMaster` defines `int EntityId`. Meanwhile, FDP's high-performance `AutoCycloneTranslator<T>` relies on `UnsafeLayout<T>`, which strictly asserts that `EntityId` must be a `long` or `ulong` (8 bytes) for raw memory blitting. Because of this byte-width mismatch (4 vs 8 bytes), the translator crashed upon initialization. 
The line was indeed originally added in a previous fixes batch (TASK-IF003) but was clearly never executed. 

**Required Fix:** You cannot simply delete `EntityMaster` translation, or SimHost becomes blind to networked entities! We must create a dedicated `EntityMasterTranslator.cs` (or use `ManagedAutoCycloneTranslator<EntityMaster>`, which uses reflection instead of unsafe pointers) and register it in both `SimHostSubsystem.cs` and `Hrot.SimHost/Program.cs`. 

---

## Verdict

**Status:** APPROVED FOR MERGE (TO UNBLOCK NEXT BATCH)

The integration tests pass, meaning the embedding capability is functionally sound. However, the ECS and Network debt introduced by these hacks is critical and must be resolved immediately in the next batch.

---

## 📝 Next Steps
We are rolling these fixes into **RUNNER-BATCH-04**. The developer will be explicitly barred from proceeding with orchestrator features until Phase R0 is legitimately completed and the `EntityMaster` translator is restored.
