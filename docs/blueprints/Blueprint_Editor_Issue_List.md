# Blueprint Editor — Verified Issue List (2026-08-04)

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
| **BP-01** | Watch panel shows raw hex bytes | `MarshalFromBytes` is complete, unit-tested, already used at 4 sites in the same file | `WatchPanelWindow.cs:54-56` |
| **BP-02** | Comment **colour** + Bring-to-Front/Send-to-Back bypass undo | route through `view.Execute(fwd, inv, label)` like every other op in the file | `CanvasRenderer.cs:808-828` |
| **BP-03** | Bookmarks can't be renamed or deleted | `BookmarkStore.Remove` already exists; `Bookmark` is a `record` → rename is `b with { Label = x }` | `BookmarksPanel.cs:17-36` |
| **BP-04** | `Compare`/`BinaryOp`/`BooleanOp`/`Not` **cannot be placed** despite being lowered + compile-tested | 14 palette entries (one per enum value), baked at create — exactly the `MakeMath` / `ChannelCommandEntries` recipe. No drawer needed | `BlueprintNodePaletteEntries.All()` |
| **BP-05** | `ReadRankedResult.Rank` uneditable | plain `ImGui.InputInt` drawer; no catalog dependency | new drawer + `BlueprintEditorBootstrap` |
| **BP-06** | `WaitForChannel.ChannelType` uneditable | reuse `IChannelCommandCatalog`; `ChannelCommandNodeDrawer.cs` (109 lines) is a near-direct template | new drawer |
| **BP-07** | `CallCustomEvent.EventId` uneditable | reuse `UnifiedEventDiscovery.All()` (already production-wired) | new drawer |
| **BP-08** | `CallPeerBlueprint` target uneditable | reuse `BlueprintPeerSource.EnumerateAll()` + existing peer-signature lookup | new drawer |
| **BP-09** | 6 abandoned node kinds are **advertised in the palette** with inviting descriptions, but compile to a silent no-op | delete 6 `Make<T>` blocks | `BlueprintNodePaletteEntries.cs:100-105, 233-244` |
| **BP-10** | `When` → **EventFired** form is a `TextDisabled` stub | `_eventCatalog.GetEntries()` is *already injected and called* at `WhenNodeDrawer.cs:175` — just never rendered | `WhenNodeDrawer.cs:172-177` |

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
| **BP-26** | `When` → **ConditionMet** form | ⚠ **Not "wire the existing predicate builder."** `PredicateBuilderState` is referenced only by its own test, and `DataBreakpointManagerPanel.DrawPredicateEditor` is **read-only** (`Summarize` + `TextUnformatted`). Needs new ImGui editors for all 9 `SearchPredicateDto` subtypes. Open in Architect Q4 |
| **BP-27** | `ScoreDecision.AssetId` uneditable | no `UtilityDecisionDef` catalog exists anywhere editor-side — needs a discovery source before a picker. Open in Architect Q4 |
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

## Suggested order

1. **BP-16** + **BP-09** — stop silent data corruption and un-advertise dead nodes (cheap, correctness)
2. **BP-11** — undo unification (highest value; ends silent unrecoverable edits)
3. **BP-01 → BP-10** — the Tier-1 wiring sweep, all independent and parallelizable
4. **BP-12** — My Blueprint registration (panel stops lying about its features)
5. **BP-23** same-graph copy/paste, then **BP-13** align — the two biggest day-to-day ergonomics wins
6. **BP-24** function-graph create + graph switching — unlocks a capability already in the data model

Tier 3's BP-26/BP-27 are the only items needing an architect round; everything else can proceed
without one.
