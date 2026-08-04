# Blueprint Subsystem — Issue Detail Register

> Full detail for every issue found in the 2026-08-04 audit. Companion tracker with checkboxes:
> [Blueprint_Issues_Tracker.md](Blueprint_Issues_Tracker.md). Narrative analysis:
> [Blueprint_Gaps_And_QoL_Audit.md](Blueprint_Gaps_And_QoL_Audit.md).
> **Goal of the programme:** make blueprint editing fully functional and pleasant.
> Macros and collapse-to-function are **out of scope** (new capability, no data model).

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

---

# Area A — Graph editor UX

Canvas ergonomics. Mostly NodeEdit-core capability the Blueprint host never registers.

### BP-23a — No copy / cut / paste / duplicate on the canvas (same-graph)
**Complexity:** RW-L · **Confidence:** ✔✔
- **Symptom:** Paste is permanently greyed out; there is no copy at all. Probably the single most-felt gap.
- **Evidence:** `CanvasRenderer.cs:570` — `ImGui.MenuItem("Paste", "Ctrl+V", false, false)` (trailing `false, false` = *selected, enabled*). `CommandCatalog.cs:19-22` declares Copy/Cut/Paste/Duplicate; **zero** handler registrations repo-wide.
- **Fix:** `Node` is already `[JsonPolymorphic]`, so JSON round-trip is free. `IClipboard` exists, is DI-wired, and has **zero call sites**. `BreakpointJsonClipboard.cs` is an in-repo precedent for the same pattern. Crucially **`AddNodeCommand(Graph, Node)` takes a fully-built node** (`Execute() => _graph.Nodes.Add(_node)`), so paste bypasses the `ApplyInitialProperties` whitelist entirely.
- **Insertion:** new `ClipboardCommands.cs`, registered in `BuiltinCommandHandlers.RegisterAll`; enable the menu item.
- **Trap to avoid:** do *not* extend `BlueprintCommandSink.ApplyInitialProperties` — it whitelists only **8 of 50** node kinds, so a paste built on it would silently drop config on the other 42.
- **Remaining work:** new node GUIDs + internal link remapping.

### BP-23b — Cross-asset / cross-graph paste
**Complexity:** RW-M · **Confidence:** ✔
- Needs `VariableId` / type re-resolution against the destination asset. Scope **after** BP-23a.

### BP-13 — No node align / distribute / straighten
**Complexity:** RW-L · **Confidence:** ✔✔
- **Evidence:** `CommandCatalog.cs:83-91` declares 9 commands (AlignLeft/Right/Top/Bottom/CenterH/CenterV/DistributeH/DistributeV/StraightenConn); zero implementations anywhere in `NodeEdit/src`.
- **Fix:** `CommandBuilder.MoveNodes(IReadOnlyList<(NodeId, Vector2)>)` is the exact batch-move-with-inverse primitive already used by drag. AABB-of-selection pattern exists at `ViewCommands.cs:71-86`. Reroute primitives cover StraightenConn.
- **Note:** Distribute needs a stable position sort.

### BP-02 — Comment colour and z-order changes bypass undo
**Complexity:** WIRING · **Confidence:** ✔✔
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

### BP-59 — Context-menu "Delete Node" is not undoable, but the Del key is 🔴 **[NEW — found in verification pass]**
**Complexity:** WIRING · **Confidence:** ✔✔
- **Two paths for the same user intent; only one is undoable.**
  - **Del key** → `EditCommands.cs:95-125` builds a correct forward/inverse pair — `RemoveNodes` forward, plus a `Batch("Restore Nodes", …)` inverse that reconstructs each node via `AddNode(n.Id, n.Kind, n.Position, props)`. **Undoable.**
  - **Right-click → "Delete Node" / "Delete N Nodes"** → `CanvasRenderer.cs:758` calls `view.Commands.Apply(new GraphCommand.RemoveNodes(targetNodes))` raw. **Not undoable — the nodes are gone.**
- **Severity:** silent, unrecoverable data loss on a destructive action, reachable from the most obvious place a designer would look. Strictly worse than BP-02's cosmetic bypasses.
- **Fix:** route `:758` through the same `EditCommands` delete path the Del key uses — the inverse-builder already exists and is proven, so this is a call-site swap, not new logic.
- **Why the audit missed it:** BP-02 was scoped from its symptom ("comment colour"), so the enumeration stopped at comment commands.

### BP-03 — Bookmarks cannot be renamed or deleted
**Complexity:** WIRING · **Confidence:** ✔✔
- **Evidence:** `BlueprintBookmarksWindow.cs:11-13` self-documents "(V1: no rename/delete UI…)"; `BookmarksPanel.cs:17-36` is a read-only text list.
- **Fix:** `BookmarkStore.Remove(id)` already exists; `Bookmark` is a `record`, so rename is `b with { Label = x }` + `SetSlot`.

### BP-17 — No node renaming / custom titles
**Complexity:** RW-L · **Confidence:** ✔✔
- **Evidence:** `BlueprintNodeModel.cs:24` — `Subtitle => null` always. Node context menu (`CanvasRenderer.cs` `HoverKind.Node`) has no Rename; the Rename at `:800` belongs to `HoverKind.Comment`. A `"Comment"` `SetNodeProperty` key exists end-to-end but **no UI ever issues it**.
- **Fix:** every piece has a precedent — `InteractionState.RenamingComment` inline-rename UX to mirror, and `SetNodeProperty` undo plumbing already proven. Add `NodeMetadata.CustomTitle`, a `"Title"` case, an F2 menu item, and a `RenamingNode` interaction field.

### BP-18 — Node body collapse not exposed
**Complexity:** RW-L · **Confidence:** ✔✔
- **Evidence:** `BlueprintNodeModel.cs:44-45` hardcodes `IsCollapsed => false`. `NodeRenderer` honours the flag.
- **Fix:** `GraphCommand.SetNodeCollapsed` is already defined, with a working reference implementation in `NodeEditor.Demo/FakeBlueprint/FakeCommandSink.cs:126`. Needs a `NodeMetadata` field + a sink case + a collapse glyph.
- ⚠ `BlueprintCommandSink.Apply`'s `default:` case **silently no-ops unknown commands** (`:156-158`), so issuing `SetNode*` today fails quietly.

### BP-19 — No minimap
**Complexity:** RW-L · **Confidence:** ✔✔
- `CommandCatalog.ToggleMinimap` declared, never implemented. `ViewportState` supplies all needed transform math (`GraphToScreen` / `ScreenToGraph` / `FrameRect`). ~150-200 lines incl. click-to-pan.

### BP-20 — No error list / jump-to-next-error
**Complexity:** RW-L · **Confidence:** ✔✔
- **Evidence:** `CommandCatalog.NextError` / `PrevError` declared, never registered. No error-list UI.
- **Fix:** `NodeState.Error`/`Warning` flags already exist and `FindEngine` already filters on them (`:47-53`). `FindBar.Next` / `CenterOnActive` is a ready cycle-and-centre pattern to mirror.
- **Open question:** diagnostics source — compile-time only, or live?

### BP-25 — Cross-blueprint search is cosmetic
**Complexity:** RW-M · **Confidence:** ✔✔
- **Evidence:** `FindEngine.Search(query, scope, view)` never reads `scope`; its own docstring says *"only `FindScope.CurrentGraph` is handled here"*. The UI offers Asset / OpenTabs / WholeProject.
- **Why bigger:** `FindEngine`/`FindBar` are architecturally single-graph-bound. Needs a multi-graph aggregation layer, merged ranking, and cross-tab navigate-then-centre.

### BP-28 — No advanced-pin hiding
**Complexity:** RW-M · **Confidence:** ✔✔
- `INodeModel.ShowAdvancedPins` and `IPin.IsAdvanced` exist and are honoured by the renderer, but `BlueprintPinModel.IsAdvanced` is never assigned. Needs a **new persisted per-pin flag** *and* an authoring UI to mark pins advanced — there is no "which params are advanced" concept to project from.

### BP-56 — No wire-level execution-flow highlighting
**Complexity:** RW-L · **Confidence:** ✔✔
- Node borders glow during execution (`NodeRenderer.cs:215-216,251`) and `WhenFiringPulseRenderer` pulses `When` nodes, but `WireRenderer` never renders execution state. Unreal shows a travelling pulse along exec wires.

---

# Area B — Node authoring surface

Whether a designer can *place* and *configure* each node kind. 13 of 50 kinds run but cannot be configured.

### BP-04 — `Compare` / `BinaryOp` / `BooleanOp` / `Not` cannot be placed at all
**Complexity:** WIRING · **Confidence:** ✔✔
- **Symptom:** Four fully-lowered, compile-tested node kinds are unreachable from the editor. Verified they are instantiated **only in tests** — zero `new CompareNode` etc. in the editor tree.
- **Why it happened:** `BlueprintMathPaletteEntries.cs` routes math through CLR `BlueprintMath` helpers as `FunctionCallNode`, so the functional need is partly covered and the native kinds were never given a front door.
- **Fix:** 14 palette entries (one per enum value), baked at create — exactly the `MakeMath` / `ChannelCommandEntries` recipe. **No drawer needed.** ~40-60 lines.
- **Also:** `Blueprints_Overview.md:75` marks these ✅ — see BP-47.

### BP-22 — `GetParameter` cannot be placed
**Complexity:** RW-L · **Confidence:** ✔✔
- Lowered at `Stage5_Schedule.cs:2098`, zero editor instantiations. Unlike BP-04 it is **asset-specific** (`ParameterId` references `asset.Parameters`), so it needs a picker rather than a baked entry. Model on `BlueprintPickerSources`' `variables.all` pattern re-pointed at parameters.

### BP-05 — `ReadRankedResult.Rank` uneditable
**Complexity:** WIRING · **Confidence:** ✔✔
- Plain `ImGui.InputInt`; no catalog dependency. Simplest of the drawer gaps.

### BP-06 — `WaitForChannel.ChannelType` uneditable
**Complexity:** WIRING · **Confidence:** ✔✔
- Runs and is run-proven, but has no drawer. Reuse `IChannelCommandCatalog`; `ChannelCommandNodeDrawer.cs` (109 lines) is a near-direct template and `WaitForChannel` needs only the channel-type list.

### BP-07 — `CallCustomEvent.EventId` uneditable
**Complexity:** WIRING · **Confidence:** ✔✔
- Reuse `UnifiedEventDiscovery.All()`, already production-wired, which unifies C# `[BlueprintEvent]` structs and editor-authored events.

### BP-08 — `CallPeerBlueprint` target uneditable
**Complexity:** WIRING · **Confidence:** ✔✔
- Reuse `BlueprintPeerSource.EnumerateAll()` (already used by `QuickReloadService` for this very node kind) plus the existing peer-signature lookup for the function list.

### BP-14 — `Return.Status` uneditable (always Success)
**Complexity:** RW-L · **Confidence:** ✔✔
- `Nodes.cs:182` — `Status { get; set; } = NodeStatus.Success`; no drawer, no bake path. A combo over `NodeStatus` mirroring `WhenNodeDrawer.DrawModeSelector`, ~20-30 lines.

### BP-10 — `When` → EventFired form is a stub
**Complexity:** WIRING · **Confidence:** ✔✔
- `WhenNodeDrawer.cs:172-177` is `ImGui.TextDisabled`. The catalog `_eventCatalog.GetEntries()` is **already injected and called at `:175`** — the result is simply never rendered.

### BP-21 — `When` → ValueChanged form is a stub
**Complexity:** RW-L · **Confidence:** ✔✔
- Needs a component + property picker; `ComponentFieldReflector` and the existing component pickers are directly reusable.

### BP-26 — `When` → ConditionMet form is a stub
**Complexity:** RW-L · **Confidence:** ✔✔
- **Corrected mid-audit — this is not REAL WORK.** A complete predicate *editing* UI already exists: `FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/ReplaySearchPanel.cs` (587 lines) — 7 modes including **Compound** AND/OR trees, save/load presets — built **generically on StructEdit** (`_editService.Open(dto, type)` → `ComponentEditDrawer` + a per-type drawer dictionary incl. a recursive `PredicateValueFieldDrawer`), not hand-written per subtype.
- **Why it's cheap:** `Hrot.Blueprints.Editor` **already references `Fdp.Presentation`** (csproj line 26); `WhenNodeDrawer` already has `IPredicateCompiler` injected; `ConditionMetPayload.Condition` is already designed to hold a `SearchPredicateDto` tree as JSON.
- **Residual risk:** panel-width UI inside a narrower node drawer (layout/sizing), and swapping replay-recording sources for blueprint ones (`ComponentTypeProvider` exists).
- *(The earlier "no predicate UI exists" finding searched only `Hrot/`. `PredicateBuilderState` being orphaned and `DataBreakpointManagerPanel` being read-only were both true — wrong surface.)*

### BP-27 — `ScoreDecision.AssetId` uneditable
**Complexity:** RW-M · **Confidence:** ✔✔
- No `UtilityDecisionDef` catalog exists editor-side, so a discovery source is needed before a picker. `Architect_Question_4_Editor_Components.md` asks this exact question and records no answer.
- ✅ **Re-check done (2026-08-04) — RW-M stands.** `UtilityDecisionDef` appears **only** in `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityDecisionGeneratorTests.cs` — there is no production catalog. `ScoreDecisionNode.AssetId` is a bare GUID string (`Nodes.cs:395`). StructEdit edits DTO *fields*; it cannot *discover assets*, so unlike BP-26 there is no reusable picker to inherit. A discovery source must be built first.

### BP-09 — Six abandoned node kinds are advertised in the palette
**Complexity:** WIRING · **Confidence:** ✔✔
- `CallDispatcher`, `BindDispatcher`, `PartitionElements`, `AssignRoles`, `AdvancePhase`, `AcquireSlot` have live palette entries at `BlueprintNodePaletteEntries.cs:100-105` and `:233-244`, with inviting descriptions ("Broadcast an event dispatcher to all bound listeners"), but are unlowered and compile to a silent no-op (BP4004 warning).
- Both families are **superseded by design** — dispatchers by `PublishEvent`/`EventEntry`, the squad quartet by `MemberSlotList`/`SlotRotation`.
- **Fix:** delete 6 `Make<T>` blocks. Pairs naturally with BP-16.

---

# Area C — Editor infrastructure

Document, undo, and panel plumbing.

### BP-11 — No inspector or drawer edit is undoable ⭐
**Complexity:** RW-M *(raised from RW-L — see estimate note below)* · **Confidence:** ✔✔
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

### BP-12a — My Blueprint: drag-variable-into-graph as Get/Set is dead
**Complexity:** WIRING · **Confidence:** ✔✔
- `editor.create-variable-get` / `-set` are invoked by the context menu but never registered. This is the most-used motion in Unreal authoring.
- Reuse the palette / `AddNode` path that already creates `GetVariableNode` / `SetVariableNode` with a baked `VariableId`.

### BP-12b — My Blueprint: items cannot be renamed, duplicated, or deleted
**Complexity:** RW-L · **Confidence:** ✔✔
- `editor.rename-item`, `duplicate-item`, `delete-item` are all unregistered. Consequence: a variable can be **created but never renamed or removed**.

### BP-12c — My Blueprint: custom events and dispatchers cannot be created
**Complexity:** RW-L · **Confidence:** ✔✔
- `editor.create-custom-event`, `editor.create-event-dispatcher` unregistered; both sections are display-only.
- ⚠ Dispatchers are a superseded concept (see BP-09) — consider **removing** that section rather than wiring it.

### BP-12d — My Blueprint: `find-references` is dead
**Complexity:** RW-M · **Confidence:** ✔✔
- `editor.find-references` unregistered. Overlaps BP-25 (cross-blueprint search) — a real implementation likely needs the same multi-graph layer.

### BP-12e — Dead commands fail silently
**Complexity:** WIRING · **Confidence:** ✔✔
- **Root cause of the whole BP-12 family's user experience.** `MyBlueprintPanel.InvokeCreate` discards the returned `EditorCommandResult` (`:288-289`) while `EditorCommandsImpl.Invoke` returns `"Unknown command"` (`:21-22`). Buttons render, click, and do nothing — no error, no toast.
- **Tally: 14 commands invoked by the panel, 1 registered** (`editor.create-variable`).
- **Fix:** surface the failure (log/toast/disable), so an unimplemented command is visible rather than mysterious.

### BP-24 — No Function-graph create path; canvas is locked to one graph
**Complexity:** RW-M · **Confidence:** ✔✔
- The data + compiler layers **already support author-defined functions**: `GraphKind.Function`, `FunctionCallNode.TargetGraphId`, and real multi-graph assets on disk (`DeepNestedBlueprint.bp.json` holds 3 Function graphs). `GraphSignatureWindow` does genuine Add/Remove/Rename/Retype/Move CRUD on `Graph.Inputs`/`Outputs` and is properly wired.
- **Missing 1 — no create path:** nothing in the editor ever appends to `BlueprintAsset.Graphs` (verified: the only `Graphs.Add` hits are compiler-internal lowering).
- **Missing 2 — no graph switching:** `BlueprintDocumentFactory.cs:130-131` binds the canvas permanently at open time via `Graphs.FirstOrDefault(g => g.Kind == Event) ?? Graphs.FirstOrDefault()`.
- ⚠ **Consequence:** in any multi-graph asset, **every graph but the first is unreachable through the UI**. The `FunctionCall` target picker can only select graphs hand-authored in JSON.
- Also fixes the My Blueprint "Graphs" section's double-click, currently `navigateToGraph: _ => { }`.

### BP-57 — Per-function local variables absent from the data model
**Complexity:** RW-M · **Confidence:** ✔✔
- `Graph` has no `LocalVariables` field — only `Id, Name, Kind, Inputs, Outputs, Nodes, Links, Comments, EditorMetadata`. All variables are blueprint-scoped. A genuine **design gap**, not unwired UI. Depends on BP-24.

---

# Area D — Compiler & correctness

### BP-16 — `ArrayMake` / `ArrayGet` produce a silent wrong value 🔴
**Complexity:** RW-L · **Confidence:** ✔✔
- **The most dangerous defect found:** compiles clean, returns wrong data. The pure-value fallback (`Stage5_Schedule.cs:3209-3224`) emits `IrOp_Const("default", pinType)` with **no `Diagnostics.Add` call at all** — unlike the exec-side fallback (`:1803-1804`) which does emit BP4004.
- `NodeCoverageTests.cs:105-118` documents the asymmetry verbatim.
- **Cheapest safe fix:** a Stage2 validator rejecting both kinds (~20-40 lines) turns silent corruption into a compile **error** — strictly better than BP4004's warning, which still lets the asset "succeed". No lowering required.

### BP-15 — Four node kinds accept bad references silently
**Complexity:** RW-L · **Confidence:** ✔✔
- No Stage2 validator for `ScoreDecision`, `ReadRankedResult`, `CallCustomEvent`, `Cast` — none appear among the 23 registered `IValidator`s.
- Template: `V_WaitNodeReferences` (`Stage2_Validate.cs:587-615`), ~30 lines each.

### BP-32 — `When` FallingEdge deferred for ValueChanged mode
**Complexity:** RW-L · **Confidence:** ✔✔
- Live `// TODO M3` at `Stage5_Schedule.cs:862` — block structure allocated, condition logic deferred. **Falling-edge behaviours silently never fire** in that mode.
- Partially fixed since July: `ConditionMet` FallingEdge *is* implemented and tested (`WhenNodeRuntimeTests.cs:771`).

### BP-33 — `WaitForEvent` is structurally broken
**Complexity:** RW-M · **Confidence:** ✔✔
- No `EventTypeId` satisfies both stages: Stage2 matches by short name against `BuiltInWaitPrimitiveCatalog` (`:602`), but Stage5's `BuildWaitForEventOp` never resolves it to an FQN (unlike `WaitForChannel`'s parallel path), so Roslyn always fails CS0400. Documented by a `[Fact(Skip=…)]` regression test.
- **Decide first:** repair, or delete the kind (superseded by named `EventEntry` handlers). Cheapest interim: fold into BP-16's validator.

### BP-58 — `Cast` has no drawer and no validator
**Complexity:** RW-L · **Confidence:** ✔✔
- The **emit bug is FIXED** — `StatementEmitter.cs:283-292` now intercepts `Cast.`-prefixed calls and emits a native `(global::T)` cast. The July matrix is stale here.
- Still no drawer and no validator (validator covered by BP-15). Note `Cast` is also inserted implicitly by Stage3 Normalize, so a drawer may be low-value — confirm before building.

---

# Area E — Debug & diagnostics

Strongest area of the subsystem — several capabilities **exceed** stock Unreal. One live bug.

### BP-29 — Blueprint conditional breakpoints silently never fire 🔴
**Complexity:** WIRING · **Confidence:** ✔✔
- **A live production bug, invisible to the test suite.**
- `PredicateCompiler`'s 3rd ctor arg `blueprintRegistry` defaults to null (`:27`), and `CompileBlueprintVariablePredicate` then returns `static (_, _) => false` (`:235-237`).
- **All three production sites omit it:** `EditorSubsystem.cs:994`, `CgfSubsystem.cs:555`, `ReplayBrowserSubsystem.cs:641`.
- So `BlueprintVariablePredicateDto` — **the exact predicate that "Add Conditional Data Breakpoint…" synthesizes** from a blueprint node — always evaluates false in the running editor.
- **Why tests miss it:** `BlueprintVariableTests.cs:70` constructs the compiler *with* the registry, proving the logic correct while the wiring stays broken.
- **Fix:** `EditorSubsystem` and `CgfSubsystem` are **one-liners** (`_blueprintRegistry` already in scope — 19 and 7 references). `ReplayBrowserSubsystem` has **no** registry field and needs plumbing first.
- **Add a regression test that constructs the compiler the way production does**, so this class of bug is catchable.

### BP-01 — Watch panel shows raw hex bytes
**Complexity:** WIRING · **Confidence:** ✔✔
- `WatchPanelWindow.cs:54-56` renders `Convert.ToHexString(w.LastValueBytes)`.
- `BlueprintDebugSession.MarshalFromBytes` is complete, unit-tested, and already used at 4 other call sites in the same file — it decodes every primitive plus fixed-list wrappers. Swap it in and format via `BlueprintPinDefaultValue.FormatValue` for vector types.

### BP-35 — D4 `MultiplexingProbeSink` missing
**Complexity:** RW-L · **Confidence:** ✔✔
- `IBlueprintProbeSink` exists; needs a composite implementation + a `DebugProbe.Sink` swap so multiple debuggers can observe one run.

### BP-36 — D5 stack-frame inspection is Blueprint-local
**Complexity:** RW-M · **Confidence:** ✔✔
- `CallFrame` / `_callStacks` / `GetCurrentCallStack` live inside `BlueprintDebugSession`. Lifting them to `IDataBreakpointManager` would let BTree/HSM/other-subsystem pauses carry a call stack too.

### BP-37 — `LifecyclePredicateDto` by `NetworkId` unsupported
**Complexity:** RW-M *(raised from RW-L on verification)* · **Confidence:** ✔✔
- Defect confirmed: `DataBreakpointManager.cs:1025` throws `NotSupportedException`, and the surrounding comments name the intended fix.
- ⚠ **But `INetworkEntityMap` does not exist as a type** — it appears *only* in those comments. The concrete `NetworkEntityMap` lives in `FDP/Network/Fdp.Network.Cyclone/Services/NetworkEntityMap.cs`, and `Hrot.Diagnostics.Breakpoints` does **not** reference that project (its only refs are `Fdp.Core`, `Fdp.ModuleHost`, `Fdp.Toolkits`, `Hrot.Blueprints.Core`).
- So this is not "inject an existing interface": it needs an abstraction defined and wired, **or** a diagnostics→specific-network-transport project reference, which is a layering smell. **Design call first.**

### BP-38 — D9 pause-on-Blueprint-exception
**Complexity:** RW-M · **Confidence:** ✔✔
- **Explicitly deferred by architect decision** (Debug Protocol DD §13.3, LOCKED). The soft-pause + triple-buffer rewind machinery is directly reusable; needs an interception point in generated code plus a new breakpoint shape.

### BP-39 — D8 CLR / Visual Studio source-line debugger sync
**Complexity:** RW-H · **Confidence:** ✔✔
- No scaffolding present; would need a DAP or VS-extensibility bridge. PDB emission already exists.

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

### BP-31 — BTree lacks the concurrent-stateful validator HSM has
**Complexity:** RW-L · **Confidence:** ✔✔
- A Subtree referenced twice under a `Parallel` node is currently unguarded on the BTree side. Port `HsmValidator.CheckConcurrentStatefulSubtrees` / `CheckConcurrentSharedScopeKeys` (`HsmValidator.cs:234-325`) to `BTreeValidator`.

### BP-41 — No test for two different AiPrimitive blueprints on one entity
**Complexity:** RW-L · **Confidence:** ✔✔
- Coverage is by analogy only: `T20` uses two *hardcoded* stateful actions on the same rail; `T35` uses the *same* blueprint 3×. The scenario an author will actually hit is unproven. HSM's collision has no regression test either.

### BP-45 — Cross-entity event dispatch (`BlueprintDeferredEvent`) absent
**Complexity:** RW-M · **Confidence:** ✔✔
- The most-cited deferred capability across the Slice-2 docs; the type does not exist anywhere. ⚠ `Blueprint_New_Node_Authoring_Guide.md` §1a describes "automatic same/cross-entity routing" **as if it were current** — that prose is aspirational (see BP-49).

### BP-42 — Cross-entity shared-state **write**
**Complexity:** RW-M · **Confidence:** ✔✔
- Read path shipped (`BlueprintSharedState.TryGetShared<T>`); write is same-entity only. Deferred by design per `Blueprint_SharedState_GetShared_Design.md` §0 — needs `UpdateSharedSlotCommand` + an Input-phase ingress system mirroring `AssignBehaviorEvent`.

### ~~BP-46 — Generic `GetShared<T>` partition-slot accessor~~ — ❌ **REFUTED, ALREADY SHIPPED**
**Confidence:** ✔✔ (verification pass)
- The claim was wrong. `BlueprintSharedState.TryGetShared<T>(EntityRepository world, Entity self, string variableId, out T value)` exists at `BlueprintSharedState.cs:58`, and the compiler **actively emits calls to it** (`StatementEmitter.cs:188`).
- **No work required. Retained as a struck-through row so the id is not silently reused.**

### BP-43 — Custom Events 2b: events with no backing C# struct
**Complexity:** RW-M · **Confidence:** ✔
- No `PublishRaw` / `InjectIntoCurrentBySize` / `IrOp_PublishCustomEvent` anywhere. Blocks fully designer-authored events.

### BP-44 — Custom Events 1d: no event-definition authoring UI
**Complexity:** RW-L · **Confidence:** ✔
- Only `BlueprintEventCatalog.cs` (data/reflection) exists; no editor window to define an event.

---

# Area G — Documentation accuracy

Cheap, and currently actively misleading.

### BP-47 — `Blueprints_Overview.md:75` marks unplaceable nodes ✅
**Complexity:** WIRING · **Confidence:** ✔✔
- `Compare` / `BinaryOp` / `BooleanOp` / `Not` are marked shipped, conflating the compiler axis with the authoring axis. They cannot be placed (BP-04).

### BP-48 — Runtime DD and Overview stale on AiPrimitive working state
**Complexity:** WIRING · **Confidence:** ✔
- `Blueprint_Subsystem_Runtime_Detailed_Design.md` §13.5 and `Blueprints_Overview.md` §1/§5 describe AiPrimitive working state as living only in `Blackboard1024`. True for the legacy/HSM path, **wrong for BTree-composed nodes** (partition tiers).

### BP-49 — Aspirational prose presented as current
**Complexity:** WIRING · **Confidence:** ✔
- `Blueprint_New_Node_Authoring_Guide.md` §1a describes cross-entity routing that does not exist (BP-45). Mark clearly as future.

### BP-50 — Trackers contradict the code
**Complexity:** WIRING · **Confidence:** ✔
- `Blueprint_Subsystem_Implementation_Roadmap_v1.1.md` is **fully superseded** (M0–M16 predate component access, collections, custom events, 50 node kinds) — label it history, not status.
- Also stale: `Custom_Events_BUILD_TRACKER.md` (3d "still remaining", shipped one commit later), `WaveCore_Slice_Design.md` (fixed CS0400 bug, un-parked asset), `Blueprint_Authoring_UX_Backlog.md` (DOC-2 "[next]", already shipped), `Blueprint_Component_Access_TASK_TRACKER.md` and `Blueprint_Editor_SaveOnClose_RESUME.md` (both flag since-fixed items as open).

### BP-51 — DOC-3 / DOC-4 illustrated SVGs missing
**Complexity:** RW-L · **Confidence:** ✔✔
- Memory-layout schematic and lifetime timeline. DOC-1 and DOC-2 shipped.

### BP-52 — UX-1…UX-5 authoring ergonomics unbuilt
**Complexity:** RW-M (architect first) · **Confidence:** ✔✔
- Intent-first memory picker, unify the "two doors" to shared state, progressive disclosure, in-context micro-explanations, graph-level scope badges. The backlog itself marks UX-1/UX-2 as needing an architect nod.

### BP-53 — E6 cross-asset blueprint-action picker
**Complexity:** RW-M · **Confidence:** ⚠ **UNCLEAR — do not act on without re-scoping**
- **Partially refuted.** An action-picker mechanism *does* exist: the `[HsmActionPicker]` attribute is used throughout `Hrot.Hsm.Editor/Inspector/HsmFacets.cs` (6+ sites), and `BehaviorActionCatalog` with `ActionSchemaEntry.IsAiPrimitive` shipped (doc-sweep item I4).
- What remains unestablished is whether that picker spans **cross-asset blueprint** actions. The original claim came from a doc sweep of the *Behavior Architecture* plan, not from code.

### BP-54 — G7 resolver-authoring UX
**Complexity:** RW-M · **Confidence:** ⚠ **UNCLEAR — do not act on without re-scoping**
- Runtime resolver support exists (`BehaviorRegistry.RegisterResolver`, `ApplyResolverOverlay`, `BehaviorRegistry.cs:247-273`). No authoring UI surfaced — but "resolver-authoring UX" is not defined precisely enough in the source doc to verify as present or absent.

> **Both BP-53 and BP-54 are BTree/HSM behavior-authoring concerns, peripheral to blueprint editing.**
> They are retained for completeness but sit outside the "make blueprint editing fully functional"
> goal. Re-scope them against the Behavior Architecture plan before treating them as actionable.

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
