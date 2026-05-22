# D.3 — Macro UX

## What a macro is

An **inline-expanded sub-graph**. Unlike a function:

- Multiple exec inputs and outputs (more complex control flow).
- Can contain latent nodes (Delay, WaitForChannel) where functions can't.
- "Pasted" at the call site by the compiler rather than being a real callable.

Think of it like a C `#define` for graphs.

## D.3.1 Creation flow

Same as function but with differences:

```
┌────────────────────────────────────────────┐
│ Create Macro                            ✕  │
├────────────────────────────────────────────┤
│ Name:    [ ForEachWithBreak             ]  │
│ Category:[ Loops ▾ ]                       │
│                                            │
│ Inputs (0):                                │
│   [ + Add input ]                          │
│                                            │
│ Outputs (0):                               │
│   [ + Add output ]                         │
│                                            │
│ Description:                               │
│ [                                       ]  │
├────────────────────────────────────────────┤
│         [ Cancel ]    [ Create Macro ]     │
└────────────────────────────────────────────┘
```

No "Pure" option. No "Replicated."

## D.3.2 What gets created

Two non-deletable special nodes:

```
┌─────────────────────────────────┐    ┌─────────────────────────────────┐
│ ⚙ ForEachWithBreak [Inputs]     │    │ ⚙ ForEachWithBreak [Outputs]    │
├─────────────────────────────────┤    ├─────────────────────────────────┤
│             ▶ Loop Body         │    │ ▶ Body End                      │
│             ▶ Complete          │    │ ▶ Break                         │
│             ◯ ArrayItem         │    │                                 │
└─────────────────────────────────┘    └─────────────────────────────────┘
```

Notice macros have **multiple output exec pins** on the Entry node (Loop Body, Complete) and **multiple input exec pins** on the Outputs node (Body End, Break). This is what lets macros express complex control flow that functions can't.

## D.3.3 Editing macro signature

Same as functions — Details panel shows two lists when Entry/Outputs node is selected. Exec pins coexist with data pins in the input/output lists; the user marks each pin as exec or data when adding it.

```
Macro: ForEachWithBreak
─────────────────────────────
Inputs:
┌──────────────────────────────────────┐
│ ≡ Array : Array<T> [wildcard]      × │
│ ≡ ▶ Reset                          × │   (exec pin)
│ + Add input                          │
└──────────────────────────────────────┘

Outputs:
┌──────────────────────────────────────┐
│ ≡ ▶ Loop Body                      × │   (exec pin)
│ ≡ Item : T [wildcard]              × │
│ ≡ Index : int                      × │
│ ≡ ▶ Complete                       × │
│ + Add output                         │
└──────────────────────────────────────┘
```

## D.3.4 Wildcards in macros

A standout feature: macro pins can be **wildcards**. The wildcard resolves to the concrete type at each call site, based on what's connected. This is how `For Each Loop` works in Unreal — the array type is wildcard, resolved when the user connects an actual array.

The Details panel lets the user mark a type as `<T>` (wildcard) instead of a concrete type. Wildcards on multiple pins can be:
- **Same** `<T>` (linked — must resolve together).
- **Different** `<T>` `<U>` (independent).

When opened, the macro's pin colors show as **white/grey/striped** to indicate "unresolved wildcard" — visually different from any concrete type color. The graph editor in the macro's internal view shows wildcard pins greyed (you can't connect them to typed pins inside; only to other wildcard pins of the same type variable).

## D.3.5 Calling a macro

Drag macro from My Blueprint → call node. At call time, wildcard types resolve based on connections. The call node visual shows the resolved type for clarity:

```
┌─────────────────────────────────┐
│ ⚙ ForEachWithBreak <Vector3>   │   ← shows resolved wildcard
├─────────────────────────────────┤
│ ▶ In            ▶ Loop Body     │
│ ◯ Array        Item ◯           │
│ ▶ Reset        Index ◯          │
│                ▶ Complete       │
└─────────────────────────────────┘
```

## D.3.6 Expand Node

Right-click call node → "Expand Node" inlines the macro's body into the calling graph. Useful for debugging or specialization. Inverse of Collapse to Macro.

This is a one-shot operation — there's no "re-link" after expanding. If the user wants to modify just one instance of macro usage, they expand it and edit the inlined nodes.

## D.3.7 Latent inside macros

Macros can contain latent nodes (Delay, WaitForChannel) where functions can't. The validator enforces this asymmetry: a macro containing latent nodes can only be called from contexts that allow latent (Event graphs, not pure function graphs).

## D.3.8 Collapse to Macro

Same flow as Collapse to Function, but the produced sub-graph is a macro. Editor offers both options when collapsing:

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

### Auto-detect heuristic

- **Latent inside** → must be macro.
- **Multiple exec exits to different destinations** → macro or function-with-multi-exit.
- **Pure data subgraph** → pure function.
- **Everything else** → function (simpler).

## D.3.9 Macro deletion

Same as function deletion. Call sites become red error nodes on broken references.
