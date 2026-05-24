# BATCH-07: FbtAssemblyHotReloader + ALC-based Reload Tests

**Batch Number:** BATCH-07
**Tasks:** FBT-023, FBT-022 (remaining ALC tests)
**Phase:** Phase 3 (BTreeHotReloadManager — assembly reload)
**Estimated Effort:** 8-10 hours
**Priority:** HIGH
**Dependencies:** BATCH-01 through BATCH-06

---

## 📋 Onboarding & Workflow

### Required Reading (IN ORDER)

1. **Task Detail:** `.dev/fluent-btree/TASK-DETAIL.md` — FBT-023, FBT-022
2. **ReloadResult + BTreeHotReloadManager:** `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/HotReload/` (both files)
3. **Attributes:** `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Attributes/FbtRegistrarAttribute.cs`
4. **FbtAutoDiscovery:** `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/FbtAutoDiscovery.cs` (reflection scanning pattern)

### Build and Test Commands

```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build FDP/ExtDeps/FastBTree/FastBTree.sln -v quiet 2>&1 | Select-String "error|Build succeeded|FAILED"
dotnet test FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Fbt.Tests.csproj 2>&1 | Select-String "Passed!|Failed!" | Select-Object -Last 3
```

### Report Submission

`.dev/fluent-btree/reports/BATCH-07-REPORT.md`

---

## 🔄 MANDATORY WORKFLOW

1. **FBT-023:** Create `FbtAssemblyHotReloader` in `Fbt.Kernel/HotReload/`
2. **FBT-022 (remaining):** Add ALC tests for `FbtAssemblyHotReloader` to `HotReloadTests.cs`
3. Verify all 145 existing tests still pass + new tests pass
4. Run full test suite, commit changes

---

## ✅ Tasks

### Task 1: FbtAssemblyHotReloader (FBT-023)

**New file:** `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/HotReload/FbtAssemblyHotReloader.cs`

#### Overview

`FbtAssemblyHotReloader` watches a directory for DLL changes. When a DLL appears:
1. Load it into a new collectible `AssemblyLoadContext`
2. Find the `[FbtRegistrar]`-annotated class in the new assembly via reflection
3. Get blobs from the new assembly (from `FbtTreeCatalog` if present)
4. Call the user-provided reload handlers
5. Unload old ALC

#### Interface Design

The class must remain free of FDP/HROT dependencies (no BehaviorRegistry, no BrainBTreeState). All integration is done via events and delegates.

```csharp
namespace Fbt.HotReload
{
    using System;
    using System.IO;
    using System.Reflection;
    using System.Runtime.Loader;
    using System.Collections.Generic;
    using System.Threading;

    /// <summary>
    /// Watches a directory for new DLLs and orchestrates assembly-based hot reload.
    /// All ALC operations run on a background thread; results are enqueued for
    /// application-thread consumption via <see cref="DrainPendingCallbacks"/>.
    /// Does not reference any FDP/HROT types directly.
    /// </summary>
    public sealed class FbtAssemblyHotReloader : IDisposable
    {
        // ---- Dependency types (provided by caller) ----

        /// <summary>
        /// Called when an assembly is loaded. The caller should invoke RegisterAll
        /// on the provided [FbtRegistrar] type, then return the set of trees to reload.
        /// </summary>
        public delegate IEnumerable<(string treeName, BehaviorTreeBlob blob)>
            AssemblyReloadHandler(Type registrarType, Assembly newAssembly);

        // ---- Events (fired from DrainPendingCallbacks on application thread) ----
        public event Action<string>? OnReloadCompleted;
        public event Action<string, Exception>? OnReloadFailed;

        // ---- Constructor ----
        public FbtAssemblyHotReloader(string watchDirectory, AssemblyReloadHandler handler)
        ...
        
        // ---- Application thread drain ----
        /// <summary>
        /// Must be called once per game update from the application thread.
        /// Fires OnReloadCompleted / OnReloadFailed for any queued reload results.
        /// </summary>
        public void DrainPendingCallbacks()
        ...

        // ---- Weak reference for GC verification ----
        /// <summary>
        /// A weak reference to the previously-unloaded ALC. Used in tests to verify
        /// the old ALC was GC'd. Null if no reload has occurred.
        /// </summary>
        public WeakReference<AssemblyLoadContext>? PreviousAlcRef { get; private set; }

        // ---- IDisposable ----
        public void Dispose()
        ...
    }
}
```

#### Implementation Details

**FileSystemWatcher + debounce:**
```csharp
private FileSystemWatcher _watcher;
private Timer _debounceTimer;
private string _pendingPath;
private readonly object _debounceLock = new object();
private const int DebounceMs = 200;

// On FileChanged/Created:
private void OnFileChanged(object sender, FileSystemEventArgs e)
{
    if (!e.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) return;
    lock (_debounceLock) { _pendingPath = e.FullPath; }
    _debounceTimer.Change(DebounceMs, Timeout.Infinite);
}

// Timer fires after debounce:
private void OnDebounceElapsed(object? state)
{
    string path;
    lock (_debounceLock) { path = _pendingPath; }
    ThreadPool.QueueUserWorkItem(_ => LoadAndReload(path));
}
```

**LoadAndReload (on background thread):**
```csharp
private void LoadAndReload(string dllPath)
{
    try
    {
        var newAlc = new AssemblyLoadContext(Path.GetFileNameWithoutExtension(dllPath), isCollectible: true);
        Assembly newAssembly;
        using (var fs = File.OpenRead(dllPath))
            newAssembly = newAlc.LoadFromStream(fs);

        // Find [FbtRegistrar]-annotated type
        Type? registrarType = null;
        foreach (var type in newAssembly.GetTypes())
        {
            if (type.GetCustomAttribute(typeof(FbtRegistrarAttribute)) != null)
            {
                registrarType = type;
                break;
            }
        }

        if (registrarType == null)
        {
            // No registrar found — fail gracefully
            var ex = new InvalidOperationException($"No [FbtRegistrar] class found in '{dllPath}'.");
            _pendingCallbacks.Enqueue(() => OnReloadFailed?.Invoke(dllPath, ex));
            // Unload the new (useless) ALC immediately
            newAlc.Unload();
            return;
        }

        // Call user handler to do registration + get blobs
        IEnumerable<(string, BehaviorTreeBlob)> results;
        try
        {
            results = _handler(registrarType, newAssembly);
        }
        catch (Exception ex)
        {
            _pendingCallbacks.Enqueue(() => OnReloadFailed?.Invoke(dllPath, ex));
            newAlc.Unload();
            return;
        }

        // Unload OLD ALC
        var oldAlc = Interlocked.Exchange(ref _currentAlc, newAlc);
        if (oldAlc != null)
        {
            PreviousAlcRef = new WeakReference<AssemblyLoadContext>(oldAlc);
            oldAlc.Unload();
        }

        // Queue success callbacks
        foreach (var (treeName, _) in results)
        {
            var name = treeName;
            _pendingCallbacks.Enqueue(() => OnReloadCompleted?.Invoke(name));
        }
    }
    catch (Exception ex)
    {
        _pendingCallbacks.Enqueue(() => OnReloadFailed?.Invoke(dllPath, ex));
    }
}
```

**Thread-safe callback queue (lock-free):**
```csharp
private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _pendingCallbacks
    = new System.Collections.Concurrent.ConcurrentQueue<Action>();

public void DrainPendingCallbacks()
{
    while (_pendingCallbacks.TryDequeue(out var cb))
        cb();
}
```

**Current ALC tracking:**
```csharp
private AssemblyLoadContext? _currentAlc;
```

**Dispose:**
```csharp
public void Dispose()
{
    _watcher.Dispose();
    _debounceTimer.Dispose();
    var alc = Interlocked.Exchange(ref _currentAlc, null);
    alc?.Unload();
}
```

#### Note on FbtRegistrarAttribute Location

`FbtRegistrarAttribute` is in `Fbt.Kernel`. `FbtAssemblyHotReloader` is also in `Fbt.Kernel`. So `typeof(FbtRegistrarAttribute)` works directly without any additional dependency.

---

### Task 2: ALC Tests (FBT-022 remaining)

**Add to existing:** `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Unit/HotReloadTests.cs`

These tests exercise `FbtAssemblyHotReloader` using **in-memory assemblies** (built via `Microsoft.CodeAnalysis.CSharp`) — but that's complex. Instead, use a **simpler approach**: build a minimal test helper assembly at test startup by compiling C# source to a temp DLL using `CSharpCompilation`.

**BUT** since `Fbt.Tests.csproj` already references `Microsoft.CodeAnalysis.CSharp` (via `Fbt.SourceGen` Analyzer reference), this is available for tests.

#### Simpler Alternative: Use Reflection.Emit

Build a minimal in-memory assembly in the test using `AssemblyBuilder.DefineDynamicAssembly`:
```csharp
// NOT RECOMMENDED: Reflection.Emit assemblies can't be loaded by AssemblyLoadContext.LoadFromStream
```

Actually Reflection.Emit dynamic assemblies cannot be persisted to a file stream. Use `CSharpCompilation` instead.

#### Recommended Approach: Compile a minimal DLL at test time

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

private static string CompileTestDll(string outputPath, string sourceCode)
{
    var compilation = CSharpCompilation.Create(
        assemblyName: "TestHotReloadAssembly",
        syntaxTrees: new[] { CSharpSyntaxTree.ParseText(sourceCode) },
        references: GetMetadataReferences(),
        options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    
    var result = compilation.Emit(outputPath);
    if (!result.Success)
        throw new InvalidOperationException("Compilation failed: " + string.Join(", ", result.Diagnostics));
    return outputPath;
}
```

The test DLL source must include `[FbtRegistrar]` attribute. Since it references `Fbt.Kernel`, include the path to `Fbt.Kernel.dll` in `GetMetadataReferences()`.

#### Tests to Write

```
AlcHotReloader_OnReloadCompleted_FiredAfterValidDll
    Setup: compile a minimal valid DLL with a [FbtRegistrar] class; point watcher at temp dir;
           copy DLL into dir; call DrainPendingCallbacks in a loop until OnReloadCompleted fires or 2s timeout
    Assert: OnReloadCompleted was fired with a non-null tree name OR handler was called

AlcHotReloader_OnReloadFailed_FiredWhenNoRegistrar
    Setup: compile a DLL with NO [FbtRegistrar] class; copy to temp dir; drain callbacks
    Assert: OnReloadFailed was fired

AlcHotReloader_OldAlc_IsUnloaded_AfterReload
    Setup: trigger first reload (valid DLL); trigger second reload (different DLL)
    Action: GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect()
    Assert: PreviousAlcRef.TryGetTarget(out _) == false (old ALC was GC'd)
```

**IMPORTANT:** The ALC unload test is inherently timing-sensitive. To avoid flakiness:
- Call `GC.Collect(0, GCCollectionMode.Forced, blocking: true, compacting: true)` three times
- Call `GC.WaitForPendingFinalizers()` between collections
- Wait for the WeakReference to become null in a short polling loop (max 5 iterations)

#### Simplification option

If compiling C# source code at test time proves too complex (requires assembling reference lists for Fbt.Kernel, etc.), fall back to:
1. Write `AlcHotReloader_OnReloadFailed_FiredWhenNoRegistrar` only — using a pre-built empty DLL (compile and check in as a test fixture, or write bytes for a minimal valid but empty assembly)
2. Skip `AlcHotReloader_OnReloadCompleted` for now and document as DT-006 (deferred — requires test assembly fixture)
3. Write only the ALC-unload test using a real temp DLL built from source

The absolute minimum for BATCH-07 is:
- `FbtAssemblyHotReloader` compiles without errors
- At least 2 tests targeting `FbtAssemblyHotReloader` pass
- All 145 existing tests continue to pass

---

## ⚠️ Quality Standards

- `FbtAssemblyHotReloader` must not block the game thread — all disk I/O and ALC operations on background thread
- Use `ConcurrentQueue<Action>` for callback dispatch
- Debounce timer: 200ms
- `FileSystemWatcher` filter: `*.dll`
- `isCollectible: true` on `AssemblyLoadContext` constructor
- Zero warnings (TreatWarningsAsErrors)
- `_blobStructureHash` in Interpreter is already `readonly` and write-only — do not remove it (it is used for hot reload introspection)

---

## 📊 Report Requirements

Create `.dev/fluent-btree/reports/BATCH-07-REPORT.md`:

```markdown
# BATCH-07 Report

## Summary

## Tasks Completed
- [ ] FBT-023: FbtAssemblyHotReloader
- [ ] FBT-022 (ALC tests): AlcHotReloaderTests

## Test Results
Total passing: XX / XX
New tests: X

## Developer Insights

**Q1:** How did you handle the timing/flakiness of ALC unload tests?

**Q2:** Any issues with compiling the test DLL at test time?

**Q3:** Thread-safety concerns or weak points?

**Suggested commit message:**
```

---

## 🎯 Success Criteria

- [ ] `Fbt.Kernel/HotReload/FbtAssemblyHotReloader.cs` compiles without errors
- [ ] `FbtAssemblyHotReloader` has `OnReloadCompleted`, `OnReloadFailed`, `PreviousAlcRef`, `DrainPendingCallbacks`, `Dispose`
- [ ] At least 2 ALC tests pass (may simplify if test DLL compilation proves too complex)
- [ ] All 145 existing tests still pass
- [ ] Zero build errors or warnings

---

## ⚠️ Common Pitfalls

- `FileSystemWatcher` raises events on a background thread — never call `DrainPendingCallbacks` from the watcher event, only enqueue to `ConcurrentQueue`
- `AssemblyLoadContext` must be set `isCollectible: true` for `Unload()` to work
- `WeakReference<T>` only returns null after GC runs AND no other reference holds the ALC
- Do not use `File.OpenRead` without waiting — the new DLL file might still be locked by the copy process. Add a retry loop or catch `IOException`
- `FbtRegistrarAttribute` comparison: use `type.GetCustomAttribute(typeof(FbtRegistrarAttribute)) != null` — not string comparison of attribute name
- When loading from a stream into ALC, the DLL file must be closed before the old ALC is unloaded

---

## 📚 Reference Materials

- **FBT-023 Task Detail:** `.dev/fluent-btree/TASK-DETAIL.md` → TASK-FBT-023
- **FBT-022 Task Detail:** `.dev/fluent-btree/TASK-DETAIL.md` → TASK-FBT-022
- **FbtAutoDiscovery pattern:** `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/FbtAutoDiscovery.cs`
- **FbtRegistrarAttribute:** `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Attributes/FbtRegistrarAttribute.cs`
- **BTreeHotReloadManager:** `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/HotReload/BTreeHotReloadManager.cs`
