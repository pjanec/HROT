<!--STATUS
state: LIVE
updated: 2026-08-18
current-answer: the whole file - it is an INDEX over the design corpus, not the truth
stale-below: nothing. Rows marked CORRECTED carry their correction inline.
note: every quote is verified verbatim by scripts/rulings-check.py; a rotted quote
  fails the gate, so this file cannot silently drift.
-->
# ⭐⭐⭐ RULINGS — **the canon. READ THIS FIRST, EVERY SESSION.**

> ⛔⛔ **This file exists because the coordinator kept re-deriving settled decisions from CODE after
> compaction, and steering development on a wrong base.** *(user, `2026-08-17`: "we start over and over
> after compaction, you forget all the design decisions and then steer the development on wrong base
> and act as if you never seen any of that.")*
>
> ⭐⭐ **Code answers *"how it IS."* ⛔ It can NEVER answer *"how it was MEANT to be."*** ⇒ **the second
> question has exactly one source, and it is the design corpus indexed here.**
>
> ⭐ **This is an INDEX, not the truth.** The cited documents are the truth. ⭐⭐ **Every quote below is
> verified verbatim against its source by `scripts/rulings-check.py`** — ⛔ **a rotted quote fails the
> gate**, so this file cannot silently drift.

---

## 0. ⭐⭐⭐ How to use this file

| when | do |
|---|---|
| ⭐⭐⭐ **session start / after compaction** | **READ THIS WHOLE FILE.** It is short on purpose |
| ⭐⭐ **before answering ANY design question** | find the row. ⛔ **If there is no row, SEARCH the corpus before answering** — §4 says where |
| ⭐⭐ **before writing a handoff item or architect question** | ⛔ **cite the design basis PER ITEM**, or write *"searched `<where>`, no design record found"* |
| ⭐ **when you FIND a ruling in the corpus** | ⭐⭐ **ADD A ROW IMMEDIATELY.** ⛔ Every row below was learned the expensive way |

---

## 1. 🔴 THE VARIABLE MODEL — **most-violated area; four wrong turns in one day**

| id | ⭐ the ruling | source |
|---|---|---|
| **R-01** | ⭐⭐⭐ **`Variable` ≡ `WorkingState`. Two names, ONE concept.** Identical `(Role=State, Scope=Asset)`; only `Dispatch` differs, and the tag carries no information `Dispatch` did not already carry | `Variable_Model_Unification.md` §2 |
| **R-02** | ⭐⭐ **User's own words:** *"as the global vars and working state vars are the same stuff, it makes no sense to emit them differently"* ⇒ `Q32-E`: **UNIFY** | `Architect_Question_32_…_ANSWERS.md` |
| **R-03** | ⛔⛔ **The unification is INFRASTRUCTURE, not a UI question.** ⭐⭐ **The LIVE stage order is `0 → C → A → B → B′ → D1 → D2 → D3 → D4`** *(§4)*. ⛔⛔ **The `A→B→C→D` table BELOW THAT LINE IS SUPERSEDED — I quoted it in `Q39` and was wrong.** ⛔ **Which stages are DONE is a MEASUREMENT — see §M**, ⚠ **not a fact this row may state** | `Variable_Model_Unification.md` §4 |
| **R-05** | ⚠⚠ **CORRECTED.** ⛔ **Stage `B` is NOT "Variables becomes a schema source"** *(that is `A`, done)*. ⭐⭐ **LIVE `B` = "Details hosts the table; My Blueprint routes selection into it" — i.e. `U-6` / `Q32` batch 57.** ⭐ The parallel `BlueprintMyBlueprintModel` path is what it removes | `Variable_Model_Unification.md` §4 |
| **R-06** | ⭐ **`Role` IS cross-host** — `BlackboardVariableRole { Input, State }` already ships and is the unified model. ⇒ **BTree/HSM working state ≡ blueprint working state** | `Variable_Model_Unification.md` |
| ⚠ **R-07** | ⚠⚠ **UNRECONCILED — `DESIGN_Parameter_Model.md` (marked AUTHORITATIVE, `2026-08-16`) gives ONE three-valued `Scope` `{Node,Behavior,Entity}`, contradicting this.** ⛔ **Reconcile before acting.** As ruled by `Q-b` *(`2026-08-13`)*: **`Scope` is NOT cross-host** — blueprint `{Asset, Graph}` = **visibility**; AI `{Node, Behavior, Entity}` = **blackboard slot sharing**. `Q-b`: *"No. `Asset` and `Graph`, and stop there"* | `Variable_Model_Unification.md` |
| **R-08** | ⚠ **`Inputs`/`Parameter` IS genuinely different** — `ParameterDecl` is a different shape, written once at behavior assignment, and the IR union has **no `Parameters` arm** | `BlueprintDeclaration.cs`, `VariableRef.cs` |
| **R-09** | ⚠⚠ **Stage `D` hazards:** synthesized fields (`__phase`, `__waitUntilTime`) are `(State, Asset)` but **never declared** ⇒ **they surface in the authoring UI without a marker**; **shared state** has **61 refs / 8 assets** declared nowhere | `Variable_Model_Unification.md` |

## 1a. ⛔⛔ SEQUENCING & STANDING CONSTRAINTS — **the section I did not read**

| id | ⭐ the ruling | source |
|---|---|---|
| ⭐⭐⭐ **R-21** | ⛔⛔ **NO VISUAL CHECKS until the Details panel is implemented AND the emitters and all access infrastructure are unified.** ⭐ **`VISUAL_CHECK_Guide.md` is SUSPENDED, not cancelled** *(user, `2026-08-14`)*. ⚠⚠ **I ran one anyway on `2026-08-17` — the user re-derived this ruling unaided** | `Q32_…_ANSWERS.md` |
| ⭐⭐⭐ **R-22** | ⭐⭐ **`Q32` §4 IS THE MASTER SEQUENCING TABLE (56→61).** ⛔ **A finding with a planned batch is NOT a new finding**, and ⛔ **the ORDER is not mine to rearrange.** ⚠ **Which rows are done is a MEASUREMENT — see §M** | `Q32_…_ANSWERS.md` |
| ⭐⭐⭐ **R-23** | ⛔⛔ **Stage `D` is FOUR stages `D1`–`D4`, not one.** ⭐ **Only `D1` reverts cheaply**; once `D2` writes v2 files the reverted reader cannot load them ⇒ **the DOWN-MIGRATOR is the revert** | `Variable_Model_Unification.md` |
| 🔴🔴 **R-24** | ⛔⛔ **Field order must be preserved within each group — or every LIVE blueprint state slot is HARD-RESET.** 📐 **Measured `2026-08-18`, `BlueprintTickSystem.cs:92`:** every tick compares `slot.StructureHash` to the compiled `def.StructureHash`; on mismatch it calls **`ResetSlot` + `InitDefault`** ⇒ ⭐ **accumulated runtime state on that entity is discarded and replaced by declared defaults.** ⭐ **NOT silent** — `_logSink.OnHardReset(blueprintId, entity, oldHash, newHash)`. ⚠ *"Deployed"* means **live entity state in a running or loaded world**, ⛔ not a release artefact | `BlueprintTickSystem.cs:92` |
| ⭐⭐⭐ **R-61** | ⭐⭐ **Ruling 8's emitter unification is REAL: the state tier is ONE projection** — `IrAsset.StateDeclarations` = `WorkingState ∪ Variables`, in `DeclarationList.KindOrder`, which is also `StructureHashComputation`'s append order. ⛔ **Do NOT say the emitters emit them separately.** ⚠ **How much of stage `D` remains is a MEASUREMENT — see §M** | `IrAsset.cs:90` · `BP-244` |
| ⭐⭐⭐ **R-75** | ⚠⚠ **CORRECTED `2026-08-18` — restart survival is BY TRANSLATION, not "by construction".** ⭐ A watch's entity is a **`NetworkIdentity.Value`, never an `Entity` handle** *(a slot/generation handle, recycled)*. 📐 **Planning entities DO carry one — but `StagingEntityExtractor` Pass 1 ALLOCATES A NEW ID on every load** *("pre-allocate new network IDs… Records the old-to-new mapping")* ⇒ ⛔⛔ **a runtime-keyed watch breaks on EVERY scenario restart.** ⭐⭐ **Key on the STAGING id** — ⚠ **which needs `oldToNewMap` PUBLISHED; today it is a local that dies in the extractor** | `StagingEntityExtractor.cs:204` · `Q40` §9e-2 |
| ⭐⭐⭐ **R-79** | ⭐⭐ **`EditorSubsystem` and `CgfSubsystem` are SEPARATELY DEPLOYABLE** — `ClusterRunner` picks them per node from `config.RequestedSubsystems`, each with an isolated network factory. ⛔ **Never assume co-hosting.** ⇒ ⭐⭐⭐ **cross-subsystem sharing goes over the ORCHESTRATION BUS** *(the channel `EditorApplication:78` already reads)*, ⛔ **not a shared in-process object.** ⭐ **Corollary for the staging id map: DO NOT move or copy the remap CODE** *(ruling 9, on the most safety-critical mapping in the system)* — ⭐ **publish its OUTPUT** | `ClusterRunner/Program.cs:212` |
| ⭐⭐ **R-78** | ⛔⛔ **THERE IS NO ENTITY-LESS RUNTIME VARIABLE** *(user, `2026-08-18`, correcting me)*. 📐 **Even shared state is entity-bound** — `BlueprintSharedState` is *"an **ENTITY-scoped** shared working-state slot"*, taking `self`; *"shared"* = **across blueprints on ONE entity**, ⛔ not across entities. ⚠ **I conflated `Scope=Asset` (VISIBILITY — my own `R-07`) with STORAGE.** ⭐ **The only entity-less value is `DefaultValueJson` — constant, pointless to watch** | `BlueprintSharedState.cs:12` |
| ⭐⭐ **R-76** | ⭐⭐⭐ **TWO CLOCKS — do not conflate.** ⭐ **VALUE clock = every brain tick** *(all rows)*; ⭐⭐ **BINDING clock = selection change ONLY** *(the "chameleon" row that follows `EditorSelectionStore.SelectedEntity`)*. ⛔ **Re-resolving the binding per tick churns the row's identity under the cursor.** ⛔ **One row per entity is REJECTED — thousands of entities** | `Q40` §9b · user `2026-08-18` |
| ⛔⛔⛔ **R-74** | ⭐⭐⭐ **INVENTORY BEFORE DESIGN — and the codebase-memory graph is MANDATORY for it** *(user, `2026-08-18`)*. ⛔ **`grep` answers *"does X exist?"*; only the graph answers *"what are ALL the X?"*** ⇒ ⚠ **three designs written against a partial inventory: `R-11` (three variable surfaces, not one), `R-72` (two watch windows — then FOUR).** ⭐ **Every architect question carries an `INVENTORY` block: the `search_graph` call, its `total`, the list.** ⭐ **Gated by `design-digest.py --check`** | `.claude/CLAUDE.md` |
| ⭐⭐ **R-71** | ⭐⭐⭐ **`VariableRow.AssetTick` IS THE HOST-NEUTRAL TICK SEAM — cut open ON PURPOSE in Batch 68.** 📌 *"Batch 68 cut the seam and left it open on purpose… (BTree, HSM and blueprint rows). ⛔ Teaching it about `BlueprintAssetTick` would make the [shared layer] blueprint-specific."* ⇒ ⛔ **do NOT put a per-tick poll in `BlueprintDebugSession`** — ⭐ drive it per row | `BlueprintAssetTickSource.cs:12` |
| ⭐⭐⭐ **R-69** | ⭐⭐ **THE "IS THE SIM UP" SIGNAL IS THE CLUSTER STATE** *(user, `2026-08-18`)*. 📐 `EditorApplication:46` already holds `_currentClusterState`, fed by `ClusterStateUpdateEvent` on the orchestration bus. ⭐ **`Idle` / `*Edit` ⇒ `Planning` · `OperatingPreview`/`OperatingLive` ⇒ `Running` · `OperatingReplay` ⇒ `Replay`** ⇒ ⭐⭐ **it resolves `R-66` AND the `Replay` arm `RunStateSource` says it cannot resolve** | `ClusterState.cs` · `EditorApplication.cs:46` |
| 🔴🔴 **R-63** | ⛔⛔ **A DIRECT WRITE TO `ActiveView` WHILE PAUSED IS LOST ON RESUME.** 📐 **Measured `2026-08-18`:** `OnHit` captures `_postTickSnapshot ← _liveRepo` then rewinds `_liveRepo ← _preTickSnapshot`; `ActiveView` **is** `_preTickSnapshot` while paused; ⭐ **`RequestStep`/`RequestContinue` restore `_liveRepo ← _postTickSnapshot` and THEN drain** *(`:495` `:514` `:498` `:517`)*. ⇒ ⛔ **ruling 15's *"the command buffer may be UNNECESSARY — write directly to the view"* is MEASURED FALSE.** ⭐ **The ECB staging path is REQUIRED**, and the drain-after-restore ordering is exactly why it works | `DataBreakpointManager.cs:495` |
| ⚠ **R-65** | ⛔ **`Blackboard1024` is ONE component SHARED by BTree, HSM and Blueprint at disjoint offsets** ⇒ a whole-component write **clobbers other subsystems' state**. ⚠⚠ **The size argument I used twice — *"exceeds `MaxComponentSize`"* — is FALSE** *(`1024 > 1024` is false; it fits exactly)*. ⭐ **Cite the sharing, never the size** | `Q32_…_ANSWERS.md` |
| ⭐⭐ **R-62** | ⭐⭐⭐ **`R-21`'s SUSPENSION CONDITION IS NOW MET FOR BLUEPRINT** — *"Details panel implemented"* ✅ *(Batch 82)* **AND** *"emitters and all access infrastructure unified"* ✅ *(Batch 56 = ruling 8, stage `C` = access path)*. ⛔ **NOT met for BTree/HSM** — `R-60`: they have no Details window at all | `Q32_…_ANSWERS.md` · `BP-244` |
| **R-26** | ⛔ **IMPLEMENTATION FREEZE — ONE session builds for ALL hosts.** Others may design; ⛔ **not code** | `Q32_…_ANSWERS.md` |
| **R-27** | ⛔ **`Q38` must NOT be built until Track C is wired AND visually checked**; it absorbs `BP-128` | `Architect_Question_38_One_Details_Panel.md` |
| **R-28** | ⭐ **`Q34` is RESOLVED, its BUILD deferred** — ⛔ **reopening the build must NOT reopen the decision** | `Architect_Question_34_…md` |
| **R-29** | ⭐ **`Q37` is PARKED with measurements BANKED — do NOT re-measure.** Reopen **before** `E3`/`E5` | `Architect_Question_37_…md` |
| **R-30** | ⛔ **`W6` is DROPPED — do not implement.** ⚠ **`W12` is unbudgeted** — no start without a scope pass | `PLAN_Cross_Host_Sequencing.md` |

## 1b. ⭐⭐ THE PARAMETER MODEL — *(swept `2026-08-17`, previously unindexed)*

| id | ⭐ the ruling | source |
|---|---|---|
| **R-31** | ⭐⭐ **Params belong to the OCCURRENCE, not the entity** — N concurrent occurrences need N regions, keyed by occurrence | `DESIGN_Parameter_Model.md` |
| **R-32** | ⭐⭐ **ONE params struct per BEHAVIOUR** — the 100 bytes are **not** carved per action; per-action scratch belongs in the state area | `DESIGN_Parameter_Model.md` |
| **R-33** | ⛔ **There is NO "Param" role.** The enum is exactly `{Input, State}` | `DESIGN_Parameter_Model.md` |
| **R-34** | ⭐ **A blueprint has params ONLY when `Dispatch == AiPrimitive`** | `DESIGN_Parameter_Model.md` |
| **R-35** | ⭐⭐ **Membership rule: a declaration is in the variable model IFF it has a byte offset in a struct THIS ASSET emits.** ⇒ puts `Graph.Inputs` and shared state OUT, synthesized fields IN | `Variable_Model_Unification.md` |
| **R-36** | ⭐ **Instance slot layout is `[Cursor 16][Params N][State M]`** — ⛔ **params must NOT be at offset 0** | `DESIGN_Parameter_Model.md` |
| **R-37** | ⭐⭐ **No LIVE parameter binding** — resolvers fill params **once** at activation/state entry. `E7a` adds a host **context** *(name-keyed, never a raw offset, fails closed)*, ⛔ **not a second supply mechanism** | `Q33` · `DESIGN_Parameter_Model.md` |
| **R-38** | ⭐ **Shared state is a DELIBERATE EXCLUSION — declared in a DIFFERENT document** *(the manifest owns the slot)*. The blueprint is a **consumer**, read-only | `Variable_Model_Unification.md` |
| ⭐⭐ **R-80** | ⛔⛔ **"STATIC PARAMETERS" IS NOT A PARAMETER EDITOR, AND THE INSTRUCTION TO RETIRE IT IS WITHDRAWN** *(`BP-295`, `2026-08-17`)*. ⭐ Measured, it is **the default-value editor for the variable the selected node WRITES** *(its `ExpressionTargetField`)*, and **the only LIVE surface for a bound variable's `DefaultValueJson`**. ⭐ Batch 74 relabelled it `DEFAULT VALUE — {var}`; ⚠ **the old name survives only in comments, a tooltip and this row.** ⛔ **Retiring it deletes a capability, not a duplicate** — the duplicate CODE half went in Batch 68 | `DESIGN_Variable_Details_And_Editing.md` |
| ⭐⭐ **R-81** | ⭐⭐⭐ **PARAMETER SUPPLY ORDER: bake the authored defaults, THEN overlay the incoming JSON keyed by VARIABLE NAME — runtime wins.** ⭐ **Applied ONCE at assignment/attach, never per tick** *(that is the whole meaning of "static")*. ⭐ **True on EVERY path since Batch 70/74** — curated and generated, BTree and HSM. ⚠⚠ **§3.2's `2026-08-16` CORRECTION saying the generated path discards the JSON went FALSE at `BP-275`/`BP-292` and is now folded as HISTORY** — ⛔ **do not quote it** | `DESIGN_Parameter_Model.md` §3.2 |

## 1c. ⭐⭐⭐ HARD LIMITS & PERSISTENCE PROMISES — **break these and it fails silently**

| id | ⭐ the constraint | source |
|---|---|---|
| 🔴 **R-39** | ⚠⚠ **UNRECONCILED: `BrainBlackboard` param region is documented as 60 bytes and ENFORCED as 100 by analyzer `FDP_001`.** ⛔ **Do NOT size a param DTO from memory — reconcile first** | `AI_DEV_GUIDE.md` vs `Fdp.Toolkits.Analyzers.md` |
| **R-40** | ⛔ **Blueprint field-layout bases are FIXED:** `Parameters` @ 0, `WorkingState` @ 8, `Variables` @ 16 | `Hrot.Blueprints.Core.md` |
| **R-41** | ⛔ **Bytes 126/127 of every `BrainBlackboard` are reserved interrupt registers** | `AI_DEV_GUIDE.md` |
| **R-42** | ⛔ **Behavior integer IDs are PERMANENT** — they appear in replays and saved scenarios. **Deprecate, never recycle** | `AI-Behavior-Authoring.md` |
| **R-43** | ⛔ **Pins are NEVER serialized** — `"Pins": []` is a persistence invariant; they are rebuilt from schema + link GUIDs | `Hrot.Blueprints.Editor.md` |
| **R-44** | ⛔ **`MAX_COMPONENT_TYPES = 256`**, globally unique, partitioned — required for multi-process determinism | `Fdp.Core.md` |
| **R-45** | ⚠ **`0xFFFF` is the reserved HSM sentinel** across ParentIndex / ActiveLeafIds / History / Timer / InitialChild | `Fhsm.Kernel.md` |
| **R-46** | ⚠ **BTree `Parallel` hard-caps at 16 children** *(32-bit status bitfield; silently truncates)*; HSM state depth **16**, BTree static depth **8** | `Fbt.Kernel.md` · `Fhsm.Kernel.md` |
| **R-47** | ⛔ **`NodeEditor.Core` must stay ImGui-free**; all rendering lives in `NodeEditor.UI`. ⭐ **Model is READ-ONLY — every mutation goes through the command sink**, or undo breaks | `NodeEditor.Core.md` · `Blueprint-Scripting-System.md` |
| **R-48** | ⛔ **NodeEdit / FastBTree / FastHSM have NO stable ABI** — vendored as source, co-evolved, CLR-identical structs | `SOLUTION-OVERVIEW.md` |
| ⭐⭐ **R-49** | ⛔⛔ **GENERATE THE DATA; HAND-WRITE ONE GENERIC ACCESSOR; NEVER GENERATE PER-VARIABLE CODE.** ⚠ **There is none today — the first one must not be introduced** | `DESIGN_Variable_Details_And_Live_Values.md` |
| **R-50** | ⛔ **Emitted behavior source is MACHINE-OWNED** — regenerated whole on save. ⚠ **Deleting the emitted `Layout()` destroys all canvas arrangement** | `Hrot.BTree.Editor.md` · `Hrot.Hsm.Editor.md` |
| **R-51** | ⭐ **Diagnostic codes (`BP####`) are stable API** — compare on `Code`, never message text | `Hrot.Blueprints.Core.md` |

## 1d. ⭐ SUPERSESSIONS — **newer overrules older**

| ⛔ old | ✅ new |
|---|---|
| `Q25-D3` — a macro has exactly ONE exec-in | ⭐ **`Q26-A3`: N exec-ins, Unreal parity** *(`2026-08-11`)* |
| `Q27-A1` — locals are C# stack locals + a refusal rail | ⭐ **`A3`: blackboard-allocated, reset in the ENTRY block. ⛔ Build NO refusal** *(`2026-08-13`)* |
| `Q32` ruling 7 — running ⇒ writes the live blackboard | ⭐ **narrowed: writes ONLY while paused or deterministic-stepping**; ⛔ none during replay |
| `Q36-A` marked OPEN in its own file | ⭐ **DECIDED — `Q36-A = B`, the host ticks the child inline** *(stated as fact in `Q37`)* |
| `Q33` — "latent requires Instance dispatch" · "a latent condition is a compile error" | ⛔ **both FALSE** — AiPrimitives suspend via `__phase`; a latent condition compiles and **silently reads false** |
| `Q12-C`'s architect answer | ⚠ **superseded by the user** — check before relying on it |
| ⚠ **stale STATUS docs** | ⛔ **`Blueprint_Editor_Issue_List.md` is SUPERSEDED — do not use for status.** ⚠ `RESUME_START_HERE.md` and `CHECKLIST_…`'s headline lag `PLAN` rev 26 |

## 1e. 🔴🔴 FORGOTTEN & ROTTED — **found by the supersession sweep, `2026-08-17`**

| id | ⚠ | source |
|---|---|---|
| 🔴🔴 **R-52** | ⛔⛔ **A LIVE DATA-CORRUPTION DEFECT ON NO WORK LIST:** the staged write takes a **whole component** and writes it with `SetComponentRaw` (no offset) ⇒ ⭐⭐ **editing ONE blueprint variable REVERTS A TICK of BTree and HSM state** on the shared `Blackboard1024`. ⚠⚠ **Batches 79/80 just made that editing reachable.** ⭐ Needs `SetComponentFieldRaw` | `PLAN…:1398` · `DESIGN_Variable_Details_And_Editing.md:353` |
| ⚠ **R-53** | ⛔ **"Do NOT ship `WorkingState` LISTS"** before the `AttachSlotsToMemory`/`InlineActionLowering` fix — **garbage-`Count` OOB hazard.** ⚠ **Working State is now a first-class authoring section with its own `[+]`** | `Blueprint_Fixed_Collections_TASK_TRACKER.md:190` |
| ⚠ **R-54** | ⭐ **`U-16` is gated: retire `BlueprintVariablesWindow` ONLY AFTER Details is proven** — *"or there is no editing surface at all."* ⛔ **Nothing is scheduled to satisfy ruling 9** | `Q32_…_ANSWERS.md:503` |
| ⚠ **R-55** | ⭐ **`Q32` ruling 12's GATE was never carried into any acceptance list:** *"with the sim frozen on a breakpoint, a value change is visible in BOTH panels within one frame."* ⛔ **Measure it, do not assume** | `Q32_…_ANSWERS.md:95` |
| ⚠ **R-56** | ⛔ **`Q34` freeze is LIVE:** Track C row identity gains a 4th component — ⭐ **"do not build for it until this lands"**, and row identity is being actively extended | `Architect_Question_34_…:75` |
| ⭐⭐ **R-59** | ⛔⛔ **`U-6`'s router does NOT merge globals with working state** — one list per SECTION. 📌 **The merge is stage `D`** *("the only risky stage", its own batch + JSON migration)* ⇒ ⭐ **merging in the UI would do `D`'s job and be undone.** Routing per section **collapses by construction** the day the sections do | `Q39:49` · `Q39-C` · Batch 82 report §2 |


## ⛔⛔⛔ §M — MEASURE, DON'T MEMORISE *(added `2026-08-18`, user ruling)*

> ⭐⭐⭐ **NOTHING IN THIS FILE MAY ASSERT WHAT THE CODE CURRENTLY IS.**
>
> 📌 **Why this section exists.** `rulings-check.py` verifies that **a quote still exists in a
> document**. ⛔⛔ **It CANNOT detect that a claim about CODE became false — because the document did
> not change, the CODE did.** ⚠⚠ **Twice on `2026-08-18` a row was GREEN AND FALSE:** `R-04`
> *("the tagged type is the VIEW")* and `R-25` *("`B′` is blocked")*. ⭐ **Both sent me to build things
> that already existed.**
>
> ⇒ ⭐⭐ **A perishable claim is not canon. It is a QUESTION plus the command that answers it.**
> ⛔ **Never quote an answer from here. Run the command.**

| # | the question | ⭐ run this | last measured |
|---|---|---|---|
| **M-1** | Is the tagged declaration list the STORAGE, or a view? *(stage `D1`)* | `grep -n "DeclarationView\|List<" Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Assets/BlueprintAsset.cs` — ⭐ `DeclarationView<T>` ⇒ it IS the storage | `2026-08-18` |
| **M-2** | Do persisted assets carry ONE tagged array? *(stage `D2`)* | `python3 -c "import json;print(list(json.load(open('<any>.bp.json')))[:12])"` — ⭐ look for `Declarations` + `*Order` | `2026-08-18` |
| **M-3** | What still treats `WorkingState` as distinct from `Variable`? | `grep -rn "DeclarationKind.WorkingState" --include=*.cs Hrot/ \| grep -v Tests` | ✅ **`2026-08-18`, Batch 86: NOTHING DOES.** `DeclarationKind` is `{ Parameter, Variable }`; `WorkingState` survives only as a **readable on-disk tag** mapping to `Variable` |
| **M-12** | Do any shipped assets declare BOTH `WorkingState` and `Variable`? *(decides whether a kind collapse can move any offset)* | ⚠ **glob SOURCE only — `'/obj/' not in f and '/bin/' not in f`** | census all `*.bp.json` by the `Kind` values present — ⭐ Batch 56 measured **0 of 458**  ✅ **`2026-08-18`: ZERO of 100 SOURCE assets declare both** *(30 `Variable` · 9 `Parameter+WorkingState` · 9 `Parameter` · 7 `WorkingState` · 45 none)* ⇒ ⭐⭐ **the merge is order-neutral.** ⛔⛔ **My first run said 541 — it counted 441 `obj/`/`bin/` BUILD COPIES.** ⭐ **16 source assets carry `WorkingState`; 43 assets actually compile** |
| **M-4** | Which perspectives have a Details window? | `search_graph(label="Class", name_pattern=".*Details.*(Window\|Panel).*")` | `2026-08-18` |
| **M-5** | How many watch surfaces exist, and which is shared? | `search_graph(label="Class", name_pattern=".*Watch.*(Window\|Panel\|Source\|Store).*")` | `2026-08-18` |
| **M-6** | Are the BTree/HSM debug sessions CONSTRUCTED in production? | `grep -rn "new HsmDebugSession\|new BTreeDebugSession" --include=*.cs \| grep -v Tests` | `2026-08-18` |
| **M-7** | Can anything ADD a watch? | `grep -rn "CommandCatalog.ToggleWatch" --include=*.cs` + check `CanvasRenderer` for `BeginDisabled` | `2026-08-18` |
| **M-8** | Does run state mean *"the sim is up"* or *"a document is open"*? | read `RunStateSource.Resolve` **and** what feeds it at the composition root | ⭐ **`2026-08-18`, re-measured after Batch 84: FIXED.** `Resolve(isSimUp, isFrozen)` no longer reads `ActiveSession`; fed by `IPreviewController.IsInPreviewMode` + `_bpManager.IsPaused`. ⚠ **`IsInPreviewMode` = `LoadingPreview or OperatingPreview` ONLY — and that is CORRECT**, because `ScenarioEditorState` has no Live/Replay member. ⛔ **`R-69`'s `OperatingLive`/`OperatingReplay` describe the CLUSTER enum the editor never sees through this adapter** |
| **M-9** | How many implementations of `FindEntityByNetworkId`? | `grep -rn "FindEntityByNetworkId" --include=*.cs` | `2026-08-18` |
| **M-10** | Is `oldToNewMap` published anywhere? | `grep -n "oldToNewMap" Hrot/Subsystems/Hrot.CGF/Orchestration/StagingEntityExtractor.cs` | `2026-08-18` |
| **M-11** | Does a surface DRAW the variable edit session *(the OK button)*? | `grep -rn "IEditSession" --include=*.cs \| grep -i "draw\|modal"` | `2026-08-18` |

⭐⭐ **A measurement older than ~14 days is a rumour.** ⭐ `rulings-check.py` warns; ⛔ **it cannot fail,
because only you know whether you are relying on it.**

## 2. ⭐⭐ SURFACES AND DUPLICATION

| id | ⭐ the ruling | source |
|---|---|---|
| **R-10** | ⭐⭐⭐ **Ruling 9 — the standing constraint over everything:** *"no keeping two implementations for the same concept."* ⭐ **`U-16` is not optional cleanup; it is the acceptance criterion** | `Architect_Question_32_…_ANSWERS.md` |
| **R-13** | ⛔ **"No rush removals"** — say which it is: **duplicate CODE** *(route)* · **duplicate SURFACE** *(usually keep)* · **genuinely dead** *(design record agrees)* | `.claude/CLAUDE.md` |

## 3. ⭐ AUTHORING UI BEHAVIOUR

| id | ⭐ the ruling | source |
|---|---|---|
| **R-14** | ⭐⭐ **A variable's classification is WHERE IT WAS CREATED.** ⛔ **NO `Role`/`Scope` dropdown anywhere — the SECTION is the control** | `DESIGN_Variable_Details_And_Editing.md` §1c |
| **R-15** | ⭐ **An empty section STAYS PRESENT** — *"a section that appears and disappears reads as a broken feature"* | `BlueprintMyBlueprintModel.cs` |
| **R-16** | ⭐ **`Q26-B2`: a refusable `[+]` STAYS and refuses out loud, naming the reason** — ⛔ it does not vanish. ⭐⭐ **`2026-08-17` user refinement: GREY it with a tooltip — greying is not vanishing, and it removes the false expectation** | `BlueprintMyBlueprintModel.cs` + user |
| **R-17** | ⭐ **Every section's `[+]` opens the SAME dialog** *(user, `2026-08-17`)*. ✅ **BUILT in Batch 81 and KEPT.** ⚠⚠ **`Q39` said "PULL IT"; the implementation session MEASURED both premises FALSE and said so instead of complying** — 📐 it is **ONE modal CLASS** per section *(not a dialog per section)*, and it **removed** a parallel create path ⇒ **two create implementations became ONE**, which SERVES ruling 9 and makes stage `D` cheaper. ⭐⭐ **`Q39` §5's pull is WITHDRAWN** | user · `REPORT_Batch81` §1a |
| **R-18** | ⭐ **Rename lives in the OUTLINE, not the table row menu** — a row is an observation with no asset handle | `Q32` / plan §4C |
| **R-19** | ⭐ **Details is authoring+runtime; Watch is runtime-only.** ⛔ **Do NOT "fix" that into consistency** — ruling 9 forbids two implementations of one concept, not two behaviours of two concepts | `Architect_Question_32_…_ANSWERS.md` |
| **R-20** | ⭐ **Run state governs WRITABILITY, not WHICH surface is shown** | `DESIGN_Variable_Details_And_Editing.md` §5 |

## 4. ⭐⭐ WHERE TO LOOK when there is no row

> ⭐⭐⭐ **USER CORRECTION, `2026-08-17`, verbatim:** *"most designs are in the **docs** folder. in the
> `.dev` those named like 'design' or 'detailed design' describe **what was implemented**."*
> ⇒ ⛔⛔ **`.dev/` is AS-BUILT, not INTENT.** ⚠ **I previously listed `.dev/*-DESIGN.md` as an intent
> source — that was WRONG**, and it is the same error as reading code: it tells you *how it is*.

| # | look | it tells you |
|---|---|---|
| ① | ⭐⭐⭐ **`docs/**` — `Architect_Question_*_ANSWERS.md`** | ⭐ **THE RULINGS.** ⛔ the non-`ANSWERS` files carry only options |
| ② | ⭐⭐ **their §"Sequencing" tables** | ⛔ **a finding with a planned batch is NOT a new finding** |
| ③ | ⭐⭐ **`docs/` — `DESIGN_*.md`, `*_Unification.md`, `BOOTSTRAP_*.md`, `PLAN_*.md`** | ⭐ **THE INTENT — the model as it is MEANT to be** |
| ④ | ⚠ **`.dev/<programme>/*-DESIGN.md`, `*_Detailed_Design.md`** | ⛔⛔ **AS-BUILT — what WAS IMPLEMENTED.** ⭐ Useful for *"why is it like this"*, ⛔ **never for *"what should it be"*** |
| ⑤ | `.dev/**/reports/*-REPORT.md` tails · `TASK-DETAIL.md` | **the DEBT** *(`DEBT-*` ids are filed here and nowhere else)* · the authorising user decision |
| ⛔ | `batches/*-INSTRUCTIONS.md`, `reviews/*` | **least useful — they restate the design** |

⚠⚠ **The trap this correction closes:** ⛔ **an as-built document AGREES WITH THE CODE by
construction.** ⭐ **Citing one to justify a design position proves nothing** — it is code-reasoning
wearing a design document's name.

## 5. ⛔ MY OWN CORRECTIONS — **do not repeat these**

| ⛔ what I claimed | ✅ the truth |
|---|---|
| *"Working State `[+]` opening no dialog is not a defect — it is deliberate"* | ⛔ **overruled.** Its premise *("renamable in place")* was false, and consistency outranks the saving |
| *"the BTree/HSM `Working State` name is a COINCIDENCE"* | ⛔ **wrong. `Role` is genuinely shared** — only `Scope` differs |
| *"`Q39` is: should the outline merge two sections?"* | ⛔ **wrong framing** — it is **infrastructure**, stages `B`+`D` |
| ⭐⭐⭐ *"pull Batch 81 §3b — it hardens the split"* | ⛔⛔ **WRONG, and it is the MIRROR of my usual error.** ⚠ I had just spent a day learning *"do not reason from code without the design"* — ⭐⭐ **and then reasoned from the design without measuring the code.** 📐 **Both premises were false.** ⇒ ⭐ **A design-based objection to an IMPLEMENTATION must be measured too** |
| *"rename the three `Variables` windows"* | ⚠ **incomplete** — the design says **retire** *(`U-16`)*; the rename is an **interim** the user authorised |
| *"`E3` is a signature widening" / "the dangerous case" / "`E5`'s dependency is stale"* | ⛔ **wrong 4×** — the **params BASE** is what collides |

---

<!-- MACHINE-CHECKABLE PROBES — id | file | verbatim substring that MUST exist in that file -->
```probes
R-52 | docs/blueprints/DESIGN_Variable_Details_And_Editing.md | prerequisite, either way
R-59 | docs/blueprints/Architect_Question_39_Merge_Variables_And_Working_State.md | the only risky stage
R-61 | Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Ir/IrAsset.cs | public IReadOnlyList<IrField> StateDeclarations
R-63 | Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs | _liveRepo.SyncFrom(_postTickSnapshot);
R-69 | Hrot/Subsystems/Hrot.Editor/EditorApplication.cs | _currentClusterState = ev.CurrentState;
R-74 | .claude/CLAUDE.md | INVENTORY BEFORE DESIGN
R-75 | Hrot/Subsystems/Hrot.CGF/Orchestration/StagingEntityExtractor.cs | pre-allocate new network IDs for every entity
R-78 | FDP/Toolkits/Fdp.Toolkits/Blueprints/Partitioning/BlueprintSharedState.cs | ENTITY-scoped shared working-state slot
R-79 | Hrot/Runner/Hrot.ClusterRunner/Program.cs | config.RequestedSubsystems
R-76 | docs/blueprints/Architect_Question_40_Watch_Variable_Pinning.md | BINDING clock
R-71 | Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Variables/BlueprintAssetTickSource.cs | left it open on purpose
R-65 | docs/blueprints/Architect_Question_32_Variable_Details_And_Values_ANSWERS.md | It fits, exactly.
R-62 | docs/blueprints/Architect_Question_32_Variable_Details_And_Values_ANSWERS.md | AND A SEQUENCING RULING: NO VISUAL CHECKS
R-03 | docs/blueprints/Variable_Model_Unification.md | order below the line is SUPERSEDED
R-05 | docs/blueprints/Variable_Model_Unification.md | Details hosts the table; My Blueprint routes selection into it
R-21 | docs/blueprints/Architect_Question_32_Variable_Details_And_Values_ANSWERS.md | AND A SEQUENCING RULING: NO VISUAL CHECKS
R-22 | docs/blueprints/Architect_Question_32_Variable_Details_And_Values_ANSWERS.md | the emitter + access-path unification
R-23 | docs/blueprints/Variable_Model_Unification.md | consumers moved off the old views, in dependency order
R-24 | docs/blueprints/Variable_Model_Unification.md | If it does not, every deployed blackboard is wiped.
R-31 | docs/blueprints/DESIGN_Parameter_Model.md | concurrent occurrences need N regions, keyed by occurrence
R-33 | docs/blueprints/DESIGN_Parameter_Model.md | There is NO "Param" role.
R-34 | docs/blueprints/DESIGN_Parameter_Model.md | A blueprint has params only when
R-35 | docs/blueprints/Variable_Model_Unification.md | iff it has a byte offset in a struct THIS ASSET emits
R-36 | docs/blueprints/DESIGN_Parameter_Model.md | params must NOT be at 0
R-38 | docs/blueprints/Variable_Model_Unification.md | it is declared in a DIFFERENT DOCUMENT
R-39 | docs/projects/FDP/Toolkits/Fdp.Toolkits.Analyzers.md | DTO size <= 100 bytes in BrainBlackboard
R-40 | docs/projects/Hrot/Blueprints/Hrot.Blueprints.Core.md | at offset 0
R-42 | docs/projects/relationships/AI-Behavior-Authoring.md | Behavior integer IDs are permanent
R-44 | docs/projects/FDP/Core/Fdp.Core.md | MAX_COMPONENT_TYPES = 256
R-47 | docs/projects/FDP/ExtDeps/NodeEdit/NodeEditor.Core.md | Never write through IGraphModel
R-48 | docs/projects/SOLUTION-OVERVIEW.md | no stable ABI boundary
R-49 | docs/blueprints/DESIGN_Variable_Details_And_Live_Values.md | NEVER GENERATE PER-VARIABLE CODE
R-80 | docs/blueprints/DESIGN_Variable_Details_And_Editing.md | the only LIVE surface for a bound variable's
R-81 | docs/blueprints/DESIGN_Parameter_Model.md | the overlay now ships on EVERY path
R-01 | docs/blueprints/Variable_Model_Unification.md | occupy the SAME cell
R-01b | docs/blueprints/Variable_Model_Unification.md | names, one concept
R-02 | docs/blueprints/Architect_Question_32_Variable_Details_And_Values_ANSWERS.md | it makes no sense to emit them differently
R-05 | docs/blueprints/Variable_Model_Unification.md | That is the parallel implementation to remove
R-06 | docs/blueprints/Variable_Model_Unification.md | BlackboardVariableRole
R-07 | docs/blueprints/Variable_Model_Unification.md | and stop there
R-09 | docs/blueprints/Variable_Model_Unification.md | they surface in the authoring UI
R-10 | docs/blueprints/Architect_Question_32_Variable_Details_And_Values_ANSWERS.md | no keeping two implementations for the same concept
R-10b | docs/blueprints/Architect_Question_32_Variable_Details_And_Values_ANSWERS.md | not optional cleanup; it is the acceptance criterion
R-15 | Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Windows/BlueprintMyBlueprintModel.cs | reads as a broken feature
R-16 | Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Windows/BlueprintMyBlueprintModel.cs | REFUSES OUT LOUD
R-19 | docs/blueprints/Architect_Question_32_Variable_Details_And_Values_ANSWERS.md | not two behaviours of two different concepts
```
