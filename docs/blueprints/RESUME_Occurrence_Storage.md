<!--STATUS
state: LIVE
doc-type: LANE RESUMPTION for the `behaviors` lane — programme ②, OCCURRENCE-SCOPED STORAGE.
  ⚠ A STATE doc, not canon. Every "green"/"pushed"/"HEAD" line is a snapshot dated below.
  ⛔ VERIFY against git before acting ("THE LEDGER MAY NOT ASSERT WHAT THE CODE IS").
updated: 2026-09-20
build-state: n/a — a resumption snapshot, not a design.
current-answer: §3 — the NEXT ACTION is task B3 (`O3a`: collapse per-tier branching to a TierSpec
  table AND re-pick the MaxSlots ladder). Increment A is COMPLETE; B1 and B2 are DONE; all pushed.
  §2 is the grounded facts: ⛔ do not re-derive them, they cost real measurement. §5 is the trap
  list, and it is the section most worth two minutes — sixteen of these were MY errors, three of
  which reached a pushed document before being caught.
stale-below: nothing — §3 rewritten 2026-09-20 after B2 landed.
known-rot: nothing.
known-conflict: RESUME_Assets_And_Occurrences.md is the COORDINATOR snapshot (2026-09-19) owning TWO
  programmes. ⛔ SUPERSEDED IN PART for this one: it says the occurrence design has no PLAN (false —
  §1), treats §3.2's defect as "shipped" (measured LATENT), and names the AI entity count as the
  first measurement to take (taken, and it was the wrong one to take first). Neither doc supersedes
  the other; this is the LANE view and is newer.
related-designs:
  - DESIGN_Occurrence_Scoped_Storage.md — ⭐ THE OWNING DESIGN. Start at its §16.
  - PLAN_Occurrence_Storage_Build.md — ⭐ THE TASK BREAKDOWN. 14 tasks, 5 increments.
  - Blueprint_Issues_Tracker.md — CE-295 (open), CE-296 (refuted). Area F.
  - Architect_Question_37_Unify_On_The_Allocator.md — the owning question.
  - RUNBOOK_Cluster_Debugging_Over_Http.md — how to run the golden test. §2.1 is load-bearing.
-->

# RESUMPTION — **occurrence-scoped storage**, the `behaviors` lane

RELEARN

> ⭐⭐⭐ **You are the `behaviors` lane on branch `behaviors`, and you OWN this design.**
> 🔒 **User, `2026-09-20`: *"you take it from here, you are the one owning the design now."***
> ⭐ **Nothing is half-finished.** `A1`, `A2`, `A3` and `A4` are committed, pushed and green; the tree
> is clean. **Increment A is COMPLETE; `B1` and `B2` have landed.** §3 says what is next.

## 0. ⭐ FIRST MOVES

```bash
python3 scripts/session-design-brief.sh          # ledger · digest · probe · 3 random rulings
# then read docs/blueprints/RULINGS.md IN FULL   (RULE ZERO)
git fetch origin && git log --oneline -3 origin/behaviors   # snapshot head: 94fc4c9f1
python3 scripts/rulings-check.py && python3 scripts/design-digest.py --check
```

⚠ **Snapshot `2026-09-20`:** `rulings-check` **37/37** · `design-digest --check` OK (69 docs).
⛔ `tracker-counts --check` says NOTHING about our rows — **it counts only `BP-` rows** (`CE-073`),
and ours are `CE-`. Do not quote it as evidence for them.

---

## 1. WHERE THE PROGRAMME STANDS

| | |
|---|---|
| **design** | ✅ FINALIZED — [`DESIGN_Occurrence_Scoped_Storage.md`](DESIGN_Occurrence_Scoped_Storage.md), start at **§16** |
| **plan** | ✅ WRITTEN — [`PLAN_Occurrence_Storage_Build.md`](PLAN_Occurrence_Storage_Build.md), 14 tasks / 5 increments |
| ⭐ **golden test** | ✅ **GREEN**, re-proved `2026-09-20` after `A3`+`A4`, after `B1`, after `B2`, and after **`B3①`** *(`simTime 116`: platoon `521.9·525.0·528.4·532.2` on the baseline, both targets `Health 0`, **0 faults**)*. ⚠ Positions vary ~1 m run to run — it is a live multi-node run, ⛔ **not a determinism check**. ⭐ Original record *(design §15.4: targets 0/0, platoon `523·525·529·531`, **0 faults**)*. ⛔ **But it does NOT close `O0`'s acceptance** — the scenario's one store has `SlotCount 0`, so no Instance ticked. ⛔ Green before **and** after every task |
| **`A1`** *(unify the slot key)* | ✅ **DONE** — `c99a8865d` |
| **`A2`** *(the resolution seam)* | ✅ **DONE** — `7a87596aa` + `7574f228d`. ⚠ See §4 for what was deliberately NOT collapsed |
| **`A3`** *(`Kind` + `H1` + `H2`)* | ✅ **DONE** — 4 rails, all red-proved. As-built folded into design **§13**'s `AS-BUILT` block |
| **`A4`** *(`O0`)* | ✅ **DONE** — `CgfLogicPack` owns the splice; walker filters on declared `Kind`; 3 rails red-proved. Scope: **CGF + editor** (user ruling). As-built in design **§6** |
| **`B1`** *(`O1`)* | ✅ **DONE** — `SquadCognitiveState` is its own component (id **270**), provisioned by `SquadStateProvisioning` from **both** roster creators. As-built in design **§6** |
| **`B2`** *(`O2`)* | ✅ **DONE** — `BrainInterrupts` is its own component (id **302**); `BrainBlackboard` is now **100 B of pure params** *(128 → 100: 28 dead bytes per brain entity)*. `R-39` reconciled, `R-41` superseded. As-built in design **§6** |
| **`A2b`** *(the emitter ladder)* | ✅ **DONE** — ⛔ **three** ladders, not two; golden diff shape **+252/−1234, net −982**, purely the collapse. As-built in design **§13** |
| **`B3①`** *(`O3a`, THE COLLAPSE)* | ✅ **DONE, golden GREEN** *(`25e158d81`)* — `BlueprintTierSpec` + `BlueprintTierTable`; **net −435 lines of C#**; 3 verbatim `TickTier_*` → 1, the quadratic `UpgradeTier` → 1 body, 3 byte-identical renderers → a generic base. ⛔ Ladder values UNCHANGED on purpose. 7 rails, red-proved. As-built in design **§17** |
| **`B3②`** *(`O3a`, THE RE-PICK)* | ✅ **DONE** — ladder **12 / 16 / 16**; `BlueprintTierLadder` is the one source, LINKED into the compiler ⇒ `Stage2_Validate`'s literals are gone and ⑪ is CLOSED. 🔴 Forced 4096 → 16 as well: a larger tier with FEWER slots makes promotion a capacity REDUCTION. 3 rails, red-proved. As-built in design **§17** |
| ⭐⭐⭐ **next** | **`B4`** *(`O3b` — the 256 tier)*. ⭐ Now genuinely additive: one entry in `BlueprintTierTable.Ascending`, one component struct, one id. ⛔ **APPENDED to `BlackboardTier`, never inserted** *(§2 ⑫)*. `MaxSlots` is PLAN `W4` — 📐 77 % of behaviours need ≤ 2 slots ⇒ **2** earns the tier, 1 makes it near-useless |
| ⚠ also open | `B4` *(`O3b` — the 256 tier; `MaxSlots` is PLAN `W4`, and 2 is the value that earns the tier)* |
| **defects filed** | `CE-295` open *(scenario live-reload is a one-shot — filed NOT fixed, user's call)* · `CE-296` **refuted** *(my error)* |

### 1.1 What increment `A` actually built

| | |
|---|---|
| 🆕 `Fdp.Toolkits/Behavior/Shared/OccurrenceSlotKey.cs` | ONE slot-key spelling, `internal`, netstandard2.0-subset, **LINKED** into `Hrot.AiEditor.Persistence` (the `BP-306` pattern — that assembly carries no project references by design). Replaced 3 copies across 2 enums. Adds `ComputeNested` for `(assetId, hostPath)` |
| 🆕 `Fdp.Toolkits/Blueprints/Partitioning/OccurrenceStoreAccess.cs` | ONE tier-resolution seam — `TryGetStore`, **`TryGetStoreReadOnly`**, `HasStore`, `GetStoreSize`, `TryResolveOccurrence`. Replaced 11 hand-rolled ladders |
| 🆕 `Fdp.Toolkits/Blueprints/Partitioning/OccurrenceKind.cs` | `Invalid=0 · Blueprint=1 · BTree=2 · Hsm=3`. **12 of 16 nibble values free** |
| ⭐ `BlueprintBlackboardPartitions` grew the nibble layer | `GetSlotKind` / `SetSlotKind` / `GetKindOf` / `TryGetSlotIndex` · `MaxKindSlots=16`, `MaxKind=0xF` · a `TryAttach` overload taking the kind *(the 5-arg one delegates with `Invalid`, so every existing call site compiles unchanged)* |
| 🔴 the two hazards closed | **`H2`** in `TryDetach` *(compact the nibbles in lockstep **and** clear the vacated tail)* · **`H1`** in `CopyToLargerTier` *(`dstHeader.Reserved = srcHeader.Reserved` — ⭐ ONE line covering all three promotion sites, because they all funnel through it)* |
| ⭐⭐ the five production attach sites DECLARE their kind | `BlueprintTickSystem:324`, `BlueprintInstanceService:161`, `BlueprintMaterializationSystem:140` *(`Blueprint`)*; `BehaviorIngressSystem` ×2, threaded from `def.BrainTier` via `ProvisionStatefulSlots → AttachManifestSlots → AttachSlotsToMemory`. ⇒ **`A4`/`O0`'s precondition is MET, not merely possible** |
| ⭐⭐ **`A4`** — the blueprint runtime reaches CGF | the splice moved to `Fdp.Toolkits/Blueprints/Systems/BlueprintRuntimeComposition.cs`; **`CgfLogicPack` performs it once** into its own `SimulationSystems`; the BeforeSync maintenance system rides a `SingleSystemModule` from `CgfCapabilities.Brain`; `BlueprintRegistry` is a **required** pack parameter; 🔴 the Editor's ROOT splice was **deleted** (`DistinctByType` runs before it, so keeping both = two tick systems); the walker filters on `GetSlotKind(...) == Blueprint` |
| rails | `OccurrenceSlotKeyParityTests` (13) · `OccurrenceStoreAccessTests` (11) · ⭐ `PartitionAllocatorTests` **`A3_R1..R4`** *(4, in the allocator's OWN suite per `R-142` ④ — ⛔ not a new class)* · ⭐ `CgfLogicPackTests` **`A4_R1..R3`** *(3, likewise in the pack's own suite)* |

---

## 2. 📐 GROUNDED FACTS — ⛔ **do not re-derive; these cost real measurement**

| # | fact | evidence |
|---|---|---|
| ① | **Unmanaged component storage is NATIVE memory, not GC heap** | `NativeChunkTable:44` → `NativeMemoryAllocator.Reserve` → `VirtualAlloc` (`WindowsVirtualMemoryBackend:35`) / `mmap` (`PosixVirtualMemoryBackend:55`); refs come from `GetRefRW:163` |
| ② | ⇒ **`fixed (byte* m = tier.Memory)` is a LANGUAGE FORMALITY, not pinning** | nothing to pin. A pointer may legitimately outlive the `fixed` block — ⭐ **this is what made the `A2` seam possible at all** |
| ③ | 🔴 **the real lifetime risk is CHUNK DECOMMIT and TIER SWAP** | `NativeChunkTable:272/:310`; promotion adds-larger-then-removes-smaller. ⇒ use a resolved pointer **within the call**; never store it across a frame |
| ④ | 🔴🔴 **`GetComponentRW` bumps the chunk version; `GetComponentRO` does not** | `GetRefRW:158-161` writes `_chunkVersions`; `GetRefRO:166-167` says "Does not update version". `EntityRepository.DeltaQuery` READS those versions ⇒ **RW-vs-RO is a behaviour difference, not a style choice** |
| ⑤ | **bytes per AI entity** | 192 B BTree root · 256 B HSM root · ⛔ **no heavy-DTO credit** ⇒ 256 tier free–1.33×, 1024 tier 4–5.3× ⇒ `O3b` is **load-bearing** |
| ⑥ | **slots per behaviour** (30 generated assets) | 0 ×17 · 1 ×6 · 2 ×5 · 3 ×1 · 8 ×1 ⇒ **77 % fit a 256 tier**; worst case `PlatoonHillAttack2` needs 9 ⇒ the `MaxSlots` ladder must be re-picked in `O3a`. ⚠ `PlatoonHillAttack2` is **NOT** the golden test's tree — `hill-attack-close` runs `PlatoonHillAttack` (hand-written nodes, 1 slot) |
| ⑦ | **`Blackboard1024` is attached to ZERO entities** in a live run | ⇒ §3.2's defect is **LATENT**, not shipped; gated on `HeavyDtoType`, which production sets nowhere |
| ⑧ | **AI entity count** | single-digit in every shipped scenario. ⚠ **No exercise-scale scenario exists in the repo** — say so, do not extrapolate |
| 🔴 ⑩ | ⛔⛔ **`MaxSlots` HAS A HARD CEILING OF 16, AND NOTHING SAID SO** | `A3`'s `Kind` nibble array is **4 bits × 16** in the header's 8-byte `Reserved` — an exact fit. A tier with more slots has slots whose kind **cannot be recorded**, and `BlueprintTickSystem` filters ON the kind ⇒ they are **silently skipped**, not rejected. ⭐ §5a's *"`MaxSlots` 12 leaves 800 B"* is safely inside it, but reads as if payload were the only constraint. ✅ Now a throw in `BlueprintTierSpec.For<T>` + rail `B3_R2` |
| 🔴 ⑪ | ⛔⛔ **A FOURTH LADDER LIVES IN THE COMPILER, AS LITERALS** | `Stage2_Validate.cs:503-508` spells the payload budgets **`928 / 3936 / 16096`** as integers, not as references to `BlueprintBlackboard*.PayloadSize`. ⚠ `Hrot.Blueprints.Compiler` targets `netstandard2.0;net8.0` and references `Fdp.Toolkits` **only under net8.0** ⇒ it *cannot* see the constants. 🔴 **Re-picking `MaxSlots` therefore desyncs compile-time validation from runtime capacity.** ⭐ Fix by the `A1`/`BP-306` precedent: a netstandard2.0-safe ladder file, LINKED |
| ⑫ | ⛔ **THREE `BlackboardTier` ENUMS** | `Fdp.Toolkit.Blueprints.BlackboardTier` · `Hrot.Blueprints.Core.Compiler.BlackboardTier : byte` · `BlackboardTierHint {Auto, Force1024, …}`. All ordinal ⇒ **`O3b`'s 256 tier must be APPENDED, never inserted** |
| ⑨ | ⭐ **the golden test exercises the HAND-WRITTEN node path, not the blueprint path** | so the `AiPrimitive` machinery this design is built around is **less exercised by it than assumed** — matters before `O4`/`O5` lean on it for proof |

---

## 3. ⭐⭐⭐ THE NEXT ACTION — **`B3②`** *(the `MaxSlots` re-pick)*

⭐ **`B3①` — the structural collapse — is DONE and green.** What is left of `O3a` is the ladder
VALUES, and `B3①` deliberately did not touch them so that *"did the refactor change behaviour?"* had
a provable answer *(design §17.6)*.

⛔⛔ **`B3②` is NOT the one-file constant change the PLAN made it look like.** Three constraints,
all measured while building `B3①`:

| | |
|---|---|
| 🔴 **the Kind-nibble CEILING** | ⛔ `MaxSlots` may not exceed **16** *(§2 ⑩)*. §5a's suggested **12** on the 1024 tier is inside it — but the ceiling was nowhere written down and payload arithmetic alone would not have found it. ✅ Now a throw plus rail `B3_R2` |
| 🔴 **the COMPILER's literals** | ⛔⛔ `Stage2_Validate.cs:503-508` hard-codes `928 / 3936 / 16096` and **cannot reference `Fdp.Toolkits` under `netstandard2.0`** *(§2 ⑪)*. ⇒ the re-pick ships a **LINKED netstandard2.0-safe ladder file** — the `A1`/`BP-306` precedent, already proven on this programme — or compile-time validation and runtime capacity silently disagree |
| ✅ **DOES IT MOVE REAL ASSETS? MEASURED `2026-09-20`: NO — the band is EMPTY** | 📐 `MaxSlots 12` drops the 1024 tier's payload **928 → 800**, so anything needing **801–928 B** would be promoted. **Measured in BOTH populations: zero.** Behaviour manifests max **320 B** *(`PlatoonHillAttack2`)*; blueprint Instances max **128 B**. The whole corpus sits **2.5× below** the new floor. ⭐⭐ And it is a large WIN: the worst case is promoted to 16384 today **on slot count alone**, with payload 50× below that tier — at `MaxSlots 12` it seats in **1024**, a **16×** reduction. ⚠ The root occurrence's own payload cannot be measured until `O4` — bounded at 80–184 B, and the conclusion holds across that whole range. 📄 design §17 |

⭐ **Then `B4`** *(`O3b`, the 256 tier)* — trivial now that the table exists: one entry in
`BlueprintTierTable.Ascending`, one component struct, one `GlobalComponentIds` id.
⛔ **APPENDED to `BlackboardTier`, never inserted** *(§2 ⑫: three ordinal enums spell this ladder)*.
`MaxSlots` is PLAN `W4`: 📐 **77 %** of behaviours need ≤ 2 slots ⇒ **2** is the value that earns the
tier; 1 makes it near-useless.

### 3.3 The working recipe (all three prior tasks used it, and it caught something every time)

1. ⭐⭐ **T-1 first** — run the feature's own suites and record the number BEFORE editing.
2. Make the change.
3. Re-run: the baseline number must be **identical** (or the delta explained).
4. ⭐⭐⭐ **Inverse-edit red-proof** — break the thing deliberately, confirm the rails redden, revert,
   re-verify. ⛔ A rail never seen red is not known to work.
5. Build the **test** project, not just production (§5 ⑦).

📌 **The suites, MEASURED `2026-09-20` at `A3`'s green** — ⭐ use these as the baseline, they are one
task old, not five:

| suite | baseline | note |
|---|---|---|
| `Fdp.Toolkits.Tests` full | **2232 / 0** | ⚠ `DEBT-AIB-030`: ~7 rotate flaky — confirm a red by re-running it ALONE |
| `Hrot.Blueprints.Tests` full | **~3970 / 0**, ⚠ **skips vary 10–18 — see below** | ⭐ **the allocator's OWN suite lives here** — `PartitionAllocatorTests`, and `A3`'s rails went INTO it |
| the allocator filter *(`PartitionAllocator`+`BlackboardLayout`+`TierSummary`)* | **40 / 0** *(36 before `A3`)* | ⭐ the ~8 s loop for anything touching partitions |
| `Hrot.Diagnostics.Breakpoints.Tests` | **165 / 0** | ⚠ needs `dotnet restore` first — trap ⑧ |
| `Hrot.Presentation.Tests` | **252 / 0** | ⚠ same |
| ⚠ `Hrot.SimHost.Tests` | **1001 / 3** | 🔴 **all three reds CONFIRMED PRE-EXISTING** at `5d3e632c2` by a stash-and-rerun: `NodeRolePersistenceRails.TheSaveHandlerSetIsStillComplete`, `MapPresentationParityRails.EveryTkbSpawningHost_ObtainsTheSharedTranslatorSet(EditorStrideSubsystem.cs)`, `FullBranchPipelineTests.BranchedRecording_CapturesHistoricalStateAsKeyframe`. ⚠ **A FOURTH is FLAKY, not a red** — `EcsRecordReplayControllerTests.PrepareRecordingAsync_InstallsRecordingModule` fired once and passed 3/3 alone + 2/2 in full runs after |
| `Hrot.AiEditor.Generators.Tests` | **280** | ⛔ this is the one `A2b` moves — goldens |

⛔⛔ **THE SKIP COUNT IN `Hrot.Blueprints.Tests` IS NOT A STABLE BASELINE — do not treat a change in it
as a finding without checking this first** *(measured `2026-09-20`, chasing an apparent 10 → 18 jump)*:

| | |
|---|---|
| 📐 **the 18 are fully accounted for** | **8** static `[Fact(Skip = …)]` + **10** from **`[SkippableFact]` / `Skip.If(…)`** — 21 such sites across 7 files *(`AllocationFreeTests`, `WhenNodePerfTests`, and the five `Editor/Frame/*` dialog suites)* |
| ⭐⭐ **`[SkippableFact]` skips at RUNTIME, on the ENVIRONMENT** | headless ImGui frame availability, GC monitoring. ⇒ **the count moves between machines and runs while the code is identical** |
| ⛔ **why this cost time** | a `grep "Skip *="` finds only the STATIC half — it returned **9** and looked like it explained everything. ⚠ The gate contract's *"a new skip is a finding"* is still right; ⭐ **it just needs `SkippableFact` in the search** |
| ✅ **verdict for `B3`** | `git status` on the test project returned **empty** *(B3 touched no test source there)*, and the count was **identical across two independent runs**. ⇒ **not a regression** |

---

## 4. ⚠ THE `A2` REMAINDER — **six sites deliberately NOT collapsed**

⛔ **These are not missed work.** Each is a different shape and each now carries a comment in the
source saying so. ⭐ Anyone "finishing A2" by collapsing them would introduce a behaviour change.

| site | why it stays |
|---|---|
| `BlueprintInstanceService` ×3 | tier-**DISCRIMINATING** — returns `BlackboardTier` and searches per tier. Needs a tier-aware overload |
| `EntityBlueprintsEditModel.GetCurrentTier` | answers `B1024` for an entity with **NO** store; the seam answers `0`. Different question, different answer |
| `EntityBlueprintsPanel` | the tier **UPGRADE** (add larger → copy → remove smaller). ⭐ One of the three promotion sites `O3a`/`H1` restructure — leave it for them |
| `BlueprintMaterializationSystem` | `EnsureTierComponent` switch — *"make sure THIS tier exists"*, not a resolution |
| `BlueprintDebugSession` | resolves through **`ISimulationView`**, not `EntityRepository` ⇒ needs a second seam overload. A deliberate decision, not a mechanical edit |
| ⛔ ~~`BTreeBridgeEmitCore` ×2~~ **RESOLVED by `A2b`** — and it was ×**3** | **the EMITTER** — the only genuine remaining duplication, and the one that multiplies into every generated assembly. ⛔ Changing it **moves the generated goldens**, so it needs its own increment with the movement reported as a **DIFF SHAPE** (gate row 3), not folded into another task. ⭐ Call it **`A2b`** |

---

## 5. ⛔⛔ THE TRAPS THIS PROGRAMME HAS PAID FOR

⭐ Sixteen of these were **my own errors**; three reached a pushed document before being caught. They are
here as checkable habits, not confessions. ⚠ **⑯ is not mine** — it is a defect in the tooling that
enforces the rules, and it is here because believing its banner would have produced a grep-only answer.

| # | trap | ⭐ the habit |
|---|---|---|
| **①** | 🔴🔴 **"WHERE THEY STARTED" IS NOT "WHERE THEY BELONG."** I read the platoon's `t=0` **spawn** as the baseline, called a passing golden test broken, filed `CE-296` and **blocked the programme on it** — then refuted it the same session | ⛔ resolve the **AUTHORED** value (`behaviorParams.baselineStart/End`), never a `t=0` reading. ⚠ A thing moving TOWARD your "failure" position is **arriving** |
| **②** | 🔴 **ONE DATA POINT IS NOT A DISCRIMINATOR.** A Windows run succeeded; I declared the defect "mode-specific" **without running the other mode** | ⭐ run the A/B on one box before naming a discriminator |
| **③** | 🔴 **A GREP IS NOT A CENSUS.** `\[HsmGuard\]` with a literal `]` missed every `[HsmGuard(Name = …)]` ⇒ I called a correct census "fabricated". 5 lines vs the real 15 | ⛔ anchor on the attribute NAME (`\[HsmGuard`), and **open the lines** before counting |
| **④** | ⚠ **THE DISCONFIRMING EVIDENCE WAS ALREADY IN THE RUN.** I proposed a test for a hypothesis the *succeeding* phase of the same run had already falsified | 🔒 when a hypothesis predicts a failure, check whether the SAME mechanism visibly **succeeded** elsewhere in that run |
| **⑤** | 🔴 **REASONING FROM FOLKLORE INSTEAD OF THE ALLOCATOR.** I argued `ref byte` was "safer because a ref is GC-tracked and survives compaction". There is no compaction — the storage is native. The conclusion flipped once I actually read it | ⛔ when the argument is about **memory semantics**, read the allocator. It is two greps |
| **⑥** | ⚠ **`ok:true` IS NOT A LOAD** | ⭐ read `sawWorldChange`, verify with `GET /entities`. This is `CE-295` |
| **⑦** | ⚠ **STALE BINARY, TWICE** | ⭐ build the **TEST** project (a test project's build copies production into its bin; the reverse does not happen), and check the dll timestamp when a result looks too clean |
| **⑧** | ⚠ **unrestored project** ⇒ `NETSDK1004` / *"the argument …dll is invalid"* | ⭐ `dotnet restore <proj>` first; this is not a code error |
| ⭐ **⑨** | ⚠ **A RAIL THAT CANNOT GO RED IS NOT A RAIL — and the inverse edit is what EXPOSES that.** `A3_R3`'s first draft iterated `Enum.GetValues` and skipped on `kind == Invalid`; the inverse edit it was written for *(a real kind renumbered onto 0)* would have made that `continue` **skip the very case being checked**. ⭐ Found only by trying to redden it, then fixed to iterate **NAMES** | 🔒 **never mark a rail done on a green** — run the inverse edit, and if it will not redden, **the rail is the defect** |
| 🔴 **⑬** | ⛔⛔ **HOOK THE FACT, NOT A WRITE PATH — and a GREEN GOLDEN TEST WILL HIDE IT.** `B1` first provisioned squad state inside `UnitHierarchySystem`'s assign handler, *"the one system that establishes the commander relationship"*. 📐 It is not: `GenesisMaterializationSystem:180` builds a commander's `UnitRoster` independently for scenario-loaded hierarchies ⇒ the live commander came up with **no** squad state, **while the golden test passed** | ⭐ provision against the **invariant** (*"owns a `UnitRoster`"*), through ONE helper both creators call. ⛔ And when a feature is meant to be ON, **read the entity** — the gate cannot answer *"is it on?"* |
| ⚠ **⑭** | 🔴 **AN ID CENSUS MUST READ EVERY `*Ids*.cs`, NOT JUST `GlobalComponentIds.cs`.** I took the next free id in the squad block's comment and collided with `NavigationContractsComponentIds.CrowdMotorIntent = 265`. ⚠ **It passed in isolation and failed only in the full suite** — `ComponentTypeRegistry` is process-global *(the `QA-008` shape)*. 📐 The census then found **three PRE-EXISTING collisions** (262/263/264) — filed as `QA-037` | ⭐ `grep -rn 'public const int' --include=*Ids*.cs` across the repo, sort numerically, **then** pick. ⛔ Never trust a block comment — the one here was stale by four ids |
| ⭐ **⑪** | 🔴 **A DUPLICATION CENSUS MUST COUNT *CALLS*, NOT OCCURRENCES OF THE PATTERN.** `A2` reported *"2 copies emitted by `BTreeBridgeEmitCore` (`:650`, `:726`)"* and the PLAN inherited it. 📐 **There were THREE**: `EmitStatefulDeactivatorTierBlock` is **parameterised per tier** — one helper, three calls — so it emits a full ladder while matching no grep for the ladder's shape | ⭐ after censusing a pattern, grep for the **tier constants** (`BlueprintBlackboard16384`) and for helpers **called once per tier**; a parameterised emitter hides in plain sight |
| ⭐ **⑫** | ⚠ **I RE-PAID TRAP ⑦ WHILE REGENERATING GOLDENS.** I built `Hrot.AiEditor.Persistence` and then ran `dotnet test <generators.Tests> --no-build` to regenerate — so the goldens were written from the **stale Persistence dll in the TEST project's bin**, and the next real run reddened them | ⛔ **regeneration is a test run: build the TEST project first**, then `--no-build`. ⭐ Check `ls -l <tests>/bin/*/Production.dll` against the edit time when a regenerated golden looks wrong |
| ⭐ **⑩** | ⚠ **EVERY public enum in `Fdp.Toolkits` is swept into generated IDL**, and `idlc` **refuses duplicate enumerator values** ⇒ some "invariants" are already enforced by the build and your rail may be guarding something free. ⭐ **The CAUSE is a RULE, not a quirk:** `Fdp.Toolkits.csproj:70` references `CycloneDDS.NET`, and the generator's discovery rule is *"`[DdsTopic]`/`[DdsStruct]`/`[DdsUnion]` **OR is an enum**"* (`targets:75-76`) ⇒ **sharing an assembly with wire types is enough.** ⛔ **A generated `.idl` is NOT evidence a type is on the wire** — nothing `#include`s these. 📄 design §13; filed as `QA-035` | ⛔ **when an inverse edit fails to COMPILE rather than redden, do not stop at the observation — find the RULE.** 📌 I first wrote this up from ONE sample and called the sweep "indiscriminate"; the tool states the rule in its own targets file |

| 🔴 **⑮** | ⛔⛔ **SPLITTING A COMPONENT SPLITS ITS AUTHORITY — and only a rail said so.** `B2` moved the interrupt bytes out of `BrainBlackboard` into `BrainInterrupts`; `CognitiveRuntimeModuleTests.WithTheGateOn_AnUnownedBrainIsNeverTouched` reddened because the gate keys on **the component the system reads**, and authority was still granted only for `BrainBlackboard` ⇒ the gate stopped discriminating and an unowned brain WAS touched. ⚠ Not live today *(`gateOnAuthority` is `false` on every host)*, which is precisely why nothing else would have caught it | ⭐ when you split a struct, enumerate **every per-component set the old type was a member of** — authority grants, replication masks, `DataPolicy`, registration paths — and decide for each. ⛔ "It compiles and the suites pass" answers none of them |
| ⚠ **⑯** | 🔴🔴 **`scripts/find.sh`'s GRAPH HALF WAS SILENTLY DEAD** — it parsed `cli list_projects` as JSON while this CLI build prints a **human-readable TABLE**, so `PROJ` came back empty and it printed *"NO INDEXED PROJECT"* on a **fully indexed repo**, every call. ⚠ The identical defect had already been found and fixed for `search_code` *(2026-09-12, comment still in the file)* — one call earlier in the same script. ⇒ the tool that exists to enforce graph-before-grep was **advertising the graph as unavailable** | ⭐ **fixed `2026-09-20`** — the parser now understands the MCP envelope, bare JSON **and** the table. ⛔ **When a tool reports its own unavailability, verify that against the tool itself** *(`… cli list_projects` takes one second)* before accepting a grep-only answer — an "UNAVAILABLE" banner is a claim, not a measurement |

| 🔴 **⑰** | ⛔⛔ **I QUOTED A TRUNCATED SEARCH PAGE AS A CENSUS.** `search_code("BlueprintBlackboard16384")` printed `files: 37` — and, in the same result, **`results_returned: 100`, `total_results: 129`, `has_more: true`**. 📐 The real figure is **75 files**. ⇒ the `B3` site table was built from one page and **missed three real ladders**; all three surfaced only from a full-solution build. ⚠ `CLAUDE.md` names this exact trap *("`limit` defaults to 10 — a truncated page looks exactly like a small answer")* and it was still paid | ⭐ **read `has_more` / `total_results` BEFORE quoting a count**, and for a whole-repo census prefer `grep -rln`, which cannot paginate. ⛔ A file list is not a census unless the result says it is complete |
| 🔴 **⑱** | ⛔⛔ **A FOREGROUND BUILD RACING A BACKGROUND ONE MAKES `--no-build` RUN A BINARY THAT SILENTLY OMITS YOUR NEW TESTS.** 📐 Measured: the test `.dll` was stamped **13:31:16**, the `.cs` holding 7 new rails **13:31:12** ⇒ the incremental check saw the dll as newer and **skipped the compile**. `dotnet build` said *"Build succeeded"*, `dotnet test --no-build` said *"Passed! 11"* — and **11 was exactly the old rail count.** ⚠ A green with a suspiciously round number is the only symptom; nothing errors. ⭐ Fixed by `--no-incremental` | ⭐⭐ **after adding tests, check the COUNT went up by what you added.** ⛔ `strings <dll> \| grep <NewTestName>` settles it in a second. ⚠ This is the third face of the stale-binary trap *(⑦ failed build, ⑫ wrong project, ⑱ skipped compile)* — ⇒ 🔒 **do not run a foreground build of a project a background job is also building** |
| ⚠ **⑲** | 🔴 **`git stash` WHILE A BACKGROUND BUILD OR TEST RUNS CORRUPTS IT.** I stashed to measure a baseline while a suite was running in the background; the stash left untracked NEW files in place, so the "baseline" build failed with 10 errors, produced a broken dll, **and poisoned the concurrent run** — whose result then had to be thrown away | ⛔ **never stash with work in flight.** ⭐ For a baseline, use `git worktree add` — it is isolated by construction — or measure the property from the SOURCE instead: 📌 here the question *"did skips change?"* was answered by `git status <test project>` returning **empty**, proving `B3` touched no test source, in one command and no build |

| 🔴 **⑳** | ⛔⛔ **A CONSTANT-CHANGE TASK MUST GREP THE TEST TREE FOR THE OLD VALUES — reasoning about production content is NOT enough.** `B3②` re-picked the `MaxSlots` ladder and I pre-measured the displaced **production** population *(empty, correctly)*. 📐 **Three tests reddened anyway**, in three files, all the same defect: each had encoded the ladder's NUMBERS rather than the property it protects — a `900`-byte fixture against a 928-byte payload, an assertion on a specific tier COMPONENT, and `112/112/112/496` filling 928 exactly. ⚠⚠ **Two of the three failed while BUILDING their scenario**, before reaching their own assertion — a pre-condition `Assert.True`, which reads like a broken test rather than a moved constant | ⭐⭐ before changing a constant, **`grep` the repo for its VALUE** *(`928`, `3936`, the literal slot sizes)*, not just for its NAME — a fixture that hard-codes `900` mentions neither. ⭐ Then rewrite each hit to DERIVE from the constant, so the next move is free. ⛔ *"I measured the production impact"* is a different claim from *"nothing references the old number"* |

⭐ **And three operational ones, all re-paid despite being in the runbook:**
⛔ `127.0.0.1` 404s on **every** route — `HttpListener` binds the hostname; use `localhost` (§2.1) ·
⛔ `pkill -f '<pattern>'` matches **your own command line** and kills the shell (exit 144) — use a PID
loop · ⛔ never pipe a long-running script through `tail` (it buffers everything to the end).

---

## 6. ⛔ DONE — do not redo

| | commit |
|---|---|
| design finalized — `H1`/`H2`/`D3` folded, slots column, corrected C1, §15 live-run record, §16 checklist | `3ca68dbd2` |
| §3.2 corrected from **shipped** to **LATENT** | `bfb0baf53` |
| `CE-295` filed *(not fixed)*; `CE-296` filed then **refuted** | `17feaf94a`, `e3b04dc47` |
| the PLAN | `86ecdac0b` |
| debug-API log fix — editor line through `FdpLog`, phrase matched to the cluster's | `e865f59c8` |
| **`A1`** — one slot-key spelling + the nested form | `c99a8865d` |
| **`A2`** — the seam + 11 adoptions | `7a87596aa`, `7574f228d` |
| **`A3`** — `OccurrenceKind` nibble array + `H1` + `H2` + 4 red-proved rails + 5 declaring attach sites | `c6048b59c` |
| **`A4`** — the blueprint runtime reaches CGF; the walker filters on the declared `Kind` | `10d55a787` |
| **`A2b`** — the emitted tier ladder collapses to the seam, in all **three** emitters | `f2d5849a1` |
| **`B1`** — `SquadCognitiveState` becomes its own component (id **270**) | `85a5cc2d0` |
| **`B2`** — `BrainInterrupts` split out (id **302**); `BrainBlackboard` is 100 B of pure params | `94fc4c9f1` |
| `QA-035` *(IDL enum sweep)* · `QA-036` *(Health divergence)* · `QA-037` *(3 pre-existing component-id collisions)* filed for the **backend** lane | `a03243084`, `8a79de684`, `85a5cc2d0` |
