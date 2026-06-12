# BTree / HSM Visual Editor — Reconciled State & Forward Plan

> **Status:** Authoritative working spec for the "make BTree/HSM visual editing usable" effort. Grounded in direct source verification on branch `blueprint-integ-1`. **Refreshed 2026-06-12** after the `main-toolbar-1` and `blueprint-finalize/BB1` merges landed in this tree.
> **Audience:** Lead (planning) + implementation agents (batch execution).
> **Supersedes for planning purposes:** the *substrate* assumptions in `BTree_Editor_NodeEditor_Host_Design.md`, `HSM_Editor_NodeEditor_Host_Design.md`, and `AI_Editor_Shared_Infrastructure.md`. Those remain the **feature/UX spec**; this doc reconciles them with the JSON substrate that actually landed (`BTree_HSM_JSON_Persistence_Detailed_Design.md`).
> **Decisions locked with lead:** (1) editing first, debugging is Phase 2; (2) BTree first, then HSM; (3) this doc is the single reconciled spec; (4) live action/condition catalog wiring is in-scope now; (5) richer showcase content + the appearance polish that surfaces it are in-scope; (6) validation surfaced **both** ways (Diagnostics table **and** inline canvas); (7) Starter recipe holds the minimal valid content.

---

## 1. How to read this — the doc reconciliation problem

`docs/blueprints` contains **two eras** of design that contradict each other on the substrate:

| Concern | Host-design docs (older) | JSON DD (newer — what shipped) |
|---|---|---|
| Source of truth | C# `.cs` is authoritative; *"No JSON in v1"* | `.btree.json` / `.hsm.json` are authoritative |
| Editor load path | Reflect compiled assembly → invoke `[BTreeDefinition]` → blob → project | Deserialize JSON → DTO → editor model |
| Layout / comments | `[BTreeLayout]` / `[HsmLayout]` attribute-methods | `EditorMetadata` blocks inside the JSON |
| C# role | Editor writes the committed `.cs` | C# is a generated `obj/` artifact (Roslyn generator), never committed |
| Round-trip self-test | emit `.cs` → Roslyn → reflect → re-project | JSON serialize↔deserialize byte-stability |

**Rule for this effort:** treat the host-design docs as the *feature and UX target* (pills, observer badges, containers, transition labels, inspector facets, validation rules) and this doc + the JSON DD as the *substrate of record*. Where a host doc describes a reflection/`[*Layout]` mechanism, it is **superseded** by the JSON path (§3).

---

## 2. Verified current state

### 2.1 Substrate — real and solid (not the problem)

- **NodeEditor library + all three extensions are implemented**, at `FDP/ExtDeps/NodeEdit/`. NodeAttachments (pills), ContainerNodes (nested/parallel states), and CustomCanvasRenderer (4-pass overlay pipeline) all have real models, commands (`GraphCommand.cs`), renderers, and demo scenarios (S34–S36). The hosts build on a genuine substrate.
- **JSON persistence landed** (PU-01…PU-06): emit core (`Hrot.AiEditor.Persistence`, netstandard2.0), DTOs, `BTreeJsonServices`/`HsmJsonServices`, the Roslyn generators over `*.btree.json`/`*.hsm.json`, the `[BlueprintRegistrar]` self-registration bridge, JSON load contributors, VisualId/StableId post-reload stitching.
- **Asset browser, create-new, Save/Save-All, menu/toolbar landed** via the merged `main-toolbar-1` work (see §2.5). The old per-editor `AssetBrowserWindow`s were retired; a unified folder-tree browser + NodeEdit-Tree asset picker + New-Asset dialog now serve all kinds.
- **Still not landed:** PU-09 (in-process quick reload for BTree/HSM — still routes through an MSBuild rebuild; latency-neutral vs. before, not the ≤100 ms target).
- **Integration is wired:** per-perspective workspace registrars, `AiGraphCanvasWindow` instances, and document factories exist for BTree and HSM ([EditorSubsystem.cs:1904](../../Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs#L1904), [:2088](../../Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs#L2088), [:2321](../../Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs#L2321)).

### 2.2 BTree editor — close to usable authoring

| Capability | State | Evidence |
|---|---|---|
| Canvas window registered & openable | **REAL** | `_btreeRegistrar` + `btreeCanvasWindow` wired in EditorSubsystem |
| Graph model / reversed-pin / pills→attachments projection | **REAL** | `BTreeGraphModel` projects nodes, links, and pills as NodeEditor attachments |
| Command sink: add/remove node, add/remove link, set property, pill CRUD | **REAL** | [BTreeCommandSink.cs](../../Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeCommandSink.cs) (now also B-4 node-owned-var cleanup on remove) |
| Decorator pills collapse/round-trip | **REAL** | projector + emit core |
| Projector, fluent emit core, auto-layout, link validator, validation rules | **REAL** | `BehaviorTreeAssetProjector`, `BTreeEmitCore`, `BTreeAutoLayout`, `BTreeValidator` |
| Inspector facets + facet edit service + **type-filtered param picker + Promote-to-variable** (BB1) | **REAL/WIRED** | `BTreeFacets`, `BTreeFacetMapper`; BB1 B-1…B-5 live ([EditorSubsystem.cs:1956](../../Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs#L1956)) |
| Action binding via Inspector (`BehaviorHashPicker`) | **WORKS** | wired to live `BehaviorRegistry` |
| New-asset / Save / Save-All | **DONE (external)** | `BTreeNewAssetService` + New-Asset dialog + toolbar Save (main-toolbar-1) |
| Save → write `.btree.json` (debounced) | **WIRED** | RegenerationScheduler flush → `BTreeJsonServices.Serialize` |
| **Live action/condition palette** | **GAP** | [BTreeNodeCatalog.cs:13](../../Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeNodeCatalog.cs#L13) is **static-only**; palette offers a generic "Action" node, not specific registered behaviors |
| **Validation feedback (live)** | **DARK** | validators not passed to the registrar (empty Diagnostics); `BTreeNodeModel.State` hardcoded `Normal` → no inline canvas feedback |
| **Node appearance richness** | **THIN** | `BTreeNodeModel.Category` hardcoded `FlowControl` (one color); pill `Label` = bare enum, `Glyph` = null (no ↺×3 / ⏲2s) |
| Edit loop speed | **WEAK** | reload via MSBuild rebuild (PU-09 not done) |

**Verdict:** BTree's edit loop works. The "unusable" feel is three missing affordances — **specific actions in the palette**, **live validation feedback**, and **thin node appearance** (which makes a rich tree look minimalistic). All low-risk.

### 2.3 HSM editor — structurally rich but **cannot author**

| Capability | State | Evidence |
|---|---|---|
| Move / reparent states, add/remove/reorder regions, attachments | **REAL** | [HsmCommandSink.cs](../../Hrot/Subsystems/AI/Hrot.Hsm.Editor/Host/HsmCommandSink.cs) |
| **Create state / delete state / draw transition / delete transition** | **MISSING** | [HsmCommandSink.cs:139,151](../../Hrot/Subsystems/AI/Hrot.Hsm.Editor/Host/HsmCommandSink.cs#L139) — `ApplyAddNode/AddLink` are `{ /* TODO */ }` |
| Container model (composite/parallel), transitions-as-links + labels, projector, validator, facets, history/final glyphs | **REAL** | `StateNode : IContainerNodeModel`, `HsmTransitionLink`, `HsmTransitionLabelRenderer`, `HsmHistoryGlyphsRenderer`, `HsmValidator` |
| Inspector facets + pickers incl. **transition** facets (HSM-TRANS) | **REAL/WIRED** | SE1/SE2/FIX-A/HSM-TRANS committed |
| Events table + Globals strip | **BUILT** (perspective wiring to verify) | `HsmEventsWindow`, `HsmGlobalsStrip` |
| Initial-state arrows | **TODO** | [HsmInitialArrowRenderer.cs:32](../../Hrot/Subsystems/AI/Hrot.Hsm.Editor/Renderers/HsmInitialArrowRenderer.cs#L32) (LCA highlight works; ⦿→ arrows unimplemented) |
| Region-conflict overlay | **REAL but UNFED** | `HsmRegionConflictsRenderer` complete; nobody calls `SetDiagnostics` |
| Validation feedback (live) | **DARK** | validators not registered; renderer unfed |
| Kernel-side builder support (`stableId`/`visualId`, `[HsmAction].Lane`) | **PRESENT** | no kernel blocker |
| Live action/guard "palette" | **N/A** | HSM actions aren't nodes — they're state/transition *properties*, bound in the Inspector (already wired) |

**Verdict:** HSM's headline blocker is the four command-sink create-ops. Until those land, the canvas is view/arrange-only, and most of its (built) rich visuals can't be exercised.

### 2.4 Content reality

Exactly **two** assets exist: [SampleScout.btree.json](../../Hrot/Subsystems/Hrot.AI.Behaviors/Trees/SampleScout.btree.json) (Sequence + two Waits; `Pills: []`) and [SampleGuard.hsm.json](../../Hrot/Subsystems/Hrot.AI.Behaviors/Machines/SampleGuard.hsm.json). The minimalistic canvas is therefore a **content** problem, not a wiring one — the renderers exist; nothing exercises pills/observer/subtree/regions. Treat content as greenfield.

### 2.5 Cross-workstream landscape (merged into this tree)

- **`main-toolbar-1` (Asset Browser + creation + toolbar) — landed.** Phases 0–8 done: unified folder-tree browser, NodeEdit-Tree asset picker, **`BTree/HsmNewAssetService` + New-Asset dialog + "Empty" recipe**, Save / Save-As / Save-All + `Ctrl+S`, subfolder-aware save, perspective/time/AI-debug toolbar groups. The old per-editor `AssetBrowserWindow`s were retired. → **Create-new, the browser, and Save UX are DONE externally; we consume them.** Recipes are on-disk `.btree.json`/`.hsm.json` the dialog clones; "Empty" is truly empty (no Root / no states).
- **`blueprint-finalize/BB1` (blackboard param authoring) — code-complete, blocked on us.** B-1…B-5 shipped (type-filtered `[BlackboardFieldPicker]`, Promote-to-node-owned-variable, StructEdit default editing, lifecycle, tooltip). Its only open item, **REVIEW-BB1** (running-editor visual smoke), is **PARKED — explicitly blocked on BTree/HSM visual-editor maturity, resume BTree-first.** → The relationship is **inverted**: BB1 is not a collision to avoid; it is finished work **waiting on this effort.** Usable BTree editing is the unblocker, and **REVIEW-BB1(BTree) is our acceptance north-star.**

---

## 3. Substrate reconciliation — what the host-design docs got superseded on

When implementing against the host docs, apply these substitutions (the table is the override; no need to re-edit the old docs):

1. **Load:** "reflect assembly → blob → project" → **deserialize JSON → DTO → `FromDto` → editor model** for editor-owned assets. The reflection/blob path survives only for hand-authored (markerless, read-only) `.cs` assets, plus post-reload VisualId/StableId *stitching* to recover runtime indices for the (Phase-2) debug overlay.
2. **Layout/comments:** `[BTreeLayout]`/`[HsmLayout]` methods → **`EditorMetadata` (X/Y/Comment/Collapsed/Color) and `Canvas` blocks in the JSON.**
3. **Emit:** the editor no longer writes committed `.cs`. The editor writes JSON; the Roslyn generator emits `CreateBuilder()` + the `[*Definition]` thunk + `[BlueprintRegistrar]` bridge to `obj/`.
4. **Round-trip CI test:** the `.cs` self-test is replaced by JSON serialize↔deserialize byte-stability + a migration-equivalence test.
5. **Create / Save / browse:** owned by `main-toolbar-1` (§2.5), not by the per-editor host code.
6. **Unchanged:** breakpoints are session-local (never persisted); identity model (Guid + FNV-1a-32); reference catalog / refactor over FQN keys.

---

## 4. Target — "usable authoring MVP" (editing first; debugging deferred)

**Phase 1 (this effort): a content author can build and persist a working tree/machine end-to-end without touching C#.**

Definition of done — **BTree** (the bar; HSM mirrors it):

1. Create a new `.btree.json` via the (existing) New-Asset dialog, optionally from a **Starter** recipe. *(done externally; we author the Starter content)*
2. Open it on the canvas. *(wired)*
3. Add/remove nodes, wire children, add decorator pills, reorder pills. *(command sink real)*
4. **Pick a real registered Action/Condition** from the palette — sourced live from the unified action catalog. *(GAP — primary Phase-1 work)*
5. Edit node/pill properties + bind params/Promote-to-variable in the Inspector. *(wired; BB1 live)*
6. See validation feedback on the canvas (outline + ⚠), in the inspector banner, and in the Diagnostics table. *(GAP — surfacing)*
7. The canvas *looks* like the design — category-colored nodes, pills with glyph + param, observer eye + OBSERVES badge, subtree black-boxes. *(GAP — content + appearance polish)*
8. Save → `.btree.json`; reopen reproduces the graph. Rebuild makes it runnable via the `[BlueprintRegistrar]` bridge. *(wired; SampleScout verified to register + tick)*

**Acceptance north-star:** once the BTree bar is met, **REVIEW-BB1(BTree)** can finally run (§2.5).

**Explicitly out of Phase 1 (deferred to Phase 2 — debugging):** live runtime overlay, breakpoints/stepping, heatmaps, trace-timeline population, `GetCurrentStateSnapshot()` kernel wiring. The renderer scaffolding stays; it just isn't fed until Phase 2.

---

## 5. Forward plan — batch breakdown (BTree-first)

Ordering: prove the loop on BTree (unblocks REVIEW-BB1), then port the closure to HSM.

**Track A — BTree to usable (do first):**
- **EB-A — Showcase asset + Starter recipe.** Author a showcase `.btree.json` exercising every built feature (ObserverSelector + OBSERVES badge, Sequence/Selector, stacked decorator pills, Action + Condition bound to real behaviors, Wait, Subtree black-box). Author a minimal **Starter** recipe (Root + an empty Sequence) so a new tree opens with something to build on. *(content)*
- **EB-B — Node-appearance polish.** Map `BTreeNodeModel.Category` by kind (composite/leaf/decorator colors); render pill glyph + param (↺×3, ⏲2s) instead of the bare enum name. *(small host fix — makes a rich tree stop looking minimalistic)*
- **EB-C — Live action/condition palette.** Wire the already-built unified action catalog (`IActionSchemaExporter` / `IBehaviorActionCatalog`, present in this tree) into `BTreeNodeCatalog` so the palette lists **specific** Actions/Conditions (+ Blueprint-hosted AiPrimitives), searchable, per host-doc §5.1. Placing one bakes the action; decorators stay attach-to-node, never free nodes. Re-query on `IAssetCatalog.Changed`.
- **EB-D — Validation surfacing (both).** Pass `BTreeAssetValidator` into the BTree registrar's `validators:` arg (empty today — [EditorSubsystem.cs:1904](../../Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs#L1904)) for the Diagnostics table; wire per-node severity into `BTreeNodeModel.State`/`StatusTooltip` (hardcoded `Normal` today) for inline canvas outline + ⚠ + inspector banner (host §11.2).
- **EB-E *(optional, can defer)* — In-process quick reload for BTree (PU-09)** via the shared emit core + `[BlueprintRegistrar]` masquerade, to get the fast edit loop.
- **REVIEW-BB1(BTree)** — runs once EB-A…D land (owned by blueprint-finalize; our work unblocks it).

*De-scoped (done externally): create-new, asset browser, explicit Save UX.*

**Track B — HSM to usable (after Track A pattern is proven):**
- **EH-01 — Command-sink create-ops** (the keystone). Implement `ApplyAddNode` (create state, incl. promote-to-composite), `ApplyRemoveNodes`, `ApplyAddLink` (create a `TransitionNode` with sidecar metadata, respecting `HsmLinkValidator`), `ApplyRemoveLinks` create-path, `ApplySetContainerCollapsed`. Unblocks all authoring.
- **EH-02 — Initial-state arrows.** Finish the `HsmInitialArrowRenderer` TODO so the ⦿→ initial-child markers render (you can't author an HSM without seeing/setting initial state).
- **EH-03 — Validation surfacing (both + renderer feed).** Validators into the registrar + node `State`/tooltip + inspector banner, **and** feed `HsmRegionConflictsRenderer.SetDiagnostics` so lane-conflict overlays appear.
- **EH-04 — Showcase + Starter recipe.** Showcase `.hsm.json` (composite + parallel/regions + transitions + history + final + events + globals); Starter recipe = one Simple state flagged Initial.
- **EH-05 — Appearance/loop hardening.** Verify container/transition/label rendering on real content; confirm Events-table/Globals-strip perspective wiring; end-to-end authoring test.
- **DEBT-BF-04 *(design call, prerequisite for REVIEW-BB1 on HSM states)*** — BB1's param-binding picker covers HSM **transitions/globals only, not states**. A state's 4 action slots (Entry/Exit/Activity/Timer) need a per-slot "one DTO → one variable" extension. **Needs an architect design decision** (not an autonomous guess); resolve as part of / just before the HSM visual pass.
- **REVIEW-BB1(HSM)** — after EH-01…05 + DEBT-BF-04.

**Phase 2 (separate effort) — Debugging:** kernel-snapshot wiring for both sessions, runtime overlay/heatmap/trace lanes, breakpoints/stepping. Plus the deferred substrate nicety PU-09 if not already done.

---

## 6. Verify-checks — RESOLVED

The four design-phase verifications and their answers:

1. **Inspector action binding works?** **YES.** `BehaviorHashPicker` is wired to the live `BehaviorRegistry`; BB1 added a type-filtered param picker + Promote on top. Only the **palette** lacks specific actions (→ EB-C).
2. **SampleScout registers + ticks after rebuild?** **YES.** The generated `[BlueprintRegistrar]` bridge builds the blob → `Interpreter` → registers into `BehaviorRegistry`. Runtime path real.
3. **Validation surfaced live?** **NO — dark.** Validators aren't passed to the registrars; `BTreeNodeModel.State` is hardcoded `Normal`; HSM's conflict renderer is unfed (→ EB-D / EH-03).
4. **New-asset flow?** **DONE externally** (`main-toolbar-1`). "Empty" recipe is truly empty → we ship a Starter recipe (→ EB-A / EH-04).

Residual verify-at-implementation items: confirm Events-table/Globals-strip are registered into the HSM perspective (EH-05); confirm the unified action catalog is populated in the running editor (EB-C).

---

## 7. One-paragraph summary

The substrate (NodeEditor + extensions + JSON persistence + the now-merged unified browser/create-new/Save) is real and solid; this is a *finish-the-host* job, not a re-architecture. **BTree** can almost author today — its gaps are the static node catalog (no specific actions in the palette), dark validation, and thin node appearance; all low-risk (EB-A…D). **HSM** cannot author at all until four `TODO`-stubbed command-sink methods are implemented (EH-01); everything around them is built, plus one architect design call (DEBT-BF-04) for HSM-state param binding. The work now has a clear purpose chain: **usable BTree editing → unparks REVIEW-BB1(BTree) → validates the finished BB1 investment** — which is exactly why BTree goes first.
