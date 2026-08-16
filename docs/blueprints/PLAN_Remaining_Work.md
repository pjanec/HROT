# PLAN — what is left *(revision 4, `2026-08-16`)*

> ⭐⭐⭐ **REVISION 5 (`2026-08-16`).** ✅ **The parameter model is RULED end to end (§4g)** — Instances use
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

## 1. ✅ Done — merged through `9edf13fdf`

Batches **56 · 58 · 57 · 59 · 60 · 61(1–2) · 63**. Phase A correctness is complete except `W6`/`W7`,
which the sweep has now **re-specified** (§4).

---

## 2. ⏭ Track B — struct support ⭐ **all three now have design records. Ready to build.**

| | design record | what changed |
|---|---|---|
| **`S2`** struct size resolution | `.dev/btree-ai-action-binding/reports/BATCH-03-REPORT.md:34` — ⭐⭐ **a stated mandate**: *"`StructSizeResolver` lives in Generators and is **injected via `Func<string,int?>`**; Persistence stays netstandard2.0 / Roslyn-free"*, from a **user decision `2026-06-15`** (`TASK-DETAIL.md:58`) | ✅ **my lean CONFIRMED, with a shipped precedent** (`BTreeBlackboardPackHelper.Pack(vars, Func<string,int?>, out total)`) ⚠ **but `:100` files `DEBT-AIB-012`: the resolver is ALREADY a third copy of `ComputeStructSize`** ⇒ **a naïve `S2` makes a fourth** |
| **`S3`** `MarshalFromBytes` struct arm | `_DONE/blueprints-1/TASK-DETAIL.md:1840` — *"reflection-based for structs (UI decode only, not on the probe path)"*; `blueprint-dbg-1:193` — *"primitives/**small** structs only"* | ✅ **CONFIRMS** — ⭐ designed in from the start, **never built**. The record also **bounds** it |
| **`S4`** fixed-list `Capacity` | ⭐ **outside `.dev/`** — `docs/blueprints/Blueprint_List_Variables_Design.md` §3:63–72 **specifies the exact missing branch**: *"`StaticTypeRegistry.TryResolve`: new branch — when `Capacity > 0` and the element resolves unmanaged, return the list `IrTypeRef` (unmanaged, real size)"* | 🔴 **REFINES** — a **designed-but-unbuilt branch**, not a bug. ⚠ Must honour `SizeReliable = false` and the `__List_{Elem}_{N}` wrapper name |
| **`S5`** one picker | `blueprint-finalize/BF-BATCH-FIXEDSTRING-INSTRUCTIONS.md:33` treats **`SelectableTypeIds`** as *the* picker list; `EditorOfferableTypeIds` is never mentioned | ⭐ **CONFIRMS the defect is real and undocumented** — the second list grew later on the compiler side |

---

## 3. ✅ Track C — the panels. ⭐⭐ **DESIGNED. The three decisions are all ruled.**

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
| **`W6`/`W7`** | 🛑 **`W7` CONTRADICTED.** `Blackboard_Authoring_Detailed_Design.md` §7.7/§9.1–9.6 is a **complete design**: a **suppressible WARNING** (not an error) with per-conflict metadata + an *"Allow concurrent writes"* checkbox; writers classified by **whether the action mutates the ref parameter** (optional annotation, conservative read-write default) — **not** by `W6`'s static projection; and §9.1 says **extend the existing `OutputLaneMask` conflict infrastructure**. §9.5 adds an **Approach B Sync-Out** case we omitted. ⇒ ⭐ **`W6` is downstream of a mechanism the design does not use — re-derive `W6` from §9.6 or drop it.** ✅ `[SharedAiCondition]` re-measured at **0 production usages** |
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

### 4g. ⭐⭐⭐ NEW — the unified parameter model *(user rulings `2026-08-16`)*

📄 **[`EXPLAINER_Where_Parameters_And_State_Live.md`](EXPLAINER_Where_Parameters_And_State_Live.md)** §5c–§5d.

| ruling | what it commits us to build |
|---|---|
| ⭐⭐⭐ **Instances use the RESOLVER shape** — *"Instances could and should reuse the param parsing and resolving"* | ⛔ **`Overrides` is not the mechanism.** The resolver pipeline serves every host ⇒ ⭐ **`G1` is now load-bearing for blueprints too** |
| ⭐⭐ **params live in the Instance's own slot** | slot becomes **`[Cursor 16][Params N][State M]`**; `StateStructBase` shifts by `N`. ⛔ **`FieldLayout`'s `startOffset: 0` for parameters would land on the cursor** — safe today only because Instances have none |
| ⭐⭐ **runtime attach carries params** | `AttachInstanceBlueprintEvent` gains a payload and `BlueprintEventIngressSystem` gains a resolve step, mirroring **parse-before-commit**. ⭐ **The delegate already takes a destination `byte*`** ⇒ the pipeline is reused **unchanged**, only the pointer differs |
| ⭐⭐⭐ **sections are the classification** | ⛔ **no `Role`/`Scope` control on any host.** Split the one Variables section per kind; give BTree/HSM their own `IMyBlueprintModel`. ⇒ **`Q-k` dissolves** — 📄 Track C design §1c |

⭐ **`R4` — installing an Instance at runtime WITH params, e.g. from a running master blueprint — had
NO design anywhere.** These rulings are it.

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

🔴 **The collision they guard is real:** the HSM action slot key is `hash(methodName @ compileTimeOffset)`
through one shared `ActionTable`, projected at a static offset in the **one** `BrainBlackboard` — **no
region index anywhere in the path** ⇒ two concurrently-active orthogonal regions running the same
action write the same bytes. ⭐ BTree is immune: it provisions per-scope slots via `ResolveStatefulSlotKey`.

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

**Track B now** *(Batch 65, dispatched)* → **`S5`** *(the dialog's Type picker needs ONE offerable list)* →
**`G4`** *(cheapest item on the page — a silent-overwrite guard, `W1`'s sibling)* →
**the surgical field write** →
⭐ **Track C**, now leading with **`C-sections`** *(split Variables per kind — §4g's ruling)*, then
table → dialog → Watch → **`C-outline`** *(BTree/HSM supply their own section list)* →
**`G1`** *(the split — ⭐ now load-bearing for blueprints too)* → **`G3`** *(service singletons)* →
⭐ **the Instance params seam** *(§4g: params in the slot · attach carries a payload · resolve-before-commit)* →
⭐ **blueprint multi-occurrence** *(§4h: `(blueprintId, instanceKey)` — `D2`, now in scope)* →
⭐ **the HSM emitter slice** *(`Role`/`Scope`, without which HSM has no authored inputs at all)* →
⭐ **HSM multi-occurrence** *(§4h — user accepted the cost; `ExtDeps` change)* →
**`W7` re-derived** → **`G7`+`W10` as ONE picker** → **`W9`** →
**last: `W11`** *(needs a joint design call, and weaker than filed)*.

⭐⭐ **`2026-08-16` — the HSM emitter slice enters the queue.** `HsmEmitCore`/`HsmBridgeEmitCore` read
**0** `Role`/`Scope`; `BTreeBridgeEmitCore` reads **45** ⇒ *"multi-field editor-authored inputs for
BTree **and HSM**"* — the stated goal — **cannot work on HSM until this lands.** It needs nothing from
the parked brain-tier work. ⇒ **inserted after `G3`.**

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
