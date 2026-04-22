Now We have entity inspector that shows entity list and for selected one the components. Sometimes I need to watch one single selected entity. I would like to be able to open a dedicated window for a concrete entity (index & generation) containing just the right part of the existing entity inspector window (i.e. the live list of components for the entity). I want to extend the entity context menu by adding "Inspect..." item that opens such a single-entity watch window. There can be multiple such windows - for different entities or event for the same entity (their id needs to include the enity id and generation to be unique). The rendering part should be shared as much as possible with the current entity inspector (follow DRY concept). These windows must be pinnable so if pinned, they will stay shown even if we change the pespective. If the watched entity ceases to exist, the watch window must show corresponding text about that entity is not existing anymore.

-----------


To implement a dedicated, pinnable single-entity watch window while adhering to clean architecture and the DRY principle, we will reuse the existing `ComponentReflector` which already handles the hierarchical component rendering and byte-level diff caching. 

We will extend the `IInspectableSession` contract to safely check entity liveness, extract the rendering shell into a new framework-level panel, wrap it in a `ManagedWindow`, and inject the new context menu handler dynamically from each subsystem's composition root where the `WindowManager` is accessible.

### 1. Extend the Session Contract
First, we must add an explicit liveness check to the session abstraction so the UI can detect when a watched entity is destroyed.

**Update `FDP/Engine/Fdp.Presentation/ImGui/Abstractions/IInspectableSession.cs`**:
```csharp
    public interface IInspectableSession
    {
        bool IsReadOnly { get; }
        int EntityCount { get; }

        IEnumerable<Entity> GetEntities();

        bool IsAlive(Entity e); // ADDED

        bool HasComponent(Entity e, Type componentType);
        // ...
```

**Update `FDP/Engine/Fdp.Presentation/ImGui/Adapters/RepositoryAdapter.cs`**:
```csharp
        public bool IsReadOnly => false;
        public int EntityCount => _repo.EntityCount;

        public bool IsAlive(Entity e) => _repo.IsAlive(e); // ADDED
```

**Update `FDP/Engine/Fdp.Presentation/ImGui/Adapters/SimulationViewAdapter.cs`**:
```csharp
        public bool IsReadOnly => true;
        public int EntityCount => 0;
        
        public bool IsAlive(Entity e) => _view.IsAlive(e); // ADDED
```

### 2. Create the Reusable Watch Panel
Create a new generic panel in the FDP framework that instantiates its own `ComponentReflector`. By isolating the reflector instance, the diff-caching (yellow text for changed fields) will work perfectly for each independent window without fighting the main Entity Inspector.

**Create `FDP/Engine/Fdp.Presentation/ImGui/Panels/EntityWatchPanel.cs`**:
```csharp
using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Utils;
using ImGuiNET;

namespace Fdp.Presentation.Panels
{
    public class EntityWatchPanel
    {
        private readonly Entity _targetEntity;
        private readonly ComponentReflector _reflector = new();

        public EntityWatchPanel(Entity targetEntity)
        {
            _targetEntity = targetEntity;
        }

        public void DrawContent(IInspectableSession session)
        {
            if (!session.IsAlive(_targetEntity))
            {
                ImGui.TextDisabled("Entity no longer exists.");
                return;
            }

            ImGui.Text($"ID: {_targetEntity.Index} | Gen: {_targetEntity.Generation}");

            if (session.IsReadOnly)
                ImGui.TextColored(new Vector4(1, 1, 0, 1), "[READ-ONLY]");

            if (ImGui.SmallButton("▶▶ Expand All")) _reflector.ForceExpandAll = true;
            ImGui.SameLine();
            if (ImGui.SmallButton("◀◀ Collapse All")) _reflector.ForceCollapseAll = true;

            ImGui.Separator();

            _reflector.DrawComponents(session, _targetEntity);
        }
    }
}
```

### 3. Create the Managed Window Wrapper
Wrap the framework panel in the Hrot integration layer. Because it inherits from `ManagedWindow` with `WindowScope.PerspectiveBound`, it automatically gains the pinning functionality required to persist across perspective switches.

**Update `Hrot/Engine/Hrot.Presentation/Windows/FdpPanelWindows.cs`**:
```csharp
using Fdp.Core; // Ensure this is at the top

namespace Hrot.Presentation.Windows;

// ... existing windows ...

/// <summary>
/// Dedicated watch window for a single entity.
/// </summary>
public sealed class FdpEntityWatchWindow : ManagedWindow
{
    private readonly EntityWatchPanel _panel;
    private readonly Func<IInspectableSession?> _sessionGetter;

    public FdpEntityWatchWindow(
        string id,
        string title,
        string owningPerspective,
        EntityWatchPanel panel,
        Func<IInspectableSession?> sessionGetter,
        Vector4? titleBarColor = null)
        : base(id, title, owningPerspective, WindowScope.PerspectiveBound)
    {
        _panel = panel;
        _sessionGetter = sessionGetter;
        IsOpen = true;
        TitleBarColor = titleBarColor;
    }

    protected override void DrawClientArea()
    {
        var session = _sessionGetter();
        if (session == null) return;
        _panel.DrawContent(session);
    }
}
```

### 4. Register the Context Menu Action Dynamically
We decouple the context menu from the application shell by registering a new menu handler inside the `RegisterWindows` phase of each subsystem, where the `WindowManager` reference is available to dynamically spawn new windows.

**Update `Hrot/Subsystems/Hrot.IG/IgSubsystem.cs`**:
```csharp
using Fdp.Presentation.Utils;
using Fdp.Presentation.Panels;
using System;

// ... inside IgSubsystem
public void RegisterWindows(Fdp.Presentation.WindowManager.WindowManager windowManager)
{
    if (_app == null) return;

    _app.FdpEntityInspector.RegisterContextMenuHandler(new LambdaEntityContextMenuHandler((entity, builder) =>
    {
        builder.AddItem("Inspect...", () =>
        {
            var id = $"ig_watch_{entity.Index}_{entity.Generation}_{Guid.NewGuid()}";
            windowManager.RegisterWindow(new FdpEntityWatchWindow(
                id, $"Watch Entity {entity.Index}", "IG", new EntityWatchPanel(entity),
                () => _app.GetFdpRepoAdapter(),
                IgWindowColor.TitleBar));
        });
    }));
    
    // ... existing registrations ...
```

**Update `Hrot/Subsystems/Hrot.SimHost/SimHostSubsystem.cs`**:
```csharp
using Fdp.Presentation.Utils;
using Fdp.Presentation.Panels;
using System;

// ... inside SimHostSubsystem
public void RegisterWindows(Fdp.Presentation.WindowManager.WindowManager windowManager)
{
    var vis = _app?.Visualization;
    if (vis == null) return;

    vis.FdpEntityInspector.RegisterContextMenuHandler(new LambdaEntityContextMenuHandler((entity, builder) =>
    {
        builder.AddItem("Inspect...", () =>
        {
            var id = $"simhost_watch_{entity.Index}_{entity.Generation}_{Guid.NewGuid()}";
            windowManager.RegisterWindow(new FdpEntityWatchWindow(
                id, $"Watch Entity {entity.Index}", "SimHost", new EntityWatchPanel(entity),
                () => vis.GetFdpRepoAdapter(),
                SimHostWindowColor.TitleBar));
        });
    }));

    // ... existing registrations ...
```

**Update `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`**:
```csharp
using Fdp.Presentation.Panels;
using System;

// ... inside EditorSubsystem
public void RegisterWindows(Fdp.Presentation.WindowManager.WindowManager windowManager)
{
    if (_editorLogic == null) return;

    _fdpEntityInspector.RegisterContextMenuHandler(new LambdaEntityContextMenuHandler((entity, builder) =>
    {
        builder.AddItem("Inspect...", () =>
        {
            var id = $"editor_watch_{entity.Index}_{entity.Generation}_{Guid.NewGuid()}";
            windowManager.RegisterWindow(new FdpEntityWatchWindow(
                id, $"Watch Entity {entity.Index}", "Editor", new EntityWatchPanel(entity),
                () => _fdpRepoAdapter,
                EditorWindowColor.TitleBar));
        });
    }));

    // ... existing registrations ...
```

This enforces strict separation of concerns: the core framework panel handles rendering the component tree, the application adapter bridges the dynamic window registration, and the inclusion of a `Guid` guarantees uniqueness so you can track multiple instances of the exact same entity safely.


---------------


i said there can be multiple watch windows for the same single single entity. Thes is usefull if the component list is long and we need to see different components expanded in different windows in case where the component details would not fit in a single watch window. This might collide with the unique id of its watch window is we use entity id and generation as part of the watch window id. Maybe we will need to add extra instance id to allow multiple watch windows for same entity.

---------------

The provided implementation already accounts for this exact requirement. The window registration explicitly appends a `Guid.NewGuid()` to the identifier string alongside the entity index and generation to guarantee uniqueness. 

For example, the ID generation follows this format:
`var id = $"ig_watch_{entity.Index}_{entity.Generation}_{Guid.NewGuid()}";`

This ensures no window ID collisions occur when spawning multiple watch windows for the exact same entity. 

Furthermore, because each `FdpEntityWatchWindow` encapsulates its own `EntityWatchPanel`, it receives a private instance of `ComponentReflector`. The `ComponentReflector` manages its own per-type byte snapshot cache (`_unmanagedCache`) for change detection and pushes stable ImGui ID scopes (`ImGuiApi.PushID`) to prevent table-state collisions. This strict state isolation guarantees that multiple windows observing the same entity can independently expand, collapse, and cache component states without any UI interference or cross-contamination.


---------


good. How will these watch windows appear in the main menu? will they be present? in what section of the menu? and under what name?

----------

Because `FdpEntityWatchWindow` inherits from `ManagedWindow`, the engine's `WindowManager` will automatically integrate it into the global menu system without requiring any explicit menu registration code.

They will be present in the main menu and will appear dynamically under the top-level **"Windows"** section.

Specifically, because we assigned the window a `WindowScope.PerspectiveBound` scope, the `WindowManager` groups it into a sub-menu matching its `OwningPerspective`. For example, if you spawn the watch window from the IG perspective, it will be placed under **"Windows" -> "IG"**. 

The name of the menu item is bound directly to the window's `Title` property. Based on our implementation, it will appear as **"Watch Entity {Index}"** (e.g., "Watch Entity 42"). 

This menu item acts as a checkbox toggle that mirrors the `IsOpen` state of the window, allowing you to hide or show the specific watch window directly from the menu. If you spawn multiple watch windows for different entities (or the same entity), each will generate its own distinct entry in that perspective's sub-menu.


---------------------

wait. those watch windows are volatile. If i close the window, it should disappear from the world completely. If I open new watch, it should instantiate a new instance of the window. The checkbox there does not make sense.

-------------


You are absolutely right. Forcing a dynamic, multi-instance tool window into a persistent singleton model clutters the global menu and creates a memory leak if the windows are never destroyed. 

To fix this while keeping the DRY principle intact (specifically, reusing `ManagedWindow`'s existing perspective-pinning logic), we must elevate volatility to a first-class concept within the window management framework. 

Here is the architectural adjustment to support volatile, menu-hidden windows that still retain cross-perspective pinning.

### 1. Extend `ManagedWindow` for Volatility
Modify the base class to expose standard lifecycle and menu-visibility flags.

```csharp
// Inside FDP/Engine/Fdp.Presentation/ImGui/WindowManager/ManagedWindow.cs
public abstract class ManagedWindow
{
    // ... existing fields ...

    /// <summary>If true, the WindowManager will destroy this instance when IsOpen becomes false.</summary>
    public bool IsVolatile { get; protected set; } = false;

    /// <summary>If true, this window appears as a toggle in the global Windows menu.</summary>
    public bool ShowInMenu { get; protected set; } = true;
    
    // ...
```

### 2. Update `WindowManager` to Garbage Collect and Filter
Modify the main `Render` loop to automatically unregister volatile windows when they are closed, and filter them out of the main menu.

**Update `Render()`:**
```csharp
// Inside FDP/Engine/Fdp.Presentation/ImGui/WindowManager/WindowManager.cs
public void Render()
{
    // ... existing menu rendering ...

    // Iterate a copy to allow safe removal of closed volatile windows
    foreach (var win in _windows.Values.ToList())
    {
        if (win.IsVolatile && !win.IsOpen)
        {
            _windows.Remove(win.Id);
            continue;
        }

        win.Render(CurrentPerspective, _atlas);
    }
}
```

**Update `RenderFixedWindowsMenu()`:**
```csharp
private void RenderFixedWindowsMenu()
{
    // ...
    foreach (var group in perspectiveGroups)
    {
        if (!Gui.BeginMenu(group.Key)) continue;

        foreach (var win in group)
        {
            if (win.ShowInMenu)
                RenderWindowToggleMenuItem(win);
        }
        Gui.EndMenu();
    }
    // ... apply the same win.ShowInMenu check to the Global windows loop below it
}
```

### 3. Update the Watch Window Implementation
Configure the `FdpEntityWatchWindow` to utilize these new base properties. It remains `WindowScope.PerspectiveBound` so the built-in title-bar pin button continues to work flawlessly across perspectives, but it is now strictly volatile.

```csharp
public sealed class FdpEntityWatchWindow : ManagedWindow
{
    // ... existing fields ...

    public FdpEntityWatchWindow(
        string id,
        string title,
        string owningPerspective,
        EntityWatchPanel panel,
        Func<IInspectableSession?> sessionGetter,
        Vector4? titleBarColor = null)
        : base(id, title, owningPerspective, WindowScope.PerspectiveBound)
    {
        _panel = panel;
        _sessionGetter = sessionGetter;
        IsOpen = true;
        TitleBarColor = titleBarColor;

        // Enforce volatile behavior
        IsVolatile = true;
        ShowInMenu = false;
    }

    // ... DrawClientArea() remains the same ...
}
```

This correctly implements your lifecycle requirements. When you click the ImGui `[x]` close button, `Gui.Begin` writes `false` to the `ref _isOpen` parameter. On the very next frame, the `WindowManager` detects the volatile flag, drops the reference from the dictionary, and the window ceases to exist completely. No menu pollution occurs, and pinning still works exactly as expected.

---------------


The watch window should keep the [Copy to JSON] button with same functionality as in the entity inspector. The window title should include the entity id & generation

---------------

To fulfill these requirements, we will reuse the `EntityJsonDumper` utility directly within the `EntityWatchPanel` and adjust the window instantiation payload to format the title correctly.

### 1. Add Copy JSON Button to the Watch Panel
Open `FDP/Engine/Fdp.Presentation/ImGui/Panels/EntityWatchPanel.cs` and update the `DrawContent` method to include the exact JSON dumping logic used by the main inspector. 

```csharp
using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Utils;
using ImGuiNET;

namespace Fdp.Presentation.Panels
{
    public class EntityWatchPanel
    {
        private readonly Entity _targetEntity;
        private readonly ComponentReflector _reflector = new();

        public EntityWatchPanel(Entity targetEntity)
        {
            _targetEntity = targetEntity;
        }

        public void DrawContent(IInspectableSession session)
        {
            if (!session.IsAlive(_targetEntity))
            {
                ImGui.TextDisabled("Entity no longer exists.");
                return;
            }

            ImGui.Text($"ID: {_targetEntity.Index} | Gen: {_targetEntity.Generation}");

            ImGui.SameLine();
            if (ImGui.Button("Copy JSON"))
            {
                var json = EntityJsonDumper.Dump(session, _targetEntity);
                ImGui.SetClipboardText(json);
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Dump exact entity state to clipboard as JSON");
            }

            if (session.IsReadOnly)
                ImGui.TextColored(new Vector4(1, 1, 0, 1), "[READ-ONLY]");

            if (ImGui.SmallButton("▶▶ Expand All")) _reflector.ForceExpandAll = true;
            ImGui.SameLine();
            if (ImGui.SmallButton("◀◀ Collapse All")) _reflector.ForceCollapseAll = true;

            ImGui.Separator();

            _reflector.DrawComponents(session, _targetEntity);
        }
    }
}
```

### 2. Update the Window Title Formatting
Update the dynamic window registrations in the subsystem composition roots to include the generation number in the title string.

**In `Hrot/Subsystems/Hrot.IG/IgSubsystem.cs`**:
```csharp
    _app.FdpEntityInspector.RegisterContextMenuHandler(new LambdaEntityContextMenuHandler((entity, builder) =>
    {
        builder.AddItem("Inspect...", () =>
        {
            var id = $"ig_watch_{entity.Index}_{entity.Generation}_{Guid.NewGuid()}";
            windowManager.RegisterWindow(new FdpEntityWatchWindow(
                id, 
                $"Watch Entity {entity.Index} v{entity.Generation}", // UPDATED
                "IG", 
                new EntityWatchPanel(entity),
                () => _app.GetFdpRepoAdapter(),
                IgWindowColor.TitleBar));
        });
    }));
```

**In `Hrot/Subsystems/Hrot.SimHost/SimHostSubsystem.cs`**:
```csharp
    vis.FdpEntityInspector.RegisterContextMenuHandler(new LambdaEntityContextMenuHandler((entity, builder) =>
    {
        builder.AddItem("Inspect...", () =>
        {
            var id = $"simhost_watch_{entity.Index}_{entity.Generation}_{Guid.NewGuid()}";
            windowManager.RegisterWindow(new FdpEntityWatchWindow(
                id, 
                $"Watch Entity {entity.Index} v{entity.Generation}", // UPDATED
                "SimHost", 
                new EntityWatchPanel(entity),
                () => vis.GetFdpRepoAdapter(),
                SimHostWindowColor.TitleBar));
        });
    }));
```

**In `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`**:
```csharp
    _fdpEntityInspector.RegisterContextMenuHandler(new LambdaEntityContextMenuHandler((entity, builder) =>
    {
        builder.AddItem("Inspect...", () =>
        {
            var id = $"editor_watch_{entity.Index}_{entity.Generation}_{Guid.NewGuid()}";
            windowManager.RegisterWindow(new FdpEntityWatchWindow(
                id, 
                $"Watch Entity {entity.Index} v{entity.Generation}", // UPDATED
                "Editor", 
                new EntityWatchPanel(entity),
                () => _fdpRepoAdapter,
                EditorWindowColor.TitleBar));
        });
    }));
```

This enforces the DRY principle by calling the static `EntityJsonDumper.Dump` utility inside the watch panel, guaranteeing identical output to the main `EntityInspectorPanel`. The window headers will now correctly distinguish multiple generations of the same entity ID.


