# BATCH-46: UBP-P9T1 + P9T2 + P9T3 — Resilience Polish

**Batch Number:** BATCH-46  
**Tasks:** UBP-P9T1 (Hot-reload auto-rebind), UBP-P9T2 (Step-abandoned preemption), UBP-P9T3 (Watch persistence)  
**Design Reference:** `.dev/breakpoints-1/DESIGN.md §12`, `.dev/breakpoints-1/TASK-DETAIL.md §P9`  
**Test project:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests`  
**Prior test count:** 89

---

## Context

Read these files before starting:

1. `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/BreakpointTypes.cs` — `Breakpoint` record (full file)
2. `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/IDataBreakpointManager.cs` — interface (full file)
3. `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs` — lines 1–200 and 480–590 (`TryMountDelegate`, `UnmountDelegate`)
4. `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/BreakpointJsonClipboard.cs` — full file (will reuse for watch persistence)
5. `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/DataBreakpointManagerTests.cs` — lines 56–90 (`ManagerFactory.Create()` pattern)
6. `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/Hrot.Diagnostics.Breakpoints.csproj` — check existing references

---

## Task 1: P9T1 — Hot-reload auto-rebind

**Design:** `.dev/breakpoints-1/DESIGN.md §12.1`

### 1.1 Add `IsBroken` to `Breakpoint` record

In `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/BreakpointTypes.cs`, add this property to the `Breakpoint` record after `SourceElementId`:

```csharp
/// <summary>
/// True when the last hot-reload recompilation of this breakpoint failed.
/// The DTO is retained; the developer can fix and re-arm.
/// </summary>
public bool IsBroken { get; init; }
```

### 1.2 Add `OnHotReloadCompleted` to `IDataBreakpointManager`

In `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/IDataBreakpointManager.cs`, add to the interface:

```csharp
/// <summary>
/// Called when the hot-reload cycle completes. Drops all cached compiled delegates
/// and recompiles from retained DTOs. Marks <see cref="Breakpoint.IsBroken"/> on failure.
/// </summary>
void OnHotReloadCompleted();
```

### 1.3 Implement `OnHotReloadCompleted` in `DataBreakpointManager`

In `DataBreakpointManager.cs`, add this method in the region near `UpdateCondition`:

```csharp
/// <inheritdoc/>
public void OnHotReloadCompleted()
{
    // Take a snapshot of all IDs to avoid modifying the dict during iteration.
    var ids = new List<BreakpointId>(_breakpoints.Keys);
    foreach (var id in ids)
    {
        if (!_breakpoints.TryGetValue(id, out var bp)) continue;

        // Always drop the stale compiled delegate — stale unmanaged pointers must not survive a reload.
        UnmountDelegate(id);

        if (bp.Condition == null || !bp.Enabled) continue;

        try
        {
            TryMountDelegate(id, bp);
            // Clear any previous broken flag on successful recompile.
            if (bp.IsBroken)
                _breakpoints[id] = bp with { IsBroken = false };
        }
        catch
        {
            // Compilation failed (field removed / layout changed). Mark broken; retain DTO.
            _breakpoints[id] = bp with { IsBroken = true };
        }
    }
}
```

### 1.4 Tests for P9T1

Create `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/HotReloadResilienceTests.cs`:

```csharp
using System;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Diagnostics.Breakpoints;

namespace Hrot.Diagnostics.Breakpoints.Tests;

// ---- Test-only unmanaged component for P9T1 --------------------------------
[ComponentId(230)]
file struct ReloadTestComponent { public int Value; }

[Collection("ComponentRegistry")]
public sealed class HotReloadResilienceTests
{
    // ── P9T1 ──────────────────────────────────────────────────────────────────

    [Fact]
    public void HotReload_StructureCompatible_PreservesBreakpoint()
    {
        // Arrange: real manager with real compiler; register a PropertyMatchDto BP.
        ComponentTypeRegistry.Clear();
        ComponentTypeRegistry.Register<ReloadTestComponent>();

        var (mgr, liveRepo, _, _) = ManagerFactory.Create();
        var id = mgr.AddBreakpoint(
            new PropertyMatchDto
            {
                ComponentType = typeof(ReloadTestComponent),
                PropertyPath  = "Value",
                Predicate     = new NumericPredicateDto { MinValue = 0, MaxValue = 10 },
            },
            displayName: "ReloadTestBP");

        // Pre-check: breakpoint is mounted.
        Assert.Single(mgr.MountedComponentPredicates);
        Assert.False(mgr.AllBreakpoints.First(b => b.Id == id).IsBroken);

        // Act: simulate a hot-reload cycle (assembly reloaded; component still exists).
        mgr.OnHotReloadCompleted();

        // Assert: still mounted, not broken.
        Assert.Single(mgr.MountedComponentPredicates);
        Assert.False(mgr.AllBreakpoints.First(b => b.Id == id).IsBroken);
    }

    [Fact]
    public void HotReload_RemovesTargetedField_MarksBreakpointBroken()
    {
        // Arrange: manager with a compiler stub that throws on the second compile call.
        ComponentTypeRegistry.Clear();
        ComponentTypeRegistry.Register<ReloadTestComponent>();

        var throwingCompiler = new ThrowOnSecondCompileCompiler();
        var liveRepo         = new EntityRepository();
        var preTickSnapshot  = new EntityRepository();
        var tc               = new MockDebugTimeController();
        var snapshotProvider = new DebugSnapshotProvider(preTickSnapshot);
        var mgr = new DataBreakpointManager(
            liveRepo, preTickSnapshot, snapshotProvider, tc,
            predicateCompiler: throwingCompiler);

        var id = mgr.AddBreakpoint(
            new PropertyMatchDto
            {
                ComponentType = typeof(ReloadTestComponent),
                PropertyPath  = "Value",
                Predicate     = new NumericPredicateDto { MinValue = 0, MaxValue = 10 },
            },
            displayName: "BreaksBP");

        // First compile succeeded (from AddBreakpoint). Now simulate a reload where the field is gone.
        // Act: second compilation attempt should throw → IsBroken.
        mgr.OnHotReloadCompleted();

        // Assert: marked broken, not crashed.
        var bp = mgr.AllBreakpoints.First(b => b.Id == id);
        Assert.True(bp.IsBroken);
        // DTO retained (so the user can fix it).
        Assert.NotNull(bp.Condition);
    }

    [Fact]
    public void HotReload_NoAccessViolation_DuringActiveBreakpoint()
    {
        // Arrange: 5 breakpoints, 100 reload cycles → must not throw.
        ComponentTypeRegistry.Clear();
        ComponentTypeRegistry.Register<ReloadTestComponent>();

        var (mgr, _, _, _) = ManagerFactory.Create();
        for (int i = 0; i < 5; i++)
        {
            mgr.AddBreakpoint(
                new PropertyMatchDto
                {
                    ComponentType = typeof(ReloadTestComponent),
                    PropertyPath  = "Value",
                    Predicate     = new NumericPredicateDto { MinValue = i, MaxValue = i + 1 },
                },
                displayName: $"BP{i}");
        }

        // Act: 100 reload cycles.
        var ex = Record.Exception(() =>
        {
            for (int cycle = 0; cycle < 100; cycle++)
                mgr.OnHotReloadCompleted();
        });

        // Assert: no exception.
        Assert.Null(ex);
        // All breakpoints still exist and are not broken.
        Assert.All(mgr.AllBreakpoints, bp => Assert.False(bp.IsBroken));
    }
```

**Continue in the same file for P9T2:**

```csharp
    // ── P9T2 ──────────────────────────────────────────────────────────────────

    [Fact]
    public void HotReloadBegin_DuringPause_ForcesContinueAndFlushesMutations()
    {
        // Arrange: pause the manager with 3 staged mutations.
        ComponentTypeRegistry.Clear();
        ComponentTypeRegistry.Register<ReloadTestComponent>();

        var (mgr, liveRepo, _, tc) = ManagerFactory.Create();
        // Put the manager in paused state by firing OnHit directly.
        var bpId = mgr.AddBreakpoint(new PropertyMatchDto(), displayName: "p9t2");
        var bp   = mgr.AllBreakpoints.First();

        // Manually invoke OnHit to enter paused state.
        var entity = liveRepo.CreateEntity();
        mgr.OnHit(bp, entity);
        Assert.True(mgr.IsPaused);

        // Stage 3 mutations.
        liveRepo.AddUnmanagedComponent(entity, new ReloadTestComponent { Value = 1 });
        mgr.StageMutation(entity, typeof(ReloadTestComponent),
            new ReloadTestComponent { Value = 42 });
        mgr.StageMutation(entity, typeof(ReloadTestComponent),
            new ReloadTestComponent { Value = 43 });
        mgr.StageMutation(entity, typeof(ReloadTestComponent),
            new ReloadTestComponent { Value = 44 });
        Assert.Equal(3, mgr.PendingMutationsCount);

        // Act: hot reload begins.
        mgr.OnHotReloadBegin();

        // Assert: unpaused, mutations flushed.
        Assert.False(mgr.IsPaused);
        Assert.Equal(0, mgr.PendingMutationsCount);
    }

    [Fact]
    public void Notification_StepAbandoned_Emitted()
    {
        // Arrange: manager with a notifier stub; put in paused state.
        ComponentTypeRegistry.Clear();
        ComponentTypeRegistry.Register<ReloadTestComponent>();

        var notifier = new RecordingBreakpointNotifier();
        var liveRepo         = new EntityRepository();
        var preTickSnapshot  = new EntityRepository();
        var tc               = new MockDebugTimeController();
        var snapshotProvider = new DebugSnapshotProvider(preTickSnapshot);
        var mgr = new DataBreakpointManager(
            liveRepo, preTickSnapshot, snapshotProvider, tc,
            notifier: notifier);

        var bpId = mgr.AddBreakpoint(new PropertyMatchDto(), displayName: "notif-bp");
        var bp   = mgr.AllBreakpoints.First();
        var entity = liveRepo.CreateEntity();
        mgr.OnHit(bp, entity);
        Assert.True(mgr.IsPaused);

        // Act: hot reload begins.
        mgr.OnHotReloadBegin();

        // Assert: notification emitted.
        Assert.Single(notifier.Messages);
        Assert.Contains("abandoned", notifier.Messages[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HotReloadBegin_WhenNotPaused_DoesNothing()
    {
        ComponentTypeRegistry.Clear();
        var (mgr, _, _, _) = ManagerFactory.Create();

        var ex = Record.Exception(() => mgr.OnHotReloadBegin());

        Assert.Null(ex);
        Assert.False(mgr.IsPaused);
        Assert.Equal(0, mgr.PendingMutationsCount);
    }
}
```

**Create test helper types at the bottom of the file (or as `file` classes in the same file):**

```csharp
// ---- Test helpers -----------------------------------------------------------

/// <summary>
/// An IPredicateCompiler that succeeds on the first compile per DTO instance,
/// then throws on subsequent calls — simulates a "field removed after hot-reload" scenario.
/// </summary>
file sealed class ThrowOnSecondCompileCompiler : IPredicateCompiler
{
    private int _callCount;

    public Func<EntityRepository, Entity, bool> CompileComponentPredicate(SearchPredicateDto dto)
    {
        if (_callCount++ >= 1)
            throw new InvalidOperationException("Simulated recompile failure: field removed.");
        // Delegate that always returns false (never fires) — enough for mounting.
        return static (_, _) => false;
    }

    public IReadOnlyList<Type> ExtractMandatoryComponents(SearchPredicateDto dto) =>
        Array.Empty<Type>();
}

/// <summary>
/// Captures Notify calls for assertion in P9T2 tests.
/// </summary>
file sealed class RecordingBreakpointNotifier : IBreakpointNotifier
{
    public List<string> Messages { get; } = new();
    public void Notify(string message) => Messages.Add(message);
}
```

**Close the namespace before the helper types (use file-scoped namespace or move types outside the class):
Note:** All `file` types must be at the **namespace** level (i.e., outside any class), not inside a class. Place them after the last `}` of the test class.

---

## Task 2: P9T2 — "Step abandoned" preemption

### 2.1 Create `IBreakpointNotifier` interface

Create `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/IBreakpointNotifier.cs`:

```csharp
namespace Hrot.Diagnostics.Breakpoints;

/// <summary>
/// Simple toast notification surface. Implement to forward to
/// <c>NodeEditor.Core.Action.IEditorIndicators.Notify</c> or similar.
/// </summary>
public interface IBreakpointNotifier
{
    void Notify(string message);
}
```

### 2.2 Update `DataBreakpointManager` constructor

Add optional `IBreakpointNotifier?` parameter to the constructor:

```csharp
public DataBreakpointManager(
    EntityRepository liveRepo,
    EntityRepository preTickSnapshot,
    DebugSnapshotProvider snapshotProvider,
    IEngineDebugTimeController timeController,
    IPredicateCompiler? predicateCompiler = null,
    IEventScannerCompiler? eventScannerCompiler = null,
    IBreakpointNotifier? notifier = null)
{
    // ... existing assignments ...
    _notifier = notifier;
}
```

Add field: `private readonly IBreakpointNotifier? _notifier;`

### 2.3 Add `OnHotReloadBegin` to `IDataBreakpointManager`

```csharp
/// <summary>
/// Called when a hot-reload cycle begins. If currently paused, forces
/// <see cref="RequestContinue"/>, flushes pending mutations, and notifies the user.
/// </summary>
void OnHotReloadBegin();
```

### 2.4 Implement `OnHotReloadBegin` in `DataBreakpointManager`

```csharp
/// <inheritdoc/>
public void OnHotReloadBegin()
{
    if (!_isPaused) return;

    // Force continue — time unfreezes, snapshot lock released.
    RequestContinue();

    // Flush pending mutations (byte offsets are invalid against new layout).
    _pendingMutations.Clear();

    // Notify operator.
    _notifier?.Notify("Step abandoned due to reload");
}
```

**Note:** `RequestContinue()` already restores post-tick snapshot and calls `_timeController.RequestResume()`. After calling it, `_isPaused` will be false. `_pendingMutations.Clear()` happens after because `RequestContinue` drains mutations into the ECB — but for hot-reload we want to DISCARD them (not apply stale mutations to the new layout). 

**Important implementation note:** `RequestContinue()` currently drains mutations via `DrainPendingMutations`. For hot-reload, we want to **discard** them. Two approaches:
1. Clear the queue **before** calling `RequestContinue()` (so it drains nothing) — then call `RequestContinue()`.
2. Call `RequestContinue()` then clear whatever might be left.

**Use approach 1**: clear queue first, then `RequestContinue()`. This ensures stale mutations are never applied:

```csharp
public void OnHotReloadBegin()
{
    if (!_isPaused) return;

    // Discard stale mutations before RequestContinue drains them.
    _pendingMutations.Clear();

    // Force unfreeze (restores post-tick snapshot, resumes time controller).
    RequestContinue();

    // Notify operator.
    _notifier?.Notify("Step abandoned due to reload");
}
```

---

## Task 3: P9T3 — Watch persistence

### 3.1 Add `IsWatch` to `Breakpoint` record

In `BreakpointTypes.cs`, add to `Breakpoint` record:

```csharp
/// <summary>
/// When true, this breakpoint is a "watch" entry — persisted to watches.json
/// and shown in the Watch panel.
/// </summary>
public bool IsWatch { get; init; }
```

### 3.2 Create `WatchPersistence` helper

Create `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/WatchPersistence.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fdp.Toolkit.ReplayBrowser.Search;

namespace Hrot.Diagnostics.Breakpoints;

/// <summary>
/// Serializable DTO for a persisted watch entry.
/// </summary>
internal sealed class WatchEntry
{
    public string DisplayName { get; set; } = string.Empty;
    public SearchPredicateDto? Condition { get; set; }
}

/// <summary>
/// Saves and loads watch entries to/from a JSON file.
/// </summary>
public static class WatchPersistence
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        WriteIndented    = true,
        IncludeFields    = true,
    };

    /// <summary>
    /// Serializes all watch-flagged breakpoints to <paramref name="path"/>.
    /// Creates or overwrites the file.
    /// </summary>
    public static void Save(IReadOnlyList<Breakpoint> breakpoints, string path)
    {
        var entries = new List<WatchEntry>();
        foreach (var bp in breakpoints)
        {
            if (!bp.IsWatch) continue;
            entries.Add(new WatchEntry
            {
                DisplayName = bp.DisplayName,
                Condition   = bp.Condition,
            });
        }
        var json = JsonSerializer.Serialize(entries, s_options);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Deserializes watch entries from <paramref name="path"/>.
    /// Returns an empty list if the file does not exist or is malformed.
    /// </summary>
    public static IReadOnlyList<WatchEntry> TryLoad(string path)
    {
        if (!File.Exists(path)) return Array.Empty<WatchEntry>();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<WatchEntry>>(json, s_options)
                   ?? Array.Empty<WatchEntry>();
        }
        catch
        {
            return Array.Empty<WatchEntry>();
        }
    }
}
```

### 3.3 Add `SaveWatches` / `LoadWatches` to `DataBreakpointManager`

Add to `IDataBreakpointManager`:

```csharp
/// <summary>Persists all watch-flagged breakpoints to <paramref name="path"/>.</summary>
void SaveWatches(string path);

/// <summary>
/// Restores watch entries from <paramref name="path"/>. Attempts to recompile each
/// condition; marks <see cref="Breakpoint.IsBroken"/> on schema mismatch.
/// </summary>
void LoadWatches(string path);
```

Implement in `DataBreakpointManager`:

```csharp
/// <inheritdoc/>
public void SaveWatches(string path) =>
    WatchPersistence.Save(AllBreakpoints, path);

/// <inheritdoc/>
public void LoadWatches(string path)
{
    var entries = WatchPersistence.TryLoad(path);
    foreach (var entry in entries)
    {
        var id = AddBreakpoint(entry.Condition!, displayName: entry.DisplayName);

        // If compilation failed (IsBroken set internally by TryMountDelegate failure),
        // we need to detect that. Check if mounted count changed:
        // Actually: TryMountDelegate does NOT catch exceptions — we need to detect broken state here.
        // The simplest approach: after Add, if condition is not null but no delegate is mounted,
        // mark it broken (schema drifted).
        var bp = _breakpoints[id];
        bool mounted = _componentPredicates.ContainsKey(id) || _eventScanners.ContainsKey(id)
            || _structuralTrackers.ContainsKey(id) || _spatialTrackers.ContainsKey(id)
            || _lifecycleTrackers.ContainsKey(id) || HasExternalHitTag(bp.Condition);

        if (entry.Condition != null && !mounted)
        {
            _breakpoints[id] = bp with { IsWatch = true, IsBroken = true };
        }
        else
        {
            _breakpoints[id] = bp with { IsWatch = true };
        }
    }
}
```

**Note on `AddBreakpoint` for null condition:** `AddBreakpoint(SearchPredicateDto condition, ...)` requires a non-null condition. If a loaded entry has `Condition == null`, skip it (guard: `if (entry.Condition == null) continue;`).

### 3.4 Tests for P9T3

Add to `HotReloadResilienceTests.cs` (or a new `WatchPersistenceTests.cs`). Create a **separate** file `WatchPersistenceTests.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Diagnostics.Breakpoints;

namespace Hrot.Diagnostics.Breakpoints.Tests;

[ComponentId(231)]
file struct WatchTestComponent { public int Value; }

[Collection("ComponentRegistry")]
public sealed class WatchPersistenceTests
{
    // ── P9T3 ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Watches_PersistAcrossRestart_StructureCompatible()
    {
        ComponentTypeRegistry.Clear();
        ComponentTypeRegistry.Register<WatchTestComponent>();

        var path = Path.Combine(Path.GetTempPath(), $"watches_test_{Guid.NewGuid():N}.json");
        try
        {
            // Arrange: manager1 with 3 watch-flagged breakpoints.
            var (mgr1, _, _, _) = ManagerFactory.Create();
            for (int i = 0; i < 3; i++)
            {
                var id = mgr1.AddBreakpoint(
                    new PropertyMatchDto
                    {
                        ComponentType = typeof(WatchTestComponent),
                        PropertyPath  = "Value",
                        Predicate     = new NumericPredicateDto { MinValue = i, MaxValue = i + 10 },
                    },
                    displayName: $"Watch{i}");
                // Mark as watch.
                mgr1.MarkAsWatch(id, true);
            }
            Assert.Equal(3, mgr1.AllBreakpoints.Count(b => b.IsWatch));

            // Act: save watches from mgr1, restore into mgr2 (simulates restart).
            mgr1.SaveWatches(path);

            var (mgr2, _, _, _) = ManagerFactory.Create();
            mgr2.LoadWatches(path);

            // Assert: 3 watches restored, none broken.
            var watches = mgr2.AllBreakpoints.Where(b => b.IsWatch).ToList();
            Assert.Equal(3, watches.Count);
            Assert.All(watches, bp => Assert.False(bp.IsBroken));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Watches_Restore_FailsGracefullyOnDriftedSchema()
    {
        ComponentTypeRegistry.Clear();
        ComponentTypeRegistry.Register<WatchTestComponent>();

        var path = Path.Combine(Path.GetTempPath(), $"watches_drift_{Guid.NewGuid():N}.json");
        try
        {
            // Arrange: save a watch pointing to WatchTestComponent.
            var (mgr1, _, _, _) = ManagerFactory.Create();
            var id = mgr1.AddBreakpoint(
                new PropertyMatchDto
                {
                    ComponentType = typeof(WatchTestComponent),
                    PropertyPath  = "Value",
                    Predicate     = new NumericPredicateDto { MinValue = 0, MaxValue = 100 },
                },
                displayName: "DriftedWatch");
            mgr1.MarkAsWatch(id, true);
            mgr1.SaveWatches(path);

            // Simulate schema drift: clear registry (component no longer registered).
            ComponentTypeRegistry.Clear();

            // Act: load into a fresh manager with cleared registry (compilation will fail).
            var liveRepo         = new EntityRepository();
            var preTickSnapshot  = new EntityRepository();
            var tc               = new MockDebugTimeController();
            var snapshotProvider = new DebugSnapshotProvider(preTickSnapshot);
            var failingCompiler  = new AlwaysThrowCompiler();
            var mgr2 = new DataBreakpointManager(
                liveRepo, preTickSnapshot, snapshotProvider, tc,
                predicateCompiler: failingCompiler);

            // Should not throw.
            var ex = Record.Exception(() => mgr2.LoadWatches(path));
            Assert.Null(ex);

            // Assert: watch is present but marked broken (not silently discarded).
            var watches = mgr2.AllBreakpoints.Where(b => b.IsWatch).ToList();
            Assert.Single(watches);
            Assert.True(watches[0].IsBroken);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

// ---- Test helpers -----------------------------------------------------------

file sealed class AlwaysThrowCompiler : IPredicateCompiler
{
    public Func<EntityRepository, Entity, bool> CompileComponentPredicate(SearchPredicateDto dto)
        => throw new InvalidOperationException("Schema drifted — component not registered.");

    public IReadOnlyList<Type> ExtractMandatoryComponents(SearchPredicateDto dto) =>
        Array.Empty<Type>();
}
```

**Note on `MarkAsWatch`:** You need to add a `MarkAsWatch(BreakpointId id, bool isWatch)` method to `DataBreakpointManager` and `IDataBreakpointManager`:

```csharp
// IDataBreakpointManager:
void MarkAsWatch(BreakpointId id, bool isWatch);

// DataBreakpointManager:
public void MarkAsWatch(BreakpointId id, bool isWatch)
{
    if (!_breakpoints.TryGetValue(id, out var bp)) return;
    _breakpoints[id] = bp with { IsWatch = isWatch };
}
```

---

## Implementation summary: all changes

### Files to modify:

| File | Change |
|------|--------|
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/BreakpointTypes.cs` | Add `IsBroken: bool` and `IsWatch: bool` properties to `Breakpoint` record |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/IDataBreakpointManager.cs` | Add `OnHotReloadCompleted()`, `OnHotReloadBegin()`, `SaveWatches(string)`, `LoadWatches(string)`, `MarkAsWatch(BreakpointId, bool)` |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs` | Add `_notifier` field; update constructor; implement `OnHotReloadCompleted`, `OnHotReloadBegin`, `SaveWatches`, `LoadWatches`, `MarkAsWatch` |

### Files to create:

| File | Purpose |
|------|---------|
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/IBreakpointNotifier.cs` | New interface |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/WatchPersistence.cs` | Save/load helpers |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/HotReloadResilienceTests.cs` | P9T1 (3 tests) + P9T2 (3 tests) |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/WatchPersistenceTests.cs` | P9T3 (2 tests) |

---

## Critical implementation notes

### `TryMountDelegate` does NOT currently catch exceptions
Looking at the current `TryMountDelegate` implementation (in `DataBreakpointManager.cs` around line 480), it calls `_predicateCompiler.CompileComponentPredicate(bp.Condition)` without a try/catch. This means:
- During normal `Add`, if the compiler throws, the exception propagates up.
- For `OnHotReloadCompleted`, the outer try/catch handles it correctly — this is fine by design.
- For `LoadWatches`, we also wrap in try/catch internally.

**You do NOT need to modify `TryMountDelegate` itself.** The outer try/catch in `OnHotReloadCompleted` and `LoadWatches` handles failures.

### `OnHotReloadBegin` mutation discard order
Clear mutations BEFORE calling `RequestContinue()`. The current `RequestContinue` drains the queue into the ECB; we want to DISCARD stale mutations, not apply them.

```csharp
public void OnHotReloadBegin()
{
    if (!_isPaused) return;
    _pendingMutations.Clear();          // discard BEFORE RequestContinue drains
    RequestContinue();                  // unfreezes time, restores post-tick snapshot
    _notifier?.Notify("Step abandoned due to reload");
}
```

### `LoadWatches` broken detection
`TryMountDelegate` does not set `IsBroken` — it may silently skip compilation (if compiler is null) OR throw. In `LoadWatches`, wrap `AddBreakpoint` in try/catch:

```csharp
public void LoadWatches(string path)
{
    var entries = WatchPersistence.TryLoad(path);
    foreach (var entry in entries)
    {
        if (entry.Condition == null) continue;

        bool broken = false;
        BreakpointId id;
        try
        {
            id = AddBreakpoint(entry.Condition, displayName: entry.DisplayName);
        }
        catch
        {
            // Compilation failed during Add — add disabled and mark broken.
            var disabledBp = new Breakpoint
            {
                Id          = BreakpointId.Invalid,
                Condition   = entry.Condition,
                Enabled     = false,
                DisplayName = entry.DisplayName,
                IsWatch     = true,
                IsBroken    = true,
            };
            Add(disabledBp);
            // The Add with Enabled=false won't try to mount, so no exception from Add itself.
            continue;
        }

        // Mark as watch (AddBreakpoint doesn't set IsWatch).
        if (_breakpoints.TryGetValue(id, out var bp))
            _breakpoints[id] = bp with { IsWatch = true };
    }
}
```

Wait — there's a problem with the above: `AddBreakpoint` calls `Add` which calls `TryMountDelegate` which calls the compiler. If the compiler throws, the exception propagates out of `AddBreakpoint`. So wrapping `AddBreakpoint` in try/catch works.

But then for the "add disabled and mark broken" path, we can't use `AddBreakpoint` (which always enables). We use `Add(Breakpoint)` directly:

```csharp
var failedBp = new Breakpoint
{
    Id          = BreakpointId.Invalid,  // overwritten by Add
    Condition   = entry.Condition,
    Enabled     = false,        // don't try to mount
    DisplayName = entry.DisplayName,
    IsWatch     = true,
    IsBroken    = true,
};
Add(failedBp);
```

But `Add(Breakpoint)` with `Enabled = false` should not call `TryMountDelegate` (check the `Add` implementation). Looking at the `Add` method:
```csharp
if (registered.Enabled)
{
    AdjustGate(+1);
    TryMountDelegate(id, registered);
}
```
Yes, `Enabled = false` skips `TryMountDelegate`. Good.

### `ComponentId` values
- 230 is used for `ReloadTestComponent` in `HotReloadResilienceTests.cs`
- 231 is used for `WatchTestComponent` in `WatchPersistenceTests.cs`

Check existing test files for which values are taken:
- ExternalHitTagTests.cs: 220
- ManagerWindowTests.cs: 221
- PredicateBuilderStateTests.cs: 222

So 230 and 231 should be safe, but **verify** by grepping the test directory for `[ComponentId(2` before using them.

### `file` class placement
All `file` sealed class helpers (e.g., `ThrowOnSecondCompileCompiler`, `RecordingBreakpointNotifier`, `AlwaysThrowCompiler`) must be at the **namespace level** (outside any class). In a file-scoped namespace, this means after the last `}` of the test class.

---

## Build and test

```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/Hrot.Diagnostics.Breakpoints.csproj
dotnet build Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/Hrot.Diagnostics.Breakpoints.Tests.csproj
dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/Hrot.Diagnostics.Breakpoints.Tests.csproj
```

All 89 existing tests must still pass. New tests should add ≥ 8 (P9T1: 3, P9T2: 3, P9T3: 2), total ≥ 97.

---

## Report

Provide a detailed report including:
1. All files modified/created
2. Test count before and after (must be ≥ 97)
3. Any deviations from these instructions (with justifications)
4. Build output (zero errors, zero new warnings)
5. The exact signatures of `OnHotReloadCompleted`, `OnHotReloadBegin`, `SaveWatches`, `LoadWatches`, `MarkAsWatch`
