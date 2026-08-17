# PLAN — what is left *(revision 17, `2026-08-17`)*

> ⭐⭐⭐ **REVISION 17 (`2026-08-17`).** ✅ **Batch 73 MERGED at `0808253e4`** — the 12 red scenario tests
> **diagnosed and quarantined with named causes** · ⭐⭐ **`E0` gained a GENERATED-CODE tier** whose
> acceptance test proves it reaches thunk ids · the HSM slot order is now by construction (§4A8).
> ⛔ **`E3` escalated a SECOND time, with the census: 55 attributed methods / 25 directories / 5
> emitters / 13 kernel sites.** ⇒ 🔴 **NEW: 📄 [`Architect_Question_35`](Architect_Question_35_Hsm_Occurrence_Delivery.md)** — ⭐⭐ **the delegate need NOT widen; the
> occurrence can ride `HsmCommandWriter`, a kernel-owned struct already passed to every action.**
> ⛔⛔ **My "the HSM did not clear locomotion" reading was WRONG** — that test is a **casualty of a
> phase-2 failure**, not HSM evidence.
>
> **REVISION 16 (`2026-08-17`).** ✅ **Batch 72 MERGED at `14f8b0ea4`** — **`E6`(A) shipped** ·
> BTree corpus **shape tier** · ⛔ **`E3` ESCALATED** · ⛔ **multi-occurrence NOT STARTED** (§4A7).
> ⛔⛔ **USER RULING: blueprint multi-occurrence is DEFERRED** — *"too many files affected, we can skip
> it, could be done sometime later once really needed."* ⭐ **`Q34`'s ANSWERS stand; only the build is
> deferred.**
> 🔴🔴 **`E3` is NOT a signature widening — it is a STORAGE MOVE.** Two occurrences have **one home by
> construction**: the thunk resolves its DTO at `bb.BehaviorParameters[0] + <baked offset>`.
> 🔴🔴 **And I found a red suite nobody was gating: `Fdp.Examples.Scenarios.Tests` — 12 failures,
> PRE-EXISTING** *(identical on the pre-batch tree, so ⛔ not a regression)*.
>
> **REVISION 15 (`2026-08-17`).** ✅ **Batch 71 MERGED at `bdd05a0dc`** — **`E0` the HSM golden
> harness** *(two tiers, and it is asserted that it CAN fail)* · `E1`/`E2` backfilled · `E7b`'s count
> half · `E6` **PARTIAL by escalation** (§4A6).
> 🔴🔴 **The floor found a live defect the moment it existed: HSM actions addressed by FQN in the blob
> and by simple-name hash in the registrar ⇒ `HsmShowcase`'s entry and activity actions SILENTLY DO
> NOTHING.** ⭐⭐ **RULED (A): FQN everywhere.**
> ✅ **`Q34` RESOLVED with the user — and BUILD IT NOW.** ⭐⭐⭐ **Plus the refinement their question
> forced: three occurrence cases, TWO mechanisms — the dangerous one is `E3`'s and does NOT need
> `Q34`'s bytes.** ⭐ **And a ruling `E5` inherits: it provisions by KEY, not by attach.**
>
> **REVISION 14 (`2026-08-17`).** ✅ **Batch 70 MERGED at `0b2b55380`** — `DEBT-AIB-021` · **the
> Instance params seam** · `G7`+`W10` (§4A5). ⭐⭐ **The parameter model now RUNS**: an Instance's params
> live in its own slot at `[Cursor 16][Params N][State M]`, the attach event carries the JSON, and the
> **same `ParseParamsDelegate`** a behaviour uses resolves it before commit. ⭐⭐⭐ **`BP1031` RETIRED —
> coordinator-reviewed and ACCEPTED**: its own message named its reason (*"nothing supplies them at
> spawn"*) and this batch makes that reason false. ⚠ **`DEBT-AIB-030` widens** — a **fourth** distinct
> test, and the first outside the AI registries.
>
> **REVISION 13 (`2026-08-17`).** ✅ **Batch 69 MERGED at `72f24d326`** — `C-tick` · `DEBT-AIB-009` ·
> `C-watch` · `C-outline` · **`E4` finished** (§4A4). ⭐⭐ **Track C is LIVE** — the highlight has a real
> per-`(asset, entity)` tick, held in a **side table owned by `Fdp.Toolkits`**, so it costs the sim
> nothing and cannot move `StructureHash`. 🔴 **My `C-watch` §7 claim was stale twice and the real defect
> was underneath** — corrected in the design. ⭐⭐⭐ **The silent-default pattern is now a repo rule**
> *(`.claude/CLAUDE.md`)*: **a production caller that HAS a dependency must PASS it.**
>
> **REVISION 12 (`2026-08-16`).** ✅ **Batch 68 MERGED at `79f23be63`** — `C-table` · `C-dialog` ·
> `W7b` · `E4` (§4A3). 🔴 **The tick unit is WORLD and no per-asset tick exists** ⇒ new item **`C-tick`**;
> the highlight is **inert until it lands**. 🔴🔴 **`DEBT-AIB-021` contradicts `DESIGN_Parameter_Model`
> §3.2** — the overlay is NOT implemented on the generated managed-asset path; **corrected there**.
>
> **REVISION 11 (`2026-08-16`).** ✅ **Batch 67 MERGED at `f52b1af15`** — `W7c` · `W7a` · `G3` ·
> **`E1`+`E2`** · the owed rail · **the twice-carried latency rail** (§4A2).
> ⛔ **`G3` was ALREADY SHIPPED** — I corrected a bad citation and wrongly discarded the right conclusion.
> 🔴 **`E4` is FILED as `DEBT-AIB-028` with an activation recipe**, and **`E5` gains a prerequisite:
> `StateNode.SubtreeAssetId` is not persisted.** ⭐ **New `E0`: the HSM golden harness, its own batch.**
>
> **REVISION 10 (`2026-08-16`).** ✅ **Batch 66 MERGED at `3ed92905a`** — `G4` · **the surgical
> write (the last live defect)** · `G1` · `C-sections`. ⭐⭐ **`G4` grew correctly** — the name guard was
> necessary but not sufficient; **two distinct names can hash to one id** (§4A).
> ✅ **Ruling: the throwing default on `IEntityCommandBuffer` is accepted**, with a reflection rail owed.
> 🔴 **`Fdp.Toolkits.Tests` full-suite runs are not a reliable gate** — filed as `DEBT-AIB-030`.
>
> **REVISION 9 (`2026-08-16`).** ✅ **`W6`/`W7` RE-DERIVED (§4i)** from the design's §9.
> ⛔ **`W6` DROPPED** — its static projection is superseded by the **shipped** annotation mechanism.
> ⭐⭐ **Most of §9 is BUILT**; `W7` becomes three gaps, and 🔴 **`W7c` is a COVERAGE HOLE in a shipped
> rule** — it sees alias bindings only, not the static-offset style, nor Sync-Out.
>
> **REVISION 8 (`2026-08-16`).** ✅ **Batch 65 MERGED at `8c09d5004`** — `S2`·`S4`·`S3`·`S5`, **Track B
> complete**, `BP-01` closed, all eight gates coordinator-re-run and matching.
> ⛔ **`DEBT-AIB-012` corrected everywhere** — the triplication was *described and never filed*; that id
> belongs to a different, RESOLVED row. ⚠ **I propagated the wrong citation; the impl session caught it.**
>
> **REVISION 7 (`2026-08-16`).** ✅ **Track E RAILS added** — 8 rows, *previously undefined*.
> 🔴 **Golden coverage MEASURED: `persistence-shape.txt` is 43 assets, ALL `.bp.json`, ZERO HSM/BTree**
> ⇒ `E1`/`E3`/`E6` change emitted output with **no golden gate watching** — `BP-240`'s shape inverted.
> ✅ **`E7a`'s host context RULED** — one interface argument, 📄 `DESIGN_Parameter_Model.md` §3.4.
>
> **REVISION 6 (`2026-08-16`).** ✅ **Track E added (§4B) — the HSM catch-up**, collecting every
> measured HSM gap as `E1`–`E7`. ⭐ **`Q33-E` answered: PHASED, not abandoned** — user ruling, *"if
> something is not present in HSM, it is not because it is not needed, just not implemented yet."*
> ⭐ **Plus the latency rail** — a latent CONDITION currently reads **false** while it waits, silently.
>
> **REVISION 5 (`2026-08-16`).** ✅ **The parameter model is RULED end to end (§4g)** — Instances use
> the **resolver** shape, params live **in the Instance's own slot**, runtime attach **carries a payload**,
> and ⭐⭐ **sections are the classification, so `Role`/`Scope` is not a control on any host** *(`Q-k`
> dissolves)*. ✅ **Multi-occurrence added (§4h) with the HSM cost accepted.**
> ⛔ **Nothing in the parameter story is still an open question — only work.**
>
> **REVISION 4.** ✅ **Track D reconciled against the resolver design's `G1`–`G7` gap list (§4).**
> ⛔ **`W8` and `W12` DROPPED as duplicates; `W10` merged with `G7`.** ⭐⭐ **Four of the seven gaps had
> already closed** — the list was `2026-07-13` and nobody had re-measured it.
> **Rev 3** ruled Track C's three decisions and wrote its design —
> 📄 **[`DESIGN_Variable_Details_And_Editing.md`](DESIGN_Variable_Details_And_Editing.md)**.
> **Rev 2** folded in three coordinator subagent scans over `.dev/` (~2887 files) + the implementation
> session's sweep; **rev 1** was written from **code alone**.
> ⛔ **Two of my own rev-1 conclusions were WRONG (§0).** 📄 Sources:
> [`REPORT_Batch64_Dev_Sweep.md`](REPORT_Batch64_Dev_Sweep.md) + the three coordinator scans.

![remaining work](PLAN_Remaining_Work.svg) *(diagram predates this revision — tracks still hold, contents changed)*

---

## 0. ⛔⛔ **I repeated the Batch-63 mistake. Twice, in this plan.**

| my v1 claim | the design record | verdict |
|---|---|---|
| *"delete the dead `IStructEditDrawer`/`DrawerRegistry` chain"* (`C6`) | `_DONE/blueprints-1` DD §7.1/§7.8 + `BATCH-22-INSTRUCTIONS:297-380` **specify it verbatim**; still referenced by `InspectorWindow.cs:10,17` and `BlueprintWindowRegistrar.cs:21,29`; `MVE-BATCH-04-REPORT.md:194` frames removal as **part of retiring the legacy `BlueprintEditorModule`**, not an isolated deletion | 🔴 **WRONG — "designed, pending a larger retirement", not dead code** |
| *"`BlueprintVariablesWindow` is redundant, retire it"* (`C6`) | migrated onto `VariablesPanelControl` (`BATCH-15`), carries the `{DtoTypeFqn}::{FieldName}` rename context (`BATCH-16`); a deliberate *"wrapper instead of modifying it"* decision exists **to avoid rewriting a working, tested class** | 🔴 **WRONG — a re-host, not a retirement** |

⇒ ⭐⭐ **Same shape both times: a grep answered *"is it used?"* and I read it as *"is it wanted?"***

---

## 1. ✅ Done — merged through `0b2b55380`

Batches **56 · 58 · 57 · 59 · 60 · 61(1–2) · 63 · 64(1) · 65 (Track B, all four) · ⭐ 66 (`G4` · the
surgical write · `G1` · `C-sections`) · 67 (`W7c` · `W7a` · `G3` · `E1`+`E2` · 2 rails) · ⭐ 68 (`C-table` · `C-dialog` · `W7b` · `E4` partial) · ⭐⭐ 69 (`C-tick` · `DEBT-AIB-009` · `C-watch` · `C-outline` · `E4` finished) · ⭐⭐⭐ 70 (`DEBT-AIB-021` · **the Instance params seam** · `G7`+`W10`)**.
Phase A correctness is complete except `W6`/`W7`, which the sweep has now **re-specified** (§4).
⭐⭐ **Merged through `0b2b55380`** — gates coordinator-re-run each time. Tracker **open 61 / done 153**.

---

## 2. ✅ Track B — struct support ⭐⭐ **DONE, Batch 65 (`8c09d5004`), coordinator-verified.**

⭐ **All four shipped: `S2` · `S4` · `S3` · `S5`.** ⛔ **`BP-01` CLOSED.** Tracker **open 61 / done 129**.
📄 [`REPORT_Batch65_Track_B.md`](REPORT_Batch65_Track_B.md) — ⭐ **and it corrected a mis-citation I had
propagated four times** *(`DEBT-AIB-012`, below)*.

### The design records each was built to

| | design record | what changed |
|---|---|---|
| **`S2`** struct size resolution | `.dev/btree-ai-action-binding/reports/BATCH-03-REPORT.md:34` — ⭐⭐ **a stated mandate**: *"`StructSizeResolver` lives in Generators and is **injected via `Func<string,int?>`**; Persistence stays netstandard2.0 / Roslyn-free"*, from a **user decision `2026-06-15`** (`TASK-DETAIL.md:58`) | ✅ **my lean CONFIRMED, with a shipped precedent** (`BTreeBlackboardPackHelper.Pack(vars, Func<string,int?>, out total)`) ⚠ **but `:100` records the resolver as ALREADY a third copy of `ComputeStructSize`** ⇒ a naïve `S2` makes a fourth. ⛔ **CORRECTED `2026-08-16` (Batch 65 §5): that line says *`DEBT-AIB-012` (suggested)* and the id was ALREADY TAKEN by a resolved row** ⇒ **the debt has a description and NO ROW — cite `BATCH-03-REPORT.md:100`** |
| **`S3`** `MarshalFromBytes` struct arm | `_DONE/blueprints-1/TASK-DETAIL.md:1840` — *"reflection-based for structs (UI decode only, not on the probe path)"*; `blueprint-dbg-1:193` — *"primitives/**small** structs only"* | ✅ **CONFIRMS** — ⭐ designed in from the start, **never built**. The record also **bounds** it |
| **`S4`** fixed-list `Capacity` | ⭐ **outside `.dev/`** — `docs/blueprints/Blueprint_List_Variables_Design.md` §3:63–72 **specifies the exact missing branch**: *"`StaticTypeRegistry.TryResolve`: new branch — when `Capacity > 0` and the element resolves unmanaged, return the list `IrTypeRef` (unmanaged, real size)"* | 🔴 **REFINES** — a **designed-but-unbuilt branch**, not a bug. ⚠ Must honour `SizeReliable = false` and the `__List_{Elem}_{N}` wrapper name |
| **`S5`** one picker | `blueprint-finalize/BF-BATCH-FIXEDSTRING-INSTRUCTIONS.md:33` treats **`SelectableTypeIds`** as *the* picker list; `EditorOfferableTypeIds` is never mentioned | ⭐ **CONFIRMS the defect is real and undocumented** — the second list grew later on the compiler side |

---

## 3. ✅ Track C — the panels. ⭐⭐⭐ **BUILT through Batch 69. Only the VISUAL CHECK remains.**

> ⭐ **`C-sections` (66) · `C-table` + `C-dialog` (68) · `C-tick` + `C-watch` + `C-outline` (69).**
> ⛔ **Nothing in Track C is designed-but-unbuilt any more.** ⚠ **What is unverified is DRAWING** — see
> §4A3 and §4A4 tails; that list is now the whole of Track C's remaining risk.

📄 **[`DESIGN_Variable_Details_And_Editing.md`](DESIGN_Variable_Details_And_Editing.md)** *(+ SVG)* —
⭐ **that is what gets built.** ⛔ **It supersedes this section and `DESIGN_Variable_Details_And_Live_Values.md` §8.**

| decision | ruling |
|---|---|
| ① **the write path** | ✅ **OPTIMISTIC DISPLAY.** Paint the new value immediately, then **stage** through the existing path. ⛔ **Do NOT write `_liveRepo` while paused** — `Blackboard1024` is `[DataPolicy(NoSave)]`, i.e. **snapshotted AND recorded**, so a non-simulation write breaks Flight Recorder linearity |
| ② **the gesture** | ✅ **two menu items = the two `EditScope`s.** *"Edit value…"* (`ForField`, double-click the **value** cell) · *"Properties…"* (`WholeComponent`, double-click the **name** cell). ⭐ **Run state decides WRITABILITY, not which dialog** |
| ③ **table or form** | ✅ **TABLE**, filtered by section — ⛔ **never a single-variable form.** `D7`'s field list becomes **the dialog's** contents |

### ⭐ What the design settles beyond those three

| | |
|---|---|
| **columns** | `Name` + `Value` **mandatory**, `Type` **one toggle** *(hidden in Watch, shown in Details)*. ⛔ **No general column framework** — the control has **seven** today |
| ⭐⭐ **generic row list** | the control renders `IReadOnlyList<VariableRow>` and **knows nothing about the source.** `SectionSource` (Details) · `PinnedSource` (Watch, **mixed assets and entities**) |
| **identity** | `(AssetId, Entity, VariablePath)` — ⛔ **entity is part of it** |
| ⭐ **grouping** | `GroupBy` = an **ordered facet list** *(`[]`, `[Entity]`, `[Asset]`, `[Asset, Entity]`)* — ⛔ not hardcoded modes. **A uniform facet emits no header.** Folding is `CollapsingHeader` *(already used 3× in that control)*, and ⭐⭐ **a collapsed header inherits its children's red/yellow** |
| ⭐⭐ **change highlight** | 🔴 **red one tick** = the sim changed it · 🟡 **yellow** = your pending edit. ⭐ **The unit is a NON-FROZEN ASSET TICK** — not a frame, not a world tick ⇒ **paused, the highlight persists until you Step.** ⭐ **Diff RAW BYTES** |
| **value rendering** | primitives inline · structs = elided one-line summary + **pretty-printed tooltip** · ⛔ **never raw hex** *(`BP-01`'s symptom)*; undecodable says `<unreadable>` |
| **budget indicator** | ⭐ **planning-only chrome**, on the same run-state switch as the Value column |

⚠ **Track C still needs the VISUAL CHECK** — grouping, folding and colour are surfaces no headless test
can verify. ⭐ **But the change-highlight PREDICATE and the grouping/column rules are headlessly testable.**

---

## 4. ⏭ Track D — ⭐⭐ **RECONCILED against `G1`–`G7` (`2026-08-16`). Two `W` items dropped as duplicates.**

📄 **[`Behavior_Parameter_Resolver_Detailed_Design.md`](Behavior_Parameter_Resolver_Detailed_Design.md)**
§7 carries a gap list `G1`–`G7` dated `2026-07-13`. ⭐⭐⭐ **Measured on `HEAD` `2026-08-16`: four of the
seven have since closed.** The gap list is stale; this table is the current one.

### 4a. ⭐ `G1`–`G7` — measured status

| | gap | status on `HEAD` |
|---|---|---|
| **`G1`** | split deserialize from resolve | ⚠ **HALF.** The signature half **landed** — `ParseParamsDelegate(string, byte*, EntityRepository, Entity)` already carries world + self. ⛔ **The split did not**: no generic auto-deserializer keyed by `ParamsDtoType` exists (that field feeds **rendering only** — ReplayBrowser drawers, StructEdit context) |
| **`G2`** | Library blueprint functions runtime-invocable | ✅ **DONE.** `BlueprintDefinition.Functions` (commented `// For Library dispatch (G2)`), emitted by `CSharpEmitter:256`, guarded by `BP5001_LibraryHasNoFunctions`, covered by `LibraryFunction_InvokeTests` + `LibraryFunctionsDemo_ProofTests` ⇒ ⭐ **a blueprint-authored resolver's runtime seam EXISTS** |
| **`G3`** | geo transform + entity map as world singletons | 🔴 **OPEN.** ⛔ **And rev-3's *"world-singleton is shipped ⇒ adopt, do not coin"* was a CONFLATION**: `BlueprintRegistry.RegisterWorldSingleton(blueprintId, tier)` registers **a blueprint to tick as a singleton**. It is *not* a service-locator for the geo transform. ⭐ Motive restated: `G6` retired the factory, so a **JSON- or blueprint-authored** resolver has no closure to reach these through |
| **`G4`** | hard-error on duplicate behavior name | 🔴 **OPEN.** `_definitions[id] = definition; _nameToId[name] = id;` — indexer assignment, **silent overwrite**. ⭐⭐ **This is `W1`'s sibling on the behavior registry** — same defect class, other side of the house |
| **`G5`** | `ActiveBehaviorHash` name-derived | ✅ **DONE.** The `3013` magic constant is gone: `BehaviorHash.FromName(BehaviorNames.HullDownAttackRun)` |
| **`G6`** | retire `AiBehaviorFactory` | ✅ **DONE.** `CgfCuratedBehaviorRegistrar` — *"replaces the retired `AiBehaviorFactory`"*; `RegisterResolver` binds by name, order-independent |
| **`G7`** | editor affordances — detach authored shape, divergence detection, resolver picker | 🔴 **OPEN** — ⚠ **converges with `W10`, see below** |

### 4b. ⛔ Dropped as duplicates

| dropped | absorbed by | why |
|---|---|---|
| ⛔ **`W8`** — reserved input variable | ⭐ **`G1`** *(+ `DEBT-AIB-021`, the scenario-overlay half)* | ⭐⭐ **Its model half is already RULED, not open:** the resolver design §3.2 — *"the variable **role** enum is `{ Input, State }` — there is no separate 'Param' role. `Input` **is** the parameter role."* ⇒ **there is no tier to choose**; what remains to BUILD is exactly `G1`'s split. ⭐ **`D2` dissolves with it** |
| ⛔ **`W12`** — Construction initializer | ⭐ **`G3`** | Same work, and `G3` is the **scope pass** `W12` was blocked on — it names the one missing piece (the geo transform's singleton registration) instead of four vaguely-sized ones. ⚠ Rev 3 called two of `W12`'s pieces "already shipped"; one of those was the conflation corrected in `4a` |

### 4c. ⚠ Merged — do not build two of these

⭐ **`W10` (initializer picker) + `G7` (resolver picker)** are both *"pick a named producer from a
contributing catalog."* ⛔ **Ruling 9 — no two implementations of one concept.** Specify them together;
`W10`'s measured constraints carry over: **offer over the union**, and identity is the generated
**FQN, not the AssetId** (architect `AQ2`).

### 4d. ⏭ Surviving `W` items

| | verdict |
|---|---|
| **`W6`/`W7`** | ✅ **RE-DERIVED `2026-08-16` — see §4i. `W6` DROPPED; `W7` becomes three concrete gaps.** *(original finding below, kept for the reasoning)* 🛑 **`W7` CONTRADICTED.** `Blackboard_Authoring_Detailed_Design.md` §7.7/§9.1–9.6 is a **complete design**: a **suppressible WARNING** (not an error) with per-conflict metadata + an *"Allow concurrent writes"* checkbox; writers classified by **whether the action mutates the ref parameter** (optional annotation, conservative read-write default) — **not** by `W6`'s static projection; and §9.1 says **extend the existing `OutputLaneMask` conflict infrastructure**. §9.5 adds an **Approach B Sync-Out** case we omitted. ⇒ ⭐ **`W6` is downstream of a mechanism the design does not use — re-derive `W6` from §9.6 or drop it.** ✅ `[SharedAiCondition]` re-measured at **0 production usages** |
| **`W9`** | ⚠ **premise coordinator-verified as REAL but MIS-LOCATED:** `HsmBridgeEmitCore` bakes **no key at all** (post-Batch-59); the simple-name hash is **`HsmActionGenerator:517/630` — `ComputeHash(action.Name)`**, and `MethodInfo` carries both `Name` and `FullName`. ⛔ **And the re-bake is TWO sites, not one** — blob key + thunk key, reconciled *"in lockstep via shared `ResolveStatefulSlotKey`"* |
| **`W10`** | ✅ mechanism **CONFIRMED** — `AN7-REPORT.md:73–95` is the **exact precedent** for *"add a source enum member + contributing catalog, not a new picker"*. 🔴 **But *"persist the catalog `Id`"* CONTRADICTS an architect ruling:** `blueprint-finalize/TASK-DETAIL.md:248` — *"Canonical identity = generated **FQN**, **not** AssetId (architect AQ2)"*. ⚠ `BehaviorActionSource.AiPrimitive` exists but is **never assigned** |
| **`W11`** | 🔴 **NOT a "twin", and not implementable as written.** `FIX-01-REPORT.md:43` — *"the HSM binding model is structurally different: there is **no per-node `ExpressionTargetField`**"*; **`VE-DEBT-001`**: an HSM state hosts **4 action slots** (Entry/Exit/Activity/Timer) so one-DTO-one-variable *"**needs an architect design call — not an autonomous guess**"*; **`VE-DEBT-004`**: **no production `[HsmGuard]` exists** to bind against. ⛔ **`HSM-016` is an UNRESOLVABLE id — zero hits anywhere; nothing defines what it says** |
| **`W13`** | ✅ **DONE** (Batch 63) |

### 4e. ⭐ Adopted new — no `W` counterpart existed

| | |
|---|---|
| **`G4`** duplicate-name guard | 🔴 a live silent-overwrite defect. **Small, isolated, and it is `W1`'s sibling** ⇒ cheapest item on this page |
| **`G1`** the split | the substance of what `W8` was reaching for |
| **`G3`** service singletons | what `W12` was reaching for, correctly scoped |
| **`G7`** + `W10` | one picker, specified once |
| ⭐⭐ **the Instance override half** | `BlueprintAssignmentDto.Overrides` is **designed and forward-compatible, empty in MVP** — 📄 `.dev/blueprint-scenario/BLUEPRINT-SCENARIO-DESIGN.md` §6, deferred for ONE stated reason: *"the authoring UX ('where is a per-instance override edited?') is unsettled."* ⇒ ⭐⭐⭐ **that UX is Track C.** The two are one work item, and nobody had connected them |

### 4f. ⚠ Two findings from the HSM scan — **described, not numbered** (rule 3)

| | measured |
|---|---|
| **HSM `Role`/`Scope` have no runtime wiring** | `HsmEmitCore` + `HsmBridgeEmitCore`: **0** references. `BTreeBridgeEmitCore`: **45**. `HsmBlackboardVariableDto` persists both faithfully ⇒ authoring metadata the HSM runtime never reads. ⭐ **This weakens `W11` further** — the "twin" premise assumes a binding model HSM has not got |
| **two guards for a real collision never fire** | `HsmValidator` rules **8**/**8b** are correct errors, but their injected resolvers default to `_ => false` / `_ => Empty` and **both production call sites use the default ctor**. The XML doc says *"Production should wire this"* ⇒ **unfinished wiring, not a dead rule** |

### 4i. ✅ `W6`/`W7` RE-DERIVED from `Blackboard_Authoring_Detailed_Design.md` §9 *(coordinator, `2026-08-16`)*

⭐⭐⭐ **Most of §9 IS BUILT.** ⛔ **`W7` was never a build-from-scratch item** — it is **one consumption
fix, one small affordance, and one coverage hole.**

| § | the design says | measured on `HEAD` |
|---|---|---|
| **9.2** the rule | walk states, find writer pairs that can be simultaneously active, emit `CrossRegionBlackboardConflict` | ✅ **BUILT** — `HsmValidator` rule 9, and ⭐ **wired at production** (`HsmGraphModel:43` passes the blackboard) — ⛔ unlike rules 8/8b |
| **9.3** warning + **per-pair** suppression | *"Suppression is per-pair, not per-variable"* | ✅ **BUILT and round-tripped** — `_conflictSuppressions`, `HsmAssetMapper`, `HsmAssetProjector`, emitted as `.SuppressBlackboardConflict(var, writerPair)`. **On BTree assets too** |
| **9.4** drop-target refusal | red drop target across regions | ✅ **BUILT** — `BlackboardAliasDropValidator` |
| **9.6** readers are safe + annotations | `[BlackboardReadOnly]` / `[BlackboardReadWrite]`, **conservative when unannotated** | ✅ **BUILT** — the attributes ship in `Fbt.Kernel/BlackboardAnnotations.cs`; `HasWritingAction` returns **writer** on `_schema == null`, on unknown FQN, and on any non-`ReadOnly` access |

### ⛔ `W6` — **DROPPED**

`W6` was a **static projection** to classify writers. ⭐ **§9.6 specifies annotations + a conservative
default instead, and that is SHIPPED.** ⇒ **`W6`'s mechanism is superseded by a built one.** ⛔ **Do not
implement it.**

### ⭐ `W7` — the three gaps that actually remain

| | gap | measured |
|---|---|---|
| **`W7a`** | ⭐ **rule 9 does not consult the suppression** | `IsConflictSuppressed` is consulted **only** by `BlackboardAliasDropValidator:43` ⇒ **suppressing silences the DROP TARGET while the PANEL WARNING persists.** ⚠ **The affordance half-works, which is worse than absent** — the designer clicks Suppress and nothing appears to happen |
| **`W7b`** | **"Allow concurrent writes" is absent** | §9.4's explicit-enable path — **0 hits repo-wide** |
| 🔴 **`W7c`** | ⭐⭐⭐ **COVERAGE HOLE in a shipped rule** | rule 9 iterates **`GetAliasesFor` ⇒ `BlackboardAliasBinding` only**. ⛔ **The `[SharedAiAction(typeof(Dto),"Field")]` static-offset binding style is NOT covered**, and **§9.5's Sync-Out bindings are not enumerated as writers** *(`SubtreeSyncBinding.SyncOut` exists; the validator never reads it)* |

⇒ ⭐⭐ **`W7c` is the one that matters.** A rule that covers one binding style **reads as guarded while
leaving the other unguarded** — ⚠ **`BP-240`'s shape again: green because of what it happens to look at.**

📌 **Order:** `W7c` *(correctness/coverage)* → `W7a` *(one consumption fix)* → `W7b` *(UX)*.
📌 **Open, minor:** §9.1 says *"extend the existing `OutputLaneMask` conflict infrastructure"* — rule 9
is a **separate** walk. ⚠ **Consistency question, not a defect; do not refactor on this alone.**

### 4g. ⭐⭐⭐ NEW — the unified parameter model *(user rulings `2026-08-16`)*

⛔⛔ **THE AUTHORITY IS 📄 [`DESIGN_Parameter_Model.md`](DESIGN_Parameter_Model.md).** ⭐ **One doc, it
supersedes every prior parameter design, and it carries a "do not re-derive" table.** The rows below are
the *plan view* of it; **it wins on any disagreement.**
📄 Measurement record + diagrams: [`EXPLAINER_Where_Parameters_And_State_Live.md`](EXPLAINER_Where_Parameters_And_State_Live.md).

| ruling | what it commits us to build |
|---|---|
| ⭐⭐⭐ **Instances use the RESOLVER shape** — *"Instances could and should reuse the param parsing and resolving"* | ⛔ **`Overrides` is not the mechanism.** The resolver pipeline serves every host ⇒ ⭐ **`G1` is now load-bearing for blueprints too** |
| ⭐⭐ **params live in the Instance's own slot** | slot becomes **`[Cursor 16][Params N][State M]`**; `StateStructBase` shifts by `N`. ⛔ **`FieldLayout`'s `startOffset: 0` for parameters would land on the cursor** — safe today only because Instances have none |
| ⭐⭐ **runtime attach carries params** | `AttachInstanceBlueprintEvent` gains a payload and `BlueprintEventIngressSystem` gains a resolve step, mirroring **parse-before-commit**. ⭐ **The delegate already takes a destination `byte*`** ⇒ the pipeline is reused **unchanged**, only the pointer differs |
| ⭐⭐⭐ **sections are the classification** | ⛔ **no `Role`/`Scope` control on any host.** Split the one Variables section per kind; give BTree/HSM their own `IMyBlueprintModel`. ⇒ **`Q-k` dissolves** — 📄 Track C design §1c |

⭐ **`R4` — installing an Instance at runtime WITH params, e.g. from a running master blueprint — had
NO design anywhere.** These rulings are it.

⭐⭐ **Wired params — the host context** *(ruled `2026-08-16`)*: a hosted occurrence's params may be
computed from its **host's** variables, via **one new resolver argument** of interface type —
📄 **`DESIGN_Parameter_Model.md` §3.4**. ⛔ **Name-keyed, read-only, `null` for a root, fails closed.**

⛔⛔ **RAILS FOR THIS SECTION ARE ALREADY WRITTEN — 📄 [`DESIGN_Parameter_Model.md`](DESIGN_Parameter_Model.md) §8.**
⚠ **Do not invent new ones.** Seven rails, including the two that stop the old assumptions returning:
⭐ **"two occurrences of one asset on one entity ⇒ distinct param bytes"** and ⭐ **"the
`BlueprintLatentCursor` at offset 0 is intact after a resolve"** *(the `startOffset: 0` trap)*.

### 4h. ⭐⭐ NEW — multi-occurrence *(HSM cost ACCEPTED by the user, `2026-08-16`)*

⭐ **One problem in three costumes: *N concurrent occurrences need N slots, keyed by occurrence.***
⭐⭐ **BTree solved it and is the template — adopt `FNV-1a(assetGuid, nodeVisualId)`'s shape, do not invent.**

| host | work | cost |
|---|---|---|
| **BTree** | ✅ **none** — the reference implementation | — |
| **Blueprint** | widen `slot.BlueprintId` → **`(blueprintId, instanceKey)`**: `BlueprintSlotEntry`, `TryAttach`, `TryGetSlotOffset`, the attach/detach events | ⚠ **moderate**, ⭐ **no kernel change**. ⇒ **`D2` is IN SCOPE** — parameterised scripts make *"the same script twice with different args"* the ordinary case |
| **HSM** | the action key must carry the occurrence; slots must be provisioned like BTree's; **the emitter must read `Role`/`Scope` at all** *(0 refs today)* | ⚠ **larger — ✅ user accepted.** ⭐⭐ **Sized by measurement: `r` (region) and `current` (state) are ALREADY IN SCOPE at the `ExecuteAction` call site** ⇒ **a signature widening + thunk regeneration, not a data-flow redesign.** ⚠ **But it is a `FastHSM` `ExtDeps` change** |

⇒ ⭐ **Order falls out: blueprint first** *(no kernel change, and `R4` needs it)*, **HSM after** — with the
HSM emitter slice already queued as its first step.

### ⭐⭐ Hand-written DTOs survive all of this **unchanged** — 📄 explainer §5e

⭐ **The 100-byte region is a TYPE, not a per-entity singleton.** `NodeLogicDelegate<TBlackboard,…>` is
generic and the instance arrives **by ref from the caller** (`Interpreter.Tick(ref blackboard, …)`);
`BrainBlackboard` is just a 128-byte struct; and a hand-written DTO's offsets are **relative to the
struct base**, with `Method@byteOffset` baking only the *field* offset.

⇒ ⭐⭐⭐ **A hosted occurrence gets its own PARAMS REGION in its slot and is ticked against that** —
every `[SharedAiAction]` thunk keeps working, **same offsets, different instance.**
⇒ **params belong to the OCCURRENCE.**

⛔ **Carry the PARAMS AREA only, never the component** *(user correction, `2026-08-16`)*. ⭐ **Measured:
no generated thunk touches the tail** — `CognitiveInterruptSystem` / `CognitiveCleanupSystem` /
`HsmTickSystem:168` / `RouteContextSystem:190` are all **systems** — and **actions never see the
blackboard at all** (`Method(ref field, ctx.Self, ctx.World)`). ⇒ the blackboard ref exists **only** to
locate the params. Interrupts and soft advice stay on the component.

⭐⭐ **Cheaper than the whole-struct version I first leaned to:** ⭐ **BTree needs NO `ExtDeps` change** —
`NodeLogicDelegate`/`Interpreter` are generic and never touch the blackboard's members, so the edit is
`ref bb.BehaviorParameters` → `ref bb` at three generator emit sites, the interpreter's type argument,
and one line in `BTreeTickSystem`. ⭐ **HSM folds into the `ExecuteAction` signature widening
occurrence-keying already needs** — one seam, two problems.

📌 **Multiple BTrees/HSMs per entity: ⛔ not as PEERS** *(root exclusivity is what preemption is defined
against)*, ✅ **yes as NESTED sub-behaviours** — the composition ruling.

🔴 **The collision they guard is real:** the HSM action slot key is `hash(methodName @ compileTimeOffset)`
through one shared `ActionTable`, projected at a static offset in the **one** `BrainBlackboard` — **no
region index anywhere in the path** ⇒ two concurrently-active orthogonal regions running the same
action write the same bytes. ⭐ BTree is immune: it provisions per-scope slots via `ResolveStatefulSlotKey`.

---

## 4A. ✅ Batch 66 — verified, merged, and **two coordinator rulings it forced**

📄 [`REPORT_Batch66_Defect_Seam_Sections.md`](REPORT_Batch66_Defect_Seam_Sections.md).
⭐ **`G4` · the surgical write · `G1` · `C-sections`** — all four, gates re-run and matching.

### ⭐⭐ `G4` grew, correctly — **the name guard was necessary but NOT SUFFICIENT**

⛔ **I specified only the design's *"duplicate name = hard error"*.** ⭐ **They found the real silent
failure underneath it:** `id` is **FNV-1a-32 of the name**, so **two DISTINCT names can hash to one
id** ⇒ `_nameToId` holds both names → one id, while `_definitions[id]` holds only the second ⇒
🔴 **the first behaviour silently resolves to the SECOND's topology.** ⭐ **That is `W1`'s hashed-id
collision, on the behavior registry**, and the shape was **transplanted from
`BlueprintRegistry.RegisterDirect`** as the design says, not invented.

### ✅ RULING — the throwing default on `IEntityCommandBuffer` is **ACCEPTED**

They flagged rather than assumed: `SetComponentRaw` **is** on an interface with **12 implementers**
(1 real · 2 production wrappers · 9 test mocks), and the new member got a **default implementation that
THROWS** so nine mocks need no body. ⭐ **Right call — a silent no-op here is a LOST EDIT, the exact
defect class the method exists to remove.**

⚠ **Residual risk I am recording, not waving through:** a **future** production wrapper that forgets to
delegate fails at **runtime**, not compile time. 📐 **Mitigation, cheap, next batch that touches this:**
a reflection rail asserting **every non-test `IEntityCommandBuffer` implementer overrides it.**

### 🔴 `Fdp.Toolkits.Tests` — **it is not a reliable full-suite gate, and now we know why**

⭐⭐ **The cause is FILED and this programme called it unexplained for three batches:**
**`DEBT-AIB-030`** — *"non-deterministic in the FULL unfiltered suite… pass deterministically under
`--filter` and in isolation"*; **`DEBT-AIB-010`** names the cause — *"xUnit cross-collection parallelism
+ process-global ECS/component-id/registry state corrupted by unrelated collections."*

📌 **Independent confirmation, my samples:** two consecutive full runs failed on **two DIFFERENT tests**
(`GizmoRegistryTests` then `StatelessGizmoRegistryTests`), both **green in isolation (8/8)**.
⚠ **Across batches 65–66: 3 of 6 full runs red, 3 distinct tests, all registry-shaped.**
⛔ **I checked the one thing that could have made this ours** — Batch 66 added `IMutationInterceptor.cs`
under `Diagnostics/Gizmos/`. **It is a pure interface, no static state** ⇒ cannot touch registry state.

⇒ ⭐ **Gate change, from now on: a FULL-suite red in `Fdp.Toolkits.Tests` is not signal by itself.**
**Confirm with `--filter` / isolation before treating it as a failure** — and ⛔ **never let a green
full run stand as evidence either**, for the same reason.

---

## 4A2. ✅ Batch 67 — verified, merged, and ⛔ **it corrected me twice more**

📄 [`REPORT_Batch67_Conflicts_Singletons_HsmState.md`](REPORT_Batch67_Conflicts_Singletons_HsmState.md).
⭐ **`W7c` · `W7a` · `G3` · `E1`+`E2` · the owed reflection rail · ⭐ the twice-carried latency rail.**
⭐⭐ **They added an `Hsm.Editor` gate themselves** — *"not a standing gate — the diff reaches it."*

### ⛔⛔ `G3` was ALREADY SHIPPED — my premise was wrong

`IGeographicTransform` carries **`[ComponentId(GlobalComponentIds.IGeographicTransform)]`** and is
published with **`SetSingletonManaged` at THREE production sites** (`CgfSubsystem:249`,
`SimHostApp:488`, `EditorSubsystem:624`) — ⭐ **the identical mechanism `NetworkEntityMap` uses.**
⇒ **only the RAIL was missing.**

⚠ **My *"constructor-injected ⇒ unreachable from the world"* named a second CONSUMER, not a second
mechanism** — `GeographicModule`/`CoordinateTransformSystem` exist **before the world does**.

🔴🔴 **The lesson, and it is a new shape:** rev-3 said *"world-singleton is shipped ⇒ adopt, do not
coin."* I found its **citation** wrong (`RegisterWorldSingleton` registers a blueprint to tick) and
⛔ **discarded the CONCLUSION along with it** — which was right all along, via a third mechanism I had
not found. ⇒ ⭐⭐ **Correcting a bad citation is not grounds for reversing the claim it was attached to.**

📌 **They also caught their own test passing VACUOUSLY** — `PickableGeoPoint` serialises as a
**`[lat, lon]` array**, so an object-shaped fixture deserialised to zeros and `0 != 14` satisfied a weak
assertion. ⭐ **Fixture corrected to pin the real converted value.**

### ✅ The corpus decision — **(b), accepted, with the follow-up promoted**

⭐⭐ **Their reframing is right and I had it too small:** ⛔ **(a) is not *"add some HSM assets"* — there
is NO HSM golden harness at all**: no corpus, no shape file, no structure-hash gate.
⇒ ⭐ **Building one is a batch of its own — and it is the same batch that gives `E3`–`E7` their
regression floor.** 📌 **Promoted to a Track E prerequisite (§4B).**

### ⭐⭐⭐ `W7c`'s boundary uncovered a FILED row that is Track E's own

⛔ **§9.5's Sync-Out half is out of scope because `StateNode.SubtreeAssetId` is NOT PERSISTED** — and
that is **`DEBT-AIB-028`**, which already contains **my `E4` verbatim** plus an activation recipe:

> *"(a) `StateNode.SubtreeAssetId` is a NEW field, **not persisted to JSON**… (b) `_isStatefulSubtree`
> defaults to `_ => false` and **production never supplies a real resolver**; (c) the production
> `HsmAssetValidator` entry point **isn't threaded** to pass the resolver. Activation needs: persist the
> HSM subtree reference, a `BehaviorTreeAsset.HasAnyStatefulNode()` + HSM equivalent, wire
> `id => catalog.TryFind(id,out a) && a.HasAnyStatefulNode()` through the production validator ctor."*

⇒ ⭐⭐ **Fourth time the `.dev/` corpus already held the answer.** 📌 **`DEBT-AIB-029`** adds: the check
walks **DIRECT children only** — a stateful subtree nested deeper is undetected.

---

## 4A3. ✅ Batch 68 — ⭐⭐ **and it found a contradiction in MY authoritative design**

📄 [`REPORT_Batch68_Track_C_Table_And_Dialog.md`](REPORT_Batch68_Track_C_Table_And_Dialog.md).
⭐ **`C-table` · `C-dialog` · `W7b` · `E4`.** Gates re-run, snapshots unchanged, tracker **61 / 143**.

### 🔴🔴 The tick unit is **WORLD**, and there is **no per-asset tick anywhere**

📐 Measured chain: `BlueprintDebugSession:1543` → `_view.Tick` → `ISimulationView.Tick`
*("current simulation tick (frame number)")* → `EntityRepository.SimulationTick`. ⇒ **per WORLD.**
🔴 **And nothing stamps a per-instance counter** — `BlueprintTickSystem` calls `def.Tick(...)` and
stamps none. ⇒ **the ruling's unit does not exist.**

⭐⭐ **They refused to wire the world tick, and were right:** under it red would clear whenever any frame
advanced — **including while paused**, the exact case the ruling exists for. **Instead `AssetTick` is a
per-row NULLABLE delegate**; `null` ⇒ **no highlight, not even recorded** — ⭐ **inert, never wrong**, and
asserted so it reads as a decision. ⭐ **The predicate is complete and tested** *(100 repaints, 100 world
frames, zero asset ticks ⇒ still red)*.

⇒ 🔴 **NEW ITEM — `C-tick`: a per-`(asset, entity)` tick counter.** ⛔ **The change highlight is INERT
until it exists.** ⭐ **When it does, it is passed to `SectionSource` and nothing else changes.**

### 🔴🔴 `DEBT-AIB-021` **contradicts `DESIGN_Parameter_Model.md` §3.2** — corrected there

> *"The generated `ParseParams` writes only baked defaults… **it ignores the incoming `json` argument**."*

⇒ ⭐⭐ **"scenario JSON overlays, runtime wins" is TRUE of the curated path and FALSE of the GENERATED
managed-asset path.** ⛔ **My design stated it as universally shipped.** ⚠ **`G1`'s split does not fix
it** — the deserializer must dispatch per-variable by name.

### ⭐ Track C's ground truth, and a count correction

| | |
|---|---|
| 🔴 **`DEBT-AIB-009`** | the render path takes `_actionSchemaExporter` and **neither production constructor supplies it** ⇒ ⭐⭐ **the same shape as `E4`: a value column over a schema nothing supplies.** ⛔ **Read before `C-watch`** |
| ⚠ **count** | **18 open `DEBT-AIB`, not my ~22** *(30 ids, 12 resolved; `-007` is explicitly not ours)* |
| ⭐ **`E4` is PARTLY done** | `-028`(b)+(c) shipped. ⛔ **`sharedScopeKeys` is threaded but left at its default** ⇒ **rule 8b still cannot fire.** `-028`(a) — persisting `SubtreeAssetId` — remains `E5`'s prerequisite |

⭐ **Four `DEBT-AIB` rows have now paid for themselves**: `-012`, `-030`, `-028`, `-021`.

### ⚠ What the suspended visual check leaves unverified

**The table DRAWING** *(header order, an empty group as a header not a gap, elision at real widths, the
red/yellow tints)* · **the gestures** *(value-cell vs name-cell double-click, the `⋮` menu, F2)* ·
**the budget indicator**. ⛔ **Written and headlessly reasoned; nothing has seen them drawn.**

---

## 4A4. ✅ Batch 69 — ⭐⭐ **the table is LIVE, and a rail I wrote was VACUOUS**

📄 [`REPORT_Batch69_Tick_Schema_Watch_Outline.md`](REPORT_Batch69_Tick_Schema_Watch_Outline.md).
⭐ **`C-tick` · `DEBT-AIB-009` · `C-watch` · `C-outline` · `E4` finished.** Gates re-run by me,
snapshots unchanged, tracker **61 / 148**. Rows `BP-270`–`BP-274`.

### ⭐⭐ `C-tick` — **a SIDE TABLE, not a field**

| the three placements rejected | why |
|---|---|
| `BlueprintSlotEntry.InstanceVersion` | it is the **latent-cursor staleness token**. ⛔ **Two meanings on one field is the trap this programme keeps finding** |
| a NEW field on `BlueprintSlotEntry` | the entry is **exactly 16 bytes with a documented budget** ⇒ growing it shrinks payload in **every** tier, **and** enters the recorded frame — for a counter **no simulation code reads** |
| `BlueprintBlackboardHeader.Reserved` | **wrong granularity** — per entity-tier, but one entity hosts many slots |

⇒ ⭐⭐ **editor telemetry belongs outside the simulation layout.** 📌 **That choice is what makes
"`StructureHash` unchanged" STRUCTURAL rather than lucky** — the item adds no byte to any persisted or
snapshotted shape. ⚠ **Opt-in, default OFF, refcounted** so closing one panel does not disable another;
allocation-free on the steady path, asserted.

⭐ **Frozen comes free and there is no fifth definition of it:** all four stamps sit inside
`BlueprintTickSystem.Execute`, which opens `if (deltaTime <= 0f) return;`.
🔴 **The frozen rail could not even be WRITTEN before this item** — `AssetTick` was `null` on every row.
⚠ **BTree/HSM rows stay `null` (inert)** — the allowed partial; those hosts need their own stamp point.

### 🔴🔴 My `C-watch` design claim was stale TWICE — and the real defect was underneath

| I wrote | measured |
|---|---|
| *"`QuickReloadService:64` hardcodes `CompilerMode.Debug`"* | ⛔ **false** — it reads `asset.EditorMetadata.CompilerMode` |
| *"Debug emits no `PinValueChanged`"* | ✅ true — **and `AddWatch` already requested `Trace`** |
| 🔴 **the actual defect** | the request was guarded on `!_debugMaps.ContainsKey(assetId)` ⇒ **set a breakpoint first and the asset HAS a map**, so adding a watch requested **nothing**: `(pending)` forever, ⛔ **indistinguishable from "it has not changed"** |

⇒ ⭐ **Corrected in `DESIGN_Variable_Details_And_Editing.md` §7.** 📌 **Same shape as `G3` (§4A2): my
citation was wrong AND the thing behind it was worse than I described** — twice now, in the opposite
direction from Batch 63's lesson.

### ⭐⭐⭐ The silent-default pattern — **now a repo rule**

> **Their verdict, adopted verbatim:** *"what distinguishes the three from the harmless majority is not
> the default — it is that the caller HELD the value and did not pass it."*

⛔ **Not "ban optional dependencies"** *(every one was deliberately optional)* · ⛔ **not a generic
detector** *(one was written and thrown away — it flags dozens of correct defaults)*.
⇒ ⭐ **The control is a forwarding rail PER DEPENDENCY, asserted on the CONSTRUCTED OBJECT.**
📌 **The first `DEBT-AIB-009` rail was VACUOUS** — it scanned the caller's IL for the type, which the
registrar mentions **in its own signature** whether or not it forwards. ⭐ **Ask the object, not the
call site** — Batch 68's `C-dialog` probe taught the same thing one level down.
📄 **Filed in `.claude/CLAUDE.md`.**

### ⚠ Still unverified — the visual check, now cumulative

**Batch 68's list** *(table drawing, gestures, budget indicator)* **plus:** the **greying** of a stale
Watch row · pin/unpin gestures · the `Type` column hidden **on screen** · the outline **drawing**,
its header order and per-section **"+"**. ⭐ **The MEANING is asserted throughout** — which rows exist,
which section each lands in, what is highlighted, what refuses a dialog.

---

## 4A5. ✅ Batch 70 — ⭐⭐⭐ **the parameter model RUNS, and a rule had to be retired to let it**

📄 [`REPORT_Batch70_Parameter_Seam.md`](REPORT_Batch70_Parameter_Seam.md).
⭐ **`DEBT-AIB-021` · the Instance params seam · `G7`+`W10`.** Gates re-run by me, `StructureHash` and
`persistence-shape` **unchanged**, tracker **61 / 153**. Rows `BP-275`–`BP-279`.

### ⭐⭐⭐ `BP1031` RETIRED — **I reviewed the diff and I ACCEPT it**

> The rule refused an Instance that declared parameters, **fatally**, and its own message carried its
> reason: *"nothing supplies them at spawn."*

⭐⭐ **This batch makes that reason false** — the attach event carries the JSON,
`BlueprintDefinition.ParseParams` resolves it through the **same delegate the behaviour path uses**, and
the payload reserves the bytes. ⛔ **Leaving it standing would have shipped the seam UNREACHABLE** — a
producer with no consumer, the *"inert rule"* shape this programme keeps filing.

| ⭐ what makes the retirement sound, not convenient | |
|---|---|
| **kept DEFINED** | on `BP1024`'s precedent, so the number is never reused |
| ⭐ **listed `RETIRED` in the coverage ratchet** | it cannot silently fall out of the diagnostic set |
| ⭐⭐ **the positive test INVERTED, not deleted** | `Instance_WithParams_NoLongerEmitsBP1031`, and it asserts **no error of any code** — stronger than the row it replaces, because it proves the asset actually compiles |

⚠ **It was not in my handoff.** ⭐ **They reported it as a blocking premise AND decided it, which is the
right call when the decision is inside the item** — the alternative was not *"seam without retirement"*
but *"no seam"*. 📌 **Two documents already knew `BP1031` was load-bearing and NEITHER said to retire
it** — my design's §0 list and this plan's §7 tail both mention it in passing.

### ⭐⭐ `DEBT-AIB-021` was **two** defects and a third guard

| | |
|---|---|
| **(a)** | the emitted lambda discarded the incoming `json` — **the defect the row describes** |
| ⭐⭐ **(b)** | the **emit guard** `defaults.Count == 0 ⇒ return false` ⇒ **an asset with no defaults emitted NO resolver at all**. ⛔ **Fixing (a) alone would have left those assets exactly as broken** |
| ⭐ **(c), found by building it** | the `JsonSerializerOptions` field had **the same guard one level up** ⇒ fixing (b) broke the whole generated corpus with `CS0103`. **Same defect, different scope** |

⭐⭐⭐ **And a test had written defect (b) down as INTENT** — `ManagedAsset_NoVariableHasDefault_*`
asserted `ParseParams` is **absent**. ⇒ 📌 **a test asserting the absence of a feature is
indistinguishable from a test asserting a bug; only the design record separates them.** ⚠ **This is
the `2026-08-15` `.dev/` lesson arriving from the opposite direction** — there the record rescued a
thing that looked dead; here it condemned a thing that looked deliberate.

### 🔴🔴 Two rails were weak — ⭐ **"ask the artefact, not the thing that produced it", third time**

| the rail | why it could not fail |
|---|---|
| the emitter held **its own `=> 16`** | reverting the layout base to `0` left the emitted `ParamsOffset` at **16** — declaration and layout describing **different memory**, rail still green. ⇒ it now asks `FieldLayout.ParamsStructBase` |
| the cursor rail called `ParseParams` **by hand at `def.ParamsOffset`** | ⛔ **it read its expected value out of the field under test.** ⇒ it now drives the **real attach path**, against a stamped cursor pattern *(a plain `Clear()` would leave both cases indistinguishable — zeroes either way)* |

📌 **The series:** Batch 68 counted methods instead of call sites · Batch 69 scanned a signature instead
of the constructed object · Batch 70 read an expectation out of the field under test. ⭐ **One rule.**

### ⭐ The rest, briefly

| | |
|---|---|
| ⭐⭐ **`G7`+`W10`: ONE catalog, and it is ASSERTED** | *"a resolver and an initializer differ in what CONSUMES the value, never in what produces it."* `OneCatalogServesBothCallers` compares the two offer lists **and requires the offer to be non-empty** ⇒ ⛔ **it cannot pass by two empty lists agreeing** |
| ⭐ **identity pinned twice** | the stored string is the **generated FQN** *(architect `AQ2`)*, and a second rail **computes** it from `LibraryEmitter`'s formula rather than pasting it |
| ⭐ **a dangling producer is KEPT and reported** | ⛔ not silently cleared — *"resetting turns a broken reference into a plausible-looking deliberate choice"* |
| ⚠ **17 generated-source snapshots moved** | 📐 **I verified the diff: purely additive, ZERO removed lines** — two constants per Instance (`ParamsOffset = 16`, `ParamsSize => 0`). ⛔ **No offset moved, no field entered `State`** |
| ⭐ **`ReadManaged` is non-consuming within a frame** | the STOP did not fire — `Read()` returns `_front`, cleared only by `Swap()` ⇒ Replace's drain-twice survives. **Attach + Replace became classes; Remove stayed a struct** |

### ⚠ `DEBT-AIB-030` widens — **a fourth test, and the first OUTSIDE the AI registries**

📐 **My run: `StatelessGizmoRegistryTests.SC_GZ022_2_Register_UnregisteredType_Throws` failed in the
full unfiltered suite, passed in isolation and under `--filter`.** Counts varied **2 → 1 → 1** across
three runs of an unchanged tree. ⛔ **Nothing in this batch touches gizmos.**
⇒ ⭐⭐ **the cause is process-global registry state generally, not the behaviour/blueprint registries
specifically.** 📌 **Record it on the row; the mitigation is unchanged.**

---

## 4A6. ✅ Batch 71 — ⭐⭐⭐ **the floor exists, and it found a live defect within one commit**

📄 [`REPORT_Batch71_Hsm_Golden_Harness.md`](REPORT_Batch71_Hsm_Golden_Harness.md).
⭐ **`E0` · `E1`/`E2` backfill · `E7b` (count half) · `E6` PARTIAL.** Gates re-run by me; ⭐ **the
BLUEPRINT golden set is untouched — no file under `Hrot.Blueprints.Tests` moved at all.** Tracker
**61 / 157**. Rows `BP-280`–`BP-283`.

### 🔴🔴 THE FINDING — **`E6` is not the defect the plan describes.** ⭐⭐ RULED **(A): FQN everywhere**

📐 **Three sites, not two.** `HsmActionGenerator` hashes the **simple** name at both its sites; ⭐ but
**`Fhsm.Compiler.HsmFlattener` hashes whatever string the ASSET stored** — and `HsmEmitCore` stores the
**FQN** (`.OnEntry("Hrot.AI.Behaviors.CgfHsmNodes.StubIdle")`).

> ⇒ ⛔⛔ **the blob addresses `16038` while the registrar registers `32291`, so
> `HsmActionDispatcher.ExecuteAction` is a `TryGetValue` MISS.** ⭐⭐ **`HsmShowcase`'s entry and
> activity actions silently do nothing — today, with no collision anywhere.** ⚠ **`W3`'s
> allocated-but-bound-by-nothing shape, in the live path.**

| option | fixes the miss | kills the collision | breaks |
|---|---|---|---|
| ⭐⭐ **(A) FQN everywhere** | ✅ | ✅ | **4 call sites** in `FDP/Examples` *(coordinator-verified: `ApcHsmSetup.cs:66,70` · `UrbanCombatNewScenario.cs:631,635`)* |
| ⛔ **(B) simple name everywhere** | ✅ | ⛔ **no** | nothing |

⭐⭐⭐ **RULING `2026-08-17` (coordinator): (A).** ⛔ **(B) leaves `W9`/`E6` unfixed AND would make the
persisted asset store a simple name** — reintroducing the exact collision `W9` named, in the file
format. ⚠ **Four call sites in EXAMPLE projects is the cheapest breakage on the page**, and it is
visible at compile time. 📌 **Escalating rather than deciding was correct** — the key string reaches
outside Track E, which is plan-level by definition.

⭐ **What landed regardless, and is the precondition for either choice:** **`HsmActionKey` — one home
for the id.** Seven sites that each spelled out the FNV-1a now call it; the private duplicate is gone.
⭐⭐ **Plus `HsmActionIdAgreementTests`, which encodes the whole measurement** ⇒ **the decision is made
against a measurement, not a memory.** ⚠ **Invert those tests when (A) lands.**

### ⭐⭐ `E0` — what makes it a real gate

| | |
|---|---|
| ⭐⭐⭐ **it is asserted that it CAN fail** | `BothTiersRedden_WhenAnAssetChanges` mutates a corpus asset in memory. ⛔ **A new green gate proves nothing** — this was the STOP and it held |
| ⭐ **two tiers, and the asymmetry is the argument** | the shape file says *an asset changed*; only stored text says **which line**. ⛔ **An id change — `E6`'s whole subject — is not in the asset at all**, so the shape tier could never see it |
| 🔴 **the shipped corpus is TWO assets and NEITHER has a managed blackboard** | ⇒ `E1`/`E2` had **nothing to be backfilled into**. ⭐ **Seeded `HsmVariableShowcase`** *(Input + State@Behavior + State@Entity)* **and `HsmOrthogonalRegions`** *(`E3`'s subject — ⭐ the gate exists BEFORE the fix)* |
| ⭐ **corpus, not fixtures** | the production generator compiles them ⇒ **the solution build is a second gate on their validity** |
| ⭐⭐ **generalising over asset kind cost NOTHING** | `AiAssetKind` = three delegates ⇒ **BTree's 26 ungated assets are a REGISTRATION, not a rewrite** — ⭐ **a line item now, not a leftover** |
| ⚠ **one ordering is deterministic by accident** | `HsmBridgeEmitCore` iterates `Dictionary<int,…>.Values`; insert-only dictionaries enumerate in insertion order **in practice, not by guarantee**. 📌 **Flagged, not changed** — fixing it inside item 1 would have moved the baseline it was creating |

### ⭐ Two more things the floor exposed

| | |
|---|---|
| 🔴 **an HSM `Role=Input` variable reaches NO emitted output** | ⛔ **there is no HSM counterpart to the BTree bridge's `ParseParams`** ⇒ `DEBT-AIB-021`'s fix has nothing to fix on this host. ⭐ **Asserted as a GAP and named as one** *(Batch 70's rule: invert it, do not delete it)*. Filed **`BP-281`** |
| ⚠ **`E7b`'s runtime half is blocked, and NOT on `E3`** | 📐 **`ExpressionTargetField` is emitted NOWHERE** — 0 occurrences in either HSM emitter ⇒ **it never reaches the blob, so there are no bytes to assert.** ⭐ **My `E3` guess was wrong**; the block is "the field is not emitted at all", which is a bigger piece |

---

## 4A7. ✅ Batch 72 — ⭐ **`E6` shipped; `E3` turned out to be a different, larger thing**

📄 [`REPORT_Batch72_Occurrence_Identity.md`](REPORT_Batch72_Occurrence_Identity.md).
Gates re-run by me. ⭐ **Blueprint golden set untouched.** Tracker **61 / 161**. Rows `BP-284`–`BP-287`.

### ⛔⛔ USER RULING `2026-08-17` — **blueprint multi-occurrence is DEFERRED**

> ⭐⭐ **Verbatim:** *"the layout changing multi instance blueprint change not done, too many files
> affected, we can skip it - could be done sometime later once really needed."*

| | |
|---|---|
| ⭐⭐⭐ **`Q34`'s ANSWERS STAND** | `A` widen to 20 B · `A` caller-supplied `InstanceKey` · `A` 3-arg lookup = key `0`. ⛔ **A future session re-opens the BUILD, never the DECISION** |
| ⭐⭐ **and the deferral is COHERENT with what `Q34` §7 established** | this case is **REFUSED today (`AlreadyAttached`), not corrupted** ⇒ ⭐ **it buys a capability, not a correctness fix.** ⛔ **The dangerous occurrence case is `E3`'s, and it is unaffected by this deferral** |
| ⭐ **the edit surface is MEASURED, so re-dispatch costs nothing** | **187 `TryGetSlotOffset` call sites all stay correct** *(⭐ `Q34-C` doing its job)*; the real surface is ~10 files — `BlueprintSlotEntry` + `SlotEntrySize` · three tier `const`s + doc comments · `Initialize`/`Migrate`/`TryAttach` · the events · **`TryFindExistingTier` and `DetachFromEntity` PER KEY** · every payload-size assertion *(928/3936/16368 → 912/3904/16032)* |
| ⭐ **carry forward as the headline** | ⛔ **`AlreadyAttached`-per-key is not a detail** — leave it and the whole capability passes vacuously |

### 🔴🔴 `E3` ESCALATED — **my "signature widening" was wrong, twice over**

| my premise | measured |
|---|---|
| ⭐ *"`r` and `current` are already in scope"* | ✅ **true** — `slotIndex`, `stateId` in `HsmKernelCore` |
| ⛔⛔ *"a signature widening, not a data-flow redesign"* | 🔴 **false** |

1. 🔴 **The thunk cannot RECEIVE the occurrence** — `HsmActionDispatcher` dispatches through
   `delegate*<void*,void*,HsmCommandWriter*,void>`, and every registered id is a **static function
   pointer chosen at build time**. ⛔ **Regions are a runtime notion.**
2. 🔴🔴 **And there is nowhere for a second occurrence's bytes to live** — the generated thunk resolves
   its DTO at **`bb.BehaviorParameters[0] + <baked offset>`**, a fixed offset into the entity's
   **single 100-byte `BrainBlackboard`**. ⇒ ⭐⭐ **two occurrences have ONE HOME BY CONSTRUCTION.**

⇒ ⭐⭐⭐ **`E3` = a STORAGE MOVE + the delegate widening.** Per-occurrence bytes must come from the
partition allocator under `ComputeStatefulSlotKey(assetId, Scope.Node, occurrence, variableId)` —
⭐ **exactly the route `Q34` §7 rules for `E5`**, which means **one mechanism serves both**. ⚠ Spans
`Fhsm.Kernel`, the analyzer's thunk emission and the allocator ⇒ **`ExtDeps`**.
📌 **The design ANTICIPATED the `ExtDeps` change** *(§4.4, user-accepted)*; ⛔ **what it got wrong — and
I repeated — was the SIZE.**

⭐ **Landed instead:** three tests asserting the gap **with the mechanism named**, one reading the
analyzer's source rather than restating the rule. ⚠ **Invert them when `E3` lands.**

### 🔴🔴 A red suite nobody was gating — **`Fdp.Examples.Scenarios.Tests`, 12 failures, PRE-EXISTING**

📐 **Measured by me, both sides:** 12 failures on `HEAD` and ⭐ **the identical 12 on the pre-batch tree
`5d01a5c2a`** ⇒ ⛔ **NOT a Batch-72 regression.** ⚠ **They ran `Fdp.Examples.UrbanCombat.Tests` (29/29)
and not this one** — a reasonable pick, and the hole is mine for never listing it.

⭐⭐ **Why it is Track E evidence, not noise:** `ComponentDamage_Phase4_LocomotionCleared_ByHSM` —
*"the HSM did not clear locomotion"* — is **exactly the symptom of an HSM action silently not firing**,
which is the defect `E6` just fixed. ⛔ **And it is STILL red after `E6`** ⇒ either a second cause or
the fix does not reach these scenarios. 📌 **Diagnosis is Batch 73's item 1**, and the suite joins the
gate set either way.

### ⭐ Item 1 and item 4, briefly

| | |
|---|---|
| ⭐ **`E6`(A) shipped** | registrar ids and blob ids **agree for every corpus asset**, asserted against the **real compiled blob** on one side and the **running generated registrar** on the other · 4 example call sites moved · ⭐ **STOP swept clean** |
| ⛔ **premise: "the HSM emit baseline moves"** | **false** — the emitted `.g.cs` carries action **STRINGS**; the **ids** are computed at runtime by `HsmFlattener` and by the **analyzer's** registrar, ⭐⭐ **neither of which `E0`'s emit tier covers.** ⇒ 🔴 **a real coverage limit of `E0`, and it is why `E6` was invisible** |
| 🔴 **a rail of mine was vacuous — fourth time in five batches** | the first draft derived the registrar side as `FNV(FullName)`, **its own rule**, so reverting the analyzer left it green. ⭐ **Now it runs the generated registrar and reads `HsmActionDispatcher`'s tables.** 📌 68 counted methods · 69 scanned a signature · 70 read an expectation out of the field under test · **72 recomputed the rule under test** |
| ⚠ **and item 1 caused a real regression, caught by the gate** | a fixture varied `[HsmAction(Name=…)]` while every method stayed `Method{i}` ⇒ under (A) **every fixture collapsed to ONE id**, so the collision tests would have been testing the fixture. ⭐ **Their miss, stated plainly; the sweep looked for consumers that ADDRESS by name, not tests that ASSERT the old key** |
| ⚠ **item 4 is HALF, and they corrected their own claim** | *"three delegates ⇒ a registration, not a rewrite"* holds for **canonicalize** *(26 assets baselined)* ⛔ **not for emit**: `BTreeJsonGenerator` needs a Roslyn `Compilation` for `structSizeResolver` **and** `BTreeDeactivatorScanner.Scan` ⇒ **the emit tier needs a `CSharpGeneratorDriver` harness.** ⭐ **The reason is asserted, with HSM as the contrast** |

---

## 4A8. ✅ Batch 73 — ⭐⭐ **the floor learned to see generated code; my scenario hypothesis was wrong**

📄 [`REPORT_Batch73_Red_Suite_And_Generated_Floor.md`](REPORT_Batch73_Red_Suite_And_Generated_Floor.md).
⭐ **Items 1, 2, 4 landed; `E3` escalated a second time — as §3 of the handoff pre-authorised.**
Gates re-run by me; blueprint golden set untouched. Tracker **61 / 165**. Rows `BP-288`–`BP-291`.

### ⛔⛔ My hypothesis about the red suite was WRONG — ⭐ **and the harness could not have told me**

> I read `ComponentDamage_Phase4_LocomotionCleared_ByHSM` as *"the HSM did not clear locomotion"* ⇒
> `E6`'s symptom. 🔴 **It is not.**

📐 **The scenario throws at PHASE 2 (tick 21) and `ExitWith(1)` ends the run** ⇒ **tick 25 never
happens and phase 4 is never evaluated.** ⇒ ⭐⭐ **the test is a CASUALTY of phase 2, not evidence of an
HSM defect** — and it was so before `E6` too.

⚠ **First: the harness could not say WHY.** `ScenarioSubsystem` caught the failure and called
**`ExitWith(1)` — the same code for every phase** ⇒ a red could only report *"exit 1"*.
⛔⛔ **A red that names no cause trains everyone to ignore the gate.** ⭐ **They fixed the harness first**
*(`LastFailure` retained and surfaced)*, which produced both diagnoses in **one run each**.

| cluster | measured message | attributed |
|---|---|---|
| `ComponentDamageScenarioTests` **× 5** | *"Phase 2 FAILED tick=21: health=100 still at max=100 after hit at tick 20"* | **damage / event pipeline** |
| `DistributedTankScenarioPhaseATests` **× 7** | *"Phase B3 FAILED tick=25: ghost not promoted in time"* | **DDS replication / ghost promotion** |

⇒ ⭐ **0 fixed, 12 quarantined — correctly**, per the STOP: both causes are outside this programme.
⭐⭐ **Each skip carries the phase, the measured message, the attributed subsystem and the note that
they are identical on `5d01a5c`.** ⇒ **the suite is in the gate set at `56 / 68, 12 skipped`.**

📌 **The lesson for me:** ⛔ **I attributed a failure from its NAME.** ⭐ The name said `_ByHSM`; the
mechanism said phase 2. ⚠ **Same shape as reading "is it used?" for "is it wanted?"** — a label is not
a measurement.

### ⭐⭐⭐ The generated-code tier — **the acceptance test passes**

| | |
|---|---|
| ⭐⭐ **it reddens when `E6` is reverted** | `TheGeneratedRegistrarIsUnchanged` fails at **line 18**, the `RegisterAction` id line: baseline **`16038`** (FQN), simple-name key would emit **`32291`**. ⭐ **A second test derives that id independently of the generated text**, so the two sides cannot agree by construction |
| ⭐ **determinism across two processes** | not just in one |
| ⛔ **BTree's emit tier still NOT reached, and the reason is named** | `BTreeJsonGenerator` builds `structSizeResolver` from the **semantic model** and runs `BTreeDeactivatorScanner.Scan` over **real method bodies** ⇒ a synthesized compilation emits **fallback output — a baseline of what production never produces.** ⭐⭐ **It needs the REAL solution compilation** |

### 🔴🔴 `E3` — escalated again, **with the census, and it changes the question**

| surface | count |
|---|---|
| `[HsmAction]`/`[HsmGuard]` methods | **55**, across **25 directories** — incl. **FastHSM's own demos/tests** and **both `FDP/Examples` projects** |
| kernel `ExecuteAction`/`EvaluateGuard` call sites | **13** |
| ⭐⭐ emitters producing the fixed `delegate*` shape | **FIVE** — incl. ⚠ **`CSharpEmitter`: the BLUEPRINT side registers HSM thunks too** |

⇒ 🔴 **widening the delegate is an ABI break reaching every one of those.**
⭐⭐⭐ **But it need not widen — 📄 [`Architect_Question_35`](Architect_Question_35_Hsm_Occurrence_Delivery.md), raised `2026-08-17`.** 📐 **Measured: `contextPtr` is
OUR `HsmKernelBridge` (not `ExtDeps`) but the kernel sees it as an opaque `void*`; `HsmCommandWriter`
is a KERNEL struct already passed to every action** ⇒ ⭐⭐ **the kernel can put `(regionSlotIndex,
stateId)` there with no signature change anywhere.** ⚠ **Guards are unserved — and measurably free:
`VE-DEBT-004`, no production `[HsmGuard]` exists.**

---

## 4B. ⏭ Track E — ⭐⭐⭐ **HSM catch-up** *(the gaps, collected)*

> ⛔⛔ **USER RULING `2026-08-16`:** *"the HSM integration is in bad shape now, for long time not updated
> and not actively used, blueprints and BTrees were favorised. **So if something is not present in HSM,
> it is not because it is not needed, just not implemented yet.**"*
> ⇒ ⭐ **Every row below is WORK, not a scope decision.** ⛔ **`Q33-E` is answered: PHASED, not abandoned.**
> ✅ **User accepted the multi-occurrence cost** *(`2026-08-16`)*.

⭐ **The pattern, stated once:** HSM's **authoring model is ahead of its runtime** in four places. BTree
and blueprints both provision per-occurrence storage; **HSM alone does not.** ⇒ **BTree is the template
throughout — adopt, do not invent.**

| | item | measured gap | depends on |
|---|---|---|---|
| ~~`E1`~~ | ✅ **DONE (Batch 67)** — emitter consumes `Role`/`Scope` | `HsmEmitCore` + `HsmBridgeEmitCore`: **0** refs; `BTreeBridgeEmitCore`: **45**. `HsmBlackboardVariableDto` persists both faithfully ⇒ **HSM has NO authored variables at runtime at all** | — ⭐ **the entry point; everything else assumes it** |
| ~~`E2`~~ | ✅ **DONE (Batch 67)** — slot provisioning, BTree-style | adopt `ComputeStatefulSlotKey` + `BlueprintBlackboardPartitions` — ⭐ **the same allocator Instances and the BTree bridge already share** | `E1` |
| **`E3`** | ⭐⭐ **occurrence in the action key** — 🔴🔴 **RE-MEASURED Batch 72: this is a STORAGE MOVE, not a signature widening.** The thunk dispatches through a `delegate*` whose id is a **static function pointer chosen at build time**, and it resolves its DTO at `bb.BehaviorParameters[0] + <baked offset>` in the entity's **single 100-byte `BrainBlackboard`** ⇒ ⛔ **two occurrences have one home by construction.** ⭐ Per-occurrence bytes must come from the partition allocator under `ComputeStatefulSlotKey(…, Scope.Node, …)` — ⭐⭐ **the same route `Q34` §7 rules for `E5`.** *(original entry:)* | `hash(method @ fieldOffset)` has **no region/state in it** ⇒ concurrent regions running one action **write the same bytes**. ⭐ **`r` and `current` are ALREADY IN SCOPE at the `ExecuteAction` call site** ⇒ a signature widening, not a redesign. ⭐ **the params-base change (§4h) folds into this same seam** | `E2` ⚠ **`FastHSM` `ExtDeps` change** |
| ~~`E4`~~ | ✅ **DONE — (b)+(c) Batch 68, `sharedScopeKeys` Batch 69.** ⚠ **Rules 8/8b still will not fire on assets LOADED FROM DISK** until `-028`(a) persists `StateNode.SubtreeAssetId` — ⭐ **expected, and it is `E5`'s prerequisite, not an `E4` gap.** *(original entry:)* ⚠ **wire `HsmValidator` rules 8 / 8b** — ⭐⭐⭐ **FILED as `DEBT-AIB-028`, WITH AN ACTIVATION RECIPE** *(found Batch 67)*: *"(b) `_isStatefulSubtree` defaults to `_ => false` and production never supplies a real resolver; (c) the production `HsmAssetValidator` entry point isn't threaded… wire `id => catalog.TryFind(id,out a) && a.HasAnyStatefulNode()` through the production validator ctor."* ⇒ ⛔ **do not re-derive it** | correct errors, but injected resolvers default to `_ => false` / `_ => Empty` and **both production call sites use the default ctor** ⇒ never fire. XML doc says *"Production should wire this"* | ⭐ **do BEFORE `E5`** — the guard should be honest before the runtime makes the hazard real |
| **`E5`** | ⭐⭐ **subtree hosting runtime** — 🔴 **NEW PREREQUISITE (Batch 67): `StateNode.SubtreeAssetId` is NOT PERSISTED.** `DEBT-AIB-028`(a): *"a NEW field, not persisted to JSON, and no real HSM asset sets it."* ⇒ **persist it FIRST.** 📌 `DEBT-AIB-029`: the check walks **DIRECT children only** — deeper nesting undetected | `SubtreeAssetId` is read **only** by `HsmValidator`; FastHSM kernel **0**, HSM emitters **0**, shipped assets **0**. ⇒ ⭐⭐⭐ **serves TWO rulings at once** — HSM-over-BTree composition **and** the latent sub-behaviour decision *(`#33` §1.5.4: `C`, subtree not action)* | `E3`, `E4` |
| **`E6`** | **`W9`** — simple-name hash | `HsmActionGenerator:517/630` — `ComputeHash(action.Name)`; `MethodInfo` carries `FullName` too. ⚠ **TWO re-bake sites**, reconciled *"in lockstep via shared `ResolveStatefulSlotKey`"* | independent |
| **`E7a`** | ⭐ **wired params — host context on the resolver** | ⛔ **RE-SCOPED `2026-08-16`; no longer needs a design call.** ⭐⭐ **Neither host has input wiring** — a BTree node binds a **field of the behaviour's params struct**, whose value **the resolver wrote at activation** ⇒ *"resolver fills, nodes read"* is already universal. **A resolver already has `world` + `self` and can reach any component; what is missing is ADDRESSING** — it cannot name *"my parent's `TargetPos`"*. ⇒ **pass a host context (variable accessor) alongside `world, self`**, ⚠ **by NAME, never raw offset** (`StructureHash`-versioned). ⛔ **Not a second supply mechanism** *(ruling 9)* | `E5` |
| **`E7b`** | **the OUTPUT binding** | ⛔ **`ExpressionTargetField` is an OUTPUT binding** — *"blackboard field that receives the expression **result** of `ActionFunction`"* — and **both hosts already have it** (BTree per node, HSM per transition). `FIX-01-REPORT:43`'s *"no per-node"* meant **per-node**. ⇒ wire it at runtime + fix `CountNodesReferencingVariable` returning `0` ⇒ ⚠ **references through it are UNCOUNTED today** | independent |
| ~~`E7`~~ | ⛔ **the old "HSM binding model" item is DISSOLVED** | ⭐ **`VE-DEBT-001`'s *"needs an architect design call"* is DISCHARGED** — it was the **four-slot / one-DTO** question, and **the subtree ruling removed it: a subtree is HOSTED, not slotted.** 📌 Still true and unrelated: **`VE-DEBT-004`** — no production `[HsmGuard]` exists; **`HSM-016`** is an unresolvable id, zero hits anywhere | — |

### 🔴 `E0` — **the HSM golden harness is a PREREQUISITE, and a batch of its own** *(Batch 67 ruling)*

⛔ **The corpus decision was (b): unit-test-only cover, accepted.** ⭐⭐ **Their reframing, which I had
too small:** *(a) is not "add some HSM assets" — there is **no HSM golden harness at all**: no corpus,
no shape file, no structure-hash gate.* ⇒ ⭐ **Build it as its own batch, and it gives `E3`–`E7` their
regression floor.** 📌 **Backfill `E1`/`E2` into it when it lands** — they shipped under unit tests only,
and this line is where that is written down.

### ⭐ Sequence within Track E

✅ ~~`E1`~~ → ✅ ~~`E2`~~ → ⭐ **`E0`** *(the harness)* → **`E4`** *(recipe filed)* → **`E3`** → **`E5`** *(persist `SubtreeAssetId` first)* → **`E7a`** *(wired params —
needs `E5`'s host)* → **`E6`** · **`E7b`** *(both independent, any time)*.

⚠ **`E1` is already in the main order** (after `G3`) because the parameter story needs it. **The rest of
Track E follows the parameter work**, not interleaved with it.
⭐⭐ **Nothing in Track E now needs a design call.** `E7` was the one item that did, and
📄 [`Architect_Question_33`](Architect_Question_33_Blueprint_Brain_Tier.md) §1.5.8 dissolved it.

### ⛔⛔ Track E RAILS — **"done" was undefined for every row until now**

⚠ **Add these to the batch that builds each item.** ⭐ **`E3`'s is the one that matters most** — it is
the direct inverse of the defect.

| | rail |
|---|---|
| **`E1`** | an HSM asset declaring a `Role=State` variable **emits a slot-manifest entry**; the key matches **BTree's algorithm for the same inputs** ⇒ ⛔ **a second key algorithm fails the rail** |
| **`E2`** | an HSM behaviour with **N** state variables gets **N slots at activation**, each **zeroed** — assert through the production ingress path, not a hand-built manifest |
| **`E3`** | ⭐⭐⭐ **two concurrently-active orthogonal regions running the SAME action write DIFFERENT bytes.** ⛔ **Today they write the same ones** — this test fails before the change and passes after |
| **`E4`** | an asset that trips rule **8** / **8b** produces the error **through the production constructor** (`new HsmValidator()`), ⛔ **not only with hand-injected resolvers** — that is exactly what is wrong today |
| **`E5`** | a state hosting a subtree: **entry** provisions + resolves · **tick** re-enters · **exit** invalidates the cursor · **completion** raises the event. ⭐ **Plus: a LATENT child suspends and resumes across ticks** |
| **`E6`** | two actions with the **same simple name in different types** get **distinct ids**, and ⚠ **both re-bake sites agree** *(blob key + thunk key)* |
| **`E7a`** | a child resolver **reads the host's variable by NAME**; ⭐ **a `StructureHash` mismatch fails CLOSED** *(returns `false`, never a silent zero)* |
| **`E7b`** | `CountNodesReferencingVariable` is **non-zero** for a field bound through `ExpressionTargetField` |

### 🔴🔴 Track E has **NO golden coverage** — measured `2026-08-16`

⛔ **`persistence-shape.txt` is 43 assets, ALL `.bp.json`. `grep -ci "hsm\|btree"` ⇒ 0.**

⇒ ⭐⭐⭐ **The golden corpus does not cover HSM or BTree at all**, so `E1`, `E3` and `E6` change emitted
output and **no golden gate would notice.** ⚠ **This is `BP-240`'s shape inverted** — *the gate is green
because the corpus does not contain the thing*, not because the code is right.

| item | emitted-output impact | guarded by |
|---|---|---|
| `E1` · `E3` · `E6` | ⭐ **HSM emitted output CHANGES** *(new manifests / new keys / new ids)* | ⛔ **unit tests only** |
| `E5` | ⭐ **byte-identical for the corpus** — **0 shipped `.hsm.json` set `SubtreeAssetId`** ⇒ purely additive | additive |
| ~~**Instance params seam**~~ (§4g) | ✅ **SHIPPED Batch 70, and byte-identical as predicted** — 296 Instance assets, **0** with parameters. ⚠ **`BP1031` is now RETIRED**, so *"0 declare parameters"* is a fact about the corpus, ⛔ **no longer a rule keeping it so** | 📄 **`DESIGN_Parameter_Model.md` §8 rails** |
| `E7a` · `E7b` | signature / editor only — **no emitted-output change** | unit tests |

⇒ ⚠ **A decision the first Track-E batch must state, not assume:** *extend the corpus to HSM/BTree
assets, or accept unit-test-only cover and say so in the report.* ⛔ **Do not let it pass silently.**

### ⛔ Not HSM-only, but discovered here — **the latency rail**

🔴 **Nothing forbids a latent node in an AiPrimitive.** `V_DispatchKindCompatibility` checks
intent-vs-hosting (`BP1022`/`BP1023`) and event graphs (`BP1025`) — **nothing about latency** — and
`BTreeEvaluate` emits `return TickCore(…) == NodeStatus.Success;` ⇒ ⭐⭐ **`Running` maps to `false`**,
so **a latent CONDITION silently reads false while it waits**, then flips true later with `__phase`
left mid-sequence. ⛔ **Silent wrong behaviour, not an error.**

⭐ **The rule: latency is legal iff the hosting can RE-ENTER.**

| intent → hosting | |
|---|---|
| ⛔ `Condition` → `BTreeCondition`, `HsmGuard` | **never legal** — a condition must answer *this tick* |
| ✅ `Action` → `BTreeAction` | `NodeStatus.Running` |
| ✅ `Action` → HSM **Activity / subtree host** | re-entered every tick |
| ⛔ `Action` → HSM **Entry / Exit / Timer** | one-shot ⇒ **a silent hang** |

⭐⭐ **A third dimension on a validator that already exists**, and ⭐ **the detector is already built** —
`MacroLatency.IsLatent` / `FindTransitivelyLatentNode`, used today by `BP1661`. ⇒ **the rule is
missing, not the analysis.** 📌 **Filed, not numbered** (rule 3).

---

## 5. ✅ The two prerequisites — **one is solved, one is a live defect**

| | verdict |
|---|---|
| ✅ **the paused snapshot-vs-live pass** | ⛔ **STRIKE — my concern was wrong.** `universal-breakpoints-DESIGN.md` §8.4 designs against it and it **shipped**: an edit while paused is **staged, not written**; on Step/Continue the manager **restores `_liveRepo` from `_postTickSnapshot` FIRST, then drains** — coordinator-verified at `DataBreakpointManager:495-498` and `:514-517`. **The rewind cannot discard the edit.** Cost: a named **1-tick latency compromise** |
| 🔴🔴 **the surgical ECB field write** | ⭐ **Ruling 14 already rules it in and names the signature** — `SetComponentFieldRaw(Entity, int typeId, int byteOffset, void* src, int size)` in `Fdp.Core`. ⭐⭐ **And it is now a FIX, not an improvement:** `StageMutation:530` takes a **whole component**, `DrainPendingMutations:548-575` writes it with `SetComponentRaw` **(no offset)** *after* the restore ⇒ **every other field of that component is reverted post-tick → pre-tick.** On the shared `Blackboard1024`, **editing one blueprint variable reverts a tick of BTree and HSM state.** ⚠ **The payload's exact origin is unverified — that is the red-first test** |
| ⛔ **correction to v1** | my `MaxComponentSize` argument was **already retracted** in the ANSWERS doc — the check is `>` and the blackboard is exactly 1024, so **it fits**. **The reason is sharing, not size** |

---

## 6. ✅ The three Track C decisions — ALL RULED `2026-08-15`. Kept for the reasoning.

| | the conflict |
|---|---|
| **① `C5` — write both copies, or stage?** | 🛑 **Three records rule AGAINST writing the live copy while paused.** `Slice2_Candidates.md:325-360`: the paused edit *"does **not** mutate `_liveRepo`"* — queued, restored-then-drained at the **N+1 boundary** — justified by **Flight Recorder linearity** and **`DataPolicy` divergence**; `BTree_Editor_..._Design.md:869` gates live edit behind a **"Make Editable" toggle + confirmation banner**; `Blackboard_Authoring_DD:1340` calls live-edit *"orthogonal to this DD"*, Slice 3. ⇒ **Rulings 12 (immediacy) and 16 (both copies) contradict this.** ⚠ **Honest caveats:** that file is titled *"Candidates"* (a proposal menu, not a dispatched design) and reasons about **ECS component** breakpoints, whereas `C5` targets **blackboard variables** |
| **② `C3` — the gesture** | ⛔ **No record of a three-dot or double-click "edit value" gesture**, the `⋮` menu is enumerated **exhaustively without one**, and **double-click is already bound to inline rename**. ⇒ **the requested gestures collide with shipped bindings** |
| **③ `C1`/`C7` — table or form?** | **D7 (authoritative) routes variables as a per-variable FORM with a `Default` row**; the plan says **one TABLE with a Value column**. ⇒ **which shape wins?** |

⭐ **Everything else is now ruled.** `D1` answered · `D2` likely **dissolved** by the existing
`BlackboardVariableRole` carrier · `D3` disposition still open but harmless.

---

## 7. ⭐ Order *(revised `2026-08-16`)*

⭐⭐ **DONE: Track B + `S5` (65) · `G4` · surgical write · `G1` · `C-sections` (66) · `W7c` · `W7a` ·
`G3` · `E1`+`E2` · latency rail (67) · `C-table` · `C-dialog` · `W7b` (68) · `C-tick` · `DEBT-AIB-009` ·
`C-watch` · `C-outline` · `E4` (69).**
⛔⛔ **TRACKS B, C AND D ARE ALL CLOSED** — the `G`-list included. ⇒ ⭐⭐ **everything remaining is
Track E, plus one item waiting on the user:**

| ⭐ next | what | why now |
|---|---|---|
| ⭐⭐⭐ **`BP-281`** — HSM has no `ParseParams` counterpart | an HSM `Role=Input` variable reaches **NO emitted output** | ⭐⭐ **NOT BLOCKED — 📄 [`DESIGN_Hsm_Storage_Model.md`](DESIGN_Hsm_Storage_Model.md) §2.** ⚠ **I pulled it from Batch 74 saying its destination was undecided; measured, it is decided by symmetry with BTree** — `BehaviorParameters` at packed offsets. ⛔ Only the **hosted/multi-occurrence** case waits on `E3` |
| ⭐⭐ **`E7b`'s runtime half** | 📐 **`ExpressionTargetField` is emitted NOWHERE** — 0 refs in either HSM emitter ⇒ it never reaches the blob | ⭐ the authoring half round-trips and the validator already reads it ⇒ **a producer with no consumer** |
| ⭐ **BTree's emit tier**, over the **REAL solution compilation** | a synthesized one emits **fallback output — a baseline of what production never produces** | closes the last golden hole |
| ⛔⛔ ~~the `InspectorWindow` "STATIC PARAMETERS" retirement~~ | ⭐⭐ **WITHDRAWN `2026-08-17` — user: *"no rush removals."*** 📐 **Measured when the user asked what it was:** it is the **default-value editor for the `ExpressionTargetField` variable**, not parameters; its duplicate-CODE half was resolved by `BP-267` (Batch 68); what remains is a **node-scoped affordance the asset-scoped table lacks** — ⛔ **and it authors a binding whose runtime `E7b` is only now building** | ⚠ **I carried this for five batches on a LABEL I had never measured.** ⭐ Rule recorded in `.claude/CLAUDE.md` |
| ⭐⭐⭐ **`E3` — ✅ UNBLOCKED `2026-08-17`** | 📄 **[`Architect_Question_35`](Architect_Question_35_Hsm_Occurrence_Delivery.md) RESOLVED**: the pair `(regionSlotIndex, stateId)` rides **`HsmCommandWriter`** ⇒ ⛔ **NO delegate change** · **ONE path** onto the allocator · ⭐ the kernel supplies identity, **the thunk does the lookup** | ⭐⭐ **THE dangerous occurrence case** *(`Q34` §7)* — the only one that silently corrupts. ⭐ `HsmOrthogonalRegions` is already in the corpus · ⚠ **guards unserved, and that limit must be ASSERTED** |
| ⏭ **then** `E5` *(by KEY — `Q34` §7)* → `E7a` | | |
| ⛔⛔ **DEFERRED by the user** | **blueprint multi-occurrence** — 📄 [`Architect_Question_34`](Architect_Question_34_Blueprint_Occurrence_Identity.md) | ⭐ **answers stand, build deferred** *("once really needed")*. §4A7 holds the measured edit surface |
| ⚠ **the Track C VISUAL CHECK** | cumulative across batches 68–70 | ⛔ **no headless test can do it** — it needs a human at the editor. §4A3/§4A4 hold the list |

✅ **DONE this round:** the parameter seam · **`G7`+`W10`** *(the last `G`-row)* · **`E0`** the golden
floor · `E1`/`E2` backfilled · `E7b`'s count half · **`E6`(A)**, a live defect · BTree's shape tier.

📌 **`W9` is `E6`; `W11` re-scoped into `E7a` + `E7b`; `W6` DROPPED; `W8`/`W12` were duplicates.**

📌 **The latency rail (§4B tail) is independent** — compiler-side, and it guards a **silent wrong
answer**, so it can land any time after Track B.

✅ **The HSM emitter slice (`E1`) SHIPPED in Batch 67** — kept here for the reasoning that queued it:
`HsmEmitCore`/`HsmBridgeEmitCore` read **0** `Role`/`Scope` against `BTreeBridgeEmitCore`'s **45**, so
*"multi-field editor-authored inputs for BTree **and HSM**"* could not work on HSM at all.

📌 Still filed, not fixed: **`BP-241`** · **`BP-242`** · the **`Fdp.Toolkits.Tests` race**.

### ⛔ Parked — 📄 [`Architect_Question_33_Blueprint_Brain_Tier.md`](Architect_Question_33_Blueprint_Brain_Tier.md)

⭐ **Blueprint as a brain tier + suspendable sub-behaviours.** ⛔ **NOT relayed** — the user ruled there
is no architect for it: *"we need to resolve that ourselves, together."* **Parked behind the parameter
story** by user instruction, minus the HSM-emitter slice above.

| | |
|---|---|
| ✅ **safe to park** | `Q33-D`'s widening is **runtime-only** — the slot table is `[DataPolicy(NoSave)]` and the scenario format is already a per-assignment list ⇒ **a later change, not a migration** |
| ⚠ **one carry-forward** | ⭐ **Track C's row identity `(AssetId, Entity, VariablePath)` gains a fourth component if `D2` ever happens.** Note it in the design; **do not build for it** |
| 🔴 **the finding that drives it** | **latent REQUIRES Instance dispatch** — `StateStructBase` is 8 (AiPrimitive) vs 16 (Instance), and the 16 **is** the `BlueprintLatentCursor` ⇒ **a blueprint hosted as an action node cannot suspend** |
