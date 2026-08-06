# Architect question #23 — Creating graphs, and switching the canvas between them

> **Scope.** Blueprint editor only. Tracker item **BP-24** (`RW-M`) —
> *"No Function-graph create path; canvas is locked to one graph."*
> Detail: `Blueprint_Issues_Detail.md#BP-24`.
> This is not a mirror-an-existing-pattern fix — it changes what a *document* means in the editor —
> so per the engine-rules gate it gets an architect pass before any code.

**Symptom.** A blueprint asset holds a **list** of graphs. The editor binds the canvas to exactly one
of them at open time and never appends to the list. So:

- Author-defined **Function graphs** cannot be created, only hand-written in JSON.
- A **custom event's body** cannot be created at all (it must be an `Event` graph of the same name),
  which is why BP-12c shipped only the declaration half and why calling one is a **BP1407** error.
- In any multi-graph asset, **every graph but the first is invisible** — `DeepNestedBlueprint.bp.json`
  ships three Function graphs, and the editor can reach one.

---

## Ground truth (verified against code 2026-08-06)

### What already works

| Fact | Evidence |
|---|---|
| The data model supports multiple graphs of mixed kinds | `BlueprintAsset.Graphs`, `GraphKind { Event, Function, Construction }` |
| Function graphs compile, are called, and return values | `FunctionCallNode.TargetGraphId` → `IrOp_GraphCall(guid, args, retType)`; `InstanceEmitter.EmitInstanceFunctionMethod` |
| Graph signature CRUD is real and wired | `Windows/GraphSignatureWindow.cs` — Add/Remove/Rename/Retype/Move on `Graph.Inputs`/`Outputs` |
| Multi-graph assets exist on disk and compile | `DeepNestedBlueprint.bp.json` (3 Function graphs), `CustomEventSubscriberDemo.bp.json` (Function + Event) |
| A command id is already declared for the gesture | `CommandCatalog.GoToGraph = "editor.go-to-graph"` — **zero handlers**, like the eight others this programme has since registered |
| The panel already has the hook | `BlueprintMyBlueprintWindow` passes `navigateToGraph: _ => { }` |

### What blocks it

| Fact | Evidence |
|---|---|
| Nothing ever appends to `Graphs` | the only `Graphs.Add` hits are compiler-internal lowering |
| The canvas graph is chosen once, at open | `BlueprintDocumentFactory.cs:132-135` — `Graphs.FirstOrDefault(g => g.Kind == Event) ?? Graphs.FirstOrDefault()` |
| **`BlueprintGraphModel._graph` is `readonly`** | `Host/BlueprintGraphModel.cs:37` |
| **`BlueprintCommandSink._graph` is `readonly`** | `Host/BlueprintCommandSink.cs:34` |
| The model's identity is derived from the graph | `BlueprintGraphModel.Id => Deterministic($"graph:{asset}:{graph.Id}")` |

> **The consequence that drives this question.** Because both are `readonly`, "switch graph" today
> means **rebuild the entire per-graph stack**: graph model → validator → sink → host services →
> `GraphView` → FindBar → `EditorCommandsImpl` → `BookmarkStore` → debug adapter. That is
> `BlueprintDocumentFactory.Build` minus the file load.

### What a rebuild silently throws away

| Per-graph state | Created in | Lost on rebuild |
|---|---|---|
| **Undo/redo history** | `GraphView.Undo` | ✅ **yes** |
| Bookmarks | `new BookmarkStore()` (§10) | ✅ yes |
| Selection, viewport pan/zoom | `GraphView` | ✅ yes |
| Breakpoint overlay binding | `BlueprintDebugToNodeEditAdapter(session, assetId, **graph.Id**)` | ✅ yes (must be rebuilt anyway) |
| `EditService.Context` | one shared `EditService`, re-pointed per document | must be re-pointed |
| Window retargets | `EditorSubsystem.cs:~2261` — My Blueprint, Details, Variables, Graph Signature | must all re-fire |

⚠ **BP-11 shipped "one undo stack" as a headline.** If switching graphs resets it, the designer's
mental model breaks in a new way: undo is now silently scoped to "the graph I am looking at", and
switching away and back loses the ability to reverse anything.

---

## Q23-A — Does the canvas rebuild, or retarget?

- **A1 — rebuild the per-graph stack on switch.** Extract the tail of `BlueprintDocumentFactory.Build`
  into `BuildForGraph(asset, graph, …)` and call it again on switch, replacing
  `AiDocument.ViewState`. *Reuse:* the factory verbatim. *Build:* re-fire the four window retargets.
  *Cost:* undo history, bookmarks, selection and viewport reset on every switch.
- **A2 — make the model and sink retargetable.** Drop `readonly` on the two `_graph` fields, add
  `Retarget(Graph)` + `RebuildAndNotify()`. `GraphView`, its `UndoStack`, the bookmark store and the
  command set all survive. *Reuse:* the existing `RebuildAndNotify` projection path. *Build:* audit
  everything that captured `graph` at construction (the debug adapter definitely did; the sink's
  `_graph` is used by ~15 methods). *Risk:* an undo entry recorded in graph A, replayed while the
  model points at graph B, mutates the wrong graph.
- **A3 — one open document per graph.** The canvas already has a document tab bar; a Function graph
  becomes its own `AiDocument`. *Reuse:* the entire multi-tab layer. *Cost:* an asset is no longer
  one document, so save/dirty/close semantics need a rule; "the asset" and "the tab" stop coinciding.

**Claude's lean: A2, with the undo stack made graph-aware rather than shared.** A1 is cheapest to
write and the worst to use — resetting undo on a navigation gesture is exactly the kind of silent
loss this programme has spent fourteen batches removing. A3 is the most honest model of the domain
but the largest behavioural change, and it makes "save the asset" ambiguous.

**Open sub-question:** if A2, should there be **one undo stack per graph** (switch swaps stacks, each
graph keeps its history) or **one per asset** (entries tagged with a graph id, and undo auto-switches
the canvas to the graph the entry belongs to)? The second is what Unreal does and is far less
surprising; it also needs every command to carry a graph id, which `GraphCommand` does not have.

---

## Q23-B — What may a designer create, and where?

- **B1 — Function graphs only.** `editor.create-function` is already declared on the My Blueprint
  Functions section (unregistered). Custom-event bodies stay a JSON-authoring task.
- **B2 — Function *and* Event graphs.** The Event case is what BP-12c needs. It could be implicit:
  declaring a custom event also creates its handler graph, so the two halves cannot drift.
- **B3 — B2, plus retire blueprint-local custom events entirely.** See the note below: a custom event
  is a strictly weaker Function graph, and the one thing that would distinguish it — being raisable
  on the bus by name — is dead (**BP-70**). If Function graphs become authorable, `CustomEventDecl`
  may have no remaining purpose.

**Claude's lean: B2 now, B3 as a separate decision.** B2 closes BP-12c's open half with no new
concepts. B3 is a deprecation, and deprecations should not ride along inside a feature item.

> ⚠ **Do not auto-create the handler graph until Q23-C is answered.** BP-12c deliberately does *not*
> do this today, because of the selection rule below.

---

## Q23-C — Which graph does the canvas open on? (a live bug, not a preference)

`Graphs.FirstOrDefault(g => g.Kind == Event) ?? Graphs.FirstOrDefault()` **prefers an Event graph**.

So adding an Event graph to an asset whose main graph is a Function graph silently moves the
designer's canvas off the graph they were editing, the next time they open it. `CustomEventSubscriberDemo.bp.json`
already opens on `OnPing` rather than `Tick`.

- **C1 — first graph in the list, always.** Simple, stable, and the list order is authored.
- **C2 — persist the last-viewed graph per asset** in `EditorMetadata`, falling back to C1.
- **C3 — a declared "main" graph** on the asset.

**Claude's lean: C2 over C1.** C1 alone is a behaviour change for the two shipped multi-graph assets;
C2 makes the question disappear after the first visit. Either way **the Event-preference must go**
before any create path lands.

---

## Q23-D — How does the designer switch?

- **D1 — double-click in the My Blueprint "Graphs" section.** The hook exists
  (`navigateToGraph: _ => { }`); this is the smallest change and matches the panel's other rows.
- **D2 — a graph dropdown above the canvas.** The Slice-1 editor DD sketches exactly this
  (`Graph: [Main ▾]`).
- **D3 — a graph tab strip**, reusing the document tab bar's look.

**Claude's lean: D1 now, D2 as a follow-up.** D1 costs one delegate and makes every graph reachable,
which is the actual defect. D2/D3 are ergonomics on top and can be judged once switching exists.

---

## What this unblocks

| Item | How |
|---|---|
| **BP-12c** (shipped, half) | a custom event can finally be given a body |
| **BP-57** | per-function local variables — explicitly *"depends on BP-24"* |
| **BP-12d / BP-25** | find-references and cross-graph search need a multi-graph layer |
| `FunctionCall` target picker | today it can only select graphs hand-authored in JSON |

## Not in scope

Macros and collapse-to-function (new capability, no data model — out of scope for the whole
programme). Cross-asset graph references.

---

## Answers — **DECIDED 2026-08-06** (self-researched round; the user delegated this pass to Claude)

Package: **A2 + B2 + C2 + D1**, undo sub-question resolved as **one per-asset stack with
per-entry graph context and auto-switch** (the Unreal behaviour).

| Sub-question | Decision | Reasoning, grounded in code |
|---|---|---|
| Q23-A — rebuild or retarget | **A2 — retarget in place, keeping every object identity.** | `GraphView` is a fixed aggregate of get-only references (`GraphView.cs:14-67`); if `BlueprintGraphModel` and `BlueprintCommandSink` swap their `_graph` internally, the view, its `UndoStack`, `FindBar` (`FindEngine` wraps the *model*), `EditorCommandsImpl`, `BookmarkStore` and validator all survive untouched. `Model.Id` is *derived* from `_graph.Id` (`BlueprintGraphModel.cs:117`), so it changes automatically on retarget — which is exactly what the bookmark filter (`BookmarkEdgeMarkerRenderer.cs:33`) and jump logic (`BookmarkCommands.cs:59`) want. A1 resets undo on a navigation gesture; A3 breaks `AiDocument`'s one-per-asset contract (dirty tracking, `ReconcileAsset`, hot-reload stitching, the save delegate all key off it). |
| Q23-A sub — undo scoping | **One stack per asset; entries tagged with a graph context; undo/redo auto-switches the canvas.** | `UndoStack` entries carry no graph id and feed one fixed sink (`UndoStack.cs:56-99`) — so a raw shared stack would replay a graph-A entry into graph B. But per-graph stacks strand *asset-level* entries (`BlueprintEditCommand` closures: variable renames, custom-event edits) in whichever graph was active when they were recorded — undoing a rename would require switching back first. Tagging fixes both: add two optional hooks to `UndoStack` — a `ContextProvider` sampled at `ApplyAndRecord` time, and an `EnsureContext` invoked before applying when the entry's context differs from the current one. **Zero changes at any of the ~dozens of `view.Execute` call sites**; asset-level edits get a null context and never force a switch. |
| Q23-B — what may be created | **B2 — Function and Event graphs; declaring a custom event auto-creates its body graph in the same undo entry.** | Shipped assets give the exact recipe for a minimal valid graph: even *Function* graphs start with an `EventEntryNode` whose `EventTypeId` is `""` as the explicit entry indicator (`CustomEventSubscriberDemo`'s `Tick`; `Stage2_Validate.FindEntryNode:324-334` documents this). A custom-event body is `Kind: Event`, `Name` = decl name, `Inputs` mirroring the decl's parameters, same empty-`EventTypeId` entry node. Auto-create becomes safe only because C kills the Event-preference (an added Event graph no longer steals the canvas). BP1407 then only fires on hand-authored JSON. **B3 (retiring `CustomEventDecl`) stays a separate deprecation decision — BP-70's one-liner stays parked.** |
| Q23-C — which graph opens | **C2 — persist last-viewed per asset; fall back to first-in-list (C1); the Event-preference is deleted.** | Session-level persistence is free under A2: `AiDocument.ViewState` already survives document switches, and the retargeted context simply stays on whatever graph it points at. Cross-restart persistence goes in `BlueprintEditorPreferences` (an existing JSON prefs file with `Save`/`Load`, `BlueprintEditorPreferences.cs`) as an AssetId→GraphId map. **Never** in the `.bp.json` — writing navigation state would dirty the asset on a click. (Note: `Graph.EditorMetadata` already has unwired `ViewportX/Y/Zoom` slots; per-graph viewport restore may use them later, written only at save time.) |
| Q23-D — switch gesture | **D1 — double-click in My Blueprint's Graphs section; D2 (dropdown) later.** | The plumbing is three-quarters built and dead at both ends: `MyBlueprintPanel` takes `Action<GraphId> navigateToGraph` and *documents* "called on double-click of a graph entry" — but never invokes it (verified: the field has no call site); the host passes `_ => { }` (`BlueprintMyBlueprintWindow.cs:149`); `CommandCatalog.GoToGraph` has zero handlers. Wiring one real handler also lights up cross-graph bookmark jumps for free — `BookmarkCommands.cs:59-60` already calls `navigate(b.TargetGraph)` when the bookmark belongs to another graph. |

### The complete retarget audit (everything that captures the graph at Build time)

Five capture sites, all in `BlueprintDocumentFactory.Build` — this is the whole A2 surface:

| Capture | Fix on switch |
|---|---|
| `BlueprintGraphModel._graph` (readonly, line 37) | `Retarget(Graph)` + `RebuildAndNotify()` |
| `BlueprintCommandSink._graph` (readonly, line 34; 22 uses, all reads) | `Retarget(Graph)` |
| `BlueprintDebugToNodeEditAdapter(session, assetId, graph.Id)` (§6) | recreate the adapter + `SetDebugSession` again (cheap) |
| ToggleBreakpoint `isEnabled` closure captures `graph.Id` (line 285) | read through a `currentGraph` provider |
| `RegisterClipboardCommands(commands, view, graph, …)` (line 311) — paste inserts into the captured graph | take a `Func<Graph>` instead |

Retarget order: save viewport+selection for the outgoing graph (per-graph cache on
`AiCanvasContext`) → model.Retarget → sink.Retarget → rebuild debug adapter → restore or default
viewport/selection for the incoming graph → `RebuildAndNotify`. The window fan-out at
`EditorSubsystem.cs:2250-2285` is asset-scoped and needs no re-fire — the asset did not change.

### Scope guards

- **Graph rename/delete stay out of BP-24.** The panel already marks graph rows
  `IsRenamable: false, IsDeletable: false`; Function-graph rename is safe by construction
  (`FunctionCallNode.TargetGraphId` is a GUID) and can come later; Event-graph rename is already
  owned by BP-12b's custom-event rename. Delete needs a dangling-`FunctionCall` story first.
- **Construction graphs are not offered in the create menu** — nothing in the runtime consumes
  `GraphKind.Construction` yet.
