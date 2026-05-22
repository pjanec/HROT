# D.2 — Function UX

## What a function is

A **callable sub-graph** with declared inputs and outputs. Compared to a custom event:

- Has return values (outputs).
- Can be called *and* returns values.
- Runs synchronously (no latent nodes).
- Can be pure (no exec pins) or impure (has exec pins).

## D.2.1 Creation flow

`+` next to "Functions" in My Blueprint, or `editor.create-function`.

Form (uses picker chrome):

```
┌────────────────────────────────────────────┐
│ Create Function                         ✕  │
├────────────────────────────────────────────┤
│ Name:        [ ComputeDamage           ]   │
│                                            │
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

**"Pure" checkbox**: critical decision. Pure functions have no exec pins, are called via data-flow only, can be called multiple times per frame, must have no side effects. The validator enforces purity in the function body.

## D.2.2 What gets created

Two special nodes appear in the new function graph:

**Entry node** (input pins as outputs):

```
┌─────────────────────────────────┐
│ ⚡ ComputeDamage [Entry]         │   ← blue header (function)
├─────────────────────────────────┤
│                          ▶ Then │   (no exec for pure funcs)
│                BaseDamage ◯     │
│                Multiplier ◯     │
└─────────────────────────────────┘
```

**Return node** (output pins as inputs):

```
┌─────────────────────────────────┐
│ ⏎ Return                        │
├─────────────────────────────────┤
│ ▶ In                            │   (no exec for pure)
│ ◯ Result: float                 │
└─────────────────────────────────┘
```

The Entry node sits at canvas origin; the Return node ~400 px to the right. User draws their logic between them.

Both special nodes are **non-deletable** in the function graph.

## D.2.3 Multiple return nodes

A function graph can have **multiple Return nodes** (Unreal-style). They all share the same output signature; whichever Return executes first determines the function's output values. The compiler enforces that all paths through exec flow eventually hit a Return.

Right-click empty canvas → "Add Return Node" creates a second Return wired to nothing.

## D.2.4 Editing inputs / outputs

When the Entry node OR a Return node is selected, the Details panel shows two lists:

```
Function: ComputeDamage
─────────────────────────────
Name:        [ ComputeDamage         ]
Pure:        ☐
Category:    [ Combat ▾ ]

Inputs:
┌──────────────────────────────────────┐
│ ≡ BaseDamage  : float [def: 10.0] ×  │
│ ≡ Multiplier  : float [def: 1.0]  ×  │
│ + Add input                          │
└──────────────────────────────────────┘

Outputs:
┌──────────────────────────────────────┐
│ ≡ Result : float                   × │
│ + Add output                         │
└──────────────────────────────────────┘
```

Changes propagate to all Entry/Return nodes inside the graph **and** all call sites elsewhere. Same identity-by-Guid preservation as custom events.

## D.2.5 Local variables

Functions can have **local variables** scoped to just the function. They live in the My Blueprint panel under the function's collapsed children:

```
▾ Functions
   ▾ ComputeDamage
       ◇ Inputs (2)
       ◇ Outputs (1)
       ◇ Local Variables (1)
           tmpScaled: float
```

`+` next to "Local Variables" → standard variable-creation flow.

Local variables are visible only when inside that function's graph; the variable picker (when used inside the function) shows asset-level variables *and* this function's locals.

## D.2.6 Calling a function

Drag function from My Blueprint onto canvas → call node placed:

```
┌─────────────────────────────────┐
│ ƒ ComputeDamage                 │   ← blue header (function call)
├─────────────────────────────────┤
│ ▶ In             ▶ Then         │
│ ◯ BaseDamage    Result ◯        │
│ ◯ Multiplier                    │
└─────────────────────────────────┘
```

For pure functions: no exec pins.

Right-click call node → "Go to Definition" navigates into the function's graph (opens tab or switches to existing tab).

## D.2.7 Function recursion

If a function calls itself (directly or indirectly), the editor allows it but the validator flags it with a warning: `Recursive call detected. Ensure base case exists.` The host decides whether to permit recursion at compile time.

## D.2.8 Collapse to Function

The killer refactor command. User selects nodes, invokes `editor.collapse-to-function`:

1. Editor analyzes the selection:
   - Inputs: any wire from outside the selection into a selected node's input pin.
   - Outputs: any wire from a selected node's output pin to outside the selection.
   - Exec entry: incoming exec wire to selection.
   - Exec exit: outgoing exec wires from selection.
2. Opens the Function creation form, pre-populated with detected inputs/outputs.
3. User confirms name and any tweaks.
4. Editor:
   - Creates the new function with detected signature.
   - Moves selected nodes into the function graph (preserves internal wiring).
   - Replaces the selection with a single call node in the original graph.
   - Connects external wires to the call node.
5. The whole operation is a single Batch command — one undo step.

### Edge cases

- **Multiple exec exits going to different external destinations**: the function gets multiple output exec pins (Unreal-style multi-exit functions). The form shows detected exits and lets the user rename them.
- **Pure subgraph (no exec wires)**: offered as a Pure function. User can override.
- **Multiple disconnected components in selection**: rejected with notification `Selection must be one connected subgraph.`

## D.2.9 Function deletion

Right-click function in My Blueprint → Delete:

- If no call sites: silently deletes.
- If call sites exist: confirmation `Delete ComputeDamage? 3 call sites will be broken.` User confirms; call sites become red error nodes `Unknown function: ComputeDamage`. User can manually fix or undo.
