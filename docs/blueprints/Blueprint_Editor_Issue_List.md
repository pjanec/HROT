# Blueprint Editor — Verified Issue List (2026-08-04)

> ## ⛔ SUPERSEDED — do not use for status
>
> This is the **original** verified issue list, frozen as written on 2026-08-04. It records what the
> audit believed at the time, including the items it got wrong.
>
> **Live status lives in [Blueprint_Issues_Tracker.md](Blueprint_Issues_Tracker.md)** (checklist) and
> [Blueprint_Issues_Detail.md](Blueprint_Issues_Detail.md) (per-item evidence + `DONE` notes).
> To resume the programme, start at
> [Blueprint_Gaps_Programme_RESUME.md](Blueprint_Gaps_Programme_RESUME.md).
>
> ⚠ **Some "Fix" columns below are wrong** and were corrected in-repo while building — the
> corrections table in the detail doc lists all ten. The sharpest: **BP-07's** *"reuse
> `UnifiedEventDiscovery.All()`"* would have produced a picker whose every choice failed to resolve,
> because a custom event is asset-scoped (`asset.CustomEvents`), not an engine event. **Re-derive any
> claim here against code before building on it.**
>
> The ID scheme is still current — BP-xx numbers in the tracker refer to these rows.


> **Goal:** make blueprint editing fully functional and pleasant to use.
> **Scope:** wiring of existing/easy-to-add capability + high-value ergonomics.
> Macros and collapse-to-function are **out of scope** (new capability, no data model).
> Every row below was verified against code. Effort classes:
> **WIRING** = call existing code / register a handler · **SMALL** = <~150 lines new logic ·
> **REAL WORK** = new subsystem or design decision needed.
> Companion analysis: [Blueprint_Gaps_And_QoL_Audit.md](Blueprint_Gaps_And_QoL_Audit.md).

## Tier 1 — WIRING (existing infrastructure, just not called)

| ID | Issue | Fix | Insertion point |
|----|-------|-----|-----------------|
| ✅ **BP-01** | Watch panel shows raw hex bytes | 🔴 **The premise was wrong and that is why it stayed open**: `MarshalFromBytes` was complete for PRIMITIVES only — 4 of the 18 offerable types fell through to `return bytes`, and 3 more failed `ResolveType` and were **silently skipped**. ✅ **Closed Batch 65 (`S3`) at the marshalling site**, as `Q32-C` ruled — a struct arm + an assembly-walking `ResolveType`, so three surfaces improve at once. See `BP-254` | `BlueprintDebugSession.cs` |
| **BP-02** | Comment **colour** + Bring-to-Front/Send-to-Back bypass undo | route through `view.Execute(fwd, inv, label)` like every other op in the file | `CanvasRenderer.cs:808-828` |
| **BP-03** | Bookmarks can't be renamed or deleted | `BookmarkStore.Remove` already exists; `Bookmark` is a `record` → rename is `b with { Label = x }` | `BookmarksPanel.cs:17-36` |
| **BP-04** | `Compare`/`BinaryOp`/`BooleanOp`/`Not` **cannot be placed** despite being lowered + compile-tested | 14 palette entries (one per enum value), baked at create — exactly the `MakeMath` / `ChannelCommandEntries` recipe. No drawer needed | `BlueprintNodePaletteEntries.All()` |
| **BP-05** | `ReadRankedResult.Rank` uneditable | plain `ImGui.InputInt` drawer; no catalog dependency | new drawer + `BlueprintEditorBootstrap` |
| **BP-06** | `WaitForChannel.ChannelType` uneditable | reuse `IChannelCommandCatalog`; `ChannelCommandNodeDrawer.cs` (109 lines) is a near-direct template | new drawer |
| **BP-07** | `CallCustomEvent.EventId` uneditable | reuse `UnifiedEventDiscovery.All()` (already production-wired) | new drawer |
| **BP-08** | `CallPeerBlueprint` target uneditable | reuse `BlueprintPeerSource.EnumerateAll()` + existing peer-signature lookup | new drawer |
| **BP-09** | 6 abandoned node kinds are **advertised in the palette** with inviting descriptions, but compile to a silent no-op | delete 6 `Make<T>` blocks | `BlueprintNodePaletteEntries.cs:100-105, 233-244` |
| **BP-10** | `When` → **EventFired** form is a `TextDisabled` stub | `_eventCatalog.GetEntries()` is *already injected and called* at `WhenNodeDrawer.cs:175` — just never rendered | `WhenNodeDrawer.cs:172-177` |
| **BP-29** 🔴 | **LIVE BUG — blueprint conditional breakpoints silently never fire.** `PredicateCompiler`'s 3rd ctor arg `blueprintRegistry` defaults to null, and `CompileBlueprintVariablePredicate` then returns `static (_, _) => false` (`PredicateCompiler.cs:235-237`). All 3 production sites omit it — so `BlueprintVariablePredicateDto`, the exact predicate "Add Conditional Data Breakpoint…" synthesizes, always evaluates false | pass the existing registry as the 3rd arg | `EditorSubsystem.cs:994` and `CgfSubsystem.cs:555` are **one-liners** (`_blueprintRegistry` already in scope, 19 / 7 refs). `ReplayBrowserSubsystem.cs:641` has **no** registry field — needs plumbing first |

## Tier 2 — SMALL

| ID | Issue | Fix | Notes |
|----|-------|-----|-------|
| **BP-11** ⭐ | **No inspector/drawer edit is undoable** — including the two written to be | see [§Undo](#the-undo-defect) | highest value; closes a whole class of silent unrecoverable edits |
| **BP-12** | My Blueprint panel: **13 of 14 commands unregistered**, failing silently | register handlers; start with `create-variable-get/-set` (node creation — reuse the palette/`AddNode` path) and `rename/duplicate/delete-item` | panel currently advertises features it doesn't have |
| **BP-13** | No align / distribute / straighten (9 commands declared, 0 implemented) | `CommandBuilder.MoveNodes(...)` is the exact batch-move-with-inverse primitive already used by drag; AABB-of-selection pattern exists in `ViewCommands.cs:71-86` | <100 lines |
| **BP-14** | `Return.Status` uneditable (always Success) | combo over `NodeStatus`, mirroring `WhenNodeDrawer.DrawModeSelector` | ~20-30 lines |
| **BP-15** | No Stage2 validators for `ScoreDecision` / `ReadRankedResult` / `CallCustomEvent` / `Cast` — bad references accepted silently | template: `V_WaitNodeReferences` (`Stage2_Validate.cs:587-615`) | ~30 lines each |
| **BP-16** 🔴 | `ArrayMake`/`ArrayGet` **silent wrong value** — pure-value fallback emits `default` with *no* `Diagnostics.Add`, unlike the exec-side fallback which emits BP4004 | a Stage2 validator rejecting both kinds turns silent data corruption into a compile error — far cheaper than lowering | ~20-40 lines. Also covers BP-09's 6 kinds |
| **BP-17** | No node rename; `Subtitle => null` always | every piece has a precedent: `InteractionState.RenamingComment` inline-rename UX, and `SetNodeProperty` undo plumbing proven end-to-end for `"Comment"` | add `NodeMetadata.CustomTitle`, `"Title"` case, F2 menu item |
| **BP-18** | Node collapse hardcoded `false` | `GraphCommand.SetNodeCollapsed` already defined, with a working reference impl in `NodeEditor.Demo/FakeBlueprint/FakeCommandSink.cs:126` | ⚠ sink's `default:` case silently no-ops unknown commands today |
| **BP-19** | No minimap | `ViewportState` supplies all transform math (`GraphToScreen`/`FrameRect`/…) | ~150-200 lines incl. click-to-pan |
| **BP-20** | No error list / jump-to-next-error | `NodeState.Error` flags exist and `FindEngine` already filters on them; `FindBar.Next/CenterOnActive` is a ready cycle-and-center pattern | decide diagnostics source (compile-time vs live) |
| **BP-21** | `When` → **ValueChanged** form stubbed | reuse `ComponentFieldReflector` + existing component pickers | |
| **BP-22** | `GetParameter` cannot be placed | asset-specific (`asset.Parameters`), so it needs a picker rather than a baked entry | ~60-100 lines |

## Tier 3 — REAL WORK (design decision or new subsystem)

| ID | Issue | Why it's bigger |
|----|-------|-----------------|
| **BP-23** | Copy / cut / paste / duplicate — **entirely absent**, Paste hard-disabled | See [§Copy-paste](#copy-paste-is-cheaper-than-it-looks). **Same-graph paste is upper-SMALL**; cross-asset paste is REAL WORK (VariableId/type re-resolution) |
| **BP-24** | No Function-graph create path; canvas binds to one graph permanently → **every graph but the first is unreachable in a multi-graph asset** | data + compiler layers already support it; needs a create command + a graph-switch concept in `BlueprintDocumentFactory` |
| **BP-25** | Cross-blueprint search is cosmetic (`FindEngine` ignores `scope`) | `FindEngine`/`FindBar` are architecturally single-graph-bound; needs a multi-graph aggregation layer + cross-tab navigate |
| **BP-27** | `ScoreDecision.AssetId` uneditable | no `UtilityDecisionDef` catalog exists anywhere editor-side — needs a discovery source before a picker. ⚠ re-check against StructEdit picker infra before accepting this class (see BP-26's correction) |
| **BP-30** | **HSM-hosted AiPrimitive blueprints still collide** — see [§C1](#slice-2-c1--aiprimitive-concurrent-working-state) | needs an `HsmBridgeEmitCore` analogue of the BTree emitter + an HSM compose command |

> **BP-26 moved to Tier 2 — it was misclassified.** A complete predicate *editing* UI already exists at
> `FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/ReplaySearchPanel.cs` (587 lines): 7 modes
> including **Compound** AND/OR trees, save/load presets, built generically on StructEdit
> (`_editService.Open(dto, type)` → `ComponentEditDrawer` + a per-type drawer dictionary, incl. a
> recursive `PredicateValueFieldDrawer`). It is **not** hand-written per subtype.
> **`Hrot.Blueprints.Editor` already references `Fdp.Presentation`** (csproj line 26), `WhenNodeDrawer`
> already has `IPredicateCompiler` injected, and `ConditionMetPayload.Condition` is already designed to
> hold a `SearchPredicateDto` tree as JSON. Job = open an edit session, render with `ComponentEditDrawer`,
> serialize to the node field. Residual risk is layout/sizing (panel-width UI inside a node drawer) and
> swapping replay-recording sources for blueprint ones (`ComponentTypeProvider` exists). **SMALL.**
>
> *(The earlier "no predicate UI exists" finding searched only `Hrot/` and missed `FDP/Engine/`.
> `PredicateBuilderState` being orphaned and `DataBreakpointManagerPanel` being read-only were both
> true — they were simply the wrong surface.)*
| **BP-28** | Advanced-pin hiding | needs a new persisted per-pin flag *and* an authoring UI to mark pins advanced; no "which params are advanced" concept exists |

## The undo defect

Not "drawers bypass undo" — the real shape is **two stacks that are never bridged**:

| | |
|---|---|
| `IEditService` (what every drawer holds) | exposes **only** `MarkDirty`. The undo API lives on the *concrete* `EditService`, reachable only by downcast — `SharedNodeDrawers.cs:248` does exactly that |
| `EditService.RecordPropertyEdit` | **fully implemented**, real `PropertyEditCommand` apply/undo pair — but records onto Hrot's `CommandHistory` |
| `CommandHistory.Undo/Redo` | **never called from any UI path** (only tests) |
| Ctrl+Z | → `view.UndoLast()` → NodeEdit's `UndoStack` — *a different stack* |

⇒ **No drawer/inspector edit is undoable, including the 2 sites written to be.** Structural edits
*are* undoable (`view.Execute` → `UndoStack.ApplyAndRecord` stores a forward/inverse pair).

**Fix:** (1) promote `RecordPropertyEdit` + `NotifyStructureChanged` onto `IEditService`;
(2) re-point the implementation at `view.Execute(fwd, inv, label)` so edits land on the single live
stack (needs the document's `GraphView` in `EditServiceContext`); (3) convert the ~10 `MarkDirty`-only
edit sites across 6 drawers; (4) `CommandHistory` then becomes genuinely dead and can go.

> ⚠ Do **not** simply delete `CommandHistory` today — its `Execute()` performs the actual mutation, so
> it is load-bearing until step 2 lands. It is a bounded 64-entry ring; no leak.

## Copy-paste is cheaper than it looks

The obvious path — extend `BlueprintCommandSink.ApplyInitialProperties` — is a trap: it whitelists
only **8 of 50** node kinds, so paste would silently drop configuration on the other 42.

The cheap path avoids it entirely:

- `Node` is already `[JsonPolymorphic]` with `[JsonDerivedType]` for every subtype → JSON round-trip is **free**.
- `IClipboard` exists, is DI-wired, and has **zero call sites** — an unused OS-clipboard abstraction.
- `BreakpointJsonClipboard.cs` is an in-repo precedent for exactly this JSON-clipboard pattern.
- **`AddNodeCommand(Graph, Node)` takes a fully-built `Node`** and its `Execute()` is
  `_graph.Nodes.Add(_node)` — so paste can deserialize → new GUID → insert, never touching the whitelist.
- Pins are regenerated by `NodePinSchema.GetCanonicalPins`, so no pin-GUID preservation is needed.

Remaining real work: new node IDs + internal link remapping, and a command variant that carries a
prebuilt `Node`. **Scope same-graph paste first**; cross-asset paste (variable/type re-resolution) is
a separate, larger job.

## Slice-2 deep dives

### Universal Breakpoints (D1) — **already built**, not a pending slice

128 unit + 25 integration tests pass (verified by running them, not just reading). Present and wired
into `EditorSubsystem` / `CgfSubsystem`: `DataBreakpointManager` (1090 lines), `DataBreakpointSystem`
(PostSimulation, `QueryDelta`-gated), `DebugSnapshotProvider`, **forward-snapshot rewind exactly as
spec'd** (triple-buffer `_preTick`/`_postTick`/`_live` + `EntityRepository.SyncFrom`), event
breakpoints via a distinct `EventScannerCompiler` path, deferred live-edit mutation through an ECB,
hot-reload auto-rebind, and a reference-counted zero-cost gate when nothing is armed. Node-granular
sub-tick stepping (incl. Step Back) goes *beyond* what §5 asked for.

The blocker is **BP-29**, not missing features. Genuinely missing:

| Gap | Effort |
|---|---|
| D4 `MultiplexingProbeSink` (multi-debugger fan-out) — `IBlueprintProbeSink` exists, needs a composite | SMALL |
| D5 stack-frame inspection is Blueprint-session-local; lifting it to `IDataBreakpointManager` would let BTree/HSM pauses carry a call stack | SMALL–REAL WORK |
| `LifecyclePredicateDto` by `NetworkId` — throws `NotSupportedException`; needs `INetworkEntityMap` injected | SMALL |
| D9 pause-on-exception — rewind machinery is reusable; **explicitly deferred by architect decision** (Debug Protocol DD §13.3) | SMALL–REAL WORK |
| D8 CLR/VS source-line debugger sync | REAL WORK |

⚠ **Library-dispatch graphs cannot carry node breakpoints** — `StatementEmitter.cs:944-951` suppresses
probes when `!HasSelfInScope`, since Library functions are stateless and have no `self`. Deliberate,
but a real surprise if an author expects to breakpoint a Library graph. Architect call, not a build.

### Slice-2 C1 — AiPrimitive concurrent working-state

**Built for BTree, still broken for HSM.** BTree-composed AiPrimitives get per-node FNV-1a slot keys
(`FNV(assetId, nodeVisualId)`, or `FNV(assetId, variableId)` for Behavior scope) over the
`BlueprintBlackboard{1024,4096,16384}` partition tiers, and `BTreeCommandSink.ComposeAiPrimitiveAction`
auto-creates a distinct `Role=State, Scope=Node` host variable per placement — so two blueprints, or
one placed twice, separate correctly. Option β's Fix-1/Fix-2/`ClearBehaviorEvent` detach are all shipped
and tested.

Verified asymmetry — the two hosts have **opposite** halves of the solution:

| | partition-slot mechanism | concurrent-stateful validator |
|---|:--:|:--:|
| **BTree** | ✅ 16 refs in `BTreeBridgeEmitCore` | ❌ none (only `NestedParallel`) |
| **HSM** | ❌ **0 refs** in `HsmBridgeEmitCore`; no compose command | ✅ `CheckConcurrentStatefulSubtrees` + `CheckConcurrentSharedScopeKeys` |

- **BP-30 (REAL WORK):** HSM-hosted AiPrimitives still use the legacy fixed offset (`Blackboard1024`+8,
  one 8-byte `StructureHash`). Two stateful AiPrimitives on one HSM entity alternately `InitBlock`-zero
  and re-init each other every tick — **neither retains state**. Reuses the FNV key math verbatim; needs
  a new emitter surface + compose command.
- **BP-31 (SMALL):** port HSM's concurrent-stateful validators to `BTreeValidator` — a Subtree
  referenced twice under a `Parallel` is currently unguarded.
- **Test gap:** no test covers *two different* blueprint-authored AiPrimitive assets concurrently on one
  entity. Coverage is by analogy (`T20` uses hardcoded actions on the same rail; `T35` uses the same
  blueprint 3×). Worth a direct proof test.
- **Doc drift:** `Blueprint_Subsystem_Runtime_Detailed_Design.md` §13.5 and `Blueprints_Overview.md`
  §1/§5 still describe AiPrimitive working state as living only in `Blackboard1024` — true for the
  legacy/HSM path, wrong for BTree-composed nodes.

## Suggested order

0. **BP-29** — one-line fix restoring blueprint conditional breakpoints, which are silently dead today
1. **BP-16** + **BP-09** — stop silent data corruption and un-advertise dead nodes (cheap, correctness)
2. **BP-11** — undo unification (highest value; ends silent unrecoverable edits)
3. **BP-01 → BP-10** — the Tier-1 wiring sweep, all independent and parallelizable
4. **BP-12** — My Blueprint registration (panel stops lying about its features)
5. **BP-23** same-graph copy/paste, then **BP-13** align — the two biggest day-to-day ergonomics wins
6. **BP-24** function-graph create + graph switching — unlocks a capability already in the data model

Tier 3's BP-26/BP-27 are the only items needing an architect round; everything else can proceed
without one.
