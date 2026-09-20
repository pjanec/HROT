<!--STATUS
state: LIVE
doc-type: LANE RESUMPTION for the `behaviors` lane — programme ②, OCCURRENCE-SCOPED STORAGE.
  ⚠ A STATE doc, not canon. Every "green"/"pushed"/"HEAD" line is a snapshot dated below.
  ⛔ VERIFY against git before acting ("THE LEDGER MAY NOT ASSERT WHAT THE CODE IS").
updated: 2026-09-20
build-state: n/a — a resumption snapshot, not a design.
current-answer: §3 — the NEXT ACTION is task A3 (Kind + H1 + H2). A1 and A2 are DONE and pushed.
  §2 is the grounded facts: ⛔ do not re-derive them, they cost real measurement. §5 is the trap
  list, and it is the section most worth two minutes — six of these were MY errors, three of which
  reached a pushed document before being caught.
stale-below: nothing — rewritten 2026-09-20 after A1/A2 landed.
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
> ⭐ **Nothing is half-finished.** `A1` and `A2` are committed, pushed and green; the tree is clean.
> The next action is `A3`, and §3 says exactly how to start it.

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
| ⭐⭐⭐ **next** | **`A3`** — `Kind` + `H1` + `H2`. §3 |
| **defects filed** | `CE-295` open *(scenario live-reload is a one-shot — filed NOT fixed, user's call)* · `CE-296` **refuted** *(my error)* |

### 1.1 What `A1` and `A2` actually built

| | |
|---|---|
| 🆕 `Fdp.Toolkits/Behavior/Shared/OccurrenceSlotKey.cs` | ONE slot-key spelling, `internal`, netstandard2.0-subset, **LINKED** into `Hrot.AiEditor.Persistence` (the `BP-306` pattern — that assembly carries no project references by design). Replaced 3 copies across 2 enums. Adds `ComputeNested` for `(assetId, hostPath)` |
| 🆕 `Fdp.Toolkits/Blueprints/Partitioning/OccurrenceStoreAccess.cs` | ONE tier-resolution seam — `TryGetStore`, **`TryGetStoreReadOnly`**, `HasStore`, `GetStoreSize`, `TryResolveOccurrence`. Replaced 11 hand-rolled ladders |
| rails | `OccurrenceSlotKeyParityTests` (13) · `OccurrenceStoreAccessTests` (11) |

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

## 3. ⭐⭐⭐ THE NEXT ACTION — **task `A3`**

⭐ **Why `A3` and not the `A2` remainder:** the six un-collapsed sites are **different shapes, not
unfinished work** (§4), and the only genuine remainder — the emitter pair — carries **golden
regeneration**, which is a gate of its own and deserves a clean start rather than the tail of a long
session. ⛔ `A3` is on the critical path: `O0` cannot land without its declared `Kind`, and `H1` is a
live hazard that must ship **with** `Kind` or promotion silently zeroes the nibble array in the gap.

### 3.1 What `A3` is

**`Kind` as a nibble per slot in `OccurrenceStoreHeader.Reserved`** (`D1′`), plus `H1` and `H2`.
📐 The exact fit: `Reserved` is `ulong` — **8 B = 16 slots × 4 bits** (`BlueprintBlackboardHeader.cs:24`),
and 16 is the largest `MaxSlots`. ⛔ NOT in `BlueprintSlotEntry` (`Size = 16`, fully packed); ⛔ NOT in
the payload (its head is the shipped 16-byte `BlueprintLatentCursor`).

### 3.2 🔴 THREE RED-FIRST RAILS — each pins a different way the nibble array dies

| # | rail | the code it guards |
|---|---|---|
| ① | **detach compacts in lockstep** | `TryDetach:188-199` dense-compacts (last entry → the hole) ⇒ nibbles must move with it, **and clear the vacated tail** (`:197-198` clears the duplicated entry but cannot reach the header) |
| ② | 🔴🔴 **promotion preserves `Reserved`** | `CopyToLargerTier:249` calls `Initialize`, which `InitBlock`s the component (`:38`) and sets **eight** header fields (`:44-51`) — ⛔ `Reserved` is not among them; `:272-290` then copy `SlotCount`/`PayloadFree`/`PayloadHighWater`/free-list **and nothing else**. ⇒ **every tier upgrade zeroes the whole array.** ⭐ Slot ORDER is preserved (`i → i`), so the fix is ONE line beside `:272` and it covers all **three** promotion sites |
| ③ | **`Kind == 0` is `Invalid`** | `Initialize:38` zeroes the component, so 0 is what an un-migrated or never-written slot reads. ⛔ If 0 meant `Blueprint`, `O0`'s walker would resume filtering **by accident** — the hash-miss filter `D1′` exists to retire |

⛔⛔ **Write all three RED first.** A zeroed nibble array is indistinguishable from "everything is
kind 0", which is exactly why this needs a failing test before a passing one.

### 3.3 The working recipe (both prior tasks used it, both times it caught something)

1. ⭐⭐ **T-1 first** — run the feature's own suites and record the number BEFORE editing.
2. Make the change.
3. Re-run: the baseline number must be **identical** (or the delta explained).
4. ⭐⭐⭐ **Inverse-edit red-proof** — break the thing deliberately, confirm the rails redden, revert,
   re-verify. ⛔ A rail never seen red is not known to work.
5. Build the **test** project, not just production (§5 ⑦).

📌 **The suites:** `Fdp.Toolkits.Tests` full = **2232** · the stateful/shared-state/parity filter =
**32** · `Hrot.SimHost.Tests` Blueprint filter = **36** · `Hrot.Blueprints.Tests`
EntityBlueprints/TierSummary/InstanceService = **31** · `Hrot.AiEditor.Generators.Tests` = **280**.
⚠ `DEBT-AIB-030`: ~7 tests in `Fdp.Toolkits.Tests` rotate flaky — confirm a red by re-running it
alone before believing it.

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

⭐ Six of these were **my own errors**; three reached a pushed document before being caught. They are
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
