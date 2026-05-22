# BATCH-21: TASK-ED-001 -- Editor Infrastructure, Window Lifecycle, IWindowRegistrar

**Batch Number:** BATCH-21
**Tasks:** TASK-ED-001
**Phase:** 6 -- Editor
**Estimated Effort:** 2-3 days
**Priority:** HIGH
**Dependencies:** All Phase 5 Debug Protocol tasks complete. Phase 6 starts here.

---

## 0. Onboarding

### Required Reading

1. `.dev/blueprints-1/reviews/BATCH-20-REVIEW.md` -- current state
2. `.dev/blueprints-1/TASK-DETAIL.md` §ED-001 -- full scope
3. `.dev/blueprints-1/Blueprint_Subsystem_Editor_Detailed_Design.md` §1, §2, §3, §13 (infrastructure + window lifecycle + IWindowRegistrar + time-controller adapter)
4. `.dev/blueprints-1/Blueprint_Subsystem_Editor_Detailed_Design_InlinePatches.md` -- Patches 1-3 (especially Patch 2: `ReloadCompletedInfo`)
5. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Hrot.Blueprints.Editor.csproj` -- existing project
6. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/IBlueprintDebugSession.cs` -- interface reference

### Source Code Locations

All new production code goes in: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/`
Tests go in: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/` (new subfolder)

### Report Submission

`.dev/blueprints-1/reports/BATCH-21-REPORT.md`

---

## 1. IBlueprintEditorWindow and BlueprintEditorWindowBase

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/IBlueprintEditorWindow.cs`:

```csharp
namespace Hrot.Blueprints.Editor;

public interface IBlueprintEditorWindow
{
    string Title { get; }
    bool IsVisible { get; set; }
    void ToggleVisible();
    void DrawUI();
    void OnActivated();
    void OnDeactivated();
}
```

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintEditorWindowBase.cs`:

```csharp
namespace Hrot.Blueprints.Editor;

public abstract class BlueprintEditorWindowBase : IBlueprintEditorWindow
{
    public abstract string Title { get; }
    public bool IsVisible { get; set; }
    public void ToggleVisible() => IsVisible = !IsVisible;
    public abstract void DrawUI();
    public virtual void OnActivated()   { }
    public virtual void OnDeactivated() { }
}
```

---

## 2. DirtyTracker

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/DirtyTracker.cs`:

```csharp
namespace Hrot.Blueprints.Editor;

public sealed class DirtyTracker
{
    private readonly HashSet<Guid> _dirty = new();

    public void MarkDirty(Guid assetId)  => _dirty.Add(assetId);
    public void MarkClean(Guid assetId)  => _dirty.Remove(assetId);
    public bool IsDirty(Guid assetId)    => _dirty.Contains(assetId);
    public IReadOnlySet<Guid> DirtyAssets => _dirty;
}
```

---

## 3. EditorSelectionStore

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/EditorSelectionStore.cs`:

```csharp
using Fdp.Toolkit.Blueprints;

namespace Hrot.Blueprints.Editor;

public sealed class EditorSelectionStore
{
    private BlueprintAsset? _selected;

    public BlueprintAsset? SelectedAsset => _selected;

    public event Action? OnSelectionChanged;

    public void SelectAsset(BlueprintAsset? asset)
    {
        _selected = asset;
        OnSelectionChanged?.Invoke();
    }
}
```

---

## 4. IOutputConsole

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/IOutputConsole.cs`:

```csharp
using Microsoft.CodeAnalysis;

namespace Hrot.Blueprints.Editor;

public interface IOutputConsole
{
    void LogInfo(string message);
    void LogWarning(string message);
    void LogError(string message);
    void LogDebug(string message);
    void LogDiagnostic(Diagnostic diagnostic);
}
```

Note: `Microsoft.CodeAnalysis` (Roslyn) is already a transitive dependency through `Hrot.Blueprints.Core`. Verify this is available in the Editor project before using it. If not, add the package reference.

---

## 5. EditorState

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/EditorState.cs`:

```csharp
using Fdp.Toolkit.Blueprints;

namespace Hrot.Blueprints.Editor;

public sealed class EditorState
{
    private readonly Dictionary<Guid, BlueprintAsset> _inMemory = new();

    public void SetInMemoryAsset(BlueprintAsset asset)
        => _inMemory[asset.AssetId] = asset;

    public BlueprintAsset? GetInMemoryAsset(Guid assetId)
        => _inMemory.TryGetValue(assetId, out var a) ? a : null;

    public void RemoveInMemoryAsset(Guid assetId)
        => _inMemory.Remove(assetId);

    public IReadOnlyDictionary<Guid, BlueprintAsset> InMemoryAssets => _inMemory;
}
```

---

## 6. IAssetCatalog and FileSystemAssetCatalog

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/IAssetCatalog.cs`:

```csharp
namespace Hrot.Blueprints.Editor;

public sealed record AssetCatalogEntry(Guid AssetId, string Path);

public interface IAssetCatalog
{
    IEnumerable<AssetCatalogEntry> EnumerateAll();
}
```

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/FileSystemAssetCatalog.cs`:

```csharp
using System.Text.Json;
using Fdp.Toolkit.Blueprints;

namespace Hrot.Blueprints.Editor;

public sealed class FileSystemAssetCatalog : IAssetCatalog
{
    private readonly string _rootDirectory;

    public FileSystemAssetCatalog(string rootDirectory)
    {
        _rootDirectory = rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory));
    }

    public IEnumerable<AssetCatalogEntry> EnumerateAll()
    {
        if (!Directory.Exists(_rootDirectory))
            yield break;

        foreach (var filePath in Directory.EnumerateFiles(
            _rootDirectory, "*.bp.json", SearchOption.AllDirectories))
        {
            Guid assetId;
            try
            {
                // Attempt to read AssetId from the JSON file header.
                var json = File.ReadAllText(filePath);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("AssetId", out var idEl) ||
                    !idEl.TryGetGuid(out assetId))
                    continue;
            }
            catch
            {
                continue;  // Skip unreadable/malformed files.
            }

            yield return new AssetCatalogEntry(assetId, filePath);
        }
    }
}
```

---

## 7. ReloadCompletedInfo and ReloadSource (Patch 2)

These types are required by the Editor module to discriminate Quick vs Full reload in the `OnReloadCompleted` handler. Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/ReloadInfo.cs`:

```csharp
namespace Hrot.Blueprints.Editor;

public enum ReloadSource
{
    QuickReloadViaApi,
    FullRebuildViaFileWatcher,
}

public sealed record ReloadCompletedInfo(
    ReloadSource Source,
    Guid[] ReloadedAssetIds,
    string? DllPath,
    long DurationMs);
```

---

## 8. BlueprintEditorModule

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintEditorModule.cs`:

```csharp
namespace Hrot.Blueprints.Editor;

/// <summary>
/// Entry point for the Blueprint editor integration. Owns all editor windows,
/// handles reload events, and wires the debug session lifecycle.
/// </summary>
public sealed class BlueprintEditorModule
{
    private readonly IWindowRegistrar _windowRegistrar;
    private readonly DirtyTracker _dirtyTracker;
    private readonly EditorSelectionStore _selectionStore;
    private readonly EditorState _editorState;
    private readonly IAssetCatalog _catalog;
    private readonly IOutputConsole _outputConsole;

    private readonly List<IBlueprintEditorWindow> _windows = new();
    private bool _activated;

    public BlueprintEditorModule(
        IWindowRegistrar windowRegistrar,
        DirtyTracker dirtyTracker,
        EditorSelectionStore selectionStore,
        EditorState editorState,
        IAssetCatalog catalog,
        IOutputConsole outputConsole)
    {
        _windowRegistrar = windowRegistrar ?? throw new ArgumentNullException(nameof(windowRegistrar));
        _dirtyTracker    = dirtyTracker    ?? throw new ArgumentNullException(nameof(dirtyTracker));
        _selectionStore  = selectionStore  ?? throw new ArgumentNullException(nameof(selectionStore));
        _editorState     = editorState     ?? throw new ArgumentNullException(nameof(editorState));
        _catalog         = catalog         ?? throw new ArgumentNullException(nameof(catalog));
        _outputConsole   = outputConsole   ?? throw new ArgumentNullException(nameof(outputConsole));
    }

    public void OnEditorActivated()
    {
        if (_activated) return;
        _activated = true;

        // Register menu entries for each window via IWindowRegistrar.
        foreach (var window in _windows)
            _windowRegistrar.RegisterMenuEntry($"Blueprint/{window.Title}", () => window.ToggleVisible());

        foreach (var window in _windows)
            window.OnActivated();
    }

    public void OnEditorDeactivated()
    {
        if (!_activated) return;
        _activated = false;

        foreach (var window in _windows)
            window.OnDeactivated();
    }

    public void RegisterWindow(IBlueprintEditorWindow window)
        => _windows.Add(window ?? throw new ArgumentNullException(nameof(window)));

    public IReadOnlyList<IBlueprintEditorWindow> Windows => _windows;

    /// <summary>
    /// Called by the editor frame loop. Draws all visible windows.
    /// </summary>
    public void DrawAllWindows()
    {
        foreach (var window in _windows)
            if (window.IsVisible) window.DrawUI();
    }

    /// <summary>
    /// Called when a reload completes (from either Quick Reload or Full Rebuild).
    /// Routes by source per Patch 2.
    /// </summary>
    public void OnReloadCompleted(ReloadCompletedInfo info)
    {
        if (info.Source == ReloadSource.QuickReloadViaApi)
        {
            // Map already registered by QuickReloadService before apply.
            // Nothing to do here for map registration.
            _outputConsole.LogInfo(
                $"Quick reload completed in {info.DurationMs}ms " +
                $"({info.ReloadedAssetIds.Length} asset(s)).");
        }
        else if (info.Source == ReloadSource.FullRebuildViaFileWatcher)
        {
            // Read debug maps from DLL output directory (DllPath is set for full rebuilds).
            if (info.DllPath != null)
                _outputConsole.LogInfo($"Full rebuild completed: {info.DllPath}");
        }
    }
}
```

---

## 9. IWindowRegistrar

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/IWindowRegistrar.cs`:

```csharp
namespace Hrot.Blueprints.Editor;

public interface IWindowRegistrar
{
    void RegisterMenuEntry(string path, Action onSelected);
    void RegisterToolbarEntry(string label, Action onClicked);
    void RegisterShortcut(string keybind, Action onTriggered);
}
```

---

## 10. EngineTimeControllerAdapter stub

Per ED-001 scope, create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/EngineTimeControllerAdapter.cs`:

The concrete engine time-control type is resolved during M13 implementation. For now, create a stub that implements `IBlueprintTimeController` using a constructor parameter of type `object` (actual type TBD):

```csharp
using Hrot.Blueprints.Core.Debug;

namespace Hrot.Blueprints.Editor;

/// <summary>
/// Adapter from the engine's time-control mechanism to IBlueprintTimeController.
/// The concrete engine type is discovered during M13 implementation (Q-16.1).
/// This stub satisfies the interface contract for testing and dependency injection.
/// </summary>
public sealed class EngineTimeControllerAdapter : IBlueprintTimeController
{
    // Actual engine reference stored as object until the concrete type is known.
    private readonly object _engineController;

    public EngineTimeControllerAdapter(object engineController)
    {
        _engineController = engineController ?? throw new ArgumentNullException(nameof(engineController));
    }

    public void RequestPause()
    {
        // TODO M13: invoke engine pause via _engineController when type is known.
    }

    public void RequestResume()
    {
        // TODO M13: invoke engine resume via _engineController when type is known.
    }

    public void RequestStepOneTick()
    {
        // TODO M13: invoke engine step via _engineController when type is known.
    }

    public bool IsPausedByDebugger => false;  // TODO M13: read from engine.
}
```

---

## 11. DI Registration Helper

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintEditorServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace Hrot.Blueprints.Editor;

public static class BlueprintEditorServiceCollectionExtensions
{
    public static IServiceCollection AddBlueprintEditor(
        this IServiceCollection services,
        string assetRootDirectory)
    {
        services.AddSingleton<DirtyTracker>();
        services.AddSingleton<EditorSelectionStore>();
        services.AddSingleton<EditorState>();
        services.AddSingleton<IAssetCatalog>(_ => new FileSystemAssetCatalog(assetRootDirectory));
        services.AddSingleton<BlueprintEditorModule>();
        return services;
    }
}
```

Note: This requires `Microsoft.Extensions.DependencyInjection`. Check if the Editor project already has it. If not, add:
```xml
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.0" />
```

---

## 12. Tests Required

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/` directory with:

### EditorInfrastructureTests.cs

**SC1: `DirtyTracker_MarkDirty_ThenIsDirty`**
- `tracker.MarkDirty(id)` -> `IsDirty(id) == true`.

**SC2: `DirtyTracker_MarkClean_AfterDirty`**
- `tracker.MarkDirty(id)` -> `tracker.MarkClean(id)` -> `IsDirty(id) == false`.

**SC3: `EditorSelectionStore_SelectAsset_FiresEvent`**
- Subscribe to `OnSelectionChanged`. Call `SelectAsset(asset)`. Assert event fired once.

**SC4: `EditorSelectionStore_SelectAsset_UpdatesSelectedAsset`**
- `SelectAsset(assetA)` -> `SelectedAsset == assetA`.

**SC5: `EditorState_SetAndGet_InMemoryAsset`**
- `SetInMemoryAsset(asset)` -> `GetInMemoryAsset(asset.AssetId) == asset`.

**SC6: `EditorState_RemoveInMemoryAsset`**
- `SetInMemoryAsset(asset)` -> `RemoveInMemoryAsset(asset.AssetId)` -> `GetInMemoryAsset(asset.AssetId) == null`.

**SC7: `FileSystemAssetCatalog_EmptyDirectory_EnumeratesNone`**
- Create temp directory with no files. `EnumerateAll()` returns no entries.

**SC8: `BlueprintEditorModule_OnEditorActivated_RegistersMenuEntries`**
- Create `MockWindowRegistrar`. Create module with 2 windows. `OnEditorActivated()`.
- Assert `MockWindowRegistrar.MenuEntries.Count == 2`.

**SC9: `BlueprintEditorModule_DrawAllWindows_OnlyDrawsVisible`**
- Create 2 windows: one `IsVisible = true`, one `IsVisible = false`. `DrawAllWindows()`.
- Assert `DrawCallCount` on visible window == 1, invisible == 0.

**SC10: `EngineTimeControllerAdapter_ImplementsInterface`**
- `new EngineTimeControllerAdapter(new object())` implements `IBlueprintTimeController`.
- Calling all 3 methods must not throw.

### MockWindowRegistrar.cs (test helper)

```csharp
using Hrot.Blueprints.Editor;

namespace Hrot.Blueprints.Tests.Editor;

public sealed class MockWindowRegistrar : IWindowRegistrar
{
    public List<(string Path, Action OnSelected)> MenuEntries { get; } = new();
    public List<(string Label, Action OnClicked)> ToolbarEntries { get; } = new();
    public List<(string Keybind, Action OnTriggered)> Shortcuts { get; } = new();

    public void RegisterMenuEntry(string path, Action onSelected)
        => MenuEntries.Add((path, onSelected));

    public void RegisterToolbarEntry(string label, Action onClicked)
        => ToolbarEntries.Add((label, onClicked));

    public void RegisterShortcut(string keybind, Action onTriggered)
        => Shortcuts.Add((keybind, onTriggered));
}
```

### CountingWindow.cs (test helper)

```csharp
using Hrot.Blueprints.Editor;

namespace Hrot.Blueprints.Tests.Editor;

public sealed class CountingWindow : BlueprintEditorWindowBase
{
    public int DrawCallCount { get; private set; }
    public override string Title { get; }

    public CountingWindow(string title) => Title = title;

    public override void DrawUI() => DrawCallCount++;
}
```

---

## 13. Verification

```powershell
dotnet build Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor -v quiet
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests --filter "FullyQualifiedName~Editor" -v minimal
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests -v minimal
```

Expected: 0 errors, 0 failures. Total count >= 439 (429 + 10 new tests).

---

## 14. Mandatory Task Progression

1. Read all required docs.
2. Check if the Editor csproj already has the necessary package references (`Microsoft.CodeAnalysis`, `Microsoft.Extensions.DependencyInjection`).
3. Create all production files in Editor project (in order: IBlueprintEditorWindow, BlueprintEditorWindowBase, DirtyTracker, EditorSelectionStore, IOutputConsole, EditorState, IAssetCatalog, FileSystemAssetCatalog, ReloadInfo, IWindowRegistrar, BlueprintEditorModule, EngineTimeControllerAdapter, DI extensions).
4. Build Editor project: `dotnet build Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor`. Fix all errors.
5. Create test helpers: MockWindowRegistrar, CountingWindow.
6. Create `EditorInfrastructureTests.cs` (10 tests).
7. Build Tests project and fix errors.
8. Run `--filter "FullyQualifiedName~Editor"` tests. Fix failures.
9. Run full suite. Fix failures.
10. Commit.
11. Write report.

**DO NOT STOP.** Complete all tasks.

---

## 15. Commit

```powershell
cd d:\WORK\IOS-IG-SimHost-FDP
git add Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/
git add Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/
git commit -m "feat(blueprints): BATCH-21 ED-001 editor infrastructure windows and DI

- IBlueprintEditorWindow + BlueprintEditorWindowBase
- DirtyTracker: MarkDirty/MarkClean/IsDirty/DirtyAssets
- EditorSelectionStore: SelectAsset/SelectedAsset/OnSelectionChanged
- IOutputConsole interface
- EditorState: SetInMemoryAsset/GetInMemoryAsset/RemoveInMemoryAsset
- IAssetCatalog + AssetCatalogEntry + FileSystemAssetCatalog (*.bp.json walk)
- ReloadCompletedInfo + ReloadSource enum (Patch 2 discrimination)
- IWindowRegistrar: RegisterMenuEntry/RegisterToolbarEntry/RegisterShortcut
- BlueprintEditorModule: RegisterWindow, OnEditorActivated/Deactivated, DrawAllWindows, OnReloadCompleted
- EngineTimeControllerAdapter: IBlueprintTimeController stub (TODO M13)
- AddBlueprintEditor DI extension
- EditorInfrastructureTests.cs: SC1-SC10 (10 tests)

Baseline: 429 total -> 439+ pass / 5 skip / 0 fail"
```

---

## 16. Report

`.dev/blueprints-1/reports/BATCH-21-REPORT.md`

---

## Success Criteria

| SC | Check |
|----|-------|
| SC1-SC2 | DirtyTracker mark/clean/query works correctly |
| SC3-SC4 | EditorSelectionStore fires event + updates property |
| SC5-SC6 | EditorState set/get/remove in-memory asset |
| SC7 | FileSystemAssetCatalog returns no entries for empty directory |
| SC8 | BlueprintEditorModule registers menu entries on activation |
| SC9 | DrawAllWindows only draws visible windows |
| SC10 | EngineTimeControllerAdapter implements interface + doesn't throw |
| Build | `dotnet build Hrot.Blueprints.Editor` zero errors |
| Tests | 0 failures full suite |
