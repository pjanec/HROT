# BATCH-08 Report

**Batch:** BATCH-08  
**Developer:** GitHub Copilot  
**Date:** 2026-03-26  
**Status:** Complete (Tasks 1–3; Task 4 deferred — see below)

---

## 📊 Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| Task 1 — ComponentReflector `AllocHGlobal` | ✅ Complete | Replaced with `ArrayPool<byte>` + pinned `GCHandle`; new test added |
| Task 2a — `DemoBehaviorIds` naming | ✅ Complete | Scenarios-local class renamed to `BehaviorValidationBehaviorIds` |
| Task 2b — `MissionTriggerHelper` CS0618 | ✅ Complete | `"ReachedDestination"` now maps to `BehaviorFinished`; test updated |
| Task 3a — Kernel topology test | ✅ Complete | `GetRegisteredModuleTypeNames()` API added; test asserts kernel structure |
| Task 3b — D008 docs + XML | ✅ Complete | `DEM1-TASK-DETAIL.md` and `ParallelStoriesScenario` XML updated |
| Task 4 — DEM1-D009 DistributedTank | ⏩ Deferred | Scope exceeded available estimate; documented below |

---

## 🧪 Testing Results

**ImGui Tests:** 42 / 42 passed  
**Map.Common Tests:** 94 / 94 passed  
**Scenarios Tests:** 51 / 51 passed  
**Full solution build:** succeeded, zero new errors or warnings

**Key Test Scenarios Verified:**
- [x] `ComponentReflector` three-frame change cycle (new — `UnmanagedComponent_ThreeFrameCycle_InPlaceCacheDetectsAllChanges`)
- [x] All five existing `ComponentReflector` diffing tests still pass
- [x] `ResolveTrigger_ReachedDestination_MapsToBehaviorFinished` (renamed + updated expectation)
- [x] `ParallelStories_NoCarKinimSystemsInReplayKernel` — now inspects real kernel topology via `GetRegisteredModuleTypeNames()`
- [x] `ParallelStories_RunToCompletion_ExitsZero` still passing
- [x] `ParallelStories_ReplayMatchesLiveAtTick25` still passing

---

## 📝 Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

Task 1: The first draft of the `Marshal.AllocHGlobal` replacement accidentally dropped the `catch {}` block and left a mismatched brace, causing a compiler error. Fixed immediately by reading back the file state and applying a precise replacement.

Task 3a: `Assert.DoesNotContain` in xUnit doesn't accept a custom message as a third argument (it accepts an `IEqualityComparer`). Replaced with `Assert.False(registered.Contains(...), message)`.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

The `ParallelStoriesScenario.HasCarKinematicsInMainKernel` flag pattern was the exact weakness targeted. More broadly, author-set boolean flags as "proof" of topology are fragile across any codebase using this pattern — consider a lint rule or convention against it in new scenarios.

The `ArrayPool<byte>.Rent` approach for Task 1 still uses a pinned `GCHandle`, which is a minor overhead. A future improvement could use `MemoryMarshal.TryGetArray` + `unsafe` with `stackalloc` for structs ≤ 512 bytes, completely eliminating GC-visible pinning. Added to debt tracker candidate list.

**Q3: What design decisions did you make beyond the instructions? What alternatives did you consider?**

**Task 1 — `ArrayPool<byte>` vs `stackalloc`:**  
The project does not declare `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`, making `stackalloc` with a pointer-based `Marshal.StructureToPtr` call require an unsafe context that would need a project file change. `ArrayPool<byte>` with a pinned `GCHandle` achieves the same goal without unsafe code and is idiomatic in .NET 8 environments. The hot path now incurs a `Rent` + `GCHandle.Alloc` instead of a native heap allocation — both are lighter-weight and bounded by the pool.

**Task 2a — Rename vs merge:**  
The two `DemoBehaviorIds` classes have intentionally different values (`Combat = 2900` vs `Combat = 200`). Merging them would require picking one value, breaking one of the callers. Renaming the scenarios-local class to `BehaviorValidationBehaviorIds` is zero-risk: only one file uses it (`BehaviorValidationScenario.cs`). The test workaround (using the qualified `Fdp.Examples.Common.Constants.DemoBehaviorIds.Combat` form) is no longer needed but was left as-is since it's still correct.

**Task 3a — `GetRegisteredModuleTypeNames()` vs `InternalsVisibleTo`:**  
Chose to add a small public diagnostic API rather than `InternalsVisibleTo` because:
1. The kernel's module list is legitimately useful for diagnostics outside tests (e.g. admin UI, health checks).
2. `InternalsVisibleTo` would couple the production codebase to the test assembly name and is harder to undo.
3. The method is O(n) and allocates; appropriate only for diagnostics, not hot paths — documented as such.

The `HasCarKinematicsInMainKernel` flag property was removed from `ParallelStoriesScenario` entirely (replaced by `ReplayKernelModuleTypeNames`) to eliminate the source of the weakness flagged in Issue 1 of the BATCH-07 review.

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

In Task 1: `ArrayPool<byte>.Rent(size)` may return a buffer _larger_ than `size`. The `Array.Copy(pooled, cached, size)` call correctly limits the copy to `size` bytes, so oversized pooled buffers are handled safely. The `_unmanagedCache` baseline array is always exactly `size` bytes (`new byte[size]`), ensuring byte-for-byte comparison with the exact struct layout.

In Task 3a: `GetRegisteredModuleTypeNames()` is called at the end of `Configure`, before `ModuleHostKernel.Initialize()`. Since `_modules` is populated by `RegisterModule()` (called during `Configure`), the snapshot is complete and accurate at that point.

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

The `GCHandle.Alloc(pooled, GCHandleType.Pinned)` + `Free()` calls per component per frame are still non-trivial in a very large entity inspector. However, `ComponentReflector` is only active when a user has an entity selected in the inspector panel — not in any simulation hot path — so the overhead is acceptable. For a future optimisation, enabling `AllowUnsafeBlocks` on the ImGui toolkit project would allow a pure `stackalloc`/`fixed` approach with zero GC involvement.

---

## ⚠️ Outstanding Issues / Next Steps

### Task 4 — DEM1-D009 DistributedTank (Deferred)

DEM1-D009 was assessed but not started. The minimal vertical slice requires:
- `DistributedTankScenario.cs` + `NetworkDemoConstants.cs` in `Fdp.Examples.Scenarios/Network/`
- Cyclone DDS loopback domain-0 configuration for two `ModuleHostKernel` instances
- xUnit test class in `Fdp.Examples.Scenarios.Tests`
- `ScenarioNames.DistributedTank` + `ScenarioRegistry` entry

Estimated effort: 4–6 hours for Phase A (harness + handshake skeleton + one test). Recommended for BATCH-09.

### Debt rows to close/retarget (lead action)

The following DEBT-TRACKER rows are addressed by this batch and can be updated to ✅ by the lead:

| Row | Resolution |
|-----|-----------|
| BD1-BATCH-03 `ComponentReflector AllocHGlobal` | ✅ Fixed — `ArrayPool<byte>` |
| BATCH-06 naming `DemoBehaviorIds` | ✅ Fixed — renamed to `BehaviorValidationBehaviorIds` |
| BATCH-07 `MissionTriggerHelper` CS0618 | ✅ Fixed — maps `"ReachedDestination"` → `BehaviorFinished` |
| BATCH-07 `NoCarKinimSystemsInReplayKernel` weak test | ✅ Fixed — actual kernel topology inspected |
| BATCH-07 DEM1-D008 docs/XML `GroundKinematicsModule` | ✅ Fixed — updated to `LiveKinematicsModule` + `AsyncRecorder` |

The `P3 Product` row (`RecordingConfiguration` / `RecorderTickSystem` blocking option, BATCH-08+`) remains open — addressing it is out of scope for this batch.
