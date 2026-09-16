<!--STATUS
state: LIVE
build-state: DISPATCH
updated: 2026-08-24
current-answer: dispatch pointer for the watch-variable-list finalization (UI lane) — wire grouping into the
  Watch window, add a group-by selector, persist the pin set, and give the concrete-vs-chameleon binding a
  real UI CHOICE. Carries no design: cites DESIGN_Variable_Watch_Pinning.md. ⛔ Restart survival (94g /
  NetworkId remap) is DEFERRED — it lands in HN-037's lane.
known-conflict: ✅ DISJOINT from the concurrent HN-037 allocator batch — the survey verified the watch-list
  files (Hrot.Editor.AiShared/**, Hrot.Diagnostics.Breakpoints/**) are NOT in HN-037's set, and the two lanes
  use different id prefixes (BP- here, HN- there). The ONE overlap (restart remap, §"deferred") is carved OUT.
-->
# HANDOFF — **watch-variable-list finalization** *(UI lane — the freeze owner)*

> 📌 **Dispatched at `304b9180e`.** ⛔ **Scope FROZEN at that sha.** ⭐ Branch fresh from the LATEST
> `claude/blueprint-authoring-status-6sr5ld` *(rule 7)*; **rule 1b: started-marker BEFORE any code.** ⛔ **No PR.**
> ⭐ ids **`BP-`**, tracker **Area A–G** *(the variable model)* — 📐 series stands at **`BP-498`**, so start at
> **`BP-499`**; state every id *(rule 5)*. ⚠ **BP- deliberately — this is the variable-model lane, and it keeps
> you clear of HN-037's `HN-` block.**

> ⭐⭐ **This IS within the variable-model freeze — and that is correct.** The freeze reserves the variable/watch
> model for ONE session *("no two implementations of one concept")*; you ARE that session *(the UI lane)*.
> ⭐ Sharing the shared implementation is the goal — fixes propagate to every consumer *(incl. CGF later)*, so
> unfinished parts are welcome to ship.

## 0. ⛔⛔ THE DESIGN IS THE SOURCE — this file is a POINTER

📄 **[`DESIGN_Variable_Watch_Pinning.md`](../DESIGN_Variable_Watch_Pinning.md)** *(LIVE, authoritative — "written
to be built from"; already shipped slices 94a–94f)*: §1 the record + *"grouping … already shared"*, §1b the
group-by modes, §3 the TWO-KIND entity binding, §5 persist-the-pin-set. 📄 Value-feed rules:
[`Architect_Question_46`](../Architect_Question_46_What_A_VariableRow_Means.md). ⛔ **`Architect_Question_40` is
DECISION-TRAIL ONLY — do not implement from it.** ⭐ These are finalization/wiring items on already-built,
already-designed machinery *(the "documented recipe" exception)* — ⛔ no new UML required; ⭐ report per obligation
③ and fold any as-built deviation into `DESIGN_Variable_Watch_Pinning.md` *(obligation ⑤)*.

## 1. ⭐⭐⭐ THE ITEMS — all DISJOINT from HN-037

| # | task | design | the one thing not to get wrong |
|---|---|---|---|
| ⭐ **①** | **Wire grouping into the Watch window** — pass `VariableRowGrouping.WatchDefault` *(`[Asset, Entity]`)* into the model at **`AiWatchWindow.cs:79`** *(today `new VariableTableModel(_pinned, VariableTableColumns.Watch)` with no groupBy ⇒ defaults to flat)* | §1 · `VariableRowGrouping.WatchDefault` exists | ⭐ the grouping ENGINE + the control's group-header rendering are ALREADY BUILT *(the Variables window uses them)* — this is a wiring line, ⛔ do NOT write a second grouping path |
| ⭐ **②** | **Group-by selector in the Watch window** — a toolbar control mirroring `AiVariablesWindow.GroupBy` *(`:145`)*, offering the §1b modes *(by-entity · by-asset-then-entity · ungrouped · by-section)* | §1b | ⭐ set `_model.GroupBy`; the modes are facet lists that already exist. ⛔ don't invent new facets |
| ⭐⭐ **③** | **Persist the pin set** — pinned rows are an in-memory `List` *(`PinnedVariableRowSource._pinned`)*; nothing saves/loads them. Extend **`DebugSessionPersistence`** to save/restore pinned rows, **entity-keyed** | §5 *("extend SaveWatches/LoadWatches; do not invent a second file")* | ⛔ `WatchPersistence` is `[Obsolete]` + breakpoint-only — ⛔ do NOT revive it. ⭐ persist by the restart-stable identity *(the `NetworkId` from ④, + the chameleon sentinel)*, not the raw `Entity` handle |
| 🔴🔴 **④** | **The concrete-vs-chameleon binding CHOICE** — today the model collapses onto `Entity` vs `default(Entity)` with **no way from the UI to choose**. Give the binding a real two-kind shape *(§3: **concrete** = a restart-stable `NetworkIdentity.Value (long)` + the in-session `Entity`; **chameleon** = the sentinel → selection)* and a **pin-time choice**: *"pin CONCRETE (the current selection)"* vs *"pin CHAMELEON (follows selection)"* | §3 · §4 *(the binding clock, built — `EntityBindingFrame`)* | ⭐ the chameleon path is ALREADY built *(sentinel + `EntityBindingFrame` + re-sample on selection change)* — ④ adds the **concrete** counterpart and the CHOICE. ⭐⭐ **Store the `NetworkId` for identity/persistence, resolve IN-SESSION via the captured `Entity`.** ⛔ **NOT the map-picker** *(§"deferred")* and ⛔ **NOT the restart remap** *(§"deferred")* — concrete = "the entity selected right now", captured |

## 2. ⛔⛔ DELIBERATELY DEFERRED — **so nobody reads silence as coverage**

| ⛔ deferred | ⭐ why |
|---|---|
| **the MAP-PICKER for an arbitrary concrete entity** *(§9c — pick an entity that is NOT the current selection)* | 📐 §9c is *"a lead, not a decision"* — it names `MapPickableEntityAttribute` but does not settle the mechanism ⇒ needs a **short architect nod** first *(coordinator will draft it — reuse `MapPickServiceBridge`?)*. ⛔ Not tonight; ④ ships the "current selection" concrete case, which needs no picker |
| 🔴🔴 **RESTART SURVIVAL / NetworkId remap** *(slice `94g`)* | 📐 **NOT disjoint from HN-037** — it edits `EditorSubsystem`/`EditorApplication` *(publish `StagingEntityExtractor`'s `oldToNewMap` on the orchestration bus)* + a `Fdp.Toolkits/Replication` resolver, and `DataBreakpointManager.cs:1354` still `throw`s for `NetworkId` *(no `INetworkEntityMap` wired)*. ⇒ ⛔ **sequence AFTER HN-037**; a concrete watch will not survive a scenario restart until then, which is fine for this batch *(state it)* |

## 3. ⛔ LANE, SCOPE & COLLISION

⭐ **Yours (UI lane, Area A–G):** `Hrot.Editor.AiShared/{Windows/AiWatchWindow, Variables/*, Selection/*}` ·
`Hrot.Diagnostics.Breakpoints/DebugSessionPersistence.cs`.

✅ **DISJOINT from the concurrent HN-037 batch** *(coordinator-verified `2026-08-24`)* — HN-037 touches
`EditorSubsystem` allocator wiring · `CgfSubsystem` · `ClusterMaster`/DDS/replication · `ScenarioFileService`/
`EditorApplication`/`IEditorLogic`; **none of the watch-list files are in that set**, and the `BP-`/`HN-` prefix
split means no id collision. ⛔ The one overlap — restart remap — is **carved out** *(§2)*.
⭐ **Rule 4: pull the coordinator branch before your final commit** *(HN-037 may land while you run)*.

⛔ **Not this batch:** the map-picker · restart survival *(§2)* · cross-asset watch in one panel *(§10 — OPEN,
architect decision)* · the breakpoint-watch/window-unification *(`AQ38`/`AQ44`, gated by `R-27`)*.

## 4. GATES

⭐ Standing contract *(rule 8)*: one row per gate · verbatim command · pass/fail/skip · **delta vs `304b9180e`** ·
`--no-build` column · every RED pre-existing **by name** · golden movement as a diff shape · `tracker-counts.py
--check` · `rulings-check.py` · `design-digest.py --check` · **the `BP-` ids you allocated** *(rule 5)*.

⭐⭐ **Row 8 — the rails that prove it:** ① a rail asserting the Watch list renders GROUP HEADERS *(by-entity /
by-asset-then-entity)*, shown RED by reverting the `WatchDefault` wiring; ③ a save→reload rail proving a pinned
row survives *(entity-keyed)*; ④ a rail proving a **concrete** pin stays on its captured entity while a
**chameleon** pin follows the selection — the two-kind choice made visible. ⛔ A rail never seen red is decoration.
📐 Baseline: the touched suites are `Hrot.Editor.AiShared.Tests` + the smoke/`PanelSnapshot` watch rails — name your baseline.

## 5. ⭐ WHEN YOU ARE DONE

⭐⭐ **Fold the as-built into [`DESIGN_Variable_Watch_Pinning.md`](../DESIGN_Variable_Watch_Pinning.md)** — §1/§1b
grouping now wired, §3 the binding's two-kind shape as built, §5 the pin-set persistence as built, and mark the
deferred slices *(map-picker, restart remap)* as the named remaining work. ⭐ State the `BP-` ids; ⛔ design
content in the design, the report points at it.
