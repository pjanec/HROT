# MODULEHOST-BATCH-DYNAMIC-RCU Report

**Batch:** MODULEHOST-BATCH-DYNAMIC-RCU  
**Developer:** GitHub Copilot  
**Date:** 2026-03-09  
**Status:** Complete

---

## 📊 Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| Task 1 – Architectural Unification & Smart Batch API | ✅ Complete | `InstallModulesAsync` + `UninstallModulesAsync` added; `RegisterModule` state-aware; `PendingTopologyOperation` extended with `DrainEntries` |
| Task 2 – ECS Schema Mutation | ✅ Complete | `EnsureComponentsRegistered()` added; called in background compile tasks for both single and batch installs |
| Task 3 – Memory Provisioning Re-evaluation | ✅ Complete | SoD and GDB `AssignProviderForDynamicInstall` TODOs replaced with mask-aware upgrade logic; `LeasedProvider` race-condition fix also applied |
| Task 4 – Honest Test Suite | ✅ Complete | 6 new tests in `HonestSodGdbTests`: basic SoD/GDB install, batch atomic install, UnionMask expansion, batch uninstall, schema mutation |

---

## 🧪 Testing Results

**Tests Passed:** 190 / 191  
**Pre-existing failures:** 1 (`ProviderIntegrationTests.AllProviders_WorkWithModules` — see Outstanding Issues)

**Key Test Scenarios Verified:**
- ✅ `SodModule_InstallAndUninstall_UsesOnDemandProvider` — solo SoD gets correct `OnDemandProvider`
- ✅ `GdbModule_InstallAndUninstall_UsesDoubleBufferProvider` — solo GDB gets correct `DoubleBufferProvider`
- ✅ `BatchInstall_SodModules_ActivatedAtomically` — 3 SoD modules installed via `InstallModulesAsync`, all live in same frame, share one `SharedSnapshotProvider`
- ✅ `UnionMask_Expansion_NewSodModule_ExpandsSharedProvider` — solo OnDemand → SharedSnapshotProvider on second SoD install, `UnionMask` contains both component bits
- ✅ `BatchUninstall_SodModules_RemovedAtomically` — `UninstallModulesAsync` removes batch of modules in a single swap
- ✅ `Install_ModuleWithNovelComponent_RegistersComponentOnLiveWorld` — novel component type registered via `EnsureComponentsRegistered`
- ✅ All 184 pre-existing tests continue to pass

---

## 📝 Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

The most difficult issue was a non-obvious race condition between the background compilation task and the main thread's dispatch phase. When `AssignProviderForDynamicInstall` mutates `entry.Provider` from `OnDemandProvider` to `SharedSnapshotProvider` (promoting a SoD convoy), the background thread can write `entry.Provider` after the main thread has already read it for `AcquireView()` but before it completes the `LeasedProvider` assignment. This resulted in the provider being called with a view it didn't create.

The fix was to capture `entry.Provider` into a local `acquireProvider` variable atomically before calling `AcquireView()`, and use that exact reference for both the acquire and the `LeasedProvider` field. The `LeasedProvider` is then used in `HarvestEntry` and inline sync releases, guaranteeing that views are always returned to the provider that created them regardless of concurrent mutations.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

1. **`ProviderIntegrationTests.AllProviders_WorkWithModules`** (pre-existing failure): The test creates a `SnapshotPool` with a `schemaSetup` that registers BOTH `Position` and `Velocity`. This means even a Position-only-masked snapshot has `Velocity` pre-registered (via `schemaSetup`), so `GetComponentRO<Velocity>()` does not throw as the test expects. The test was designed for a schema-less pool but was later given a full schema — the assertion is now incorrect for the current setup.

2. **`AssignProviderForDynamicInstall` mutates shared `ModuleEntry` objects**: The "Promote to SharedSnapshotProvider" path (`e.Provider = sharedProvider`) mutates `ModuleEntry` instances that may simultaneously be read/written by the main thread. A cleaner architecture would create new `ModuleEntry` copies for topology rebuilds, with shared immutable state via a separate `ModulePersistentState` record, avoiding all mutation races by construction.

**Q3: What design decisions did you make beyond the instructions? What alternatives did you consider?**

1. **`LeasedProvider` field on `ModuleEntry`**: The spec mentioned task 3 should "gracefully drain" old providers via existing `_drainingModules`. A cleaner but more complex alternative would create new `ModuleEntry` wrappers for convoy members pointing to the new provider, and drain the old entries safely via `_drainingModules` (with a `SkipModuleDispose` flag). I chose the `LeasedProvider` approach because it solves the core race, is simpler, and doesn't require changes to the draining lifecycle.

2. **Sequential `AssignProviderForDynamicInstall` calls for batch install**: In `InstallModulesAsync`, I call `AssignProviderForDynamicInstall` sequentially for each entry (not in parallel). This is intentional: each successive entry can see the providers already assigned to prior entries in the batch, enabling correct convoy grouping (e.g., 3 SoD modules in one batch correctly form a single `SharedSnapshotProvider` convoy).

3. **`PendingTopologyOperation.DrainEntries` replaces `DrainEntry`**: Changed the single `DrainEntry` to `IReadOnlyList<ModuleEntry>? DrainEntries` to eliminate the polymorphism awkwardness of the old design (null check + single item). The `UpdateInternal` swap block is cleaner with `AddRange`.

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

1. **`SharedSnapshotProvider.ReleaseView` with `_activeReaders = 0`**: When a convoy upgrade happens (OnDemand → Shared), an in-flight async task holds a view from the old provider. If the background thread writes `entry.Provider` between the main thread's `AcquireView()` call and the `LeasedProvider` assignment, the harvester would call `SharedProvider.ReleaseView()` without a prior `AcquireView()` on that provider, crashing with an active-readers invariant violation. This cascade prevented modB from ever being dispatched (the harvest exception propagated before the dispatch loop ran).

2. **Pre-existing `AllProviders_WorkWithModules` test bug**: The `SnapshotPool(schemaSetup)` pre-registers both `Position` and `Velocity` components on snapshots even when only `Position` is in the BitMask256. The snapshot's `SyncFrom` with a Position-only mask skips syncing Velocity data but the table still exists (from `schemaSetup`). The test's expectation of an `InvalidOperationException` on `GetComponentRO<Velocity>` is wrong for this setup.

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

1. **`AssignProviderForDynamicInstall` mutates live topology entries**: For SoD convoy upgrades, the background task writes `entry.Provider` on live `ModuleEntry` objects. While the race is now handled by `LeasedProvider`, the mutation itself is not `volatile`-safe. For a correctness-critical system, `Volatile.Write` should be used for the `Provider` field (or the `LeasedProvider` pattern should be extended to make `Provider` fully immutable post-creation, using new entry copies for topology rebuilds).

2. **`EnsureComponentsRegistered` uses reflection**: Calling `typeof(EntityRepository).GetMethod(...).MakeGenericMethod(type).Invoke(...)` has overhead. For production code with frequent dynamic installs, caching the reflected `MethodInfo` per type in a `ConcurrentDictionary<Type, MethodInfo>` would eliminate the repeated reflection cost.

---

## ⚠️ Outstanding Issues / Next Steps

- [ ] **Pre-existing test bug**: `ProviderIntegrationTests.AllProviders_WorkWithModules` was failing before this batch and remains failing. The test assertion is incorrect: it expects `GetComponentRO<Velocity>` to throw on a schema-setup'd snapshot that has `Velocity` pre-registered even though it was masked out during sync. This needs to be fixed by the test author (either remove the `schemaSetup` from the `SnapshotPool` used in that test, or change the assertion).

- [ ] **`ModuleEntry.Provider` mutation safety**: The SoD/GDB convoy upgrade path (Task 3) writes to `entry.Provider` from the background thread without `volatile` semantics. This is functionally safe given the `LeasedProvider` protection, but a follow-up batch should consider making `Provider` immutable per-entry (new entry copies on rebuild) for a fully lock-free architecture.
