# Blueprint Subsystem — Issue Tracker

> Checklist view. Full detail for every ID: **[Blueprint_Issues_Detail.md](Blueprint_Issues_Detail.md)**
> (IDs are searchable there). Grouped by **area**, sorted **cheapest-first** within each area.
> Nothing here is prioritised yet — priorities are the reader's call.

**Complexity:** `WIRING` = call existing code, no new logic · `RW-L` = real work, low (≲150 lines) ·
`RW-M` = real work, medium (new component / some design) · `RW-H` = real work, high (new subsystem or
architect decision first).
🔴 = correctness/data-loss issue, not an enhancement.

| Complexity | Count |
|---|---:|
| `WIRING` | 19 |
| `RW-L` | 23 |
| `RW-M` | 18 |
| `RW-H` | 2 |
| **Total actionable** | **62** |
| *(refuted on verification)* | *1* |

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

- [ ] **BP-59** 🔴 · `WIRING` — **Context-menu "Delete Node" is not undoable, but the Del key is.** `CanvasRenderer.cs:758` applies `RemoveNodes` raw; `EditCommands.cs` builds a proper inverse for the same intent. Silent unrecoverable data loss — *found in the verification pass*
- [ ] **BP-02** · `WIRING` — Undo bypassed via `view.Commands.Apply`. ⚠ *scope corrected:* **15 sites, not 10** — also pin "Reset to Default" (`:638`), comment delete (`:845`), "Promote to Variable" (`:970`)
- [ ] **BP-03** · `WIRING` — Bookmarks can't be renamed or deleted; `BookmarkStore.Remove` already exists
- [ ] **BP-23a** · `RW-L` — **No copy/cut/paste/duplicate on the canvas.** Paste is hard-disabled; `AddNodeCommand` already accepts a prebuilt `Node`, so paste can skip the 8-of-50 property whitelist
- [ ] **BP-13** · `RW-L` — No align/distribute/straighten; 9 commands declared, 0 implemented. `CommandBuilder.MoveNodes` is the ready primitive
- [ ] **BP-17** · `RW-L` — No node renaming/custom titles; `Subtitle => null` always. Every piece has a precedent to mirror
- [ ] **BP-18** · `RW-L` — Node body collapse hardcoded `false`; `SetNodeCollapsed` exists with a working reference impl
- [ ] **BP-19** · `RW-L` — No minimap; `ViewportState` already supplies the transform math
- [ ] **BP-20** · `RW-L` — No error list / jump-to-next-error; `NodeState.Error` flags and a cycle-and-centre pattern already exist
- [ ] **BP-56** · `RW-L` — No wire-level execution-flow highlighting (nodes glow, wires don't)
- [ ] **BP-23b** · `RW-M` — Cross-asset paste; needs variable/type re-resolution. Do after BP-23a
- [ ] **BP-25** · `RW-M` — Cross-blueprint search is cosmetic; `FindEngine` ignores its `scope` argument by its own docstring
- [ ] **BP-28** · `RW-M` — No advanced-pin hiding; needs a new persisted per-pin flag *and* an authoring UI

## Area B — Node authoring surface
*Whether a designer can place and configure each node kind. **13 of 50 kinds run but can't be configured.***
→ [detail](Blueprint_Issues_Detail.md#area-b--node-authoring-surface)

- [ ] **BP-04** · `WIRING` — `Compare`/`BinaryOp`/`BooleanOp`/`Not` **cannot be placed at all** despite being lowered + compile-tested. 14 baked palette entries, no drawer needed
- [ ] **BP-09** · `WIRING` — 6 abandoned node kinds are **advertised in the palette** but compile to a silent no-op. Delete 6 `Make<T>` blocks
- [ ] **BP-05** · `WIRING` — `ReadRankedResult.Rank` uneditable; plain `InputInt`
- [ ] **BP-06** · `WIRING` — `WaitForChannel.ChannelType` uneditable; reuse `IChannelCommandCatalog`
- [ ] **BP-07** · `WIRING` — `CallCustomEvent.EventId` uneditable; reuse `UnifiedEventDiscovery`
- [ ] **BP-08** · `WIRING` — `CallPeerBlueprint` target uneditable; reuse `BlueprintPeerSource`
- [ ] **BP-10** · `WIRING` — `When` → EventFired form stubbed; the catalog is *already injected and called*, just never rendered
- [ ] **BP-14** · `RW-L` — `Return.Status` uneditable (always Success); a `NodeStatus` combo, ~20-30 lines
- [ ] **BP-22** · `RW-L` — `GetParameter` cannot be placed; asset-specific, so needs a picker not a baked entry
- [ ] **BP-21** · `RW-L` — `When` → ValueChanged form stubbed; reuse `ComponentFieldReflector` + component pickers
- [ ] **BP-26** · `RW-L` — `When` → ConditionMet form stubbed. *Reclassified from REAL WORK:* a full StructEdit-generic predicate editor already exists in `Fdp.Presentation`, which the Blueprint editor already references
- [ ] **BP-27** · `RW-M` — `ScoreDecision.AssetId` uneditable; no `UtilityDecisionDef` catalog exists. ⚠ re-check against StructEdit pickers first — BP-26 was misclassified on identical reasoning

## Area C — Editor infrastructure
*Document, undo and panel plumbing.*
→ [detail](Blueprint_Issues_Detail.md#area-c--editor-infrastructure)

- [ ] **BP-12a** · `WIRING` — Drag-variable-into-graph as Get/Set is dead (`create-variable-get`/`-set` unregistered) — the most-used motion in Unreal authoring
- [ ] **BP-12e** · `WIRING` — Dead panel commands **fail silently**; `InvokeCreate` discards the result. Root cause of the whole BP-12 family's UX. *Tally: 14 commands invoked, 1 registered*
- [ ] **BP-11** ⭐ · `RW-L` — 🔴 **No inspector/drawer edit is undoable**, including the 2 written to be. Two undo stacks exist and are never bridged; Ctrl+Z drains the wrong one
- [ ] **BP-12b** · `RW-L` — Panel items can't be renamed/duplicated/deleted; a variable can be created but never removed
- [ ] **BP-12c** · `RW-L` — Custom events and dispatchers can't be created. ⚠ consider *removing* the dispatcher section instead (superseded)
- [ ] **BP-24** · `RW-M` — **No Function-graph create path; canvas locked to one graph.** In any multi-graph asset every graph but the first is unreachable through the UI. Data + compiler layers already support functions
- [ ] **BP-12d** · `RW-M` — `find-references` dead; overlaps BP-25's multi-graph layer
- [ ] **BP-57** · `RW-M` — Per-function local variables absent from the data model itself. Depends on BP-24

## Area D — Compiler & correctness
→ [detail](Blueprint_Issues_Detail.md#area-d--compiler--correctness)

- [ ] **BP-16** · `RW-L` — 🔴 **`ArrayMake`/`ArrayGet` produce a silent wrong value** — emit `default` with *no diagnostic at all*, unlike the exec-side BP4004 path. Compiles clean, returns wrong data. A ~30-line Stage2 validator converts it to a compile error
- [ ] **BP-15** · `RW-L` — 4 node kinds accept bad references silently (no Stage2 validator for `ScoreDecision`/`ReadRankedResult`/`CallCustomEvent`/`Cast`)
- [ ] **BP-32** · `RW-L` — `When` FallingEdge deferred for ValueChanged mode (live `// TODO M3`); falling-edge behaviours silently never fire
- [ ] **BP-58** · `RW-L` — `Cast` has no drawer (emit bug itself is **fixed**; July matrix is stale)
- [ ] **BP-33** · `RW-M` — `WaitForEvent` structurally broken: no `EventTypeId` satisfies both Stage2 and Roslyn. **Decide repair vs delete** — superseded by named `EventEntry` handlers

## Area E — Debug & diagnostics
*Strongest area — several capabilities **exceed** stock Unreal. Universal Breakpoints (Slice-2 D1) is **already built**: 128 unit + 25 integration tests pass.*
→ [detail](Blueprint_Issues_Detail.md#area-e--debug--diagnostics)

- [ ] **BP-29** · `WIRING` — 🔴 **LIVE BUG: blueprint conditional breakpoints silently never fire.** `PredicateCompiler` gets no `blueprintRegistry` at any of 3 production sites, so the predicate compiles to constant-false. Invisible to tests because they pass the registry explicitly. 2 one-liners + 1 needing plumbing
- [ ] **BP-01** · `WIRING` — Watch panel shows raw hex bytes; `MarshalFromBytes` is complete, tested, and used at 4 other sites in the same file
- [ ] **BP-35** · `RW-L` — D4 `MultiplexingProbeSink` missing; `IBlueprintProbeSink` exists, needs a composite
- [ ] **BP-37** · `RW-M` — `LifecyclePredicateDto` by `NetworkId` throws. ⚠ *raised on verification:* `INetworkEntityMap` **doesn't exist**; the concrete map lives in a network project Breakpoints doesn't reference → layering decision first
- [ ] **BP-36** · `RW-M` — D5 stack-frame inspection is Blueprint-local; lifting it would let BTree/HSM pauses carry a call stack
- [ ] **BP-38** · `RW-M` — D9 pause-on-Blueprint-exception. **Already LOCKED as deferred** by architect decision; rewind machinery is reusable
- [ ] **BP-39** · `RW-H` — D8 CLR/Visual Studio source-line debugger sync; no scaffolding present
- [ ] **BP-40** · `RW-H` — Library-dispatch graphs **structurally cannot** carry node breakpoints (probes need `self`; stateless Library functions have none). Deliberate, but a real authoring surprise. **Architect call — do not build speculatively**

## Area F — Runtime & state architecture
→ [detail](Blueprint_Issues_Detail.md#area-f--runtime--state-architecture)

- [ ] **BP-31** · `RW-L` — BTree lacks the concurrent-stateful validator HSM has; a Subtree twice under a `Parallel` is unguarded
- [ ] **BP-41** · `RW-L` — No test for two *different* AiPrimitive blueprints on one entity; coverage is by analogy only
- [ ] **BP-44** · `RW-L` — Custom Events 1d: no event-definition authoring UI
- [ ] **BP-30** · `RW-M` — 🔴 **HSM-hosted AiPrimitive blueprints collide** — they zero and re-init each other every tick, so neither retains state. BTree has the partition-slot mechanism (16 refs); HSM has **0** and no compose command
- [ ] **BP-45** · `RW-M` — Cross-entity event dispatch (`BlueprintDeferredEvent`) absent; the most-cited deferred capability
- [ ] **BP-42** · `RW-M` — Cross-entity shared-state **write** (read path shipped); deferred by design
- [x] ~~**BP-46** — Generic `GetShared<T>` partition-slot accessor~~ ❌ **REFUTED — already shipped.** `BlueprintSharedState.TryGetShared<T>` exists at `:58` and the compiler emits calls to it. No work required
- [ ] **BP-43** · `RW-M` — Custom Events 2b: events with no backing C# struct

## Area G — Documentation accuracy
*Cheap, and currently actively misleading.*
→ [detail](Blueprint_Issues_Detail.md#area-g--documentation-accuracy)

- [ ] **BP-47** · `WIRING` — `Blueprints_Overview.md:75` marks the 4 unplaceable value-op nodes ✅, conflating compiler and authoring axes
- [ ] **BP-48** · `WIRING` — Runtime DD §13.5 + Overview §1/§5 stale on AiPrimitive working state (wrong for BTree-composed nodes)
- [ ] **BP-49** · `WIRING` — Authoring guide describes cross-entity routing **as if current**; it doesn't exist (BP-45)
- [ ] **BP-50** · `WIRING` — Trackers contradict the code; the **v1.1 roadmap is fully superseded** — label it history, not status
- [ ] **BP-55** · `WIRING` — Asset-Browser delete affordance. ⚠ *lowered on verification:* `RefactorService.PreviewDelete` (with dangling-ref detection) already exists; every caller is a test fake, so only the UI affordance is missing
- [ ] **BP-51** · `RW-L` — DOC-3/DOC-4 illustrated SVGs (memory layout, lifetime timeline) missing
- [ ] **BP-52** · `RW-M` — UX-1…UX-5 authoring ergonomics unbuilt; UX-1/UX-2 need an architect nod first
- [ ] **BP-53** · `RW-M` ⚠ **UNCLEAR** — E6 cross-asset blueprint-action picker. *Partially refuted:* `[HsmActionPicker]` exists and is used throughout `HsmFacets.cs`; whether it spans cross-asset blueprint actions is unestablished. Peripheral to blueprint editing — re-scope before acting
- [ ] **BP-54** · `RW-M` ⚠ **UNCLEAR** — G7 resolver-authoring UX. Runtime `BehaviorRegistry.RegisterResolver` exists; "authoring UX" is too loosely defined in the source doc to verify. Peripheral — re-scope before acting

---

## Out of scope

- [ ] ~~Macros~~ — absent from the entire codebase; new capability, architect round required
- [ ] ~~Collapse-to-function / collapse-to-macro~~ — absent, and nothing to collapse into until BP-24
- [ ] ~~Squad-quartet & dispatcher lowering~~ — abandoned by design; remove rather than implement (BP-09)

## Needs an architect decision before scoping

`BP-40` · `BP-38` (already LOCKED as deferred) · `BP-52` (UX-1/UX-2) · `BP-27` *if* the StructEdit
re-check confirms no reusable picker.

## Confidence

**No unverified rows remain.** Every issue is hand-verified (**✔✔**) or spot-checked (**✔**), except
BP-53/BP-54 which are explicitly flagged **UNCLEAR**. Per-issue tags live in the detail file.

Four "nothing exists" claims were overturned across the audit — the predicate editing UI, Universal
Breakpoints, C1-for-BTree, and BP-46 — every one because a search covered `Hrot/` but not `FDP/`.
**Lesson for future work in this repo: absence claims must be checked across both trees.**
