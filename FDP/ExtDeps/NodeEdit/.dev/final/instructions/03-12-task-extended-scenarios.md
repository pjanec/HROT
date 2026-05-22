# T-22b — Demo: Extended Scenarios (Authoring, Multi-Tab, Refactor, Find, Bookmarks, Comments, Big Graph)

## Goal
Cover the editor features that aren't exercised by S01–S14: function and
macro authoring, custom events, variables, multi-tab graphs, refactoring,
find, bookmarks, comments, hot-reload conflict handling, and a big-graph
performance scene.

Without these scenarios there's no easy way for a user to verify these
features work end-to-end.

## Project
`NodeEditor.Demo`

## When this runs
After T-20, T-21, T-22 (core demo + picker + debug viz) are complete and
after T-23 (bookmarks) and T-24 (hot-reload) so this task can exercise
them.

## References

**Specs:**
- `../specs/D1-to-D4-flows.md` — function / macro / custom event / find
- `../specs/D8-comments-reroutes.md` — comments
- `../specs/D9-bookmarks.md` — bookmarks
- `../specs/D10-hot-reload.md` — hot-reload conflict

## Deliverables

```
src/NodeEditor.Demo/
    Scenarios/
        S15_VariablesGetSet.cs           // create variable, drag Get/Set onto canvas
        S16_PromoteToVariable.cs         // RMB pin → Promote → variable appears
        S17_CustomEvent.cs               // create event with params, place call, edit params
        S18_FunctionAuthoring.cs         // create function, navigate in/out of body graph
        S19_MultipleReturnNodes.cs       // function with multiple Return nodes
        S20_MacroWithWildcards.cs        // wildcard macro, call with different types
        S21_EventDispatcher.cs           // create dispatcher, drop Call/Bind/Unbind
        S22_CollapseToFunction.cs        // select 5 nodes, Ctrl+E → function call replaces them
        S23_CollapseToMacro.cs           // selection with latent → macro choice
        S24_ExpandNode.cs                // RMB function call → Expand → inlined
        S25_MultiTab.cs                  // 3 graphs open simultaneously, Ctrl+Tab switching
        S26_Comments.cs                  // create comment around selection, color cycle, resize, move with contents
        S27_NestedComments.cs            // 3 nested comments with z-order
        S28_FindInGraph.cs               // Ctrl+F, prefixes (kind:, category:, type:)
        S29_FindInAsset.cs               // Ctrl+Shift+F across multiple graphs
        S30_GoToDefinition.cs            // F12 from function-call → opens function graph
        S31_Bookmarks.cs                 // Ctrl+Shift+1..9 set, Ctrl+1..9 jump, edge markers
        S32_HotReloadConflict.cs         // dirty + external reload → blocking toast with Save/Discard/Ignore
        S33_BigGraph.cs                  // 500 nodes, perf budget validation
```

Each scenario is ~30–80 LOC.

## Per-scenario detail

### S15 — Variables: Get/Set drag

**What the user does:** in My Blueprint panel, click `+` next to
"Variables". Picker opens for variable type. User picks `float`, types
name "Health", confirms. Variable appears under Variables section. User
drags it onto canvas. Get/Set popup appears at drop position; user picks
Get; a `Get Health` node is placed.

**Pre-built state:** empty graph; My Blueprint already has 2 example
variables (`Health: float = 100`, `Position: Vector3`) so the user can
also drag existing ones.

**Verifies:** variable creation form, drag-from-panel, Get/Set popup,
typed accent dot in panel matches wire color.

### S16 — Promote to Variable

**Pre-built state:** A node `Multiply` with two unconnected float input
pins, default values 5 and 7.

**What user does:** RMB on the `A` input pin → "Promote to Variable…" →
form opens prefilled (name=`A`, type=float, scope=Member). User edits
name to `Multiplier`, confirms. Variable `Multiplier: float = 5` appears
in My Blueprint; pin is now wired from a `Get Multiplier` node placed
beside Multiply.

**Verifies:** promote command, picker as form, batch (AddVariable + AddNode
+ AddLink), single undo unit.

### S17 — Custom Event

**Pre-built state:** empty graph.

**What user does:** `+` next to "Custom Events" → form. User enters name
`OnEnemyKilled`, adds two parameters (`EnemyId: int`, `Killer: Entity`).
Confirm. New tab opens with red-headed `OnEnemyKilled` Entry node. User
goes back to EventGraph tab via dropdown or Ctrl+Tab. From My Blueprint,
drags `OnEnemyKilled` onto canvas → call node placed.

**Then:** select Entry node, in Details panel adds a third parameter
`Reward: int`. Call node updates immediately: third input pin appears.

**Verifies:** custom event creation form, multi-tab implicit, parameter
propagation to call sites, Details panel for FunctionEntry target.

### S18 — Function Authoring

**Pre-built state:** EventGraph tab. My Blueprint has a function
`ComputeDamage(base: float, mult: float) → float` defined.

**What user does:**
1. Double-click `ComputeDamage` in My Blueprint → navigates into function
   graph.
2. Sees Entry (left) and Return (right) nodes, both non-deletable.
3. Drags a `Multiply` node from picker, wires `Base * Mult → Multiply.A,
   Multiply.B → Return.Result`.
4. Hits Ctrl+S (host save mock).
5. Switches back to EventGraph; places a call node by dragging
   `ComputeDamage` from My Blueprint.

**Verifies:** function navigation, non-deletable Entry/Return, function
body editing, multi-tab.

### S19 — Multiple Return Nodes

**Pre-built state:** function `IsAlive(health: float) → bool` is open;
Entry and Return placed; mid-graph has a Branch on `health > 0`.

**What user does:**
1. RMB empty canvas → "Add Return Node" → second Return placed.
2. Wires Branch's `True` exec into existing Return (sets `Result=true`);
   Wires `False` exec into new Return (sets `Result=false`).

**Verifies:** multi-return Function topology, secondary Return creation.

### S20 — Macro with Wildcards

**Pre-built state:** macro `ForEachWithBreak<T>(Array: Array<T>, Index: int → T, …)`
defined. EventGraph tab.

**What user does:**
1. Drags `ForEachWithBreak` from My Blueprint → places call node with
   `<T>` wildcard shown as grey striped pins.
2. Wires an array of `Vector3` into `Array` pin → wildcard resolves to
   `Vector3`; call node updates header to `ForEachWithBreak <Vector3>`
   and all wildcard pins become yellow/V3.
3. Connects a second call node with array of `string`; resolves
   independently to `<string>`.

**Verifies:** wildcard rendering, per-call-site resolution.

### S21 — Event Dispatcher

**Pre-built state:** Dispatcher `OnHealthChanged(newValue: float)`
defined. EventGraph tab.

**What user does:** Drag dispatcher onto canvas; popup with Call / Bind /
Unbind / Unbind All. User picks Call; node placed. Repeats and picks Bind;
binding node placed.

**Verifies:** dispatcher payload menu, all four node kinds.

### S22 — Collapse to Function (Ctrl+E)

**Pre-built state:** A subgraph of 5 nodes computing damage: takes 3
floats in (from Get-variable nodes), outputs 1 float. Already wired.

**What user does:**
1. Marquee-selects all 5.
2. Ctrl+E.
3. Form opens; signature auto-detected (3 float inputs, 1 float output).
4. User types name `CalculateDamage`, picks category `Combat`, confirms.
5. Subgraph replaced by single call node; the 5 nodes moved into the new
   function's body graph (visible by navigating in).
6. Ctrl+Z undoes everything (single undo unit).

**Verifies:** signature detection, refactor batch, single undo step.

### S23 — Collapse to Macro

**Pre-built state:** subgraph with a `Delay` (latent) node.

**What user does:** Ctrl+E → dialog appears asking Function or Macro;
explanation says "Selection contains a latent node, so a Macro is
required." User confirms Macro; refactor proceeds.

**Verifies:** auto-detect of latent → Macro requirement.

### S24 — Expand Node

**Pre-built state:** a call node to a function `ScaleBy(value: float,
factor: float) → float` whose body is `value * factor` (2 nodes).

**What user does:** RMB call node → "Expand Node". Call node replaced
inline by the body's two nodes, external wires preserved.

**Verifies:** inverse of collapse refactor.

### S25 — Multi-Tab

**Pre-built state:** asset opened with three graphs: `EventGraph`,
`ComputeDamage` (function body), `OnEnemyKilled` (event body). Tab bar
shows all three.

**What user does:**
1. Ctrl+Tab cycles forward; Ctrl+Shift+Tab cycles backward.
2. Click each tab; viewport pan/zoom remembered per tab.
3. Middle-click tab → closes tab (function body tabs reopenable from My
   Blueprint).
4. Right-click tab → context menu (Close, Close Others, Close All to
   Right).

**Verifies:** per-tab viewport state, navigation, tab close.

### S26 — Comments

**Pre-built state:** 6 nodes loosely arranged.

**What user does:**
1. Marquee-select 3 of them. Press `C`. Comment created around selection
   (blue by default — first in palette).
2. With nothing selected, `C` does nothing (or RMB empty → Add Comment
   places empty comment at cursor).
3. Drag comment header — contained nodes move with it.
4. Hold Shift, drag — comment moves alone.
5. Hold Alt, drag — contents move, comment stays.
6. Double-click header → rename inline. Type "Boss Phase 2".
7. RMB header → Color → Yellow. Comment retints.
8. Drag corner handle → resize.

**Verifies:** comment creation, move-with-contents, modifiers, rename,
recolor, resize.

### S27 — Nested Comments

**Pre-built state:** 3 comments, each enclosing some nodes; comment B
fully inside comment A; comment C overlaps A and B partially.

**What user does:** click bodies — pass-through to underlying nodes
works. Click each header — selects respective comment. Send-to-back /
bring-to-front via right-click affects which header wins at overlap.

**Verifies:** body click-through, z-order rules, header overlap
arbitration.

### S28 — Find in Graph

**Pre-built state:** EventGraph with ~40 nodes including 5 `Multiply`
nodes, 3 errors, 2 breakpoints.

**What user does:**
1. Ctrl+F → find bar appears.
2. Types `multiply` → 5 nodes outlined yellow, others dimmed.
3. F3 cycles through matches; canvas centers on active match.
4. Clears, types `error:` → 3 error nodes outlined.
5. Types `kind:branch` → branch nodes filtered.
6. Esc clears bar text; Esc again closes bar.

**Verifies:** live filtering, prefixes, F3 cycling, Esc behavior.

### S29 — Find in Asset

**Pre-built state:** asset has 4 graphs each with `Multiply` nodes
distributed.

**What user does:** Ctrl+Shift+F → side panel opens. Types `multiply` →
12 matches grouped by graph. Click result in graph 3 → opens graph 3,
frames the node.

**Verifies:** asset-scope search, grouping, navigation jump.

### S30 — Go to Definition

**Pre-built state:** EventGraph has a call node to function
`ComputeDamage` and references variable `Health`.

**What user does:**
1. Select call node, press F12 → opens `ComputeDamage` graph tab,
   focuses Entry.
2. Back to EventGraph (Ctrl+Tab). Select a `Get Health` node, press F12
   → My Blueprint panel scrolls to `Health` row, highlighted briefly.

**Verifies:** F12 dispatcher to function or variable.

### S31 — Bookmarks

**Pre-built state:** EventGraph with ~50 nodes spread across a wide
area; second graph `ComputeDamage` open in another tab.

**What user does:**
1. Pan to interesting region. Ctrl+Shift+1 sets bookmark in slot 1.
2. Pan far away. Ctrl+1 → camera animates back. Edge marker arrow
   visible when off-screen.
3. Switch to ComputeDamage tab. Ctrl+Shift+2 sets bookmark 2.
4. Ctrl+1 from inside ComputeDamage → opens EventGraph tab and jumps.
5. Set bookmark 3 to slot 3, then attempt Ctrl+Shift+3 again → prompt
   to overwrite.

**Verifies:** set/jump, cross-tab jump, edge markers, overwrite prompt.

### S32 — Hot-Reload Conflict

**Pre-built state:** small graph with one node renamed (= dirty).

**What user does:** clicks "Simulate External Modify" button. Because
local state is dirty, instead of applying the change, a blocking toast
appears: "External changes detected. Save or discard your changes to
reload." with buttons.

**Verifies:** conflict detection, blocking toast with Save / Discard /
Ignore actions.

### S33 — Big Graph (performance)

**Pre-built state:** generated graph with 500 nodes, ~800 wires, 30
comments, randomly arranged in a ~10000×10000 canvas region.

**What user does:** pans / zooms freely. FPS counter (top-right toast)
should stay ≥ 60. Low-zoom mode kicks in below 0.5×.

**Verifies:** spatial index + virtualization perf; FPS budget from
spec §27.

```csharp
// helper to build the big graph
public override void Build(GraphView view)
{
    var rng = new Random(seed: 42);
    var kinds = new[] { "Math.Multiply", "Math.Add", "Flow.Branch", "Util.Print" };
    var nodes = new List<NodeId>(500);

    for (int i = 0; i < 500; i++)
    {
        var pos = new Vector2(rng.Next(0, 10000), rng.Next(0, 10000));
        var id = AddNode(view, kinds[rng.Next(kinds.Length)], pos);
        nodes.Add(id);
    }

    for (int i = 0; i < 800; i++)
    {
        var a = nodes[rng.Next(nodes.Count)];
        var b = nodes[rng.Next(nodes.Count)];
        if (a == b) continue;
        TryAddCompatibleLink(view, a, b);   // skip on validation failure
    }

    // Spread comments
    for (int i = 0; i < 30; i++)
    {
        var pos = new Vector2(rng.Next(0, 9500), rng.Next(0, 9500));
        AddComment(view, $"Region {i}", pos, new Vector2(500, 400));
    }
}
```

## Demo chrome additions

Add to `DemoShell`:
- A small "FPS: NNN" badge in the toolbar (or status bar) so perf
  scenarios are observable.
- "Save (mock)" and "Compile (mock)" buttons wired to `editor.save` and
  `editor.compile` commands (they emit a toast; no real I/O).
- A "Make Dirty" button next to scenario picker that lets the user
  artificially set the dirty flag without doing real edits, for S32.

## Acceptance

- All 19 new scenarios (S15–S33) selectable from the scenario dropdown.
- Each scenario's `Description` field explains what the user should try.
- Function/macro/custom-event/dispatcher/variable authoring all
  exercised through host's fake `IMyBlueprintModel` + `FakeCommandSink`
  command handling.
- Multi-tab works: at least one scenario (S25) opens 3 graphs and lets
  user Ctrl+Tab through.
- Find bar functional across single-graph and asset scopes.
- Bookmarks set/jump/cross-tab work.
- Big-graph scene runs ≥ 60 FPS.

## Notes on host responsibilities

Several of these scenarios require the fake host to extend beyond its
T-20 implementation. Specifically:

- **FakeMyBlueprintModel:** needs methods to add/remove/rename
  variables, functions, macros, events, dispatchers.
- **FakeCommandSink:** must handle `PromoteToVariable`,
  `CollapseToFunction`, `CollapseToMacro`, `ExpandNode`. For the demo,
  these implementations are simplified — they perform the structural
  rewrite but skip full Blueprint semantics.
- **FakeGraphContainer:** the host needs a multi-graph asset abstraction
  so the demo can track multiple `IGraphModel` instances and route
  Ctrl+Tab between them.

These extensions land in this task alongside the scenarios.

## Estimated Size
~1200 LOC across 19 scenarios + ~400 LOC of fake-host extensions = ~1600 LOC total.

## Status
Pending.
