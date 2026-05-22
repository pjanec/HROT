# Kernel 04 — My Blueprint and Remaining Interfaces

---

## File: `NodeEditor.Core/Interfaces/IMyBlueprintModel.cs`

```csharp
using System.Numerics;

namespace NodeEditor.Core.Interfaces;

/// <summary>
/// Host-provided model for the "My Blueprint" panel: a hierarchical
/// outline of the asset's variables, functions, macros, events, and
/// dispatchers. The editor renders this purely as data; semantics are
/// entirely host-defined.
/// </summary>
public interface IMyBlueprintModel
{
    /// <summary>Top-level sections (Graphs, Functions, Variables, ...).</summary>
    IReadOnlyList<MyBlueprintSectionDescriptor> Sections { get; }

    /// <summary>Items in a given section.</summary>
    IReadOnlyList<MyBlueprintItem> GetItems(string sectionId);

    /// <summary>Raised when section content changes.</summary>
    event Action? Changed;
}

/// <summary>Descriptor for a top-level My Blueprint section.</summary>
public sealed record MyBlueprintSectionDescriptor(
    string Id,
    string DisplayName,
    int SortOrder,
    string? IconKey,
    bool CanCreateItems,
    bool CanHaveCategories,
    string? CreateCommandId);

/// <summary>An item appearing in a section. Can have children (nested categories or sub-items).</summary>
public sealed record MyBlueprintItem(
    string ItemId,
    string SectionId,
    string DisplayName,
    string? CategoryPath,
    string? IconKey,
    string? BadgeText,
    Vector4? AccentColor,
    IReadOnlyList<MyBlueprintItem>? Children,
    bool IsRenamable,
    bool IsDeletable,
    bool IsHostDefined,
    string? Tooltip);
```

---

## File: `NodeEditor.Core/Interfaces/IEnumValueProvider.cs`

```csharp
using NodeEditor.Primitives;

namespace NodeEditor.Core.Interfaces;

/// <summary>
/// Provides enum value lists for inline editors. Host registers one for
/// each enum type its catalog uses.
/// </summary>
public interface IEnumValueProvider
{
    /// <summary>Get values for an enum type.</summary>
    IReadOnlyList<EnumValueEntry> GetValues(TypeKey enumType);

    /// <summary>
    /// Above this count, enum editors fall back from inline combo to picker.
    /// Default 8.
    /// </summary>
    int GetMaxInlineValues();
}

/// <summary>One enum value with display info.</summary>
public sealed record EnumValueEntry(
    long Value,
    string DisplayName,
    string? Description,
    string? IconKey);
```

---

## File: `NodeEditor.Core/Interfaces/IGraphSearchProvider.cs`

```csharp
namespace NodeEditor.Core.Interfaces;

/// <summary>
/// Extension point for the Find-in-Graph feature. Host implements this to
/// expose extra searchable text per node (e.g., bound variable references).
/// </summary>
public interface IGraphSearchProvider
{
    /// <summary>Return whitespace-separated searchable terms for a node.</summary>
    string GetSearchableText(INodeModel node);
}
```

---

## File: `NodeEditor.Core/Interfaces/IPinDefaultValueEditorRegistry.cs`

```csharp
using NodeEditor.Primitives;

namespace NodeEditor.Core.Interfaces;

/// <summary>
/// Registry of type-key → editor mappings for inline pin defaults.
/// The editor library auto-registers built-in editors at init time.
/// Hosts can override any by re-registering.
/// </summary>
public interface IPinDefaultValueEditorRegistry
{
    /// <summary>Register or replace an editor for the given type.</summary>
    void Register(TypeKey type, IPinDefaultValueEditor editor);

    /// <summary>Register a fallback used when no type-specific editor matches.</summary>
    void RegisterFallback(IPinDefaultValueEditor editor);

    /// <summary>Look up the editor for a type, or null if none.</summary>
    IPinDefaultValueEditor? GetEditor(TypeKey type);
}
```

---

## File: `NodeEditor.Core/Interfaces/IDetailsViewProvider.cs`

```csharp
using NodeEditor.Primitives;

namespace NodeEditor.Core.Interfaces;

/// <summary>
/// Builds a <see cref="IDetailsView"/> for a given target. Multiple
/// providers may register; first matching (highest priority) wins.
/// </summary>
public interface IDetailsViewProvider
{
    int Priority { get; }
    bool CanHandle(DetailsTarget target);
    IDetailsView Build(DetailsTarget target, IDetailsContext ctx);
}

/// <summary>An instance of a Details panel view bound to a specific target.</summary>
public interface IDetailsView
{
    void Draw(IDetailsRenderContext ctx);
    bool IsDirty { get; }
    void Commit();
    void Revert();
}

/// <summary>Context handed to providers at Build time.</summary>
public interface IDetailsContext
{
    IGraphCommandSink CommandSink { get; }
    IPinDefaultValueEditorRegistry Editors { get; }
    IIconProvider Icons { get; }
    IEditorTheme Theme { get; }
}

/// <summary>Context handed to a view at Draw time.</summary>
public interface IDetailsRenderContext
{
    IIconProvider Icons { get; }
    IEditorTheme Theme { get; }
    bool ShowAdvanced { get; }
    bool ShowHelpTooltips { get; }
}

/// <summary>Target the Details panel is currently displaying.</summary>
public abstract record DetailsTarget
{
    public sealed record None : DetailsTarget;
    public sealed record SingleNode(NodeId Id) : DetailsTarget;
    public sealed record MultipleNodes(IReadOnlyList<NodeId> Ids) : DetailsTarget;
    public sealed record Variable(string VariableId) : DetailsTarget;
    public sealed record Function(string FunctionId) : DetailsTarget;
    public sealed record Macro(string MacroId) : DetailsTarget;
    public sealed record CustomEvent(string EventId) : DetailsTarget;
    public sealed record EventDispatcher(string DispatcherId) : DetailsTarget;
    public sealed record LocalVariable(string FunctionId, string LocalId) : DetailsTarget;
    public sealed record FunctionEntry(string FunctionId) : DetailsTarget;
    public sealed record Comment(CommentId Id) : DetailsTarget;
    public sealed record Asset : DetailsTarget;
}
```

---

## File: `NodeEditor.Core/Action/IEditorCommands.cs`

```csharp
using System.Numerics;
using NodeEditor.Primitives;

namespace NodeEditor.Core.Action;

/// <summary>
/// Surface exposing the editor's commands to a host shell. The host binds
/// these to its toolbars, menus, and hotkeys.
/// </summary>
public interface IEditorCommands
{
    /// <summary>All published commands.</summary>
    IReadOnlyList<EditorCommandDescriptor> All { get; }

    /// <summary>Look up a single command by id, or null.</summary>
    EditorCommandDescriptor? Get(string commandId);

    /// <summary>Invoke a command. Returns success/failure with optional message.</summary>
    EditorCommandResult Invoke(string commandId, EditorCommandContext? ctx = null);

    /// <summary>Raised when a command's enabled/checked state changes (host should refresh UI).</summary>
    event Action<string>? AvailabilityChanged;
}

/// <summary>Static metadata for a single command.</summary>
public sealed record EditorCommandDescriptor(
    string Id,
    string DisplayName,
    string? Category,
    string? Description,
    string? IconKey,
    KeyBinding? DefaultKey,
    Func<bool> IsEnabled,
    Func<bool>? IsChecked = null);

/// <summary>Optional invocation context (e.g., screen position for context-menu commands).</summary>
public readonly record struct EditorCommandContext(
    Vector2? ScreenPos,
    Vector2? CanvasPos,
    IReadOnlyDictionary<string, object?>? Args);

public readonly record struct EditorCommandResult(bool Success, string? Message);

/// <summary>Default key binding for a command.</summary>
public readonly record struct KeyBinding(EditorKey Key, KeyModifiers Modifiers)
{
    public override string ToString()
    {
        var parts = new List<string>(4);
        if ((Modifiers & KeyModifiers.Ctrl) != 0)  parts.Add("Ctrl");
        if ((Modifiers & KeyModifiers.Shift) != 0) parts.Add("Shift");
        if ((Modifiers & KeyModifiers.Alt) != 0)   parts.Add("Alt");
        if ((Modifiers & KeyModifiers.Super) != 0) parts.Add("Super");
        parts.Add(Key.ToString());
        return string.Join('+', parts);
    }
}
```

---

## File: `NodeEditor.Core/Action/IEditorIndicators.cs`

```csharp
using System.Numerics;

namespace NodeEditor.Core.Action;

/// <summary>
/// Read-only status surface for a host shell to read editor state and
/// receive notifications.
/// </summary>
public interface IEditorIndicators
{
    /// <summary>Current status snapshot.</summary>
    EditorStatusSnapshot Snapshot { get; }

    /// <summary>Raised when the snapshot changes.</summary>
    event Action? Changed;

    /// <summary>Emit a notification (toast).</summary>
    void Notify(EditorNotification notification);
}

/// <summary>Snapshot of editor state for status bar / chrome rendering.</summary>
public readonly record struct EditorStatusSnapshot(
    string? CurrentGraphName,
    int NodeCount,
    int SelectedNodeCount,
    int LinkCount,
    bool IsDirty,
    int ErrorCount,
    int WarningCount,
    float Zoom,
    Vector2 CanvasCursorPos,
    EditorMode Mode,
    string? CurrentTool);

/// <summary>Coarse editor mode classification.</summary>
public enum EditorMode
{
    Editing,
    Compiling,
    Debugging,
    DebugPaused,
}

/// <summary>One notification posted by the editor for the host to render.</summary>
public sealed record EditorNotification(
    string Id,
    NotificationSeverity Severity,
    string Title,
    string? Body,
    TimeSpan? AutoDismiss,
    IReadOnlyList<NotificationAction>? Actions);

public enum NotificationSeverity { Info, Success, Warning, Error }

/// <summary>An action shown in a notification (a button that invokes a command).</summary>
public sealed record NotificationAction(string Label, string CommandId);
```

---

These complete the kernel's interface surface. Implementation classes
(canvas renderer, picker window, my-blueprint panel, etc.) belong in
`NodeEditor.UI` and are the bulk of the task list — agent implements
those.
