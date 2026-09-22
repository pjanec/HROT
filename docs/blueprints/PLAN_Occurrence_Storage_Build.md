<!--STATUS
state: LIVE
build-state: PLAN — the dispatchable breakdown of an approved design. ⛔ NOT a design: every task
  REFERENCES its owning chapter and restates nothing. If this file and the design disagree, the DESIGN wins.
updated: 2026-09-21
current-answer: §2 is the stage/task table (14 tasks, 5 increments). §3 is the under-specified register
  (W1–W5, all implementer calls). §4 is what this deliberately does NOT contain. §5 is the dispatch
  grouping — ⭐ increment A is dispatchable now, nothing blocks it.
stale-below: nothing — new document.
known-rot: nothing.
known-conflict: ⚠ ONE, and it is a deliberate deviation from the design's §6 sequence table, argued in
  §2-A3: the design homes H1's `Reserved` copy in `O3a`, but `Kind` lands in `O3`. Between the two,
  every tier promotion would zero the nibble array. ⇒ H1 moves INTO `O3` here. The design's §6 row is
  corrected in the same commit (obligation ⑤).
related-designs:
  - docs/blueprints/DESIGN_Occurrence_Scoped_Storage.md — ⭐ THE owning design for every task here.
    Start at its §16. ⛔ Where this plan and that design disagree, the design wins.
  - docs/blueprints/RESUME_Occurrence_Storage.md — the lane resumption. Its §4 is the trap list.
  - docs/blueprints/Architect_Question_37_Unify_On_The_Allocator.md — the decision record; read it
    when a task's rationale is unclear.
  - docs/blueprints/Architect_Question_35_Hsm_Occurrence_Delivery.md — owns HOW the occurrence reaches
    an HSM thunk. D2 (guards served) is an amendment recorded there.
  - docs/blueprints/PLAN_Asset_Management_Build.md — the shape this file mirrors.
  - docs/RUNBOOK_Cluster_Debugging_Over_Http.md — how to run the golden-test gate. §2.1 is load-bearing.
-->

# PLAN — **occurrence-scoped storage: the build breakdown**

> ⛔ **No design content here.** Each task names its owning chapter.
> ⭐ **Five increments, 14 tasks.** `A` is the enabler — ⛔ nothing else may start before it.
> ⭐⭐ **`C1` is the STOP-OR-GO gate**: it proves the model with **zero** ExtDeps edits. If it fails,
> stop before paying for `D1`.

## 0. How to use this

| | |
|---|---|
| ⭐⭐⭐ **THE GOLDEN TEST IS THE GATE** | `hill-attack-close` must be **green before AND after every task**. ✅ It is green today *(design §15.3, reproduced 3×)*. ⛔ A task that reddens it is not done. 📄 Run it per the runbook; ⚠ **`--mode all` and editor mode both work** |
| ⭐⭐ **T-1 first** (`R-142`) | the feature suites exist — ⛔ run them BEFORE writing code and add **into** them: `BlackboardLayoutTests` · `PartitionAllocatorTests` · `BlueprintSharedStateTests` · `BehaviorIngressStatefulTests` · `HsmStatefulProvisioningTests` · `CodeBuiltStatefulActionTests` · `StatefulPrimitiveTests` |
| ⭐⭐ **reuse, do not re-derive** | the partition allocator is **production-proven** and one behaviour system already runs on it. ⛔ A second allocator, a second slot table or a second freshness rule is a review finding |
| ⭐ **ids** | ⛔ the coordinator allocated NONE (rule 3). Number them into the tracker; state them in the report |
| 🔴 **verification constraint** | **`CE-295`** — `load_scenario_live` works **once per process**. ⇒ ⭐ **one scenario per process**, or the harness silently re-tests the first one |
| ⚠ **the ONE ExtDeps crossing** | `D1` only. ⛔ `A`–`C` must not touch `FDP/ExtDeps/**` at all — if a task thinks it needs to, that is a STOP-and-report |
| ⛔⛔ **never a text rename** | §10's rename is Roslyn-only, and it comes **after** `A2`. `GlobalComponentIds` **field** names may change; ⛔ **the numeric ids must not** (`R-44`) |

---

## 1. WHAT THIS BUILDS, IN ONE PARAGRAPH

One lookup — *"which occurrence am I, and where are its bytes"* — built on the allocator that already
ships; a **declared** `Kind` per slot so a tick system filters by declaration instead of by a registry
miss; a `TierSpec` table that collapses ten hand-rolled three-way branches and makes a fourth tier
additive; a **256-byte tier** so the simple case pays ~1× instead of 4–5.3×; then the adoption itself —
BTree state and params into slots *(the proof, with no kernel change)*, blueprint Instance params, and
**one** crossing into `FastHSM` that stamps the occurrence onto the command writer for actions **and**
guards. ⛔ **No new allocator, no new component family, no new freshness rule.**

---

## 2. THE TASKS

### Increment A — the occurrence seam *(the enabler — ⛔ nothing else starts first)*

| # | task | success condition | owning chapter |
|---|---|---|---|
| **A1** *(`O3`a)* ✅ **DONE `2026-09-20`** | ⭐⭐ **UNIFY THE SLOT KEY — this is `O3`'s FIRST job, not an assumption it may lean on.** 📐 Three entry points and two enums today: `StatefulBTreeActionBinder.ComputeStatefulSlotKey(Guid, StatefulSlotScope, Guid, string)` (`:89`), `BTreeBridgeEmitCore.ComputeStatefulSlotKey(Guid, WorkingStateScope, Guid, …)` (`:227`) and a 2-arg overload (`:187`) | ⭐ **ONE function.** ⛔⛔ **Existing keys must come out BYTE-IDENTICAL** for all three scopes — rail it against the current values, because a changed key silently orphans every live slot. ⭐ The new signature can express `(assetId, hostPath)` to **depth ≥ 2** *(§3-W2)*. ⚠ **≥4 test mirrors** hand-copy this FNV and must move with it | design **§3** (`F5`); ruling 9 |
| **A2** *(`O3`b)* ✅ **DONE `2026-09-20`** | **`OccurrenceStoreAccess`** — the one lookup blast-radius classes 4 / 5 / 6 all call | ⛔ **No behaviour change in this task.** ✅ **11 ladders collapsed**; 24 rails; `Fdp.Toolkits.Tests` 2232/2232 unchanged. 📐 **Measured 20 sites / 11 files, not "~15"**. 🔴 **The seam grew an RO variant mid-task**: `GetRefRW` bumps the chunk version and `GetRefRO` does not (`NativeChunkTable:158-161` vs `:166-167`), and `DeltaQuery` reads it ⇒ resolving a read-only consumer through the RW form is a **behaviour change** | design **§3**, **§5** classes 4–6 |
| ⭐ **A2b** *(split out `2026-09-20`)* ✅ **DONE `2026-09-20`** — ⛔ **THREE ladders, not two**: `EmitStatefulDeactivatorTierBlock` (`S3-G`) is parameterised per tier and the census missed it. 📐 Golden diff shape **11 files · +252 / −1234 · net −982**, purely the collapse — no key, offset, struct size or registration moved. Suite **280/280 unchanged** *(as-built in design §13)* | **The EMITTER pair** — `BTreeBridgeEmitCore:650, :726` emit the three-tier ladder INTO generated code | ⛔ **Deliberately its own increment, not a tail of `A2`.** ⭐ It is the only genuine remaining duplication and the one that **multiplies into every generated assembly** — ⚠ but changing it **moves the generated goldens**, so the movement must be reported as a **DIFF SHAPE** (gate row 3), against `Hrot.AiEditor.Generators.Tests` (280). ⛔ Six other un-collapsed sites are NOT this: they are different shapes, listed in `RESUME_Occurrence_Storage` §4, and collapsing them would change behaviour | design **§5** class 4 |
| **A3** *(`O3`c + `H1` + `H2`)* ✅ **DONE `2026-09-20`** — ⭐ 4 rails, all red-proved; `Fdp.Toolkits.Tests` **2232/2232 unchanged**; the five production attach sites now DECLARE their kind, so **`O0`'s precondition is MET** *(as-built in design §13)* | ⭐⭐⭐ **`Kind` as a NIBBLE PER SLOT in `OccurrenceStoreHeader.Reserved`** *(`D1′` — 8 B = 16 slots × 4 bits, an exact fit)*. ⛔ **NOT in `BlueprintSlotEntry`** *(`Size = 16`, fully packed)*, ⛔ **NOT in the payload** *(the head is the shipped 16-byte `BlueprintLatentCursor`)* | 🔴 **THREE red-first rails, and each pins a different way the nibble array dies:** ① **detach** — `TryDetach:188-199` dense-compacts, so the nibbles must compact in lockstep **and clear the vacated tail** · ② **promotion** — `CopyToLargerTier` never copies `Reserved` (`Initialize:38,44-51` + `:272-290`), so every upgrade zeroes the whole array; one line beside `:272` fixes all **three** promotion sites · ③ **`Kind == 0` is `Invalid`**, never a valid kind. ⛔ **Write all three RED first** — a zeroed array is indistinguishable from *"everything is kind 0"* | design **§13** (`D1′`), **§7** rails |
| **A4** *(`O0`)* ✅ **DONE `2026-09-20`** — ⭐ splice moved into `CgfLogicPack`; 3 rails, all red-proved; scope ruled **CGF + editor** by the user, not literally every host. 🔴 The `Kind` filter reddened **120** tests whose harnesses attach without declaring — fixed at the harnesses, ⛔ never by defaulting the overload *(as-built in design §6)* | **WIRE `BlueprintTickSystem` on every ECS host.** ⚠ **Not a re-home** — it already lives in `Fdp.Toolkits`; only `BlueprintRuntimeWiring` is editor-side (`EditorSubsystem:1585/1594`) | ⭐⭐ **A `--mode all` run shows a blueprint *Instance* TICK COUNTER ADVANCING on CGF** — ⛔ **anti-vacuity: CGF already materialises and event-attaches Instances**, so asserting the slot exists proves nothing. ⭐⭐ **The walker filters on the DECLARED `Kind`** — ⛔ never on `_registry.TryGetById(…) → continue`, which works today only because `BlueprintRegistry` happens not to know an FNV key | design **§2.2**, **§6** `O0`; `F7`/`F8` |

### Increment B — the tier machinery

| # | task | success condition | owning chapter |
|---|---|---|---|
| **B1** *(`O1`)* ✅ **DONE `2026-09-20`** — ⭐ id **270** *(265 collided; 3 pre-existing collisions found and filed as `QA-037`)*; provisioning via `SquadStateProvisioning` from **both** roster creators; golden test green *(as-built in design §6)* | **`SquadCognitiveState` gets its own typed component** — removes the largest non-AI consumer of `Blackboard1024` | ⭐ commander-scoped state survives a round trip with **no** projection onto a shared blackboard. ⚠ **Pure win even if the rest is cancelled** | design **§5** class 1 |
| **B2** *(`O2`)* ✅ **DONE `2026-09-20`** — ⭐ `BrainInterrupts` id **302**; `BrainBlackboard` **128 → 100 B** *(28 dead bytes per brain entity reclaimed)*; attach beside the blackboard's in `BehaviorTkbTranslator`; `R-39` reconciled, `R-41` superseded; golden test green. 🔴 **A rail caught that splitting a component splits its AUTHORITY** — `BrainInterrupts` must join `BrainBlackboard`'s ownership set before `gateOnAuthority` is ever enabled *(as-built in design §6)* | **Split `BrainBlackboard` → `BrainInterrupts` + an addressable params region** | ⭐ the params region is addressable; the interrupt tail stops travelling with it. ⛔⛔ **`R-39` and `R-41` pin byte offsets 126/127 and the param-region size — UPDATE BOTH LEDGER ROWS**, do not silently invalidate them | design **§5** class 2; `R-39`, `R-41` |
| **B3①** *(`O3a` — THE COLLAPSE)* ✅ **DONE `2026-09-20`** — ⭐ `BlueprintTierSpec` + `BlueprintTierTable`; **18 files, +522/−957 C# ⇒ net −435**, plus 3 new files. 3 verbatim `TickTier_*` → 1 · quadratic `UpgradeTier` → 1 body · 3 byte-identical renderers → a generic base + 3 stubs · 6 `BehaviorIngressSystem` helpers routed to the EXISTING seam *(they were never tier branching)*. 7 rails, red-proved. 🔴 **Fixed a latent editor hole**: `EntityBlueprintsPanel` corrupted a `1024→16384` jump, and nothing repaired it. 🔴 **New hard gate found by building**: `MaxSlots ≤ 16`, because `A3`'s Kind nibble array is 4 bits × 16 in the header's `Reserved` *(as-built in design §17)* | ⭐⭐ **Collapse per-tier branching to a `TierSpec` table** — ingress, tick, renderers | ⛔⛔ **Ladder values UNCHANGED (4/8/16) ON PURPOSE**, so *"did the refactor change behaviour?"* has a provable answer (design §17.6) | design **§17**; `G7` |
| **B3②** *(`O3a` — THE RE-PICK, PLAN `W1`)* ✅ **DONE `2026-09-20`** — ⭐ ladder **12 / 16 / 16**; the numbers live in `BlueprintTierLadder`, LINKED into the compiler, so `Stage2_Validate`'s literals are gone and **`N1` is CLOSED**. 🔴 **The re-pick forced 4096 to 16 too**: a larger tier with FEWER slots makes promotion a capacity reduction — now pinned by `B3_R1`. 3 new rails, red-proved *(as-built in design §17)*. ✅ **PRE-MEASURED `2026-09-20`: the 801–928 B band is EMPTY in BOTH populations** *(behaviour manifests max **320 B**; blueprint Instances max **128 B**)* ⇒ **`MaxSlots 12` on the 1024 tier moves NOTHING**, and it takes the worst case (`PlatoonHillAttack2`, 9 slots / 320 B) from **16384 → 1024**, a **16×** reduction *(design §17)* | ⭐⭐ **RE-PICK THE `MaxSlots` LADDER with a sizing rationale** | 🔴 **A REAL BEHAVIOUR CHANGE, and bigger than it looked.** 📐 The root occurrence promotes an 8-slot behaviour 4096 → **16384** on slot count alone, while `MaxSlots 12` on the 1024 tier still leaves **800 B** (§5a). ⛔⛔ **BUT: `Stage2_Validate.cs:503-508` hard-codes the payload budgets `928 / 3936 / 16096` as LITERALS**, in an assembly that sees `Fdp.Toolkits` only under `net8.0` (it also targets `netstandard2.0`) ⇒ **re-picking desyncs compile-time validation from runtime capacity** (design §17.1 `N1`). ⭐ Fix by the `A1`/`BP-306` precedent: a netstandard2.0-safe ladder file, LINKED (§17.5). ⛔ And `MaxSlots` may not exceed **16** — the Kind-nibble ceiling | design **§17.5/§17.6**; `W1` |
| **B4** *(`O3b`)* ✅ **DONE `2026-09-20`** — ⭐⭐ **genuinely additive, which is what `O3a` was FOR**: 3 ladder consts, 1 struct, 1 id (**303**), 1 appended enum member, 1 table entry, a **4-line** renderer, one compiler `Auto` arm. ⛔ Registration, promotion, probe order, adjacent pairs and the tick walker all pick it up from the table with **no code edit**. `MaxSlots` **3** *(measured — `W4`'s lean of 2 was overturned)*. 2 rails, both red-proved *(as-built in design §17)*. 🔴🔴 **AND THE GATE FOUND A PRODUCTION DEFECT THE "additive" CLAIM MISSED** — the two sites that pick a tier from CONTENT (`BlueprintInstanceService.AttachToEntity`, `BlueprintMaterializationSystem`) added that component without looking at the tier the entity ALREADY carried, so a small instance on a 1024 entity left it carrying **two** stores and the largest-first probe returned the empty one. ⛔ Dormant before `O3b` only because 1024 was the ladder's floor. ✅ Fixed by `BlueprintTierTable.EnsureAtLeast` + the one shared `Promote` body; rails `B4_R3`/`B4_R4` *(design §17.7)* | **Add the `OccurrenceStore256` tier** — `Q37` option **B** |

### Increment C — ⭐⭐⭐ the model proof *(the STOP-OR-GO gate)*

| # | task | success condition | owning chapter |
|---|---|---|---|
| **C1** *(`O4`)* | ✅ **DONE `2026-09-20` — THE STOP-OR-GO GATE IS GO.** Proved the model with **ZERO** ExtDeps change: `BehaviorTreeState` untouched, no new delegate parameter, no `BTreeContext` field, no kernel edit. Golden GREEN *(`522.7·524.8·528.4·531.0`, both targets `Health 0`, 0 faults)*; 6 runtime rails + 2 emit guards; red-proof exact *(reverting only the hosting argument reddens only rail ①)*. ⚠ **Not done, and named in design §21.2**: the root occurrence still lives in `BrainBTreeState` *(§19.7 ①'s "both halves" was measured TOO STRONG)*, end-to-end abandon is unmeasured, and the EXTERNAL reset path *(`BehaviorIngressSystem:204`/`:235`)* is uncovered. ⛔ §19.4's sizing re-measure is vacuous today — 0 assets carry an alias, so `B4`'s `MaxSlots 3` is unmoved. ⛔ HISTORY: **THE TWO HALVES ARE NOT INDEPENDENT — measured `2026-09-20`, design §19.7 ①**: `ComputeNested` returns the ROOT form verbatim when `hostKey == 0`, dropping `siteId`, so a hosted child cannot be keyed until the **root occurrence has its own slot**. ⇒ **ship both or neither.** ⭐⭐⭐ **BTree onto occurrence storage** — tree state and params into slots, **including a hosted subtree's own `BehaviorTreeState`** and its **RE-ENTRY RESET** | ⭐⭐ **PROVES THE WHOLE MODEL WITH ZERO ExtDeps CHANGE** *(§4.1)*. 🔴 **Two red-first rails:** ① **a hosted subtree keeps its own cursor** — host `Running` at node A, child `Running` at node B, **both survive a tick**; ⛔ **must go RED before this task**, because it reproduces the shipped defect at `BTreeOrchestratorEmitCore:143-144/:176` *(both variants pass the MASTER's `ref state`)* · ② **re-entry reset** — when the host re-enters the hosting node the child's cursor resets; ⚠ own state removes the accidental continuity `ref state` provided | design **§3.1**, **§6** `O4`; `F14` |
| **C2** *(`O5`)* | ✅⛔ **ALREADY BUILT — NO WORK. This row was STALE when it was written** *(measured `2026-09-20`)*. 📐 `git log`: commit `a957ed448` *(`2026-08-17`, "the Instance params seam")* shipped **all three** of `DESIGN_Parameter_Model.md` §3.3's *"what must be built"* items — **① the layout** *(`FieldLayout.ParamsStructBase` = 0 for an AiPrimitive, `BlueprintLatentCursorSize` 16 for an Instance; state base `16 + N`)*, **② the attach payload** *(`AttachInstanceBlueprintEvent.ParamsJson`, `AttachToEntity(..., string? paramsJson)`)*, **③ parse-before-commit** *(a `stackalloc` resolve before any slot is allocated, new status `ParamsParseFailed`)*. ⭐ **And the rail this row demanded already exists BY NAME**: `InstanceParamsSeamTests.ResolvingParams_DoesNotTouchTheLatentCursorAtOffsetZero`. ✅ **Verified `2026-09-20`: 13 / 0.** ⇒ 🔒 **`RULE ZERO`'s corollary, paid again — never write *"X is not built"* without running the enumeration that would find X**; three greps would have spared this row | ⭐ the slot layout is now shared with `C1`. 🔴 **Rail the `startOffset: 0` trap**: an Instance with params keeps its `BlueprintLatentCursor` at payload offset 0 after a resolve | design **§6** `O5`; `DESIGN_Parameter_Model.md` §3.3 |

### Increment D — ⚠ the kernel crossing *(the ONLY ExtDeps edit in this programme)*

| # | task | success condition | owning chapter |
|---|---|---|---|
| **D1** *(`O6`)* | ✅ **DONE `2026-09-20` — THE ExtDeps CROSSING IS PAID. As-built in design §23.** 📐 **The whole 156-project solution build reported exactly TWO compile errors**, both in `BlueprintTestFixture.InvokeHsmGuard` ⇒ §4.2's *"single-digit blast radius"* held and the ACTION delegate really is untouched. ⭐ **TWO stamping sites and only two** — `HsmKernelCore.ExecuteAction` / `.EvaluateGuard`, which every dispatch already funnelled through; the pair is threaded from **7** call sites that each already had both halves in scope. ⚠ `SelectTransition` gained `ref HsmCommandWriter` purely so a guard can be stamped before evaluation. 🔴 **Rail ③ found a fixture defect worth keeping**: `HsmInstance128` has FOUR region slots and **`0` is a valid state index**, so unmarked regions read as *"in State 0"* and the guard fired four times — an instance must mark unused regions `0xFFFF`. 6 rails in `HsmOccurrenceStampTests` *(in-solution, because `Fhsm.Tests` cannot gate)*; red-proof exact: removing only the two stamps gives **0 build errors, 4 red / 2 green**. ⛔ **Nothing READS the pair yet — that is `O7`** | ⚠ **The kernel stamps the occurrence.** Three things ride **one** crossing: ① two additive fields on `HsmCommandWriter` *(a `ref struct` — stack-only, no ABI)* + the kernel assigning them before each dispatch · ② **`EvaluateGuard` widens to carry the writer** *(`D2`)* · ③ the additive **pointer overload** forwarding to the already-`internal` `UpdateBatchCore` *(§9.4 option b)* | ⛔ **The ACTION delegate is untouched** — the 55 attributed methods, both `FDP/Examples` projects and FastHSM's own demos still compile unchanged. ⭐ **The guard population is generated, and `R-50` is why it is safe**: emitted behavior source is machine-owned and regenerated whole. 📐 Generator sites that bake the guard signature as literal text: `HsmActionGenerator.cs:551, :564, :567, :659` + `EmitSharedAiGuardThunk:633`, plus `AiPrimitiveEmitter.EmitHsmGuardThunk` and `CSharpEmitter.EmitAiPrimitiveRegistration`. ⭐ `SharedAiHsmTests:80` matches by **name** and survives; `ActionDispatchTests.cs:70,86` call positionally and do not | design **§4.2**, **§9.4**, **§14** (`D2`) |
| **D2** *(`O7`)* ⭐⭐ **PRICED `2026-09-21` — the COLLISION half is nearly free; the row's OTHER two thirds are the cost.** 📐 **The delivery mechanism is already BUILT by `O6`/`D1`**: `HsmCommandWriter` carries `_occurrenceRegionSlotIndex` + `_occurrenceStateId` *(`HsmCommandWriter.cs:28`)*, `HsmKernelCore.StampOccurrence` fires at **both** stamp sites *(`:638`, `:817`)* feeding **5** dispatch sites, and `HsmOccurrence.ResolveOrAttach` *(`HsmOccurrence.cs:49`)* already reads it. ⭐⭐ **The blueprint emitter ALREADY consumes it** — `AiPrimitiveEmitter.cs:614`, `SeedParamsOffset(instance, writer)`. ⇒ ⛔ **the only unconverted consumer is `HsmActionGenerator`**, whose population is **4 inert entries** *(see the `E5a` banner)*. ⭐ **So "close the collision" is: point one emitter at a seam that already exists and has a working precedent.** 🔴 **What actually costs:** ① the row's own blocker — **a genuine 2-region HSM asset must be AUTHORED** or the rail is vacuous *(`BP-297`)*; ② `HsmTickSystem` entity discovery, which only bites once `E4`/`O7c` deletes `BrainHsm*` and the tick query loses its root component; ③ `D3`'s HTTP list. ⛔⛔ **① IS NOT A COST — IT IS THE TRIGGER, AND THAT MAKES THE SLICE INDIVISIBLE** *(corrected `2026-09-21`; an earlier revision of this row recommended splitting ① out, which was WRONG)*. 📐 The 2-region asset **already exists** — `HsmOrthogonalRegions.hsm.json` carries `TopRegion`/`RegionZero`/`RegionOne`, with `RegionZeroWorker` and `RegionOneWorker` both on `CgfHsmNodes.StubIdle`, **whose body is empty ⇒ there are no bytes to collide** *(`BP-297`)*. ⇒ the authoring job is **add a DTO-bound `[SharedAiAction]` to `Hrot.AI.Behaviors`** *(per `BP-297` the only assembly that runs `HsmActionGenerator`)* and point both regions at it. ⛔⛔ **The moment that exists, `NoNewDtoBoundHsmAction_ExistsInAGeneratorBearingAssembly` REDDENS** — by design: *"This is NOT a ban — it means the change now needs `E3`."* ⇒ 🔒 **`BP-297`'s own sentence: *"they are the same blocker, and this is the first time that is stated."*** ⭐ **So it is ONE slice**: author the subject *(rail goes red)* → convert the emitter → move the baseline entry. ⭐ Only ② and ③ may be split off, and ② is blocked behind `E4`/`O7c` anyway. ⚠ **One rail to re-read while here:** `TheActionDispatchSignature_CarriesNoOccurrence_Yet` asserts the DISPATCHER's parameter names carry no occurrence — ⛔ still literally true, but **misleading after `O6`**, because `Q35` deliberately put the stamp on the **`writer`**, which IS one of those parameters | **HSM per-region actions key on the occurrence** — closes `BP-297`/`E3` — **plus `HsmTickSystem` entity discovery** and **`D3`'s HTTP list** | 🔴 **The two-regions rail is VACUOUS unless authored**: `BP-297` measured that today's fixture runs an **empty** action in both regions ⇒ ⛔ **a DTO-bound HSM action must be written as part of this task**. ⚠ **With `BrainHsm*` deleted the tick query has no root component** — it takes `BlueprintTickSystem`'s shape, and **C2's indirection cost must price per-tick discovery across archetypes**, not only per-action lookup. ⭐ `D3`: the AI-state route returns a **list**, with the **root occurrence as element 0, deterministically** | design **§6** `O7`, **§11.3** (`D3`); `F9` |

### ⭐⭐⭐ HOW TO AUTHOR THE 2-REGION VERIFICATION — **a PROOF today, before `D2`** *(user asked `2026-09-21`)*

⛔⛔ **First, the obvious idea and why it FAILS.** *"`E3a` already fixed the blueprint HSM path — so just
point both regions at a blueprint AiPrimitive."* 📐 **Measured: an HSM asset CANNOT name a blueprint
action by FQN.** `HsmFlattener.cs:111` builds its action table as `ComputeHash(name)` — **FNV-1a-16 over
the NAME STRING** — while `CSharpEmitter.cs:474` registers the blueprint thunk under
**`(ushort)BlueprintId`**, and `Stage5_Schedule.cs:72` computes that as
**`BlueprintIdHash.Compute(asset.AssetId)` — a hash of the GUID.** ⇒ ⭐ **two unrelated id spaces, and no
naming convention bridges them.**

⭐⭐⭐ **But there IS a bridge, and it is already in the parser.** 📐 `JsonStateMachineParser.cs:48` reads a
**numeric `entryActionId`** straight into `state.EntryActionId`, and `HsmFlattener.cs:172` honours it
**ahead of** the name hash: `node.EntryActionId != 0 ? node.EntryActionId : actionTable[node.OnEntryAction]`.
⭐ And `HsmGraphValidator.ValidateFunctions:340` gates only on `state.OnEntryAction != null` ⇒ with the
name left null, the registered-name check does not fire.

| ⭐ the recommendation — **one asset edit, no new C#, no tripwire tripped** | |
|---|---|
| **①** | take an AiPrimitive with `Hostings: [HsmAction]` and **parameters**, and read its `BlueprintId` |
| **②** | in `HsmOrthogonalRegions.hsm.json`, set **`entryActionId: (ushort)BlueprintId`** on `RegionZeroWorker` **and** `RegionOneWorker`, leaving `OnEntryAction` null |
| **③** | bind each region's seed to a **different** blackboard variable — ⭐ that is exactly what `SeedParamsOffset(instance, writer)` was built for: the `E3a` rail's own words, *"the seed offset is the STATE'S OWN BINDING, not a literal 0 — that is what lets two parallel regions seed from different variables"* |
| **④** | assert **the two regions' occurrences hold DIFFERENT bytes** after a tick — the invariant `BP-297` could never redden |

| ⭐ what this buys, and what it does NOT | |
|---|---|
| ✅ **proves the occurrence-stamp seam END TO END, in action, TODAY** | `StampOccurrence` *(`HsmKernelCore.cs:638,817`)* → `HsmCommandWriter` → `HsmOccurrence.ResolveOrAttach:49` → the blueprint thunk. ⭐ **De-risks `D2` before a line of it is written** |
| ✅ **trips NO tripwire and needs NO `E3`** | it adds no DTO-bound `[HsmAction]` to a generator-bearing assembly |
| ⛔⛔ **it does NOT close `BP-297`** | ⚠ it verifies the **blueprint** path, which `E3a` already fixed. **The curated `HsmActionGenerator` path is untouched** — that is still `D2`'s job, and still one indivisible slice |
| ⚠ **the numeric bypass is UNEXERCISED** | 🔒 `HsmBridgeEmitCore.cs:195`'s own words: *"used by neither shipped asset."* ⇒ **it may have rot of its own** — budget for that, and treat a failure there as a finding about the bypass, not about the seam |
| ⚠ **two smaller risks to name** | ⭐ `(ushort)BlueprintId` truncates a GUID hash, so it could collide with a name-hashed action id in the same asset — **`BHU_020` is the rail that ranges over the final id set**; and hand-editing `.hsm.json` **moves the HSM goldens** *(`HsmGoldenCorpusTests`)*, so canonicalise deliberately |

## ⭐⭐⭐ THE PATH — **from here to (a) a multi-region parallel action-with-params RAIL and (b) retiring `BrainBlackboard`** *(user asked `2026-09-21`)*

⚠ **They are far apart. The rail is 2 steps away; the retirement is 4 and crosses a DECLINED item.**
⭐ Stated as one ordered path so the distance is visible rather than implied.

### ⛔⛔ First, the measurement that resizes the retirement — **`BrainBlackboard.BehaviorParameters` has THREE roles, and `E5a` names TWO**

| # | role | sites |
|---|---|---|
| ① | **the ROOT behaviour's LIVE params home** *(read)* | `AiPrimitiveEmitter.cs:425,607` · `HsmBridgeEmitCore.cs:243` · `JoinFormationExecutor.cs:92` · `PredicateCompiler.cs:357` |
| ② | **the SEED SOURCE** every hosted occurrence copies from on first attach *(read)* | `AiPrimitiveEmitter.cs:461`, inside the `freshlyAttached` arm |
| ③ | 🔴 **the INGRESS COMMIT TARGET** *(write)* — **newly named `2026-09-21`; `E5a` did not list it** | `BehaviorIngressSystem.cs:113` memcpys the parse shadow into the live component |

⇒ ⭐⭐ **Retirement re-homes all three.** ⭐ The root occurrence answers ① and ③ *(it becomes the params home AND what ingress writes into)*. ⚠⚠ **② is the subtle one**: `SeedParamsOffset` returns an offset **into the packed blackboard variable layout**, so the root occurrence's slot must carry that same layout for the seed to keep meaning what it means. ⛔ **That is a design question nobody has answered**, and it is not in the root occurrence's 136-ref estimate.

### ⭐ The ordered path

| step | what | gate to the next | state |
|---|---|---|---|
| **`P0`** ✅ **DONE `2026-09-21`** | ⭐ **the SEAM carries a hand-authored resolver's values to two regions** — rail `O7_R36` in `HsmOccurrenceKeyTests`, driven by a real `HsmKernel.Update` tick, red-proved exactly *(binding both states to offset 0 reddens only it)* | — | ✅ |
| **`P1`** ✅ **DONE `2026-09-21`** | ⭐⭐⭐ **the rail with a REAL emitted action** — rail `O7_R38`'s predecessor `O7_R37`: two parallel regions, a compiled blueprint thunk, different params each. 📐 Needed a new golden first — **no AiPrimitive in the corpus was hosted as `HsmAction`** *(all 46 were BTree)*, so `HsmTwoRegionParamsDemo` was authored, corpus 46 → 47 | ⭐ It also **validated the numeric `entryActionId` bypass recipe** end to end: the thunk is reached under `(ushort)BlueprintId = 0x647B` | ✅ |
| **`P2`** *(= `D2`/`O7`)* ✅ **DONE `2026-09-21` — `BP-297` CLOSED** | the **curated** `[SharedAiAction]` half, as ONE indivisible slice | ⭐ `HsmTwoRegionCuratedNodes` authored — **2 actions at offsets `@0` and `@4`**, so the `@offset` half of the identity is exercised *(all four legacy entries sat at `@0`)* → **tripwire reddened, as designed** → `EmitSharedAiActionThunk` converted to the occurrence seam → baseline moved. ⭐⭐ **Rail `O7_R38`**: two parallel regions, one curated DTO-bound action, **its own params in each**. Red-proof: forcing `__seedOffset = 0` in the emitter reddens only it | ⭐ **`P2-A`(a)** — `OccurrenceSlotKey.ComputeHsmStateKeyForCurated` folds the compound key *(`fqn@fieldOffset`)* where a Guid goes: **no new hash, no second identity**. ⭐ **`P2-B`** — `HsmOccurrence.EmptyWorkingState`. ⚠ Layout guard is a baked `Fnv64(compoundKey\|dtoFqn)` **XOR `sizeof(dto)` at runtime**, because a curated action has no compiled `StructureHash` |
| — | 🔒 **`(b)` DIVERGES HERE.** ⭐⭐ **After `P2` the COLLISION is gone and `BrainBlackboard` is no longer a hazard — it is merely a SEED BUFFER.** ⛔ Everything below is about **deleting a component**, which buys tidiness, not correctness | | |
| **`P3`** ⭐⭐ **THE REAL ITEM — "UNIFY PARAMS INTO SLOTS", not "delete the component"** | ⭐ the **root** behaviour's params get a slot *(role ①)*, and ingress **SCATTERS** each state's slice into its slot at assign time *(role ③)* instead of leaving a table for occurrences to copy. 🔴 **BUT IT MUST KEEP A FIRST-DISPATCH HOOK — see the resolver constraint below** | 📐 **PRICED `2026-09-21`: the params role is `BehaviorParameters` — 28 production files / 60 refs** *(the component as a whole is 66 files / 176 refs; ⛔ do NOT price this off that number)*. ⚠ **The old "136 refs / 17 files" figure was never about this** — it was `BrainBTreeState` | ⛔ **open, unscheduled** |
| ~~**`P3a`**~~ ✅ **DISSOLVED `2026-09-21`** | ~~where the seed layout lives once ② has no blackboard~~ | ⭐ **the question was wrong.** Slots are **provisioned EAGERLY at assign time already** — `BehaviorIngressSystem.cs:140-165` calls `TryGetHostedOccurrenceDemand` → `ProvisionStatefulSlots`/`EnsureOccurrenceStore`, off a demand computed at **scan** time. ⇒ the **SEED COPY** can be scattered there, and the parse still lands transactionally in the **stack shadow** that already exists at `:92`. ⛔ **There is no persistent seed layout to re-home.** ⚠⚠ **An earlier revision of this cell said the whole `freshlyAttached` arm disappears — WRONG, see below** | ✅ |

#### 🔴 `P3`'s ONE HARD CONSTRAINT — **the RESOLVE hook must stay at FIRST DISPATCH, only the SEED COPY moves**

⛔⛔ **This corrects a `2026-09-21` overstatement of my own** *("eager scatter retires the `freshlyAttached` seed arm")*. 📐 `AiPrimitiveEmitter.EmitParamSeed:440-462` emits **three** things inside that arm, not one:

```
if (freshlyAttached) {
    *__params = <bb.BehaviorParameters[0] + __seedOffset>;            // ① the SEED COPY   -> P3 moves this
    HostedParamResolvers.TryRun(AssetId, ref *__params, world, self, host);  // ② the RESOLVE -> MUST STAY
    InitDefaultWorkingState(...);                                     // ③ unrelated
}
```

| ⭐ why ② cannot move to assign time | |
|---|---|
| ⭐⭐⭐ **it needs `host`, and `IHostVariableAccess` ONLY EXISTS at the child's activation** | 📐 `BehaviorIngressSystem.cs:100` passes **`host: null`** explicitly, and says so: *"this is a ROOT behaviour, which is its defined value… a HOSTED occurrence will pass its host's variable access here, at `E7a`"*. ⭐ `HsmHostVariableAccess` is constructed from the **host's** params pointer + the kernel's stamp — neither exists at ingress |
| ⚠ **so `P3` SPLITS the arm, it does not delete it** | ⭐ the seed copy becomes an eager scatter; ⛔ the `TryRun` stays exactly where it is. ⚠ **A `P3` that removes the whole arm silently disables every hosted resolver** *(`E8a`, `Q41-C1′`)* — no diagnostic, params just stop being refined |

⭐ **This is also where `E8c` meets `P3`, and they do NOT conflict:** `E8c`'s per-variable `TryRun` runs at **ingress**, inside `__parseParams`, above the scatter. ⇒ **`E8c` = how params get their value · `P3` = where the value is stored · ② = the per-occurrence refinement that needs the host.** Three layers, one order.
| **`P4`** ⭐⭐⭐ **RETIRE IT — no true need was found** | delete `BrainBlackboard` **and `Blackboard1024`**; ⛔⛔ ~~the BTree action's blackboard type parameter goes with it~~ — **SUPERSEDED `2026-09-22` by a user ruling: the type parameter STAYS and is BOUND TO `byte`, pointing at the root slot's memory.** 📐 Measured: the FastBTree kernel has **zero** by-value or sized uses of `TBlackboard`, and `where TBlackboard : struct` admits `byte` ⇒ **no `ExtDeps` change at all** | ⭐ **DESIGNED — 📄 `DESIGN_Occurrence_Scoped_Storage.md` §30**, three slices, with UML | ⛔ open, after `P3` *(`P3` is DONE and validated — `CE-304` closed `2026-09-22`)* |

#### ⭐⭐⭐ THE THREE "HOLDS" WERE TESTED FOR A **NEED** AND NONE SURVIVED *(user ruling, `2026-09-21`)*

> 🔒 **User, verbatim:** *"We will retire it unless we find a true need and do not see any. Being part
> of ABI is no reason, ABI can and must change."*

⛔⛔ **An earlier revision of this section called these "structural holds" and concluded the component
must stay. That conflated EFFORT with NEED and is WITHDRAWN.**

| the claimed hold | 📐 what it measured to |
|---|---|
| ⛔ *"the BTree action ABI is hard-typed to it"* | ⭐⭐⭐ **TRUE AND IT IS THE ARGUMENT FOR RETIREMENT, NOT AGAINST.** 📐 Since `B2`/`O2`, `BrainBlackboard` is **exactly and only** `fixed byte BehaviorParameters[100]` *(`BehaviorComponents.cs:58-78` — one field, nothing else)*. ⇒ an action's `ref BrainBlackboard bb` is **purely the params handle**; once params live in slots, **`bb` carries NOTHING**. ⭐ `ActionRegistry<TBlackboard, TContext>` is **generic** — the kernel never names the type. ⇒ this is the SAME work as `P3`, and the emptiness is **positive evidence there is no other purpose** |
| ⛔ *"it is a replicated component"* | ⚠ **OVERSTATED.** `BehaviorTkbTranslator.cs:21-46` is a **component-set DECLARATION** *(`yield return typeof(...)` for `BehaviorState`, `BrainBTreeState`, the channels…)* and `:125` **attaches** it on receive. ⭐ That is provisioning, not payload — and the occurrence store is **already** declared in the same registries *(`CgfSubsystem`, `EditorSubsystem`, `HrotSharedComponentRegistry`)* |
| ⚠ **role ④** — `HsmHostVariableAccess` *(`E7a`)* holds a pointer into `BehaviorParameters` | ⭐ **re-points at the root occurrence's slot.** A consumer to sweep, never a reason to keep the component |

⇒ ⭐⭐⭐ **RETIREMENT IS THE TARGET.** ⛔ *"`BrainBlackboard` stops being a params store"* and *"`BrainBlackboard` goes away"* turn out to be **the same change**, because after `P3` the struct is empty.

#### ✅ `P4`'s DESIGN QUESTION IS **ANSWERED** — **the action is HANDED its DTO; it never looks** *(user ruling, `2026-09-21`)*

> 🔒 **User, verbatim:** *"Action should get the param dto reference, not actively looking for it.
> Action should not need to know where to look for params, it doesn't care."*

⭐⭐⭐ **And the measurement is the good news: THIS IS ALREADY THE SHAPE AT THE BODY LEVEL.**

| 📐 measured `2026-09-21` | |
|---|---|
| ⭐⭐⭐ **a curated `[SharedAiAction]` body ALREADY receives `ref TParams`** | `BlueprintLifecycleLibrary.cs:70` — `AttachInstanceBlueprint(ref AttachInstanceBlueprintParams dto, Entity self, EntityRepository world)`. ⛔ **It has never known where params live** |
| ⭐⭐ **ALL the looking is in the GENERATED THUNK** | `HsmActionGenerator.EmitSharedAiActionThunk:749-788` does `GetComponentRW<BrainBlackboard>(bridge->Self)` → `Unsafe.As<byte, TDto>(BlackboardParamsExpression.At("bb", entry.Offset))` → `{Body}(ref field, self, repo)`. ⇒ **exactly two lines to replace with an occurrence-slot resolve** |
| ⭐⭐⭐ **and the 4 raw bodies that DO take the blackboard NEVER USE IT** | `CgfNodes.cs:417,609` · `HillAttackTankNodes.cs:560,580` — `Action_Wander` and two `[BTreeDeactivator]`s. 📐 **All four work entirely off `ctx.World`/`ctx.Self`; the `blackboard` parameter is unused.** ⇒ they lose a **vestigial** parameter, not a capability |

⇒ ⭐⭐⭐ **The ruling is not a redirection — it is already satisfied wherever an action touches params,
and the four exceptions ignore the argument.** ⛔ **`P4` therefore changes NO hand-written body
semantically:** it changes the **generated adapters**, `BlackboardParamsExpression`, and the
`ActionRegistry<…>` type parameter, and drops 4 unused parameters.

⚠ **What still wants care** *(effort, not doubt)*: the adapters are emitted by **four** generators
*(`HsmActionGenerator`, the two `BTreeActionGenerator`s, the bridge emit cores)*, and
`BlackboardParamsExpression` exists precisely because that expression **was once spelled four ways and
one was wrong** *(`BP-306`)*. ⭐ **Change it in that ONE home** and the adapters follow.

| ⭐⭐⭐ the two answers, plainly | |
|---|---|
| ⭐ **the RAIL** *(a)* | **`P1`** — one asset edit, no new C#, no tripwire tripped. ⭐⭐ **Then `P2` makes it a HAND-AUTHORED action**, which is the full form of the requirement |
| ⭐ **the PARAMS UNIFICATION** *(b)* | **`P3`** — **28 files / 60 refs**, no declined prerequisite, no unowned design question. ⛔ **It does NOT depend on `O8`**, and `P3a` is gone. ⚠ **`P2` should land first** so the curated path is already on the seam when the root joins it |
| ⭐⭐ **RETIREMENT** *(b, completed)* | **`P4`** — ⭐ **the struct is EMPTY after `P3`**, and the design call is **SETTLED**: the action is handed its DTO *(user ruling)*, which is already how every `[SharedAiAction]` body works. ⇒ **no hand-written body changes semantically**; the work is the **generated adapters** + `BlackboardParamsExpression` + the `ActionRegistry<…>` **type ARGUMENT** *(⚠ `BrainBlackboard` → `byte`; ⛔ an earlier wording said the type PARAMETER is removed — **SUPERSEDED**, §30.9)*, plus 4 unused parameters dropped. ⛔ **No true need was found to keep the component** |

---

### Increment E — composition

| # | task | success condition | owning chapter |
|---|---|---|---|
| **E1** *(`O8`)* | **BTree hosted under an HSM state** — the strategic/tactical composition | ⭐ the child is just another occurrence. ⛔⛔ **BLOCKED ON THE DISPATCH FIX** *(§4 ①)* if two regions are meant to host trees concurrently — ⚠ confirm at dispatch *(§3-W3)* | design **§6** `O8`, **§9.3** |
| **E2** *(`O9`)* | **Blueprint as an ASSIGNED ROOT behaviour** — the third `BrainTier` | ⛔ **Three of its four gaps are NOT storage**: registry resolution by name, a root tick path, and joining `BehaviorState.InstanceId` preemption. ⭐ This design is its **prerequisite**, not its delivery | design **§12**; `Q33` |

---

## 3. ⚠ UNDER-SPECIFIED REGISTER — **implementer calls, all of them**

| # | question | ⭐ the call, and what constrains it |
|---|---|---|
| **W1** ✅ **ANSWERED `2026-09-20` — `12 / 16 / 16`** | the `MaxSlots` **ladder values** *(`B3②`, shipped)* | ⛔ **not 4 / 8 / 16 by default.** 📐 `MaxSlots × 16 B` comes out of payload; on the 1024 tier `MaxSlots 12` still leaves 800 B. ⭐ Ship the numbers **with the arithmetic**. 🔴 **TWO CONSTRAINTS FOUND WHILE BUILDING `B3①` (2026-09-20), neither previously written down:** ① ⛔⛔ **`MaxSlots` may not exceed 16** — `A3`'s Kind nibble array is 4 bits × 16 in the header's 8-byte `Reserved`, and slots past it would be **silently skipped** by the tick walker. ✅ Now enforced by a throw in `BlueprintTierSpec.For<T>` and pinned by `B3_R2`. ② ⛔⛔ **`Stage2_Validate.cs:503-508` hard-codes `928 / 3936 / 16096`** in an assembly that cannot see `Fdp.Toolkits` under `netstandard2.0` ⇒ the re-pick must carry a LINKED ladder file (design §17.5) or the compiler and the runtime disagree |
| **W2** | how `hostPath` is **encoded** in the unified key *(`A1`)* | must express depth ≥ 2 *(§11.2 renders nesting as a tree)*. ⚠ **31-bit FNV today** — state the collision argument, or add a collision assert at `TryAttach` |
| **W3** | does `E1` wait on the **dispatch fix**? | design §9.3 says **yes** if two regions host trees concurrently. ⭐ Confirm at dispatch; ⛔ do not smuggle the dispatch fix into a storage task *(§4 ①)* |
| **W4** ✅ **ANSWERED `2026-09-20` — `3`, NOT 2** | `OccurrenceStore256`'s `MaxSlots` *(`B4`)*. ⛔⛔ **The old lean was *"1 or 2; 2 earns the tier"* and it counted SLOTS ONLY.** 📐 Measured with BYTES *(root occurrence priced from the real baked params — max **89 B** of the 100 B cap, 16 of 30 assets use none)*: `@1` 56 % · `@2` 73 % · ⭐ **`@3` 83 %** · `@4` 80 % · `@6` 60 % ⇒ **3 is a genuine maximum**, payload 176 B. ⚠ POST-`O4` arithmetic for a root occurrence not yet built — **re-measure after `O4`**; and `HsmVariableShowcase` sits exactly on the line, so a one-asset swing moves the 83 % *(design §17)* |
| **W5** | ✅ **ANSWERED `2026-09-20` by `B2`: a COMPONENT for the tail, and `BrainBlackboard` ITSELF becomes the params region** — no third type. | 📐 The region needed no new struct: with the tail gone, `BrainBlackboard` is exactly `fixed byte BehaviorParameters[100]`, which is the addressable region `O4` will move into a slot. ⭐ Introducing a separate region type would have added a rename to every one of the 75 consumer files for no behaviour. `R-39` reconciled, `R-41` superseded |

---

## 4. ⛔ WHAT THIS DELIBERATELY DOES **NOT** CONTAIN

| # | | why |
|---|---|---|
| **①** | 🔴 **the `H1`–`H5` HSM dispatch fixes** *(one region consumes the event; global priority; global transitions reporting region 0; timer events carrying no identity)* | ⛔⛔ **A SEPARATE ExtDeps change that must be justified on its own terms** — *"orthogonal-region event dispatch is wrong"* — and ⛔ **not smuggled in as storage work**. ⚠ They are real and structural; they block `E1` only |
| **②** | **`CE-295`'s fix** *(live scenario re-load is a one-shot)* | 🔒 user: file it, do not fix now. ⚠ It constrains **verification** (§0), not the build |
| **③** | **the `§10` rename** *(`BlueprintBlackboard*` → `OccurrenceStore*`)* | ⭐ off the critical path, and it must come **after `A2`** or it is two passes over the same files. ⛔ Roslyn only |
| **④** | **a `BrainHsm256` component** | ⛔ it would be a new component, a new `GlobalComponentIds` entry and a new tick registration that `D2` then deletes. The capacity answer is *"allocate the bigger payload"* |

---

## 5. ⭐ DISPATCH GROUPING

| batch | tasks | state |
|---|---|---|
| **① A** | `A1`–`A4` — the seam, the key, `Kind`, the tick wiring | ✅ **DISPATCHABLE NOW.** ⛔ Carries **no** ExtDeps change. ⭐ `A1` goes **first** — everything else keys off it |
| **② B** | `B1`–`B4` — squad split, blackboard split, `TierSpec`, the 256 tier | ✅ dispatchable after `A`. ⭐ `B1` and `B2` are independent of each other and of `B3`/`B4` |
| **③ C** | `C1`, `C2` — ⭐⭐⭐ **the gate** | ⛔ **`C1` decides whether the programme continues.** Zero ExtDeps edits by construction |
| **④ D** | `D1`, `D2` — the one crossing | ⛔ **only after `C1` proves the model** |
| **⑤ E** | `E1`, `E2` — composition | ⚠ `E1` gated on §4 ①; `E2` is `Q33`'s, three of whose four gaps are not storage |

---

## ⭐⭐⭐ INCREMENT E — **THE AGREED QUEUE** *(user, `2026-09-21`)*

🔒 **User:** *"agreed with the queue, record it as a plan, make sure there are tests for all those
surprisingly found missing pieces like the colliding params."*

⭐⭐ **And the standing rule this increment is built on** *(user, `2026-09-21`)*:
> *"HSMs are under adopted now, but their time will come soon, so all the features need to be covered
> with tests at least if not yet real usages."*

⇒ ⛔⛔ **LOW CORPUS ADOPTION IS NOT A LICENCE FOR THIN RAILS.** 📐 Measured: **1** shipped asset declares
`HsmAction`, **0** declare `HsmGuard`, and **no test anywhere drives a multi-region HSM**
*(`RegionDef[]` is `Array.Empty` in every one)*. ⇒ **coverage has to stand in for usage until usage
arrives.**

### ⭐ E0 — **DEFECT-PINNING RAILS** *(the "surprisingly found missing pieces")*

⭐⭐⭐ **Every defect found by inspection gets a rail BEFORE its fix**, asserting the **current, wrong**
behaviour and naming the id that will flip it.

| ⭐ why this shape | |
|---|---|
| ⭐⭐ **it proves the defect is REAL** | ⛔ not inferred from reading. 📌 This programme has filed a refuted defect before (`CE-296`) — a rail is the difference |
| ⭐⭐⭐ **it REDDENS when the defect is fixed** | ⇒ the fix cannot land silently, and whoever lands it must consciously flip the rail. ⛔ A defect with no rail is a defect that gets re-found |
| ⭐ **the gate stays GREEN** | ⛔ not a `Skip` — *"a new skip is a finding, not a fix"* |

### ⭐ The queue, in order

| # | item | what | why here |
|---|---|---|---|
| **E1** | ⭐⭐ **the two-region rails** | a genuine 2-region HSM driving the same asset at both ⇒ ① working state SEPARATES *(proves `O7b` end-to-end, retiring §7's "the fixture cannot redden this" caveat)* · ② params COLLIDE *(pins `CE-298`)* | ⛔ nothing has ever driven a multi-region machine; every `O7` claim rests on it |
| ⭐⭐ **E-cap** | ✅ **LANDED `2026-09-21`** *(design §27)* — `BehaviorIngressSystem.EnsureOccurrenceStore` provisions the **smallest** tier for a brain-tier behaviour whose stateful manifest is empty, so a lazily-attached occurrence has somewhere to land | ⛔⛔ **This is the SUPPLY half of `O7b-3`, not all of it** — it does **not** size the tier. ⭐⭐ The part that needed care: it **SKIPS when the tier type is not registered** rather than making eight fixtures register it — ⛔ provisioning must not widen the `Fdp.Toolkits` contract *(§27.2)* | rails `O7_R14`/`O7_R15` |
| **E2** | ✅ **`O7d` — LANDED `2026-09-21`** *(design §27.3)*, once E-cap gave it a store. Both standalone thunks route onto `OccurrenceSlots.StandaloneStateKeyFor(AssetId)` + `OccurrenceWorkingState.ResolveOrAttach`; **ASSET-scoped**, because `Interpreter.cs:655` hands an action delegate no node identity and per-node is the BRIDGE's job by design | the standalone BTree thunks | ⛔ **zero goldens still mention the legacy blackboard** ⇒ ⭐ the last *emitted* use is gone, which is what **E5** was blocked on. The defect pin flipped to `StandaloneBTreeThunks_UseTheOccurrenceStore_O7d` | 📐 golden movement: **30 files, +330/−420, net −90** |
| ⭐ **E-cap′** | ✅ **`O7b-3` PROPER — LANDED `2026-09-21`** *(design §27.7)*. The demand is **DERIVED** from the machine's own `StateDef` action ids against the blueprint registry, recorded as an **OVERLAY on `BehaviorRegistry`** *(the `_resolversByName` shape — prior art, not a new mechanism)*, and **ADDED to the manifest's** demand in **both** tier-selection branches | correct TIER SIZING | ⭐ The join runs in `BlueprintRegistrarScanner.Scan` — 📐 measured as the only place in the tree where both registries are populated in one pass ⇒ **`HsmBridgeEmitCore` never learns about blueprints** *(the user's ruling)* | ⚠ the `ushort` premise was ANSWERED first: 85 assets → 85 distinct low-16, and `BHU020` makes a collision a build error. ⭐ Rails `O7_R16`–`O7_R22`, **two independent red-proofs** |
| **E3a** | ✅ **LANDED `2026-09-21`** *(design §28, `DESIGN_Parameter_Model` §4.7)* — slot payload is `[Params N][WorkingState M]`; a hosted occurrence's params are **its own** | ⭐ ① is NOT a regression because it is **SEEDED** from `BehaviorParameters[0] + 0` *(the exact bytes the thunk read before)* and **re-supplied** by `DetachHostedOccurrenceSlots` on re-assign. ⛔ The detach is mandatory, not a follow-up: without it new JSON silently stops taking effect | 🔒 forced by the user's ruling *"forget the fact it is not in use now. it will be"*; rails `O7_R24`–`O7_R27` |
| **E3b** | ⛔ **OPEN — per-site authored VALUES** | ⭐ every occurrence still seeds from the SAME variable ⇒ own *copy*, one authored value. `Q41-C1′` (emit the resolve hook) then `C2′`; `E7a`/`IHostVariableAccess` becomes callable only then | ⚠ **both are APPROVED IN FULL and UNBUILT** — `Q41`/`Q43` STATUS blocks, user `2026-08-18` |
| **E4** | **`O7c`** — delete `BrainHsm64`/`BrainHsm128` | instances into slots; `F9`'s tick-system reshape | 🔴 **L** — 188 refs / 18 production files |
| **E5** | ⭐ **retire the LEGACY `Blackboard1024`** | ⚠ **NOT `BlueprintBlackboard*`** — those ARE the store. 📐 It is already on **ZERO** production entities: both `AddComponent` sites are gated on `HeavyDtoType`, which **nothing ever sets** *(both editor mappers hard-write `null`)* | ✅ **UNBLOCKED** — `E2`/`O7d` landed and no emitted thunk reads it any more. ⚠ What remains is the `HeavyDtoType`-gated consumers and the cleanup, **not** the emitter. 📐 **Re-measured `2026-09-21`, the remaining PRODUCTION sites are six files**: `BehaviorIngressSystem.cs:134,243` *(the gated attach)* · `PredicateCompiler.cs:384` · `BlueprintDebugSession.cs:1016,1519` · `CognitiveComponentRegistry.cs:45` · `Blackboard1024Translator.cs` · `HrotRoleComponentSets.cs:128`, plus ~20 squad TEST files that register it as fixture scaffolding *(that is the "52 files" figure)*. ⭐ **`O1` is DONE** — `SquadCognitiveState` carries its own `[ComponentId]`, so `SquadInputs`/`StandardInputs` no longer reach through `Blackboard1024`; their remaining mentions are stale doc comments. ⛔⛔ **NOT `BlueprintBlackboard1024`** — the two names collide and only the `Fdp.Toolkit.Behavior.Components` one retires |
| **E5a** ⭐⭐⭐ **RETIRE IT — confirmed `2026-09-21`** *(user: "we will retire it unless we find a true need and do not see any. Being part of ABI is no reason, ABI can and must change.")* | ⭐ **`P3`** moves params into occurrence slots *(28 files / 60 refs)*, which leaves the struct **EMPTY** — `BehaviorComponents.cs:58` is one field and nothing else. ⭐ **`P4`** then deletes it. ⛔⛔ **Two earlier framings of this row are WITHDRAWN**: *"gated on the root occurrence"* *(wrong gate)* and *"deletion is not achievable, the ABI forces it to stay"* *(conflated EFFORT with NEED — the three claimed holds are refuted in "THE PATH")* | ⚠ **HISTORY — the gate named below was measured to be the WRONG gate.** 📐 Measured `2026-09-21`: `BrainBlackboard` is now exactly `fixed byte BehaviorParameters[100]` *(`B2`/`O2` moved the tail to `BrainInterrupts`)*, but it is still ① the live root-params home — `AiPrimitiveEmitter.cs:382,528` · `HsmBridgeEmitCore.cs:243` · `JoinFormationExecutor.cs:92` · `PredicateCompiler.cs:357` — and ② **the SEED SOURCE** every hosted occurrence copies from on first attach *(design §28)*. ⇒ retiring it before the root path has a slot zeroes every occurrence's params, which is exactly the regression §26.1's supply rule exists to prevent | ⚠ `C1`(`O4`) landed the hosted half but explicitly **not** the root: *"the root occurrence still lives in `BrainBTreeState`"* *(design §21.2)*. ⭐ Once the root occurrence lands, `BrainBlackboard` has no writer and the four readers above are mechanical |
| **E6** ✅ **`Q43` slice ① LANDED `2026-09-21`** | ⭐ **a parameter resolver authored AS A BLUEPRINT compiles and registers** | `LibraryEmitter` emits a method per `Construction` graph *(the consumer the kind was RESERVED for since `Q23`)* · `BlueprintDefinition.Resolvers` · `V_ResolverPurity` `BP1675`/`BP1676`/`BP1677` with a completeness rail over all 53 node kinds · golden `ParamResolverDemo`, corpus **43 → 44** | 📐 Blueprints **3985/0/18**, Toolkits **2297/0**, Generators **283/0**; golden movement purely additive *(2 new files + 1 line)*; red-proof exact *(neutering only the `Resolvers` table reddens exactly the e2e rail + that one Tier2 golden, 142 others green)*. ⚠ **Two parts shipped PROVISIONAL and are superseded by the `R4` design**: the `Resolvers` CURRENCY *(§7.1 — `LibraryFunctionDelegate` carries no `host`)* and **`BP1676`** *(§7.3 — an asset may carry its OWN resolver graph)* |
| **E7** ⭐⭐⭐ **NEXT — `R4`, DESIGNED `2026-09-21`** 📄 [`DESIGN_Resolver_World_Reach.md`](DESIGN_Resolver_World_Reach.md) *(`build-state: READY-TO-BUILD`)* | ⭐ **give a resolver graph WORLD REACH** | 🔴 today `EmissionContext.cs:279` — `HasSelfInScope => Dispatch != Library` ⇒ a resolver can only do arithmetic on the DTO it is handed, so the **motivating case** *(geo-authored params → `IGeographicTransform.ToCartesian`)* is **not expressible**. ⭐ The emitted method becomes `ResolveParams<TDto>` UNROLLED — `(TDto, EntityRepository, Entity, IHostVariableAccess?)` — so it **IS** the universal currency: no adapter, no bridge, no new delegate | ⭐⭐ **the delta is small and measured**: the gate is **ONE LINE** *(`Stage5_Schedule.cs:3866`)*, the trailing-context mechanism is already built *(`P7`)*, and `EmissionContext`'s **AiPrimitive arm already answers `"world"`** for both scope vars. ⛔ **REFUSES** the *"read-singleton node"* alternative *(§6)*. Acceptance `A1`–`A6`, red-proof included |
| **E8a** ✅ **LANDED `2026-09-21`** *(design §11)* | ⭐⭐⭐ **an asset carries its OWN resolver — and NOTHING names it** | 📐 `HostedParamResolvers.Register` is keyed by the hosted blueprint's OWN asset id and the thunk already emits `TryRun(AssetId, …)` ⇒ producer and consumer key on **one value the asset already carries**, so there is **no binding step**. ⭐ That is why this case needs no selection property | 🔴 **The measurement that resized the slice:** an AiPrimitive's params struct is **GENERATED** (`{Class}.Params`), so an own-asset resolver **cannot name its DTO** ⇒ `BP1677` splits, `BP1676` is revised **and** gains a one-per-region arm, and `BP1675` gains ONE exemption *(a `SetVariable` targeting a PARAMETER is the resolver's OUTPUT)*. ⭐⭐ **`R-149`'s duplicate guard landed here too** — `BehaviorRegistry.RegisterResolver` THROWS while `HostedParamResolvers.Register` keeps OVERWRITING: **opposite policies, measured reasons** *(fresh staging registry per scan vs. asset-keyed re-registration)*. ⛔ The obvious "throw on any duplicate" would have broken hot reload |
| ~~**E8b**~~ ⛔⛔ **WITHDRAWN `2026-09-21` — FOLDED INTO `E8c`** *(design §7.2a)* | ~~the `ParamResolverRef` property on the blueprint ASSET~~ | 🔴 **measured, and the row was MISFILED, not wrong.** All three arms were re-measured after `E8a` landed: `OwnGraphId` is **redundant** *(`E8a` binds on one key, and `BP1676` already forbids a second own-resolver)*; `AssetId+GraphId` is **type-impossible on a blueprint** *(the params struct is GENERATED per asset — `AiPrimitiveEmitter.cs:151` — while a reusable resolver is typed on an AUTHORED `TypeId`)*; `CuratedName` is a **category error** *(`BehaviorRegistry.cs:468` keys on BEHAVIOUR names — `CgfCuratedBehaviorRegistrar.cs:131`)* | ⭐⭐ **The two surviving arms both resolve to a BEHAVIOUR**, so they are `E8c`'s, not a row of their own. ⚠ **The `R-149` ruling is untouched** — the blueprint half simply expresses it structurally instead of with a property. ⛔ **The earlier *"needs a runtime-join check / `V_PeerReferences`"* caveat is WITHDRAWN**: it argued from an under-adopted feature's limits *(the peer-CALL path is designed-only, §8)*, which is evidence for nothing |
| **E8c** ⛔⛔ **NEXT — the EXPENSIVE one, and it now carries `E8b` too. Its own design pass** | the **per-VARIABLE** half for BTree/HSM behaviours — ⭐ **including the `ParamResolverRef` on the blackboard VARIABLE**, which is the region that can actually name one | ⭐ the carrier EXISTS — `ManagedBlackboardVariable(Name, Type, ByteOffset)`, emitted by BOTH bridges — and the resolve site has everything in scope *(after Step 2 in `__parseParams`: the DTO type, the offset, `world`, `self`, `host`)*. ⭐⭐ **And the types LINE UP here**: a params variable is typed on an authored DTO *(`PlatoonHillAttack.btree.json` — `Params : PlatoonHillAttackParams`)*, which is exactly what a reusable resolver declares and what `blackboardLayoutType` already carries | ⛔ but it moves a DTO field on **two** asset kinds, a 4th member on the manifest record, a Step-3 emit in **both** bridges, the scanner join — **and a runtime granularity that does not exist today** *(the curated mechanism is whole-behaviour)*. 📐 31 of 32 params variables are whole structs, so `ResolveParams<TDto>` fits. ⚠ **Check the UI-lane fence**: the asset MODELS live in `Hrot.BTree.Editor` / `Hrot.Hsm.Editor` |

### ⛔⛔ `E5a` — **DO NOT READ THE DEFERRAL AS "`BrainBlackboard` IS HARMLESS"** *(user, `2026-09-21`)*

> 🔒 **User, verbatim:** *"How can the brainblackboard retirement be declined when its existence can not
> be justified — two parallel actions would collide if their params are written to same small
> brainblackboard?"*

⭐⭐⭐ **The objection is CORRECT, and the repo pins it in a rail's own words.**
`HsmOccurrenceCollisionTests.TheGeneratedThunk_ResolvesStateAtAFixedPerEntityOffset_Yet`:
*"the offset is baked at build time and the component is one per entity, so **two concurrently-active
regions running the same action address the same bytes**."*

⛔⛔ **Two DIFFERENT things were conflated here, and the conflation was the coordinator's:**

| | |
|---|---|
| ⭐⭐ **the COLLISION** — a params region addressed by a baked compile-time offset | ⛔ **a CAPABILITY defect**, not uniformity. ⭐ It has its **own open item — `D2`/`O7`** *(closes `BP-297`/`E3`)*, and its design is **finished and waiting**: `Architect_Question_35` *(RESOLVED)* + `DESIGN_Hsm_Storage_Model.md` §3. ⛔⛔ **It is NOT gated on the root occurrence** |
| ⭐ **the STORAGE** — where params live | ⭐⭐⭐ **`P3` then `P4`: params unify into occurrence slots, which leaves the struct EMPTY, and then it is DELETED.** ⛔⛔ **An earlier revision of this cell said *"deleting the component is NOT achievable"* on the BTree-ABI and replication arguments — both WITHDRAWN** *(they were EFFORT, not need; refuted in "THE PATH")*. 📐 `P3` is **28 files / 60 refs**, no declined prerequisite. 📄 **The ordered path `P0`…`P4` is in *"THE PATH"* above**, and it names **two further roles this row never did** *(the ingress commit target; `HsmHostVariableAccess`'s pointer into `BehaviorParameters`)*, ⚠ plus `P3`'s **one hard constraint** — the hosted **RESOLVE** hook stays at first dispatch because it needs `host`; only the seed COPY moves |

⚠⚠ **And the liveness must be stated honestly in BOTH directions — the defect is REAL but LATENT.**
📐 Measured `2026-09-21`: ⭐ the **blueprint** path was fixed by `E3a` *(the thunk reads
`ref *__params` from the occurrence slot; the blackboard survives only as the SEED inside the
`freshlyAttached` arm, at the state's own binding offset)*. ⛔ Only the **curated `[HsmAction]`
DTO-bound** path still bakes an offset — and its population is **FOUR entries, all INERT for two
independent reasons**: nothing calls `HsmActionRegistrar.RegisterAll()`, so no thunk is ever
registered with `HsmActionDispatcher`; and every one sits at offset `0`. ⭐ They carry
`[SharedAiAction]` for the **BTree** host — the HSM thunk is incidental.

⇒ ⭐⭐ **That — not "uniformity, not capability" — is the real reason the fix has not shipped**, and the
tripwire says so: *"building the delivery mechanism now would leave two mechanisms in place, which
`Q35-C` forbids."* ⛔ **A second subject reddens `NoNewDtoBoundHsmAction_ExistsInAGeneratorBearingAssembly`
the day it is authored**, which is what makes deferring it safe rather than merely convenient.

---

⛔ **NOT in this increment, and why:** the **root occurrence** staying in `BrainBTreeState` *(§21.2)* —
📐 136 refs / 17 files for **uniformity, not capability**; `Q4` makes the root genuinely singular and the
tick path would gain a slot lookup. ⭐ **Revisit only if `O8` forces it.** · **`O8`** itself — blocked on
the `H1`–`H3` event-queue hazards, and it wants an architect question with measurements, not a build.

### ⚠ `O7b-3` is RE-SCOPED, twice — read this before picking it up

> ✅ **`2026-09-21` — BOTH halves are now built.** The SUPPLY half is `E-cap` (design §27): an empty
> manifest gets the smallest tier provisioned so lazy attach has somewhere to land. The SIZING half is
> §27.7, landed the same day. ⛔ **The table below is HISTORY** — it records the two re-scopings that got
> the item to a buildable shape, and both of its ✅ columns are what was built.
>
> ⚠ **What §27.7 deliberately did NOT do:** it computes a **SIZE**, never a manifest of keys — the
> occurrence key needs the region slot the kernel picks at runtime. ⭐ Slots still attach lazily.

| ⛔ what I said | ✅ what measuring found |
|---|---|
| *"it gives the inspector typed labels"* | ⛔ **false** — the decode already uses `descriptor.ClrType` / `ResolveType(field.Type)`, and the label is derived. ⭐ **Its real value is TIER CAPACITY**: `ProvisionStatefulSlots` only runs on a non-empty manifest, and that is what sizes the tier |
| *"it needs `HsmEmitCore` to know about blueprints"* | ⛔ **false, and the user was right to refuse it** — 📐 the action id **IS** the truncated blueprint id (`RegisterAction(unchecked((ushort)BlueprintId), …)`), and `StateDef` carries `OnEntryActionId`/`OnExitActionId`/`ActivityActionId`/`TimerActionId` at runtime ⇒ ⭐ **the join can happen at RUNTIME against the blueprint registry; the HSM editor never learns about blueprints.** ⚠ Verify the `ushort` truncation cannot collide two blueprints first |


### 🔴🔴🔴 THE RULE INCREMENT E EARNED — **check SUPPLY before moving STORAGE**

📐 Measured **three times in two days**, each time by building it and watching it break — `O7b-1`
*(inspector left behind, withdrawn a day)*, `CE-298` *(nothing writes hosted params — filed, not
built)*, `O7d` *(nothing provisions the store — reverted the same day)*.

🔒 **Before moving ANY state into an occurrence slot, name the thing that will ① PROVISION the slot and
② WRITE its initial contents. If either answer is "nothing", the move is a REGRESSION** — the old
location at least had a producer.

⚠ **Why it keeps recurring:** the storage half is mechanical and satisfying — a key, a resolve, a rail.
⛔ The supply half lives in another file, often another lane, and **nothing about the storage work
forces you to look at it.** ⇒ the check belongs at the START, not at the gate.
