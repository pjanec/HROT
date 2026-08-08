# Blueprint Subsystem — Issue Detail Register

> Full detail for every issue found in the 2026-08-04 audit. Companion tracker with checkboxes:
> [Blueprint_Issues_Tracker.md](Blueprint_Issues_Tracker.md). Narrative analysis:
> [Blueprint_Gaps_And_QoL_Audit.md](Blueprint_Gaps_And_QoL_Audit.md).
> **Goal of the programme:** make blueprint editing fully functional and pleasant.
> Macros and collapse-to-function are **out of scope** (new capability, no data model).
>
> 🔗 **Every issue has a stable anchor `#bp-<id>`** (e.g.
> [`#bp-69`](Blueprint_Issues_Detail.md#bp-69)) — an explicit `<a id>` on each heading, so the tracker's
> deep links survive heading rewording. Headings carry emoji, quotes and `[NEW — …]` tags, so
> GitHub's prose-derived anchors would be both ugly and fragile. **When adding an issue, add its
> anchor line too**; `Blueprint_Issues_Tracker.md` links every row this way.

## How to read this

**Complexity** — the cost axis:

| Tag | Meaning |
|-----|---------|
| **WIRING** | Call existing code / register a handler. No new logic. |
| **RW-L** | Real work, low — contained new logic (≲150 lines), no design decisions. |
| **RW-M** | Real work, medium — new component or cross-cutting change; some design. |
| **RW-H** | Real work, high — new subsystem, or an architect decision is required first. |

**Confidence** — how the claim was established:

| Tag | Meaning |
|-----|---------|
| **✔✔** | Hand-verified directly against code during this audit. |
| **✔** | Agent-reported, key claim spot-checked. |
| **~** | Agent-reported, not independently re-derived. |

> ⚠ Three "nothing exists" findings were overturned mid-audit because the search missed `FDP/`.
> Any remaining **~** absence-claim should be re-checked across `FDP/` **and** `Hrot/` before being
> treated as settled.

## Two repo-wide patterns this audit kept hitting

**1. Absence claims need both trees.** Four "nothing exists" findings were overturned — the predicate
editing UI, Universal Breakpoints, C1-for-BTree, and BP-46 — every one because a search covered
`Hrot/` but not `FDP/`. Verifying *presence* is easy; verifying *absence* is where the failure mode
lives.

**2. 🔁 The inert-default guard — three confirmed instances.**
*An optional constructor dependency defaults to an inert value; the tests pass it explicitly and
prove the logic; every production site omits it, so the feature is silently dead.*

| # | Where | Effect | Status |
|---|---|---|---|
| 1 | `PredicateCompiler`'s `blueprintRegistry` (**BP-29**) | conditional breakpoints never fired | **Fixed** |
| 2 | `HsmValidator`'s `isStatefulSubtree` + `sharedScopeKeys` (**BP-61**) | both HSM concurrency rules never fire | **Open** |
| 3 | — | this blind spot is *why* **BP-31** was mis-scoped: it credited HSM with a guard that has never run | **Re-scoped** |

> **A green test suite is not evidence that a guard is wired.** Grep the production construction
> sites. Both fixed/found instances had passing tests that injected the dependency by hand.

> 🧪 **Before judging any test failure, read the [Test baseline appendix](#appendix--test-baseline-what-green-means-in-this-repo)**
> at the end of this document. Current baseline is **2594 passed / 0 failed** with the suite
> serialized; the older "~8–9 reds is normal" guidance is stale and would hide real regressions.

---

# Area A — Graph editor UX

Canvas ergonomics. Mostly NodeEdit-core capability the Blueprint host never registers.

<a id="bp-23a"></a>
### BP-23a — No copy / cut / paste / duplicate on the canvas (same-graph)
**Complexity:** RW-L · **Confidence:** ✔✔

> ✅ **DONE (2026-08-05).** Registered host-side (`BlueprintDocumentFactory.RegisterClipboardCommands`)
> for the same reason as BP-60: a clipboard entry is a list of asset `Node`s, which the vendored tree
> knows nothing about. `BlueprintClipboard` owns the payload format and the id remapping.
>
> ⚠ **The audit's trap, taken seriously.** Paste adds **fully-built** nodes through
> `BlueprintEditCommand`. Routing it through `GraphCommand.AddNode` would rebuild each node from its
> kind and re-apply only what `ApplyInitialProperties` knows — 8 node kinds of 50 — silently
> stripping the rest. `NodeConfiguration_SurvivesPaste_ForKindsTheSinkCannotBuild` pins it with
> `CompareNode.Operator` and `CastNode.TargetTypeId`, two the sink cannot set.
>
> **Pin GUIDs are re-minted too, not just node ids.** Pins carry their own GUIDs and links reference
> them directly, so a paste that reused them would leave two nodes whose pins collide — and any
> later link lookup could resolve to either.
>
> Decisions worth not revisiting: a link is copied only when **both** ends are in the selection (a
> half-copied wire dangles or silently re-attaches); **Duplicate never touches the clipboard**, so
> duplicating cannot clobber what was copied; paste **leaves its nodes selected**, so paste-then-drag
> works; a paste with no target position is **offset**, because a copy landing exactly on its source
> looks like nothing happened; and the payload carries a format marker so ordinary clipboard text is
> never parsed as a graph.
>
> The canvas menu's Paste entry — hard-disabled since it was written — now greys out only when there
> is genuinely nothing to paste, and the node menu gained Copy / Cut / Duplicate.
- **Symptom:** Paste is permanently greyed out; there is no copy at all. Probably the single most-felt gap.
- **Evidence:** `CanvasRenderer.cs:570` — `ImGui.MenuItem("Paste", "Ctrl+V", false, false)` (trailing `false, false` = *selected, enabled*). `CommandCatalog.cs:19-22` declares Copy/Cut/Paste/Duplicate; **zero** handler registrations repo-wide.
- **Fix:** `Node` is already `[JsonPolymorphic]`, so JSON round-trip is free. `IClipboard` exists, is DI-wired, and has **zero call sites**. `BreakpointJsonClipboard.cs` is an in-repo precedent for the same pattern. Crucially **`AddNodeCommand(Graph, Node)` takes a fully-built node** (`Execute() => _graph.Nodes.Add(_node)`), so paste bypasses the `ApplyInitialProperties` whitelist entirely.
- **Insertion:** new `ClipboardCommands.cs`, registered in `BuiltinCommandHandlers.RegisterAll`; enable the menu item.
- **Trap to avoid:** do *not* extend `BlueprintCommandSink.ApplyInitialProperties` — it whitelists only **8 of 50** node kinds, so a paste built on it would silently drop config on the other 42.
- **Remaining work:** new node GUIDs + internal link remapping.

<a id="bp-23b"></a>
### BP-23b — Cross-asset / cross-graph paste
**Complexity:** RW-M · **Confidence:** ✔
- Needs `VariableId` / type re-resolution against the destination asset. Scope **after** BP-23a.

<a id="bp-13"></a>
### BP-13 — No node align / distribute / straighten
**Complexity:** RW-L · **Confidence:** ✔✔

> ✅ **DONE (2026-08-05).** `AlignCommands` in NodeEdit itself — no asset knowledge is involved, so
> unlike BP-23a/BP-60 this belongs in the vendored tree. All nine are batch moves, so all nine go
> through `CommandBuilder.MoveNodes` and are single undo entries for free.
>
> Decisions: alignment measures **bounds**, not origins (aligning right or centring by the top-left
> corner leaves different-width nodes visibly ragged); distribute equalises **edge** gaps and never
> moves the two extremes, so it is idempotent and a second press is a no-op; straighten **anchors on
> the first selected node** and walks links inside the selection, rather than averaging — the
> designer keeps control of where the row lands. An alignment that would move nothing records
> nothing, so it cannot cost a Ctrl+Z that appears to do nothing.
>
> Exposed as an **Align** submenu on the node context menu; rows grey out with a tooltip naming the
> selection size they need.
- **Evidence:** `CommandCatalog.cs:83-91` declares 9 commands (AlignLeft/Right/Top/Bottom/CenterH/CenterV/DistributeH/DistributeV/StraightenConn); zero implementations anywhere in `NodeEdit/src`.
- **Fix:** `CommandBuilder.MoveNodes(IReadOnlyList<(NodeId, Vector2)>)` is the exact batch-move-with-inverse primitive already used by drag. AABB-of-selection pattern exists at `ViewCommands.cs:71-86`. Reroute primitives cover StraightenConn.
- **Note:** Distribute needs a stable position sort.

<a id="bp-02"></a>
### BP-02 — Comment colour and z-order changes bypass undo
**Complexity:** WIRING · **Confidence:** ✔✔

> ✅ **DONE (2026-08-04).** **13 of the 15 sites** now route through `view.Execute` with an inverse snapshotted from the current model state — 8 comment colours, 3 z-order/move-with-contents, comment delete (inverse re-adds with the original id and every property), and pin "Reset to Default" (inverse restores the prior value). The 14th was BP-59. **The 15th, `PromoteToVariable`, is deliberately left alone — see BP-60.**
- **Evidence:** `CanvasRenderer.cs:808-815` (8 palette colours) and `:823/:828` (Bring to Front / Send to Back) call `view.Commands.Apply(...)` directly instead of `view.Execute(fwd, inv, label)`.
- ⚠ **Scope corrected (2026-08-04) — there are 15 such sites, not 10.** The audit counted only the comment colours and z-order. Full enumeration of `view.Commands.Apply` in `CanvasRenderer.cs`:

| Line | Command | Menu item |
|---|---|---|
| 808-815 | `UpdateComment` ×8 | comment palette colours |
| 823, 828, 839 | `UpdateComment` ×3 | Bring to Front / Send to Back / *(third site)* |
| **638** | `SetPinDefault` | **"Reset to Default"** — *uncounted* |
| **758** | `RemoveNodes` | **"Delete Node"** — *uncounted, see BP-59* 🔴 |
| **845** | `RemoveComment` | **comment delete** — *uncounted* |
| **970** | `PromoteToVariable` | **"Promote to Variable"** — *uncounted* |

- **Fix:** capture the prior value as the inverse and route through `view.Execute`, as every other op in the same file does. Do **all 15**, not just the comment ones.

<a id="bp-59"></a>
### BP-59 — Context-menu "Delete Node" is not undoable, but the Del key is 🔴 **[NEW — found in verification pass]**
**Complexity:** WIRING · **Confidence:** ✔✔

> ✅ **DONE (2026-08-04).** Routed `CanvasRenderer.cs:758` to `EditCommands.DeleteSelectedUndoable`, the same path the Del key uses — which also removes the implicitly orphaned links the raw command left dangling.
- **Two paths for the same user intent; only one is undoable.**
  - **Del key** → `EditCommands.cs:95-125` builds a correct forward/inverse pair — `RemoveNodes` forward, plus a `Batch("Restore Nodes", …)` inverse that reconstructs each node via `AddNode(n.Id, n.Kind, n.Position, props)`. **Undoable.**
  - **Right-click → "Delete Node" / "Delete N Nodes"** → `CanvasRenderer.cs:758` calls `view.Commands.Apply(new GraphCommand.RemoveNodes(targetNodes))` raw. **Not undoable — the nodes are gone.**
- **Severity:** silent, unrecoverable data loss on a destructive action, reachable from the most obvious place a designer would look. Strictly worse than BP-02's cosmetic bypasses.
- **Fix:** route `:758` through the same `EditCommands` delete path the Del key uses — the inverse-builder already exists and is proven, so this is a call-site swap, not new logic.
- **Why the audit missed it:** BP-02 was scoped from its symptom ("comment colour"), so the enumeration stopped at comment commands.

<a id="bp-60"></a>
### BP-60 — "Promote to Variable" silently does nothing in the Blueprint editor 🔴 **[NEW — found while fixing BP-02]**
**Complexity:** RW-M · **Confidence:** ✔✔

> ✅ **DONE (2026-08-05).** Fixed as a **host command** (`editor.promote-to-variable`, registered by
> `BlueprintDocumentFactory`), not as a `BlueprintCommandSink` case.
>
> **Why.** `GraphCommand.PromoteToVariable` is a single opaque command: whichever sink implements it
> allocates the new node's id internally, so a caller cannot write the inverse. That is exactly why
> BP-02 left this one site on `Commands.Apply`, and why in the Blueprint editor it reached the sink's
> `default:` arm — which returns `new GraphCommandResult(true, null)`. Promotion is not one primitive
> anyway: it is *declare a variable* + *place a node* + *link it*. Composed at the host from commands
> the sink already implements, the caller owns every id, so BP-11's invariant holds (**the sink
> applies, the stack records**) and the gesture is **one undo entry** that reverses all three steps
> in the only safe order — unlink, remove node, undeclare.
>
> **This lifts BP-02's 15th and last bypass.** `CanvasRenderer` now invokes the command when a host
> registers one and falls back to the single-command path for `NodeEditor.Demo`, whose
> `FakeCommandSink` does implement it.
>
> Shape matches the reference implementation: an **input** pin gets a Get node to its left feeding
> it, an **output** pin gets a Set node to its right fed by it. The link names the right pin because
> the host projects the new node's canonical pins itself and pre-mints their GUIDs through
> `InitialProperties["PinIds"]` — the same machinery the wire-drop create-path uses.
>
> ⚠ **A name collision uniquifies rather than rejects.** `CreateVariable` refuses duplicates, which
> from this modal would look exactly like the bug being fixed. ⚠ **BP-57:** there is no per-graph
> scope in the data model, so "Promote to Local Variable" produces a Blueprint variable and logs
> that it did — rather than silently reinterpreting the request.
>
> ⚠ **Every test asserts the effect, never `Success`.** A test on the result would have passed
> against the bug — that *is* the bug. Non-vacuity checked by pointing the handler back at the old
> `Commands.Apply` path: 9 of 15 went red, and the 6 that stayed green are the guards that correctly
> expect nothing to happen.

**Complexity:** RW-M · **Confidence:** ✔✔
- **`GraphCommand.PromoteToVariable` is implemented only by `NodeEditor.Demo`'s `FakeCommandSink`** (`:176`, `:357`). `BlueprintCommandSink` has **no case for it**, so it falls to that sink's `default:` branch — *"Unknown commands are silently accepted (forward-compat)"* — which returns `Success = true` and does nothing.
- **User-visible effect:** the pin context menu offers "Promote to Variable…" / "Promote to Local Variable…", a modal opens, the designer types a name and clicks **Promote**, the modal closes — and nothing happens. No node, no variable, no error. Same family as BP-09 and BP-12e: the UI advertises an action that cannot run.
- **Reference implementation exists** — `FakeCommandSink.ApplyPromoteToVariable` (~50 lines) resolves the pin, allocates a `VariableId`, adds a `Util.GetVar`/`Util.SetVar` node offset from the owner, and links it. The Blueprint version additionally needs to append a declaration to `asset.Variables` with a type inferred from the pin.
- ⚠ **Undo must be designed together with the implementation, not retrofitted.** `UndoStack` requires the *caller* to supply the inverse, but the inverse needs the node/link/variable ids the sink allocates. This is why BP-02 deliberately left this one call site on `Commands.Apply`: recording an undo entry for a no-op would make Ctrl+Z consume a step that reverses nothing.

<a id="bp-62"></a>
### BP-62 — Component type resolution depends on assembly **load order** ⚠ **[NEW — root cause of the order-dependent test]**
**Complexity:** RW-M · **Confidence:** ✔✔

> ✅ **DONE (2026-08-04) — fixed at the product level, not papered over in the test.**
> New `EditorTypeResolutionScope` force-loads referenced-but-not-yet-loaded assemblies once per process (transitive walk of the reference graph from everything currently loaded), then returns the **live** `AppDomain` list so ALC hot-reloaded assemblies still appear — the list itself is deliberately not cached. Both resolution paths now go through it: `ComponentFieldReflector.ResolveType` **and** `ComponentTypeScan.Compute` (the picker), which had the identical flaw.
> The conflation is gone too: new `ComponentWritability` tri-state (`Writable` / `NotWritable` / `Unresolved`) separates a decision from missing information. `IsWritableComponent` keeps its bool contract (delegates, `== Writable`) so no call-site churn; the collection-write bake uses the tri-state.
> **Proof it is a real fix:** the test's `EnsureAiBehaviorsLoaded()` band-aid was *removed*, and `BakesOpAccessor` still passes standalone. 8 new regression tests pin the tri-state and both discovery paths. Suite 2583/0.
> ⚠ *Residual, deliberately not chased here:* an `Unresolved` at the bake site still leaves the node silently unbaked rather than logging. Making it loud needs an `IDiagnosticsSink` injected into `BlueprintCommandSink`, which it does not currently take — a wider change than this fix.
- **`ComponentFieldReflector.ResolveType` only sees assemblies that are already loaded:**

```csharp
internal static Type? ResolveType(string fqn)
{
    var t = Type.GetType(fqn);                                  // this assembly / already-resolved only
    if (t != null) return t;
    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) // ONLY loaded assemblies
        { try { t = asm.GetType(fqn); } catch { continue; } if (t != null) return t; }
    return null;                                                 // silently "not a component"
}
```

- The CLR loads assemblies **lazily on first use**, and a `ProjectReference` does *not* force a load. So a component whose assembly nothing has touched yet resolves to `null`, and every caller reads that as *"not a writable component"* rather than *"don't know yet"*.
- **Callers affected:** `IsWritableComponent` (the `[BlueprintWritable]` opt-in gate), `TryReflectWriteAccessors`, the `SetComponent` picker's type bake, and `BlueprintNodeModel`'s stale-reference check.
- **How it surfaced:** `BlueprintCommandSink.TryBakeCollectionConsumer`'s `CollectionWriteNode` case gates on `IsWritableComponent(...)`. With `Hrot.AI.Behaviors` unloaded the gate returns false, the method early-returns, and the node keeps `ComponentTypeFqn == ""` — **silently unbaked**. This is exactly why `CommandSink_AddLink_…BakesOpAccessor` passed only when some *other* test happened to load the assembly first.
- **Editor impact:** probably masked today, because the editor loads the AI assembly at startup via `CgfBehaviorSetup.LoadFromAiAssembly`. But the dependency is real and unstated: the bake's correctness rests on an unrelated startup side effect. It also **degrades silently** — an unbaked node is only caught later by the canvas bake-incomplete error / Stage2 BP2067, well away from the wire that caused it.
- **Fix direction (needs a decision):** either resolve against an explicit, eagerly-populated assembly set (the editor already knows which game assemblies it loads), or have `ResolveType` distinguish *"resolved: not writable"* from *"could not resolve"* so callers can fail loudly instead of treating unknown as no.
- **Test-side mitigation applied:** `BlueprintCommandSinkTests.EnsureAiBehaviorsLoaded()` touches a type in the assembly before the reflecting test, making it deterministic alone and in-suite. ⚠ **That is a band-aid on the test, not a fix for the product.** Other test files also reference `Hrot.AI.Behaviors.*` FQNs as strings and carry the same latent dependency; they pass today because the full suite loads the assembly early.

<a id="bp-03"></a>
### BP-03 — Bookmarks cannot be renamed or deleted
**Complexity:** WIRING · **Confidence:** ✔✔

> ✅ **DONE (2026-08-04).** `BookmarkStore.Rename(id, label)` added (blank labels refused — a
> label-less bookmark is an unclickable blank row). Panel rows gained inline rename (double-click or
> context menu), a delete button plus menu item, and **click-to-jump**, which the panel never had:
> a bookmark's whole purpose was reachable only via Ctrl+1..9, so the panel itself was inert.
> Ordering/labelling moved to `BookmarkPanelLogic` so they are testable without an ImGui context.
> 13 tests.
- **Evidence:** `BlueprintBookmarksWindow.cs:11-13` self-documents "(V1: no rename/delete UI…)"; `BookmarksPanel.cs:17-36` is a read-only text list.
- **Fix:** `BookmarkStore.Remove(id)` already exists; `Bookmark` is a `record`, so rename is `b with { Label = x }` + `SetSlot`.

<a id="bp-17"></a>
### BP-17 — No node renaming / custom titles
**Complexity:** RW-L · **Confidence:** ✔✔

> ✅ **DONE (2026-08-05).** New `NodeMetadata.CustomTitle`, a `"Title"` key on the existing
> `SetNodeProperty` path (so the undo plumbing came for free), and a Rename item on the node menu.
>
> Two decisions: the **generated title becomes the subtitle** when an override is set — a renamed
> node must not lose the only indication of what it actually is, and `NodeRenderer` already draws
> subtitles; and **blank clears the override** rather than storing an empty header, so a node can
> always be restored to the title its configuration implies without reaching for undo. The two are
> kept separate rather than baked, so a node whose configuration later changes still re-derives its
> generated title underneath.
- **Evidence:** `BlueprintNodeModel.cs:24` — `Subtitle => null` always. Node context menu (`CanvasRenderer.cs` `HoverKind.Node`) has no Rename; the Rename at `:800` belongs to `HoverKind.Comment`. A `"Comment"` `SetNodeProperty` key exists end-to-end but **no UI ever issues it**.
- **Fix:** every piece has a precedent — `InteractionState.RenamingComment` inline-rename UX to mirror, and `SetNodeProperty` undo plumbing already proven. Add `NodeMetadata.CustomTitle`, a `"Title"` case, an F2 menu item, and a `RenamingNode` interaction field.

<a id="bp-18"></a>
### BP-18 — Node body collapse not exposed
**Complexity:** RW-L · **Confidence:** ✔✔

> ✅ **DONE (2026-08-05).** New `NodeMetadata.Collapsed`, a `SetNodeCollapsed` case on the sink, and
> a Collapse/Expand item on the node menu.
>
> ⚠ **The third instance of the `default:`-returns-success trap** (after BP-60 and BP-68): the
> command existed, `NodeRenderer` honoured the flag, and the sink silently accepted the command
> while doing nothing. The test asserts the *effect*, and a separate one asserts that collapsing an
> unknown node now **fails** rather than reporting success.
- **Evidence:** `BlueprintNodeModel.cs:44-45` hardcodes `IsCollapsed => false`. `NodeRenderer` honours the flag.
- **Fix:** `GraphCommand.SetNodeCollapsed` is already defined, with a working reference implementation in `NodeEditor.Demo/FakeBlueprint/FakeCommandSink.cs:126`. Needs a `NodeMetadata` field + a sink case + a collapse glyph.
- ⚠ `BlueprintCommandSink.Apply`'s `default:` case **silently no-ops unknown commands** (`:156-158`), so issuing `SetNode*` today fails quietly.

<a id="bp-19"></a>
### BP-19 — No minimap
**Complexity:** RW-L · **Confidence:** ✔✔

> ✅ **DONE (2026-08-05).** `MinimapRenderer` — a corner overlay drawing each node as a filled rect
> (error nodes in the error colour) plus an outline of the current view; clicking or dragging inside
> recentres the viewport, so it works as a scrubber rather than only a jump target.
>
> Two geometry decisions: the fit is **uniform**, not per-axis, or the minimap stops being a
> recognisable miniature of the graph; and the mapped region is the graph bounds **unioned with the
> visible rect**, so the viewport rectangle stays inside the overlay even when the user has panned
> away from every node — which is exactly when a minimap earns its keep. Sub-pixel nodes are floored
> to 2px: a minimap that omits nodes is worse than none.
>
> The visibility flag lives on `ViewportState` so `editor.toggle-minimap` — declared and never
> registered — could be registered alongside the other view commands, which only ever see the
> `GraphView`.
- `CommandCatalog.ToggleMinimap` declared, never implemented. `ViewportState` supplies all needed transform math (`GraphToScreen` / `ScreenToGraph` / `FrameRect`). ~150-200 lines incl. click-to-pan.

<a id="bp-20"></a>
### BP-20 — No error list / jump-to-next-error
**Complexity:** RW-L · **Confidence:** ✔✔

> ✅ **DONE (2026-08-05).** `editor.next-error` / `editor.prev-error` registered on **F8 /
> Shift+F8**, selecting and centring each node whose `State` is `Error` or `Warning`.
>
> **Open question answered by the existing data:** the source is the host's node model, which is
> live rather than compile-time — a Blueprint already marks an unresolved CLR call or a stale
> component bake, and the canvas already paints them. Nothing new had to be plumbed.
>
> **Errors before warnings**, so the first press on a broken graph lands on something that actually
> stops the build. The sequence **anchors on the current selection** rather than a stored cursor:
> a stored index would resume from a stale position after the user clicks elsewhere, and would
> silently skip an entry once a fix removes a node from the list.
- **Evidence:** `CommandCatalog.NextError` / `PrevError` declared, never registered. No error-list UI.
- **Fix:** `NodeState.Error`/`Warning` flags already exist and `FindEngine` already filters on them (`:47-53`). `FindBar.Next` / `CenterOnActive` is a ready cycle-and-centre pattern to mirror.
- **Open question:** diagnostics source — compile-time only, or live?

<a id="bp-25"></a>
### BP-25 — Cross-blueprint search is cosmetic
**Complexity:** RW-M · **Confidence:** ✔✔
- **Evidence:** `FindEngine.Search(query, scope, view)` never reads `scope`; its own docstring says *"only `FindScope.CurrentGraph` is handled here"*. The UI offers Asset / OpenTabs / WholeProject.
- **Why bigger:** `FindEngine`/`FindBar` are architecturally single-graph-bound. Needs a multi-graph aggregation layer, merged ranking, and cross-tab navigate-then-centre.

<a id="bp-28"></a>
### BP-28 — No advanced-pin hiding
**Complexity:** RW-M · **Confidence:** ✔✔
- `INodeModel.ShowAdvancedPins` and `IPin.IsAdvanced` exist and are honoured by the renderer, but `BlueprintPinModel.IsAdvanced` is never assigned. Needs a **new persisted per-pin flag** *and* an authoring UI to mark pins advanced — there is no "which params are advanced" concept to project from.

<a id="bp-56"></a>
### BP-56 — No wire-level execution-flow highlighting
**Complexity:** RW-L · **Confidence:** ✔✔
- Node borders glow during execution (`NodeRenderer.cs:215-216,251`) and `WhenFiringPulseRenderer` pulses `When` nodes, but `WireRenderer` never renders execution state. Unreal shows a travelling pulse along exec wires.

---

# Area B — Node authoring surface

Whether a designer can *place* and *configure* each node kind. 13 of 50 kinds run but cannot be configured.

<a id="bp-04"></a>
### BP-04 — `Compare` / `BinaryOp` / `BooleanOp` / `Not` cannot be placed at all
**Complexity:** WIRING · **Confidence:** ✔✔

> ✅ **DONE (2026-08-04).** 14 baked palette entries added via a new `MakeBaked<TNode>` helper — 6 `Compare`, 5 `BinaryOp`, 2 `BooleanOp`, 1 `Not` — grouped under the existing `Math/Compare`, `Math` and `Math/Bool` picker categories. Baking is safe because `BlueprintCommandSink.CreateAssetNode` builds from `CreateInstance` and only *overlays* caller props, so the 8-of-50 `ApplyInitialProperties` whitelist is never in the path. **Pins are left empty deliberately** — `Stage0_Rehydrate` reconstructs `A`/`B`/`Result` for a pin-less instance (`DeterministicPinReconstructionTests`), so no pin authoring or drawer is needed. Guarded by a test asserting **every** enum value has a row, so a new operator cannot silently become unreachable.
- **Symptom:** Four fully-lowered, compile-tested node kinds are unreachable from the editor. Verified they are instantiated **only in tests** — zero `new CompareNode` etc. in the editor tree.
- **Why it happened:** `BlueprintMathPaletteEntries.cs` routes math through CLR `BlueprintMath` helpers as `FunctionCallNode`, so the functional need is partly covered and the native kinds were never given a front door.
- **Fix:** 14 palette entries (one per enum value), baked at create — exactly the `MakeMath` / `ChannelCommandEntries` recipe. **No drawer needed.** ~40-60 lines.
- **Also:** `Blueprints_Overview.md:75` marks these ✅ — see BP-47.

<a id="bp-22"></a>
### BP-22 — `GetParameter` cannot be placed
**Complexity:** RW-L · **Confidence:** ✔✔
- Lowered at `Stage5_Schedule.cs:2098`, zero editor instantiations. Unlike BP-04 it is **asset-specific** (`ParameterId` references `asset.Parameters`), so it needs a picker rather than a baked entry. Model on `BlueprintPickerSources`' `variables.all` pattern re-pointed at parameters.

<a id="bp-05"></a>
### BP-05 — `ReadRankedResult.Rank` uneditable
**Complexity:** WIRING · **Confidence:** ✔✔

> ✅ **DONE (2026-08-04).** Clamped to ≥ 0 (a rank indexes the EQS result list; a negative one
> indexes out of range — clamped rather than rejected, so the stepper cannot author an invalid
> asset). Hold-to-repeat coalesces to one undo entry via `ContinuousEditCoalescer`.
- Plain `ImGui.InputInt`; no catalog dependency. Simplest of the drawer gaps.

<a id="bp-06"></a>
### BP-06 — `WaitForChannel.ChannelType` uneditable
**Complexity:** WIRING · **Confidence:** ✔✔

> ✅ **DONE (2026-08-04).** ⚠ **The catalog is keyed by *(channel, action)***, so the naive
> `Select(e => e.ChannelTypeFqn)` the task implies would list a channel once per action — a channel
> with 8 actions appears 8×. Deduplicated and sorted. Unlisted current values are surfaced and
> preserved, mirroring `GetSharedNodeSession`'s picker.
- Runs and is run-proven, but has no drawer. Reuse `IChannelCommandCatalog`; `ChannelCommandNodeDrawer.cs` (109 lines) is a near-direct template and `WaitForChannel` needs only the channel-type list.

<a id="bp-07"></a>
### BP-07 — `CallCustomEvent.EventId` uneditable
**Complexity:** WIRING · **Confidence:** ✔✔

> ✅ **DONE (2026-08-04).** ⚠ **The audit named the wrong source.** It says reuse
> `UnifiedEventDiscovery.All()` — but that enumerates `[BlueprintEvent]` C# structs and
> editor-authored **engine** events, the vocabulary `WaitForEvent` and the When node's EventFired
> mode use. A custom event is **asset-scoped**: `NodePinSchema.CallCustomEventPins` resolves
> `EventId` against `asset.CustomEvents`, and Stage5 does the same. Building it as written would
> have produced a picker whose every choice failed to resolve.
>
> The drawer reads the owning asset, writes the declaration's **GUID** (what pin projection parses)
> and still resolves a bare **Name**, which Stage5 accepts — so hand-authored assets don't show as
> dangling. The event's parameters are its data-IN pins, so the edit is structural.
>
> ⚠ **Was unreachable until BP-12c** — no asset could declare a custom event, so the picker
> correctly rendered "this Blueprint declares no custom events" and had nothing to select. BP-12c
> shipped (2026-08-05) and the picker is now live; `CustomEventCreateTests` asserts the round trip
> end-to-end (create → listed → selected by GUID → resolves).
- Reuse `UnifiedEventDiscovery.All()`, already production-wired, which unifies C# `[BlueprintEvent]` structs and editor-authored events.

<a id="bp-08"></a>
### BP-08 — `CallPeerBlueprint` target uneditable
**Complexity:** WIRING · **Confidence:** ✔✔

> ✅ **DONE (2026-08-04).** New `IBlueprintPeerProvider` seam (mirroring `IComponentTypeProvider`)
> with a `BlueprintPeerSource`-backed implementation and an empty default — peers are discovered by
> scanning a directory, which a headless test must be able to replace, and the drawer registry is
> built at startup where no asset root is in scope.
>
> Two **dependent** pickers: choosing a peer narrows the function list and **clears a `FunctionRef`
> the new peer doesn't export, in the same edit**. Leaving it would silently collapse the node's
> pins to the untyped `exec + Return:System.Object` fallback with nothing on screen to say why.
> Dangling peer and dangling function are flagged separately — with a resolved peer we know the
> function name is wrong, which is a different message.
>
> ⚠ **Wired in `EditorSubsystem`, not left to the default.** The provider defaults to empty, so
> omitting it at the one production call site would have shipped a picker that always says "no peer
> Blueprints discovered" — the inert-default guard (BP-29, BP-61) all over again. Passed explicitly.
- Reuse `BlueprintPeerSource.EnumerateAll()` (already used by `QuickReloadService` for this very node kind) plus the existing peer-signature lookup for the function list.

<a id="bp-14"></a>
### BP-14 — `Return.Status` uneditable (always Success)
**Complexity:** RW-L · **Confidence:** ✔✔
- `Nodes.cs:182` — `Status { get; set; } = NodeStatus.Success`; no drawer, no bake path. A combo over `NodeStatus` mirroring `WhenNodeDrawer.DrawModeSelector`, ~20-30 lines.

<a id="bp-71"></a>
### BP-71 — A Function graph's return value cannot be wired 🔴 **[NEW — found while auditing what BP-24 unblocked]**
**Complexity:** RW-L *(the change is small; the contract decision is not)* · **Confidence:** ✔✔
*(re-derived across editor, compiler and all 92 shipped assets)*

> ✅ **DONE (2026-08-06, Batch 16)** to [Q24](Architect_Question_24_Function_Return_Value_Wiring.md)
> **A1+B1+C3**. Both projections emit `Direction=="In"`; `BuildReturnTerminator` accepts either
> direction; **BP1655** (unwired return) and **BP1656** (`Outputs.Count > 1`, worded *"not supported
> yet — see BP-73"*) are new Stage 2 errors; Stage 5 falls back to a **declared** `default(T)` via the
> existing `IrOp_Const("default")`, so the dangling-temp CS0103 is structurally impossible.
> 12 new tests including the seam-crossing one the old suite lacked — the **link validator accepting
> the wire** — plus a companion that keeps the legacy-`"Out"` rejection as evidence of the defect.
> ⚠ **BP1655 skips link-less graphs** (the unauthored on-disk stub shape). Shipped
> `SquadState.GetThreatLevel` is exactly that and stays green in
> `RecipeIntegrityTests.AllRecipes_ValidateOnly_NoErrors` — real-asset proof of the exemption, not
> just a synthetic one.

**Original decision record —** A1+B1+C3, by the user 2026-08-06; single-output only. Flip both projections to `Direction == "In"`; accept
**either** direction in `BuildReturnTerminator` (one `||`, so no migration and no silently-void
return); an unwired return becomes a **Stage 2 error** *and* the emitter falls back to `default(T)`.
Q24-**D** (Unreal-style N outputs) is still open and blocks nothing here — costed in the doc as
`RW-M` / ~250–450 lines and mostly additive, recommended **D1′** (ship single-output; word the
`Outputs.Count > 1` diagnostic *"not supported yet"*, not *"illegal"*).

The `Return` node's value pin is declared an **output** and consumed as an **input**:

| Where | Says |
|---|---|
| `Host/NodePinSchema.cs:328` `ReturnNodePins` | `MakeData(output.Name, **"Out"**, typeId)` |
| `Stage0_Rehydrate.EnrichReturnPins` | `MakePin(output.Name, **"Out"**, …)` |
| `Stage5_Schedule.BuildReturnTerminator:1896` | matches `!IsExec && Direction == "Out"`, then `ResolveDataPin(rn.Id, outPin.Id)` — which looks for a link with **`ToNodeId == rn.Id`** |

`BlueprintPinModel.cs:86` maps Direction straight through (`"In" → Input`, else `Output`), and
`BlueprintLinkValidator` rejects same-direction links outright. **So the designer cannot wire a value
into it, and there is no fallback:** the pin gets no inline default-value editor either, because
`BlueprintPinModel` synthesises `Default` only for `Direction == "In"` data pins.

⚠ **`Return` is the only node in the compiler with this shape.** The universal convention is
`ResolveAllDataInputs` — `Where(p => !p.IsExec && p.Direction == "In")` (`Stage5_Schedule.cs:3233`).
Of ~20 `ResolveDataPin` call sites, `BuildReturnTerminator:1898` is the **sole** one passing an
`"Out"` pin.

**The contract is deliberate and test-locked** — `BATCH03A_FunctionGraphCallTests.cs:139` and
`NodePinSchemaEnrichmentTests.cs:750` both state it in prose ("*the value pin MUST have
`Direction=="Out"` (compiler contract)*"). Nothing is broken in the compiler's own terms; **the two
halves have never been used together.** That is why a green suite proves nothing here.

**Failure mode** (Instance dispatch only — `BuildReturnTerminator` returns `IrTerm_ReturnStatus`
early for `Library`/`AiPrimitive` and ignores the pin): `ResolveDataPin` finds no link ⇒ **BP4001
*warning*** + a dummy `IrValue`; temps are declared only at their assigning statement, and a dummy
has none; `TerminatorEmitter` writes `return __t7;` ⇒ **CS0103 with no BP diagnostic**. The same
unattributed-Roslyn-error shape as **BP-69**.

**Blast radius is near-zero — the path is wholly unexercised.** Across all **92** `*.bp.json`:
**2** graphs declare `Outputs` (`SquadState.bp.json` `GetThreatLevel`, two copies), **0** `Return`
nodes carry authored pins (all `"Pins": []`, reprojected on load), **0** functions wire a return
value. `GetThreatLevel` itself has `"Links": []` — it does not wire its own return either.

**Fix (pending Q24-A):** flip both projections to `"In"` and widen `BuildReturnTerminator` to accept
either direction — two lines, one predicate, two contract tests. Q24 also decides whether an unwired
return becomes a Stage 2 error (today: a warning plus an untraceable CS0103) and whether
`Graph.Outputs.Count > 1` is rejected — only `[0]` is ever read, at 5 sites, while
`GraphSignatureWindow` lets a designer add more and **silently discards them**.

<a id="bp-73"></a>
### BP-73 — Function graphs support only ONE output value (Unreal supports N) ✅ **[SHIPPED — Batch 18]**
**Complexity:** RW-M · **Confidence:** ✔✔ *(costed against code, not estimated)*

**Decision:** the user wants **proper N-output** for Unreal parity. It is deliberately **not** bundled
into BP-71 — until this ships, `Outputs.Count > 1` is a Stage 2 error worded
***"not supported yet — see BP-73"***, never "illegal". See
[Q24-D](Architect_Question_24_Function_Return_Value_Wiring.md#q24-d--one-output-or-many).

**Today.** `Graph.Outputs` is a `List<ParameterDecl>` but only `[0]` is ever read — 5 sites
(`InstanceEmitter:272`, `LibraryEmitter:33`, `CSharpEmitter:240`, `Stage0_Rehydrate:284/808`,
`Stage5:1480/3010`). `GraphSignatureWindow` lets a designer add a second and it was **silently
discarded** (BP-71 makes that loud).

#### ⚠ The one invariant not to break

`IrStatement` has exactly one `IrValue? ResultValue` (`Ir/IrStatement.cs:5`). Making it a list would
touch every statement consumer **plus** the one-`PinId`-per-statement debug annotation, probe
insertion and breakpoint mapping. **Do not.** Both viable designs below keep it: the call produces
one carrier value, then N cheap field-reads fan it out.

#### What already exists (this is why it is `RW-M`, not `RW-H`)

| Piece | State |
|---|---|
| **Library dispatch ABI** | **already N-shaped.** `EmitLibraryFunctionAdapter` writes results into an `outputs` **byte span** via `MemoryMarshal.Write` (`CSharpEmitter:267`) and already walks **N inputs** with an `__off` cursor (`:252-258`). N outputs is that loop mirrored. ⚠ **Sequential writes with `__off` advance, not one packed struct** — the reader walks sequentially and struct padding would not match. |
| **Per-pin value resolution** | **exists.** `_statementPinCache` maps pin→value precisely so a statement-produced value is never recomputed (`Stage5:1526` — *"re-invoking would re-run the side effect"*). Probes/watch stay per-pin correct for free. |
| **Debug map** | **already loops all outputs** — `CSharpEmitter.Emit:69` registers every `graph.Outputs` entry as a `DebugPinInfo`. |
| **Signature window** | **nothing to build** — already N-row Add/Remove/Rename/Retype/Move. |
| **Synthesized return types** | **precedent exists** — `StatementEmitter.TypeRefToCSharp:1591` passes `_`-prefixed names through verbatim, commented *"local generated type (synthesized struct)"*. |

#### What is genuinely new

1. **The Instance carrier** — the one real design question. `IrOp_GraphCall.ReturnType` is a single
   `IrTypeRef` and the emitted C# is a plain method. **`ValueTuple` vs a synthesized
   `_FuncOut_{Name}` struct:** the tuple gives named elements free; the struct matches the existing
   `_`-prefix convention and is easier to name in diagnostics and the watch panel. **Settle this
   first.** The Library path uses neither.
2. **Fan-out op** — one new `IrOp` reading field *i* of the carrier, one statement per *consumed*
   out-pin, each cached in `_statementPinCache`.
3. **Return side** — `BuildReturnTerminator` collects N pins. It can stay **single-value** by
   synthesizing a carrier-construction statement immediately before the return
   (`var __t9 = (a, b); return __t9;`) ⇒ **zero terminator changes**.
4. **Projections** — `EnrichReturnPins`, `ReturnNodePins`, `FunctionGraphCallPins` loop instead of
   taking `[0]`. All three **already loop over `Inputs`** — mirror that.
5. Retire the "not supported yet" diagnostic; keep a `[CoversDiagnosticCode]` test if the code stays.

**Estimate: ~250–450 lines across compiler + editor, plus tests.** Strictly additive — with
`Outputs.Count <= 1` the emitter keeps producing today's bare `float`/`void`, so no golden IR, no
shipped asset and no existing test moves.

**Depends on BP-71** (the pin must be wirable at all before N of them are useful).

---

### ✅ Resolved — Batch 18 (2026-08-06)

Shipped as scoped. The five `Outputs[0]` sites all loop now, and the estimate held (~330 lines).

**Carrier: `ValueTuple`** — item 1 above called this "the one real design question"; here is what
settles it. `CSharpEmitter.IsReferencableStateFieldType` (`:330`) treats a `_`-prefixed synthesized
type as **NOT referencable outside the generated class** and excludes it from `StateFields`, so a
`_FuncOut_{Name}` return would be **invisible to the debugger/watch** — the very thing the "easier to
name in the watch panel" argument wanted. A `ValueTuple` is a BCL type. *The `_`-prefix precedent
cited in the table above is real but points the wrong way.*

**The simplification that shrank the work.** The carrier needs **no `IrTypeRef` representation at
all**: temps emit as `var __tN = …`, so C# infers it. Only the three method-**declaration** sites need
a composed type string, and each already reads `graph.Outputs`, so one shared
`LibraryEmitter.CSharpReturnType` covers them. N-output never enters the type system.

| Piece | Shipped as |
|---|---|
| Carrier | unnamed `ValueTuple`, e.g. `(float, bool)` |
| Pack | `IrOp_MakeTuple` in a statement **before** the return ⇒ `IrTerm_Return` keeps its single `IrValue`, so block emitters / debug map / breakpoint anchoring are untouched |
| Fan-out | `IrOp_TupleField`, read **positionally** (`ItemN`) so an output whose name is not a valid C# identifier cannot break it — the same reason the tuple is **unnamed** (named elements inherit the `ItemN` positional-collision rule for no benefit the emit uses) |
| Library ABI | sequential writes with an `__oo` advance, as the table predicted |
| BP1656 | **retired**, not reworded |

⚠ **Fan-out emits a statement per out-pin even when only some are wired.** Emitting lazily on first
use would place the extraction in whichever block first *consumed* the pin — which for a result
crossing a branch is not the block holding the call. An unused `var` is harmless; a value read in a
block that never declared it is CS0103.

⚠ **BP1656 retirement touched three places, not one:** the validator, BP-71's test asserting the gate
**fires** (inverted rather than deleted, so the transition stays visible in the suite), and the
diagnostic-coverage ratchet — whose not-emitted list is now documented to cover **retired** as well as
reserved codes. The code stays in `DiagnosticCodes` so the number is never reused.

**13 tests.** Verified by reverting the source: **9 of 12 go red**, and the 3 that stay green are
exactly the additivity guards, which must not move. One test compiles through **Roslyn** — the only
check that proves the emit is *valid* C#, since `BlueprintCompiler.Compile(...).Succeeded` does not run
it. That test immediately earned its place by catching a fixture bug: a `System.Single` literal
authored as `1.5` emits a C# **double**, because `ValueJson` is emitted **verbatim** and float literals
need the `f` suffix (the shipped convention, e.g. `ValueJson = "5.5f"`). One test that asserted inside
`if (src is not null)` was also tightened — it could pass vacuously, BP-69's exact shape.

All eight gates green (blueprints **2854/0**, 10 skipped).

<a id="bp-74"></a>
### BP-74 — Collapse selection → Function / Macro is unreachable, and would no-op if reached 🔴 **[NEW — functions audit, 2026-08-06]**
**Complexity:** RW-L · **Confidence:** ✔✔ *(both trees checked)*

> ⚠ The register previously listed this as **out of scope — "absent, and nothing to collapse into
> until BP-24"**. Both halves of that are now wrong: BP-24 shipped, and the capability is **not
> absent** — it is scaffolded in `FDP/` and unwired in `Hrot/`. The search that produced the original
> claim covered `Hrot/` only, which is the failure mode this register has already recorded four times.

| Piece | State |
|---|---|
| `GraphCommand.CollapseToFunction` / `CollapseToMacro` | **exist** — `NodeEditor.Core/Commands/GraphCommand.cs:93,100` |
| `editor.collapse-to-function` / `-macro` ids | declared — `CommandCatalog.cs:57-58` |
| `BlueprintCommandSink` case | **none** ⇒ falls to the `default:` arm that **returns success** (trap #5, as BP-18 and BP-60) |
| Canvas menu item | **none** — `CanvasRenderer` has no Collapse entry at all; the Demo binds Ctrl+E in `DemoShell`, not in shared UI |

So **both halves are missing**: there is no way to invoke it, and if there were, it would silently
report success and do nothing.

⚠ **`FakeCommandSink.ApplyCollapseToFunction:401` is a scenario prop, not a reference
implementation.** It hardcodes the S22 demo's pin signature (`Base`/`Multiplier`/`Bonus`/`Result`) as
literal `AddPin` calls. It models the gesture — delete the selection, add a call node at the
centroid, add a My Blueprint entry — but **not** the actual problem: deciding which links crossing the
selection boundary become parameters and which become returns. That boundary analysis is the work.

**Fix:** boundary analysis over the selection (in-edges → Inputs, out-edges → Outputs), create the
graph, place the call node, remap links — composed from primitives the sink already implements, as a
**host command** so it is one undo entry (the BP-60 precedent: the single opaque command hides the new
ids, so no caller could write its inverse). Collapse-to-**macro** is the same gesture and lands with
`BP-78`.

<a id="bp-75"></a>
### BP-75 — A function graph has no palette entry and cannot be dragged onto the canvas **[NEW — functions audit, 2026-08-06]**
**Complexity:** RW-L · **Confidence:** ✔✔

`BlueprintNodeCatalog` mints per-asset palette entries for exactly two things:

| Asset-scoped thing | Palette entry | Drag-to-canvas |
|---|---|---|
| Custom event | ✅ `CustomEvent.{Name}` (`:243`) | ✅ `Event.CallCustom` (BP-68) |
| Callable peer | ✅ `CallPeer.{guid}` (`:260`) | ✅ (BP-68) |
| **Function graph** | ❌ **nothing** | ❌ `CreateDynamicNode` has no case |

The catalog never iterates `asset.Graphs` at all. So after creating a function the designer's only
route to calling it is: place the generic **"Function Call"** node (`BlueprintNodePaletteEntries:147`),
then switch its drawer to graph mode and pick the target from a combo
(`FunctionCallNodeDrawer:185`). That combo **does** work — this is a discoverability gap, not a
correctness one — but it is not the Unreal motion, and it is inconsistent with the two sibling
asset-scoped kinds that both got the treatment in BP-12c/BP-68.

**Fix:** mirror BP-12c exactly — a `Function.{guid}` per-asset catalog entry plus a `CreateDynamicNode`
case binding it to a `FunctionCallNode` with `TargetGraphId` pre-set. The My Blueprint drag path is the
same one BP-68 fixed for the other two kinds.

<a id="bp-76"></a>
### BP-76 — "Go to Definition" and "Expand Node" can never enable in the Blueprint editor **[NEW — functions audit, 2026-08-06]**
**Complexity:** WIRING · **Confidence:** ✔✔

`CanvasRenderer` shows both menu items and gates them on a **hardcoded list of node-kind ids**:

```
node.Kind.Id == "Function.Call" || "Macro.Call" || "Event.CallCustom"     // :740-743, :751-753
```

Those are **NodeEditor.Demo's** dotted naming convention. The Blueprint editor's ids are
`"FunctionCall"` and `"CallCustomEvent"` (`BlueprintNodePaletteEntries:110,147`), so **no blueprint
node can ever match** and both items render permanently greyed. `Go to Definition` also advertises
**F12**, which does nothing.

⚠ **The gate is worse than a wrong list.** `canExpand` additionally tests
`node.Title == "ScaleBy"` (`:753`) — a **demo node's display name**, in shared UI code that every host
renders. Any host other than the Demo inherits a dead menu item.

⚠ Separately, **nothing registers `editor.go-to-definition`** in the Blueprint editor, so fixing only
the gate would turn a greyed item into a dead one.

**The good news:** `CommandCatalog.GoToGraph` **is** registered
(`BlueprintDocumentFactory.RegisterGoToGraphCommand:788`) and already does the canvas switch via
`BlueprintGraphSwitcher`. So jump-to-definition is *resolve `FunctionCallNode.TargetGraphId` → 
`switcher.SwitchTo`* — genuinely `WIRING` once the gate is addressed.

**Fix:** replace the hardcoded id list with a host-supplied capability predicate (the id list and the
`"ScaleBy"` title test both belong to the Demo, not to shared UI), then register
`editor.go-to-definition` alongside `GoToGraph`. ⚠ Touching `CanvasRenderer` means the NodeEdit UI
suite is a gate for this one.

<a id="bp-77"></a>
### BP-77 — My Blueprint's "Macros +" button is live and does nothing 🔴 **[NEW — functions audit, 2026-08-06]**
**Complexity:** WIRING · **Confidence:** ✔✔

`BlueprintMyBlueprintModel:59` declares the Macros section with `canCreate: true` and the create
command `"editor.create-macro"`. **Nothing registers that id** anywhere in `Hrot/`. The section's item
list is hardcoded to `Array.Empty<MyBlueprintItem>()` — *"faked/empty v1"* (`:116`).

This is the **BP-60 shape** (a live affordance whose command has no handler) and it is **visible to a
designer right now**, independent of whether macros are ever built.

**Fix:** it resolves either way — implement it as part of `BP-78`, or hide the section until macros
exist. ⚠ Do **not** leave a live button with no handler; that is the defect.

<a id="bp-78"></a>
### BP-78 — Macros: **design** **[scoped 2026-08-06 · design CLOSED 2026-08-07]**
**Complexity:** RW-H · **Confidence:** ✔✔

> ✅ **DONE (2026-08-07) — design only.** [Q25](Architect_Question_25_Macros.md) answered all five
> sub-questions in a **self-researched** round (no NotebookLM; same footing as Q23 and Q24, and
> recorded as such in the doc's Answers section):
> **A1** own `GraphKind.Macro` · **B1** new `Stage2_5_ExpandMacros` between Validate and Normalize ·
> **C1** asset-local now, macro libraries their own round · **D3** one exec-in / **N ≥ 0** exec-out ·
> **E** four rails kept, **two added**, one dropped as already-handled.
> **Implementation is [BP-79](#bp-79)…[BP-83](#bp-83)**, in that order. Nothing is built under this ID.

> ⚠ The register previously listed macros as **out of scope — "absent from the entire codebase; new
> capability, architect round required"**. The round was indeed required, but **"absent" was
> wrong** — see the scaffolding table in
> [Q25](Architect_Question_25_Macros.md#ground-truth-verified-against-code-2026-08-06). Fifth and
> sixth overturned "nothing exists" claim.

#### ⭐ Why macros are worth building *here* — not "Unreal has them"

**`BP1650`** (`Stage2_Validate.cs:2150-2166`): *"A function graph invoked by FunctionCall must not
contain latent nodes; latent execution is only supported in the top-level Tick/event graphs."*

A function compiles to a plain `static` C# method, so it structurally cannot contain a `Delay` or a
`WaitForChannel`. A **macro inlines**, so a latent node inside one lands where the cursor already
lives.

⇒ **A macro is currently the only possible way to factor out a reusable *latent* sequence**
(*aim → wait 0.4s → fire*). Today that must be copy-pasted at every call site, and no amount of work
on functions can ever fix it. Multiple exec out pins are the secondary payoff, and are likewise
only expressible by inlining — a C# method has one entry and one return.

> ⚠ **Correction (2026-08-07).** This section previously said the `BlueprintLatentCursor`/resume-block
> machinery *"exists **only** for the top-level graph"*. **That claim is false.**
> `InstanceLowering.cs:16-21` applies `WaitLowering_Instance` to **every** graph in the asset that
> contains a latent op, and `Func_X` does receive `ref State s` (`InstanceEmitter.cs:283-291`) — so
> the cursor is reachable from a function body. The real reason BP1650 must exist is narrower:
> `State` holds exactly **one** `Cursor` field (`InstanceEmitter.cs:109`) — one resume slot per
> instance, not per call frame — and suspension is expressed as an early **`return`**
> (`WaitLowering_Instance.cs:109`). A suspending `Func_X` would clobber the caller's single shared
> cursor *and* return to a caller with no way to learn it suspended. Fixing that for functions needs a
> cursor **stack** plus suspend propagation through every call site.
> **The justification is unchanged in substance and stronger in force:** macros avoid the problem by
> construction instead of paying for it. Found by the Q25 answers round, which was explicitly required
> to test this claim rather than inherit it.

#### What exists / what does not

Scaffolding (command ids, `GraphCommand.CollapseToMacro`, the `Macro.Call` gate references, the
rendered My Blueprint section) is listed in Q25. **Missing:** `GraphKind.Macro` — the enum is
`{ Function, Event, Construction }` (`Assets/GraphTypes.cs:24`) — any expansion pass, and any handler
for `editor.create-macro` (`BP-77`).

⚠ **`FakeCommandSink`'s macro/function collapse is a scenario prop** with S22's pin names hardcoded;
it is not prior art for the semantics. See [BP-74](#bp-74).

---

<a id="bp-79"></a>
### BP-79 — `GraphKind.Macro` + the fail-loud net **[from Q25 · lands FIRST]**
**Complexity:** RW-L · **Confidence:** ✔✔ 🔴

Add the enum member to `{ Function, Event, Construction }` (`Assets/GraphTypes.cs:24`). It serialises
as a **string** — `BlueprintJsonServices.cs:26` registers `JsonStringEnumConverter` — so appending is
additive on disk.

**Then close the two silent-failure holes, before any expansion code exists.** This ordering is the
whole point of the item: it converts every later bug in BP-81 from a silent miscompile into a build
error.

| Hole | Where | Why it is silent |
|---|---|---|
| `GraphKind → IrGraphKind` ends in a catch-all | `Stage5_Schedule.cs:4311-4314` — `_ => IrGraphKind.Function` | A macro that survives expansion is emitted as a `Func_X` **with no diagnostic** — re-creating the exact BP1650 breakage the feature exists to avoid. **Trap #5** (a `default:` arm that reports success), which already bit BP-18, BP-60 and BP-74 |
| Tick-graph selection falls back to "first Function graph" | `InstanceEmitter.cs:81-82` — `?? FirstOrDefault(g => g.Kind == Function)` | A macro must never be eligible. Free under A1's separate enum member — but it needs an explicit test, because a later refactor could quietly make it eligible again |

⭐ **This pair is why Q25 answered A1 (own `GraphKind`) rather than "a flag on a Function graph".**
Under the flag design a macro *could become the tick graph*. The 40 existing
`== GraphKind.Function` filters in the compiler then fail correctly and by default.

<a id="bp-80"></a>
### BP-80 — Macro authoring surface **[from Q25 · closes BP-77]**
**Complexity:** RW-M · **Confidence:** ✔✔

Create / rename / delete a macro graph, plus:

- **`Input` / `Output` boundary nodes** with pin projection for one exec-in and **N ≥ 0** exec-out.
  Mirrors `EventEntryNodePins`' one-pin-per-declared-thing loop (`NodePinSchema.cs:259`) — which today
  emits exactly **one** exec-out, so the N-exec-out projection is genuinely new, but small.
- ⚠ **Both pin projections move together** — editor `NodePinSchema` **and** compiler
  `Stage0_Rehydrate`. They are the two halves that must agree; every batch that touched one and not
  the other produced a silent shape mismatch.
- **Graph Signature window** coverage (BP-72 precedent).
- **A real handler for `editor.create-macro`** ⇒ **closes [BP-77](#bp-77)**, whose "+" button is live
  and does nothing today.
- **Palette entry + drag** — same fix shape as [BP-75](#bp-75).

<a id="bp-81"></a>
### BP-81 — `Stage2_5_ExpandMacros` — the expansion pass **[from Q25-B]**
**Complexity:** RW-H · **Confidence:** ✔✔

A new stage between Validate and Normalize, with **one** call site: `BlueprintCompiler.Compile` is a
literal statement sequence (`:54-77`), and `Stage3_Normalize.Run` already returns a **new** asset, so a
stage that rewrites the node set has a precedent to copy.

**Work:** clone the macro body per call site with a fresh-Guid remap · splice boundary links into the
host graph · stamp `OriginNodeId` on every synthesized node.

✅ **The cloning half already exists — reuse it.** `BlueprintClipboard.Rehydrate`
(`Hrot.Blueprints.Editor/Host/BlueprintClipboard.cs:129-184`, shipped by [BP-23a](#bp-23a)) is
substantially the required transform: a JSON deep-copy through `[JsonPolymorphic]` that is independent
per call, fresh **node and pin** GUIDs via two maps, internal links remapped, and the denormalised
`Pin.LinkedToIds` mirror remapped too (`:159-164` — leaving stale ids there "would make a pasted node
claim wires it does not have", which applies verbatim to an expanded node).

Two real deltas, and they are the actual scope of this item:

1. **Boundary links must be *rewired*, not dropped.** `Rehydrate:171-174` `continue`s on any link
   whose endpoint is outside the fragment. Expansion needs precisely those links spliced onto the host
   graph — the macro's `Input`/`Output` boundary nodes collapse away and their outside-the-fragment
   endpoints reconnect to whatever the call node was wired to. This is the part with no prior art
   (and the part `FakeCommandSink` also skips — see [BP-74](#bp-74)).
2. ⚠ **Assembly direction.** `BlueprintClipboard` lives in `Hrot.Blueprints.**Editor**`; Stage 2.5 lives
   in `Hrot.Blueprints.**Compiler**`, and the dependency runs Editor → Compiler, not the reverse. The
   remap logic must move **down** into the compiler assembly (with the editor then calling it), or be
   duplicated. This repo has done both — BP-69 duplicated `ResolveCustomEventDecl` across this exact
   boundary. **Prefer moving it down**, so the two paths cannot drift.

⚠ `BlueprintCommandSink` implements no Collapse case — see [BP-74](#bp-74).

✅ **Verified cheaper than the question assumed.** Because the call node is **gone** before Stage 5:

- `GetSingleExecSuccessor` (`Stage5:3628`) returns null for `execOutPins.Count != 1`, and
  `ReportDroppedExecSuccessors` raises **BP1412** — *"a node type with multiple exec-out pins, e.g.
  Sequence, is not yet schedulable"*. **Neither ever sees a macro.** N exec-outs become N ordinary
  host-graph links, so they cost the scheduler nothing.
- `ComputeMergePoints` (`Stage5:4269`) already handles exec in-degree ≥ 2 generically, allocating one
  shared block per join.

**Why this seam and not Stage 0:** `BlueprintCompiler.Validate()` (`:108-123`) runs **Stage 2 alone**
as the editor's live validator. Under B1 the editor validates macros exactly **as authored**; expanding
earlier would red-underline synthesized nodes the designer never placed.

<a id="bp-82"></a>
### BP-82 — Macro guard rails (Stage 2) **[from Q25-D/E]**
**Complexity:** RW-L · **Confidence:** ✔✔

| Rule | Approach |
|---|---|
| No recursion, direct or mutual | **Reuse, don't build.** BP1654 (`Stage2_Validate.cs:2173+`) is already a three-colour DFS over the FunctionCall graph; the macro check is the same algorithm over macro-call edges |
| Expansion depth cap | Counter in the pass — a macro calling a macro 20 deep is a build-time bomb |
| No macro-local state | An error **from day one**. Moot until BP-57 lands function locals, but retrofitting a rail later is exactly how BP-60-shaped holes appear |
| Multi-**entry** macros rejected | `Stage5:204-208` records that the merge machinery is deliberately **not** applied at Sequence-branch or When-arm roots ("its fall-through continuation is position-dependent, which a shared block cannot express"), so a body entered from two places is not uniformly safe. D3 → D1 stays additive with no asset migration |

<a id="bp-83"></a>
### BP-83 — Macro debug provenance: arm a breakpoint at **every** expansion site **[from Q25-E]**
**Complexity:** RW-M · **Confidence:** ✔✔

`OriginNodeId` is confirmed live, but it is a **fallback**: `CSharpEmitter.cs:45,53` read
`debug?.NodeId ?? debug?.OriginNodeId`.

And `DebugMapBuilder.RecordNodeStart` (`:99`) ignores a re-open while a node id is already open, with
`RecordNodeEnd` (`:103`) closing it. So one designer node expanded at two call sites yields **two
`DebugMapEntry` rows — same `NodeId`, different line ranges**:

- **line → node** stays 1:1 ✅
- **node → line** becomes **one-to-many** ⚠ — the breakpoint path must arm *all* of them, and the
  watch panel must resolve which frame it is looking at.

This is the rail Q25 flagged as *"the one that will bite if it is skipped"*: eighteen batches went into
making failures attributable, and an expansion pass that loses provenance reintroduces the
error-with-no-explicable-source shape that BP-69, BP-71 and BP-73 each ended in.

✅ **Checked and cleared — not a hazard:** `IrOp_WriteCursorResumeAt(k + 1)` numbers resume points by
**block-list position** (`WaitLowering_Instance.cs:89`), so adding a macro call renumbers every
downstream resume point. But that changes `StructureHash` ⇒ **hard** reload, and
`LatentCursorReloadTests` documents *"hard reload resets cursor to ResumeAt=0"*. No new rail needed.

<a id="bp-84"></a>
### BP-84 — Undo after deleting a `GetVariable` node restores it **without its output pin** 🔴
**Complexity:** RW-L · **Confidence:** ✔✔ *(user-reproduced; cause narrowed, not yet pinned)*

> 🔎 **The first defect found by the batches-9–18 visual check (2026-08-08)** — exactly the class the
> eight green suites cannot see.

**Reproduction** (user, on a `SquadState1` created from the `SquadState` recipe):

1. Open the `GetThreatLevel` Function graph. The `GetVariable` node correctly shows its **`Value`
   output pin**.
2. Delete that node.
3. **Ctrl+Z.**
4. ⇒ The node comes back **without the `Value` pin**. Nothing can be wired to it.

**Why this is 🔴 and not cosmetic:** the node still serialises, so saving here persists a graph whose
`GetVariable` can never be connected — a silent, durable break from a single Ctrl+Z.

#### Cause: narrowed to the view layer

Two obvious suspects are **already ruled out**, so do not start there:

| Suspect | Ruled out because |
|---|---|
| The undo lost the node's data (kind / `VariableId`) | `DeleteNodeCommand.Undo` (`GraphEditor/GraphCommands.cs:57-62`) re-adds the **same `Node` object** it removed. Nothing is reconstructed, so nothing can be lost |
| The pin projection failed to resolve the variable | `NodePinSchema.GetVariablePins` (`:670-679`) returns **one `Value` out-pin unconditionally** — even an unresolvable id falls through to `ResolveVariableTypeId` → `System.Object` and still yields a pin. It cannot return zero pins |

⇒ The fault is **downstream of both**. Two hypotheses were put up:

1. **The node view-model is not rebuilt after undo** — the asset is correct but the canvas renders a
   stale/empty pin list. *(Local fix.)*
2. **Node *order* is not restored.** `Undo` does `_graph.Nodes.Add(_node)` — an **append**. Contrast
   `LinkEditCommand` immediately below it, which restores links *at their original indices* with the
   explicit comment *"order matters for positional-projection assets"*. A recipe-loaded asset carries
   `"Pins": []` and is rehydrated in Stage 0, where `AssignDirection` falls back to **positional**
   binding. *(Would be far broader — any pin-less asset after any undo.)*

#### ✅ Discriminated 2026-08-08 — **hypothesis 1**

The user added a second `GetVariable`, wired it, deleted the first, and pressed Ctrl+Z:
**only the restored node lost its pins.** The sibling node's pins and wires were unaffected.

⇒ **Node ordering is not implicated, and the blast radius is one node, not the whole graph.** The fix
is a view-model rebuild (or pin-list invalidation) on the undo path — `RW-L`, as tagged. Do **not**
reorder `DeleteNodeCommand.Undo`'s `Nodes.Add`; that was the expensive hypothesis and it is now ruled
out.

⚠ Still worth one check during the fix: whether the broken state **persists to disk** or clears on
reopen. It serialises either way (pins are stripped on save and re-projected on load), so a reopen
almost certainly heals it — which would downgrade the *data-loss* framing above to a
render-until-reopen defect. Confirm rather than assume.

#### ✅ DONE 2026-08-08 — **both hypotheses were wrong; the real cause is node-type substitution**

**Hypothesis 1 is also refuted.** Nothing about the view model is at fault: the *asset* is wrong after
the undo. What actually happens:

The canvas does **not** undo a delete by calling `DeleteNodeCommand.Undo`. `EditCommands.BuildDeleteSelection`
(`NodeEditor.UI/Action/EditCommands.cs:108-126`) records the gesture on the editor's `UndoStack` as a
forward/inverse **command pair**:

| | command |
|---|---|
| forward | `GraphCommand.RemoveNodes(nodes)` |
| inverse | `GraphCommand.AddNode(n.Id, n.Kind, n.Position, { "PinIds": … })` |

So Ctrl+Z arrives at the sink as an **`AddNode`**, and the node is **rebuilt from its kind string**.
That string is `BlueprintNodeModel.Kind` = **`node.GetType().Name`** (`BlueprintNodeModel.cs:105`) —
i.e. `"GetVariableNode"`. But `BlueprintCommandSink.IsGetVariableKind` accepted only the *palette* ids
(`Util.GetVar` / `Variable.Get` / `Blueprint.GetVariable` / `GetVariable`), and no `NodeKindRegistry`
entry is keyed by the type name either. The reconstruction therefore fell through to the generic
fallback:

```
FunctionCallNode { MethodName = "GetVariableNode" }   // exec in/out, NO data pin
```

which is exactly the reported symptom — and answers the open question above: this is **not**
render-until-reopen. `_markDirty` runs, so **saving persists a node of the wrong type**. The
*data-loss* framing was right, for a different reason than anyone guessed.

It also explains the sibling experiment cleanly: only the node that goes through reconstruction is
damaged, because only it is rebuilt.

⚠ **Second, independent loss on the same path:** the inverse carries **no node properties at all**,
only `PinIds`. Even a correctly typed reconstruction would lose `VariableId` / `ValueJson` / `EventId`
/ `MethodName` / pin defaults. A kind-matching fix alone would give back a `GetVariableNode` bound to
nothing.

**Fix (both halves).** `BlueprintCommandSink` now keeps a bounded, id-keyed **tombstone** of the nodes
`ApplyRemoveNodes` removed, and `CreateAssetNode` restores the *original object* when an `AddNode`
names one and the requested kind still denotes that node's type. That preserves every property for
every node kind, not just Get/Set-variable. `IsGetVariableKind` / `IsSetVariableKind` additionally
accept the type-name form, so a reconstruction that misses its tombstone still yields the right node
*type*.

**Tests** — `Hrot.Blueprints.Tests/Host/UndoRestoresNodeWithPinsTests.cs`, driving the real
forward/inverse pair through the sink. One test pins the kind string the canvas emits, so a change to
`BlueprintNodeModel.Kind` cannot silently re-open this. **3 of 4 go red on revert** (the fourth is the
kind-string documentation test, which is supposed to hold either way).

⚠ **Design note worth an architect nod:** the tombstone is a Blueprint-side workaround for a
**generic** gap — `INodeModel` exposes no property bag, so `EditCommands` (shared FDP UI) *cannot*
build a lossless inverse for any host. The BTree and HSM sinks have the same exposure. The principled
fix is a node-state snapshot on the command pair; that is a shared-layer change and was not taken here.

<a id="bp-86"></a>
### BP-86 — Function input parameters render with **corrupted names** on the entry node 🔴
**Complexity:** RW-L · **Confidence:** ✔✔ *(observed; four layers eliminated, culprit not yet pinned)*

Add three input parameters to a Function graph's signature and **rename them to something shorter than
the default** — the user typed `P1`, `P2`, `P3` over the generated `Param0`, `Param1`, `Param2`. The
entry node's pins then read:

| Typed | Persisted / rendered |
|---|---|
| `P1` | **`P1␀am0`** — shown as `P1?am0` |
| `P2` | **`P2␀am1`** — shown as `P2?am1` |
| `P3` | **`P3␀am2`** — shown as `P3?am2` |

#### ✅ Root cause — confirmed, `GraphSignatureWindow.cs:343-345`

```csharp
var newName = System.Text.Encoding.UTF8
    .GetString(nameBuf)      // ← decodes ALL 256 bytes
    .TrimEnd('\0');          // ← strips only TRAILING nulls
```

`nameBuf` is a 256-byte buffer seeded with the *old* name. `ImGui.InputText` writes the new text plus a
terminating `\0` **and leaves the remainder of the buffer untouched.** So typing `P1` over `Param0`
gives:

```
offset:  0    1    2    3    4    5    6 ...
before: 'P'  'a'  'r'  'a'  'm'  '0'  \0      ← "Param0"
after:  'P'  '1'  \0   'a'  'm'  '0'  \0      ← InputText wrote "P1\0", bytes 3-5 survive
                  ↑ terminator — everything past it is stale
```

`GetString` over the whole buffer yields `"P1\0am0\0\0…"`, and `TrimEnd('\0')` removes only the
*trailing* nulls — leaving **`"P1\0am0"`**, with the interior `\0` intact. The canvas renders that
embedded null as `?`.

⇒ **The bug fires whenever the new name is SHORTER than the old one**, and the visible residue is
exactly the old name's tail. My earlier reading of this — "two bytes overwritten by *(row index + 1)*" —
was **wrong**: the incrementing digit is not a row index at all, it is the trailing digit of the
default name `Param{i}` showing through. The correct diagnosis only became possible once the user said
they had typed `P1`/`P2`/`P3` themselves.

#### 🔴 It is persisted, not render-only

`RenameParameter(param.Name, newName)` writes the mangled string straight into `ParameterDecl.Name`, so
it round-trips to `.bp.json` and reaches the compiler as an **identifier containing a NUL**. This is
data corruption, not a display glitch.

#### The fix — truncate at the FIRST null

```csharp
int len = Array.IndexOf(nameBuf, (byte)0);
if (len < 0) len = nameBuf.Length;
var newName = System.Text.Encoding.UTF8.GetString(nameBuf, 0, len);
```

#### ⚠ Systemic — **seven sites**, same shape

`TrimEnd('\0')` after decoding a whole ImGui buffer is a repeated idiom in this codebase. Every one is
the same latent defect, and each corrupts as soon as a user shortens an existing value:

| # | Site |
|---|---|
| 1 | `Hrot.Blueprints.Editor/Windows/GraphSignatureWindow.cs:345` ← **this bug** |
| 2 | `Hrot.Hsm.Editor/Windows/HsmEventsWindow.cs:110` |
| 3 | `Hrot.Editor.AiShared/Windows/InspectorWindow.cs:231` |
| 4 | `Hrot.Editor.AiShared/Blackboard/VariablesPanelControl.cs:561` |
| 5 | `Hrot.Editor.AiShared/Blackboard/VariablesPanelControl.cs:562` |
| 6 | `Hrot.Editor.AiShared/Blackboard/VariablesPanelControl.cs:640` |
| 7 | `Fdp.Presentation/ImGui/Panels/ImGuiFileDialogService.cs:184` |

Sites 4–6 add `.Trim()`, which does **not** help — `Trim()` does not remove an interior NUL either.
Fix all seven behind one shared helper rather than patching site 1 alone; this is trap #5's lesson in a
different costume (an idiom that looks defensive and isn't).

#### ✅ DONE 2026-08-08

Re-derived the site list from source rather than trusting the table: an independent sweep for
`TrimEnd('\0')` across **both** trees returns exactly these seven — no more, no less.

**One helper serves all seven, contrary to the handoff's guidance.** The handoff suggested
`Hrot.Editor.AiShared` for sites 1–6 with a separate copy for site 7, reasoning that the dependency
runs Hrot → FDP. It runs that way — which is *why* a single helper works: put it in **FDP**, the lower
layer, and Hrot can use it. Confirmed by inspection that all three Hrot editor assemblies
(`Hrot.Blueprints.Editor`, `Hrot.Editor.AiShared`, `Hrot.Hsm.Editor`) already reference
`Fdp.Presentation`, and site 7 lives there. No duplication was needed.

New: `Fdp.Presentation/ImGui/Utils/ImGuiBufferText.cs` — `Decode(byte[]?)` / `Decode(ReadOnlySpan<byte>)`
stop at the **first** NUL; `DecodeTrimmed` exists as a distinct method so the whitespace trim that
sites 4–6 wanted cannot be copied without the NUL handling coming along.

**Tests** — `Fdp.Presentation.Tests/ImGui/ImGuiBufferTextTests.cs` (13, one per *behaviour* not per
site: shorter / longer / equal-length, empty, null, all-zero, unterminated, multi-byte UTF-8, and two
that assert the **old idiom is still wrong** on the same input so a future "simplification" back to
`TrimEnd` cannot pass) plus `Hrot.Blueprints.Tests/Editor/GraphSignatureNulCorruptionTests.cs` (4,
the designer's gesture end-to-end through `RenameParameter`, asserting the stored `ParameterDecl.Name`
carries no NUL). **8 of 17 go red on revert**, including **all four** round-trip tests.

✅ **Confirmed in the running editor (2026-08-08):** three inputs added to `Count4`'s `Tick` graph,
renamed `Param0` → `P1`. The row reads exactly `P1`. Persistence is covered by the round-trip test
rather than by saving, so no tracked demo asset was dirtied.

<a id="bp-85"></a>
### BP-85 — The canvas never says which graph you are editing
**Complexity:** RW-L · **Confidence:** ✔✔

> 🔎 Found by the same visual check. Not a crash, but it produced a **false data-loss scare**, which is
> the expensive kind of UX bug.

The canvas tab shows only the **asset** name (`SquadState1`). So when the user pressed **Functions +**
and the canvas correctly switched to the new empty graph, it read as *"the graph tab has been emptied
and replaced with a single Event node"*. The switch was right; the absence of any label made it look
like destruction. Their words: *"Not easy to see in what function graph we currently editing."*

Compounding it: nothing on the canvas states the asset's **dispatch** either, so *"Not even sure what
`SquadState1` actually is — is it an Instance blueprint?"* is a fair question with no on-screen answer.

**Fix:** show the active graph's **name and kind** in the canvas tab or a breadcrumb, and the asset's
dispatch alongside the asset name. Cheap: BP-72 already routes canvas-switch events to the Graph
Signature window via `BlueprintGraphSwitcher`, so the signal this needs is already live.

**Related, same root (discoverability of the signature):** a Function graph with **zero declared
outputs** gives its `Return` node no value pin — correct behaviour, but the user hit it as *"no data
input pins — nowhere to connect the return value"*. There is no hint that outputs are declared in the
**Graph Signature** window first. Surfacing the active graph's signature summary on the canvas would
answer both complaints at once.

#### ✅ DONE 2026-08-08

A breadcrumb line now sits directly under the canvas tab:

```
Count4 · Instance  >  Tick (Function)
```

Three parts, three changes:

1. **`BlueprintGraphModel.Kind` was a lie.** It was a `{ get; } =` initialised **once** to
   `("EventGraph", "Event Graph", …)`, so *every* graph reported itself as an event graph regardless of
   `Graph.Kind` — a Function graph included. Now computed from `_graph.Kind`. It has to be computed
   rather than stored because BP-24's `Retarget` swaps `_graph` in place on a canvas switch, so a
   captured descriptor would keep describing the graph the model was constructed with.
   ⚠ `AllowsLatent` / `RequiresEntryNode` deliberately left unchanged for every kind — those carry
   editor *semantics*, and varying them per graph kind is a behaviour decision, not this display fix.
2. **Dispatch**: new optional `IAssetSubtitleProvider` in `Hrot.Editor.AiShared/Identity`, mirroring
   the existing `IAssetIconKeyProvider`. `BlueprintFileAsset` implements it from the `Dispatch` field
   the header reader **already parses** for the icon, so no extra I/O. Kind-agnostic: BTree/HSM assets
   don't implement it and simply get no dispatch segment.
3. **`AiGraphCanvasWindow.BuildBreadcrumb`** — a pure static so the format is testable headlessly.

⚠ **Caught only by running the editor:** the first version used `▸` (U+25B8) as the separator and it
rendered as **`?`** — that glyph is not in the ImGui font atlas (the same reason `? EDIT` and
`? Enter Preview` show elsewhere in this UI). Switched to ASCII `>`; `·` does render and was kept. A
test now asserts every character stays below U+2000, so the next person cannot reintroduce it. This is
precisely the class of bug no headless test in this repo can see — the argument for the visual check,
made again.

**Tests** — `Hrot.Editor.AiShared.Tests/Windows/CanvasBreadcrumbTests.cs` (9, driving the real
`BlueprintGraphModel` so the kind label is the one the canvas actually shows). **3 go red on revert.**

✅ **Confirmed in the running editor (2026-08-08)** — screenshot evidence; `Tick` is correctly labelled
`(Function)`, which the pre-fix build would have called `Event Graph`.

<a id="bp-98"></a>
### BP-98 — The asset browser gives no signal of what a blueprint *is*
**Complexity:** RW-L · **Confidence:** ✔✔

> User: *"the SquadState1 appears among blueprints with the usual instance blueprint icon (nothing
> indicating a function blueprint)… I am not even sure what SquadState1 actually is — is it an instance
> blueprint?"*

Every blueprint renders with the same icon regardless of `Dispatch`, so a behaviour asset and a pure
function library are indistinguishable until opened — and even then, only by inference. Unreal gives
**Blueprint Class**, **Blueprint Function Library** and **Blueprint Macro Library** distinct asset
icons in the Content Browser.

**Fix:** dispatch-derived icon plus a subtitle. `IAssetSubtitleProvider` already exists (added in batch
19 for [BP-85](#bp-85)) and `IAssetIconKeyProvider` is its sibling, so the plumbing is in place.

⚠ **Depends on [BP-92](#bp-92)** — while `BlueprintNewAssetService:96` forces every asset to `Instance`,
a dispatch-derived icon would correctly show every asset as the same thing. Do BP-92 first or this
ships a label that is accurate and useless.

<a id="bp-99"></a>
### BP-99 — My Blueprint has no search box
**Complexity:** RW-L · **Confidence:** ✔✔

Unreal's My Blueprint panel opens with a search field that filters **all** sections at once — the
standard way to find an item in a large blueprint. Ours has five sections (Graphs, Functions, Macros,
Custom Events, Variables) and no filter, so locating a function in a 30-graph asset means scrolling.

Purely additive: no data-model change, no compiler involvement. Genuinely low priority — listed so it
is not rediscovered as a "finding" later.

<a id="bp-100"></a>
### BP-100 — Graph kind is invisible: no icon on My Blueprint rows or canvas tabs
**Complexity:** RW-L · **Confidence:** ✔✔

The three graph kinds behave **completely differently**, and render identically as plain text:

| | Runs how | Latent? | Returns? | Shared? |
|---|---|---|---|---|
| **Event** | on tick / on an event | ✅ | ✗ | ✗ |
| **Function** | called | ❌ **BP1650** | ✅ N outputs | ✅ cross-asset |
| **Macro** | **inlined** at each call site | ✅ | ✅ | ⚠ asset-local |

A designer cannot act correctly without knowing which one they are editing — *"can I put a Delay here?"*
has three different answers and no on-screen cue.

**Fix: one colour + one letter per kind, repeated in every surface** — My Blueprint row, canvas tab,
breadcrumb, palette entry — so the mapping is learned once and reused. Extends
[BP-85](#bp-85)'s breadcrumb rather than duplicating it; see the
[functions/macros UX plan](Blueprint_Functions_Macros_UX_Plan.md) for the target layout.

⚠ Put the *latent* rule in the Macros section tooltip and the create dialog. It is the one fact that
explains why macros exist at all, and it is currently written down only in `Stage2_Validate.cs`.

<a id="bp-101"></a>
### BP-101 — No F2 / context-menu rename on panel items; double-click is the only route
**Complexity:** RW-L · **Confidence:** ✔✔

> 🔁 **Third confirmed instance of one pattern** — an action that exists but is reachable *only* by
> double-clicking an unmarked row: [BP-75](#bp-75) (function items), [BP-89](#bp-89) (the Outputs `+`),
> [BP-90](#bp-90) (blackboard variables). Each was reported by the user as *"there is no way to…"*.

Unreal renames from **F2** *and* a right-click **Rename** entry, on every panel item; double-click is an
*additional* shortcut, never the only one.

**Fix it as a convention in one pass across all panels** — a shared keybinding plus a context-menu entry
— rather than per control. Fixing them one at a time is what produced three instances; the fourth is
otherwise already being written.

⚠ Ties into [BP-90](#bp-90)'s open question: establish **why `IsReadOnly` is set for a BTree
blackboard**, since that silently swallows the rename even where the affordance exists. The two are
independent — do not let the investigation block the affordance.

<a id="bp-97"></a>
### BP-97 — No wire-time type feedback; every invalid wire is drawable
**Complexity:** RW-M · **Confidence:** ✔✔

Grepping **both** `Hrot.Blueprints.Editor` and `NodeEditor.Core` for `CanCoerce` / `CanConnect` /
`IsCompatible` returns **nothing**. There is no link type-check anywhere in the editor. A `float`→`int`
wire draws happily, saves, and surfaces as **BP1501** *"no coercion"* during an MSBuild run — in a
different project, long after the gesture.

#### ✅ Unreal's behaviour is the specification

| Case | Unreal |
|---|---|
| Same type | connects |
| **Compatible, different** | **auto-inserts a conversion (autocast) node**, with a tooltip naming the conversion |
| **Incompatible** | **an icon appears with the reason**, and the connection is **refused** |

#### ⭐ We already have the first half — server-side

`Stage3_Normalize.cs:275-292` inserts a synthesized `CastNode` on **any** link where
`ITypeRegistry.TryGetCoercion` succeeds. The decision "can these two pins connect, and via what
conversion?" is therefore **already computed and already correct** — it just happens at compile time,
invisibly, and only tells the designer when the answer is *no* (and then via a build error).

⇒ This item is mostly **surfacing an existing decision at wire time**, not new logic:

1. Expose `TryGetCoercion` to the canvas (the editor already references the compiler assembly).
2. On hover / drag, show the conversion that will be inserted — matching Unreal's tooltip.
3. When it returns false, **refuse the wire and say why**, rather than accepting it silently.

⚠ **This gates [BP-96](#bp-96).** Shipping `Truncate`/`Round`/`Floor`/`Ceil` without wire-time feedback
leaves them undiscoverable — precisely the [BP-89](#bp-89) trap, where the affordance existed and the
user could not find it. The natural pairing is Unreal's: refuse the lossy wire, and offer the
conversion node in the refusal.

⚠ Note the asymmetry this fixes: today the *silent* case is the working one (auto-cast, no feedback)
and the *loud* case is a build error in another project. Both ends are wrong way round.

<a id="bp-95"></a>
### BP-95 — One "call function" node; stop making the designer declare peers
**Complexity:** RW-M · **Confidence:** ✔✔

> User: *"if we can hide (at least from the user's point of view) the unnecessary complexity — a node
> for calling a function would call any, no matter how it is defined, as long as it is technically
> possible."*

#### What the ceremony actually is

Two node kinds exist today:

| Node | Scope | Binds by |
|---|---|---|
| `FunctionCallNode` | same asset | `TargetGraphId` |
| `CallPeerBlueprintNode` | another asset | `PeerBlueprintId` + `FunctionRef` |

The cross-asset one passes **three** gates (`Stage2_Validate.cs:905-940`):

| Gate | Diagnostic | Nature |
|---|---|---|
| Target GUID is in the consumer's `CallablePeers` list | **BP1300** | ⚠ **hand-maintained ceremony** |
| Target is among `SiblingSignatures` | **BP1301** | build-system registration |
| Named function is exported by the target | **BP1302** | ✅ real validation — keep |

#### Why only one of the three is really in the way

**Gate 2 is already effectively satisfied.** The csproj globs
`<AdditionalFiles Include="Assets\Blueprints\**\*.bp.json" />`, so every asset under the assets root is
already visible to the compiler. BP1301 fires only for assets outside that tree.

**Gate 3 is genuine** — calling a function that does not exist must fail.

⇒ **`CallablePeers` is the only real ceremony**, and it is exactly what Unreal does not have: a
Blueprint Function Library's functions appear in every Blueprint's context menu with no declaration
and no per-consumer opt-in.

#### The fix, in the order that matters

1. **Editor-maintain `CallablePeers`.** When the designer picks a function from another asset, the
   editor adds the target GUID — the same bake-on-wire pattern as [BP-12c](#bp-12c) / [BP-68](#bp-68).
   The declaration stays as a compiler-level fact (it is plausibly load-bearing for build ordering and
   incremental rebuild); it simply stops being something a human types. ⚠ Confirm the build-ordering
   assumption before removing any validation — this is the piece to check, not assume.
2. **Unify the two node kinds** behind one picker that lists local **and** peer functions together and
   resolves whichever applies. That is the user's ask, and it is only safe once (1) removes the reason
   the two kinds diverged.

⚠ Interacts with [BP-75](#bp-75) (function palette entries) and [BP-92](#bp-92) (Library dispatch —
once a Library asset is creatable, cross-asset calls become the *normal* case rather than the exception,
which raises the value of this item).

<a id="bp-96"></a>
### BP-96 — Narrowing conversions have no path; `float`→`int` is a hard error
**Complexity:** RW-M · **Confidence:** ✔✔

#### ⚠ First, a correction in the user's favour

> User: *"between int and float types — I think now these two are incompatible."*

**Half right. `int`→`float` already works, automatically and invisibly.**
`Stage3_Normalize.cs:275-292` inserts a **synthesized `CastNode`** on any link where
`ITypeRegistry.TryGetCoercion` succeeds, and `Int32→Single` **is** in the table. The designer wires it
and nothing is required of them — no node, no ceremony, no diagnostic.

**The reverse is not supported.** `Single→Int32` is absent from `CoercionTable` — the table is
explicitly a *"Slice 1 conservative set"* (`StaticTypeRegistry.cs:8`) and is **widening-only**:

```
Byte→Int32, Byte→Single, Int16→Int32, Int16→Single,
Int32→Int64, Int32→Single, Int32→Double, Single→Double
```

So `float`→`int` hits **BP1501** *"Link type mismatch … no coercion"* (`Stage4_TypeResolve.cs:218-231`)
with no suggested remedy, because none exists.

#### ✅ Unreal does precisely what the user proposed

Implicit widening on the wire, and an explicit **`Truncate`** node (rounds toward zero: `1.6 → 1`,
`-1.6 → -1`) for the lossy direction. Our half of that is already built; the explicit half is missing.

#### Work

1. **A conversion-node family for the lossy direction** — `Truncate`, `Round`, `Floor`, `Ceil`
   (round-out per the build-general rule: they share one machinery and are all plausibly wanted).
   `CastNode` already exists (`Nodes.cs:185`) and is what Stage 3 synthesizes, so the emit path is
   proven.
2. **Fixed-string coercions** — `String`→`FixedString32/64` and `FixedString64`→`FixedString32`.
   ⚠ The user has explicitly approved these as **silently truncating**. Record that as a *deliberate*
   data-loss default: it is the one place this programme knowingly accepts a silent wrong value, and
   the BP-16 lesson ("never a silent wrong value") is being consciously overridden for ergonomics.
   Worth a one-line note at the coercion site so a later reader does not "fix" it.
3. **Unsigned coercions** — see [BP-87](#bp-87); they are the gate on offering those types at all.

⚠ **The editor performs no link type-checking whatsoever.** Grepping `Hrot.Blueprints.Editor` and
`NodeEditor.Core` for `CanCoerce` / `CanConnect` / `IsCompatible` returns **nothing** — every invalid
wire is drawable and fails only at compile time, as BP1501, in another project. Adding the nodes above
without an editor-side hint leaves them undiscoverable; pair this with a "no coercion — insert a
Truncate?" affordance at wire time.

<a id="bp-10"></a>
### BP-10 — `When` → EventFired form is a stub
**Complexity:** WIRING · **Confidence:** ✔✔

> ✅ **DONE (2026-08-04).** Filtered picker over the catalog (name / display name / FQN). Choosing
> an event **adopts that entry's `TargetFieldName` in the same edit** — the field belongs to the
> event's own payload shape, so it cannot survive a change of event. The self-filter checkbox is
> offered only when the catalog says the event carries a target field; otherwise "Self" would
> silently mean "everything", the opposite of what the checkbox promises. BestEffort events are
> flagged (the compiler's BP2016 warning, surfaced at authoring time).
- `WhenNodeDrawer.cs:172-177` is `ImGui.TextDisabled`. The catalog `_eventCatalog.GetEntries()` is **already injected and called at `:175`** — the result is simply never rendered.

<a id="bp-21"></a>
### BP-21 — `When` → ValueChanged form is a stub
**Complexity:** RW-L · **Confidence:** ✔✔
- Needs a component + property picker; `ComponentFieldReflector` and the existing component pickers are directly reusable.

<a id="bp-26"></a>
### BP-26 — `When` → ConditionMet form is a stub
**Complexity:** RW-L · **Confidence:** ✔✔
- **Corrected mid-audit — this is not REAL WORK.** A complete predicate *editing* UI already exists: `FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/ReplaySearchPanel.cs` (587 lines) — 7 modes including **Compound** AND/OR trees, save/load presets — built **generically on StructEdit** (`_editService.Open(dto, type)` → `ComponentEditDrawer` + a per-type drawer dictionary incl. a recursive `PredicateValueFieldDrawer`), not hand-written per subtype.
- **Why it's cheap:** `Hrot.Blueprints.Editor` **already references `Fdp.Presentation`** (csproj line 26); `WhenNodeDrawer` already has `IPredicateCompiler` injected; `ConditionMetPayload.Condition` is already designed to hold a `SearchPredicateDto` tree as JSON.
- **Residual risk:** panel-width UI inside a narrower node drawer (layout/sizing), and swapping replay-recording sources for blueprint ones (`ComponentTypeProvider` exists).
- *(The earlier "no predicate UI exists" finding searched only `Hrot/`. `PredicateBuilderState` being orphaned and `DataBreakpointManagerPanel` being read-only were both true — wrong surface.)*

<a id="bp-27"></a>
### BP-27 — `ScoreDecision.AssetId` uneditable
**Complexity:** RW-M · **Confidence:** ✔✔
- No `UtilityDecisionDef` catalog exists editor-side, so a discovery source is needed before a picker. `Architect_Question_4_Editor_Components.md` asks this exact question and records no answer.
- ✅ **Re-check done (2026-08-04) — RW-M stands.** `UtilityDecisionDef` appears **only** in `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityDecisionGeneratorTests.cs` — there is no production catalog. `ScoreDecisionNode.AssetId` is a bare GUID string (`Nodes.cs:395`). StructEdit edits DTO *fields*; it cannot *discover assets*, so unlike BP-26 there is no reusable picker to inherit. A discovery source must be built first.

<a id="bp-09"></a>
### BP-09 — Six abandoned node kinds are advertised in the palette
**Complexity:** WIRING · **Confidence:** ✔✔

> ✅ **DONE (2026-08-04).** All 6 `Make<T>` palette blocks deleted (`CallDispatcher`, `BindDispatcher`, `PartitionElements`, `AssignRoles`, `AdvancePhase`, `AcquireSlot`). **Also removed `ArrayMake`/`ArrayGet`** — BP-16 made them a BP1420 compile error, so offering them would let a designer place a node that guarantees a broken build. Node classes are retained so existing assets still deserialize (and now fail loudly). `BcpBatch02BlueprintTests` was asserting `AcquireSlot` was present; retargeted to `Compare.Equal`.
- `CallDispatcher`, `BindDispatcher`, `PartitionElements`, `AssignRoles`, `AdvancePhase`, `AcquireSlot` have live palette entries at `BlueprintNodePaletteEntries.cs:100-105` and `:233-244`, with inviting descriptions ("Broadcast an event dispatcher to all bound listeners"), but are unlowered and compile to a silent no-op (BP4004 warning).
- Both families are **superseded by design** — dispatchers by `PublishEvent`/`EventEntry`, the squad quartet by `MemberSlotList`/`SlotRotation`.
- **Fix:** delete 6 `Make<T>` blocks. Pairs naturally with BP-16.

---

# Area C — Editor infrastructure

Document, undo, and panel plumbing.

<a id="bp-11"></a>
### BP-11 — No inspector or drawer edit is undoable ⭐
**Complexity:** RW-M *(raised from RW-L — see estimate note below)* · **Confidence:** ✔✔

> ✅ **DONE (2026-08-04).** One stack. A drawer edit is now reversed by the undo the editor actually runs, and a mixed canvas/drawer sequence undoes in the order the designer performed it. 18 new tests.
>
> **What shipped, against the approved package:**
>
> | Sub-q | Approved | Shipped |
> |---|---|---|
> | A1 one stack | ✅ | ✅ `UndoStack` is the only live recorder |
> | B1 promote onto `IEditService` | ✅ | ✅ both downcast sites deleted; 10 test doubles updated |
> | C2 transport delegate | ✅ | ✅ `EditServiceContext.RecordUndoable`, wired in `BlueprintDocumentFactory` |
> | D2 adapter first | ✅ | ⚠ **superseded — see gap 4** |
> | E1 coalescing | ✅ | ✅ `ContinuousEditCoalescer<T>`, baseline on *activation* |
>
> **Transport: R3, not the addendum's R1 or R2.** R1 (`GraphCommand.SetNodeProperty`) is *provably* unable to carry these edits — the addendum's own escape clause. Drawer edits are multi-field bakes: picking a component type rewrites `ComponentTypeFqn`, the whole `Fields` list **and** `IsManaged`; picking a function target rewrites four fields. `SetNodeProperty` is one key + one value, and its sink whitelist covers none of `Fields`/`IsManaged`/`IsPure`/`ValueJson`. R2 was the sanctioned fallback but required editing the vendored `FDP/ExtDeps/NodeEdit` tree. **R3 gets R2's expressiveness with neither cost:** `GraphCommand` is a plain `public abstract record`, so `BlueprintEditCommand(string Label, Action Mutate)` is declared in `Hrot.Blueprints.Editor` and the vendored tree is untouched. Gap 3's silent-`default:` trap is closed by adding the sink case in the same change and pinning it with a test that asserts the *delegate ran* (asserting on `Success` cannot catch it).
>
> ### ⚠ Gap 4 — the sink was double-recording (found during implementation, not in the audit or the architect round)
> **D2 as written would have produced two undo entries per gesture, then three on undo.** `BlueprintCommandSink` recorded on `CommandHistory` for *every* command — 6 `RecordPropertyEdit` sites plus 4 `_history.Execute` ones — while `UndoStack` was already recording the same gestures at the issuing site (`CanvasRenderer`, since BP-02). That was harmless only because `CommandHistory` was dead. Making it live — which is exactly what D2 asks for — would have surfaced it: `ApplyAndRecord` applies *then* pushes, so the sink's inner entry lands first, and on undo the inverse re-enters the same sink method and pushes again.
>
> **Resolution:** a sink is the **applier**; the stack is the **recorder**. The 6 property sites now apply + `MarkDirty` and record nothing; callers snapshot the previous value and issue the pair through `GraphView.Execute` (BP-02 already converted the canvas sites). `GetNodeProperty` was made `internal` so callers can take that snapshot. Pinned by `SinkAppliedCommand_PushesExactlyOneUndoEntry_NotTwo`.
>
> **Not converted, and why:** the 4 `_history.Execute` structural sites are redundant-but-harmless (those gestures are recorded on `UndoStack` by the caller) and removing them is cleanup, not a fix — left with `CommandHistory` as the documented fallback. Deleting `CommandHistory` outright stays queued as D2's "later cleanup".
>
> **Verification** (the assertion no prior test made — every one of these fails if the transport is disabled): drawer edit → `Undo()` restores · the whole multi-field bake reverts, not one field · redo re-applies · one gesture = exactly one entry · interleaved canvas + drawer edits undo in reverse chronological order · undo re-projects derived views. Non-vacuity checked by disabling the transport: **7 of 11 fail**, and only the undoability ones.
>
> Files: `Host/BlueprintEditCommand.cs`, `NodeDrawers/ContinuousEditCoalescer.cs` (both new) · `Host/BlueprintCommandSink.cs`, `Host/BlueprintDocumentFactory.cs`, `NodeDrawers/{IEditService,EditService,ComponentNodeDrawers,SharedNodeDrawers,FunctionCallNodeDrawer,LiteralNodeDrawer,PlayMontageChainNodeDrawer}.cs` · tests `Editor/{BlueprintUndoUnificationTests,ContinuousEditCoalescerTests}.cs`.
>
> **Found while fixing:** BP-63 (NodeEdit's Comment Details view commits via a raw `CommandSink.Apply`), BP-64 (2 pre-existing Windows-only reds in `Hrot.Editor.AiShared.Tests`).

- ✅ **UNBLOCKED — architect approved 2026-08-04.** Package **A1 + B1 + C2 + D2 + E1**: collapse onto NodeEdit's `UndoStack` and retire `CommandHistory`; promote `RecordPropertyEdit` + `NotifyStructureChanged` onto `IEditService`; keep `EditService` canvas-agnostic via an injected transport delegate; adapter-first migration; coalesce continuous edits into one entry on widget deactivation. Full answers + reasoning: [Architect_Question_22](Architect_Question_22_Undo_Unification.md#answers--approved-2026-08-04).
- ⚠ **Three implementation gaps found after approval** (all recorded in the Q22 addendum; none blocks the design):
  1. **`UndoStack` can't carry a `PropertyEditCommand`.** `ApplyAndRecord` takes a `GraphCommand` — a ~30-variant data-record hierarchy with **zero delegate-carrying members**. Route through the existing `SetNodeProperty` (already handled at `BlueprintCommandSink:129`) rather than adding a variant to the vendored `FDP/ExtDeps/NodeEdit` tree.
  2. **D2 fixes Tier 2 only.** Drawers never call `RecordPropertyEdit`, so re-pointing `CommandHistory` cannot capture them — the ~9 `MarkDirty` sites across 5 drawer files still need converting. That is the bulk of the work.
  3. **The sink's `default:` returns success** for unknown commands, so a new `GraphCommand` variant without a sink case would no-op *and report success* — the exact failure class BP-11 exists to remove.
- **The real shape is two undo stacks that are never bridged**, not "drawers bypass undo":

| Layer | State |
|---|---|
| `IEditService` (what every drawer holds) | exposes **only** `MarkDirty`; the undo API lives on the concrete `EditService`, reachable solely by downcast — `SharedNodeDrawers.cs:248` does exactly that |
| `EditService.RecordPropertyEdit` | **fully implemented** (real `PropertyEditCommand` apply/undo pair) — but records onto Hrot's `CommandHistory` |
| `CommandHistory.Undo`/`Redo` | **never called from any UI path** (tests only) |
| Ctrl+Z | → `view.UndoLast()` → NodeEdit's `UndoStack` — *a different stack* |

- **Net effect:** no drawer/inspector edit is undoable. Structural edits **are** (`view.Execute` → `UndoStack.ApplyAndRecord` stores a forward/inverse pair).
- ⚠ **Correction (2026-08-04):** an earlier revision said the 2 `SharedNodeDrawers` downcast sites were "written to be undoable". **They are not** — they call `NotifyStructureChanged`, which is unrelated to undo. **No drawer calls `RecordPropertyEdit` at all.** The real shape is *three* tiers: canvas edits → `UndoStack` (works); `BlueprintCommandSink` property edits → `CommandHistory` (recorded, unreachable); drawer edits → `MarkDirty` only (not recorded).
- **Approved fix, in order:** (1) make `CommandHistory.Execute` delegate to `UndoStack` via an injected transport delegate (D2 + C2) — fixes the 6 sink sites with no call-site churn; (2) promote `RecordPropertyEdit` + `NotifyStructureChanged` onto `IEditService` (B1), deleting both `as EditService` downcasts; (3) convert the **~9** `MarkDirty`-only sites across **5** drawer files to record apply/undo pairs; (4) add activation-time coalescing (E1 — snapshot on `IsItemActivated()`, commit on `IsItemDeactivatedAfterEdit()`); (5) `CommandHistory` is then dead and can go.
- **Test:** a headless assertion that a drawer edit followed by `view.UndoLast()` restores the prior value — the assertion no existing test makes.
- ⚠ **Do not simply delete `CommandHistory` today** — its `Execute()` performs the actual mutation, so it is load-bearing until step 2 lands. Bounded 64-entry ring; no leak.

<a id="bp-12a"></a>
### BP-12a — My Blueprint: drag-variable-into-graph as Get/Set is dead
**Complexity:** WIRING · **Confidence:** ✔✔

> ✅ **DONE (2026-08-04).** ⚠ **Scope corrected — the title is wrong.** The **drag**-to-canvas path
> already worked: `CanvasRenderer` accepts the drop and calls `PlaceVariableNode`. The dead route is
> the **My Blueprint context menu** (`MyBlueprintContextMenu.DrawVariableMenu` → "Get"/"Set"), which
> matters most when the panel is docked away from the canvas. Registered in
> `BlueprintDocumentFactory` against the document's view, so placement is undoable, at
> `ctx.CanvasPos` or the viewport centre (a menu invocation carries no mouse position; the graph
> origin would be off-screen). No selection places nothing, rather than a node bound to nothing.
>
> **Found while testing this: BP-65** — placement wasn't undoable *at all*.
- `editor.create-variable-get` / `-set` are invoked by the context menu but never registered. This is the most-used motion in Unreal authoring.
- Reuse the palette / `AddNode` path that already creates `GetVariableNode` / `SetVariableNode` with a baked `VariableId`.

<a id="bp-68"></a>
### BP-68 — Asset-scoped dynamic kinds created an unbound generic node 🔴 **[NEW — found in BP-12c's visual check]**
**Complexity:** WIRING · **Confidence:** ✔✔

> ✅ **DONE (2026-08-05).** `BlueprintCommandSink.CreateAssetNode` mapped any kind missing from
> `NodeKindRegistry` to `new FunctionCallNode { MethodName = kind.Id }`. Its own comment named the
> victims — *"Dynamic kind (custom event, callable peer)"* — without noticing that those are exactly
> the kinds **only the sink can bind**, because only the sink holds the asset:
>
> | Create-path | Kind id | Should be |
> |---|---|---|
> | My Blueprint → drag onto canvas | `Event.CallCustom` (+ `EventId`/`EventName` props) | `CallCustomEventNode` bound to the declaration |
> | Palette → per-asset entry | `CustomEvent.{Name}` | same |
> | Palette → callable peer | `CallPeer.{guid:N}` | `CallPeerBlueprintNode` bound to the peer |
>
> The symptom was a node with **no drawer and nothing to edit** — BP-07's picker never saw it,
> because it was not a `CallCustomEventNode` at all. `CreateDynamicNode` now resolves all three, and
> `ApplyInitialProperties` normalises whatever identity the path supplies (the panel's `evt:{guid}`
> item id, a bare GUID, or the event's name) into the canonical GUID the picker writes. An
> identity that resolves to nothing is **passed through, not blanked** — the drawer shows it as
> "unresolved", which is recoverable; an empty `EventId` looks like a node never configured.
>
> ⚠ **Why it hid for so long:** the *static* "Call Custom Event" palette entry is registry-backed and
> always worked, and until BP-12c **no asset could declare a custom event**, so neither dynamic
> custom-event path had anything to carry. The peer path was hidden behind BP-66's dead catalog.
> Three separate bugs kept the same code path unreachable.
>
> ⚠ **Undo was reported as broken too, and I could not reproduce it.** The drop already routed
> through `view.Execute`, and a test driving the exact command path — including `view.UndoLast()`,
> what Ctrl+Z calls — removes the node. Most likely the degenerate fallback node was the thing that
> looked unremovable. Worth re-checking now that a real node is created.

<a id="bp-12b"></a>
### BP-12b — My Blueprint: items cannot be renamed, duplicated, or deleted
**Complexity:** RW-L · **Confidence:** ✔✔

> ✅ **DONE (2026-08-05).** `editor.rename-item` / `-delete-item` / `-duplicate-item` registered for
> variables and custom events, each recorded on the document's undo stack via
> `BlueprintEditCommand` (BP-11's transport — these are asset-level edits, so the inverse cannot be
> a `GraphCommand`).
>
> ⚠ **The inverse is a snapshot, and it must be a DEEP one.** Rename mutates a declaration in place,
> so a shallow list copy would hold the same object in both snapshots and "undo" would restore the
> new name. Caught by the first test run.
>
> ⚠ **Renaming a custom event does three things, not one:** it renames the declaration, renames the
> paired `Event` handler graph (the compiler emits `Event_{Name}` from the *graph*), and rewrites any
> **name-keyed** `CallCustomEvent` reference. The editor writes GUIDs, which survive untouched, but
> Stage5 accepts a bare name and hand-authored assets use one — renaming the declaration alone would
> be a silent BP1407/BP1403.
>
> ⚠ **Deleting a declaration leaves its nodes in place.** They render dangling and the compiler names
> them (BP1403/BP1500), which is recoverable; silently deleting a designer's wired-up nodes because
> a declaration went away is not.
>
> Still unregistered on that menu: `editor.move-to-category`, `editor.change-variable-type`,
> `editor.show-properties`, and `editor.find-references` (BP-12d).
- `editor.rename-item`, `duplicate-item`, `delete-item` are all unregistered. Consequence: a variable can be **created but never renamed or removed**.

<a id="bp-12c"></a>
### BP-12c — My Blueprint: custom events and dispatchers cannot be created
**Complexity:** RW-L · **Confidence:** ✔✔

> ✅ **DONE (2026-08-05).** "Custom Events +" opens a create modal (name + an editable list of typed
> parameters) that calls a headless `BlueprintDocumentFactory.CreateCustomEvent`, mirroring the
> `editor.create-variable` pair exactly — quick-add overload for tests, modal overload for
> production. **Parameters are part of the create gesture**, not a follow-up: they *are* what the
> declaration is for (`NodePinSchema.CallCustomEventPins` projects one data-in pin per parameter,
> and BP-07's picker labels an event by them), and there is no post-create editor for them.
>
> ⚠ **Names are validated as C# identifiers, not just as non-blank.** `InstanceEmitter` emits
> `Event_{Name}` and each parameter becomes a C# parameter — so `On Hit`, `1st`, or `class` are
> Roslyn errors, not validation messages. The modal shows the reason and disables Confirm;
> `CreateCustomEvent` is the authoritative guard.
>
> ⚠ **The dispatcher half was removed, not wired** — the audit's own suggestion, taken. BP-09
> established dispatchers are superseded by `PublishEvent`/`EventEntry` and deleted their six node
> kinds; nothing in the editor or compiler consumes `EventDispatchers`, and **no shipped asset
> declares one**. The section is gone from `BlueprintMyBlueprintModel`; the asset field stays so
> hand-authored JSON round-trips. (The generic NodeEdit panel's right-click "+ Event Dispatcher"
> remains, disabled — it belongs to the vendored tree and its Demo model still uses the concept.)
>
> 🔎 **Found while fixing — a declaration is only half a custom event.** The *body* is an `Event`
> graph of the same name: `InstanceEmitter.EmitEventMethod` emits `Event_{graph.Name}` with one
> parameter per `graph.Inputs`, while `StatementEmitter` lowers `IrOp_RaiseCustomEvent` to a direct
> call with one argument per *declaration* parameter. Nothing checked that the two agree, so a call
> to an unhandled event produced C# that did not compile, blaming a method the designer never wrote.
> New `V_CustomEventHandlers` → **BP1407** (declared, called, no handler graph — names the graph to
> add) and **BP1408** (handler arity mismatch). **Call sites only**: declaring an event and never
> calling it stays silent, which is exactly what the new create button produces on its own — the
> paired graph needs **BP-24**, which has no editor path yet.
- `editor.create-custom-event`, `editor.create-event-dispatcher` unregistered; both sections are display-only.
- ⚠ Dispatchers are a superseded concept (see BP-09) — consider **removing** that section rather than wiring it.

<a id="bp-12d"></a>
### BP-12d — My Blueprint: `find-references` is dead
**Complexity:** RW-M · **Confidence:** ✔✔
- `editor.find-references` unregistered. Overlaps BP-25 (cross-blueprint search) — a real implementation likely needs the same multi-graph layer.

<a id="bp-12e"></a>
### BP-12e — Dead commands fail silently
**Complexity:** WIRING · **Confidence:** ✔✔

> ✅ **DONE (2026-08-04).** `InvokeCreate` now logs failures through the host diagnostics sink, and menu items / section "+" buttons whose command has no handler are **disabled with a "Not implemented" tooltip** instead of rendering as live buttons that do nothing.
- **Root cause of the whole BP-12 family's user experience.** `MyBlueprintPanel.InvokeCreate` discards the returned `EditorCommandResult` (`:288-289`) while `EditorCommandsImpl.Invoke` returns `"Unknown command"` (`:21-22`). Buttons render, click, and do nothing — no error, no toast.
- **Tally: 14 commands invoked by the panel, 1 registered** (`editor.create-variable`).
- **Fix:** surface the failure (log/toast/disable), so an unimplemented command is visible rather than mysterious.

<a id="bp-69"></a>
### BP-69 — A name-referenced `CallCustomEvent` silently loses its argument pins 🔴 **[NEW — found while documenting custom events]**
**Complexity:** WIRING · **Confidence:** ✔✔ *(reproduced against the compiler)*

`CallCustomEventNode.EventId` has **two accepted forms** and they are not treated equally:

| Consumer | GUID form | Name form |
|---|---|---|
| `Stage2_Validate.V_ValueNodeReferences` | ✅ | ✅ |
| `Stage2_Validate.V_CustomEventHandlers` (BP1407/BP1408) | ✅ | ✅ |
| `Stage5_Schedule.FindCustomEventIndex` | ✅ | ✅ |
| `BlueprintDocumentFactory.RenameItem` (BP-12b) | ✅ (untouched) | ✅ (rewritten) |
| **`NodePinSchema.CallCustomEventPins`** (editor) | ✅ | ❌ `return` on `!Guid.TryParse` |
| **`Stage0_Rehydrate.EnrichCallCustomEventPins`** (compiler) | ✅ | ❌ same |

So a name-referenced call to an event **that has parameters** shows exec-only pins in the editor and
emits `Event_X(ref s, view, ecb, self, time)` — no arguments — against a method that declares them.
Result: **CS7036 from Roslyn, with no BP diagnostic**.

⚠ **BP1408 does not catch this.** It compares the declaration's `Parameters` against the handler
graph's `Inputs`, and those agree; the mismatch is between the declaration and the **call node's
data-in pins**, which is a third list nothing compares.

The editor's own picker writes the GUID, so the shipped path is safe. The exposed cases are
hand-authored JSON and the `.WithCustomEvent("X") + CallCustomEvent("X")` shape used across the test
builders — which is precisely the shape `V_ValueNodeReferences`' comment calls *"the ordinary
authoring shape"*.

**Fix:** accept the Name form in both projections (a three-line fallback each, mirroring
`FindCustomEventIndex`), *or* narrow Stage 2 to reject the Name form outright. Do not leave the two
halves disagreeing.

---

### ✅ Resolved — Batch 17 (2026-08-06)

Both projections now resolve through a `ResolveCustomEventDecl` helper that mirrors
`FindCustomEventIndex` exactly: GUID first, then an ordinal `Name` match.

⚠ **The `WIRING` / "three lines each" estimate above was wrong — not in size, in consequence.**
Creating the argument pins exposed the next defect in the same breath. An **unwired** argument pin
goes through `ResolveDataPin`'s dummy path, which allocated an `IrValue` with **no producing
statement** — and a value is only declared in the generated C# by the statement that produces it:

| | emitted call | Roslyn | BP diagnostic |
|---|---|---|---|
| before | `Event_X(ref s, view, ecb, self, time)` | CS7036 | none |
| after the 3-line fix alone | `Event_X(..., __t0, __t1)`, no `var __t0` | **CS0103** | BP4001 *warning* |
| shipped | `Event_X(..., __t0, __t1)` with both declared | ✅ | BP4001 *warning* |

Fixing one and introducing the other is a lateral move, so `ResolveDataPin` now emits a typed
`default(T)` `IrOp_Const` statement for an unwired pin, using Stage 4's resolved pin type so a
`float` parameter gets `default(float)` rather than `default(object)`. **This hardens all ~20
`ResolveDataPin` call sites**, not just the return terminator [BP-71](#bp-71) covered.

⚠ **Trap #9 struck this item's own tests.** The first end-to-end test **passed against the bug**, for
two independent reasons: its caller graph had no exec links (so the call node was unreachable and no
call site was emitted at all), and `IndexOf("Event_OnDamaged(")` matched the method **declaration**
rather than the invocation. This was caught only by reverting the fix to check the tests went red —
they didn't. **Reverting to watch a test fail is now a required step, not an optional one.** The test
now asserts on trimmed invocation lines and pins the exact argument-less text the defect produced.

7 new tests (`BP69_NameReferencedCallCustomEventTests`); **4 go red on revert, verified**. All eight
gates green (blueprints 2842/0, 10 skipped).

<a id="bp-70"></a>
### BP-70 — The `?? Name` fallback for Event-graph identity never fires 🔴 **[NEW — found while explaining custom events]**
**Complexity:** WIRING · **Confidence:** ✔✔ *(reproduced against the emitter)*

`CSharpEmitter` keys the runtime handler table by `evtGraph.EventTypeFqn ?? evtGraph.Name`, and its
own comment states the intent: *"Fallback to name for legacy Event graphs that carry no event
identity."*

`EventTypeFqn` is copied from `EventEntryNode.EventTypeId` — declared `string EventTypeId { get; set; } = ""`.
So for an Event graph with no bus identity it is **the empty string, not null**, `??` never triggers,
and the emitted line is literally:

```csharp
[""] = DoorActorDemo_338A7C0C_Bp.Event_OnHit_Thunk,
```

**Consequence.** `BlueprintEventDispatch.ResolveTypeId("")` finds no type, falls through to
`"".GetHashCode() & 0x7FFFFFFF`, and `bus.HasEvent(thatId)` is false forever. The whole
name-keyed dispatch route the runtime documents — *"an event-type FQN, **or a custom event name**"*,
resolved via *"the FQN hash (matching the bus's custom/untyped fallback)"* — is therefore
**unreachable**. `BlueprintEventSubscriptionRegistry` indexes the same dead key.

That is the difference between "a blueprint-local custom event can also be raised on the bus by
name" and "it can only ever be called directly by `CallCustomEvent`". Today only the latter works,
which is a large part of why the feature looks redundant against Function graphs.

Two such graphs additionally collide on `""`, and the later silently overwrites the earlier.

**Fix is one line:** `string.IsNullOrEmpty(evtGraph.EventTypeFqn) ? evtGraph.Name : evtGraph.EventTypeFqn`.
⚠ Before shipping it, decide whether name-keyed bus dispatch is *wanted* — see the note under BP-24
about custom events being a strictly weaker Function graph. Turning it on makes every custom event
globally raisable by a name-hash, which is a design decision, not a bug fix.

<a id="bp-24"></a>
### BP-24 — No Function-graph create path; canvas is locked to one graph
**Complexity:** RW-M · **Confidence:** ✔✔

> ✅ **DONE (2026-08-06, Batch 15).** Architect package **Q23 A2+B2+C2+D1**
> ([decisions + full retarget audit](Architect_Question_23_Graph_Create_And_Switching.md)).
>
> **Switching (A2):** `BlueprintGraphSwitcher` retargets `BlueprintGraphModel._graph` and
> `BlueprintCommandSink._graph` (both un-`readonly`d) **in place** — the `GraphView`, undo stack,
> FindBar, `EditorCommandsImpl` and `BookmarkStore` keep their identity across a switch. `Model.Id`
> is derived from the graph id, so bookmark filtering follows automatically. The debug adapter is
> the one deliberate rebuild (it binds `graph.Id` at construction, cheap to remake).
>
> **Undo (the Q23-A sub-decision):** one per-asset stack; `UndoStack` gained optional
> `ContextProvider` (sampled at record time) + `ContextRestorer` (invoked before replaying an entry
> whose context differs) — zero changes at any `view.Execute` call site. Undo/redo auto-switches
> the canvas to the entry's graph, Unreal-style. Without this, an entry recorded in graph A would
> be replayed by the sink while it points at graph B — mutating the wrong graph.
>
> **Create (B2):** `CreateFunctionGraph` (name modal; signature edited in Graph Signature window);
> the new graph is born with the entry-indicator `EventEntryNode { EventTypeId = "" }` — the
> shipped-asset shape `FindEntryNode` looks for. `CreateCustomEvent` now also builds the body
> `Event` graph (Inputs mirroring the parameters) in the same undo entry, adopting a hand-authored
> body of the same name instead of duplicating; a non-Event graph holding the name is rejected.
> **The BP1407 loop closes for editor-created events.**
>
> **Open rule (C2):** last-viewed (session memory, `BlueprintGraphViewMemory`) → first in authored
> order. The Event-preference is gone. Per-graph viewport persists via the previously-unwired
> `Graph.EditorMetadata.ViewportX/Y/Zoom`, written on switch-away, never dirtying the asset.
> Cross-restart last-viewed is parked until anything actually composes
> `BlueprintEditorPreferences` (nothing loads that file today).
>
> **Gesture (D1):** the panel already fired `navigateToItem` on double-click of *every* row — only
> the host delegate was a no-op. Graphs/Functions rows (sections split Unreal-style) and custom
> events (→ body graph) route to `editor.go-to-graph`, which gained its first handler. Cross-graph
> bookmark jumps work for free: `BookmarkCommands.cs:59` had the whole design behind the no-op.
>
> ⚠ **Found & fixed: BP-12b rename-undo desync** — renaming a custom event renames its body graph
> and rewrites name-keyed call refs, but the snapshot undo only restored declaration lists; undoing
> left the `Event_{Name}` pairing broken (silent BP1407). `SnapshotEventNaming` restores all three.
> ⚠ **Five `graph`-capture sites audited** (model, sink, debug adapter, ToggleBreakpoint closure,
> clipboard commands) — all read through the switcher now; the captured-graph clipboard would have
> pasted into the wrong graph after a switch.
> **Scope guards:** graph rename/delete not in this slice; Construction graphs not offered.
> Tests: `GraphSwitchingTests` + `GraphCreateTests` + `UndoStackContextTests` (41 new, all
> effect-asserting; 11 go red when the wiring is disabled).

- The data + compiler layers **already support author-defined functions**: `GraphKind.Function`, `FunctionCallNode.TargetGraphId`, and real multi-graph assets on disk (`DeepNestedBlueprint.bp.json` holds 3 Function graphs). `GraphSignatureWindow` does genuine Add/Remove/Rename/Retype/Move CRUD on `Graph.Inputs`/`Outputs` and is properly wired.
- **Missing 1 — no create path:** nothing in the editor ever appends to `BlueprintAsset.Graphs` (verified: the only `Graphs.Add` hits are compiler-internal lowering).
- ⚠ **Second reason to want this, added by BP-12c:** a custom event's *body* is an `Event` graph named after it. With no graph-create path, a declared custom event can never be given one, and calling it is a BP1407 error. BP-12c ships the declaration half; BP-24 is the other half.
- **Missing 2 — no graph switching:** `BlueprintDocumentFactory.cs:130-131` binds the canvas permanently at open time via `Graphs.FirstOrDefault(g => g.Kind == Event) ?? Graphs.FirstOrDefault()`.
- ⚠ **Consequence:** in any multi-graph asset, **every graph but the first is unreachable through the UI**. The `FunctionCall` target picker can only select graphs hand-authored in JSON.
- Also fixes the My Blueprint "Graphs" section's double-click, currently `navigateToGraph: _ => { }`.

<a id="bp-72"></a>
### BP-72 — The Graph Signature window ignores the canvas, and Event-graph parameters cannot be edited at all **[NEW — found while auditing what BP-24 unblocked]**
**Complexity:** RW-L · **Confidence:** ✔✔

> ✅ **DONE (2026-08-06, Batch 16).** `AiCanvasContext` gained `Func<Guid>? CurrentGraphId` — a plain
> delegate, for the same reason `AssetRef` is an `object`: the shared assembly must not depend on one
> asset kind. `BlueprintDocumentFactory` sets it from the **switcher** (never a captured `graph`), and
> `EditorSubsystem` passes it to `Retarget`. The picker snaps when the canvas **moves** and otherwise
> leaves an explicit combo choice alone, so it does not fight the user each frame. The filter is now
> Function **+** Event; Event rows read `Name (event)` and their list is titled **Parameters**, and
> every Inputs mutation mirrors into the paired `CustomEventDecl.Parameters` (ids preserved by name)
> so BP1408 cannot fire. **Outputs are hidden for Event graphs** — a custom event returns nothing, and
> an editable list the compiler discards would be a fresh instance of what BP-71 just removed. A
> Function graph with >1 output warns inline, pointing at BP-73.
> 11 new tests, one of them driving a **real** `BlueprintGraphSwitcher` end-to-end through the same
> provider the composition root passes (trap #9: assert the halves *together*).

BP-24 gave the canvas a current graph (`BlueprintGraphSwitcher.CurrentGraph`). `GraphSignatureWindow`
predates it and was never joined up:

- **It does not follow the canvas.** `Retarget(BlueprintAsset?)` is **asset**-scoped
  (`EditorSubsystem.cs:2276`) and resets to `functionGraphs[0]`; the window then keeps its own combo
  (`GraphSignatureWindow.cs:118`). So switching the canvas to a Function graph leaves the signature
  window pointing somewhere else — the designer edits `Inputs`/`Outputs` of a graph they are not
  looking at. Q23's retarget audit classified the window fan-out as asset-scoped and needing no
  re-fire, which is true of My Blueprint / Details / Variables but **not** of this one.
- **It lists Function graphs only** — `asset.Graphs.Where(g => g.Kind == GraphKind.Function)`. A
  custom event's body is an **Event** graph, so once BP-24 auto-creates it, its `Inputs` (the event's
  parameters) are editable **nowhere**: `CustomEventCreateModal` sets them at creation, BP-12b covers
  rename only, and this window filters the graph out. Adding a parameter to an existing custom event
  therefore requires hand-editing JSON.

⚠ The two halves interact: fixing only the first would follow the canvas onto an Event graph and then
show "No Function graphs in this blueprint."

**Fix:** take the switcher (or a `Func<Graph>`) so the combo defaults to the current graph, and widen
the filter to Function **+** Event graphs. Keep the combo — an explicit override is still useful —
but seed it from the canvas. ⚠ Event-graph `Inputs` are mirrored from `CustomEventDecl.Parameters`
(BP-24's auto-create); editing one side must rewrite the other or BP1408 fires. That pairing is the
real work here, not the picker.

<a id="bp-57"></a>
### BP-57 — Per-function local variables absent from the data model
**Complexity:** RW-M · **Confidence:** ✔✔
- `Graph` has no `LocalVariables` field — only `Id, Name, Kind, Inputs, Outputs, Nodes, Links, Comments, EditorMetadata`. All variables are blueprint-scoped. A genuine **design gap**, not unwired UI. ✅ **Unblocked — BP-24 shipped** (functions are creatable, the canvas reaches them).

---

# Area D — Compiler & correctness

<a id="bp-16"></a>
### BP-16 — `ArrayMake` / `ArrayGet` produce a silent wrong value 🔴
**Complexity:** RW-L · **Confidence:** ✔✔

> ✅ **DONE (2026-08-04).** New `V_UnloweredNodeKinds` rejects both kinds with **BP1420 error** (not a warning — BP4004 still lets the build succeed). `NodeCoverageTests` re-categorised via `UnloweredNodeKinds_AreRejectedAtStage2`, following the `WaitForEventNode` precedent.
- **The most dangerous defect found:** compiles clean, returns wrong data. The pure-value fallback (`Stage5_Schedule.cs:3209-3224`) emits `IrOp_Const("default", pinType)` with **no `Diagnostics.Add` call at all** — unlike the exec-side fallback (`:1803-1804`) which does emit BP4004.
- `NodeCoverageTests.cs:105-118` documents the asymmetry verbatim.
- **Cheapest safe fix:** a Stage2 validator rejecting both kinds (~20-40 lines) turns silent corruption into a compile **error** — strictly better than BP4004's warning, which still lets the asset "succeed". No lowering required.

<a id="bp-15"></a>
### BP-15 — Four node kinds accept bad references silently
**Complexity:** RW-L · **Confidence:** ✔✔

> ✅ **DONE (2026-08-04).** New `V_ValueNodeReferences` (BP1403–BP1406). ⚠ **Two claims in this entry were wrong, corrected by the test suite:** (1) `ScoreDecision.AssetId` is **not** a parseable GUID by convention — the shipped `CombatPostureDecision` uses `3c6f9e42-5d10-6f3a-ac23-posture0000001`, so the check is non-empty only; (2) custom events resolve **by Name as well as GUID** (`FindCustomEventIndex`), so GUID-only matching rejected the ordinary `CallCustomEvent("OnFire")` shape. `Cast` checks empty only — unresolvable targets are already BP1500 via `V_TypeReferences`. Caught a real defect: an inert `CallCustomEvent` placeholder shipped in `EnumDemo.bp.json`, removed.
- No Stage2 validator for `ScoreDecision`, `ReadRankedResult`, `CallCustomEvent`, `Cast` — none appear among the 23 registered `IValidator`s.
- Template: `V_WaitNodeReferences` (`Stage2_Validate.cs:587-615`), ~30 lines each.

<a id="bp-32"></a>
### BP-32 — `When` FallingEdge deferred for ValueChanged mode
**Complexity:** RW-L · **Confidence:** ✔✔
- Live `// TODO M3` at `Stage5_Schedule.cs:862` — block structure allocated, condition logic deferred. **Falling-edge behaviours silently never fire** in that mode.
- Partially fixed since July: `ConditionMet` FallingEdge *is* implemented and tested (`WhenNodeRuntimeTests.cs:771`).

<a id="bp-33"></a>
### BP-33 — `WaitForEvent` is structurally broken
**Complexity:** RW-M · **Confidence:** ✔✔
- No `EventTypeId` satisfies both stages: Stage2 matches by short name against `BuiltInWaitPrimitiveCatalog` (`:602`), but Stage5's `BuildWaitForEventOp` never resolves it to an FQN (unlike `WaitForChannel`'s parallel path), so Roslyn always fails CS0400. Documented by a `[Fact(Skip=…)]` regression test.
- **Decide first:** repair, or delete the kind (superseded by named `EventEntry` handlers). Cheapest interim: fold into BP-16's validator.

<a id="bp-58"></a>
### BP-58 — `Cast` has no drawer and no validator
**Complexity:** RW-L · **Confidence:** ✔✔
- The **emit bug is FIXED** — `StatementEmitter.cs:283-292` now intercepts `Cast.`-prefixed calls and emits a native `(global::T)` cast. The July matrix is stale here.
- Still no drawer and no validator (validator covered by BP-15). Note `Cast` is also inserted implicitly by Stage3 Normalize, so a drawer may be low-value — confirm before building.

---

# Area E — Debug & diagnostics

Strongest area of the subsystem — several capabilities **exceed** stock Unreal. One live bug.

<a id="bp-29"></a>
### BP-29 — Blueprint conditional breakpoints silently never fire 🔴
**Complexity:** WIRING · **Confidence:** ✔✔

> ✅ **DONE (2026-08-04).** Registry now passed at all three sites. `ReplayBrowserSubsystem` had none, so it builds one and feeds the two-arg `CgfBehaviorSetup.LoadFromAiAssembly`, mirroring `CgfSubsystem`. Regression test puts the with/without-registry constructions side by side.
- **A live production bug, invisible to the test suite.**
- `PredicateCompiler`'s 3rd ctor arg `blueprintRegistry` defaults to null (`:27`), and `CompileBlueprintVariablePredicate` then returns `static (_, _) => false` (`:235-237`).
- **All three production sites omit it:** `EditorSubsystem.cs:994`, `CgfSubsystem.cs:555`, `ReplayBrowserSubsystem.cs:641`.
- So `BlueprintVariablePredicateDto` — **the exact predicate that "Add Conditional Data Breakpoint…" synthesizes** from a blueprint node — always evaluates false in the running editor.
- **Why tests miss it:** `BlueprintVariableTests.cs:70` constructs the compiler *with* the registry, proving the logic correct while the wiring stays broken.
- **Fix:** `EditorSubsystem` and `CgfSubsystem` are **one-liners** (`_blueprintRegistry` already in scope — 19 and 7 references). `ReplayBrowserSubsystem` has **no** registry field and needs plumbing first.
- **Add a regression test that constructs the compiler the way production does**, so this class of bug is catchable.

<a id="bp-01"></a>
### BP-01 — Watch panel shows raw hex bytes
**Complexity:** WIRING · **Confidence:** ✔✔
- `WatchPanelWindow.cs:54-56` renders `Convert.ToHexString(w.LastValueBytes)`.
- `BlueprintDebugSession.MarshalFromBytes` is complete, unit-tested, and already used at 4 other call sites in the same file — it decodes every primitive plus fixed-list wrappers. Swap it in and format via `BlueprintPinDefaultValue.FormatValue` for vector types.

<a id="bp-35"></a>
### BP-35 — D4 `MultiplexingProbeSink` missing
**Complexity:** RW-L · **Confidence:** ✔✔

> ✅ **DONE (2026-08-04).** New `MultiplexingProbeSink` fans one probe stream out to N observers, so an editor session and a recording sink can watch the same run. Copy-on-write sink list (lock on mutate, `volatile` array read on the probe path) — allocation-free dispatch via an index loop, which matters because probes fire per node-enter and `ProbeOverheadTests` holds the budget.
> ⚠ **The trap it would have walked into:** `DebugProbe.NewTick()` resolved the session with `Sink as IBlueprintDebugSession`. A composite is a probe sink, **not** a session — it deliberately does not implement that far larger interface (breakpoints/watches/filters) — so the cast would have failed and every session behind the multiplexer would have silently stopped receiving `OnNewTick`, quietly breaking per-frame breakpoint dedup. `DebugProbe` now fans out explicitly, with a regression test.
> `OnCollectionWriteFailed` is forwarded **explicitly** rather than inherited from its default interface implementation, which would have dropped the never-silent write diagnostic for every inner sink. Exceptions deliberately propagate (same as a directly-wired sink) rather than being swallowed — pinned by a test so it stays a decision. 11 new tests; suite 2594/0.
- `IBlueprintProbeSink` exists; needs a composite implementation + a `DebugProbe.Sink` swap so multiple debuggers can observe one run.

<a id="bp-36"></a>
### BP-36 — D5 stack-frame inspection is Blueprint-local
**Complexity:** RW-M · **Confidence:** ✔✔
- `CallFrame` / `_callStacks` / `GetCurrentCallStack` live inside `BlueprintDebugSession`. Lifting them to `IDataBreakpointManager` would let BTree/HSM/other-subsystem pauses carry a call stack too.

<a id="bp-37"></a>
### BP-37 — `LifecyclePredicateDto` by `NetworkId` unsupported
**Complexity:** RW-M *(raised from RW-L on verification)* · **Confidence:** ✔✔
- Defect confirmed: `DataBreakpointManager.cs:1025` throws `NotSupportedException`, and the surrounding comments name the intended fix.
- ⚠ **But `INetworkEntityMap` does not exist as a type** — it appears *only* in those comments. The concrete `NetworkEntityMap` lives in `FDP/Network/Fdp.Network.Cyclone/Services/NetworkEntityMap.cs`, and `Hrot.Diagnostics.Breakpoints` does **not** reference that project (its only refs are `Fdp.Core`, `Fdp.ModuleHost`, `Fdp.Toolkits`, `Hrot.Blueprints.Core`).
- So this is not "inject an existing interface": it needs an abstraction defined and wired, **or** a diagnostics→specific-network-transport project reference, which is a layering smell. **Design call first.**

<a id="bp-38"></a>
### BP-38 — D9 pause-on-Blueprint-exception
**Complexity:** RW-M · **Confidence:** ✔✔
- **Explicitly deferred by architect decision** (Debug Protocol DD §13.3, LOCKED). The soft-pause + triple-buffer rewind machinery is directly reusable; needs an interception point in generated code plus a new breakpoint shape.

<a id="bp-39"></a>
### BP-39 — D8 CLR / Visual Studio source-line debugger sync
**Complexity:** RW-H · **Confidence:** ✔✔
- No scaffolding present; would need a DAP or VS-extensibility bridge. PDB emission already exists.

<a id="bp-40"></a>
### BP-40 — Library-dispatch graphs cannot carry node breakpoints
**Complexity:** RW-H (architect decision) · **Confidence:** ✔✔
- `StatementEmitter.cs:944-951` suppresses `NodeEnter`/`PinValueChanged` probes when `!HasSelfInScope`, because entity-scoped probes reference `self`, which stateless Library functions never have.
- **Deliberate**, but a real surprise for an author who expects to breakpoint a Library graph. Resolving it means either a synthetic entity context or redefining "breakpoint" for a stateless function. **Flag to architect; do not build speculatively.**

> **Context — Universal Breakpoints (Slice-2 D1) is already built, not pending.** 128 unit + 25
> integration tests pass. Present and wired into `EditorSubsystem`/`CgfSubsystem`:
> `DataBreakpointManager` (1090 lines), `DataBreakpointSystem` (PostSimulation, `QueryDelta`-gated),
> `DebugSnapshotProvider`, **forward-snapshot rewind exactly as specified** (triple-buffer
> `_preTick`/`_postTick`/`_live` + `EntityRepository.SyncFrom`), event breakpoints via a distinct
> `EventScannerCompiler` path, ECB-deferred live edits, hot-reload auto-rebind, and a reference-counted
> zero-cost gate. Node-granular sub-tick stepping incl. **Step Back** goes beyond the original design.

---

# Area F — Runtime & state architecture

<a id="bp-30"></a>
### BP-30 — HSM-hosted AiPrimitive blueprints collide 🔴
**Complexity:** RW-M · **Confidence:** ✔✔
- **Verified asymmetry — the two hosts hold opposite halves of the solution:**

| | partition-slot mechanism | concurrent-stateful validator |
|---|:--:|:--:|
| **BTree** | ✅ 16 refs in `BTreeBridgeEmitCore` | ❌ none (only `NestedParallel`) |
| **HSM** | ❌ **0 refs**; no compose command | ✅ `CheckConcurrentStatefulSubtrees` + `CheckConcurrentSharedScopeKeys` |

- **Failure mode:** HSM-hosted AiPrimitives use the legacy fixed offset (`Blackboard1024`+8, single 8-byte `StructureHash`). Two stateful AiPrimitives on one HSM entity alternately `InitBlock`-zero and re-init each other every tick — **neither retains state**.
- **Fix:** an `HsmBridgeEmitCore` analogue of `EmitBlueprintActionThunks` plus an HSM compose command. Reuses the FNV-1a key math and `BlueprintBlackboardPartitions` verbatim — same rail, new emitter surface.
- *(BTree is fine: `ComposeAiPrimitiveAction` auto-creates a distinct `Role=State, Scope=Node` host variable per placement, so two blueprints — or one placed twice — separate correctly. Option β's Fix-1/Fix-2/`ClearBehaviorEvent` detach are all shipped and tested.)*

<a id="bp-61"></a>
### BP-61 — HSM's two concurrency validators never fire in production 🔴 **[NEW — found while scoping BP-31]**
**Complexity:** RW-M · **Confidence:** ✔✔
- **The same defect shape as BP-29, third instance in this codebase.** `HsmValidator`'s constructor takes two optional resolvers and **defaults both to inert values**:

```csharp
public HsmValidator(IActionSchemaExporter? schema = null,
    Func<Guid, bool>? isStatefulSubtree = null,
    Func<Guid, IReadOnlyCollection<int>>? sharedScopeKeys = null)
{
    _isStatefulSubtree = isStatefulSubtree ?? (_ => false);          // <-- always false
    _sharedScopeKeys   = sharedScopeKeys   ?? (_ => Array.Empty<int>());  // <-- always empty
}
```

- **Both production construction sites omit both resolvers:**
  - `HsmGraphModel.cs:43` — `new HsmValidator()`
  - `HsmAssetValidator.cs:18` — `new HsmValidator(schema)`
- **Consequence:** Rule 8 (`CheckConcurrentStatefulSubtrees`) hits `if (!_isStatefulSubtree(subtreeId)) continue;` for every candidate and emits nothing; Rule 8b (`CheckConcurrentSharedScopeKeys`) iterates an always-empty key set. **Neither rule can ever produce a diagnostic in the running editor.**
- **Why the tests miss it:** `HsmValidatorStatefulSubtreeTests` constructs the validator *with* a lambda (`isStatefulSubtree: id => id == subtreeId`), proving the rule logic correct while the wiring stays dead — exactly as `BlueprintVariableTests` did for BP-29.
- ⚠ **Not a one-line fix.** Unlike BP-29 there is no existing value to pass: **no `IsStateful` / `HasWorkingState` notion exists editor-side at all** (verified: zero hits). A resolver must first be able to answer "does this referenced subtree asset carry per-node working state?", which means resolving the asset and inspecting its state declaration. That is the design question to settle before wiring.

<a id="bp-31"></a>
### BP-31 — BTree lacks the concurrent-stateful validator HSM has
**Complexity:** RW-L · **Confidence:** ✔✔

> ⚠ **RE-SCOPED (2026-08-04) — the premise is inverted; do not build as written.**
> This entry assumed HSM has a working guard that BTree lacks. It does not: **HSM's rule is wired to always-false in production** (see **BP-61**), so neither host is actually guarded. Mirroring it onto BTree today would produce a *second* rule that can never fire.
> **BP-61 must be fixed first** — and its resolver design will determine what BTree's equivalent should even consume. `BTreeValidator` is additionally a static-method class with no constructor, so it would need an injection seam that HSM's instance-based validator already has.
- A Subtree referenced twice under a `Parallel` node is currently unguarded on the BTree side. Port `HsmValidator.CheckConcurrentStatefulSubtrees` / `CheckConcurrentSharedScopeKeys` (`HsmValidator.cs:234-325`) to `BTreeValidator`.

<a id="bp-41"></a>
### BP-41 — No test for two different AiPrimitive blueprints on one entity
**Complexity:** RW-L · **Confidence:** ✔✔

> ✅ **DONE (2026-08-04).** New proof `T39_TwoDistinctPrimitives` — **three** different AiPrimitives on one entity, 5 tests, all green.
>
> ⚠ **The audit named the wrong risk.** It reads as a slot-*key* collision worry, but keys were never in doubt: a Node-scoped key is `FNV-1a(assetId, nodeVisualId)`, so distinct placements differ regardless of which blueprint sits on them — which is exactly why T20/T35 already looked like enough. The untested thing is **per-slot sizing**: every prior test places a single `WorkingState` *type*, so a provisioner that sized every slot from the first placement would pass all of them.
>
> **What the proof varies** (the differences are the mechanism, not decoration):
>
> | | Params | WorkingState | Per tick |
> |---|--:|--:|---|
> | **A** `DemoAiPrimitiveNodes` | 4 B | 4 B `{int Ticks}` | `Ticks += 1` |
> | **B** `DemoAiPrimitiveNodesB` *(new)* | 8 B | **16 B** `{long Accumulator; int Steps}` | `Accumulator += 7` |
> | **C** `ParamDemo_CEFE162F_Bp` | 8 B | **empty** (zero-size edge) | writes `LocomotionChannel` |
>
> **C is a real blueprint-generated primitive** (from `ParamDemo.bp.json`, the one `T33` composes alone) — so the proof is not purely stand-in-on-stand-in, and it covers the empty-`WorkingState` case that `Marshal.SizeOf` reports as 1 byte.
>
> **Asserted:** 3 distinct slot keys · manifest sizes each slot from *its own* type (4 / 16 / 1) with 3 distinct `StructureHash`es · the three provisioned payload ranges are **pairwise disjoint** at their 8-byte-aligned extents · after N ticks A reads `Ticks = N` while B reads `Accumulator = 7N, Steps = N` — arithmetically distinct, so cross-talk is a wrong number, not a coincidence · C actually dispatches (`ActiveAction = 99`).
>
> **Non-vacuity checked**, not assumed: flipping `StrideB` 7 → 8 fails the runtime test and only that one.
>
> **The HSM half is not deferred — it is currently unauthorable.** BP-30 records that HSM has **no compose command** (0 partition-slot refs), so two AiPrimitives cannot be placed on one HSM at all; there is no artifact to characterize. The regression test belongs with BP-30's fix, not here.
>
> Files: `Assets/BTrees/Authoring/T39_TwoDistinctPrimitives.btree.json`, `Brains/DemoAiPrimitiveNodesB.cs`, `Demos/T39_TwoDistinctAiPrimitives_ProofTests.cs`.
- Coverage is by analogy only: `T20` uses two *hardcoded* stateful actions on the same rail; `T35` uses the *same* blueprint 3×. The scenario an author will actually hit is unproven. HSM's collision has no regression test either.

<a id="bp-45"></a>
### BP-45 — Cross-entity event dispatch (`BlueprintDeferredEvent`) absent
**Complexity:** RW-M · **Confidence:** ✔✔
- The most-cited deferred capability across the Slice-2 docs; the type does not exist anywhere. ⚠ `Blueprint_New_Node_Authoring_Guide.md` §1a describes "automatic same/cross-entity routing" **as if it were current** — that prose is aspirational (see BP-49).

<a id="bp-42"></a>
### BP-42 — Cross-entity shared-state **write**
**Complexity:** RW-M · **Confidence:** ✔✔
- Read path shipped (`BlueprintSharedState.TryGetShared<T>`); write is same-entity only. Deferred by design per `Blueprint_SharedState_GetShared_Design.md` §0 — needs `UpdateSharedSlotCommand` + an Input-phase ingress system mirroring `AssignBehaviorEvent`.

### ~~BP-46 — Generic `GetShared<T>` partition-slot accessor~~ — ❌ **REFUTED, ALREADY SHIPPED**
**Confidence:** ✔✔ (verification pass)
- The claim was wrong. `BlueprintSharedState.TryGetShared<T>(EntityRepository world, Entity self, string variableId, out T value)` exists at `BlueprintSharedState.cs:58`, and the compiler **actively emits calls to it** (`StatementEmitter.cs:188`).
- **No work required. Retained as a struck-through row so the id is not silently reused.**

<a id="bp-43"></a>
### BP-43 — Custom Events 2b: events with no backing C# struct
**Complexity:** RW-M · **Confidence:** ✔
- No `PublishRaw` / `InjectIntoCurrentBySize` / `IrOp_PublishCustomEvent` anywhere. Blocks fully designer-authored events.

<a id="bp-44"></a>
### BP-44 — Custom Events 1d: no event-definition authoring UI
**Complexity:** RW-L · **Confidence:** ✔
- Only `BlueprintEventCatalog.cs` (data/reflection) exists; no editor window to define an event.

---

# Area G — Documentation accuracy

Cheap, and currently actively misleading.

<a id="bp-47"></a>
### BP-47 — `Blueprints_Overview.md:75` marks unplaceable nodes ✅
**Complexity:** WIRING · **Confidence:** ✔✔

> ✅ **DONE (2026-08-04).** `Blueprints_Overview.md` §3: the four value ops go ✅ → ◐ with a note that they are unplaceable (BP-04); `Cast` ⚠ → ◐ (its emit bug is fixed, it just has no drawer — BP-58); `ArrayMake`/`ArrayGet` ⚠ → new **⛔** mark, since BP-16 made them a compile error. The legend now states explicitly that the marks blend the compiler and authoring axes and that the weaker axis wins.
- `Compare` / `BinaryOp` / `BooleanOp` / `Not` are marked shipped, conflating the compiler axis with the authoring axis. They cannot be placed (BP-04).

<a id="bp-48"></a>
### BP-48 — Runtime DD and Overview stale on AiPrimitive working state
**Complexity:** WIRING · **Confidence:** ✔

> ✅ **DONE (2026-08-04).** ⚠ **Citation was wrong** — it is Runtime DD **§9.6** ("Cross-AiPrimitive reconciliation"), not §13.5. Both that section and `Blueprints_Overview.md` §1 now carry a correction table: BTree provisions a real partition slot per placement (so multiple AiPrimitives separate correctly), HSM still uses the legacy fixed offset with no compose command (so they collide — BP-30). The stale "one Blueprint per entity" invariant is called out as no longer holding uniformly.
- `Blueprint_Subsystem_Runtime_Detailed_Design.md` §13.5 and `Blueprints_Overview.md` §1/§5 describe AiPrimitive working state as living only in `Blackboard1024`. True for the legacy/HSM path, **wrong for BTree-composed nodes** (partition tiers).

<a id="bp-49"></a>
### BP-49 — Aspirational prose presented as current
**Complexity:** WIRING · **Confidence:** ✔

> ✅ **DONE (2026-08-04).** The cross-entity `DispatchOrder` snippet in `Blueprint_Authoring_Examples.md` §3 is now fenced with an explicit ⛔ **NOT IMPLEMENTED** banner recording that `BlueprintDeferredEvent` has zero hits repo-wide, retained only as a design sketch, and the verdict line now separates the shipped same-entity case from the unshipped cross-entity one (BP-45).
- `Blueprint_New_Node_Authoring_Guide.md` §1a describes cross-entity routing that does not exist (BP-45). Mark clearly as future.

<a id="bp-50"></a>
### BP-50 — Trackers contradict the code
**Complexity:** WIRING · **Confidence:** ✔

> ✅ **DONE (2026-08-04).** `Blueprint_Subsystem_Implementation_Roadmap_v1.1.md` now opens with a **📜 HISTORICAL — NOT A STATUS DOCUMENT** banner pointing at the Overview and the issue tracker, and saying plainly that its M0–M12 milestones describe pre-implementation intent and do not describe the code.
- `Blueprint_Subsystem_Implementation_Roadmap_v1.1.md` is **fully superseded** (M0–M16 predate component access, collections, custom events, 50 node kinds) — label it history, not status.
- Also stale: `Custom_Events_BUILD_TRACKER.md` (3d "still remaining", shipped one commit later), `WaveCore_Slice_Design.md` (fixed CS0400 bug, un-parked asset), `Blueprint_Authoring_UX_Backlog.md` (DOC-2 "[next]", already shipped), `Blueprint_Component_Access_TASK_TRACKER.md` and `Blueprint_Editor_SaveOnClose_RESUME.md` (both flag since-fixed items as open).

<a id="bp-51"></a>
### BP-51 — DOC-3 / DOC-4 illustrated SVGs missing
**Complexity:** RW-L · **Confidence:** ✔✔
- Memory-layout schematic and lifetime timeline. DOC-1 and DOC-2 shipped.

<a id="bp-52"></a>
### BP-52 — UX-1…UX-5 authoring ergonomics unbuilt
**Complexity:** RW-M (architect first) · **Confidence:** ✔✔
- Intent-first memory picker, unify the "two doors" to shared state, progressive disclosure, in-context micro-explanations, graph-level scope badges. The backlog itself marks UX-1/UX-2 as needing an architect nod.

<a id="bp-53"></a>
### BP-53 — E6 cross-asset blueprint-action picker
**Complexity:** RW-M · **Confidence:** ⚠ **UNCLEAR — do not act on without re-scoping**
- **Partially refuted.** An action-picker mechanism *does* exist: the `[HsmActionPicker]` attribute is used throughout `Hrot.Hsm.Editor/Inspector/HsmFacets.cs` (6+ sites), and `BehaviorActionCatalog` with `ActionSchemaEntry.IsAiPrimitive` shipped (doc-sweep item I4).
- What remains unestablished is whether that picker spans **cross-asset blueprint** actions. The original claim came from a doc sweep of the *Behavior Architecture* plan, not from code.

<a id="bp-54"></a>
### BP-54 — G7 resolver-authoring UX
**Complexity:** RW-M · **Confidence:** ⚠ **UNCLEAR — do not act on without re-scoping**
- Runtime resolver support exists (`BehaviorRegistry.RegisterResolver`, `ApplyResolverOverlay`, `BehaviorRegistry.cs:247-273`). No authoring UI surfaced — but "resolver-authoring UX" is not defined precisely enough in the source doc to verify as present or absent.

> **Both BP-53 and BP-54 are BTree/HSM behavior-authoring concerns, peripheral to blueprint editing.**
> They are retained for completeness but sit outside the "make blueprint editing fully functional"
> goal. Re-scope them against the Behavior Architecture plan before treating them as actionable.

<a id="bp-55"></a>
### BP-55 — Asset-Browser delete affordance for referenced blueprints
**Complexity:** WIRING *(lowered from RW-L on verification)* · **Confidence:** ✔✔
- **The backend already exists.** `RefactorService.PreviewDelete(Guid assetId, DeleteOptions)` returns a `DeletePreview` carrying `danglingRefs` + `issues` (`RefactorService.cs:137-164`), behind `IRefactorService`.
- Verified that **every caller is a test fake** — no production UI invokes it. Only the affordance is missing.

---

<a id="bp-87"></a>
### BP-87 — The parameter/variable **type dropdown offers 8 types the compiler cannot resolve** 🔴
**Complexity:** RW-H *(the work is small; the **scope** needs an architect call)* · **Confidence:** ✔✔✔ *(reproduced from a build failure)*

> 🔎 Found 2026-08-08 while fixing BP-84/85/86 — not by the audit. The user's own
> `SquadState1.bp.json`, authored during the visual check, **fails the build**:
> `CSC : error BP1500: Pin type 'Vector3' does not resolve. [Hrot.AI.Behaviors.csproj]`

**Reproduce:** Graph Signature → add an input → set its type to `Vector3` from the dropdown → build.

**Root cause — two lists that were never reconciled.**

The Graph Signature type combo is populated from
`BlackboardTypeHelper.DefaultKnownTypeNames` (`BlackboardTypeHelper.cs:48-52`) and persists the
**bare display name** as the `TypeId` (`GraphSignatureWindow.cs:361` — `model.RetypeParameter(param.Name, typeNames[currentIdx])`).

The compiler resolves a `TypeId` in `StaticTypeRegistry.TryResolve` by exact `TypeTable` hit, plus one
escape hatch for a `global::`-prefixed FQN. Its alias entries are only
`bool, byte, short, int, long, float, double` (`StaticTypeRegistry.cs:83-89`). Vector types **are**
registered — but under their **FQNs** (`System.Numerics.Vector3`, `:38-42`), which the bare alias never matches.

| Offered by the editor | Resolves? | Why |
|---|---|---|
| `bool` `byte` `short` `int` `long` `float` `double` | ✅ | in the alias table |
| `sbyte` `ushort` `uint` `ulong` | ❌ | no entry under **either** name |
| `Vector2` `Vector3` `Vector4` `Quaternion` | ❌ | registered as FQN only; bare alias unmapped |

⇒ **8 of the 15 types the dropdown offers produce BP1500.** Nothing warns at author time; the failure
lands at *build* time, in a different project, naming a type the designer picked from a supported-looking list.

Visible in the user's asset: the recipe-authored parts carry FQNs (`System.Single`, `System.Int32`,
`System.Byte`) while the three parameters they added by hand carry `int`, `float`, `Vector3` — the
first two happen to be in the alias table, the third is not.

**Why this is not just "add 8 aliases".** Two separate questions, and the second is the architect's:

1. *Mechanical* — the vector four need alias→FQN entries (or the editor should persist FQNs). Low risk.
2. *Scope* — should `sbyte`/`ushort`/`uint`/`ulong` be legal blueprint types **at all**? They are absent
   from the compiler under any name, which may be deliberate (blackboard tier sizing, coercion table
   completeness — `CoercionTable:96-103` has no unsigned entries). Either register them or **remove them
   from the dropdown**; silently offering them is the one option that is certainly wrong.

⚠ Whichever way it goes, the durable fix is to stop having **two hand-maintained lists** — the
dropdown should be derived from what the compiler can resolve, so this cannot drift again.

#### ✅ Scope settled by the user 2026-08-08 — **register and support, do not remove**

> *"uint data type is maybe unnecessary … but the same argument can be taken for FixedString32 vs 64
> ('why not just string?') — the technical limitations justify both fixed variants. Uint/ushort is not
> an issue **as long as it can be seamlessly converted to ints** (wiring possible between
> uint ↔ ushort ↔ int pins). So no issue with a large selection of data types, they just need to be
> supported properly."*

That resolves question 2 and adds a third the entry had missed entirely.

**⭐ Strings are the bigger gap, and they are already supported by the compiler.** `StaticTypeRegistry`
registers **`System.String`** (`:35`) and **`Fdp.Core.FixedString32` / `FixedString64`** (`:62-63`,
commented *"preferred over System.String in state"*). No Stage 2 validator rejects a managed or
fixed-string type for a graph parameter — grepping `Stage2_Validate.cs` for `IsUnmanaged` returns
**nothing**. ⇒ **A function already can take a string parameter; the dropdown simply never offers one.**
`BlackboardTypeHelper.DefaultKnownTypeNames` lists 15 numeric/vector types and **zero** string types.

**⚠ The user's condition on the unsigned types is NOT met today.** `CoercionTable` (`:96-103`) has
exactly 8 entries and **every one is signed**:

```
Byte→Int32, Byte→Single, Int16→Int32, Int16→Single,
Int32→Int64, Int32→Single, Int32→Double, Single→Double
```

There is **no** `UInt16→Int32`, no `UInt32→Int64` — nothing unsigned at all. So a `uint` pin cannot wire
to an `int` pin, which is precisely the condition attached to keeping them. **Registering the aliases
alone would produce types that resolve but cannot be connected** — a worse failure than BP1500, because
it fails later and less legibly.

#### Revised work (the architect question is closed)

| # | Item | Note |
|---|---|---|
| 1 | Add `Fdp.Core.FixedString32` / `FixedString64` to the dropdown | Already registered; pure UX. **Do this first** — it is the one the user actually asked for |
| 2 | Map the bare vector aliases → FQNs (or persist FQNs from the editor) | `Vector2/3/4`, `Quaternion` |
| 3 | Register `sbyte`/`ushort`/`uint`/`ulong` in the alias table | Mechanical |
| 4 | **Add unsigned coercion entries** | The gate on item 3 — without it the types resolve but will not wire |
| 5 | Derive the dropdown from the registry | The durable fix; kills the two-list drift |
| 6 | Decide `System.String` separately | It is registered, but it is **managed** (`IsUnmanaged=false`), so it cannot live in a `State` struct — fine as a *parameter*, not as a variable. That asymmetry needs a deliberate call, and is exactly why the FixedString variants exist |

⇒ Reclassify from `RW-H` + 📐 to **`RW-M`, no architect round**. Items 1–2 are `RW-L` and independently
shippable.

⚠ **Practical note for the next session:** while this asset is present, `dotnet build` of the solution
**fails**, because the blueprint compile runs as a build step of `Hrot.AI.Behaviors`. The BP-84/85/86
work was built and gated with the file temporarily moved aside and restored afterwards; it is
untracked and was left in place.

---

# UX blockers found in the batch-19 visual check (2026-08-08)

> All five are *"there is no way to…"* / *"I could not find how to…"* reports from the user driving the
> real editor after batch 19 shipped. **Four of the five affordances exist in code and were simply not
> discoverable** — which makes them cheap to fix and expensive to leave.
>
> 🔁 **Pattern, third confirmed instance: double-click-only, no affordance, no hint.**
> [BP-75](#bp-75) (function items) · [BP-90](#bp-90) (blackboard variables) · and the Outputs table in
> [BP-89](#bp-89). If an action is reachable *only* by double-clicking an unmarked row, users do not
> find it. Check for this before adding any new list interaction.

<a id="bp-88"></a>
### BP-88 — An Instance blueprint can contain **no Event graph, and none can be created**
**Complexity:** RW-L · **Confidence:** ✔✔✔ *(grounded in the asset JSON)*

> User: *"I now can switch just between functions … and have no idea how to go to the instance graph."*

**They were not failing to navigate — there is nothing to navigate to.** `SquadState1.bp.json`
(`Dispatch: Instance`) contains **two graphs, both `Kind=Function`** (`GetThreatLevel`, `NewFunc1`).
The shipped `Recipes/Blueprints/SquadState.bp.json` ships exactly one graph, `GetThreatLevel`,
`Kind=0` (Function). So **My Blueprint's `Graphs (0)` is literal.**

Two distinct gaps:

1. **No create-path for an Event graph.** BP-24 shipped **"Functions +"** (and Macros has a `+`), but
   the **Graphs** section has none. A designer who starts from the `SquadState` recipe therefore has
   *no route at all* to an Event/Tick graph.
2. **An `Instance` asset with zero Event graphs may not be a meaningful shape.** Contrast the sibling
   recipe `SquadAwareEngagement.bp.json`, which is `Tick`/`Kind=1` (Event). `SquadState` is
   functions-only yet declares `Dispatch: Instance` — i.e. it is shaped like a *library* and labelled
   like an *instance*. See [BP-92](#bp-92); resolve that before deciding whether the fix is "add a
   Graphs +" or "the recipe's dispatch is wrong".

⚠ Do not fix (1) in isolation until (2) is answered — adding a create button to an asset shape that
should not exist would cement the confusion.

#### ✅ Unreal comparison

In Unreal an **Event Graph always exists** on a Blueprint, and more can be added — a Blueprint is never
in the state this asset is in. The reason ours can be is [BP-92](#bp-92): the editor can only ever
create `Dispatch: Instance`, so a functions-only asset has no way to say it is a **library** and no way
to gain an event graph either. ⇒ fix BP-92 first; "add a Graphs +" alone would cement the confusion.

<a id="bp-89"></a>
### BP-89 — Function **outputs are undiscoverable**, and the Return node's Details panel shows only `Success`
**Complexity:** RW-L · **Confidence:** ✔✔✔

> User: *"I have no idea how to add 3 function outputs. Where? Return node detail panel always shows
> Success and nothing else."*

⚠ **This blocked the T1–T7 verification of [BP-73](#bp-73)** — the largest unverified item in the
programme could not be exercised because the entry point could not be found.

The affordance **exists**: *Graph Signature* window → **Outputs** table → **`+`**. It is a bare `+`
under an unlabelled table, in a panel the designer has no reason to associate with the Return node
they are actually looking at. Nothing on the Return node, and nothing in its Details panel, points at it.

#### ✅ Unreal validates the proposed fix — and goes further

In Unreal the **Details panel** carries **Inputs** and **Outputs** sections with **+** buttons, and it
opens from **three** places: the My Blueprint item, the **function entry node**, *or* the **result
node**. Input params surface as data-**out** pins on the entry node and outputs as data-**in** pins on
the return node — **exactly our shape**, which BP-71/BP-73 already got right.

⇒ The gap is purely *where the control lives*. We have **one** entry point — a separate Graph Signature
window; Unreal has three, two of which are the node the designer is already looking at. Putting
add/remove on the Return node's Details is therefore not a workaround: **it is what Unreal does**, and
it is why nobody hits this problem there.

**Fix — make the Return node self-explanatory, since that is where the designer already is:**
- Details for a `Return` node should list the graph's **declared outputs** (and offer add/remove), not
  just `Success`.
- With zero outputs declared, say so and link to Graph Signature, rather than rendering a bare node
  that reads as broken.
- Label the Graph Signature tables (`Inputs` / `Outputs`) with what the pins will *become* — the
  BP-85 note already records that the entry-node/Return-node asymmetry costs real time
  ([§ four things that look like bugs](#bp-85), item 4).

> ✅ **DONE (2026-08-08, Batch 20).** New `ReturnNodeDrawer` + `ReturnNodeSession`
> (`NodeDrawers/ReturnNodeDrawer.cs`), registered in `BlueprintEditorBootstrap` beside the
> `FunctionCallNode` drawer. **There was no Return drawer at all** — `BlueprintDetailsWindow`
> fell through to `DrawReadOnlySummary`, a reflection dump of the single property `ReturnNode`
> has. That *is* the reported symptom, and it also means a registered drawer **replaces** that
> fallback, so the drawer renders `Status` itself — now an editable combo instead of read-only.
> `CreateSession` only receives the asset, so the session resolves its containing graph by
> scanning `parentAsset.Graphs`.
>
> `DrawParameterRows` was **extracted**, not copied, into `Windows/ParameterRowsView.cs`; both the
> Graph Signature window and the Return panel call it, so they cannot drift. BP-86's
> `ImGuiBufferText.Decode` moved verbatim — no `TrimEnd('\0')` reintroduced, and
> `GraphSignatureNulCorruptionTests` still passes through the new path. Zero declared outputs now
> renders *"This function declares no outputs. Add one to return a value."* above a live `+`.
>
> ⚠ **Two corrections to the handoff's premises.** (1) `GraphSignatureEditModel` had **no undo
> integration whatsoever** — the named reuse target could not produce an undo entry, so it gained
> an optional recorder seam (null recorder ⇒ pre-BP-89 behaviour byte-for-byte). Undo is by
> whole-list snapshot: `ParameterDecl` is mutable and rename/retype edit elements in place, so a
> shallow copy cannot capture "before". (2) `ReturnNode` is in namespace
> `Hrot.Blueprints.Core.Assets`, not `…Compiler.Assets` — the *project* is the compiler, the
> namespace is not.
>
> 🔴 **One defect found in review, not by the tests.** `RestoreInto` published the snapshot's own
> `ParameterDecl` instances into the live list, so a later in-place rename rewrote an *older* undo
> entry's captured state and replaying it restored the newer value (reachable as undo → redo →
> edit → undo → undo). The snapshot is now re-copied on the way out;
> `OutputUndoEntry_ReplayedAfterALaterInPlaceRename_StillRestoresTheOriginalName` fails against the
> old code.
>
> Outputs edits also raise `NotifyStructureChanged` in **both** directions (they change pin
> projection on the Return node *and* every call site); a `Status` edit does not. Reverting the
> `record` wiring reddens 6 tests; dropping only the two `NotifyStructureChanged` calls reddens
> exactly the 2 structure-changed tests — the concerns are independently locked. 20 new tests.
>
> ⚠ **Remaining gap:** edits from the **standalone Graph Signature window are still not undoable.**
> It holds a `DirtyTracker` but no `IEditService`, and wiring one in means restructuring its
> construction — deliberately out of scope here.
>
> **T1–T7 are now performable.** They remain *unperformed* — this unblocks the visual check, it is
> not the visual check.

<a id="bp-90"></a>
### BP-90 — Blackboard / variables panel rename has **no visible affordance** (BTree, and everywhere the control is reused)
**Complexity:** RW-L · **Confidence:** ✔✔✔ *(read from source)*

> User: *"For BTree there is no possibility for rename in the variable panel, nor in the blackboard
> variable panel."*

Rename **is** implemented — `VariablesPanelControl.cs:319` starts it on
`ImGui.IsMouseDoubleClicked(Left)` over the row, gated on `!schema.IsReadOnly`. There is **no** context
menu, no pencil affordance, and no tooltip saying so; the only tooltip on the row is the unrelated
*"Not referenced by any node"* hint for unused variables.

Two failure modes, indistinguishable to the user:
- they never discover the double-click, **or**
- `schema.IsReadOnly` is true for this asset and the double-click is silently swallowed — **no
  feedback either way.**

**Fix:** a right-click → *Rename* item (and/or an inline pencil), plus a disabled-with-reason state
when `IsReadOnly`. ⚠ **Also determine why `IsReadOnly` is set for a BTree blackboard** — if it is, that
is a second, larger issue hiding behind this one.

⚠ **Testing consequence:** [BP-86](#bp-86) sites 4–6 (`VariablesPanelControl:561,562,640`) could **not
be exercised in the editor** for this reason. They are covered by the headless helper tests, but the
NUL fix at those three sites remains unconfirmed by hand.

#### ✅ Unreal comparison

Unreal renames from **F2** *and* a right-click **Rename** entry, on every panel item, everywhere —
double-click is an *additional* shortcut, never the only route. ⇒ our fix is not a new idea, it is the
industry-default affordance we are missing. **This is the third instance of the same pattern here**
([BP-75](#bp-75), [BP-89](#bp-89), this) — worth fixing as a convention across all panels rather than
one control at a time.

<a id="bp-91"></a>
### BP-91 — No discoverable way to **add an event to an HSM graph**
**Complexity:** RW-L · **Confidence:** ✔ *(user-reported; not yet traced to source)*

> User: *"I did not find any way how to add event in HSM graph."*

⚠ **Testing consequence:** [BP-86](#bp-86) site 2 (`HsmEventsWindow.cs:110`) could **not be exercised
in the editor** — the events window's rename path is reachable only once an event exists.

**Next step (not done):** determine whether the HSM events window has a create path at all, or whether
it is (like BP-90) present but undiscoverable. Trace `HsmEventsWindow` before scoping — the answer
decides whether this is a missing feature or a missing affordance.

<a id="bp-92"></a>
### BP-92 — 📐 Where do Functions *live*, and how are they shared? (architect)
**Complexity:** RW-H *(architect decision first)* · **Confidence:** n/a — this is a design question

> User: *"why are functions stored together in an instance blueprint json? how can they be shareable
> with other blueprints? should they be in separate blueprint files? I do not understand the logic.
> How does Unreal do that?"*

A fair question the current model does not answer on screen. Today a Function graph is stored **inside
the owning asset's `.bp.json`**, alongside that asset's Event graphs, and cross-asset calls go through
the peer mechanism (`CallPeer.{guid}`, see [BP-68](#bp-68)) rather than through any notion of a shared
function library.

#### ✅ Answered 2026-08-08 — **mostly NOT an architect question. The model already matches Unreal; the editor cannot reach it.**

The previous revision said *"Claude cannot verify Unreal's semantics from this repo"* and left all three
questions open. That was a cop-out — the user pointed out the web was available. Verified against both
the Unreal docs and this codebase:

**1. Are our functions local to the owning blueprint? Yes — and Unreal is the same.**
A Function graph compiles to a `private static Func_X` inside that asset's generated class
(`InstanceEmitter.EmitInstanceFunctionMethod:270-298`). Unreal's per-Blueprint functions are likewise
members of that Blueprint class. **This part is already at parity and needs no change.**

**2. "Why not function-only blueprints?" — ⭐ WE ALREADY HAVE THEM.**
`BlueprintDispatchKind { Library, AiPrimitive, Instance }` (`BlueprintAsset.cs:34`). **`Library` *is*
the function-only blueprint**, and it is real end to end:

| Layer | Evidence |
|---|---|
| Data model | `BlueprintDispatchKind.Library` |
| Compiler | `LibraryEmitter.cs:20` emits **every** `Function` graph as a static method; `LibraryLowering` handles the dispatch |
| Editor pin projection | `NodePinSchema.cs:574,598` already has a Library-specific branch (no self/view context params) |
| Shipped assets | `LibraryMath.bp.json` (graph `Add`), `with-callable-peer.bp.json` (graph `Execute`) |

**3. ⭐ The actual root cause — one line.** `BlueprintNewAssetService.cs:96` hardcodes:

```csharp
Dispatch = BlueprintDispatchKind.Instance,
```

There is **no dispatch choice anywhere in the editor UI**. Every blueprint the editor creates is an
Instance blueprint, always. `SquadState` is a functions-only asset wearing `Dispatch: Instance` **because
that is the only thing the editor can produce** — not because anyone designed it that way.

⇒ This dissolves the confusion in [BP-88](#bp-88) too: *"an Instance blueprint with no Event graph"* is
not a design puzzle, it is a Library blueprint that could not declare itself one.

#### Unreal comparison — where we actually stand

| Unreal | Here | Gap |
|---|---|---|
| Per-Blueprint **Functions** — members of that class, created from My Blueprint → Functions **+** | Function graph in the owning asset; **Functions +** shipped in BP-24 | ✅ at parity |
| **Blueprint Function Library** — separate asset of static functions callable project-wide, created via Content Browser → *Create Advanced Asset → Blueprints → Blueprint Function Library* | `Dispatch: Library`, fully supported by the compiler | ❌ **cannot be created from the editor** |
| **Blueprint Macro Library** — separate asset of macros | No macros at all yet | 📐 [Q25](Architect_Question_25_Macros.md) answered; `BP-79`…`BP-83`. ⚠ Q25-C chose **asset-local macros now**, libraries later — worth revisiting given Library dispatch already exists |
| **Local variables** per function | None | [BP-57](#bp-57) |

#### What actually remains for the architect

Only one real question, and it is narrow: **is `Library` dispatch intended to be author-facing, or is it
a compiler-internal kind** (e.g. reserved for generated/AI-primitive support code)? If author-facing —
which the shipped `LibraryMath` recipe suggests — then this is a **`RW-L` wiring item**, not `RW-H`
design: expose dispatch at create time and show it in the UI ([BP-85](#bp-85) already adds the
breadcrumb slot for it).

⚠ **Cross-asset *calling* is a separate axis and already works** — `CallPeerBlueprint` + `CallablePeers`
(BP-66/BP-68). Unreal's library functions need no such declaration, so a follow-up question is whether
Library-dispatch calls should skip the `CallablePeers` opt-in. That one *is* worth an architect round;
the create-time dispatch choice is not.

⚠ Upstream of [BP-88](#bp-88); touches [BP-75](#bp-75).

> ✅ **DONE (2026-08-08, Batch 20).** `BlueprintNewAssetService` is now table-driven: one built-in
> **blank-template recipe per dispatch kind** — `Empty` → `Instance`, `Function Library` →
> `Library`. `MacroLibrary` (Q25) slots in as one more row with no data migration, which is what
> "extensible list, not a toggle" required. `AiPrimitive` is deliberately **not** offered: it needs
> a `Primitive` declaration and hostings this flow does not populate.
>
> ⚠ **Correction to the handoff.** It specifies "create-dialog UI (a combo + label)" — **there is no
> such dialog in production.** `NewAssetLauncher` opens a *recipe tree picker*, then the vendored
> `SaveAsBrowserDialog` (name + folder only), then calls `CreateNew`; `NewAssetDialog` is a headless
> model nothing in production constructs. A combo would have meant editing the vendored NodeEdit
> tree. One recipe entry per dispatch is also **exactly Unreal's own shape** — *Blueprint Class /
> Blueprint Function Library / Blueprint Macro Library* are separate entries in its create-asset
> menu — so this is parity, and needs no new UI at all.
>
> Blank templates are matched by **`AssetId` against the cached instances the picker was handed**,
> replacing a `Name == "Empty"` string compare. That same compare also drove the default asset name
> in `ShowNewAssetDialog` and would have named a new library `Function Library`; it now routes
> through a **default-implemented** `INewAssetService.IsBlankTemplate`, so BTree/HSM/Scenario
> services compile untouched.
>
> ⭐ **"Visible in the UI" needed no new code — it was already built and unreachable.**
> `BlueprintFileAsset` implements `IAssetSubtitleProvider` fed from the header `Dispatch` (BP-85
> breadcrumb) and `BlueprintIconKeys.ForHeader` already maps `Library` to a distinct browser icon.
> Verified end to end: `Dispatch` serialises as a **root-level string** (`"Dispatch": "Library"`),
> which is what `BlueprintAssetContributor` reads, and `RefreshFromAssembly` does call
> `_blueprintRefresh()`, so a new library is catalogued and opened rather than merely written.
>
> **No existing asset changed dispatch** — retagging `SquadState` is a separate, reviewable change.
>
> **Three Library diagnostics rewritten as designer-facing guidance**, now that users can reach them
> for the first time. **BP1101** (the one a designer actually hits, `V_LatentRules`) named the node
> by raw GUID; it now names it by palette name and says *why* a library cannot suspend. **BP5001**
> (empty library — hit immediately on creating one) points at *My Blueprint → Functions → +* and
> offers the other exit. **BP9001** no longer opens with *"Stage 2 should have caught this"*. All
> three are asserted on by **code**, not message text, so no test was weakened. ⚠ Trap:
> `Hrot.Blueprints.Compiler` multi-targets **netstandard2.0** — `name[..^4]` does not compile there.
>
> 📐 The one genuine architect question — whether `Library` calls should skip the `CallablePeers`
> opt-in — **remains open and unaddressed here.**

<a id="bp-93"></a>
### BP-93 — The editor **writes tracked assets to disk without an explicit Save** 🔴
**Complexity:** RW-L · **Confidence:** ✔✔✔ *(reproduced by `git status` after a session with no Save)*

> User: *"I never saved the BTree and HSM so it is very weird they got saved automatically — this is
> undesired."*

After a batch-19 verification session in which the user opened a BTree and an HSM, poked at the
variables panels, and **never invoked Save**, `git status` showed **two modified tracked assets**:

```
 M Hrot/Subsystems/Hrot.AI.Behaviors/Assets/BTrees/Authoring/PlatoonHillAttack2.btree.json
 M Hrot/Subsystems/Hrot.AI.Behaviors/Assets/HSMs/SampleGuard.hsm.json
```

Exploratory edits were **persisted to disk anyway**:

| Asset | Written |
|---|---|
| `PlatoonHillAttack2.btree.json` | variable **`NewVar1`** added |
| `SampleGuard.hsm.json` | variable **`halo1`** added; **two new states** (both named `State`); `__Root` lost child `aa010000-…-0001`; `__Root` moved `X 0→395, Y 0→130` |

Both files were also **entirely reformatted** — hand-authored compact JSON (`"EditorMetadata": { … }`
on one line) rewritten as fully expanded multi-line JSON, so the real change is buried in ~160 lines of
noise and the diff is near-unreviewable.

**Why this matters more than the nuisance:**
- Scratch experiments silently become **committed-asset changes**. A designer exploring the editor
  dirties the repo without knowing it.
- Combined with the reformat, an unwanted semantic edit is **easy to miss in review**.
- It defeats the whole "try it and discard" workflow — there is no safe way to explore an asset.

#### ✅ Trigger identified (2026-08-08, cloud session) — **a 500 ms debounce timer**

It is **not** close, exit, or a perspective switch. It is
**`RegenerationScheduler`** (`Hrot.Editor.AiShared/Emit/RegenerationScheduler.cs`), wired at
`EditorSubsystem.cs:3312` and ticked once per frame from `EditorSubsystem.cs:1628`:

- Any edit calls `Schedule(asset)`; the debounce window **resets from the last edit**.
- `DebounceTicks` defaults to **500 ms**.
- When it elapses, `flushAction` fires. For BTree/HSM (`EditorSubsystem.cs:3325-3345`) that is
  `mapper.ToDto → JsonServices.Serialize → JsonAestheticFormatter.FlattenNumericArrays →
  AtomicFileWriter.Write(path, …)` — **a real write to the asset's own `SourceFilePath`.**

⇒ **Every BTree/HSM asset is written to disk half a second after you stop typing, with no Save
anywhere in the path.** `AiGraphCanvasWindow.cs:558`'s claim that close-save is *"the ONLY write"* is
true of that window and false of the application.

**The asymmetry is the smoking gun, and it is documented in the code itself.** In the same
`flushAction`:

| Kind | Behaviour |
|---|---|
| **Blueprint** | Guarded by `_blueprintAutoReloadOnEdit`, **default `false`** — *"BF-UX1 FIX A: only auto-recompile when the opt-in flag is set… The user triggers compilation via the Quick Reload / Full Rebuild toolbar buttons."* |
| **BTree / HSM** | **No guard at all** — writes unconditionally |

Blueprints were given an explicit opt-in for auto-*recompile*; BTree/HSM auto-*write* never got one.
That is exactly why the user's blueprint work stayed clean while `.btree.json` and `.hsm.json` were
modified underneath them.

⚠ **It also exceeds its own documented scope.** The comment at `EditorSubsystem.cs:3323-3324` states
*"SampleScout.btree.json / SampleGuard.hsm.json are the only two editor-owned assets"* — but the flush
is keyed off `asset.SourceFilePath` with no ownership check, so it wrote
**`PlatoonHillAttack2.btree.json`**, a hand-authored production asset that is *not* one of the two.

⇒ **The reformatting is not a separate bug**: every flush is a full `ToDto → Serialize` round-trip, so
the file is rewritten wholesale from the DTO each time. That is also the mechanism behind
[BP-94](#bp-94) — same round-trip, same 500 ms cadence.

**Decide the policy** (explicit Save only, with a dirty marker + prompt on close, is the conventional
answer). The narrowest immediate mitigation mirroring the existing Blueprint precedent is to put the
BTree/HSM JSON write behind the same kind of opt-in flag, defaulted off — but note that the scheduler's
*other* job (debounced regeneration) is legitimate, so **do not simply disable the scheduler**.

⚠ See [BP-94](#bp-94) — the same round-trip *also* changes values the user never touched, which is a
separate and arguably worse defect.

<a id="bp-94"></a>
### BP-94 — A load→save round-trip **changes fields the user never touched** 🔴
**Complexity:** RW-L · **Confidence:** ✔✔ *(observed in the BP-93 diffs; cause not yet traced)*

Independent of *when* the editor saves ([BP-93](#bp-93)), *what* it writes is not value-preserving.
Two fields moved with no corresponding user action:

| Asset | Field | Before | After |
|---|---|---|---|
| `PlatoonHillAttack2.btree.json` | `Blackboard.Variables["state"].IsAutoManaged` | `false` | **absent / null** |
| `SampleGuard.hsm.json` | `Blackboard.Managed` | `false` | **`true`** |

The `state` variable was not edited at all — only an unrelated variable was added — yet its
`IsAutoManaged` flag was dropped. `IsAutoManaged` is load-bearing for the shared-state wiring the
HillAssault2 integration depends on (see [BP-83](#bp-83) and the tree-integration work), so silently
turning `false` into "unspecified" is not cosmetic.

#### ⚠ Both fields traced (2026-08-08, cloud session) — **this entry largely dissolves into BP-93**

**1. `IsAutoManaged` `false` → absent is NOT a bug — it is by design and value-preserving.**

`BehaviorTreeAssetDto.cs:38-40`:

```csharp
/// Omitted from JSON when false (default) for backwards compatibility.
[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
public bool IsAutoManaged { get; set; }
```

The omission is deliberate and documented, and **absent deserializes back to `false`** — the DTO's own
default. So `false → omitted → false` round-trips correctly. Both mappers are symmetric too
(`BehaviorTreeAssetMapper:407,425`; `HsmAssetMapper:430,447`), and neither `BTreeJsonServices` nor
`HsmJsonServices` sets a global `DefaultIgnoreCondition`.

⇒ **This entry's claim that *"'false' → 'unspecified' is not cosmetic"* was wrong.** Absent *is*
`false`, by the schema's stated contract. What changed is the file's *text*, not its *meaning* — the
original file simply carried an explicit `false` that this serializer no longer writes. The
shared-state wiring is unaffected.

**2. `Managed` `false` → `true` is a real value change, and it is intended.**

`BTreeCommandSink.cs:231` and `:276` set `_asset.IsBlackboardEditorManaged = true` on the
add/promote-variable paths (right beside the `IsAutoManaged: true` calls). The flag means *"the editor
manages this blackboard"*, so adding `halo1` legitimately flips it. Candidate 2 as originally written —
*"inferred on save from whether variables exist"* — is **not** the mechanism; it is set at edit time by
the command, then round-tripped faithfully.

⇒ **The value change is correct. The defect is that the edit was persisted at all** — which is
[BP-93](#bp-93), not a separate serializer bug.

#### What actually remains here

| Was claimed | Verdict |
|---|---|
| `IsAutoManaged` silently dropped | ❌ **Not a defect** — documented `WhenWritingDefault` omission; absent ≡ `false` |
| `Managed` flipped by the writer | ❌ **Not a defect** — set by the add-variable command, round-tripped correctly |
| Round-trip is not value-preserving | ❌ **Refuted** — on these two fields it is |
| **Wholesale reformatting** (compact → expanded) | ✅ **Real** — `WriteIndented = false` + `JsonAestheticFormatter` produce a different layout from the hand-authored files, so any write rewrites ~160 lines and buries the real change |

**Still worth doing, for the reformatting alone:** load every shipped `.btree.json` / `.hsm.json` /
`.bp.json`, save untouched, and assert the parsed result is **deeply equal** to the original — deep
equality, *not* textual, so it passes on the by-design omissions above while still catching a genuine
value change. Pair it with a **byte**-stability test only if the layout is made canonical first.

⇒ **Severity downgraded from 🔴 to a diff-hygiene item.** The alarming half of the original report was
[BP-93](#bp-93) all along: the writes should not have happened.

---

## Explicitly out of scope

| Item | Why |
|---|---|
| **Macros** | Absent from the entire codebase — no stub, no data model. New capability; architect round required. |
| **Collapse-to-function / collapse-to-macro** | Absent, and would have nothing to collapse into until BP-24 lands. |
| **Squad quartet & dispatcher lowering** | Abandoned by design, superseded. Remove rather than implement (BP-09). |

## Items needing an architect decision before scoping

`BP-40` (Library-dispatch breakpoints) · `BP-38` (pause-on-exception, already LOCKED as deferred) ·
`BP-52` (UX-1/UX-2) · `BP-27` *if* the StructEdit re-check confirms no reusable picker · plus macros
and collapse-to-function if ever brought back into scope.

**Added 2026-08-08:**

- **[BP-87](#bp-87)** — *which* types may a blueprint parameter/variable declare? The vector four
  (`Vector2/3/4`, `Quaternion`) are plainly an alias-mapping oversight, but whether
  `sbyte`/`ushort`/`uint`/`ulong` should be legal at all is a real design question (they exist in no
  compiler table and have no coercion entries). Register or remove — the status quo offers them and
  then fails the build.
- **[BP-92](#bp-92)** — **where do Functions live and how are they shared?** Asset-private + peer-call,
  or a first-class shared function library? And what does `Dispatch: Instance` mean for a
  functions-only asset? ⚠ Upstream of [BP-88](#bp-88) and [BP-75](#bp-75) — settle before building
  either. Raised by the user in the batch-19 visual check.
- **[BP-84](#bp-84) follow-on** — undo-of-delete rebuilds a node from its *kind string alone*, because
  `INodeModel` exposes no property bag and so the shared `EditCommands` cannot build a lossless
  inverse. BP-84 fixed this Blueprint-side with a removed-node tombstone; **the BTree and HSM sinks
  have the same exposure.** Whether to lift a node-state snapshot into the shared command pair is a
  cross-editor decision.

<a id="bp-65"></a>
### BP-65 — Placing a node was silently non-undoable 🔴
**Complexity:** WIRING · **Confidence:** ✔✔ *(found while testing BP-12a)*

> ✅ **DONE (2026-08-04).**
- **`BlueprintCommandSink.CreateAssetNode` ignored `GraphCommand.AddNode.AssignedId`** and minted its own `Guid.NewGuid()` — at two sites (the generic path and `FinishVariableNode`).
- `CommandBuilder.AddNode` mints an id, puts it in the forward command and pairs it with **`RemoveNodes([thatId])`**. So every inverse named a node that did not exist: **palette drops, wire-drops, variable drags, custom-event drags — none of them undoable.**
- **The other two sinks already got this right:** `BTreeCommandSink.cs:134` and `HsmCommandSink.cs:184/276` both use `cmd.AssignedId.Value`. The Blueprint sink even honours it for `AddComment` (`:970`) — just not for nodes.
- **Why nothing caught it:** the sink *also* recorded an `AddNodeCommand` on `CommandHistory`, which holds the node **object** rather than its id, so `history.Undo()` worked correctly — in a stack no UI path ever reached (that is BP-11). `CommandSink_AddNode_Undo_RemovesNode` therefore passed while the real path was broken.
- **Fix:** honour the assigned id, falling back to a fresh Guid only when it is empty. Pinned by three tests in `BlueprintUndoUnificationTests`.

<a id="bp-66"></a>
### BP-66 — The peer-blueprint catalog scanned a directory that does not exist 🔴
**Complexity:** WIRING · **Confidence:** ✔✔ *(found in the visual check)*

> ✅ **DONE (2026-08-05).**
- `EditorSubsystem` built `BlueprintPeerSource` over **`{BaseDirectory}/blueprints`**. Every other blueprint consumer uses **`Assets/Blueprints`** via `AssetRoots.AssetsRelative(AssetKind.Blueprint)` — including **two other sites in the same file** (`:715` `_bpRootDir`, `:3099` the quick-reload catalog), both of which also honour a resolved project directory.
- So `EnumerateAll()` yielded nothing, and `BlueprintDocumentFactory`'s peer-signature lookup **never resolved a peer**. `NodePinSchema.CallPeerBlueprintPins` treats that as "not found" and returns its graceful fallback — untyped `exec + Return:System.Object`. **The typed-argument-pin feature has never worked in the editor.**
- **Why it stayed invisible:** the fallback is deliberate and silent — it exists so the node still renders when no lookup is wired. A path that finds nothing is indistinguishable from no lookup at all.
- **Fix:** both sites now use `_bpRootDir` (already resolved at `:715`) with `AssetRoots.AssetsFor` as the fallback.
- ⚠ **This is why BP-08's picker reported "no peer Blueprints discovered".** The drawer was correct; the catalog under it was empty.

<a id="bp-67"></a>
### BP-67 — The When node's other three mode forms are stubs
**Complexity:** RW-M · **Confidence:** ✔✔ *(found in the visual check)*
- BP-10 fixed **EventFired**. The remaining three modes each render a single `TextDisabled` line and cannot be configured at all: `DrawValueChangedForm` ("component/property picker"), `DrawConditionMetForm` ("predicate editor"), `DrawEqsResultForm` ("trigger and sensor picker").
- ⚠ **Not the same shape as BP-10.** EventFired was `WIRING` because its catalog was already injected *and already called* — only the result was unrendered. These three have **no ready-made source**: ValueChanged needs a component→property picker (BP-62's `ComponentFieldReflector` gets partway, but property *paths* are new), ConditionMet needs a predicate-editor UI over `IPredicateCompiler`, and EqsResult needs a trigger enum plus a sensor-variable picker (`ReadEqsResultNodeDrawer` already lists `EqsSensorHandle` variables — the one reusable piece).
- **Three of the node's four modes are unusable**, so the node is effectively EventFired-only.

<a id="bp-63"></a>
### BP-63 — NodeEdit's built-in Comment Details view is not undoable
**Complexity:** RW-L · **Confidence:** ✔✔ *(found while fixing BP-11)*

> ✅ **DONE (2026-08-04).** `IDetailsContext` gained two members, **both defaulted so no implementer
> breaks**: `Model` (so a view can read the state it is about to overwrite) and
> `Execute(forward, inverse, label)` (whose default applies the forward through the sink, exactly the
> old behaviour). `CommentDetailsView.Commit` now builds a real inverse from the model and routes
> through `Execute`; `Revert()` actually reverts instead of just clearing the dirty flag.
- `CommentDetailsView.Commit` (`FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Panels/Views/CommentDetailsView.cs:43`) calls `_ctx.CommandSink.Apply(new GraphCommand.UpdateComment(...))` **raw** — the same bypass shape BP-02/BP-59 fixed across `CanvasRenderer`, in a file BP-02's sweep did not reach.
- ⚠ **Not a one-line conversion.** `IDetailsContext` exposes only `CommandSink`, `Editors`, `Icons`, `Theme` — no `GraphView` to record on and **no `IGraphModel` to snapshot the prior state from**. The class says so itself: `Revert()` is a no-op commented *"Re-load from model not possible here (no IGraphModel reference)"*. Fixing it means widening the context in the vendored tree.
- **Not a regression from BP-11.** It was already non-undoable — the sink recorded onto `CommandHistory`, which no UI path read. BP-11 removed that dead recording, so the file's status is unchanged and now honest.

<a id="bp-64"></a>
### BP-64 — 2 pre-existing Windows-only test failures in `Hrot.Editor.AiShared.Tests`
**Complexity:** WIRING · **Confidence:** ✔✔ *(found while fixing BP-11)*

> ✅ **DONE (2026-08-04).** Rewritten path-agnostic rather than platform-gated — the assertions are
> about path *semantics*, which hold on both platforms once the paths are built with
> `Path.Combine` instead of hardcoded as Windows literals. `CheckCollisionOnDisk` was the sharper
> case: `@"C:\Trees\Foo.btree.json"` is **one long file name** on Linux, so the
> `GetDirectoryName`/`GetFileName` split under test never happened — the test was passing for the
> wrong reason on Windows. `SaveToFile` now targets a path under a non-existent subdirectory, which
> fails everywhere. **1204/1204.**
- `ExportDeliveryModalTests.SaveToFile_InvalidPath_ReturnsErrorString` expects a non-null error, but Linux has no invalid path characters, so the save succeeds.
- `AssetBaseNameCollisionGuardTests.CheckCollisionOnDisk_ConsultsOnlyTargetDirectory` asserts `"C:\Trees"` and gets `""`.
- **Verified pre-existing:** both reproduce on a `git stash`ed tree. 1202 of 1204 pass.
- **Why it went unnoticed:** this suite is not in the programme's gate list. Decide platform-gate (`SkipUnless` Windows) vs. rewriting the assertions to be path-agnostic — and consider whether the gate list should include it.

---

# Appendix — Programme log (2026-08-04 session)

Orientation for resuming: [Blueprint_Gaps_Programme_RESUME.md](Blueprint_Gaps_Programme_RESUME.md).
Per-issue outcomes are in the `DONE` notes above — this records the **arc and the decisions**, which
those notes do not.

## Batches shipped

| # | Items | Theme |
|---|---|---|
| 1 | BP-59 · BP-29 · BP-16 · BP-15 · BP-12e | silent failures made loud |
| 2 | BP-02 · BP-47 · BP-48 · BP-49 · BP-50 | undo bypasses + documentation accuracy |
| 3 | BP-04 · BP-09 | palette: add what was unreachable, retire what was dead |
| 4 | BP-62 · BP-35 (+ suite serialization) | reflection robustness & test health |
| 5 | BP-41 | coverage: three *different* AiPrimitives on one entity |
| 6 | BP-11 ⭐ | undo unification — one stack for canvas and drawer edits |
| 7 | BP-03 · BP-05…BP-08 · BP-10 · BP-12a · BP-63 · BP-64 (+ BP-65) | the WIRING batch — machinery existed, only the UI hook was missing |
| 8 | BP-12c · BP-68 (+ BP1407/BP1408; dispatcher section retired) | custom-event authoring — unblocks BP-07 |
| 9 | BP-60 🔴 | promote-to-variable — and BP-02's last undo bypass |
| 10 | BP-23a | canvas clipboard: copy / cut / paste / duplicate |
| 11 | BP-12b | My Blueprint item rename / delete / duplicate |
| 12 | BP-13 | align / distribute / straighten |
| 13 | BP-17 · BP-18 | node custom titles and body collapse |
| 14 | BP-19 · BP-20 | minimap and jump-to-issue |

## The audit was wrong ten times — every correction is recorded in-place

This matters more than any single fix: **the register cannot be trusted without re-derivation.**

| Claim | Reality |
|---|---|
| BP-46 — generic `GetShared<T>` missing | **Already shipped**; compiler emits calls to it. Refuted. |
| BP-37 — "inject `INetworkEntityMap`" | That type **does not exist**; raised `RW-L`→`RW-M`. |
| BP-55 — needs a delete affordance built | Backend exists; only the UI hook is missing. Lowered to `WIRING`. |
| BP-02 — 10 undo-bypass sites | **15**, including node delete (became BP-59, 🔴 data loss). |
| BP-31 — "BTree lacks HSM's guard" | HSM's guard **never runs in production**. Premise inverted (BP-61). |
| BP-48 — "Runtime DD §13.5" | It is **§9.6**. |
| BP-41 — implies a slot-**key** collision risk | Keys were never at risk (per-`VisualId`). The untested thing is per-slot **sizing** — every prior test placed one `WorkingState` *type*. |
| BP-07 — "reuse `UnifiedEventDiscovery`" | That enumerates **engine** events. A custom event is **asset-scoped** (`asset.CustomEvents`); every choice from that picker would have failed to resolve. |
| BP-12a — "drag-variable-into-graph is dead" | The **drag** path already worked (`CanvasRenderer.PlaceVariableNode`). The dead route is the **context menu**. |
| BP-12c — "custom events can't be created" | True, but **the declaration is only half the feature**. The body is an `Event` graph of the same name (`Event_{graph.Name}` is emitted from the graph, not the declaration), and the editor cannot create graphs at all — BP-24. Shipping the declaration alone made *calling* one a compile error, so it needed BP1407/BP1408 to say so. |

Two architect statements were also wrong and are corrected in the
[Q22 addendum](Architect_Question_22_Undo_Unification.md): D2 does **not** fix tier 3, and
`IsItemDeactivatedAfterEdit` cannot capture a pre-drag baseline.

## Decisions taken (so they are not silently revisited)

- **BP-15 `ScoreDecision.AssetId` is checked for non-empty only.** A `Guid.TryParse` check rejected
  the shipped `CombatPostureDecision`. The pseudo-GUID convention is now pinned by a test.
- **`ArrayMake`/`ArrayGet` are a hard error (BP1420), not a warning.** BP4004's warning still lets a
  build succeed, which is what hid the bug.
- **`PromoteToVariable` was deliberately left on `Commands.Apply`** during BP-02 — recording undo for
  a no-op would make Ctrl+Z consume a step that reverses nothing. Tracked as BP-60.
- **Removed a shipped asset node.** `EnumDemo.bp.json` carried an inert `CallCustomEvent`
  (empty `EventId`, no pins, unlinked) that the new BP-15 validator caught. Deleting it was chosen
  over weakening the validator to a warning.
- **Multiplexer exceptions propagate**, matching a directly-wired sink; swallowing would hide a
  broken observer.
- **Flaky tests are fixed, never skipped.** A skip is a permanent silent coverage hole.
- **The Event Dispatchers section was deleted, not wired** (BP-12c). The audit offered the choice;
  BP-09 had already retired the concept's node kinds, nothing consumes `EventDispatchers`, and no
  shipped asset declares one. Wiring a create path would have been the only way to author data
  nothing reads.
- **BP1407/BP1408 fire at the CALL site, never at the declaration.** Erroring on a declaration would
  make BP-12c's own create button emit a broken asset on first click, with no way to fix it until
  BP-24 lands.

## New issues found *while fixing*, not by the audit

**BP-59** (🔴 data loss) · **BP-60** · **BP-61** (🔴) · **BP-62** · **BP-63** · **BP-64** ·
**BP-65** (🔴) · **BP-66** (🔴) · **BP-68** (🔴) · **BP-69** (🔴) · **BP-70** · **BP1407/BP1408** (custom events with no handler graph compiled to
uncompilable C#). Nearly all
were found by following an inconsistency rather than by reading the register — which is the argument
for re-deriving claims rather than working the list top-down.

The same holds one level up: **the architect round missed a gap the code showed immediately.** Q22's
approved D2 ("make `CommandHistory.Execute` delegate to `UndoStack`") would have double-recorded,
because the sink was already recording every command it applied onto a stack that happened to be
dead. Three reviewers — audit, architect, post-approval addendum — all read D2 as the low-risk step.
It was the one step that could not work.

**BP-65 is the same lesson once more, and the sharpest instance.** Node placement had been
non-undoable in the Blueprint editor for the whole life of the feature, in a sink whose two sibling
sinks (BTree, HSM) got it right — and no audit item mentions it. It surfaced only because a test
written for something else (BP-12a's menu commands) asserted that undo actually removed the node.
Every part of the register's coverage of undo was written *around* this bug without seeing it.

**BP-68 adds a third variation: a bug can need another bug fixed before it is even reachable.** The
sink has bound nothing for asset-scoped dynamic kinds for the life of the feature, and no audit item
mentions it — because until BP-12c no asset could declare a custom event, and until BP-66 no peer was
ever discovered. Shipping BP-12c made the very next gesture fail. **Expect the item after a wiring
fix to expose the next one**; the visual check is what finds these, not the suite.

**Latent, recorded for BP-24:** `BlueprintDocumentFactory` binds the canvas to
`Graphs.FirstOrDefault(g => g.Kind == Event) ?? Graphs.FirstOrDefault()`. An asset whose main graph
is a `Function` graph would therefore *switch* to a newly added `Event` graph on next open. This is
why BP-12c does **not** auto-create the handler graph alongside the declaration: it would silently
move the designer's canvas off the graph they were editing. Fix the selection rule as part of BP-24
(graph switching) before adding any graph-create path.

---

# Appendix — Test baseline (what "green" means in this repo)

Recorded 2026-08-04 while shipping batches 1–3, then re-measured after serializing the suite.
**Read this before concluding a change broke something**, and before dismissing a failure as
"just a flake".

## Current baseline

> Totals quoted in individual `DONE` notes are **point-in-time** — the count grows as each
> batch adds tests (2551 → 2575 → 2583 → 2594). Only the table below is the *current* baseline.


| Suite | Result |
|---|---|
| `Hrot.Blueprints.Tests` | **2594 passed**, 10 skipped, 0 failed |
| `Hrot.Diagnostics.Breakpoints.Tests` | 130 passed, 0 failed |
| `Hrot.Blueprints.Compiler.Tests` | 3 passed, 0 failed |
| `NodeEditor.Core.Tests` / `NodeEditor.UI.Tests` | 195 / 90 passed, 0 failed |
| Full solution build | clean, 0 errors |

## Fixed: the suite was running in parallel, alone among 9 test projects

`Hrot.Blueprints.Tests` had **no `xunit.runner.json`**, so xUnit defaulted to parallel collections.
Nine other test projects in this repo already ship the same config with parallelism fully disabled.
Adding it (plus the `<Content Include>` line — without that it never reaches the output directory and
has no effect) made the suite deterministic:

| | Runs | Result |
|---|---|---|
| **Before** (parallel) | 3 | **Every run** had exactly 1 failure, **a different test each time** — `MoveToAndFire_BTreeTick_Tests`, `LibraryFunction_InvokeTests`, `CommandSink_AddLink_…BakesOpAccessor` |
| **After** (serialized) | 6 | **5 green** (2575/0); 1 run had the `PdbEmbeddedSourceTests` pair fail |

**Cost: ~90s wall vs ~50s parallel (~1.8×).** Worth it — a suite that cries wolf once per run trains
people to ignore it, which is how a real regression gets waved through.

## Two genuine defects remain — do NOT disable them

**1. ~~`CommandSink_AddLink_…BakesOpAccessor` — order-dependent.~~ ✅ ROOT-CAUSED AND FIXED.**
It now passes alone (1/1), in its class (36/36), and in the full suite (2575/0). The cause was **not**
test-local: `TryBakeCollectionConsumer`'s `CollectionWriteNode` case gates on
`ComponentFieldReflector.IsWritableComponent`, which resolves the component FQN by scanning only
*already-loaded* assemblies. With `Hrot.AI.Behaviors` unloaded the gate returned false and the bake
silently no-opped. Fixed test-side by forcing the load; the underlying product fragility is
**BP-62**, which is the finding that actually matters.

**2. `PdbEmbeddedSourceTests` (`WithPdbOption_PdbIsNonNull`, `PdbContainsEmbeddedSourceSignature`) —
environment-sensitive.** Failed together once in 6 serialized runs. These run a real Roslyn compile
with PDB + embedded-source emission, so they are resource/timing sensitive. Prior sessions flagged
the same family: `Blueprint_CA07d2_..._RESUME.md:125` names *"env-flaky perf/ALC"* tests.

## Why not just `[Fact(Skip = …)]` them

A skipped test is a **permanent silent coverage hole** that nobody revisits — and both of these point
at real defects: one at a static-initialisation dependency in the editor host, one at PDB emission
under load. Together they are ~0.08% of the suite. Serializing removed the noise that actually
impeded regression detection; the remaining two deserve fixes, not suppression. If one must be
quarantined temporarily, use `Skip` **with a linked issue id in the message** so it resurfaces.

## The "~8–9 reds" note is stale

`Blueprint_Component_Access_RESUME.md:79` says a full parallel run shows *"~8–9 reds that are
pre-existing/flaky — DO NOT chase"*. **That is no longer true.** Anyone still quoting it could wave
through eight genuine regressions. That file now carries a correction banner.

## Method that worked

To decide whether a failure is yours: `git stash`, run the same filter, `git stash pop`. If it fails
identically without your changes, it predates you. This is how both defects above were classified,
and how the BP-15 validator errors were correctly identified as *real* rather than dismissed.
