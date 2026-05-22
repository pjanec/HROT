# D.6 — The "My Blueprint" Panel

## Purpose

The navigator/outliner. Sits on the left of the editor by default. Lists everything *declarable* in the current asset: variables, functions, custom events, event dispatchers, macros, interfaces, local variables (in scope).

It's the entry point for **creating, finding, organizing, and dragging** graph elements onto the canvas.

## D.6.1 Layout overview

```
┌────────────────────────────────┐
│ My Blueprint                ⊕▼ │   ← header with global "Add new" menu
├────────────────────────────────┤
│ 🔍 [ search                ]   │   ← inline find within panel
├────────────────────────────────┤
│ ▾ Graphs                     ⊕ │
│     ⚡ EventGraph              │
│     ⚡ ConstructionScript      │
│ ▾ Functions (3)              ⊕ │
│   ▾ ƒ ComputeDamage            │
│       ◇ Inputs (2)             │
│       ◇ Outputs (1)            │
│       ◇ Local Variables (1)    │
│           ▣ tmpScaled : float  │
│     ƒ ApplyKnockback           │
│     ƒ IsAlive          [pure]  │
│ ▾ Macros (1)                 ⊕ │
│     ⚙ ForEachWithBreak         │
│ ▾ Custom Events (2)          ⊕ │
│     ⚡ OnEnemyKilled            │
│     ⚡ OnLevelUp                │
│ ▾ Variables (5)              ⊕ │
│     🟦 ▣ Health : float        │
│     🟪 ▣ Position : Vector3    │
│     ▾ Combat                   │   ← user-defined category
│         🟥 ▣ Damage : float    │
│         🟥 ▣ Range : float     │
│     ▾ default                  │
│         🟦 ▣ Name : string     │
│ ▾ Event Dispatchers (1)      ⊕ │
│     📢 OnHealthChanged         │
│ ▾ Interfaces                 ⊕ │   ← V2
│     (none)                     │
├────────────────────────────────┤
│ Show: ☑ Inherited ☐ Generated  │
└────────────────────────────────┘
```

## D.6.2 The tree structure

Top-level sections, in fixed order:

1. **Graphs** — root entry points (EventGraph, ConstructionScript, custom-named graphs the host adds). Special: cannot be created from here; they're host-defined.
2. **Functions** — both impure and pure. Pure shown with `[pure]` badge.
3. **Macros**
4. **Custom Events**
5. **Variables** — asset-level (member) variables.
6. **Event Dispatchers** — declared dispatchers/delegates.
7. **Interfaces** — implemented interfaces (V2).
8. *(Function-scope only when one is open):* **Inputs**, **Outputs**, **Local Variables** as sub-nodes under the function.

### Section configuration

The host configures which sections appear:

```csharp
public sealed record MyBlueprintSectionDescriptor(
    string Id,                       // "variables", "functions", ...
    string DisplayName,
    int SortOrder,
    string? IconKey,
    bool CanCreateItems,             // is the "+" button shown?
    bool CanHaveCategories,
    string? CreateCommandId);        // editor command for "+"

public interface IMyBlueprintModel
{
    IReadOnlyList<MyBlueprintSectionDescriptor> Sections { get; }
    IReadOnlyList<MyBlueprintItem> GetItems(string sectionId);
    event Action? Changed;
}

public sealed record MyBlueprintItem(
    string ItemId,                   // stable, opaque to editor
    string SectionId,
    string DisplayName,
    string? CategoryPath,            // "Combat/Stats" → nested category folders
    string? IconKey,
    string? BadgeText,               // "[pure]", "[replicated]", "[deprecated]"
    Vector4? AccentColor,            // type color for variables
    IReadOnlyList<MyBlueprintItem>? Children, // for nested (function inputs/outputs)
    bool IsRenamable,
    bool IsDeletable,
    bool IsHostDefined,              // built-in EventGraph cannot be edited
    string? Tooltip);
```

The editor doesn't know what a "function" is — it just renders items in sections. The asset host implements `IMyBlueprintModel` over the actual asset structure.

## D.6.3 Item visuals

Each row shows: icon, accent dot (for type color), name, optional badge.

```
🟦 ▣ Health : float                  ← variable: blue dot = float type
🟥 ▣ Damage : float        [exposed] ← variable with exposure badge
ƒ ComputeDamage                      ← function (impure)
ƒ IsAlive                  [pure]    ← pure function (green badge)
⚙ ForEachWithBreak                   ← macro
⚡ OnEnemyKilled                      ← custom event (red badge)
📢 OnHealthChanged                    ← dispatcher
```

Inline badges are tinted by their meaning: `[pure]` green, `[exposed]` blue, `[replicated]` purple, `[deprecated]` red strikethrough.

### Color and icon source

Variables get a colored circle/square matching their type's pin color (from `ITypeSystem.GetPinColor`). This is the same color the wire would have.

For container types: shaped icon (◆ for array, ⊞ for map) instead of round dot.

## D.6.4 Categories

Variables and other items support nested categories: `Combat/Stats/Damage`. In the tree, categories render as expandable folders inside their section.

```
▾ Variables (8)
    🟦 ▣ Health : float
    ▾ Combat (3)
        🟥 ▣ Damage : float
        🟥 ▣ Range : float
        ▾ Stats (1)
            🟥 ▣ CritChance : float
    ▾ default (4)
        🟦 ▣ Name : string
        ...
```

Items without a category live under a virtual "default" folder shown at the bottom. Category folders sort alphabetically; items within a category sort alphabetically.

Category assignment: edit the item's `CategoryPath` field via the Details panel. Or right-click → "Move to Category…" opens a picker prefilled with existing categories + "[New category…]" option.

Categories are **per-section** — Variables' "Combat" category is independent from Functions' "Combat" category.

## D.6.5 Search filter (within the panel)

The 🔍 box at the top filters the visible tree to items whose display name, category, or type contains the search text. Uses the same fuzzy matcher as the global picker (see `C_generic_picker.md` and `K04_fuzzy_matcher.md`).

While searching:

- Section headers stay visible if they contain matches.
- Empty sections are hidden.
- Categories auto-expand to reveal matches.
- Matched characters highlighted in the item names.

Clearing the search box (Esc or ✕) restores the previous expansion state. **The user's manual collapsed-state for sections/categories is preserved across search sessions.**

## D.6.6 Interactions

**LMB-click an item:**

- Selects it (single-select within the panel).
- Updates Details panel to show that item's properties.
- **Does not** navigate to a graph or focus the canvas. Just selects.

**Double-click an item:**

- For a Graph entry: opens that graph in a tab (or switches to existing tab).
- For a Function / Custom Event / Macro: opens its graph.
- For a Variable: focuses the variable in any graph that references it (if no current focus, no-op).
- For an Input/Output/Local: opens the parent function's graph.
- Generally: navigate to the most useful place.

**RMB-click an item:** context menu (see D.6.7).

**Drag-and-drop:**

- Drag a Variable onto canvas → opens a small menu: "Get / Set". Drop without modifier shows the menu; **Ctrl+drag** = Get directly; **Alt+drag** = Set directly.
- Drag a Function/Macro/Custom Event onto canvas → places a call node immediately.
- Drag a Local Variable (only valid inside the parent function's graph) → same as variable.
- Drag onto a wire → if the dropped item creates a node that fits the wire's pin types, insert it into the wire. Otherwise drop is rejected (red flash).

The drag uses ImGui's payload system. Each draggable item exposes a typed payload (e.g., `"NodeEditor.MyBlueprint.Variable"`) so external drop targets (like inline pin editors) can accept the same drags.

**Reordering by drag (V2):**

- Drag a variable within its section to reorder. Only within the same section; cross-section drag is rejected.
- Drag onto a category folder to move into that category.

## D.6.7 Context menus per item type

### Variable

```
Get
Set
─────────
Find References          Ctrl+Shift+F
Duplicate                Ctrl+D
Rename                   F2
Delete                   Del
─────────
Move to Category…
Change Type…
─────────
Copy Reference                          (puts a special payload on clipboard)
─────────
Properties…              F4             (focuses Details panel)
```

### Function

```
Go to Function           ⏎
─────────
Find References          Ctrl+Shift+F
Duplicate
Rename                   F2
Delete                   Del
─────────
Move to Category…
Convert to Pure / Impure                 (if applicable)
─────────
Add Input
Add Output
Add Local Variable
─────────
Properties…              F4
```

### Custom Event

Like Function but no "Convert to Pure" and no "Outputs" subsection.

### Macro

Like Function but no "Pure" option and inputs/outputs can be exec or data.

### Event Dispatcher

```
Call
Bind
Unbind
Unbind All
─────────
Find References
Rename
Delete
─────────
Add Parameter
─────────
Properties…
```

The first 4 items create the matching nodes on the canvas when chosen.

### Graph entry (EventGraph, ConstructionScript)

```
Open Graph               ⏎
Find in this Graph       Ctrl+F
─────────
Properties…
```

(Graphs themselves are usually host-defined and not deletable; some hosts may allow custom graphs.)

## D.6.8 Header "Add new" menu

The `⊕▼` at the top-right of the panel header is a single click → dropdown:

```
+ Variable
+ Function
+ Macro
+ Custom Event
+ Event Dispatcher
─────────────
+ Implement Interface…    (V2)
```

Each invokes the corresponding `editor.create-*` command. Section-level `⊕` next to each section header is a shortcut to the same command.

## D.6.9 "Show: Inherited / Generated" toggles

Two filter toggles at the bottom of the panel:

- **Inherited**: when checked, shows items inherited from a parent class/interface/template (host-determined). Greyed-out style. Read-only.
- **Generated**: when checked, shows items the compiler synthesized (auto-promoted variables, helpers). Useful for debugging the compiler; off by default.

Host declares whether items are inherited/generated via `MyBlueprintItem.IsHostDefined`. For MVP Slice 1, both toggles can default off and stay off; this is V2 territory.

## D.6.10 Keyboard navigation within the panel

When the panel has focus:

| Key | Action |
|---|---|
| `↑` / `↓` | Move selection up/down (skip collapsed children) |
| `←` / `→` | Collapse / expand the focused folder or section |
| `Home` / `End` | First / last visible item |
| `Enter` | Double-click equivalent — navigate |
| `F2` | Rename |
| `Delete` | Delete |
| `Ctrl+D` | Duplicate |
| `F4` | Open Properties (move focus to Details panel) |
| `/` | Focus the search box |

Keyboard-driven workflow: focus panel, type `/`, search, ↓ to a variable, Enter — graph opens, variable selected. Common Unreal speed-edit pattern.

## D.6.11 Multi-selection

**No multi-selection within My Blueprint** in MVP. A single selected item drives the Details panel. Multi-select is V2+ if users request it.
