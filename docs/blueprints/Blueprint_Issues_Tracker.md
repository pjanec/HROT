# Blueprint Subsystem — Issue Tracker

> Checklist view. Full detail for every ID: **[Blueprint_Issues_Detail.md](Blueprint_Issues_Detail.md)**
> (IDs are searchable there). Grouped by **area**, sorted **cheapest-first** within each area.
> Nothing here is prioritised yet — priorities are the reader's call.

**Complexity:** `WIRING` = call existing code, no new logic · `RW-L` = real work, low (≲150 lines) ·
`RW-M` = real work, medium (new component / some design) · `RW-H` = real work, high (new subsystem or
architect decision first).
🔴 = correctness/data-loss issue, not an enhancement.

| Complexity | Open | Done |
|---|---:|---:|
| `WIRING` | 4 | 21 |
| `RW-L` | 10 | 15 |
| `RW-M` | 20 | 4 |
| `RW-H` | 2 | — |
| **Total** | **36** | **40** |
| *(refuted on verification)* | | *1* |

> 📌 **Resuming this programme?** Start with [Blueprint_Gaps_Programme_RESUME.md](Blueprint_Gaps_Programme_RESUME.md) — branch, batches
> shipped, next items, and the traps that cost real time. This tracker stays the source of truth.

> 🔁 **Systemic pattern — now three confirmed instances. Check for it before trusting any validator
> or guard in this repo.**
> *An optional constructor dependency defaults to an inert value; the tests pass it explicitly and
> prove the logic; every production site omits it, so the feature is silently dead.*
> 1. **BP-29** — `PredicateCompiler`'s `blueprintRegistry` → conditional breakpoints never fired. **Fixed.**
> 2. **BP-61** — `HsmValidator`'s `isStatefulSubtree` + `sharedScopeKeys` → both HSM concurrency rules
>    never fire. **Open.**
> 3. **BP-30/BP-31** — the same blind spot is *why* BP-31 was mis-scoped: it credited HSM with a guard
>    that has never actually run.
> A green test suite is not evidence that a guard is wired. Grep the production construction sites.

> ✅ **Batch 16 shipped (2026-08-06) — BP-71 + BP-72: closing out BP-24.** Both were gaps BP-24
> *exposed* rather than caused, found in its post-ship audit.
> **BP-71** 🔴 ([Q24](Architect_Question_24_Function_Return_Value_Wiring.md) **A1+B1+C3**, decided by
> the user): the `Return` node's value pin was `Direction=="Out"` in both projections yet resolved as
> an **input** by `Stage5.BuildReturnTerminator` — and the canvas rejects same-direction links, so a
> Function graph could **never return a value**. Both projections now emit `"In"`; the terminator
> accepts **either** direction so hand-authored legacy JSON keeps compiling (BATCH03A's fixture uses
> the old form and stayed green untouched). New **BP1655** makes an unwired return a Stage 2 error and
> Stage 5 falls back to a declared `default(T)`, so the emitter can no longer produce
> `return __t7;` with no `var __t7` — **CS0103 with no BP diagnostic**, BP-69's shape. New **BP1656**
> gates `Outputs.Count > 1` with wording that says ***"not supported yet — see BP-73"***, never
> "illegal" — proper Unreal-style N-output is **wanted** and scheduled as **BP-73**.
> **BP-72**: `AiCanvasContext` gained a `Func<Guid>? CurrentGraphId` (a plain delegate, for the same
> reason `AssetRef` is an `object`), so the **graph**-scoped Graph Signature window follows a BP-24
> switch instead of sitting on `functionGraphs[0]`; and it now lists **Event** graphs, so a custom
> event's auto-created body graph finally has its parameters editable somewhere. Event `Inputs`
> mirror into the paired `CustomEventDecl.Parameters` (ids preserved by name) or BP1408 fires.
> ⚠ **Outputs are hidden for Event graphs** rather than shown-and-ignored — a custom event returns
> nothing, and an editable list the compiler discards is the very shape BP-71 just removed.
> ⚠ **The lesson, now trap #9:** both halves of BP-71's contract were individually **test-locked**
> and the feature was still unusable, because **no test performed the designer's gesture**. Both
> items ship with a deliberate seam-crossing test — the link validator *accepting* the wire, and a
> **real** `BlueprintGraphSwitcher` moving the signature window.
> 23 new tests; all eight gates green (blueprints **2835/0**, 10 skipped).

> ✅ **Batch 15 shipped (2026-08-06) — BP-24: graph create + canvas graph switching.** Architect
> package **Q23 A2+B2+C2+D1** (self-researched round, recorded in
> [Architect_Question_23](Architect_Question_23_Graph_Create_And_Switching.md)).
> **A2:** a switch retargets `BlueprintGraphModel` + `BlueprintCommandSink` **in place** — the
> `GraphView`, its undo stack, FindBar, commands and bookmarks all survive. Undo is **one per-asset
> stack whose entries carry a graph context**; undo/redo auto-switches the canvas to the entry's
> graph before replaying (`UndoStack.ContextProvider`/`ContextRestorer` — without this, an entry
> recorded in graph A would replay into whatever graph the sink pointed at). Per-graph
> viewport+selection save/restore; the camera also persists via the previously-unwired
> `Graph.EditorMetadata` slots. **B2:** "Functions +" creates a Function graph (entry-indicator
> `EventEntryNode`, the shipped-asset shape); **declaring a custom event now auto-creates its body
> graph** in the same undo entry — the BP1407 loop closes for editor-created events. **C2:** the
> Event-graph open preference is dead (it moved the canvas whenever an asset gained an Event graph);
> open = last-viewed (session memory) → first-in-authored-order. **D1:** double-click any graph in
> My Blueprint (Graphs/Functions sections split Unreal-style; custom events navigate to their body);
> `editor.go-to-graph` gained its first handler; **cross-graph bookmark jumps lit up for free** —
> `BookmarkCommands` had the full design waiting behind a no-op delegate.
> ⚠ **Found & fixed while building: BP-12b's rename-undo desync** — renaming a custom event also
> renames its body graph and rewrites name-keyed call refs, but the undo snapshots only restored the
> declaration lists, leaving the pairing broken (a silent BP1407). A naming snapshot now restores
> all three together. ⚠ The five build-time `graph` capture sites (sink, model, debug adapter,
> ToggleBreakpoint closure, clipboard commands) are all provider-based now — a captured graph goes
> stale on the first switch; the clipboard one would have **pasted into the wrong graph**.
>
> 🔎 **Post-ship audit (2026-08-06) — two gaps BP-24 exposed rather than caused.** The
> *create → author → call with arguments* loop verifies end-to-end in code
> (`FunctionCallNodeDrawer.DrawFunctionGraphPicker` lists the asset's Function graphs;
> `NodePinSchema.FunctionGraphCallPins` projects typed argument pins from `target.Inputs`). What does
> **not** work: **BP-71** 🔴 — the callee cannot *return* a value, because the `Return` node's value
> pin faces the wrong way on the canvas ([Q24](Architect_Question_24_Function_Return_Value_Wiring.md));
> and **BP-72** — the Graph Signature window does not follow the switched canvas and hides Event
> graphs, so a custom event's parameters cannot be edited after creation.

> ✅ **Batch 14 shipped (2026-08-05) — BP-19 + BP-20: minimap and jump-to-issue.**
> Two more declared-but-never-registered command ids. The minimap is a corner overlay with a
> viewport rectangle and click/drag-to-recentre; **uniform fit, not stretched**, over the graph
> bounds *unioned with the visible rect*, so the viewport box stays inside it when you pan off the
> graph. Jump-to-issue is **F8 / Shift+F8**, visits **errors before warnings**, and **anchors on the
> selection** rather than a stored cursor — so it stays correct after a click elsewhere, and after a
> fix removes a node from the list.

> ✅ **Batch 13 shipped (2026-08-05) — BP-17 + BP-18: node titles and body collapse.**
> Both were the same shape: the canvas already honoured the feature (`NodeRenderer` draws a subtitle
> and a collapsed node), but `BlueprintNodeModel` hardcoded `Subtitle => null` / `IsCollapsed =>
> false` and nothing could change either. `SetNodeCollapsed` even existed as a command — **the sink
> had no case, so it hit the `default:` that returns success**, the third instance of that trap
> after BP-60 and BP-68.
> ⚠ A renamed node keeps its **generated title as the subtitle**, and **blank clears the override**
> rather than storing an empty header. Both new `NodeMetadata` fields are omitted from JSON at their
> defaults, so existing assets round-trip byte-identically.

> ✅ **Batch 12 shipped (2026-08-05) — BP-13: align / distribute / straighten.**
> Nine declared command ids, zero implementations, all of them a batch move — so all nine reduce to
> `CommandBuilder.MoveNodes` and each is one undo entry for free. Registered in NodeEdit itself: no
> asset knowledge is involved, unlike BP-23a/BP-60.
> ⚠ Alignment works on node **bounds**, not origins (right/centre by the top-left corner leaves
> different-width nodes ragged); distribute equalises **edge** gaps and holds the extremes still, so
> it is idempotent; straighten **anchors on the first selected node** rather than averaging, so the
> designer keeps control and a second invocation changes nothing. An alignment that would move
> nothing records nothing — it must not cost a Ctrl+Z.

> ✅ **Batch 11 shipped (2026-08-05) — BP-12b: My Blueprint items can be renamed, duplicated, deleted.**
> The context menu has always invoked all three; nothing ever registered them. Works for variables
> and custom events, undoable on the same stack as everything else.
> ⚠ **Renaming a custom event also renames its paired `Event` graph and rewrites name-keyed
> `CallCustomEvent` references** — the compiler emits `Event_{Name}` from the *graph*, and Stage5
> accepts a bare name, so renaming the declaration alone would be a silent BP1407/BP1403.
> ⚠ **Deleting a declaration leaves its nodes in place.** Dangling is recoverable and the compiler
> names it; silently deleting a designer's wired-up nodes is not.

> ✅ **Batch 10 shipped (2026-08-05) — BP-23a: copy / cut / paste / duplicate.**
> Registered host-side for the same reason as BP-60: a clipboard entry is a list of asset `Node`s.
> `Node` is already `[JsonPolymorphic]`, so the round-trip is free; paste re-mints node **and pin**
> GUIDs, remaps the links whose *both* ends were copied, and rejects foreign clipboard text.
> ⚠ **Paste ships fully-built nodes through `BlueprintEditCommand`**, never `AddNode` — that path
> re-applies only `ApplyInitialProperties`' 8-of-50 whitelist and would have silently stripped the
> configuration of the other 42 kinds. A test pins exactly that.
> Duplicate deliberately does **not** touch the clipboard. Paste leaves its nodes selected.

> ✅ **Batch 9 shipped (2026-08-05) — BP-60: "Promote to Variable" works.**
> Fixed as a **host command**, not a sink case. `GraphCommand.PromoteToVariable` is one opaque
> command whose new-node id the sink allocates internally, so no caller could write its inverse —
> that is precisely why BP-02 left this site on `Commands.Apply` and why it reached the
> `default:` arm that returns success. Promotion is not one primitive anyway: it is *declare a
> variable* + *place a node* + *link it*. Composing it at the host from commands the sink already
> implements keeps BP-11's invariant (**the sink applies, the stack records**) and makes the whole
> gesture **one undo entry**, because the caller owns every id in it.
> **This lifts BP-02's 15th and last undo bypass.** ⚠ Every test asserts the *effect*; a test on
> `Success` would have passed against the bug.

> ✅ **Batch 8 shipped (2026-08-05) — custom-event authoring: BP-12c + BP-68.**
> "Custom Events +" opens a create modal (name + typed parameters), mirroring `editor.create-variable`
> exactly. This **unblocks BP-07**, whose picker was correct but had nothing it could ever list.
> ⚠ **The dispatcher half was retired, not wired** — the audit's own suggestion; BP-09 had already
> deleted the concept's node kinds and nothing consumes `EventDispatchers`.
> ⚠ **Found while fixing: a declaration is only half a custom event.** The body is an `Event` graph of
> the same name, which the editor cannot create (**BP-24**) — so calling an unhandled event emitted C#
> that did not compile, blaming a method the designer never wrote. New **BP1407/BP1408** make that a
> Stage 2 error naming the graph to add. They fire at **call sites only**, so the new create button
> never produces a broken asset on its own.
> ⚠ **BP-68, found in this batch's own visual check:** the moment an asset *had* a custom event, the
> next gesture — dragging it onto the canvas — exposed that the sink binds nothing for asset-scoped
> dynamic kinds. Fixed for all three of them.

> ✅ **Batch 3 shipped (2026-08-04) — the palette batch: BP-04 + BP-09.**
> **BP-04:** 14 baked entries (6 `Compare` · 5 `BinaryOp` · 2 `BooleanOp` · 1 `Not`) — the four kinds
> were fully lowered but had no palette row, so they were reachable only from hand-authored JSON.
> No drawer needed: `Stage0_Rehydrate` reconstructs `A`/`B`/`Result` for a pin-less instance.
> **BP-09:** the 6 abandoned kinds are gone from the picker — **plus `ArrayMake`/`ArrayGet`**, which
> BP-16 turned into a hard BP1420 error, so offering them would let a designer place a node that
> guarantees a broken build. Node classes retained so old assets still deserialize, then fail loudly.
> A round-out test asserts **every** operator enum value has a palette row, so a newly added operator
> cannot silently become unreachable — the exact shape of the original BP-04 defect.

> ✅ **Batch 2 shipped (2026-08-04) — BP-02 + all four documentation-accuracy items.**
> **BP-02:** 13 of the 15 undo-bypass sites now route through `view.Execute` with a real inverse
> (the 14th was BP-59; the 15th is blocked — see BP-60). NodeEdit core 195 + UI 90 green.
> **BP-47/48/49/50:** the Overview's node-status marks no longer conflate the compiler and authoring
> axes (new **⛔** mark for compiler-rejected kinds); Runtime DD **§9.6** — *the audit cited §13.5,
> which was wrong* — and Overview §1 now carry the BTree-vs-HSM working-state correction; the
> cross-entity `BlueprintDeferredEvent` example is fenced as **NOT IMPLEMENTED**; the v1.1 roadmap
> is banner-marked **HISTORICAL**.
> ⚠ **New issue found while fixing BP-02: BP-60** — "Promote to Variable" silently does nothing in
> the Blueprint editor.

> ✅ **Batch 1 shipped (2026-08-04) — the silent-failure batch: BP-59, BP-29, BP-16, BP-15, BP-12e.**
> Common theme: a failure that was invisible is now loud. Verified headless — full solution builds
> clean; blueprint suite **2551 passed / 0 failed**, breakpoints 130, NodeEdit core 195 + UI 90.
> Two things the test suite corrected while building it, both recorded in the detail file:
> decision-asset ids are **not** parseable GUIDs (`CombatPostureDecision` ships
> `3c6f9e42-…-posture0000001`), and custom events resolve **by name as well as GUID**.
> The new BP-15 validator also caught a real shipped defect — an inert `CallCustomEvent`
> placeholder in `EnumDemo.bp.json`, now removed.

> ✅ **Second verification pass (2026-08-04) — all 33 spot-checked (`✔`) claims re-derived from code.**
> **27 confirmed and upgraded to `✔✔`**; 6 documentation-accuracy items left at `✔` (their file/section
> citations are loose, though the underlying claims hold). **No claim was refuted.** Two corrections and
> one new issue:
> - **BP-59 (new, 🔴)** — context-menu node delete bypasses undo while the Del key doesn't.
> - **BP-02 scope** — 15 undo-bypass sites, not 10.
> - **BP-27 ⚠ resolved** — re-check confirms `RW-M`; no reusable picker exists.

> ✅ **Verification pass complete (2026-08-04).** All 11 previously agent-only claims were re-checked
> against the **whole repo** (`FDP/` *and* `Hrot/`). Outcome: **6 confirmed**, **1 refuted**
> (BP-46 — already shipped), **2 re-classified** (BP-37 harder, BP-55 easier), **2 downgraded to
> UNCLEAR** (BP-53/BP-54 — partially refuted, and peripheral to blueprint editing).
> Every remaining row is now hand-verified (**✔✔**) or spot-checked (**✔**).

---

## Area A — Graph editor UX
*Canvas ergonomics. Mostly NodeEdit-core capability the Blueprint host never registers.*
→ [detail](Blueprint_Issues_Detail.md#area-a--graph-editor-ux)

- [x] **BP-59** 🔴 · `WIRING` — **Context-menu "Delete Node" is not undoable, but the Del key is.** `CanvasRenderer.cs:758` applies `RemoveNodes` raw; `EditCommands.cs` builds a proper inverse for the same intent. Silent unrecoverable data loss — *found in the verification pass*
- [x] **BP-60** 🔴 · `RW-M` — **"Promote to Variable" silently does nothing.** `PromoteToVariable` was implemented only in `NodeEditor.Demo`'s `FakeCommandSink`; `BlueprintCommandSink` has no case, so it hit the `default:` that returns success and no-ops. ⚠ **fixed as a host command, not a sink case** — the single opaque command hides the new node's id, so no caller could ever write its inverse (which is why BP-02 left this site on `Commands.Apply`). Promotion is *declare + place + link*; composing it from primitives the sink already implements keeps BP-11's invariant and makes the gesture one undo entry. **Lifts BP-02's 15th and last bypass** — *found while fixing BP-02*
- [x] **BP-62** ⚠ · `RW-M` — **Component type resolution depends on assembly load order.** `ComponentFieldReflector.ResolveType` scans only `AppDomain.CurrentDomain.GetAssemblies()`, which excludes not-yet-loaded assemblies, and callers read `null` as *"not a writable component"* rather than *"unknown"* — so the collection-write bake silently no-ops. Masked in the editor only by a startup side effect (`LoadFromAiAssembly`). *Root cause of the order-dependent test; found by chasing it down*
- [x] **BP-02** · `WIRING` — Undo bypassed via `view.Commands.Apply`. ⚠ *scope corrected:* **15 sites, not 10** — also pin "Reset to Default" (`:638`), comment delete (`:845`), "Promote to Variable" (`:970`)
- [x] **BP-03** · `WIRING` — Bookmarks can't be renamed or deleted; `BookmarkStore.Remove` already exists. Added `Rename` + inline edit, delete button/menu, and click-to-jump; ordering/labelling split into `BookmarkPanelLogic` so they're headlessly testable
- [x] **BP-65** 🔴 · `WIRING` — **Placing a node was silently non-undoable.** `BlueprintCommandSink` ignored `GraphCommand.AddNode.AssignedId` and minted its own Guid, so `CommandBuilder`'s paired `RemoveNodes([thatId])` inverse named a node that doesn't exist — palette drops, wire-drops, variable drags, all of it. The **BTree and HSM sinks already honour it**; only this one didn't. Masked until BP-11 by the sink's parallel `CommandHistory` record, which holds the node *object* — *found while testing BP-12a*
- [x] **BP-68** 🔴 · `WIRING` — **Dragging a custom event out of My Blueprint produced an unbound node.** `BlueprintCommandSink.CreateAssetNode` mapped every kind missing from `NodeKindRegistry` to a generic `FunctionCallNode` — its own comment said *"Dynamic kind (custom event, callable peer)"*. Three create-paths land there and **all three are asset-scoped kinds the sink is the only thing able to bind**: the drag drop (`Event.CallCustom`) and both per-asset palette entries (`CustomEvent.{Name}`, `CallPeer.{guid}`). Result: a node with no drawer that BP-07's picker could never see, because it was not a `CallCustomEventNode` at all. Hidden because the *static* "Call Custom Event" palette entry is registry-backed and worked — *found in the BP-12c visual check*
- [x] **BP-66** 🔴 · `WIRING` — **The peer-blueprint catalog scanned a directory that does not exist.** `EditorSubsystem` built `BlueprintPeerSource` over `{BaseDirectory}/blueprints`; every other consumer uses `Assets/Blueprints` (`AssetRoots.AssetsRelative`) — *including two other sites in the same file*. So `EnumerateAll()` returned nothing and **`CallPeerBlueprint` pin projection has never resolved a peer**, silently falling back to untyped `exec + Return:System.Object`. Long-standing; surfaced by BP-08's picker reporting "no peer Blueprints discovered" — *found in the visual check*
- [x] **BP-23a** · `RW-L` — **No copy/cut/paste/duplicate on the canvas.** All four ids were declared in `CommandCatalog` with **zero** handlers repo-wide and Paste was hard-disabled. Registered host-side (like BP-60) because a clipboard entry is a list of asset `Node`s: JSON round-trip via `[JsonPolymorphic]`, fresh node **and pin** GUIDs, internal links remapped, foreign clipboard text rejected. ⚠ **paste ships fully-built nodes through `BlueprintEditCommand`** — routing it through `AddNode` would have re-applied only the 8-of-50 whitelist and silently stripped the other 42 kinds
- [x] **BP-13** · `RW-L` — No align/distribute/straighten; 9 commands declared, 0 implemented. All nine now registered in NodeEdit itself (no asset knowledge needed) via `CommandBuilder.MoveNodes`, so each is one undo entry. ⚠ **alignment uses node *bounds*, not origins** — aligning right or centring by the top-left corner leaves different-width nodes ragged; **distribute equalises edge gaps** and holds the extremes still, so it is idempotent; **straighten anchors on the first selected node** rather than averaging. An Align submenu on the node context menu
- [x] **BP-17** · `RW-L` — No node renaming/custom titles; `Subtitle => null` always. New `NodeMetadata.CustomTitle` + a `"Title"` `SetNodeProperty` key + Rename (F2) on the node menu. ⚠ **the generated title becomes the subtitle** — a renamed node must not lose the only indication of what it is — and **blank clears the override** rather than storing an empty header, so a node can always be put back without undo
- [x] **BP-18** · `RW-L` — Node body collapse hardcoded `false`; `SetNodeCollapsed` existed with a working reference impl but **the sink had no case, so it hit the `default:` that returns success** — the same trap as BP-60. New `NodeMetadata.Collapsed` + a sink case + a Collapse/Expand menu item
- [x] **BP-19** · `RW-L` — No minimap; `ViewportState` already supplied the transform math. Corner overlay with a viewport rectangle, click/drag to recentre, `editor.toggle-minimap` (declared, never registered) now live plus a canvas-menu toggle. ⚠ **uniform fit, not stretched**, and the mapped region is the graph bounds **unioned with the visible rect** so the viewport box stays inside it when you pan off the graph
- [x] **BP-20** · `RW-L` — No error list / jump-to-next-error; `NodeState.Error` flags and a cycle-and-centre pattern already existed. `editor.next-error`/`prev-error` registered on **F8 / Shift+F8**, selecting and centring each problem node. ⚠ **errors before warnings**, and the sequence **anchors on the selection** rather than a stored cursor, so it stays right after a click elsewhere or after a fix removes a node from the list
- [ ] **BP-56** · `RW-L` — No wire-level execution-flow highlighting (nodes glow, wires don't)
- [ ] **BP-23b** · `RW-M` — Cross-asset paste; needs variable/type re-resolution. Do after BP-23a
- [ ] **BP-25** · `RW-M` — Cross-blueprint search is cosmetic; `FindEngine` ignores its `scope` argument by its own docstring
- [ ] **BP-28** · `RW-M` — No advanced-pin hiding; needs a new persisted per-pin flag *and* an authoring UI

## Area B — Node authoring surface
*Whether a designer can place and configure each node kind. **13 of 50 kinds run but can't be configured.***
→ [detail](Blueprint_Issues_Detail.md#area-b--node-authoring-surface)

- [x] **BP-04** · `WIRING` — `Compare`/`BinaryOp`/`BooleanOp`/`Not` **cannot be placed at all** despite being lowered + compile-tested. 14 baked palette entries, no drawer needed
- [x] **BP-09** · `WIRING` — 6 abandoned node kinds are **advertised in the palette** but compile to a silent no-op. Delete 6 `Make<T>` blocks
- [x] **BP-05** · `WIRING` — `ReadRankedResult.Rank` uneditable; plain `InputInt`. Clamped to ≥ 0; the gesture coalesces to one undo entry
- [x] **BP-06** · `WIRING` — `WaitForChannel.ChannelType` uneditable; reuse `IChannelCommandCatalog`. ⚠ the catalog is keyed by *(channel, action)*, so the list needs deduplicating — otherwise a channel with 8 actions appears 8×
- [ ] **BP-69** 🔴 · `WIRING` — **A name-referenced `CallCustomEvent` silently loses its argument pins.** `EventId` accepts two forms — the declaration's GUID (what the picker writes) and a bare **Name** — and Stage2's `V_ValueNodeReferences`, Stage5's `FindCustomEventIndex` and BP-12b's rename path all honour both. But **both pin projections resolve only the GUID**: `NodePinSchema.CallCustomEventPins` and `Stage0_Rehydrate.EnrichCallCustomEventPins` each `return` early on `!Guid.TryParse`. So a name-referenced call to an event *with* parameters shows exec-only pins in the editor and emits `Event_X(ref s, view, ecb, self, time)` with **no arguments**, against a handler that declares some → **CS7036 with no BP diagnostic**. ⚠ **BP1408 does not catch it**: it compares the declaration's parameters against the handler graph's inputs, which agree — the mismatch is at the *call node's pins*. *Fix: accept the Name form in both projections (three lines each), or reject it in Stage 2.* — *found while writing up how custom events work*
- [ ] **BP-70** 🔴 · `WIRING` — **The `?? Name` fallback that would let an Event graph be raised by name never fires.** `CSharpEmitter` keys `EventHandlers` by `evtGraph.EventTypeFqn ?? evtGraph.Name`, with the comment *"fallback to name for legacy Event graphs that carry no event identity"*. But `EventTypeFqn` is copied from `EventEntryNode.EventTypeId`, which defaults to **`""`, not null** — so `??` never triggers and the key is the empty string. `BlueprintEventDispatch.ResolveTypeId("")` hashes `""` to a type-id nothing publishes, so **the handler is never dispatched from the bus** — the designed "custom carrier / FQN-hash" path is dead. Two such graphs also collide on `""`. *Fix is one line:* `string.IsNullOrEmpty(EventTypeFqn) ? Name : EventTypeFqn`. — *found while explaining what a blueprint-local custom event is for*
- [x] **BP-07** · `WIRING` — `CallCustomEvent.EventId` uneditable. ✅ **unblocked by BP-12c** — an asset can now declare custom events, so the picker has something to list. ⚠ **the audit named the wrong source:** `UnifiedEventDiscovery` enumerates *engine* events; a custom event is **asset-scoped** (`asset.CustomEvents`), so every choice from that picker would have failed to resolve
- [x] **BP-08** · `WIRING` — `CallPeerBlueprint` target uneditable; reuse `BlueprintPeerSource` behind a new `IBlueprintPeerProvider` seam. Dependent peer→function pickers; switching peer clears a function it doesn't export, in the same edit
- [ ] **BP-67** · `RW-M` — **The When node's other three mode forms are also stubs.** `ValueChanged` (component/property picker), `ConditionMet` (predicate editor) and `EqsResult` (trigger + sensor picker) each render one `TextDisabled` line, so three of the node's four modes cannot be configured at all. Unlike BP-10 these have **no already-injected catalog** to render — ValueChanged needs a component→property picker, ConditionMet a predicate editor UI — *found in the visual check*
- [x] **BP-10** · `WIRING` — `When` → EventFired form stubbed; the catalog is *already injected and called*, just never rendered. Filtered picker + self-filter toggle (shown only when the event carries a target field)
- [x] **BP-71** 🔴 · `RW-L` — **A Function graph's return value cannot be wired.** ✅ **Batch 16.** The `Return` node's value pin is declared `Direction=="Out"` by *both* projections (`NodePinSchema.ReturnNodePins:328`, `Stage0_Rehydrate.EnrichReturnPins`) but consumed as an **input** — `Stage5.BuildReturnTerminator:1896` matches `Direction=="Out"` then calls `ResolveDataPin(rn.Id, outPin.Id)`, which follows a link *arriving* at the node. `BlueprintPinModel:86` maps Direction straight through and `BlueprintLinkValidator` rejects same-direction links, so **nothing can be wired in** — and the pin gets no inline default editor either (that too is `Direction=="In"`-only). ⚠ **`Return` is the compiler's only such pin**: of ~20 `ResolveDataPin` sites it is the sole `"Out"` one, against the universal `ResolveAllDataInputs` convention. Deliberate and **test-locked** at both ends, so a green suite proves nothing — the two halves have simply never been used together. Ends in **CS0103 with no BP diagnostic** (BP-69's shape): unwired ⇒ BP4001 *warning* + a dummy temp that is never declared ⇒ `return __t7;`. Blast radius nil — of 92 shipped assets, **0** wire a function return. 📐 **[Q24](Architect_Question_24_Function_Return_Value_Wiring.md): A1+B1+C3 decided by the user 2026-08-06 — buildable now** (flip both projections to `"In"`, accept either direction in the terminator, Stage 2 error + `default(T)` on an unwired return). Q24-**D** (one output or many) is still open and does not block it — *found while auditing what BP-24 unblocked*
- [ ] **BP-73** · `RW-M` — **Function graphs support only ONE output value; Unreal supports N.** ✅ **Scheduled by decision** ([Q24-D](Architect_Question_24_Function_Return_Value_Wiring.md#q24-d--one-output-or-many), 2026-08-06) — the user wants proper N-output; until it ships, `Outputs.Count > 1` is a Stage 2 error worded ***"not supported yet — see BP-73"***, never "illegal". **Costed, not estimated:** the Library ABI is **already** N-shaped (results go into an `outputs` byte span via `MemoryMarshal.Write`, and N *inputs* already walk an `__off` cursor — mirror that loop, sequential writes not a packed struct); `_statementPinCache` already maps pin→value so a statement-produced value is never recomputed; `CSharpEmitter.Emit:69` already registers **every** `graph.Outputs` entry in the debug map; the signature window is already N-row; `TypeRefToCSharp:1591` already passes synthesized `_`-prefixed types through. ⚠ **Do not turn `IrStatement.ResultValue` into a list** — it carries the one-`PinId`-per-statement debug annotation, probe insertion and breakpoint mapping. Keep one carrier value + N field-read statements. **Genuinely new:** the Instance carrier (**`ValueTuple` vs a synthesized `_FuncOut_{Name}` struct — settle first**), one fan-out `IrOp`, and looping the three `[0]` projections. ~250–450 lines, strictly additive (`Count <= 1` emits exactly today's C#). **Depends on BP-71**
- [ ] **BP-14** · `RW-L` — `Return.Status` uneditable (always Success); a `NodeStatus` combo, ~20-30 lines
- [ ] **BP-22** · `RW-L` — `GetParameter` cannot be placed; asset-specific, so needs a picker not a baked entry
- [ ] **BP-21** · `RW-L` — `When` → ValueChanged form stubbed; reuse `ComponentFieldReflector` + component pickers
- [ ] **BP-26** · `RW-L` — `When` → ConditionMet form stubbed. *Reclassified from REAL WORK:* a full StructEdit-generic predicate editor already exists in `Fdp.Presentation`, which the Blueprint editor already references
- [ ] **BP-27** · `RW-M` — `ScoreDecision.AssetId` uneditable. ✅ *re-check done:* `UtilityDecisionDef` exists only in `Fdp.Toolkits.Tests`, and `AssetId` is a bare GUID string. StructEdit edits DTO fields but can't discover assets, so unlike BP-26 there's no picker to inherit — `RW-M` confirmed

## Area C — Editor infrastructure
*Document, undo and panel plumbing.*
→ [detail](Blueprint_Issues_Detail.md#area-c--editor-infrastructure)

- [x] **BP-12a** · `WIRING` — Get/Set from the My Blueprint menu is dead (`create-variable-get`/`-set` unregistered) — the most-used motion in Unreal authoring. ⚠ *scope corrected:* the **drag**-to-canvas path already worked (`CanvasRenderer.PlaceVariableNode`); the **menu** was the dead route
- [x] **BP-12e** · `WIRING` — Dead panel commands **fail silently**; `InvokeCreate` discards the result. Root cause of the whole BP-12 family's UX. *Tally: 14 commands invoked, 1 registered*
- [x] **BP-11** ⭐ · `RW-M` — 🔴 **No inspector/drawer edit is undoable.** Three tiers; Ctrl+Z drained a stack the drawers never wrote to. Shipped A1+B1+C2+E1 ([Q22](Architect_Question_22_Undo_Unification.md#-implementation-outcome-2026-08-04--shipped)); transport is **R3** (a Blueprint-owned `GraphCommand` subtype — R1 provably can't carry multi-field bakes, R2 would have meant editing the vendored tree). ⚠ **D2 superseded — gap 4:** the sink was double-recording, so making `CommandHistory` live would have pushed 2 entries per gesture and 3 on undo. Sinks apply; the stack records
- [x] **BP-12b** · `RW-L` — Panel items can't be renamed/duplicated/deleted; a variable can be created but never removed. Registered for variables **and** custom events, undoable via `BlueprintEditCommand`. ⚠ **renaming a custom event also renames its paired Event graph and rewrites name-keyed `CallCustomEvent` refs** — the GUID form survives untouched, but Stage5 accepts a bare name and leaving those behind would turn a rename into a silent BP1403. ⚠ deleting a declaration **leaves its nodes in place** — dangling is recoverable and named by the compiler; silently deleting wired-up nodes is not. *`move-to-category` / `change-variable-type` still unregistered*
- [x] **BP-12c** · `RW-L` — Custom events can't be created — **the blocker for BP-07**, whose picker had nothing it could ever list. "Custom Events +" now opens a create modal (name + typed parameters). ⚠ **the dispatcher half was removed, not wired** — BP-09 established dispatchers are superseded and deleted their node kinds; nothing consumes `EventDispatchers` and no shipped asset declares one. ⚠ **found while fixing: the declaration is only half a custom event** — the body is an Event graph of the same name, which the editor cannot create yet (BP-24), so a call to an unhandled event emitted C# that did not compile. New **BP1407/BP1408** turn that into a Stage 2 diagnostic
- [x] **BP-24** · `RW-M` — **No Function-graph create path; canvas locked to one graph.** ✅ Batch 15: canvas switches by retargeting the model+sink in place (undo/bookmarks/commands survive; undo auto-switches to the entry's graph); "Functions +" creates Function graphs; declaring a custom event auto-creates its body (BP1407 loop closed); Event-graph open preference removed; double-click in My Blueprint navigates; cross-graph bookmark jumps live. Decisions: [Q23](Architect_Question_23_Graph_Create_And_Switching.md)
- [x] **BP-72** · `RW-L` — **The Graph Signature window ignores the canvas, and Event-graph parameters are editable nowhere.** ✅ **Batch 16.** Its `Retarget` is asset-scoped (`EditorSubsystem.cs:2276`) and it keeps its own combo seeded at `functionGraphs[0]`, so after a BP-24 graph switch the designer edits the `Inputs`/`Outputs` of a graph they are not looking at. It also filters to **Function graphs only**, so a custom event's auto-created **Event** body graph is excluded — its parameters can be set at creation (`CustomEventCreateModal`) and never again, short of hand-editing JSON. ⚠ The halves interact: following the canvas alone would land on an Event graph and print "No Function graphs in this blueprint". ⚠ Event `Inputs` mirror `CustomEventDecl.Parameters` — editing one side must rewrite the other or BP1408 fires; that pairing is the real work, not the picker — *found while auditing what BP-24 unblocked*
- [ ] **BP-12d** · `RW-M` — `find-references` dead; overlaps BP-25's multi-graph layer
- [ ] **BP-57** · `RW-M` — Per-function local variables absent from the data model itself. ✅ **unblocked — BP-24 shipped** (functions are creatable and the canvas reaches them)

## Area D — Compiler & correctness
→ [detail](Blueprint_Issues_Detail.md#area-d--compiler--correctness)

- [x] **BP-16** · `RW-L` — 🔴 **`ArrayMake`/`ArrayGet` produce a silent wrong value** — emit `default` with *no diagnostic at all*, unlike the exec-side BP4004 path. Compiles clean, returns wrong data. A ~30-line Stage2 validator converts it to a compile error
- [x] **BP-15** · `RW-L` — 4 node kinds accept bad references silently (no Stage2 validator for `ScoreDecision`/`ReadRankedResult`/`CallCustomEvent`/`Cast`)
- [ ] **BP-32** · `RW-L` — `When` FallingEdge deferred for ValueChanged mode (live `// TODO M3`); falling-edge behaviours silently never fire
- [ ] **BP-58** · `RW-L` — `Cast` has no drawer (emit bug itself is **fixed**; July matrix is stale)
- [ ] **BP-33** · `RW-M` — `WaitForEvent` structurally broken: no `EventTypeId` satisfies both Stage2 and Roslyn. **Decide repair vs delete** — superseded by named `EventEntry` handlers

## Area E — Debug & diagnostics
*Strongest area — several capabilities **exceed** stock Unreal. Universal Breakpoints (Slice-2 D1) is **already built**: 128 unit + 25 integration tests pass.*
→ [detail](Blueprint_Issues_Detail.md#area-e--debug--diagnostics)

- [x] **BP-29** · `WIRING` — 🔴 **LIVE BUG: blueprint conditional breakpoints silently never fire.** `PredicateCompiler` gets no `blueprintRegistry` at any of 3 production sites, so the predicate compiles to constant-false. Invisible to tests because they pass the registry explicitly. 2 one-liners + 1 needing plumbing
- [ ] **BP-01** · `WIRING` — Watch panel shows raw hex bytes; `MarshalFromBytes` is complete, tested, and used at 4 other sites in the same file
- [x] **BP-35** · `RW-L` — D4 `MultiplexingProbeSink` missing; `IBlueprintProbeSink` exists, needs a composite
- [ ] **BP-37** · `RW-M` — `LifecyclePredicateDto` by `NetworkId` throws. ⚠ *raised on verification:* `INetworkEntityMap` **doesn't exist**; the concrete map lives in a network project Breakpoints doesn't reference → layering decision first
- [ ] **BP-36** · `RW-M` — D5 stack-frame inspection is Blueprint-local; lifting it would let BTree/HSM pauses carry a call stack
- [ ] **BP-38** · `RW-M` — D9 pause-on-Blueprint-exception. **Already LOCKED as deferred** by architect decision; rewind machinery is reusable
- [ ] **BP-39** · `RW-H` — D8 CLR/Visual Studio source-line debugger sync; no scaffolding present
- [ ] **BP-40** · `RW-H` — Library-dispatch graphs **structurally cannot** carry node breakpoints (probes need `self`; stateless Library functions have none). Deliberate, but a real authoring surprise. **Architect call — do not build speculatively**

## Area F — Runtime & state architecture
→ [detail](Blueprint_Issues_Detail.md#area-f--runtime--state-architecture)

- [ ] **BP-31** · `RW-L` — ⚠ **RE-SCOPED — do not build as written.** The premise is inverted: HSM\'s guard is wired to always-false in production (**BP-61**), so neither host is guarded. Mirroring it would add a second rule that can never fire. Fix BP-61 first
- [x] **BP-41** · `RW-L` — No test for two *different* AiPrimitive blueprints on one entity; coverage is by analogy only. ⚠ *the real gap was **per-slot sizing**, not key collision* — every prior test placed a single `WorkingState` **type**. HSM half stays with **BP-30** (unauthorable today)
- [ ] **BP-44** · `RW-L` — Custom Events 1d: no event-definition authoring UI
- [ ] **BP-30** · `RW-M` — 🔴 **HSM-hosted AiPrimitive blueprints collide** — they zero and re-init each other every tick, so neither retains state. BTree has the partition-slot mechanism (16 refs); HSM has **0** and no compose command
- [ ] **BP-61** 🔴 · `RW-M` — **HSM's two concurrency validators never fire in production.** `HsmValidator` defaults `isStatefulSubtree` to `_ => false` and `sharedScopeKeys` to empty; both production sites (`HsmGraphModel:43`, `HsmAssetValidator:18`) omit them, so Rules 8/8b emit nothing. Same shape as BP-29 — tests inject the lambda and prove the logic while the wiring stays dead. ⚠ No `IsStateful` notion exists editor-side, so this needs a design, not a one-liner — *found while scoping BP-31*
- [ ] **BP-45** · `RW-M` — Cross-entity event dispatch (`BlueprintDeferredEvent`) absent; the most-cited deferred capability
- [ ] **BP-42** · `RW-M` — Cross-entity shared-state **write** (read path shipped); deferred by design
- [x] ~~**BP-46** — Generic `GetShared<T>` partition-slot accessor~~ ❌ **REFUTED — already shipped.** `BlueprintSharedState.TryGetShared<T>` exists at `:58` and the compiler emits calls to it. No work required
- [ ] **BP-43** · `RW-M` — Custom Events 2b: events with no backing C# struct

## Area G — Documentation accuracy
*Cheap, and currently actively misleading.*
→ [detail](Blueprint_Issues_Detail.md#area-g--documentation-accuracy)

- [x] **BP-47** · `WIRING` — `Blueprints_Overview.md:75` marks the 4 unplaceable value-op nodes ✅, conflating compiler and authoring axes
- [x] **BP-48** · `WIRING` — Runtime DD §13.5 + Overview §1/§5 stale on AiPrimitive working state (wrong for BTree-composed nodes)
- [x] **BP-49** · `WIRING` — Authoring guide describes cross-entity routing **as if current**; it doesn't exist (BP-45)
- [x] **BP-50** · `WIRING` — Trackers contradict the code; the **v1.1 roadmap is fully superseded** — label it history, not status
- [ ] **BP-55** · `WIRING` — Asset-Browser delete affordance. ⚠ *lowered on verification:* `RefactorService.PreviewDelete` (with dangling-ref detection) already exists; every caller is a test fake, so only the UI affordance is missing
- [ ] **BP-51** · `RW-L` — DOC-3/DOC-4 illustrated SVGs (memory layout, lifetime timeline) missing
- [ ] **BP-52** · `RW-M` — UX-1…UX-5 authoring ergonomics unbuilt; UX-1/UX-2 need an architect nod first
- [ ] **BP-53** · `RW-M` ⚠ **UNCLEAR** — E6 cross-asset blueprint-action picker. *Partially refuted:* `[HsmActionPicker]` exists and is used throughout `HsmFacets.cs`; whether it spans cross-asset blueprint actions is unestablished. Peripheral to blueprint editing — re-scope before acting
- [ ] **BP-54** · `RW-M` ⚠ **UNCLEAR** — G7 resolver-authoring UX. Runtime `BehaviorRegistry.RegisterResolver` exists; "authoring UX" is too loosely defined in the source doc to verify. Peripheral — re-scope before acting
- [x] **BP-63** · `RW-L` — **NodeEdit's built-in Comment Details view is not undoable.** `CommentDetailsView.Commit` calls `_ctx.CommandSink.Apply` raw; `IDetailsContext` exposes neither the view nor the model, so there is nothing to build an inverse from (its own `Revert()` says so). Needs a widened context in the vendored tree — a BP-02-family bypass BP-02 did not reach. *Not a regression: it was already non-undoable* — *found while fixing BP-11*
- [x] **BP-64** · `WIRING` — **2 pre-existing Windows-only reds in `Hrot.Editor.AiShared.Tests`** (1202 pass): `ExportDeliveryModalTests.SaveToFile_InvalidPath` (no invalid path chars on Linux) and `AssetBaseNameCollisionGuardTests.CheckCollisionOnDisk` (asserts `C:\Trees`). Verified against a stashed tree — they predate this programme. Decide platform-gate vs. rewrite; the suite is not in the gate list, which is why it went unnoticed — *found while fixing BP-11*

---

## Out of scope

- [ ] ~~Macros~~ — absent from the entire codebase; new capability, architect round required
- [ ] ~~Collapse-to-function / collapse-to-macro~~ — absent, and nothing to collapse into until BP-24
- [ ] ~~Squad-quartet & dispatcher lowering~~ — abandoned by design; remove rather than implement (BP-09)

## Needs an architect decision before scoping

`BP-40` · `BP-38` (already LOCKED as deferred) · `BP-52` (UX-1/UX-2).

**Cleared:** **`BP-71`** — [Q24](Architect_Question_24_Function_Return_Value_Wiring.md) **all four
sub-questions decided by the user 2026-08-06** (A1+B1+C3+D1′); buildable now, single-output only.
Q24-**D** resolved as **D1′**: proper N-output is **wanted** and is now the scheduled, costed
**`BP-73`**; until it ships the `Outputs.Count > 1` diagnostic reads *"not supported yet — see
BP-73"*, never *"illegal"*.

**Cleared:** `BP-11` — approved 2026-08-04, see [Q22](Architect_Question_22_Undo_Unification.md).
`BP-27` — re-check done, no reusable picker exists, `RW-M` confirmed; no architect round needed.

## Confidence

**No unverified rows remain.** Every issue is hand-verified (**✔✔**) or spot-checked (**✔**), except
BP-53/BP-54 which are explicitly flagged **UNCLEAR**. Per-issue tags live in the detail file.

Four "nothing exists" claims were overturned across the audit — the predicate editing UI, Universal
Breakpoints, C1-for-BTree, and BP-46 — every one because a search covered `Hrot/` but not `FDP/`.
**Lesson for future work in this repo: absence claims must be checked across both trees.**
