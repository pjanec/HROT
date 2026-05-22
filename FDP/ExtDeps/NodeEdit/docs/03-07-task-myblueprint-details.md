# Tasks T-15 and T-16 — My Blueprint and Details Panels

These two panels are the editor's side surfaces. **My Blueprint** is the
hierarchical outline (left side by default); **Details** is the property
inspector (right side by default).

Both are pure ImGui code; they read view-model + host interfaces and emit
commands.

---

# T-15 — UI: My Blueprint Panel

## Goal
Render the hierarchical outline of the asset's variables, functions,
macros, custom events, dispatchers, and graph entries. Handle clicks,
double-clicks (navigate), drag-onto-canvas (place node), and right-click
context menus.

## Project
`NodeEditor.UI`

## References

**Specs:**
- `../specs/D6-my-blueprint-panel.md` — full normative behavior
- `../instructions/01-spec-brief-part2.md` §18 (My Blueprint panel)

**Kernel:**
- `../kernel/04-my-blueprint-and-rest.md` — `IMyBlueprintModel` interface and records
- `../kernel/03-search-spatial-constants.md` — `FuzzyMatcher` (search box)

## Deliverables

```
src/NodeEditor.UI/
    Panels/
        MyBlueprintPanel.cs
        MyBlueprintItemRenderer.cs       // draw one row + icon + badges
        MyBlueprintDragSource.cs         // drag payload helpers
        MyBlueprintContextMenu.cs        // per-item-type context menus
```

## Public surface

```csharp
namespace NodeEditor.UI.Panels;

/// <summary>
/// The "My Blueprint" outline panel.
/// Render once per frame inside any ImGui window region.
/// Reads <see cref="IMyBlueprintModel"/> data; writes through editor
/// commands and triggers navigation via callbacks supplied at construction.
/// </summary>
public sealed class MyBlueprintPanel
{
    public MyBlueprintPanel(
        IMyBlueprintModel model,
        IEditorHostServices host,
        IEditorCommands commands,
        Action<GraphId> navigateToGraph,
        Action<string, string> navigateToItem);   // (sectionId, itemId)

    /// <summary>Draw the panel inside the current ImGui window/region.</summary>
    public void Draw();

    /// <summary>The currently selected item, used by Details panel target dispatch.</summary>
    public MyBlueprintItem? SelectedItem { get; }

    /// <summary>The section the selected item belongs to.</summary>
    public string? SelectedSectionId { get; }

    /// <summary>Raised whenever the user picks a different item (single click).</summary>
    public event Action<MyBlueprintItem?>? SelectionChanged;
}
```

## Implementation outline

```
Draw()
├── Header bar:  "My Blueprint"  + [+▼ Add new] popup button
├── Search box  ("/" focuses)
├── For each section in model.Sections.OrderBy(s => s.SortOrder):
│     ├── Section header row (icon, name, count, + button)
│     ├── If expanded:
│     │     ├── group items by Category (using item.CategoryPath, split on "/")
│     │     ├── render category folders recursively
│     │     ├── render uncategorized items in "default" folder
│     │     └── for each item: MyBlueprintItemRenderer.Render(item)
│     └── (collapsed: skip)
└── Footer toggles (V2): "Show Inherited", "Show Generated"
```

## Item row rendering

```csharp
namespace NodeEditor.UI.Panels;

internal static class MyBlueprintItemRenderer
{
    /// <summary>
    /// Render one item row. Returns true if the user clicked it (selection event).
    /// Handles: selection highlight, accent dot (type color), icon, name,
    /// badge chip, drag-source registration, right-click context menu.
    /// </summary>
    public static bool Render(
        MyBlueprintItem item,
        bool isSelected,
        IIconProvider icons,
        IEditorTheme theme);
}
```

Pixel layout per row (left to right):

```
[selection bg] [indent] [accent dot 8px] [icon 16x16] [name] [badge chip] [right-aligned tooltip ⓘ on hover]
```

The accent dot uses `item.AccentColor`. The icon uses `item.IconKey` looked
up through `icons.Resolve(key)`. The badge chip is small text in a rounded
rect (max ~80 px wide).

## Search filter behavior

When the search box has non-empty text:
- Walk all items in all sections.
- Score each with `FuzzyMatcher.Score(query, item.DisplayName, item.Keywords)`.
- Auto-expand sections + categories containing matches.
- Hide sections/categories with no matches.
- Highlight matched chars in the rendered name (use `MatchPositions` returned
  by the matcher to drive ImGui colored-text rendering).

Esc in the search box clears the text and restores previous collapse state
(stored before the search began).

## Drag-from-panel

Each item row is a drag source. Use `ImGui.BeginDragDropSource()` /
`SetDragDropPayload()` with the item's payload type:

| Item kind | Payload type string |
|---|---|
| Variable | `"NodeEditor.MyBlueprint.Variable"` |
| Function | `"NodeEditor.MyBlueprint.Function"` |
| Macro | `"NodeEditor.MyBlueprint.Macro"` |
| Custom Event | `"NodeEditor.MyBlueprint.CustomEvent"` |
| Dispatcher | `"NodeEditor.MyBlueprint.EventDispatcher"` |

Payload data: the item's `ItemId` string (8 bytes pointer + length, or
fixed-size buffer). The canvas accepts drops and dispatches an
`AddNode` command (host-defined node kind for "VariableGet", etc.).

While dragging a variable, hold Ctrl to bias toward Get, Alt toward Set.
If neither: drop opens a small "Get / Set" picker at drop location.

## Context menu per item type

See `../specs/D6-my-blueprint-panel.md` §D.6.8 for exact menus. Each menu
entry invokes an `IEditorCommands` command. The renderer doesn't
implement actions directly — only menu structure.

```csharp
// Example: variable context menu
ImGui.MenuItem("Get",              "",          false, true) → commands.Invoke("editor.create-variable-get", new EditorCommandContext { Args = { ["variableId"] = item.ItemId } });
ImGui.MenuItem("Set",              "",          false, true) → commands.Invoke("editor.create-variable-set", ...);
ImGui.Separator();
ImGui.MenuItem("Find References",  "Ctrl+Shift+F") → commands.Invoke("editor.find-references", ...);
// ...
```

## Acceptance

- Compiles.
- Demo (T-20) shows the panel populated with a fake `IMyBlueprintModel`
  containing 2 variables, 1 function, 1 macro, 1 custom event.
- Single-click selects (raises `SelectionChanged`), Details panel updates
  in response.
- Double-click on a graph entry navigates.
- Search filters live and highlights matched characters.
- Drag a variable onto the canvas; canvas's drop handler creates a node.

## Estimated Size
~400 LOC across panel + renderer + drag-source + context menu.

## Status
Pending.

---

# T-16 — UI: Details Panel

## Goal
Render the property inspector for the editor's current "details target."
Stateless dispatcher: chooses the right `IDetailsView` from the registered
providers and draws it.

## Project
`NodeEditor.UI`

## References

**Specs:**
- `../specs/D7-details-panel.md` — full normative behavior
- `../instructions/01-spec-brief-part2.md` §19 (Details panel)

**Kernel:**
- `../kernel/04-my-blueprint-and-rest.md` — `IDetailsViewProvider`, `IDetailsView`, `DetailsTarget`

## Deliverables

```
src/NodeEditor.UI/
    Panels/
        DetailsPanel.cs
        DetailsViewRegistry.cs           // registry of providers
        Views/
            FallbackDetailsView.cs       // generic property-tree fallback
            CommentDetailsView.cs        // built-in for Comment target
            MultipleNodesDetailsView.cs  // intersection-of-properties view
```

The host typically registers its own provider for `SingleNode`, `Variable`,
`Function`, etc. — those are NOT in scope for this task. The editor only
ships fallback views.

## Public surface

```csharp
namespace NodeEditor.UI.Panels;

/// <summary>
/// The Details panel. Render once per frame inside any ImGui window region.
/// Picks the best registered <see cref="IDetailsViewProvider"/> for the
/// current target and delegates rendering. Persists section collapse state
/// globally (per section-name key, not per target).
/// </summary>
public sealed class DetailsPanel
{
    public DetailsPanel(
        IDetailsViewRegistry registry,
        IDetailsContext context);

    /// <summary>The current target (driven by canvas selection or MyBlueprint).</summary>
    public DetailsTarget Target { get; set; } = new DetailsTarget.None();

    /// <summary>Show advanced (host-marked) sections.</summary>
    public bool ShowAdvanced { get; set; }

    /// <summary>Show help tooltips on property labels.</summary>
    public bool ShowHelpTooltips { get; set; } = true;

    /// <summary>Draw inside the current ImGui region.</summary>
    public void Draw();
}

/// <summary>Provider registry.</summary>
public interface IDetailsViewRegistry
{
    void Register(IDetailsViewProvider provider);
    IDetailsView? GetViewFor(DetailsTarget target, IDetailsContext ctx);
}
```

Default `DetailsViewRegistry` implementation iterates providers in
`Priority` desc order and returns the first whose `CanHandle(target)` is
true.

## Section collapse persistence

Persist a `HashSet<string>` of expanded section names in the panel's
backing field (keyed by name only, NOT by target). When the panel renders
a section with `BeginCollapsible(name, defaultOpen)`, it looks up the name
in the set.

## Target switching

When `Target` setter is called and differs from previous:
1. If current view `IsDirty`: silently call `Commit()` to flush pending
   changes.
2. Set new target; build new view via registry.
3. Cache the view; reuse across frames until target changes again.

## Layout

```
┌────────────────────────────────────┐
│ Details                       ⋮    │   header with overflow menu
├────────────────────────────────────┤
│ ▼ 🟥 Damage  (float)               │   breadcrumb (icon + name + type)
├────────────────────────────────────┤
│ [filter properties…]               │   (V2; render placeholder)
├────────────────────────────────────┤
│                                    │
│   (delegated to IDetailsView.Draw) │
│                                    │
└────────────────────────────────────┘
```

## Overflow menu (⋮)

Top-right corner button. Menu items:
- "Collapse All Sections" — clears the expanded set.
- "Expand All Sections" — sets all known section names expanded.
- "Reset This Item to Defaults" — calls `view.ResetToDefaults()` if
  implemented (optional method on `IDetailsView`).
- "Show Advanced Properties: ☑" — toggle.
- "Show Help Tooltips: ☑" — toggle.

## Fallback view (reflection)

For unrecognized targets, `FallbackDetailsView` walks public properties of
the target's identity-mapped host object (the host provides a method to
look it up via `IDetailsContext.GetSubjectObject(target)`). Renders
read-only.

```csharp
namespace NodeEditor.UI.Panels.Views;

internal sealed class FallbackDetailsView : IDetailsView
{
    private readonly object? _subject;

    public FallbackDetailsView(object? subject) { _subject = subject; }

    public bool IsDirty => false;
    public void Commit() { }
    public void Revert() { }

    public void Draw(IDetailsRenderContext ctx)
    {
        if (_subject is null)
        {
            ImGui.TextColored(ctx.Theme.TextMuted, "(no target)");
            return;
        }

        foreach (var prop in _subject.GetType().GetProperties())
        {
            if (!prop.CanRead) continue;
            ImGui.Text(prop.Name + ":");
            ImGui.SameLine();
            var v = prop.GetValue(_subject);
            ImGui.TextDisabled(v?.ToString() ?? "<null>");
        }
    }
}
```

## CommentDetailsView (built-in)

When target is `DetailsTarget.Comment`, render:
- Text: `InputTextMultiline` — commits on Enter or focus-out → emits
  `UpdateComment(Text)`.
- Color: ImGui color picker → emits `UpdateComment(Color)`.
- Move with Contents: checkbox → emits `UpdateComment(MoveWithContents)`.
- Position: two DragFloats → emits `UpdateComment(Position)`.
- Size: two DragFloats → emits `UpdateComment(Size)`.

## Mini-editor integration

Property rows that edit pin defaults reuse the mini-editor registry from
T-13:

```csharp
var editor = ctx.Editors.GetEditor(pin.Type!.Value);
if (editor is not null)
{
    object? v = pin.Default?.Value;
    bool changed = editor.Render(in editorCtx, ref v);
    if (changed) { /* dispatch SetPinDefault */ }
}
```

## Acceptance

- Compiles.
- Demo (T-20) shows the panel.
- Selecting a comment in canvas → CommentDetailsView appears.
- Editing comment text via Details panel → reflects on canvas immediately.
- Selecting a variable in MyBlueprint → host-provided variable view appears
  (the demo registers its own provider).
- Section collapse state persists when switching targets within the same
  section names.

## Estimated Size
~350 LOC.

## Status
Pending.
