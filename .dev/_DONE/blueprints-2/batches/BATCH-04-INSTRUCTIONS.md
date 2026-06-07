# BATCH-04: Shared Windows and DI Wiring

**Batch Number:** BATCH-04  
**Tasks:** TASK-S1-09, TASK-S1-10, TASK-S1-14, TASK-S1-15  
**Phase:** Phase 1 — Shared infrastructure foundation (UI windows + DI)  
**Estimated Effort:** 8-10 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-02 + BATCH-03 (DONE)

---

## Mandatory Workflow

**Complete tasks in order, all tests passing before submitting:**

1. **Project changes:** Add `Fdp.Presentation` project ref + `Microsoft.Extensions.DependencyInjection` to both projects
2. **TASK-S1-15:** `IRuntimeInspectorPane`, `ITraceLaneProvider`, `TraceLaneDescriptor` interfaces + shell windows
3. **TASK-S1-09:** `AssetBrowserWindow` ManagedWindow subclass
4. **TASK-S1-10:** `InspectorWindow` ManagedWindow subclass
5. **TASK-S1-14:** `AddSharedAiEditor()` DI extension + `SharedAiWindowRegistrar`
6. **Final:** ALL 110 existing tests still pass; new tests pass; main solution builds

Do NOT stop and ask for permission. Complete the entire batch and submit the report.

---

## Onboarding

### What you're building

Four `ManagedWindow` subclasses + DI wiring in `Hrot.Editor.AiShared`. These are shells — `DrawClientArea()` shows an empty state; subsystems plug their content in later phases.

### Required Reading

1. `d:\Work\IOS-IG-SimHost-FDP-2\.dev\blueprints-2\AI_Editor_Shared_Infrastructure.md` — §9, §10, §14, §15, §19
2. `d:\Work\IOS-IG-SimHost-FDP-2\.dev\blueprints-2\TASK-DETAIL.md` — S1-09, S1-10, S1-14, S1-15
3. **Existing window example:** `Hrot/Subsystems/Hrot.IG/Windows/IgWindows.cs` — read this to understand the ManagedWindow pattern

### Existing Code to Read First

```
Hrot/Editor/Hrot.Editor.AiShared/Selection/EditorSelectionStore.cs
Hrot/Editor/Hrot.Editor.AiShared/Catalog/AssetCatalog.cs
Hrot/Editor/Hrot.Editor.AiShared/Debug/IDebugSessionRegistry.cs
Hrot/Editor/Hrot.Editor.AiShared/Debug/DebugSessionRegistry.cs
Hrot/Editor/Hrot.Editor.AiShared/Debug/AiTracerCoordinator.cs
FDP/Engine/Fdp.Presentation/ImGui/WindowManager/ManagedWindow.cs
FDP/Engine/Fdp.Presentation/ImGui/WindowManager/WindowScope.cs
FDP/Engine/Fdp.Presentation/ImGui/IWindowRegistrar.cs
```

### Build Commands

```powershell
dotnet test "Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj"
dotnet build IOS-IG-SimHost.sln
```

### Report Submission

`.dev/blueprints-2/reports/BATCH-04-REPORT.md`

---

## Step 0: Project File Changes (DO THIS FIRST)

### `Hrot.Editor.AiShared.csproj`

Add to the ItemGroup with the existing `Fdp.Core` reference:

```xml
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.0" />
```

Add to the same ProjectReference ItemGroup:

```xml
<ProjectReference Include="..\..\..\FDP\Engine\Fdp.Presentation\Fdp.Presentation.csproj" />
```

### `Hrot.Editor.AiShared.Tests.csproj`

Add a new ItemGroup (or to the existing one):

```xml
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.0" />
```

**Verify the projects still build after these changes before proceeding.**

---

## Tasks

### Task 1 (do first): TASK-S1-15 — Shell interfaces + window stubs

**Spec:** shared infra §14, §15. These are interfaces only (no subsystem content yet).

**Files to create:**

#### `Hrot/Editor/Hrot.Editor.AiShared/Debug/IRuntimeInspectorPane.cs`

```csharp
namespace Hrot.Editor.AiShared.Debug;

/// <summary>
/// Subsystem-provided pane rendered inside the RuntimeInspectorWindow content area.
/// One implementation per subsystem (BTree, HSM, Blueprint); selected at runtime
/// by matching TargetKind to the active asset's kind.
/// </summary>
public interface IRuntimeInspectorPane
{
    /// <summary>The asset kind this pane handles.</summary>
    AssetKind TargetKind { get; }

    /// <summary>
    /// Draw the pane's ImGui content. Called every frame while the matching asset is active.
    /// Do NOT call ImGui.Begin/End here; the window already did that.
    /// </summary>
    void Draw();
}
```

#### `Hrot/Editor/Hrot.Editor.AiShared/Debug/ITraceLaneProvider.cs`

```csharp
namespace Hrot.Editor.AiShared.Debug;

/// <summary>Describes one swim lane in the Trace Timeline window.</summary>
public sealed record TraceLaneDescriptor(
    string Id,
    string DisplayName,
    TraceLevel SupportedLevels);

/// <summary>
/// Subsystem-provided swim-lane definitions for the TraceTimelineWindow.
/// One implementation per subsystem; selected by matching Kind to the active asset.
/// </summary>
public interface ITraceLaneProvider
{
    AssetKind Kind { get; }
    IReadOnlyList<TraceLaneDescriptor> Lanes { get; }
}
```

#### `Hrot/Editor/Hrot.Editor.AiShared/Windows/RuntimeInspectorWindow.cs`

Shell window:

```csharp
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Selection;

namespace Hrot.Editor.AiShared.Windows;

/// <summary>
/// Shell for the shared runtime inspector. Renders the entity-lifecycle status,
/// mode controls, scrub bar, and delegates the asset-specific pane to
/// the registered IRuntimeInspectorPane for the active asset kind.
/// Subsystems provide IRuntimeInspectorPane implementations; this window
/// selects the matching pane at draw time.
/// </summary>
public sealed class RuntimeInspectorWindow : ManagedWindow
{
    private readonly EditorSelectionStore _store;
    private readonly IDebugSessionRegistry _registry;
    private readonly List<IRuntimeInspectorPane> _panes = new();

    public RuntimeInspectorWindow(
        EditorSelectionStore store,
        IDebugSessionRegistry registry)
        : base("ai_runtime_inspector", "Runtime Inspector", "Authoring", WindowScope.PerspectiveBound)
    {
        _store = store;
        _registry = registry;
    }

    /// <summary>Register a subsystem-provided pane. Called at editor startup.</summary>
    public void RegisterPane(IRuntimeInspectorPane pane) => _panes.Add(pane);

    protected override void DrawClientArea()
    {
        // Shell: show empty state until subsystem panes are registered.
        ImGuiNET.ImGui.TextDisabled("No active session.");
    }
}
```

#### `Hrot/Editor/Hrot.Editor.AiShared/Windows/TraceTimelineWindow.cs`

Shell window:

```csharp
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Selection;

namespace Hrot.Editor.AiShared.Windows;

/// <summary>
/// Shell for the shared trace timeline. Renders swim lanes provided by
/// the registered ITraceLaneProvider for the active asset kind.
/// Subsystems provide ITraceLaneProvider implementations.
/// </summary>
public sealed class TraceTimelineWindow : ManagedWindow
{
    private readonly EditorSelectionStore _store;
    private readonly IDebugSessionRegistry _registry;
    private readonly List<ITraceLaneProvider> _providers = new();

    public TraceTimelineWindow(
        EditorSelectionStore store,
        IDebugSessionRegistry registry)
        : base("ai_trace_timeline", "Trace Timeline", "Authoring", WindowScope.PerspectiveBound)
    {
        _store = store;
        _registry = registry;
    }

    /// <summary>Register a subsystem-provided lane provider. Called at editor startup.</summary>
    public void RegisterProvider(ITraceLaneProvider provider) => _providers.Add(provider);

    protected override void DrawClientArea()
    {
        // Shell: show empty state until lane providers are registered.
        ImGuiNET.ImGui.TextDisabled("No trace data.");
    }
}
```

---

### Task 2: TASK-S1-09 — `AssetBrowserWindow`

**Spec:** shared infra §9.

**File to create:** `Hrot/Editor/Hrot.Editor.AiShared/Windows/AssetBrowserWindow.cs`

```csharp
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Selection;

namespace Hrot.Editor.AiShared.Windows;

/// <summary>
/// Asset browser window — lists all editor assets grouped by folder.
/// Single-click sets ActiveAsset; double-click opens the asset in its editor canvas.
/// </summary>
public sealed class AssetBrowserWindow : ManagedWindow
{
    private readonly EditorSelectionStore _store;
    private readonly IAssetCatalog _catalog;

    public AssetBrowserWindow(EditorSelectionStore store, IAssetCatalog catalog)
        : base("ai_asset_browser", "Asset Browser", "Authoring", WindowScope.PerspectiveBound)
    {
        _store = store;
        _catalog = catalog;
    }

    protected override void DrawClientArea()
    {
        if (_catalog.All.Count == 0)
        {
            ImGuiNET.ImGui.TextDisabled("No assets loaded.");
            return;
        }

        foreach (var asset in _catalog.All)
        {
            ImGuiNET.ImGui.Text(asset.Name);
        }
    }
}
```

---

### Task 3: TASK-S1-10 — `InspectorWindow`

**Spec:** shared infra §10.

**File to create:** `Hrot/Editor/Hrot.Editor.AiShared/Windows/InspectorWindow.cs`

```csharp
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Selection;

namespace Hrot.Editor.AiShared.Windows;

/// <summary>
/// Inspector window — shows properties for the currently-selected sub-element.
/// StructEdit-driven dispatch by asset type; subsystems supply facet structs.
/// This is a shell; per-subsystem inspector panels are added in later phases.
/// </summary>
public sealed class InspectorWindow : ManagedWindow
{
    private readonly EditorSelectionStore _store;

    public InspectorWindow(EditorSelectionStore store)
        : base("ai_inspector", "Inspector", "Authoring", WindowScope.PerspectiveBound)
    {
        _store = store;
    }

    protected override void DrawClientArea()
    {
        if (_store.ActiveAsset is null)
        {
            ImGuiNET.ImGui.TextDisabled("Select an asset to begin.");
            return;
        }

        ImGuiNET.ImGui.Text(_store.ActiveAsset.Name);
    }
}
```

---

### Task 4: TASK-S1-14 — DI wiring

**Spec:** shared infra §19.

**Files to create:**

#### `Hrot/Editor/Hrot.Editor.AiShared/Windows/SharedAiWindowRegistrar.cs`

```csharp
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Runner;

namespace Hrot.Editor.AiShared.Windows;

/// <summary>
/// Registers the four shared AI editor windows with the WindowManager.
/// Implement IWindowRegistrar so the subsystem orchestrator can call RegisterWindows.
/// </summary>
public sealed class SharedAiWindowRegistrar : IWindowRegistrar
{
    private readonly AssetBrowserWindow _assetBrowser;
    private readonly InspectorWindow _inspector;
    private readonly RuntimeInspectorWindow _runtimeInspector;
    private readonly TraceTimelineWindow _traceTimeline;

    public SharedAiWindowRegistrar(
        AssetBrowserWindow assetBrowser,
        InspectorWindow inspector,
        RuntimeInspectorWindow runtimeInspector,
        TraceTimelineWindow traceTimeline)
    {
        _assetBrowser = assetBrowser;
        _inspector = inspector;
        _runtimeInspector = runtimeInspector;
        _traceTimeline = traceTimeline;
    }

    public void RegisterWindows(WindowManager windowManager)
    {
        windowManager.RegisterWindow(_assetBrowser);
        windowManager.RegisterWindow(_inspector);
        windowManager.RegisterWindow(_runtimeInspector);
        windowManager.RegisterWindow(_traceTimeline);
    }
}
```

#### `Hrot/Editor/Hrot.Editor.AiShared/Di/SharedAiEditorServiceCollectionExtensions.cs`

```csharp
using Microsoft.Extensions.DependencyInjection;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Windows;
using Fdp.Toolkit.Runner;

namespace Hrot.Editor.AiShared.Di;

/// <summary>
/// DI extension method for registering all shared AI editor services and windows.
/// Call from the subsystem's composition root (or from tests).
/// </summary>
public static class SharedAiEditorServiceCollectionExtensions
{
    public static IServiceCollection AddSharedAiEditor(this IServiceCollection services)
    {
        // Core services
        services.AddSingleton<EditorSelectionStore>();
        services.AddSingleton<IAssetCatalog, AssetCatalog>();
        services.AddSingleton<IDebugSessionRegistry, DebugSessionRegistry>();
        services.AddSingleton<AiTracerCoordinator>();

        // Windows
        services.AddSingleton<AssetBrowserWindow>();
        services.AddSingleton<InspectorWindow>();
        services.AddSingleton<RuntimeInspectorWindow>();
        services.AddSingleton<TraceTimelineWindow>();

        // Window registrar
        services.AddSingleton<IWindowRegistrar, SharedAiWindowRegistrar>();

        return services;
    }
}
```

---

## Testing Requirements

### Test file locations

All new tests in `Hrot/Editor/Hrot.Editor.AiShared.Tests/`:
- `Windows/AssetBrowserWindowTests.cs`
- `Windows/InspectorWindowTests.cs`
- `Windows/RuntimeInspectorWindowTests.cs`
- `Windows/TraceTimelineWindowTests.cs`
- `Di/SharedAiEditorDiTests.cs`
- `Debug/TraceLaneDescriptorTests.cs`

### Window property tests (no ImGui context needed for constructors)

The window tests verify properties only — no rendering. ManagedWindow constructors don't call ImGui.

**`AssetBrowserWindowTests.cs` — minimum 4 tests:**

```csharp
[Fact]
public void Constructor_SetsId()
{
    var store = new EditorSelectionStore();
    var catalog = new AssetCatalog();
    var window = new AssetBrowserWindow(store, catalog);
    Assert.Equal("ai_asset_browser", window.Id);
}
```

- `Constructor_SetsId` — Id is `"ai_asset_browser"`
- `Constructor_SetsTitle` — Title is `"Asset Browser"`
- `Constructor_SetsOwningPerspective` — OwningPerspective is `"Authoring"`
- `Constructor_SetsScopePerspectiveBound` — Scope is `WindowScope.PerspectiveBound`

**`InspectorWindowTests.cs` — minimum 4 tests:**

- `Constructor_SetsId` — Id is `"ai_inspector"`
- `Constructor_SetsTitle` — Title is `"Inspector"`
- `Constructor_SetsOwningPerspective` — OwningPerspective is `"Authoring"`
- `Constructor_SetsScopePerspectiveBound` — Scope is `WindowScope.PerspectiveBound`

**`RuntimeInspectorWindowTests.cs` — minimum 5 tests:**

- `Constructor_SetsId` — Id is `"ai_runtime_inspector"`
- `Constructor_SetsTitle` — Title is `"Runtime Inspector"`
- `Constructor_SetsScopePerspectiveBound`
- `RegisterPane_IncreasesRegisteredPaneCount`
- `RegisterPane_MultipleCanBeRegistered`

For RegisterPane tests: expose a `RegisteredPaneCount` internal property OR make the `_panes` list accessible internally. Add `<InternalsVisibleTo Include="Hrot.Editor.AiShared.Tests" />` is already in the csproj — so you can use `internal` for test-support members.

**`TraceTimelineWindowTests.cs` — minimum 5 tests:**

- `Constructor_SetsId` — Id is `"ai_trace_timeline"`
- `Constructor_SetsTitle` — Title is `"Trace Timeline"`
- `Constructor_SetsScopePerspectiveBound`
- `RegisterProvider_AddsProvider`
- `RegisterProvider_MultipleCanBeRegistered`

**`TraceLaneDescriptorTests.cs` — minimum 3 tests:**

- `Record_EqualityByValues`: two `TraceLaneDescriptor` with same values are equal
- `Record_DisplayName_IsSet`
- `Record_SupportedLevels_IsSet`

### DI Integration tests — minimum 8 tests in `Di/SharedAiEditorDiTests.cs`

```csharp
// No ImGui context needed — we're just testing DI registrations
using Microsoft.Extensions.DependencyInjection;
using Hrot.Editor.AiShared.Di;
using Hrot.Editor.AiShared.Windows;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Selection;
using Fdp.Toolkit.Runner;

public class SharedAiEditorDiTests
{
    private static ServiceProvider BuildSp()
    {
        var services = new ServiceCollection();
        services.AddSharedAiEditor();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddSharedAiEditor_Resolves_EditorSelectionStore()
    {
        using var sp = BuildSp();
        Assert.NotNull(sp.GetRequiredService<EditorSelectionStore>());
    }

    [Fact]
    public void AddSharedAiEditor_Resolves_IAssetCatalog()
    {
        using var sp = BuildSp();
        Assert.NotNull(sp.GetRequiredService<IAssetCatalog>());
    }

    [Fact]
    public void AddSharedAiEditor_Resolves_AssetBrowserWindow_WithCorrectId()
    {
        using var sp = BuildSp();
        var w = sp.GetRequiredService<AssetBrowserWindow>();
        Assert.NotNull(w);
        Assert.Equal("ai_asset_browser", w.Id);
    }

    [Fact]
    public void AddSharedAiEditor_Resolves_InspectorWindow()
    {
        using var sp = BuildSp();
        Assert.NotNull(sp.GetRequiredService<InspectorWindow>());
    }

    [Fact]
    public void AddSharedAiEditor_Resolves_RuntimeInspectorWindow()
    {
        using var sp = BuildSp();
        Assert.NotNull(sp.GetRequiredService<RuntimeInspectorWindow>());
    }

    [Fact]
    public void AddSharedAiEditor_Resolves_TraceTimelineWindow()
    {
        using var sp = BuildSp();
        Assert.NotNull(sp.GetRequiredService<TraceTimelineWindow>());
    }

    [Fact]
    public void AddSharedAiEditor_Resolves_IDebugSessionRegistry()
    {
        using var sp = BuildSp();
        Assert.NotNull(sp.GetRequiredService<IDebugSessionRegistry>());
    }

    [Fact]
    public void AddSharedAiEditor_Resolves_IWindowRegistrar_AsSharedAiWindowRegistrar()
    {
        using var sp = BuildSp();
        var registrar = sp.GetRequiredService<IWindowRegistrar>();
        Assert.IsType<SharedAiWindowRegistrar>(registrar);
    }
}
```

---

## Success Criteria

This batch is DONE when:

- [ ] `Hrot.Editor.AiShared.csproj` has `Fdp.Presentation` + `Microsoft.Extensions.DependencyInjection` references
- [ ] `Hrot.Editor.AiShared.Tests.csproj` has `Microsoft.Extensions.DependencyInjection` reference
- [ ] TASK-S1-15: `IRuntimeInspectorPane`, `ITraceLaneProvider`, `TraceLaneDescriptor`, `RuntimeInspectorWindow`, `TraceTimelineWindow` created
- [ ] TASK-S1-09: `AssetBrowserWindow` created
- [ ] TASK-S1-10: `InspectorWindow` created
- [ ] TASK-S1-14: `SharedAiWindowRegistrar`, `SharedAiEditorServiceCollectionExtensions` created
- [ ] `dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj` — ALL PASS (110 old + 29+ new)
- [ ] `dotnet build IOS-IG-SimHost.sln` — clean build
- [ ] Report at `.dev/blueprints-2/reports/BATCH-04-REPORT.md`

---

## Common Pitfalls

- `IWindowRegistrar` lives in namespace `Fdp.Toolkit.Runner` (not `Fdp.Presentation`) — check the using directive.
- `WindowManager` is in namespace `Fdp.Presentation.WindowManager`.
- The `WindowManager.RegisterWindow(ManagedWindow)` method is the correct call — check the existing subsystem code in `Hrot.IG` for the exact call pattern.
- `AssetCatalog` has a no-arg constructor — DI can construct it without configuration.
- Do NOT call `ImGui.Begin/End` in `DrawClientArea` — the base class already handles that.
- The window classes should NOT be sealed if there is any chance the test needs to subclass them. The spec says sealed is fine — keep them sealed, the tests verify via constructor.
- The `using Gui = ImGuiNET.ImGui` global alias may exist in `Fdp.Presentation`'s GlobalUsings.cs but is NOT available in `Hrot.Editor.AiShared`. Use `ImGuiNET.ImGui.TextDisabled(...)` directly.

---

## Developer Insights Report Questions

1. Did you need to use the `using Gui = ImGuiNET.ImGui` alias or the full path? Why?
2. Is `AssetCatalog` constructor-injectable from DI without any additional setup? Any issue?
3. For the `RegisterPane` and `RegisterProvider` tests: how did you expose the count for test verification? Internal property? Or count via a separate interface?
4. Any namespace conflicts between `Fdp.Toolkit.Runner.IWindowRegistrar` and other types?
5. Were all 110 existing tests unaffected after adding the new project references?
