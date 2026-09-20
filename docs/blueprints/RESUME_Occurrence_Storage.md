<!--STATUS
state: LIVE
doc-type: LANE RESUMPTION for the `behaviors` lane — programme ②, OCCURRENCE-SCOPED STORAGE.
  ⚠ A STATE doc, not canon. Every "green"/"pushed"/"HEAD" line is a snapshot dated below.
  ⛔ VERIFY against git before acting ("THE LEDGER MAY NOT ASSERT WHAT THE CODE IS").
updated: 2026-09-20
build-state: n/a — a resumption snapshot, not a design.
current-answer: §3 — the NEXT ACTION is task B1 (`O1`: SquadCognitiveState gets its own component).
  A1, A2, A3 and A4 are DONE and pushed; A2b is open and is the smaller alternative start. §2 is the grounded facts: ⛔ do not re-derive them, they cost
  real measurement. §5 is the trap list, and it is the section most worth two minutes — six of these
  were MY errors, three of which reached a pushed document before being caught.
stale-below: nothing — §3 rewritten 2026-09-20 after A4 landed.
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
> is clean. Increment **A is complete bar `A2b`**. §3 says how to start what is next.

## 0. ⭐ FIRST MOVES

```bash
python3 scripts/session-design-brief.sh          # ledger · digest · probe · 3 random rulings
# then read docs/blueprints/RULINGS.md IN FULL   (RULE ZERO)
git fetch origin && git log --oneline -3 origin/behaviors   # snapshot head: 7574f228d
python3 scripts/rulings-check.py && python3 scripts/design-digest.py --check
```

⚠ **Snapshot `2026-09-20`:** `rulings-check` **37/37** · `design-digest --check` OK (68 docs).
⛔ `tracker-counts --check` says NOTHING about our rows — **it counts only `BP-` rows** (`CE-073`),
and ours are `CE-`. Do not quote it as evidence for them.

---

## 1. WHERE THE PROGRAMME STANDS

| | |
|---|---|
| **design** | ✅ FINALIZED — [`DESIGN_Occurrence_Scoped_Storage.md`](DESIGN_Occurrence_Scoped_Storage.md), start at **§16** |
| **plan** | ✅ WRITTEN — [`PLAN_Occurrence_Storage_Build.md`](PLAN_Occurrence_Storage_Build.md), 14 tasks / 5 increments |
| ⭐ **golden test** | ✅ **GREEN** and usable as the gate — `hill-attack-close`, both targets destroyed, all 4 back on baseline, reproduced **3×**. ⛔ Green before **and** after every task |
| **`A1`** *(unify the slot key)* | ✅ **DONE** — `c99a8865d` |
| **`A2`** *(the resolution seam)* | ✅ **DONE** — `7a87596aa` + `7574f228d`. ⚠ See §4 for what was deliberately NOT collapsed |
| **`A3`** *(`Kind` + `H1` + `H2`)* | ✅ **DONE** — 4 rails, all red-proved. As-built folded into design **§13**'s `AS-BUILT` block |
| **`A4`** *(`O0`)* | ✅ **DONE** — `CgfLogicPack` owns the splice; walker filters on declared `Kind`; 3 rails red-proved. Scope: **CGF + editor** (user ruling). As-built in design **§6** |
| ⭐⭐⭐ **next** | **`B1`** *(`O1`)* — `SquadCognitiveState` gets its own typed component. ⚠ **`A2b`** *(the emitter pair — moves goldens)* is still open and can go first if you prefer a smaller start |
| ⚠ also open | **`A2b`** *(the emitter pair — moves goldens)* · `B2`–`B4` *(rest of increment B)* |
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
| ⑨ | ⭐ **the golden test exercises the HAND-WRITTEN node path, not the blueprint path** | so the `AiPrimitive` machinery this design is built around is **less exercised by it than assumed** — matters before `O4`/`O5` lean on it for proof |

---

## 3. ⭐⭐⭐ THE NEXT ACTION — **`B1`** *(`O1`)*, or **`A2b`** for a smaller start

⭐ **Increment `A` is complete except `A2b`.** Two honest options, and they do not block each other:

| | |
|---|---|
| ⭐⭐ **`B1`** *(`O1`)* — `SquadCognitiveState` gets its own typed component | removes the **largest non-AI consumer** of `Blackboard1024`. ⭐ **A pure win even if the rest of the programme is cancelled**, which is why the design sequences it early |
| ⚠ **`A2b`** — the emitter pair (`BTreeBridgeEmitCore:650, :726`) | the only genuine remaining duplication, and the one that **multiplies into every generated assembly**. ⛔ It **moves the generated goldens**, so the movement must be reported as a **DIFF SHAPE** against `Hrot.AiEditor.Generators.Tests` (280) — its own gate, which is why it was split out rather than folded into `A2` |

🔴 **What `A4` taught, and `A2b` will hit the same wall:** the `Kind` filter reddened **120 tests**
because harnesses attach through the allocator directly and kept the kind-less overload. ⇒ ⭐ **any
code path that attaches a slot must DECLARE its kind** — including generated code, which is exactly
what `A2b` regenerates. ⛔ Do not "fix" such a red by defaulting the overload to `Blueprint`: that
rebuilds the *"undeclared means blueprint"* accident `D1′` exists to retire.

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
| `Hrot.Blueprints.Tests` full | **3978 / 0**, 10 skipped | ⭐ **the allocator's OWN suite lives here** — `PartitionAllocatorTests`, and `A3`'s rails went INTO it |
| the allocator filter *(`PartitionAllocator`+`BlackboardLayout`+`TierSummary`)* | **40 / 0** *(36 before `A3`)* | ⭐ the ~8 s loop for anything touching partitions |
| `Hrot.Diagnostics.Breakpoints.Tests` | **165 / 0** | ⚠ needs `dotnet restore` first — trap ⑧ |
| `Hrot.Presentation.Tests` | **252 / 0** | ⚠ same |
| ⚠ `Hrot.SimHost.Tests` | **1001 / 3** | 🔴 **all three reds CONFIRMED PRE-EXISTING** at `5d3e632c2` by a stash-and-rerun: `NodeRolePersistenceRails.TheSaveHandlerSetIsStillComplete`, `MapPresentationParityRails.EveryTkbSpawningHost_ObtainsTheSharedTranslatorSet(EditorStrideSubsystem.cs)`, `FullBranchPipelineTests.BranchedRecording_CapturesHistoricalStateAsKeyframe`. ⚠ **A FOURTH is FLAKY, not a red** — `EcsRecordReplayControllerTests.PrepareRecordingAsync_InstallsRecordingModule` fired once and passed 3/3 alone + 2/2 in full runs after |
| `Hrot.AiEditor.Generators.Tests` | **280** | ⛔ this is the one `A2b` moves — goldens |

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
| ⭐ **`BTreeBridgeEmitCore` ×2** | **the EMITTER** — the only genuine remaining duplication, and the one that multiplies into every generated assembly. ⛔ Changing it **moves the generated goldens**, so it needs its own increment with the movement reported as a **DIFF SHAPE** (gate row 3), not folded into another task. ⭐ Call it **`A2b`** |

---

## 5. ⛔⛔ THE TRAPS THIS PROGRAMME HAS PAID FOR

⭐ Seven of these were **my own errors**; three reached a pushed document before being caught. They are
here as checkable habits, not confessions.

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
| ⭐ **⑩** | ⚠ **EVERY public enum in `Fdp.Toolkits` is swept into generated IDL**, and `idlc` **refuses duplicate enumerator values** ⇒ some "invariants" are already enforced by the build and your rail may be guarding something free. ⭐ **The CAUSE is a RULE, not a quirk:** `Fdp.Toolkits.csproj:70` references `CycloneDDS.NET`, and the generator's discovery rule is *"`[DdsTopic]`/`[DdsStruct]`/`[DdsUnion]` **OR is an enum**"* (`targets:75-76`) ⇒ **sharing an assembly with wire types is enough.** ⛔ **A generated `.idl` is NOT evidence a type is on the wire** — nothing `#include`s these. 📄 design §13; filed as `QA-035` | ⛔ **when an inverse edit fails to COMPILE rather than redden, do not stop at the observation — find the RULE.** 📌 I first wrote this up from ONE sample and called the sweep "indiscriminate"; the tool states the rule in its own targets file |

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
| **`A3`** — `OccurrenceKind` nibble array + `H1` + `H2` + 4 red-proved rails + 5 declaring attach sites | *(this run)* |
