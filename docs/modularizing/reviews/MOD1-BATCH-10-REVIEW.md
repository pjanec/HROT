# MOD1-BATCH-10 Review

**Batch:** MOD1-BATCH-10  
**Reviewer:** Development Lead  
**Date:** 2026-03-16  
**Status:** ⚠️ APPROVED WITH CAVEATS

---

## Summary

BATCH-10 correctly completed all assigned tasks. The debt fixes were handled well and the Phase 6 translator pack work is structurally sound. However, an independent architecture audit surfaced **4 pre-existing gaps** that the developer did not flag—gaps that fall squarely within the batch's scope of "completing Phase 6 and generalizing". These are tracked as new debt and the most critical ones are carried into BATCH-11.

---

## What Went Well

### DB-MOD1-03 — `PrimaryOwnerId` Audit
One production system fixed (`CycloneNetworkCleanupSystem`). The `HasAuthority` helper property was added to `NetworkOwnership` correctly. The developer correctly identified that the 7 remaining systems in `FDP/Examples/` are demo code and out of scope for a production audit — that call is right.

### DB-MOD1-07 — CycloneDDS Daemon Test
Gating `EntityMasterEgressTranslatorTests` with `[Trait("Category","Integration")]` is the correct pattern. The 3 tests are now skipped in the standard `dotnet test` run.

### DB-MOD1-10 — DDS Participant Cleanup
9 tests in `EntityMissionTranslatorTests.cs` wrapped with `using var` — proper fix, prevents domain collision under parallel execution.

### DB-MOD1-21 — `TestMetricsCollector` Audit
Confirmed zero `Bagira.*` references, documented in design doc. Clean.

### MOD1-P6T8 — Four Translator Packs
The tests are correctly scoped: they verify that `BuildTranslators(NodeRole.X)` produces the right pack types, and explicitly verify that `AllInOne` includes all four packs while `Brain` excludes sim-side packs. The `AllInOne` one-frame DDS lag analysis (Q3) is architecturally sound.

### BTreeContext Stubs (P6T4/P6T5)
The developer correctly identified the situation: the stubs **must remain** because `IAIContext` is defined in the third-party `Fbt.Kernel` submodule and `BTreeContext` must satisfy the interface contract. The no-op implementation with a clear comment is the right answer. The user's original concern (from the analysis) that "this was completely ignored" is **incorrect** — the stubs are intentional and necessary, not forgotten. The work was done properly in BATCH-07 via `RaycastBatchHelper` and `PathfindingBatchHelper`, confirmed in this batch.

---

## Pre-existing Issues Surfaced by Architecture Audit

The user's independent code analysis identified 4 structural gaps not addressed in BATCH-10. All are confirmed as real by direct source inspection:

### Issue 1: `IgSymbolOverride` Component ID in `GlobalComponentIds` (Confirmed — DB-MOD1-22)

`GlobalComponentIds.IgSymbolOverride = 119` exists in `Fdp.Kernel/GlobalComponentIds.cs`. The component class is in `Bagira.Map.Common` and is exclusively used by `Bagira.IG`, `Bagira.Map.Common`, and `Bagira.IG.Tests`. An IG-specific visual override component ID has no business in the FDP kernel registry.

**Required fix:** Move ID 119 into `BagiraComponentIds` (the registry established in Phase 5 for exactly this purpose) and update the `[ComponentId]` attribute on `IgSymbolOverride`.

### Issue 2: `NavigationIntent` + `NavigationStatus` Forced Into `Fdp.Kernel` (Confirmed — DB-MOD1-23)

`NavigationIntent` (ID 67) and `NavigationStatus` (ID 68) live in `Fdp.Kernel/CoreComponents/NavigationComponents.cs` explicitly to break the circular dependency between `FDP.Toolkit.Navigation` and `FDP.Toolkit.CarKinem`. The comment in the file says so. This is a valid workaround but it is a known deviation from the design and must be tracked.

**The fix is non-trivial** and requires either restructuring the assembly graph (introducing a thin `FDP.Toolkit.Navigation.Contracts` assembly for shared types) or accepting the current placement as a permanent decision. This needs an architecture decision before coding, not just a ticket.

### Issue 3: `KinematicTranslatorPack` + `CognitiveTranslatorPack` Missing (Confirmed — DB-MOD1-24)

`grep` confirms: `KinematicTranslatorPack` and `CognitiveTranslatorPack` do not exist anywhere in the solution. `SimHostApp.OnLoad` still manually instantiates individual translators. Phase 3 of the design called for these factory classes to replace the manual `translators.Add(...)` pattern. `SharedTranslatorPack` exists; the other two do not.

**Required fix:** Create `KinematicTranslatorPack` and `CognitiveTranslatorPack` in `Bagira.SimHost.Network`, following the same static factory pattern as `SharedTranslatorPack`, and refactor `SimHostApp.OnLoad`.

### Issue 4: `HealthData` Mirror in `Fdp.Kernel` — Accepted with Caveat (DB-MOD1-25)

`HealthData` (ID 2) in `Fdp.Kernel/CoreComponents/SimComponents.cs` is confirmed. `DamageSystem.cs` explicitly synchronizes `Health.Current → HealthData.Current` on every damage event. The comment in `MissionDirectorSystem.cs` says "DEBT-033 resolved" but this is the mitigation, not the resolution.

The duplication is real. However, given that:
- `HealthData` is in the 0–19 "universal" block — it represents a genuinely cross-cutting concern (how healthy is an entity?)
- Removing it would require `FDP.Toolkit.Behavior` to reference `FDP.Toolkit.Combat`, creating a circular dependency

This is best classified as a **design constraint accepted with documentation**, not a fixable bug. The actual debt is the sync call in the hot path of `DamageSystem` — it should be a dirty-flag write (only when `Health.Current` changes), not an unconditional write per damage event.

---

## Verdict

**Status:** ⚠️ APPROVED WITH CAVEATS

The developer completed all assigned tasks correctly. The four architectural gaps were pre-existing and not introduced by this batch. However, `IgSymbolOverride` (Issue 1) is a straightforward fix that should be in BATCH-11, and the missing `KinematicTranslatorPack`/`CognitiveTranslatorPack` (Issue 3) are Phase 3 deliverables that have been outstanding for too long.

---

**Next Batch:** MOD1-BATCH-11
