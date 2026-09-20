<!--STATUS
state: LIVE
build-state: PLAN — the dispatchable breakdown of an approved design. ⛔ NOT a design: every task
  REFERENCES its owning chapter and restates nothing. If this file and the design disagree, the DESIGN wins.
updated: 2026-09-20
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
| **C2** *(`O5`)* | **Blueprint Instances take params** | ⭐ the slot layout is now shared with `C1`. 🔴 **Rail the `startOffset: 0` trap**: an Instance with params keeps its `BlueprintLatentCursor` at payload offset 0 after a resolve | design **§6** `O5`; `DESIGN_Parameter_Model.md` §3.3 |

### Increment D — ⚠ the kernel crossing *(the ONLY ExtDeps edit in this programme)*

| # | task | success condition | owning chapter |
|---|---|---|---|
| **D1** *(`O6`)* | ⚠ **The kernel stamps the occurrence.** Three things ride **one** crossing: ① two additive fields on `HsmCommandWriter` *(a `ref struct` — stack-only, no ABI)* + the kernel assigning them before each dispatch · ② **`EvaluateGuard` widens to carry the writer** *(`D2`)* · ③ the additive **pointer overload** forwarding to the already-`internal` `UpdateBatchCore` *(§9.4 option b)* | ⛔ **The ACTION delegate is untouched** — the 55 attributed methods, both `FDP/Examples` projects and FastHSM's own demos still compile unchanged. ⭐ **The guard population is generated, and `R-50` is why it is safe**: emitted behavior source is machine-owned and regenerated whole. 📐 Generator sites that bake the guard signature as literal text: `HsmActionGenerator.cs:551, :564, :567, :659` + `EmitSharedAiGuardThunk:633`, plus `AiPrimitiveEmitter.EmitHsmGuardThunk` and `CSharpEmitter.EmitAiPrimitiveRegistration`. ⭐ `SharedAiHsmTests:80` matches by **name** and survives; `ActionDispatchTests.cs:70,86` call positionally and do not | design **§4.2**, **§9.4**, **§14** (`D2`) |
| **D2** *(`O7`)* | **HSM per-region actions key on the occurrence** — closes `BP-297`/`E3` — **plus `HsmTickSystem` entity discovery** and **`D3`'s HTTP list** | 🔴 **The two-regions rail is VACUOUS unless authored**: `BP-297` measured that today's fixture runs an **empty** action in both regions ⇒ ⛔ **a DTO-bound HSM action must be written as part of this task**. ⚠ **With `BrainHsm*` deleted the tick query has no root component** — it takes `BlueprintTickSystem`'s shape, and **C2's indirection cost must price per-tick discovery across archetypes**, not only per-action lookup. ⭐ `D3`: the AI-state route returns a **list**, with the **root occurrence as element 0, deterministically** | design **§6** `O7`, **§11.3** (`D3`); `F9` |

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
