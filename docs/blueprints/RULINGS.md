<!--STATUS
state: LIVE
updated: 2026-08-26
current-answer: this file is now LEAN. Feature intent lives in the DESIGN docs (search
  docs/blueprints, now free of handoffs/reports). Generic process rules live in CLAUDE.md.
  This file holds ONLY what those two cannot: engine invariants, silent-failure hazards,
  and cross-cutting decisions with no single design home.
stale-below: nothing.
note: every quote is verified verbatim by scripts/rulings-check.py; a rotted quote fails the
  gate. On 2026-08-21 the ~120 feature-decision rows were removed (they cite a design doc
  that holds them — verified by the probes); all removed rows remain in git history.
-->
# ⭐⭐⭐ RULINGS — **cross-cutting canon. Lean on purpose.**

> ⭐⭐ **Code answers *"how it IS."* ⛔ It can NEVER answer *"how it was MEANT to be."*** ⇒ **that answer
> lives in the DESIGN docs** *(`docs/blueprints`, now free of handoffs/reports)* and, for generic process
> rules, in **`.claude/CLAUDE.md`**.
>
> ⛔⛔ **This file is NOT a feature index.** It was, and it rotted and bloated. It now holds only the three
> things a design search and CLAUDE.md do NOT give you:
> **① engine invariants that fail silently · ② cross-subsystem hazards · ③ decisions that span features.**
>
> ⭐ **For any feature question: `grep -rln docs` and read the owning DESIGN doc** *(`R-129`)* —
> ⛔ do not expect a row here. Every quote below is verified verbatim by `scripts/rulings-check.py`.

---

## 0. ⭐⭐⭐ How to use this file

| when | do |
|---|---|
| ⭐⭐⭐ **session start / after compaction** | **READ THIS WHOLE FILE** *(it is short)* + `.claude/CLAUDE.md` |
| ⭐⭐ **a FEATURE design question** | ⛔ **do not look here first** — `grep -rln docs` for the feature, read the owning `DESIGN_*` / `Architect_Question_*_ANSWERS` doc *(`R-129`)* |
| ⭐⭐ **before a handoff item / architect question** | ⛔ **cite the owning DESIGN doc + section** in `design-basis`, not a row here |
| ⭐ **when you find a CROSS-CUTTING hazard/invariant/decision** | ⭐⭐ **add a row** — ⛔ but a feature ruling belongs in its DESIGN doc, not here |

---

## 1. 🔴 SILENT-FAILURE HAZARDS & STANDING CONSTRAINTS — **cross-subsystem; a design search will not surface these**

| id | ⭐ the ruling | source |
|---|---|---|
| ⚠ **R-07** | ⚠⚠ **UNRECONCILED — `DESIGN_Parameter_Model.md` (marked AUTHORITATIVE, `2026-08-16`) gives ONE three-valued `Scope` `{Node,Behavior,Entity}`, contradicting this.** ⛔ **Reconcile before acting.** As ruled by `Q-b` *(`2026-08-13`)*: **`Scope` is NOT cross-host** — blueprint `{Asset, Graph}` = **visibility**; AI `{Node, Behavior, Entity}` = **blackboard slot sharing**. `Q-b`: *"No. `Asset` and `Graph`, and stop there"* | `Variable_Model_Unification.md` |
| ⭐⭐⭐ **R-21** | ⛔⛔ **NO VISUAL CHECKS until the Details panel is implemented AND the emitters and all access infrastructure are unified.** ⭐ **`VISUAL_CHECK_Guide.md` is SUSPENDED, not cancelled** *(user, `2026-08-14`)*. ⚠⚠ **I ran one anyway on `2026-08-17` — the user re-derived this ruling unaided** | `Q32_…_ANSWERS.md` |
| 🔴🔴 **R-24** | ⛔⛔ **Field order must be preserved within each group — or every LIVE blueprint state slot is HARD-RESET.** 📐 **Measured `2026-08-18`, `BlueprintTickSystem.cs:92`:** every tick compares `slot.StructureHash` to the compiled `def.StructureHash`; on mismatch it calls **`ResetSlot` + `InitDefault`** ⇒ ⭐ **accumulated runtime state on that entity is discarded and replaced by declared defaults.** ⭐ **NOT silent** — `_logSink.OnHardReset(blueprintId, entity, oldHash, newHash)`. ⚠ *"Deployed"* means **live entity state in a running or loaded world**, ⛔ not a release artefact | `BlueprintTickSystem.cs:92` |
| 🔴🔴 **R-63** | ⛔⛔ **A DIRECT WRITE TO `ActiveView` WHILE PAUSED IS LOST ON RESUME.** 📐 **Measured `2026-08-18`:** `OnHit` captures `_postTickSnapshot ← _liveRepo` then rewinds `_liveRepo ← _preTickSnapshot`; `ActiveView` **is** `_preTickSnapshot` while paused; ⭐ **`RequestStep`/`RequestContinue` restore `_liveRepo ← _postTickSnapshot`**. ⇒ ⛔ **ruling 15's *"the command buffer may be UNNECESSARY — write directly to the view"* is MEASURED FALSE.** ⭐ **The ECB staging path is REQUIRED** — the restore overwrites the whole repository, so anything written into the rewound view is erased. ⚠⚠ **STATE CLAIM CORRECTED `2026-08-22` by `W5`** *(📌 §M: a state claim rots; the DECISION above does not)*: this row used to add *"…and THEN drain"*. 🔴 **The resume path no longer drains** — the drain is the kernel's `PreFrame` `ResumeAndDrainSystem`, a PULL *(`R-126`)*, which is what lets a TOOLBAR pause's edit land at all. ⭐ The restore stays, and the ordering argument is unchanged: the drain runs on a later advancing frame, i.e. still after the restore. ⚠ **REFINED `2026-08-26` by `CE-035`:** the restore in `RequestContinue` is now **guarded by `_isPaused`**, and the RESUME half is unconditional — ⭐ the restore only ever made sense while the world was rewound, so this row's claim is unchanged for the paused case; ⛔ do not read it as *"`RequestContinue` always restores"* | `DataBreakpointManager.cs:495` |
| ⚠ **R-65** | ⛔ **`Blackboard1024` is ONE component SHARED by BTree, HSM and Blueprint at disjoint offsets** ⇒ a whole-component write **clobbers other subsystems' state**. ⚠⚠ **The size argument I used twice — *"exceeds `MaxComponentSize`"* — is FALSE** *(`1024 > 1024` is false; it fits exactly)*. ⭐ **Cite the sharing, never the size** | `Q32_…_ANSWERS.md` |
| 🔴🔴 **R-88** | ⭐⭐⭐ **A VARIABLE NAME IS LOAD-BEARING AT RUNTIME IN FOUR CASES, AND EDITOR-ONLY OTHERWISE — do not treat rename as one feature.** 🔴 `Scope=Behavior` *(slot key = `FNV(assetId ++ name)`)* · 🔴🔴 `Scope=Entity` *(slot key = `FNV(name)`, **cross-asset** via `TryGet/TrySetShared`)* · 🔴 **any variable a scenario overrides** *(`ParseParams` step 2 switches on `case "{name}"`)* · 🔴 **any blueprint declaration** *(`StructureHashComputation` appends `f.Name` ⇒ `R-24` hard reset)*. ⭐⭐ **The promoted BTree/HSM `Role=Input` params case is NOT among them** — offsets come from the packer and the thunk key is `MethodFqn@offset` ⇒ **editor-only** | `DESIGN_Variable_Details_And_Editing.md` §5 |

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

## 2. ⭐⭐ CROSS-CUTTING DECISIONS — **recent; span features or subsystems, so they live here not in one design**

| id | ⭐ the ruling | source |
|---|---|---|
| ⭐⭐⭐ **R-124** | ⛔⛔⛔ **THE IMGUI LAYER IS RAILABLE — `R-21`/`R-62`'s PREMISE IS FALSE HERE.** *(user, `2026-08-20`: "of course imgui can render, you have virtual frame buffer xvfb")* 📐 **PROVEN, not argued** — the stack is **Raylib-cs 7.0.2 + rlImgui-cs 3.2.0**, the machine has **Xvfb + Mesa software GL**, Raylib ships `TakeScreenshot`, and a probe **reproduced the live Batch-99 defect and verified its fix**: an `AlwaysAutoResize` popup with a `WidthStretch` column measures `GetContentRegionAvail().X` = **60.0 px** *(the clamp floor — `InputInt`'s step buttons eat it)*; one `SetNextWindowSize` ⇒ **305.0 px** and the value renders. ⇒ ⭐⭐⭐ **the strongest form needs NO image comparison** — measure inside a real frame *(`GetContentRegionAvail`, `GetItemRectSize`, `IsPopupOpen`)*, which catches the width defect, the un-drawn modal and the reopening `[x]` alike. ⚠⚠ **`R-21`/`R-62` STAY IN THE LEDGER** — ⛔ everything built under them was correct AT THE TIME; ⭐ what changed is the measurement, not the judgement. ⛔ **From now on a UI defect is REPRODUCED before it is fixed** | `tools/ui-probe/README.md` |
| ⭐⭐ **R-125** | ⭐⭐ **EVERY MODAL CLOSES ON `ESC`.** *(user, `2026-08-21`, on the variable edit dialog: "can be cancelled by [x] closing button (but not by ESC - any modal needs to be)")* ⭐ **A general rule, not a fix to one dialog** — ⛔ a modal that traps the designer is a defect whatever it is about. ⚠ **`ESC` must do what CANCEL does** — 📌 the same lesson as the `[x]`: ImGui clearing its own flag is not enough while the SESSION stays open, or the popup reopens on the next frame *(Batch 100 `100c`)* | `RULINGS.md` |
| ⭐⭐⭐ **R-126** | ⭐⭐⭐ **ONE SOURCE OF "PAUSED" — THE CLOCK — AND THE TICK LOOP DRAINS.** *(user, `2026-08-21`: "the final real source is just one - the (1). All others should be derived from this (1) and likely they should not be latched/cached as the state should be read from the original source all the time")* ⭐ **(1) is `GlobalTime.TimeScale == 0`** — 📐 already a DERIVED property on an ECS singleton pushed every tick. ⛔ **The debugger pause is a CAUSE, not a second source**: it switches time mode *(cluster-wide eventually — 🔒 "now just inside the editor process")*, and the clock reports the result. ⇒ ⛔⛔ **an OR-ing predicate over the arms is the THIRTEENTH notion, not the fix.** ⭐⭐ **And staged writes drain from the SIM TICK LOOP** — *"on the first next simulation tick… maybe in first brain non-frozen tick"* ⇒ ⛔ **PULL, not a release event: no path can forget to raise what is never raised.** ⭐⭐⭐ **Corollary the user stated directly — *"I do not understand how comes that something can be unwritable… we should be able to write anything anywhere"*: RUNNING IS NOT A REASON TO REFUSE, IT IS A REASON TO STAGE.** ⇒ `RefusedRunning` and `LiveWriteRefusal.NotFrozen` are **deleted**; ⚠ only DATA-shaped refusals survive *(no entity · field not resolvable · size mismatch — 📌 the last is `Q32` §2.1's corruption gate and must stay)* | `Architect_Question_48_What_Stopped_Means_And_Who_Drains.md` §5 |
| ⭐⭐⭐ **R-127** | ⛔⛔⛔ **THE INTEGRATION NET COMES FIRST — NOTHING IN THE TIME REFACTOR STARTS UNTIL IT IS GREEN.** *(user, `2026-08-21`: "the integration tests are the most important thing we need to make working before we touch any time monitoring/control related code. lets put that as the beginning of all the tasks belonging to the time system unification/refactor.")* ⭐ **The net is `Hrot.ClusterRunner.Integration.Tests` ▸ `TimeControlIntegrationTests`** — a real orchestrator + a real SimHost over `MockNetworkFactory`, a full `ClusterOpRequest` → intent → `MasterSyncController` → DDS → slave round trip. ⇒ ⭐⭐ **`T0` in `PLAN_Time_System_Refactor.md`**, and the first Batch-104 dispatch was **WITHDRAWN** under rule 1c because it began with `GlobalTime`, a kernel phase and a drain system — all time code, all before a working net | `PLAN_Time_System_Refactor.md` §1 |
| ⭐⭐⭐ **R-128** | ✅✅ **THE `2026-08-15` FREEZE IS SCOPED TO THE VARIABLE MODEL — A SECOND, TIME-LANE SESSION IS APPROVED.** *(user, `2026-08-21`: "the freeze was about the variable model, time lane is fine. approved.")* ⭐⭐ **Still frozen to ONE session:** variables · working state · the blackboard panel · `Hrot.Editor.AiShared` · the Q38/Details work. ⭐⭐⭐ **Outside it and now approved:** `Fdp.Toolkits/Time/` · `Hrot.Orchestrator` · `ModuleHostKernel` · `Hrot.ClusterRunner.Integration.Tests`. ⛔⛔ **`MIN` is NOT in the time lane** — it edits `BlueprintDebugSession`, `BlueprintLiveValueWriter` and `VariableEditCommit`, so it ships with the UI/variable session. ⇒ ⭐⭐ **THREE RULES KEEP THE LANES APART, and they are STRUCTURAL:** ① **id prefix per lane** — `BP-` UI, **`TM-` time** *(📌 id collisions have bitten three times)* · ② **tracker partition** — the time lane writes **only** `Area H` · ③ **no cross-lane files** — ⚠ a cross-lane edit is a **STOP-and-report** | `.claude/CLAUDE.md` |
| ⛔⛔⛔ **R-129** | ⭐⭐⭐ **READ THE OWNING DESIGN DOC BEFORE TOUCHING OR DESIGNING A CHANGE TO AN EXISTING FEATURE — the CODE is not the intent.** *(user, `2026-08-21`)* ⛔ **The full, generic rule lives in `.claude/CLAUDE.md`** *("THE INTENT IS IN THE DESIGN DOC, NOT THE CODE")* — ⭐ **put there on purpose so it is not buried among the rulings** *(user)*. ⇒ **Indexed here; read it there.** | `.claude/CLAUDE.md` |
| ⭐⭐⭐ **R-130** | ✅ **YELLOW = A STAGED-CHANGE INDICATOR. It is meaningless for a value written directly/immediately.** *(user, `2026-08-21`: "yellow display is an indication of staged change. makes no sense if value is directly written now. make it yellow when really staged.")* ⇒ ⭐⭐ **A row goes 🟡 yellow ONLY while a genuine STAGED write is pending** *(not yet applied by a tick)* — 📌 `DESIGN_..._Editing.md` §4a *("YOUR optimistic edit has not been applied yet — clears when the staged write lands")*. ⛔⛔ **A directly-applied write is NOT staged ⇒ NO yellow.** ⇒ ⭐⭐⭐ **CONSEQUENCE: this reaffirms the §6/`R-126` STAGED model and puts `MIN`'s direct `_liveRepo` write (`WriteFieldNow`) at odds with the design** — the honest resolution is to STAGE the write *(so the yellow is true and Flight-Recorder linearity holds — §6)*, which needs the drain-from-tick-loop *(`R-126`: PULL from the sim tick; `W1`/`W2`; `M-41` is the gap)* | `DESIGN_Variable_Details_And_Editing.md` §4a–§6 |
| ⭐⭐ **R-131** | ⛔⛔ **A CRASHING / UN-GATEABLE TEST IS A DEFECT TO RESOLVE — analyse, fix, or JUSTIFY its removal; ⛔ never a permanent filter-around.** *(user, `2026-08-23`)* 📌 `BP-378` *(`Hrot.ClusterRunner.Integration.Tests`)* + `BP-419` *(`Fdp.Presentation.Tests`)* have been gated by FILTER only for ~40 batches — that is the "generic refusal" this ends. ⭐ The subprocess harness relieves the e2e portion, but ⛔ does not excuse leaving the in-process suites broken | `DESIGN_Headless_Testability.md` |
| ⭐⭐ **R-132** | ⛔⛔ **A CURATED (HAND-AUTHORED) ARTEFACT OUTRANKS A GENERATED ONE — and two producers for one slot bound by REGISTRATION ORDER is a race, not a precedence rule.** *(user, `2026-08-23`: "if curated (hand-authored) exists, then no other is needed - having automatically generated is undesired in such a case.")* 📌 **The case:** `ApplyResolverOverlay`'s `if (def.ParseParams == null)` let a **generated** `ParseParams` win the slot, so the curated geo-aware resolver for `PlatoonHillAttack` never ran and the platoon drove to `(0,0)`. ⭐⭐ **Silent because the two resolvers expect DIFFERENT WIRE FORMATS** — the generated lambda switches on blackboard variable names, the mission plan's keys are flat and geo ⇒ every key hit a `default: break` *("unknown key: IGNORED")* and the params region stayed **zeros**: no exception, no log line. ⚠ **Found by a visual check through the MCP API; every rail was green** | `Behavior_Parameter_Resolver_Detailed_Design.md` §10 |
| ⭐⭐⭐ **R-134** | ⛔⛔⛔ **NETWORK IS STRICTLY SEPARATED FROM INTERNAL FDP EVENT PROCESSING — no DDS type crosses into the FDP-internal path; the egress translator is the SOLE boundary.** *(user, `2026-08-25`: the gizmo must NEVER use a DDS structure directly; the intent FDP-bus record uses its OWN enum, the egress converts to the network enum — "even at the cost of keeping the same enum duplicated in two namespaces, still numerically identical")* ⭐ **The pair already exists:** `AttributeValueKind` *(FDP-internal, `Fdp.Toolkits/Replication/Patching`)* vs `AttributeValueType` *(network, `Hrot.Network.NED`)*; precedent = `NavigationIntent` internal-vs-wire, converted only in `NavigationIntentEgressTranslator`. ⛔ **Duplication here is the CORRECT pattern, not debt.** 🔴 **As-built finding:** the merged `AttributeEntityComponentWriter` (AX-003) uses DDS `AttributeRecord`/`AttributeValueType` inside the FDP-internal path — AX-005 corrects it | `DESIGN_Cgf_AxisB_Rotation_Slice.md` §11.1 |
| ⭐⭐ **R-133** | ⛔⛔ **THE CAPABILITY MANIFEST IS MEASURED, NEVER DECLARED — and the known-absent BASELINE lives in the HARNESS, not in the manifest.** 📌 **The case (`2026-08-25`, cgf==editor slice 1):** the handoff and the design both said *"flip the capability-manifest cells absent→present"*, and there is **no such cell to flip** — `CapabilityManifest`'s DESCRIPTION layer is *"enumerated from the live route table"* and its AVAILABILITY layer is *"measured from what is actually wired"*. ⇒ ⭐ **a port is a reviewed DELETION from `ClusterConformanceRails.EditorOnlyKinds`**, and nothing else. ⭐⭐ **Why this is a ledger row and not a design note:** a hand-authored availability table is exactly §M's disease — it stays green while the code drifts — and this is the second time an artefact asked someone to hand-edit one. ⛔ **A cell reported present that silently no-ops is worse than an absent one** *(`AQ54` `D4`)* | `Hrot/Subsystems/Hrot.Editor/DebugApi/CapabilityManifest.cs` |

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

| # | the measurement | ⭐ run this · answer | last |
|---|---|---|---|
| ⭐⭐ **M-37** | **What actually costs the time in a small-fix loop, and do the ~8 000 unit tests catch anything?** | `time dotnet build <proj>` vs `--no-restore` vs `scripts/quick-check.sh`; and read the last 8 REPORTs for what found each defect | ⭐⭐⭐ **`2026-08-20`: RESTORE, not tests.** build **79 s** → `--no-restore` **16 s** → `quick-check.sh` **8 s end to end**; a filtered run is **3 s**, the whole Blueprints suite **179 s**. ⇒ ⭐ the small-fix loop was **10× slower than it needed to be**, for a reason unrelated to test count. ⚠⚠ **And the tally is stark:** in batches 94–101 every defect was found by **a NEW rail written for that item** or **by the user opening the editor** — ⛔ **not one by the ~8 000 existing regression tests**, whose only firing was a false positive. ⭐ **Re-run them at the BATCH gate, never per fix** | `2026-08-20` |
| ⛔⛔⛔ **M-38** | **Does `isSimUp` read the CLUSTER STATE, as `R-69` ruled?** *(user, `2026-08-21`: "whether sim is running or not should be read from orchestrator's state machine state (each node has a cluster orchestrator client), is it so?")* | read `EditorSubsystem`'s `isSimUp` lambda; then `EditorApplication:46` `_currentClusterState`; then `grep -rn "VariableRunState.Replay" --include=*.cs Hrot/ \| grep -v Tests` and ask **who PRODUCES it** | ⛔⛔ **`2026-08-21` (Batch 102 §9): NO — a canon row the code contradicts.** `isSimUp` reads `IPreviewController.IsInPreviewMode`, **a private `bool` in a nested class** *(`EditorSubsystem:467`)*, ⚠ **while the editor already receives and stores the real signal** — `EditorApplication:46` `_currentClusterState`, fed from `ClusterStateUpdateEvent`. ⭐ Every node has a `ClusterSlave` *(`EditorSubsystem:863`)*, exactly as the user said. ⇒ ⭐⭐⭐ **the measurable cost: `VariableRunState.Replay` is CONSUMED AND NEVER PRODUCED** — `VariableEditPolicy:153` denies editing during replay and `VariableWatchGesture:74` has an arm for it, ⛔ **but two booleans cannot emit it** ⇒ **a shipped safety rule that cannot fire**; plus `OperatingEdit`/`OperatingLive` flattened to one bit, and the transients/`Degraded` defaulting to `Planning` *("show the initial value")* during exactly the moments the world is changing. ⚠ **`isFrozen` stays LOCAL** — the cluster has no notion of a breakpoint stop | `2026-08-21` |
| ⭐⭐⭐ **M-39** | **Can a rail DRIVE the editor — click, right-click, type — not just render it?** *(user, `2026-08-21`: "isn't this visual check something the implem session can do headlessly with xvfb and dpi aware clicking?")* | `tools/ui-probe/synthetic-input/` — build it and run under `xvfb-run`; it prints the injection API, the click count and the typed text | ⭐⭐⭐ **`2026-08-21`: YES, PROVEN.** `ImGuiIOPtr` exposes **`AddMousePosEvent` · `AddMouseButtonEvent` · `AddKeyEvent` · `AddInputCharacter` · `AddMouseWheelEvent`** — all present. A probe **clicked a button and typed `42` into an `InputText`** under Xvfb: `clicks=1 typed='42'`. ⭐⭐ **Two mechanics matter:** ① **inject AFTER `rlImGui.Begin()`**, i.e. after the backend has pushed real input, or it is overwritten; ② ⭐⭐⭐ **never guess coordinates — the APP publishes them**: `GetItemRectMin/Max` right after drawing a widget gives its rect in the same space the injection uses ⇒ **coordinate-free and DPI-proof by construction**, ⛔ not "DPI-aware maths". ⚠⚠ **What it CANNOT catch:** injection enters at the ImGui layer, so it **bypasses the OS/polling input path** — 📌 exactly where the user's own dropped-click bug lived. ⚠ And `ManagedWindow.Render` still dies under a real GL context on a zero-handle icon atlas *(Batch 100)*, so **full-window** driving needs that first | `2026-08-21` |
| ⛔⛔⛔ **M-40** | **The editor has TWO notions of "the simulation is stopped" and the run state reads only ONE.** *(user, `2026-08-21`: "When setting the new value in running paused, it tells me i can only do that when simulation is paused")* | read `EditorSubsystem:2229` `isFrozen:`; then `RunStateSource.Resolve`; then `grep -rn "IsPaused\|TogglePlayPause" --include=*.cs Hrot/Engine/Hrot.Presentation/Facades/` | ⛔⛔ **`2026-08-21`: CONFIRMED.** `isFrozen: () => (_bpManager?.IsPaused ?? false)` — ⭐ **the DATA-BREAKPOINT manager, and nothing else.** ⚠ But the ordinary pause the designer presses is **`ITimeTransportFacade.TogglePlayPause()`** *(`MainToolbarTimeControlSection:42`, `ClusterTimeControlStatusBarSection:47`)*, whose state is **`ITimeTransportFacade.IsPaused`** — ⛔ **never consulted.** ⇒ ⭐⭐⭐ **the designer pauses on the toolbar, `RunStateSource.Resolve` answers `Running`, `TargetFor(Running)` is `Nowhere`, and the dialog refuses with *"only when the simulation is paused"* — while it IS paused.** ⚠⚠ **Batch 102 §9 said *"`isFrozen` stays local"* — true, ⛔ but incomplete: there are TWO ways to be stopped and it sees one.** ⭐ The facade is already in the composition root ⇒ this does **not** wait on `M-38`'s `ClusterState` work | `2026-08-21` |
| ⛔⛔⛔ **M-41** | ⭐⭐⭐ **"TWO notions" was wrong and so was "five" — how many are there, and WHO DRAINS a staged write?** *(the user's own measurement, `2026-08-21`: "livewrite unavailable, simup true, frozen true => paused" — ⭐ **the run state was CORRECT and the edit was still refused**)* | `search_graph(name_pattern=".*(IsPaused\|IsFrozen\|IsStopped\|PausedBy).*")` — ⛔ **not grep, `R-74`**; then `trace_path("DataBreakpointManager.DrainPendingMutations", direction=inbound)`; then corroborate with `grep -rn "RequestContinue\|RequestStep" --include=*.cs Hrot/ FDP/ \| grep -v Tests` | ⛔⛔ **`2026-08-21`: TWELVE production notions, not two and not five** *(total 91 declarations)* — enumerated in **`Q48` §0**. ⚠⚠ **And the far bigger finding: `DrainPendingMutations` has exactly THREE production callers — `RequestStep`, `RequestContinue`, `OnHotReloadBegin`, ALL on `DataBreakpointManager` itself**, and **no production code outside that class calls either request** *(graph AND grep agree)*. ⇒ ⭐⭐⭐ **`BlueprintDebugSession.Continue()` resumes via `_timeController.RequestResume()` and never tells the queue** ⇒ ⛔⛔ **a staged live write appears to land only when a HOT RELOAD happens to occur.** ⇒ ⚠ **widening the `_isPaused` gate ALONE would turn "refused with a wrong reason" into "accepted and silently discarded"** — 📌 `Q48-C` is therefore the BLOCKING sub-question, not `Q48-A`. ⛔ **Reading only — no rail has run this end to end; `Q48-E` is what would** | `2026-08-21` |
| ⛔⛔⛔ **M-42** | ⭐⭐⭐ **WHICH FIELD of the clock means "stopped"? ⛔ NOT `GlobalTime.IsPaused`.** *(the correction `R-126` needs: the user said "giving **dt=0**", and the existing flag says `TimeScale == 0`)* | read `FDP/Engine/Fdp.Core/GlobalTime.cs`; then `MasterSyncController.UpdateStepping` + `BuildGlobalTime`; then `grep -rn "GlobalTime.IsPaused" --include=*.cs FDP/ Hrot/ \| grep -v Tests` | ⛔⛔ **`2026-08-21`: `GlobalTime.IsPaused` is `TimeScale == 0`, and NOTHING EVER SETS `TimeScale` TO 0 ON A PAUSE.** 📐 Pause is `PauseTimeIntent` → `SwitchToDeterministic` → `MasterMode.Stepping`, and `UpdateStepping` returns `BuildGlobalTime(dt: _pendingStepDelta, …)` with **`TimeScale = _timeScale` UNCHANGED** ⇒ ⭐⭐⭐ **the flag is FALSE while the simulation is paused.** ⚠ **And it has ZERO production readers**, which is the only reason this has never bitten. ⇒ ⛔⛔ **a refactor pointing the twelve notions at `GlobalTime.IsPaused` would ship twelve readers of a flag that never fires.** ⭐ **The true predicate is `DeltaTime > 0` (advancing) / `== 0` (halted)** — 📌 `AS-1` in `DESIGN_Time_Architecture.md` §6 | `2026-08-21` |
| ⛔⛔⛔ **M-43** | ⭐⭐⭐ **WHY can a live value not just be written? ⛔ NOT threading — the TICK NEVER STOPS.** *(user: "I do not understand how comes that something can be unwritable. The only real reason might be threading issues")* | read `SubsystemOrchestrator:105-114`; then `ExecutionPolicy.Validate:148-157` and `ModuleHostKernel:246`; then `ModuleHostKernel.ShouldRunThisFrame` — ⚠ **does it consult `deltaTime`?** | ⭐⭐ **`2026-08-21`: THREADING IS NOT THE REASON.** 📐 The runner is ONE loop *(`Update(dt); DrawWorldAll(); DrawUIAll();`)*, `DataStrategy.Direct` is `Synchronous`-only and **enforced** by `ExecutionPolicy.Validate`, and async modules run on **leased views** ⇒ ⭐ **the UI writes between frames with nothing else touching the live repo.** ⛔⛔ **The real mechanism: `ShouldRunThisFrame` NEVER CONSULTS `deltaTime`** ⇒ a module at ≥60 Hz **ticks every frame even while paused**, with `moduleDelta == 0` ⇒ ⭐⭐⭐ **a direct write to a RECOMPUTED value is overwritten on the next frame, with time still stopped.** ⇒ ⭐ **STAGE and drain at the top of an ADVANCING tick** — ⚠ **and accept that a recomputed variable cannot SHOW its new value while paused**; the surface must say *queued*, not pretend. 📌 The old refusal sentence *"the edit would be overwritten by the next tick"* described a REAL mechanism — ⛔ refusing instead of staging was the error | `2026-08-21` |

⭐⭐ **A measurement older than ~14 days is a rumour.** ⛔ **Never quote an answer above — run the command.**

## 4. ⭐⭐ WHERE TO LOOK when there is no row

> ⭐⭐⭐ **USER, `2026-08-17`:** *"most designs are in the **docs** folder. in the `.dev` those named like
> 'design' describe **what was implemented**."* ⭐⭐⭐ **REFINED `2026-08-21`, verbatim:** *"the .dev
> contains implemented intents, but they are still intents telling often more than the code."*
> ⇒ ⛔⛔ **CORRECTION of my earlier over-reading:** I had written *".dev is as-built, citing it proves
> nothing." That was WRONG.* ⭐⭐ **A `.dev` design is an IMPLEMENTED intent — it explains the WHY and
> the MEANT-TO-BE, often more than the code does.** ⇒ **it IS a valid intent source.** ⚠ The only
> caveat: it describes work already built, so **check whether a NEWER `docs/` design supersedes it**
> *(STATUS block / recency)* — ⛔ not *"discard it because it is old."*

| # | look | it tells you |
|---|---|---|
| ① | ⭐⭐⭐ **`docs/**` — `Architect_Question_*_ANSWERS.md`** | ⭐ **THE RULINGS.** ⛔ the non-`ANSWERS` files carry only options |
| ② | ⭐⭐ **their §"Sequencing" tables** | ⛔ **a finding with a planned batch is NOT a new finding** |
| ③ | ⭐⭐ **`docs/` — `DESIGN_*.md`, `*_Unification.md`, `BOOTSTRAP_*.md`, `PLAN_*.md`, `docs/UX/`, `docs/projects/`** | ⭐ **THE CURRENT INTENT — the model as it is MEANT to be now** |
| ④ | ⭐ **`.dev/<programme>/*-DESIGN.md`, `*_Detailed_Design.md`** | ⭐⭐ **IMPLEMENTED INTENT — the WHY behind what was built, often more than the code.** ⚠ check for a newer `docs/` supersession before treating it as current |
| ⑤ | `.dev/**/reports/*-REPORT.md` tails · `TASK-DETAIL.md` | **the DEBT** *(`DEBT-*` ids are filed here and nowhere else)* · the authorising user decision |
| ⛔ | `*-INSTRUCTIONS.md`, `reviews/*`, `batches/` | ⛔ **ephemera — they restate the design; not an intent source** |

⭐⭐ **The distinction that stays:** ⛔ **CODE** tells you *how it IS* and can never tell you *how it was
MEANT to be*; ⭐ **a `.dev` design doc, even an old one, DOES carry intent the code cannot.** ⚠ **What a
`.dev` doc cannot promise is CURRENCY** — a later `docs/` design may have moved on. ⇒ read it for intent,
check its STATUS for supersession.

## 5. ⛔ MY OWN CORRECTIONS — **do not repeat these**

| ⛔ what I claimed | ✅ the truth |
|---|---|
| *"Working State `[+]` opening no dialog is not a defect — it is deliberate"* | ⛔ **overruled.** Its premise *("renamable in place")* was false, and consistency outranks the saving |
| *"the BTree/HSM `Working State` name is a COINCIDENCE"* | ⛔ **wrong. `Role` is genuinely shared** — only `Scope` differs |
| *"`Q39` is: should the outline merge two sections?"* | ⛔ **wrong framing** — it is **infrastructure**, stages `B`+`D` |
| ⭐⭐⭐ *"pull Batch 81 §3b — it hardens the split"* | ⛔⛔ **WRONG, and it is the MIRROR of my usual error.** ⚠ I had just spent a day learning *"do not reason from code without the design"* — ⭐⭐ **and then reasoned from the design without measuring the code.** 📐 **Both premises were false.** ⇒ ⭐ **A design-based objection to an IMPLEMENTATION must be measured too** |
| *"rename the three `Variables` windows"* | ⚠ **incomplete** — the design says **retire** *(`U-16`)*; the rename is an **interim** the user authorised |
| *"`E3` is a signature widening" / "the dangerous case" / "`E5`'s dependency is stale"* | ⛔ **wrong 4×** — the **params BASE** is what collides |
| ⭐ *"the watch row needs a `LiveWriteFrame` re-sample clock"* | ⛔ **wrong — `R-129`.** The intent *(optimistic yellow display, already built-but-unwired)* was in `DESIGN_..._Editing.md` §4a/§6, which I did not read before designing. ⭐ I reasoned from the sampler's code instead of the owning doc |

---

<!-- MACHINE-CHECKABLE PROBES — id | file | verbatim substring that MUST exist in that file -->
---

```probes
R-52 | docs/blueprints/DESIGN_Variable_Details_And_Editing.md | prerequisite, either way
R-63 | Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs | _liveRepo.SyncFrom(_postTickSnapshot);
R-65 | docs/blueprints/Architect_Question_32_Variable_Details_And_Values_ANSWERS.md | It fits, exactly.
R-21 | docs/blueprints/Architect_Question_32_Variable_Details_And_Values_ANSWERS.md | AND A SEQUENCING RULING: NO VISUAL CHECKS
R-24 | docs/blueprints/Variable_Model_Unification.md | If it does not, every deployed blackboard is wiped.
R-39 | docs/projects/FDP/Toolkits/Fdp.Toolkits.Analyzers.md | DTO size <= 100 bytes in BrainBlackboard
R-40 | docs/projects/Hrot/Blueprints/Hrot.Blueprints.Core.md | at offset 0
R-42 | docs/projects/relationships/AI-Behavior-Authoring.md | Behavior integer IDs are permanent
R-44 | docs/projects/FDP/Core/Fdp.Core.md | MAX_COMPONENT_TYPES = 256
R-47 | docs/projects/FDP/ExtDeps/NodeEdit/NodeEditor.Core.md | Never write through IGraphModel
R-48 | docs/projects/SOLUTION-OVERVIEW.md | no stable ABI boundary
R-49 | docs/blueprints/DESIGN_Variable_Details_And_Live_Values.md | NEVER GENERATE PER-VARIABLE CODE
R-88 | docs/blueprints/DESIGN_Variable_Details_And_Editing.md | name load-bearing at runtime?
R-124 | tools/ui-probe/README.md | the ImGui layer IS railable
R-126 | docs/blueprints/Architect_Question_48_What_Stopped_Means_And_Who_Drains.md | so the final real source is just one - the (1).
R-127 | docs/blueprints/PLAN_Time_System_Refactor.md | the integration tests are the most important thing we need to make working
R-128 | .claude/CLAUDE.md | the freeze was about the variable model, time lane is fine. approved.
R-129 | .claude/CLAUDE.md | THE INTENT IS IN THE DESIGN DOC, NOT THE CODE
R-130 | docs/blueprints/DESIGN_Variable_Details_And_Editing.md | OPTIMISTIC DISPLAY
R-131 | docs/DESIGN_Headless_Testability.md | not generic refusal
R-132 | docs/blueprints/Behavior_Parameter_Resolver_Detailed_Design.md | automatically generated is undesired in such a case
R-133 | Hrot/Subsystems/Hrot.Editor/DebugApi/CapabilityManifest.cs | The known-absent BASELINE lives in the HARNESS, not here.
R-01b | docs/blueprints/Variable_Model_Unification.md | names, one concept
R-07 | docs/blueprints/Variable_Model_Unification.md | and stop there
R-10b | docs/blueprints/Architect_Question_32_Variable_Details_And_Values_ANSWERS.md | not optional cleanup; it is the acceptance criterion
```
