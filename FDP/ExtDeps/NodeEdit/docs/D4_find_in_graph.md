# D.4 — Find in Graph UX

## What this is

A focused find panel — separate from the picker. Inline within the graph editor, more like VSCode's Ctrl+F than a popup.

## D.4.1 Activation

`editor.find-in-graph` (default `Ctrl+F`) opens a slim find bar at the top of the canvas:

```
┌──────────────────────────────────────────────────────────────────────┐
│ 🔍 [ Multiply                          ] ▶ ▲ ▼  [Aa] [.*]  3/12  ✕  │
└──────────────────────────────────────────────────────────────────────┘
       │                                  │  │ │   │   │     │     │
       │                                  │  │ │   │   │     │     close
       │                                  │  │ │   │   │     match count
       │                                  │  │ │   │   regex toggle
       │                                  │  │ │   case-sensitive toggle
       │                                  │  │ next match
       │                                  │  previous match
       │                                  open scope dropdown
       search query
```

When `editor.find-in-asset` is invoked (`Ctrl+Shift+F`), the same bar opens with scope set to "Asset" — searches across all graphs in the current asset, results listed in a side panel.

## D.4.2 Scope dropdown ▶

The ▶ button opens a small scope menu:

```
┌────────────────────────────┐
│ ◉ Current Graph            │
│ ○ Current Asset (all graphs)│
│ ○ Open Tabs                │
│ ○ Whole Project            │   ← if host supports it
└────────────────────────────┘
```

Default scope is **Current Graph**. Each scope shapes the result display.

## D.4.3 What's searched

Per node, the searchable text is:

- Node title and subtitle.
- Node category.
- All pin labels.
- All pin default values (string-rendered).
- Comment text.
- Variable references inside the node.

The host can extend the searchable surface:

```csharp
public interface IGraphSearchProvider
{
    string GetSearchableText(INodeModel node);
}
```

## D.4.4 Match visualization

In **Current Graph** scope:

- Matching nodes get a **bright yellow outline** + yellow tint on the matching pin/header section.
- The active match (the "current" one) gets an even stronger highlight + a pulsing border.
- Non-matching nodes dim to ~40% opacity. **This is the killer feature** — instantly see the match in context.
- Canvas auto-centers on the active match (animated, ~180 ms — same animation as Frame All).

Stepping with ▲ ▼ (or F3 / Shift+F3):

- Cycle through matches in **proximity-to-current** order (rather than creation order). Suggest proximity for "what's near where I'm looking."
- Wrap around at ends.

Match count `3/12` updates live as the query changes.

## D.4.5 Esc behavior

- If find bar has text: clear the text but stay open.
- If empty: close the find bar.

This is the VSCode pattern; everyone expects it.

## D.4.6 "Current Asset" scope — side panel

When scope is broader than current graph, a **Find Results** panel slides in (left side of editor, on top of My Blueprint or as a tab):

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
│     ...                                      │
│ ▸ ApplyKnockback (2)                         │
│ ▸ ForEachWithBreak (2)                       │
└──────────────────────────────────────────────┘
```

Click any result → opens that graph tab, frames on the node. Active result highlighted in the canvas.

Keyboard navigation: ↑↓ navigate, Enter jumps, Esc returns focus to canvas (without closing the panel).

## D.4.7 Special search prefixes

Power-user filters in the search input:

| Prefix | Searches |
|---|---|
| `type:Vector3` | Only nodes with a Vector3 pin |
| `kind:branch` | Only nodes whose Kind contains "branch" |
| `category:math` | Only nodes in Math/* category |
| `var:HealthCurrent` | Only references to that variable |
| `func:ComputeDamage` | Only call sites to that function |
| `error:` | Only nodes with errors |
| `warning:` | Only nodes with warnings |
| `breakpoint:` | Only nodes with breakpoints |
| `watched:` | Only nodes with watched pins |

Combinable: `kind:branch category:flow` finds branch-like nodes in flow category.

## D.4.8 "Find References" — slightly different UX

`editor.find-references` (RMB on a variable / function / event in My Blueprint, or on a node):

- Opens the Find Results panel directly in "Current Asset" scope.
- Pre-fills the search with a structured query like `var:HealthCurrent` (so the user sees what's being searched and can edit).
- Lists all reference call sites.

One-button version of the broader find, optimized for "show me where X is used."

## D.4.9 "Go to Definition"

`editor.go-to-definition` (F12 by default, like every IDE):

- On a function call node → opens the function's graph, centers on Entry.
- On a variable reference → opens My Blueprint, scrolls to the variable's declaration row (and selects it, so Details panel shows it).
- On a custom event call → opens the event's graph.
- On a macro call → opens the macro's graph (or expands it inline if user prefers; configurable).

## D.4.10 Performance — find at scale

- **Current Graph** scope: always live (filters as you type).
- **Current Asset** scope: live up to ~1000 nodes; beyond, switches to "press Enter to search" mode to avoid stutter.
- **Whole Project**: always "press Enter to search" — host provides results asynchronously.

A loading indicator in the find bar shows when the host is searching.

## D.4.11 Find bar position

The find bar slides down from the top of the canvas area (above any tabs or rulers, below the host's main menu). It's an overlay — doesn't push the canvas content. When it closes, slides back up.

Height: ~32 px. Width: fills canvas width (minus a small margin on each side).

## D.4.12 Toggle modifiers

Two buttons on the right side of the find bar:

- `Aa` — case-sensitive toggle (off by default).
- `.*` — regex toggle (off by default).

When regex is on, the search query is interpreted as a .NET regex. Invalid regex shows red border + tooltip with parse error.

## D.4.13 Performance reuses fuzzy matcher

For the default (non-prefix) search, the find feature uses the same fuzzy matcher as the picker (`K04_fuzzy_matcher.md`). Single implementation, consistent behavior. Special prefixes are evaluated by a separate prefix-parser layer before invoking the matcher on the remaining text.
