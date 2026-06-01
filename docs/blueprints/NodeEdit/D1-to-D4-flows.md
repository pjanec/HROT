# D.1–D.4 — Authoring Flows and Find

## D.1 Custom event creation

### Flow

User clicks `+` next to "Custom Events" in My Blueprint, or invokes
`editor.create-custom-event`.

Modal form opens (using picker chrome):

```
┌────────────────────────────────────────────┐
│ Create Custom Event                     ✕  │
├────────────────────────────────────────────┤
│ Name:    [ OnEnemyKilled                ]  │
│                                            │
│ Category: [ Combat ▾ ]                     │
│                                            │
│ Description:                               │
│ [                                       ]  │
│                                            │
│ Parameters (0):                            │
│   [ + Add parameter ]                      │
│                                            │
│ Replicated: ☐                              │
│ Reliable:   ☐ (greyed unless replicated)   │
├────────────────────────────────────────────┤
│         [ Cancel ]    [ Create Event ]     │
└────────────────────────────────────────────┘
```

Name validation:
- Must be valid identifier.
- Must be unique within asset.
- Red border + inline error if invalid.

### After creation

- New entry in My Blueprint under "Custom Events".
- New graph tab opens.
- Entry node placed at canvas center.
- Focus moves to new graph.

### Entry node

```
┌─────────────────────────────────┐
│ ⚡ OnEnemyKilled                 │   ← red header
├─────────────────────────────────┤
│                          ▶ Then │   ← output exec
│  EnemyId: int            ◯      │   ← param output (data)
│  Killer:  Entity         ◯      │
└─────────────────────────────────┘
```

- **Cannot be deleted** from inside graph. RMB → Delete greyed.
- **Renamable** via inline double-click on header or Details panel.
- Parameters edited in Details panel when entry node selected.

### Parameter changes propagate

Adding/removing/reordering parameters updates Entry node AND all call sites.
Existing wires preserved by parameter Guid identity.

Deleted parameters → wires broken at call sites. Notification:
"Parameter 'X' removed. 3 connections broken in 2 graphs."

### Calling

Drag custom event from My Blueprint onto canvas. Places "Call OnEnemyKilled":

```
┌─────────────────────────────────┐
│ ⚡ Call OnEnemyKilled            │   ← purple header
├─────────────────────────────────┤
│ ▶ In           ▶ Out            │
│ ◯ EnemyId: int                  │
│ ◯ Killer:  Entity               │
└─────────────────────────────────┘
```

RMB call node → "Go to Definition" opens event's graph.

### Renaming

Three paths (all run same command):
- Inline rename on Entry node header (double-click).
- Details panel name field.
- My Blueprint → Rename context menu (F2).

Side effect: all call sites update. If rename conflicts → reject with
inline error.

## D.2 Function creation

### Form

Like custom event but with:
- Inputs and Outputs (separate lists).
- "Pure" checkbox.

```
┌────────────────────────────────────────────┐
│ Create Function                         ✕  │
├────────────────────────────────────────────┤
│ Name:        [ ComputeDamage           ]   │
│ Category:    [ Combat ▾ ]                  │
│                                            │
│ Inputs (0):                                │
│   [ + Add input ]                          │
│                                            │
│ Outputs (0):                               │
│   [ + Add output ]                         │
│                                            │
│ Pure (no exec pins): ☐                     │
│   ⓘ Pure functions compute on demand.      │
│                                            │
│ Description:                               │
│ [                                       ]  │
├────────────────────────────────────────────┤
│         [ Cancel ]    [ Create Function ]  │
└────────────────────────────────────────────┘
```

### Entry and Return nodes

After creation, function graph has Entry node (left, blue header) and Return
node (right, ~400 px to the right):

```
┌─────────────────────────────────┐    ┌─────────────────────────────────┐
│ ƒ ComputeDamage [Entry]         │    │ ⏎ Return                        │
├─────────────────────────────────┤    ├─────────────────────────────────┤
│                          ▶ Then │    │ ▶ In                            │
│                BaseDamage ◯     │    │ ◯ Result: float                 │
│                Multiplier ◯     │    │                                 │
└─────────────────────────────────┘    └─────────────────────────────────┘
```

Pure functions: no exec pins on either.

Both nodes non-deletable in function graph.

### Multiple Return nodes

A function can have multiple Return nodes (Unreal-style). All share same
output signature.

RMB empty canvas → "Add Return Node" creates a second Return.

### Local variables

Scoped to function. Shown in My Blueprint under function:

```
▾ Functions
   ▾ ComputeDamage
       ◇ Inputs (2)
       ◇ Outputs (1)
       ◇ Local Variables (1)
           ▣ tmpScaled : float
```

`+` next to "Local Variables" → variable creation flow scoped to function.
Visible only inside that function's graph.

### Calling

Drag function from My Blueprint → place call node:

```
┌─────────────────────────────────┐
│ ƒ ComputeDamage                 │
├─────────────────────────────────┤
│ ▶ In             ▶ Then         │
│ ◯ BaseDamage    Result ◯        │
│ ◯ Multiplier                    │
└─────────────────────────────────┘
```

Pure functions: no exec pins.

RMB → "Go to Definition" navigates.

### Recursion

Allowed but validator warns: "Recursive call detected. Ensure base case
exists." Host decides whether to permit at compile time.

### Collapse to Function

`Ctrl+E` on selection:

1. Analyze:
   - Inputs: wires from outside into selection's input pins.
   - Outputs: wires from selection's output pins to outside.
   - Exec entry: incoming exec wire to selection.
   - Exec exit: outgoing exec from selection.
2. Open Function creation form, pre-populated.
3. User confirms.
4. Editor:
   - Creates function with detected signature.
   - Moves selected nodes into function graph.
   - Replaces selection with call node.
   - Connects external wires.
5. Single Batch command, single undo step.

Edge cases:
- Multiple exec exits to different external destinations → function gets
  multiple output exec pins, form shows detected exits.
- Pure subgraph → offered as Pure function; user can override.
- Disconnected selection components → rejected: "Selection must be one
  connected subgraph."

### Function deletion

RMB function in My Blueprint → Delete.
- No call sites: silent.
- Call sites exist: confirm "Delete ComputeDamage? 3 call sites broken."
  Confirm → call sites become red error nodes "Unknown function:
  ComputeDamage". Undo restores.

## D.3 Macro creation

### Differences from function

- No "Pure" option.
- No "Replicated."
- Inputs and outputs can be EXEC or DATA.
- Wildcards allowed.
- Latent nodes allowed inside (forbidden in functions).

### Entry and Outputs nodes

```
┌─────────────────────────────────┐    ┌─────────────────────────────────┐
│ ⚙ ForEachWithBreak [Inputs]     │    │ ⚙ ForEachWithBreak [Outputs]    │
├─────────────────────────────────┤    ├─────────────────────────────────┤
│             ▶ Loop Body         │    │ ▶ Body End                      │
│             ▶ Complete          │    │ ▶ Break                         │
│             ◯ ArrayItem         │    │                                 │
└─────────────────────────────────┘    └─────────────────────────────────┘
```

Notice macros have multiple output exec pins on Entry, multiple input exec
pins on Outputs. This is what enables loop-like control flow.

### Wildcards

Macro pins can be wildcards `<T>`. Resolves at each call site by what's
connected. Inside the macro graph, wildcards render as white/grey/striped
pins.

Multiple wildcards can be either:
- Same `<T>` (linked, must resolve together).
- Different `<T> <U>` (independent).

Details panel exposes "Wildcard" checkbox per pin type field.

### Calling a macro

Call node shows resolved wildcard types:

```
┌─────────────────────────────────┐
│ ⚙ ForEachWithBreak <Vector3>   │
├─────────────────────────────────┤
│ ▶ In            ▶ Loop Body     │
│ ◯ Array        Item ◯           │
│ ▶ Reset        Index ◯          │
│                ▶ Complete       │
└─────────────────────────────────┘
```

### Expand Node

RMB macro call → "Expand Node" inlines the macro body into the calling
graph. Inverse of Collapse to Macro.

### Collapse to Macro

Similar to Collapse to Function but produces a macro. Editor offers choice:

```
┌────────────────────────────────────────────┐
│ Collapse Selection To...                ✕  │
├────────────────────────────────────────────┤
│ ○ Function (callable, returns one value)   │
│ ○ Macro (inline-expanded, multi-exit)      │
│ ◉ Auto: choose based on selection content  │
│                                            │
│ ⓘ Selection contains a latent node, so a   │
│   Macro is required.                       │
├────────────────────────────────────────────┤
│         [ Cancel ]    [ Collapse ]         │
└────────────────────────────────────────────┘
```

Auto-detect heuristic:
- Latent inside → must be macro.
- Multiple exec exits to different destinations → macro or function with
  multi-exit (offer choice).
- Pure data subgraph → pure function.
- Default → function.

## D.4 Find / Go-to UX

### Find in Graph (Ctrl+F)

Slim find bar inserted at top of canvas:

```
┌──────────────────────────────────────────────────────────────────────┐
│ 🔍 [ Multiply                          ] ▶ ▲ ▼  [Aa] [.*]  3/12  ✕  │
└──────────────────────────────────────────────────────────────────────┘
       │                                  │  │ │   │   │     │     │
       │                                  │  │ │   │   │     │     close
       │                                  │  │ │   │   │     match count
       │                                  │  │ │   │   regex toggle
       │                                  │  │ │   case sensitive toggle
       │                                  │  │ next match
       │                                  │  previous match
       │                                  open scope dropdown
       search query (live filter)
```

### Visualization in current graph

- Matching nodes: yellow outline + yellow tint on matched pin/header section.
- Active match: stronger highlight + pulsing border.
- Non-matching nodes: dimmed to ~40% opacity.
- Canvas auto-centers on active match (animated 180 ms).

Stepping:
- ▲ ▼ buttons or F3 / Shift+F3.
- Cycle in node-creation order, wrap at ends.
- Match count "3/12" updates live.

### Esc behavior

- Has text: clear text but stay open.
- Empty: close find bar.

(VSCode pattern.)

### Scope dropdown

```
◉ Current Graph
○ Current Asset (all graphs)
○ Open Tabs
○ Whole Project    (if host supports)
```

### Searchable text

Per node:
- Title and subtitle
- Category
- All pin labels
- All pin default values (stringified)
- Comment text
- Variable references inside

Host extends via:
```csharp
public interface IGraphSearchProvider
{
    string GetSearchableText(INodeModel node);
}
```

### Special prefixes

| Prefix | Searches |
|---|---|
| `type:Vector3` | Nodes with that pin type |
| `kind:branch` | Kind name substring |
| `category:math` | Nodes in category |
| `var:HealthCurrent` | Variable references |
| `func:ComputeDamage` | Function call sites |
| `error:` | Nodes with errors |
| `warning:` | Nodes with warnings |
| `breakpoint:` | Nodes with breakpoints |
| `watched:` | Nodes with watched pins |

Combinable: `kind:branch category:flow`.

### Find in Asset (Ctrl+Shift+F)

Same bar, scope = Asset. Side panel shows results grouped by graph:

```
┌──────────────────────────────────────────────┐
│ Find Results                              ✕  │
│ "Multiply" — 12 matches across 4 graphs      │
├──────────────────────────────────────────────┤
│ ▾ EventGraph (5)                             │
│     Multiply (Vector × Vector)    [node]     │
│       Pin 'A' = Multiplier        [pin]      │
│       ...                                    │
│ ▾ ComputeDamage (3)                          │
│     Multiply (float × float)      [node]     │
│ ▸ ApplyKnockback (2)                         │
│ ▸ ForEachWithBreak (2)                       │
└──────────────────────────────────────────────┘
```

Click result → opens graph tab, frames node.

Keyboard within panel:
- ↑↓ navigate
- Enter jump to result
- Esc return focus to canvas (without closing panel)

### Find References

`editor.find-references` (RMB on item):
- Opens Find Results panel in Asset scope.
- Pre-fills with structured query: `var:HealthCurrent`, `func:ComputeDamage`.

### Go to Definition

`editor.go-to-definition` (F12):
- Function call → function's graph, centered on Entry.
- Variable reference → My Blueprint, scrolls to variable's row.
- Custom event call → event's graph.
- Macro call → macro's graph (or expand inline if configured).

### Performance

| Scope | Behavior |
|---|---|
| Current Graph | Live filter as-you-type. |
| Current Asset | Live up to ~1000 nodes; beyond, "press Enter to search". |
| Whole Project | Always "press Enter"; host async. |

Loading indicator in find bar during host async search.
