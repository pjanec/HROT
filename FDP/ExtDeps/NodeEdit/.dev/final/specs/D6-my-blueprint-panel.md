# D.6 — My Blueprint Panel

Left-side navigator/outliner. Lists everything declarable in current asset.

## D.6.1 Layout

```
┌────────────────────────────────┐
│ My Blueprint                ⊕▼ │
├────────────────────────────────┤
│ 🔍 [ search                ]   │
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
│     ▾ Combat                   │
│         🟥 ▣ Damage : float    │
│         🟥 ▣ Range : float     │
│     ▾ default                  │
│         🟦 ▣ Name : string     │
│ ▾ Event Dispatchers (1)      ⊕ │
│     📢 OnHealthChanged         │
│ ▾ Interfaces                 ⊕ │   (V2)
│     (none)                     │
├────────────────────────────────┤
│ Show: ☑ Inherited ☐ Generated  │   (V2)
└────────────────────────────────┘
```

## D.6.2 Sections

Top-level, fixed order:

1. Graphs — root entries (host-defined; cannot be created from here).
2. Functions (pure and impure; pure shown with `[pure]` badge).
3. Macros
4. Custom Events
5. Variables — asset-level (member) variables.
6. Event Dispatchers
7. Interfaces (V2)
8. Sub-sections when function open: Inputs, Outputs, Local Variables.

## D.6.3 Host interfaces

```csharp
public sealed record MyBlueprintSectionDescriptor(
    string Id,
    string DisplayName,
    int SortOrder,
    string? IconKey,
    bool CanCreateItems,
    bool CanHaveCategories,
    string? CreateCommandId);

public interface IMyBlueprintModel
{
    IReadOnlyList<MyBlueprintSectionDescriptor> Sections { get; }
    IReadOnlyList<MyBlueprintItem> GetItems(string sectionId);
    event Action? Changed;
}

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

The editor doesn't know what a "function" is — just renders items in
sections. Host adapts its data model behind `IMyBlueprintModel`.

## D.6.4 Item visuals

Each row: icon, accent dot (for type color), name, optional badge.

```
🟦 ▣ Health : float                    ← variable: blue dot = float type
🟥 ▣ Damage : float        [exposed]
ƒ ComputeDamage
ƒ IsAlive                  [pure]
⚙ ForEachWithBreak
⚡ OnEnemyKilled
📢 OnHealthChanged
```

Inline badges:
- `[pure]` green
- `[exposed]` blue
- `[replicated]` purple
- `[deprecated]` red strikethrough

### Color and icon source

- Variable accent color: `ITypeSystem.GetPinColor(varType)`. SAME as wire
  color in graphs. User has visual continuity: blue dot in panel → blue
  wire when used.
- Container types: shape indicates (◆ array, ⊞ map).
- Functions/macros/events get section-default icon from theme.

## D.6.5 Categories

Variables and Functions support nested categories: `Combat/Stats/Damage`.
Render as expandable folders.

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
```

Items without category → virtual "default" folder at bottom.
Sort: categories alphabetic; items within category alphabetic.

Categories per-section: Variables' "Combat" is independent from Functions'
"Combat".

### Edit category

Via Details panel CategoryPath field. Or RMB → "Move to Category…" opens
picker prefilled with existing categories + "[New category…]".

## D.6.6 Search filter

🔍 box at top filters tree to items matching search (fuzzy matcher from
picker §16). Sections with matches stay visible; empty sections hide;
categories auto-expand. Matched characters highlighted.

Esc / clear restores previous collapse state.

## D.6.7 Interactions

| Action | Behavior |
|---|---|
| LMB click item | Select; Details panel updates. No navigation. |
| Double-click item | Navigate: open graph, focus variable, etc. |
| RMB click | Context menu per item type. |
| LMB drag onto canvas | Variable → menu Get/Set (Ctrl=Get, Alt=Set). Func/Macro/Event → place call node. |
| Drop on wire | Insert if compatible. |
| LMB drag within section | Reorder (V2). |
| LMB drag onto category | Move to category (V2). |

### Drag payload identifiers

Each draggable item exposes typed payload like
`"NodeEditor.MyBlueprint.Variable"` so external drop targets (e.g., inline
pin editors) can accept.

## D.6.8 Context menus per item type

### Variable
```
Get
Set
─────
Find References          Ctrl+Shift+F
Duplicate                Ctrl+D
Rename                   F2
Delete                   Del
─────
Move to Category…
Change Type…
─────
Copy Reference
─────
Properties…              F4
```

### Function
```
Go to Function           ⏎
─────
Find References          Ctrl+Shift+F
Duplicate
Rename                   F2
Delete                   Del
─────
Move to Category…
Convert to Pure / Impure
─────
Add Input
Add Output
Add Local Variable
─────
Properties…              F4
```

### Custom Event
Like Function but no "Convert to Pure"; no "Outputs" submenu (events have
no return).

### Macro
Like Function but no "Pure" option; inputs/outputs can be exec or data.

### Event Dispatcher
```
Call
Bind
Unbind
Unbind All
─────
Find References
Rename
Delete
─────
Add Parameter
─────
Properties…
```

First 4 items create matching nodes on canvas when chosen.

### Graph entry (EventGraph, ConstructionScript)
```
Open Graph               ⏎
Find in this Graph       Ctrl+F
─────
Properties…
```

## D.6.9 Header "Add new" menu

`⊕▼` at top-right → dropdown:

```
+ Variable
+ Function
+ Macro
+ Custom Event
+ Event Dispatcher
─────
+ Implement Interface…    (V2)
```

Each invokes corresponding `editor.create-*` command.

Section-level `⊕` next to each section header is shortcut to same command.

## D.6.10 Show toggles (V2)

Two checkboxes at bottom:
- **Inherited**: shows items inherited from parent class/interface. Greyed.
  Read-only.
- **Generated**: shows compiler-synthesized items.

Both default off.

## D.6.11 Keyboard nav when panel focused

| Key | Action |
|---|---|
| ↑ ↓ | Move selection (skip collapsed children) |
| ← → | Collapse / expand folder |
| Home / End | First / last visible |
| Enter | Navigate (= double-click) |
| F2 | Rename |
| Delete | Delete |
| Ctrl+D | Duplicate |
| F4 | Open Properties |
| / | Focus search box |

## D.6.12 Multi-select

**Not in MVP.** Single-select only. Revisit only if users ask.

## D.6.13 Reordering

User-order preserved in host data (List-backed, not alphabetic). Drag-to-
reorder exposed as V2.
