# D.7 — The Details Panel

## Purpose

Right-side default. Shows editable properties of whatever's currently selected — node on canvas, variable in My Blueprint, function entry node, etc. This is where the bulk of "configuring blueprint behavior" happens. For projects using StructEdit-style infrastructure, the Details panel naturally hosts those `IComponentEditService` sessions.

## D.7.1 The big idea

The Details panel is **stateless** and **dispatched**. Given a "what's selected right now?" signal, it picks the right *details view* and renders it. Different selections → different views. The editor library defines built-in views; the host can register more.

```csharp
public interface IDetailsPanel
{
    void SetTarget(DetailsTarget? target);
    DetailsTarget? CurrentTarget { get; }
    event Action? Changed;
}

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

The editor sets the target based on:

- Canvas selection (highest priority): the primary selected node, or the primary comment if all selections are comments.
- If canvas selection is empty and My Blueprint has a selection: that's the target.
- If neither: target is `Asset` (shows top-level asset properties).

## D.7.2 Anatomy

```
┌────────────────────────────────────┐
│ Details                         ⋮  │   ← header with overflow menu
├────────────────────────────────────┤
│ [breadcrumb / target label]        │
│ ƒ ComputeDamage > Inputs           │
├────────────────────────────────────┤
│ 🔍 [ filter properties        ]    │   ← optional, V2
├────────────────────────────────────┤
│ ▾ Name                             │
│     Name:       [ ComputeDamage  ] │
│     Category:   [ Combat ▾ ]       │
│ ▾ Signature                        │
│     Inputs:                        │
│       ≡ BaseDamage : float ×       │
│       ≡ Multiplier : float ×       │
│       + Add input                  │
│     Outputs:                       │
│       ≡ Result : float        ×    │
│       + Add output                 │
│ ▾ Behavior                         │
│     Pure:    ☐                     │
│     Replicated: ☐                  │
│ ▾ Documentation                    │
│     Description:                   │
│     [                          ]   │
│     Keywords: [ damage, math  ]    │
└────────────────────────────────────┘
```

- **Header bar** with panel title + overflow `⋮` menu (Reset to Defaults, Expand All, Collapse All, Help).
- **Breadcrumb**: shows what's being edited, with parent context if nested.
- **Filter box** (V2): for targets with many properties.
- **Sections (categories)**: collapsible groups. Order fixed per view; user-collapsed state persists per view-type globally (confirmed in design).

## D.7.3 Built-in views — what each shows

### `SingleNode(Id)` — most common

Per-node, the panel shows:

```
▾ Node
    Title:        [ Multiply (Vector × Vector)         ]
    Comment:      [ optional user note                 ]
    Disabled:     ☐
    Advanced pins shown: ☐
▾ Pin Defaults
    A : Vector3   [ X 1.0  Y 0.0  Z 0.0 ]
    B : Vector3   [ X 1.0  Y 1.0  Z 1.0 ]
▾ Debug
    Breakpoint: ☐
    Watched pins:
        ○ A   ○ B   ○ Result
```

Inline default values appear here too (mirror of what's on the pin) — sometimes easier to edit in the panel for tightly-packed nodes.

**Node-kind-specific properties**: defined by the host. For example, a Channel Command node might add:

```
▾ Channel
    Channel Type:    [ Locomotion ▾ ]
    Action:          [ MoveTo ▾ ]
    Action Params (from action schema):
        TargetPosition: [ X 0  Y 0  Z 0 ]
        Speed:          [ 1.0 ]
        Tolerance:      [ 0.5 ]
```

### Custom view providers

```csharp
public interface IDetailsViewProvider
{
    bool CanHandle(DetailsTarget target);
    IDetailsView Build(DetailsTarget target, IDetailsContext ctx);
}

public interface IDetailsView
{
    void Draw(IDetailsRenderContext ctx);
    bool IsDirty { get; }
    void Commit();
    void Revert();
}
```

Multiple providers can register; first one whose `CanHandle` returns true is used. For a `SingleNode` target where the node kind has a registered provider, that provider takes over; otherwise a default property-tree view is used.

**For projects with StructEdit-style infrastructure**: implement `IDetailsViewProvider.Build` for `BlueprintNode` to call `_componentEditService.Open(node)` and return an `IDetailsView` that wraps the resulting `IEditSession` + `ComponentEditDrawer`. The Details panel is essentially a managed window for `ComponentEditDrawer`. Existing StructEdit work powers Details automatically — no double infrastructure.

### `MultipleNodes(Ids)` — multi-select

Shows the **intersection** of properties across selected nodes. Common properties get an editor that bulk-edits all of them; mixed values show as `(multiple values)` placeholder.

```
▾ Common Properties (8 nodes selected)
    Disabled:        [ ─ ] (mixed)
    Advanced shown:  [ ☐ ]
▾ Bulk Actions
    Align Top                    [Apply]
    Align Left                   [Apply]
    Distribute Horizontally      [Apply]
```

Setting a field with mixed values commits the same value to all. The `[ ─ ]` indicator visually distinguishes "field has different values across selection." Editing it sets all to the new value.

### `Variable(VariableId)` — variable from My Blueprint

```
▾ Variable
    Name:           [ Health                ]
    Type:           [ float ▾ ]
    Category:       [ Combat/Stats          ]
▾ Default Value
    Default:        [ 100.0 ]
▾ Exposure
    Editable:       ☑
    Expose on Spawn:☐
    Read Only:      ☐
▾ Replication
    Replicated:     ☐
    Rep Notify:     [ —— ▾ ]    (function picker)
▾ Documentation
    Tooltip:        [                       ]
```

Changing the Type opens the type picker. Changing the type **may break wires** in graphs that reference this variable; show a confirmation toast with affected-references count.

### `Function(FunctionId)` / `FunctionEntry(FunctionId)`

These two targets show **the same details** — clicking the function name in My Blueprint or selecting the Entry node on canvas both show the function's signature editor.

```
▾ Function
    Name:           [ ComputeDamage         ]
    Category:       [ Combat                ]
    Description:    [ ...                   ]
▾ Inputs
    ≡ BaseDamage : float    [default: 10]  ×
    ≡ Multiplier : float    [default:  1]  ×
    + Add input
▾ Outputs
    ≡ Result : float                       ×
    + Add output
▾ Behavior
    Pure:               ☐
    Const:              ☐    (V2)
▾ Calling
    Call in Editor:     ☐    (V2: button to invoke as test)
```

### `Macro(MacroId)`

Like Function but:

- No "Pure" / "Const".
- Inputs and Outputs can be exec or data — type picker offers both.
- "Wildcard" checkbox per pin type field.

### `CustomEvent(EventId)`

```
▾ Custom Event
    Name:           [ OnEnemyKilled         ]
    Category:       [ Combat                ]
    Description:    [ ...                   ]
▾ Parameters
    ≡ EnemyId : int                    ×
    ≡ Killer : Entity                  ×
    + Add parameter
▾ Network
    Replicated:     ☐
    Reliable:       ☐ (greyed unless Replicated)
    Run On Server:  ○
    Run On Client:  ○
    Multicast:      ○
```

### `EventDispatcher(DispatcherId)`

```
▾ Event Dispatcher
    Name:           [ OnHealthChanged       ]
    Category:       [ default               ]
▾ Parameters
    ≡ NewValue : float
    ≡ OldValue : float
    + Add parameter
▾ Documentation
    Tooltip:        [ ... ]
```

### `LocalVariable(FunctionId, LocalId)`

Similar to Variable but no exposure section (locals are never exposed) and a hint at the top:

```
ⓘ This is a local variable in ComputeDamage.
   It cannot be accessed from other graphs.
─────────────────────────────────────────
▾ Variable
    Name:           [ tmpScaled             ]
    Type:           [ float ▾ ]
▾ Default Value
    Default:        [ 0.0 ]
```

### `Comment(Id)`

```
▾ Comment
    Text:           [ multi-line text       ]
                    [                       ]
                    [                       ]
    Color:          [██]    (palette + custom picker)
    Font Size:      [ 1.0× ▾ ]   (V2)
▾ Behavior
    Move with contents:  ☑
```

### `Asset` — nothing selected

Top-level asset properties (host extends):

```
▾ Asset
    Name:           [ MyBlueprint           ]
    Display Name:   [ ...                   ]
    Description:    [ ...                   ]
    Category:       [ ...                   ]
▾ Compilation
    Mode:           [ Debug ▾ ]   (Debug / Release / Trace)
    Last Compiled:  2026-05-21 10:42
▾ Dispatch                 ← host-specific
    Kind:           [ Instance ▾ ]
    ...
```

### `None` — explicit empty

Shows a placeholder: `Select something to see its properties here.`

## D.7.4 Property change flow

User edits a field. The change goes through the standard command pipeline:

1. Field's editor fires `onChanged(newValue)` (potentially many times during a drag).
2. The view holds the change in a *pending state* (not yet committed).
3. On commit (mouse-up, Enter, focus-out): view emits a `SetNodeProperty` / `SetVariableProperty` / etc. command.
4. Host applies via `IGraphCommandSink`; view rebuilds from the post-mutation model.

This means **the Details panel never directly mutates the model**. It composes commands. Same architecture as the canvas. This is what makes everything undoable uniformly.

Specifically: dragging a `DragFloat` to change a node's "Default Value" pin fires many `changed` events but only one `committed` → one `SetPinDefault` command → one undo step. Same as inline pin editing on the canvas.

## D.7.5 Reactive updates

The Details panel re-fetches from the model when:

- Selection changes (different target).
- The model emits `GraphChanged` for an entity the panel is displaying.
- The user manually clicks "Refresh" in the overflow menu.

If the displayed entity is **deleted externally** (e.g., variable removed via undo), the panel transitions to `None` and shows a brief toast: `The selected item was removed.`

## D.7.6 Validation & inline errors

Fields validate as you type:

- Variable name: must be valid identifier, unique within its scope.
- Default value: must parse as the declared type.
- Category: any string; no validation.
- Function inputs with duplicate names: red border + inline error.

Invalid state means the commit is rejected (or the field keeps the old value visually, the dirty marker stays on, and an error icon shows next to the field). The user must fix before commit succeeds.

For broader validation (e.g., a custom event with the same name as an inherited one), the host returns an error from the command's `Apply`, and the panel displays it as a notification.

## D.7.7 Default property tree (fallback view)

When a node has **no host-registered details view**, the editor renders a default property tree from reflection over the node's CLR type. This means every node has *something* editable even before the host registers a custom view.

For projects using existing infrastructure like `ImGuiPropertyTree.Render`: that's the default fallback view's implementation.

## D.7.8 Overflow menu (⋮)

Top-right of the panel, expandable:

```
Collapse All Sections
Expand All Sections
─────────
Reset This Item to Defaults     (host-implemented per type)
─────────
Help…                            (opens host's help / docs)
Show Help Tooltips: ☑
```

## D.7.9 Layout / sizing

- Minimum width: ~280 px. Below that, sections may overflow horizontally; tolerate gracefully.
- Default width: ~360 px.
- The user can resize via the panel splitter (host's responsibility).
- Two-column layout: label on left (~140 px), control on right (fills remaining width). Standard StructEdit pattern.

## D.7.10 Performance

The panel is **dispatched once per selection change**, then re-renders only as needed. Per-frame work:

- Read current target.
- If target unchanged and no events fired: short-circuit, render cached widgets.
- Otherwise: rebuild the view (cheap; ~ms even for complex nodes).

For very rich node properties (e.g., a graph with 200 parameters), virtualization within the panel may matter; defer until needed.

## D.7.11 Section collapse state persistence

Confirmed in design: section collapse state is persisted **globally**, not per-target. Collapsing "Replication" stays collapsed across all variables until the user expands it again.

Mechanism: each view declares a stable section ID; collapse state is stored in editor session state under `details.sections.{view-type}.{section-id}`.
