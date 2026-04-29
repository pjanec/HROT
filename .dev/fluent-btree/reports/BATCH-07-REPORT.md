# BATCH-07 Report

## Summary

Implemented `FbtAssemblyHotReloader` (FBT-023) and all four ALC-based hot reload tests (FBT-022 remaining). All 149 tests pass (145 existing + 4 new). Zero build errors or warnings.

## Tasks Completed

- [x] FBT-023: FbtAssemblyHotReloader
- [x] FBT-022 (ALC tests): AlcHotReloader_Dispose_DoesNotThrow, AlcHotReloader_OnReloadFailed_FiredForDllWithoutRegistrar, AlcHotReloader_OnReloadCompleted_FiredForValidDll, AlcHotReloader_OldAlc_IsUnloaded_AfterReload

## Test Results

Total passing: 149 / 149
New tests: 4

All four ALC tests passed without `[Fact(Skip)]` fallback — full runtime compilation via `CSharpCompilation` worked.

Individual test timings:
- `AlcHotReloader_Dispose_DoesNotThrow`: 2 ms
- `AlcHotReloader_OnReloadFailed_FiredForDllWithoutRegistrar`: 267 ms
- `AlcHotReloader_OnReloadCompleted_FiredForValidDll`: 780 ms
- `AlcHotReloader_OldAlc_IsUnloaded_AfterReload`: 549 ms

## Files Changed

- **Created:** `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/HotReload/FbtAssemblyHotReloader.cs`
- **Modified:** `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Fbt.Tests.csproj` — added `Microsoft.CodeAnalysis.CSharp` 4.8.0 package reference
- **Modified:** `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Unit/HotReloadTests.cs` — added 4 ALC tests and compilation helpers

## Developer Insights

**Q1: How did you handle the timing/flakiness of ALC unload tests?**

The `AlcHotReloader_OldAlc_IsUnloaded_AfterReload` test uses a polling loop of up to 10 iterations with `GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true)` + `GC.WaitForPendingFinalizers()` between each pass, and a 50ms sleep between iterations. This gives the GC plenty of opportunity to collect the unloaded ALC. The test ran in ~549ms without hitting the limit, indicating the ALC was collected quickly once all local references on the background thread went out of scope.

The critical correctness point: `LoadAndReload` uses `Interlocked.Exchange` to swap `_currentAlc`, so after the method returns, the old ALC is only reachable via `PreviousAlcRef` (a `WeakReference`), making it eligible for GC.

**Q2: Any issues with compiling the test DLL at test time?**

The batch instructions noted that `Fbt.Tests.csproj` already references `Microsoft.CodeAnalysis.CSharp` via the SourceGen Analyzer, but that is incorrect — the Analyzer reference uses `ReferenceOutputAssembly="false"` which makes it available only to the Roslyn compiler, not as a runtime dependency. An explicit `PackageReference` for `Microsoft.CodeAnalysis.CSharp` 4.8.0 (same version as the SourceGen project) was added to `Fbt.Tests.csproj`.

The minimal metadata references needed for test DLL compilation were:
- `typeof(object).Assembly.Location` (mscorlib/System.Private.CoreLib)
- `typeof(FbtRegistrarAttribute).Assembly.Location` (Fbt.Kernel.dll)
- `Path.Combine(runtimeDir, "System.Runtime.dll")`

These three references were sufficient for both the "with registrar" and "without registrar" test sources.

**Q3: Thread-safety concerns or weak points?**

- `FileSystemWatcher` raises events on a background thread. Only `ConcurrentQueue.Enqueue` is called from background threads; `DrainPendingCallbacks` dequeues on the application thread. This is lock-free and safe.
- The debounce timer uses a single `_pendingPath` field protected by `_debounceLock`. Multiple rapid file changes collapse into one load attempt — the last written path wins. This is intentional.
- `Dispose()` races with `LoadAndReload` are benign: `Interlocked.Exchange` ensures exactly one caller gets each ALC reference, so each ALC is unloaded exactly once regardless of race outcome.
- `IOException` retry loop (5 retries × 50ms) guards against file-lock races when the watcher fires before the copy completes.
- The `PreviousAlcRef` property is written only from the background `LoadAndReload` thread. In the ALC unload test this is read after all background work completes (verified by the poll loop), so no data race occurs. In production usage, `PreviousAlcRef` is advisory/diagnostic — callers should not depend on seeing a specific value at a specific time.

**Suggested commit message:**

```
FBT-023: FbtAssemblyHotReloader + ALC tests (FBT-022)
```
