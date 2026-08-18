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
| **R-03** | ⛔⛔ **The unification is UNFINISHED INFRASTRUCTURE, not a UI question.** ⚠⚠ **CORRECTED `2026-08-17`: the LIVE order is `0 → C → A → B → B′ → D1 → D2 → D3 → D4`** *(§4)*. ⛔⛔ **The `A→B→C→D` table BELOW THAT LINE IS SUPERSEDED — I quoted it in `Q39` and was wrong.** ⭐ **`C` ✅ and `A` ✅ shipped; `B` onward did not** | `Variable_Model_Unification.md` §4 |
| **R-04** | ⭐⭐⭐ **`U-9` was built INVERSE of the plan — the tagged type is the VIEW, the three lists are still the STORAGE.** ⇒ **that is WHY every storage-reading surface still sees three concepts** | `BOOTSTRAP_Cross_Host_Variable_Model.md` |
| **R-05** | ⚠⚠ **CORRECTED.** ⛔ **Stage `B` is NOT "Variables becomes a schema source"** *(that is `A`, done)*. ⭐⭐ **LIVE `B` = "Details hosts the table; My Blueprint routes selection into it" — i.e. `U-6` / `Q32` batch 57.** ⭐ The parallel `BlueprintMyBlueprintModel` path is what it removes | `Variable_Model_Unification.md` §4 |
| **R-06** | ⭐ **`Role` IS cross-host** — `BlackboardVariableRole { Input, State }` already ships and is the unified model. ⇒ **BTree/HSM working state ≡ blueprint working state** | `Variable_Model_Unification.md` |
| ⚠ **R-07** | ⚠⚠ **UNRECONCILED — `DESIGN_Parameter_Model.md` (marked AUTHORITATIVE, `2026-08-16`) gives ONE three-valued `Scope` `{Node,Behavior,Entity}`, contradicting this.** ⛔ **Reconcile before acting.** As ruled by `Q-b` *(`2026-08-13`)*: **`Scope` is NOT cross-host** — blueprint `{Asset, Graph}` = **visibility**; AI `{Node, Behavior, Entity}` = **blackboard slot sharing**. `Q-b`: *"No. `Asset` and `Graph`, and stop there"* | `Variable_Model_Unification.md` |
| **R-08** | ⚠ **`Inputs`/`Parameter` IS genuinely different** — `ParameterDecl` is a different shape, written once at behavior assignment, and the IR union has **no `Parameters` arm** | `BlueprintDeclaration.cs`, `VariableRef.cs` |
| **R-09** | ⚠⚠ **Stage `D` hazards:** synthesized fields (`__phase`, `__waitUntilTime`) are `(State, Asset)` but **never declared** ⇒ **they surface in the authoring UI without a marker**; **shared state** has **61 refs / 8 assets** declared nowhere | `Variable_Model_Unification.md` |

## 1a. ⛔⛔ SEQUENCING & STANDING CONSTRAINTS — **the section I did not read**

| id | ⭐ the ruling | source |
|---|---|---|
| ⭐⭐⭐ **R-21** | ⛔⛔ **NO VISUAL CHECKS until the Details panel is implemented AND the emitters and all access infrastructure are unified.** ⭐ **`VISUAL_CHECK_Guide.md` is SUSPENDED, not cancelled** *(user, `2026-08-14`)*. ⚠⚠ **I ran one anyway on `2026-08-17` — the user re-derived this ruling unaided** | `Q32_…_ANSWERS.md` |
| ⭐⭐⭐ **R-22** | ⭐⭐ **`Q32` §4 IS THE MASTER SEQUENCING TABLE (56→61).** ⛔ **A finding with a planned batch is NOT a new finding.** Still NOT done: **`U-16`** *(60, retire the duplicate Variables windows)* · **`59b`** *(Watch populate/edit)* | `Q32_…_ANSWERS.md` |
| ⭐⭐⭐ **R-23** | ⛔⛔ **Stage `D` is FOUR stages `D1`–`D4`, not one.** ⭐ **Only `D1` reverts cheaply**; once `D2` writes v2 files the reverted reader cannot load them ⇒ **the DOWN-MIGRATOR is the revert** | `Variable_Model_Unification.md` |
| 🔴🔴 **R-24** | ⛔⛔ **`D2` MUST preserve field order within each group — or every deployed blackboard is WIPED.** 📐 order → `FieldLayout` offsets → `StructureHash` → the emitted tick wipes on mismatch | `Variable_Model_Unification.md` |
| ✅ **R-25** | ⚠⚠ **CORRECTED `2026-08-17` — `B′` is NOT blocked and is DONE.** `BP-228` closed **Batch 47** *(`U-7`)*, *"stage `B′` unblocked with it (`U-8`)"*; `S5` built the union **Batch 65** *(`BP-255`)*. ⛔ **Three of my own documents still said BLOCKED** — that is why this row exists | `Blueprint_Issues_Tracker.md` BP-228 |
| ⭐⭐⭐ **R-61** | ⛔⛔ **STAGE `D` IS THE ONLY UNIFICATION WORK LEFT.** ⭐ `0` `C` `A` `B` `B′` are **all done**. ⚠ **Ruling 8's emitter unification SHIPPED in Batch 56** — `IrAsset.StateDeclarations` = `WorkingState ∪ Variables`, walked by both struct emitters, `CSharpEmitter` and `FieldLayout` *(ONE run, one base)*. ⛔ **Do NOT say "the emitters still emit them separately"** — I did, on `2026-08-17`, and it was false | `IrAsset.cs:90` · `BP-244` |
| ⭐⭐⭐ **R-75** | ⚠⚠ **CORRECTED `2026-08-18` — restart survival is BY TRANSLATION, not "by construction".** ⭐ A watch's entity is a **`NetworkIdentity.Value`, never an `Entity` handle** *(a slot/generation handle, recycled)*. 📐 **Planning entities DO carry one — but `StagingEntityExtractor` Pass 1 ALLOCATES A NEW ID on every load** *("pre-allocate new network IDs… Records the old-to-new mapping")* ⇒ ⛔⛔ **a runtime-keyed watch breaks on EVERY scenario restart.** ⭐⭐ **Key on the STAGING id** — ⚠ **which needs `oldToNewMap` PUBLISHED; today it is a local that dies in the extractor** | `StagingEntityExtractor.cs:204` · `Q40` §9e-2 |
| ⭐⭐⭐ **R-79** | ⭐⭐ **`EditorSubsystem` and `CgfSubsystem` are SEPARATELY DEPLOYABLE** — `ClusterRunner` picks them per node from `config.RequestedSubsystems`, each with an isolated network factory. ⛔ **Never assume co-hosting.** ⇒ ⭐⭐⭐ **cross-subsystem sharing goes over the ORCHESTRATION BUS** *(the channel `EditorApplication:78` already reads)*, ⛔ **not a shared in-process object.** ⭐ **Corollary for the staging id map: DO NOT move or copy the remap CODE** *(ruling 9, on the most safety-critical mapping in the system)* — ⭐ **publish its OUTPUT** | `ClusterRunner/Program.cs:212` |
| ⭐⭐ **R-78** | ⛔⛔ **THERE IS NO ENTITY-LESS RUNTIME VARIABLE** *(user, `2026-08-18`, correcting me)*. 📐 **Even shared state is entity-bound** — `BlueprintSharedState` is *"an **ENTITY-scoped** shared working-state slot"*, taking `self`; *"shared"* = **across blueprints on ONE entity**, ⛔ not across entities. ⚠ **I conflated `Scope=Asset` (VISIBILITY — my own `R-07`) with STORAGE.** ⭐ **The only entity-less value is `DefaultValueJson` — constant, pointless to watch** | `BlueprintSharedState.cs:12` |
| ⭐⭐ **R-76** | ⭐⭐⭐ **TWO CLOCKS — do not conflate.** ⭐ **VALUE clock = every brain tick** *(all rows)*; ⭐⭐ **BINDING clock = selection change ONLY** *(the "chameleon" row that follows `EditorSelectionStore.SelectedEntity`)*. ⛔ **Re-resolving the binding per tick churns the row's identity under the cursor.** ⛔ **One row per entity is REJECTED — thousands of entities** | `Q40` §9b · user `2026-08-18` |
| ⚠ **R-77** | ⛔ **`FindEntityByNetworkId` EXISTS TWICE** — `ReplayBrowserSubsystem:933` and `EditorMissionService:54`. 📌 **ruling 9 flag, FILED not fixed.** ⚠ **And existing watch persistence is NOT entity-keyed** *(`SaveWatches` stores breakpoints marked `IsWatch`, keyed by `PropertyMatchDto`)* — ⭐ **extend that file, do not invent a second** | `ReplayBrowserSubsystem.cs:933` · `WatchPersistenceTests.cs:18` |
| ⛔⛔⛔ **R-74** | ⭐⭐⭐ **INVENTORY BEFORE DESIGN — and the codebase-memory graph is MANDATORY for it** *(user, `2026-08-18`)*. ⛔ **`grep` answers *"does X exist?"*; only the graph answers *"what are ALL the X?"*** ⇒ ⚠ **three designs written against a partial inventory: `R-11` (three variable surfaces, not one), `R-72` (two watch windows — then FOUR).** ⭐ **Every architect question carries an `INVENTORY` block: the `search_graph` call, its `total`, the list.** ⭐ **Gated by `design-digest.py --check`** | `.claude/CLAUDE.md` |
| 🔴🔴 **R-72** | ⛔⛔ **THERE ARE TWO WATCH WINDOWS.** ⭐ **`AiWatchWindow`** *(`Hrot.Editor.AiShared`, built by the SHARED registrar ⇒ all three perspectives, fed by the shared `_bpManager`, and it ALREADY holds a `PinnedVariableRowSource`)* · ⚠ **`WatchPanelWindow`** *(`Hrot.Blueprints.Editor`, blueprint-only, session-fed)*. ⚠⚠ **The user has been looking at `AiWatchWindow`** *(its empty state says "No pinned variables" / "No watch entries")*; **Batch 83's `BP-01` fix landed on `WatchPanelWindow`** — ⭐ a real fix, ⛔ **possibly not the window in front of them.** 📌 **`R-13`: duplicate SURFACE *and* CODE ⇒ ruling 9** — ⛔ **retirement belongs to row 60** | `AiWatchWindow.cs:99` · `BlueprintWindowRegistrar.cs:53` |
| ⭐⭐ **R-73** | ⭐⭐⭐ **WATCH AND BREAKPOINT CONTENT IS ALREADY SHARED ACROSS PERSPECTIVES** *(the user's `2026-08-18` requirement, already met)* — `_bpManager` is passed to all three registrars *(`:2128` `:2152` `:2164`)*, and both windows are built by the shared registrar. ⭐ **And the field READ is not session-bound**: it needs an `ISimulationView`, the shared `Blackboard1024`, a `StructureHash` guard and a layout ⇒ ⛔ **only the BASE OFFSET and the layout source are host-specific, and both are DATA** | `PerspectiveWorkspaceRegistrar.cs:330` · `BlueprintDebugSession.cs:1304` |
| 🔴🔴 **R-70** | ⛔⛔ **`HsmDebugSession` AND `BTreeDebugSession` ARE BUILT AND NEVER CONSTRUCTED** — ⭐ complete classes on `AiDebugSessionBase`, **zero production construction sites**. 📌 `EditorSubsystem:2183`: *"BTree/HSM debug sessions are not yet attached/working — intentionally null until wired."* ⇒ ⭐⭐ **THE THIRTEENTH INSTANCE**, and the whole cross-host gap: **anything that observes a running AI asset has nothing to observe** | `EditorSubsystem.cs:2183` |
| ⭐⭐ **R-71** | ⭐⭐⭐ **`VariableRow.AssetTick` IS THE HOST-NEUTRAL TICK SEAM — cut open ON PURPOSE in Batch 68.** 📌 *"Batch 68 cut the seam and left it open on purpose… (BTree, HSM and blueprint rows). ⛔ Teaching it about `BlueprintAssetTick` would make the [shared layer] blueprint-specific."* ⇒ ⛔ **do NOT put a per-tick poll in `BlueprintDebugSession`** — ⭐ drive it per row | `BlueprintAssetTickSource.cs:12` |
| ⭐⭐⭐ **R-69** | ⭐⭐ **THE "IS THE SIM UP" SIGNAL IS THE CLUSTER STATE** *(user, `2026-08-18`)*. 📐 `EditorApplication:46` already holds `_currentClusterState`, fed by `ClusterStateUpdateEvent` on the orchestration bus. ⭐ **`Idle` / `*Edit` ⇒ `Planning` · `OperatingPreview`/`OperatingLive` ⇒ `Running` · `OperatingReplay` ⇒ `Replay`** ⇒ ⭐⭐ **it resolves `R-66` AND the `Replay` arm `RunStateSource` says it cannot resolve** | `ClusterState.cs` · `EditorApplication.cs:46` |
| 🔴🔴 **R-66** | ⛔⛔ **`IDebugSessionRegistry.ActiveSession` MEANS "A BLUEPRINT DOCUMENT IS OPEN", NOT "THE SIM IS UP."** 📐 `EditorSubsystem:2180-2186` sets it from `_aiDocumentManager.Active.Kind`. ⇒ ⛔ **`RunStateSource`'s premise — *"a live session is what running means to this editor"* — is FALSE**, so the Value column's INITIAL arm is unreachable in production and every row reads `(pending)` | `EditorSubsystem.cs:2180` · visual check `2026-08-18` |
| 🔴🔴 **R-67** | ⛔⛔ **A RAIL THAT BUILDS ITS OWN COMPOSITION ROOT CANNOT SEE A COMPOSITION-ROOT DEFECT.** ⚠⚠ **Four times now** — Batch 80 *(`hostKind`)*, 82 *(named it)*, 83 *(`facetEditService`)*. 📐 **The `2026-08-18` instance: `facetEditService` is passed to the BTree `:2134` and HSM `:2158` registrars and OMITTED from the Blueprint one `:2162`** ⇒ the gestures never attach. ⭐ **The control is a rail on the PRODUCTION object, or one construction site instead of three** | `EditorSubsystem.cs:2134` · `.claude/CLAUDE.md` |
| 🔴🔴 **R-68** | ⛔⛔ **NOTHING CAN ADD A WATCH — the panel has NO reachable entry point at all.** ⚠⚠ **Worse than the coordinator first stated `2026-08-18`:** the canvas pin menu's **`"Watch this Value"` sits inside `ImGui.BeginDisabled()`** *(`CanvasRenderer.cs:684`)* — a greyed stub — and ⛔ **`CommandCatalog.ToggleWatch` has NO invoker anywhere**, so `BlueprintDebugToNodeEditAdapter.ToggleWatch` is dead. ⇒ ⭐ **row `59b` gave the panel a RENDERER and nothing to render** *(it specified: make `HandlePinValueChanged` real · edit through the same dialog · show nothing before the run — ⛔ never an entry point)*. ⭐⭐ **Unspecified, not regressed — it needs a RULING before a batch** | `CanvasRenderer.cs:684` · `Q32_…_ANSWERS.md` row 59b |
| 🔴🔴 **R-63** | ⛔⛔ **A DIRECT WRITE TO `ActiveView` WHILE PAUSED IS LOST ON RESUME.** 📐 **Measured `2026-08-18`:** `OnHit` captures `_postTickSnapshot ← _liveRepo` then rewinds `_liveRepo ← _preTickSnapshot`; `ActiveView` **is** `_preTickSnapshot` while paused; ⭐ **`RequestStep`/`RequestContinue` restore `_liveRepo ← _postTickSnapshot` and THEN drain** *(`:495` `:514` `:498` `:517`)*. ⇒ ⛔ **ruling 15's *"the command buffer may be UNNECESSARY — write directly to the view"* is MEASURED FALSE.** ⭐ **The ECB staging path is REQUIRED**, and the drain-after-restore ordering is exactly why it works | `DataBreakpointManager.cs:495` |
| ⭐⭐⭐ **R-64** | ⭐⭐ **`59c`'s `Fdp.Core` HALF IS ALREADY BUILT** — `SetComponentFieldRaw` ships end-to-end *(interface `:57` · ECB `:256` · repository `:1720` · playback `:437`)*, `DrainPendingMutations` already branches on `IsFieldWrite`, and `SurgicalFieldWriteTests` is the red-first test. ⛔⛔ **What is missing is a STAGING ENTRY POINT that sets `ByteOffset`** — `IDataBreakpointManager` exposes **whole-component `StageMutation` only** ⇒ ⭐ **`59c` is WIRING, like 82 and 83** | `PendingDebugMutation.cs:50` · `IDataBreakpointManager.cs:96` |
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
| ✅ **R-57** | ⭐ **`BP1031` is RETIRED** *(`Stage2_Validate.cs:168`, Batch 70)* — no production code raises it. ⚠ **The three docs that described it as live were REPAIRED in Batch 82** *(`BP-318`)*; the reasoning is kept as the record of why it went | `Blueprint_Issues_Tracker.md` BP-278 |
| ✅ **R-58** | ⭐⭐ **The `InspectorWindow` STATIC PARAMETERS retirement is WITHDRAWN** *(premise measured inverted — it is the only LIVE default-value surface)*. ⚠ **The stale order in `DESIGN_Variable_Details_And_Editing.md` was REMOVED in Batch 82** ⇒ ⛔ **do not re-derive the retirement from an old copy** | `HANDOFF_Batch74…:117` · BP-295 |
| ⭐⭐ **R-59** | ⛔⛔ **`U-6`'s router does NOT merge globals with working state** — one list per SECTION. 📌 **The merge is stage `D`** *("the only risky stage", its own batch + JSON migration)* ⇒ ⭐ **merging in the UI would do `D`'s job and be undone.** Routing per section **collapses by construction** the day the sections do | `Q39:49` · `Q39-C` · Batch 82 report §2 |
| 🔴 **R-60** | ⛔⛔ **Ruling 6 wants ONE Details panel across three perspectives — and TWO OF THREE HAVE NO DETAILS WINDOW AT ALL.** 📐 `BlueprintDetailsWindow` is the only one in the repo; BTree/HSM have `InspectorWindow`. ⭐ **`NodeEditor.UI.Panels.DetailsPanel` is a generic host nobody constructs** *(one demo call site)* ⇒ ⛔ **not dead — it belongs to sequencing row 61** | `BP-317` · Batch 82 report §0 |

## 2. ⭐⭐ SURFACES AND DUPLICATION

| id | ⭐ the ruling | source |
|---|---|---|
| **R-10** | ⭐⭐⭐ **Ruling 9 — the standing constraint over everything:** *"no keeping two implementations for the same concept."* ⭐ **`U-16` is not optional cleanup; it is the acceptance criterion** | `Architect_Question_32_…_ANSWERS.md` |
| **R-11** | ⚠ **Ruling 9's target is BIGGER than `U-16` assumed** — **three** variable surfaces, plus `InspectorWindow` in **two** assemblies | `Architect_Question_32_…_ANSWERS.md` |
| **R-12** | ⭐ **User `2026-08-17`: `VariablesPanelControl` KEEPS drawing for now**; the merge is `Q38`. ⇒ **duplicate SURFACE ≠ duplicate CODE** | `Architect_Question_38_One_Details_Panel.md` |
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
R-57 | docs/blueprints/Blueprint_Issues_Tracker.md | is RETIRED, and the batch that needed it had not been told
R-25 | docs/blueprints/Blueprint_Issues_Tracker.md | stage B′ unblocked with it
R-59 | docs/blueprints/Architect_Question_39_Merge_Variables_And_Working_State.md | the only risky stage
R-61 | Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Ir/IrAsset.cs | public IReadOnlyList<IrField> StateDeclarations
R-63 | Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs | _liveRepo.SyncFrom(_postTickSnapshot);
R-66 | Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs | debugRegistry.SetActiveSession(session);
R-69 | Hrot/Subsystems/Hrot.Editor/EditorApplication.cs | _currentClusterState = ev.CurrentState;
R-70 | Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs | not yet attached/working
R-74 | .claude/CLAUDE.md | INVENTORY BEFORE DESIGN
R-75 | Hrot/Subsystems/Hrot.CGF/Orchestration/StagingEntityExtractor.cs | pre-allocate new network IDs for every entity
R-78 | FDP/Toolkits/Fdp.Toolkits/Blueprints/Partitioning/BlueprintSharedState.cs | ENTITY-scoped shared working-state slot
R-79 | Hrot/Runner/Hrot.ClusterRunner/Program.cs | config.RequestedSubsystems
R-76 | docs/blueprints/Architect_Question_40_Watch_Variable_Pinning.md | BINDING clock
R-77 | Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs | private Entity FindEntityByNetworkId(long networkId)
R-72 | Hrot/Editor/Hrot.Editor.AiShared/Windows/AiWatchWindow.cs | No pinned variables. Pin one from the Variables table.
R-73 | Hrot/Editor/Hrot.Editor.AiShared/Windows/PerspectiveWorkspaceRegistrar.cs | Breakpoints = new AiBreakpointsWindow(
R-71 | Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Variables/BlueprintAssetTickSource.cs | left it open on purpose
R-67 | Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs | facetEditService:              facetEditService,
R-68 | FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Canvas/CanvasRenderer.cs | ImGui.MenuItem("Watch this Value")
R-64 | Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/PendingDebugMutation.cs | IsFieldWrite => ByteOffset >= 0
R-65 | docs/blueprints/Architect_Question_32_Variable_Details_And_Values_ANSWERS.md | It fits, exactly.
R-62 | docs/blueprints/Architect_Question_32_Variable_Details_And_Values_ANSWERS.md | AND A SEQUENCING RULING: NO VISUAL CHECKS
R-60 | docs/blueprints/REPORT_Batch82_U6_Details_Hosts_The_Table.md | two of the three have no Details panel
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
R-01 | docs/blueprints/Variable_Model_Unification.md | occupy the SAME cell
R-01b | docs/blueprints/Variable_Model_Unification.md | names, one concept
R-02 | docs/blueprints/Architect_Question_32_Variable_Details_And_Values_ANSWERS.md | it makes no sense to emit them differently
R-04 | docs/blueprints/BOOTSTRAP_Cross_Host_Variable_Model.md | the tagged type is the VIEW, the three lists are still the STORAGE
R-05 | docs/blueprints/Variable_Model_Unification.md | That is the parallel implementation to remove
R-06 | docs/blueprints/Variable_Model_Unification.md | BlackboardVariableRole
R-07 | docs/blueprints/Variable_Model_Unification.md | and stop there
R-09 | docs/blueprints/Variable_Model_Unification.md | they surface in the authoring UI
R-10 | docs/blueprints/Architect_Question_32_Variable_Details_And_Values_ANSWERS.md | no keeping two implementations for the same concept
R-10b | docs/blueprints/Architect_Question_32_Variable_Details_And_Values_ANSWERS.md | not optional cleanup; it is the acceptance criterion
R-11 | docs/blueprints/Architect_Question_32_Variable_Details_And_Values_ANSWERS.md | THREE surfaces that show
R-15 | Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Windows/BlueprintMyBlueprintModel.cs | reads as a broken feature
R-16 | Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Windows/BlueprintMyBlueprintModel.cs | REFUSES OUT LOUD
R-19 | docs/blueprints/Architect_Question_32_Variable_Details_And_Values_ANSWERS.md | not two behaviours of two different concepts
```
