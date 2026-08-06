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
### BP-78 — Macros: design and implement **[NEW — taken INTO scope 2026-08-06 by the user]**
**Complexity:** RW-H · **Confidence:** ✔✔ *(scaffolding verified; semantics undecided)*

> ⚠ The register previously listed macros as **out of scope — "absent from the entire codebase; new
> capability, architect round required"**. The architect round is indeed required, but **"absent" was
> wrong** — see the scaffolding table in
> [Q25](Architect_Question_25_Macros.md#ground-truth-verified-against-code-2026-08-06). Fifth and
> sixth overturned "nothing exists" claim.

📐 **Design first: [Architect_Question_25_Macros.md](Architect_Question_25_Macros.md)** — five
decision-shaped sub-questions (A: what a macro *is* · B: where expansion happens · C: scope and
sharing · D: multiple exec pins · E: guard rails), each with options, a recommended lean and the
reuse-vs-build tradeoff. **Nothing is built until those answers land**; implementation items will be
split out from them.

#### ⭐ Why macros are worth building *here* — not "Unreal has them"

**`BP1650`** (`Stage2_Validate.cs:2150-2166`): *"A function graph invoked by FunctionCall must not
contain latent nodes; latent execution is only supported in the top-level Tick/event graphs."*

A function compiles to a plain `static` C# method. Latent execution needs the
`BlueprintLatentCursor` in the blueprint's `State` struct plus a resume-block state machine, and that
machinery exists **only** for the top-level graph — the validator is describing the emit, not being
cautious. A **macro inlines**, so a latent node inside one lands where the cursor already lives.

⇒ **A macro is currently the only possible way to factor out a reusable *latent* sequence**
(*aim → wait 0.4s → fire*). Today that must be copy-pasted at every call site, and no amount of work
on functions can ever fix it. Multiple exec in/out pins are the secondary payoff, and are likewise
only expressible by inlining — a C# method has one entry and one return.

#### What exists / what does not

Scaffolding (command ids, `GraphCommand.CollapseToMacro`, the `Macro.Call` gate references, the
rendered My Blueprint section) is listed in Q25. **Missing:** `GraphKind.Macro` — the enum is
`{ Function, Event, Construction }` (`Assets/GraphTypes.cs:24`) — any expansion pass, and any handler
for `editor.create-macro` (`BP-77`).

⚠ **`FakeCommandSink`'s macro/function collapse is a scenario prop** with S22's pin names hardcoded;
it is not prior art for the semantics. See [BP-74](#bp-74).

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
