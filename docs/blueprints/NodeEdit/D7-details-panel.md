# D.7 — Details Panel

Right-side panel. Stateless dispatcher: renders properties of whatever is
currently the editor's "details target."

## D.7.1 Target dispatching

```csharp
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

### Target selection priority

1. Canvas selection (primary) — if any selected.
2. My Blueprint selection — if no canvas selection.
3. `Asset` — when nothing else selected.

Multi-selection on canvas:
- Same kind → `MultipleNodes` with intersection of properties.
- Mixed kinds → fall back to `Asset` or "Multiple Selection" placeholder.

## D.7.2 View provider interface

```csharp
public interface IDetailsViewProvider
{
    int Priority { get; }
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

public interface IDetailsContext
{
    IGraphCommandSink CommandSink { get; }
    IPinDefaultValueEditorRegistry Editors { get; }
    IIconProvider Icons { get; }
    IEditorTheme Theme { get; }
}
```

Host registers providers; first matching (by `CanHandle` and highest
`Priority`) wins.

Default fallback: read-only property tree via reflection on whatever public
fields/properties the host exposes.

## D.7.3 Layout

```
┌────────────────────────────────────┐
│ Details                       ⋮    │   ← header + overflow menu
├────────────────────────────────────┤
│ 🟥 ▣ Damage  (float)               │   ← breadcrumb / target label
├────────────────────────────────────┤
│ 🔍 [filter properties…]            │   ← V2 (live filter)
├────────────────────────────────────┤
│ ▾ Variable                         │
│     Name:         [ Damage ]       │
│     Type:         [ float ▾]       │
│     Default:      [ 100.0 ]        │
│     Tooltip:      [ ... ]          │
│     Category:     [ Combat ▾]      │
│                                    │
│ ▾ Replication                      │
│     Replicated:   ☐                │
│     RepCondition: [ None ▾]        │
│                                    │
│ ▾ Editor                           │
│     Expose on Spawn:  ☑            │
│     Editable:         ☑            │
│     Slider Range:                  │
│         Min:  [ 0  ]               │
│         Max:  [ ∞  ]               │
└────────────────────────────────────┘
```

- Two-column inside sections (label ~140 px, control fills remaining width).
- Sections collapsible. Collapse state persists **globally per section name**
  (not per-target). User collapses "Replication" once, stays collapsed across
  variables.
- Min width 280 px; default 360 px; user-resizable.

## D.7.4 Property mutation flow

All mutations go through `IGraphCommandSink`. View accumulates pending
changes; on commit (mouse-up, Enter, focus-out) emits one command (or a
Batch).

The view should never directly mutate host data.

Per-frame edits (slider drag) are visual only via the view's local state.

## D.7.5 Per-target views

### `None`

Empty panel with hint text:
```
       (no selection)

Click a node, variable, or item in
My Blueprint to view its properties.
```

### `SingleNode`

Sections:
- Node — title, subtitle, ID, position, comment.
- Pin Defaults — collapsible per-pin sub-sections (only for nodes with
  default-valued input pins).
- Custom (host-provided via `IDetailsContent` registered for the NodeKindKey).
- Advanced — ID, kind, type info.

### `MultipleNodes`

- "N nodes selected" header.
- Show only properties common to all (intersection).
- Mixed values shown as `[ — ]` placeholder. Setting it applies to all.
- Always show: "Comment" (group comment), bulk position controls.

### `Variable`

- Variable section: Name, Type, Default, Tooltip, Category.
- Replication (if asset supports): Replicated, RepCondition, RepNotify.
- Editor: Exposed on Spawn, Editable, ReadOnly Per Default.
- Range: Min/Max (only for numeric types).
- Advanced: Variable ID.

### `Function`

- Function: Name, Category, Tooltip, Description.
- Pure: ☐
- Replicated, RepNotify: ☐ ☐
- Inputs: list editor (add/remove/reorder).
- Outputs: list editor.
- Local Variables: list (read-only summary; manage from MyBlueprint).
- Compile: AccessSpecifier (Public/Protected/Private).

### `Macro`

- Macro: Name, Category, Tooltip, Description.
- Inputs/Outputs: list editor (each entry: Name, Type or Exec, Wildcard ☐).

### `CustomEvent`

- Custom Event: Name, Category, Tooltip.
- Replicated, Reliable: ☐ ☐
- Parameters: list editor.

### `EventDispatcher`

- Dispatcher: Name, Category, Tooltip.
- Parameters: list editor.

### `LocalVariable`

Same as `Variable` but without Replication section.

### `FunctionEntry`

- Read-only target info ("This is the entry node of ComputeDamage").
- Link to edit function signature → switches target to `Function`.

### `Comment`

- Comment: Text (multi-line input), Color (color swatch), Move with Contents ☑.
- Layout: Position, Size.

### `Asset`

- Top-level metadata (host-defined).
- Class settings, parent class, blueprint type.

## D.7.6 List editors (param/input/output lists)

Used in Function / Macro / CustomEvent / EventDispatcher views.

```
Inputs (2):
  ▼ BaseDamage : float       [×]
       Type:    [ float ▾]
       Default: [ 0.0 ]
       Description: [ ... ]
  ▼ Multiplier : float       [×]
       Type:    [ float ▾]
  [ + Add input ]
```

- Click row header to expand/collapse parameter details.
- Drag handle on left side to reorder.
- `×` removes (confirm if connections exist).
- `+ Add` opens type picker then prompts for name.

Renaming a parameter via Details propagates to all call sites (preserves
wires by stable parameter Guid). Same as flow described in D.1/D.2.

## D.7.7 Section show/hide

Sections marked "Advanced" hidden by default. Toggle via:
- Each section header right-side gear icon.
- Overflow menu (⋮) → "Show Advanced Properties: ☐".

## D.7.8 Overflow menu (⋮)

```
Collapse All Sections
Expand All Sections
─────
Reset This Item to Defaults
Reset Section to Defaults
─────
Show Advanced Properties: ☑
Show Help Tooltips:      ☑
─────
Help…
```

## D.7.9 Help tooltips

When `Show Help Tooltips` enabled, hovering a property label shows tooltip
after 600ms with field description (provided by the view).

## D.7.10 Filter properties (V2)

Live filter box atop Details body. Filters property rows by label match.
Sections with no matching properties hide.

## D.7.11 Commit semantics

- Per-keystroke / per-drag: view-model updates only.
- On commit (Enter / focus-out / mouse-up): single command emitted.
- `IsDirty` reflects un-committed transient state (rare; usually committed
  immediately).
- `Commit` / `Revert` explicit methods used when target switches with
  pending changes:
  - If switching to another target with `IsDirty` → commit pending changes
    first (silent), then switch.
  - Esc reverts pending.

## D.7.12 Performance

The Details panel rebuilds on target change. Within a target, rendering is
lightweight (a handful of editor widgets). No virtualization needed.

Building the view should be cheap. View providers should not allocate
per-frame in `Draw`. Build-time work is one-shot.

## D.7.13 Drag in / out targets

Some details fields are drop targets (e.g., Entity reference accepts entity
drag from world panel). Each drop-target editor declares which payload types
it accepts. Highlight green border on drag-hover. Source `IPickerSource`
similarly declares `AllowsDragOut`.
