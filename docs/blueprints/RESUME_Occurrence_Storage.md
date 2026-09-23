<!--STATUS
state: LIVE
doc-type: THE resumption doc for the `behaviors` lane — programme: OCCURRENCE-SCOPED STORAGE.
  ⚠ A STATE doc, not canon. Every "green"/"pushed"/"HEAD" line is a snapshot dated below.
  ⛔ VERIFY against git before acting ("THE LEDGER MAY NOT ASSERT WHAT THE CODE IS").
updated: 2026-09-23
build-state: ✅ **NOTHING IS IN FLIGHT.** `O7c` is COMPLETE — ①②③④a④b④c④d all DONE and pushed,
  golden passes (§31.19.8) — and `CE-325` *(SelectTier → CheckTierBudget)* is DONE, gated and red-proved.
  ⚠ §0 records `CE-325` because it MOVED THE TIER NUMBERS §9.4 and several rails quote; the durable
  account is `DESIGN_Occurrence_Scoped_Storage.md` §31.24.
current-answer: ⭐⭐⭐ START AT §2 "WHAT IS LEFT IN THE LANE". §1 is the standing account of `O7c`;
  §0 is `CE-325` and is only needed if you are about to reason about HSM INSTANCE TIER WIDTHS.
  📐 VERIFIED 2026-09-23 by grepping for the struct declarations, not from memory:
     BrainBlackboard / Blackboard1024  ✅ DELETED (P4)
     BrainBTreeState                   ✅ DELETED, id 31 burned _RESERVED
     BrainHsm64                        ✅ DELETED, id 35 burned _RESERVED
     BrainHsm128                       ✅ DELETED, id 36 burned _RESERVED   ⇐ O7c-④d
  ⭐ NO ROOT BRAIN COMPONENT REMAINS. BehaviorState.BrainTier is the only discriminator, and
    BTreeTickSystem + HsmTickSystem<T> are ONE BrainTickSystem.
  ⛔⛔ DO NOT re-derive any design. §31.14 has the merge with three UML diagrams; §31.15–§31.19 are
    the as-built for ④a/④b/④c, the HSM inertia bug, and ④d.
stale-below: nothing. ⚠ §5's GATE BASELINES were measured at the ④d commit and §0 supersedes two of
  them: `Fdp.Toolkits.Tests` is now 2328 (was 2315) and `Fhsm.Tests` 307 (was 300). Re-verify, do not quote.
known-rot: nothing.
known-conflict: none.
related-designs:
  - DESIGN_Occurrence_Scoped_Storage.md — ⭐ THE OWNING DESIGN. §30 = P4, §31 = O7c end to end.
  - PLAN_Occurrence_Storage_Build.md — the task ladder; row E4 carries O7c's re-rating.
  - Blueprint_Issues_Tracker.md — CE-320 (O7c, DONE) · CE-325 (SelectTier tiers, DONE) · CE-321 (example rot, ② open)
    · CE-322 (inertia, DONE) · CE-318 (deferred) · CE-300/301 (open).
  - RUNBOOK_Cluster_Debugging_Over_Http.md — how to run the golden. §1.1 and §4 are load-bearing.
-->

# RESUMPTION — **occurrence-scoped storage**, `behaviors` lane

RELEARN

---

## 0. ✅ `CE-325` IS DONE — **`SelectTier` now defers to `CheckTierBudget`** *(`2026-09-23`)*

> ⭐ **Nothing is in flight.** This section records the slice that WAS in flight at the last
> compaction; it is kept because the next reader of §9.4's tier table needs to know the numbers moved.
> ⛔ The durable account is 📄 `DESIGN_Occurrence_Scoped_Storage.md` **§31.24**, not this section.

🔴 **What it was.** `HsmInstanceManager.SelectTier` and `HsmValidator.CheckTierBudget` were **two
tables of one fact and disagreed on every row** — regions, history, and timers, which `SelectTier`
never looked at at all. ⇒ a 3- or 4-region machine that fits `HsmInstance128` exactly was sent to
**256**, and a 9-region machine got a 256-byte instance that **cannot hold it**, with nothing
refusing it. ⚠ `CheckTierBudget` was called from FastHSM's own tests **and nowhere else**.

🔒 **User asked for:** *"route `SelectTier` through `CheckTierBudget` and wire the check in."*

| ✅ done | |
|---|---|
| layout limits come from `CheckTierBudget` alone; tier 3 **throws** rather than returning a size that cannot hold the machine | `Fhsm.Kernel/HsmInstanceManager.cs` |
| **7 new rails** | `Fhsm.Tests` **307/307** *(baseline 300/300)* |
| **3 premise corrections** — `O7_R48` → 128/128/4, `O7_R44` + `O7_R54` `wide` blob 3 → **5** regions | `Fdp.Toolkits.Tests` **2328/2328** · `Hrot.Editor.Tests` **423/424, 1 skipped** |
| docs | §31.24 *(as-built + flowchart)*, §9.4 *(gate-vs-capacity columns)*, §31.17.2 *(corrected)*, tracker `CE-325` + corrections to `CE-320` / `CE-324` |
| ✅ **red-proof** | `SelectTier` reverted **in full** ⇒ 5 of 7 rails red + `O7_R48` red; §31.24.7 says why the other two are green by construction and still load-bearing |

⚠⚠ **THE ONE JUDGEMENT CALL, MEASURED NOT CHOSEN — do not "simplify" it away.** Tier 1 keeps an
explicit `regions <= 1` instead of deferring to `CheckTierBudget(64)`. ⛔ The 64-byte layout holds
**two** regions and the budget check accepts them — but **64 is the only tier with NO RESERVED
INTERRUPT SLOT**. 📐 Routing tier 1 through the budget check alone dropped every 2-region machine to
64 and **reddened 9 rails, `CE-324`'s two included**. 🔒 **General shape: a layout check alone is not
sufficient, because a tier differs from its neighbours in more than its byte counts.**

⚠ **One method note worth keeping.** `O7_R48` spelled the old answer in **THREE** places — the
`SelectTier` guard, the provisioned `size`, and the leaf-array capacity — and the first edit changed
only the guard. ⇒ the suite caught it; a follow-up sweep of **every `SelectTier` caller in the tree**
found no fourth. 🔒 **Same shape as ④d's "thirteen sites, not nine": when a number moves, sweep for
the number, not for the lines that look like the thing you are changing.**

### ⚠ WHAT WAS DELIBERATELY **NOT** CHANGED

⛔ **`BlueprintBlackboard512` was considered and NOT built.** The user asked whether it is a viable
alternative if HSM256 turns out to be needed. 📐 Answer: **yes, and cheaper than when `O3b` added the
256 tier** — `EnsureAtLeast` and `IsLargerThan` now exist. ⭐ **But `CE-325` roughly doubles what the
128 tier accepts, so it may remove the need** ⇒ **re-measure before spending a tier.**
📄 Sizing if ever wanted: **512 / MaxSlots 6 / payload 384**; ladder stays legal; id **304**;
`BlackboardTier.B512 = 4` **APPENDED** *(ordinal is ABI)*. ⚠ And `B4`'s lesson: **ask which sites
derive a tier from CONTENT rather than from the entity** before calling it additive.

### 📐 THREE LATENT TRAPS — **recorded in §31.24.6, none of them fixed**

| # | trap |
|---|---|
| **1** | ⚠ **the kernel drains ONE event per FOUR frames** — `Idle → Entry → RTC → Activity` is one phase per `Update`. ⇒ ~15 events/s at 60 Hz, and the ring count is a BURST tolerance, not a throughput |
| **2** | 🔴 **the reserved interrupt slot is 1 deep at EVERY tier** — two interrupts inside ~66 ms and one is lost. ⛔ **No tier fixes this**; `CE-324` only made the loss visible |
| **3** | ⚠ **nothing currently produces a normal/low event in production** — HROT's one site is now `Interrupt`; `FireTimerEvent` is unreachable *(nothing ever arms `TimerDeadlines`)*; no shipped asset declares deferred events. ⇒ the ring is empty today, so ring-capacity risk is **latent, not absent** |

---

> ⭐⭐ **Branch `behaviors`. Tree clean, everything pushed.**
> ⛔ `git stash@{0}` holds *"EXPERIMENT: RootParamsBytes always 100 — probe only"* — **a diagnostic
> that must NEVER be committed.** Leave it stashed.

---

## 1. ⭐⭐⭐ WHERE IT STANDS — **`O7c` is finished**

| brain component | state |
|---|---|
| **`BrainBlackboard`** / **`Blackboard1024`** | ✅ **RETIRED** by `P4` |
| **`BrainBTreeState`** | ✅ **RETIRED** — id 31 burned `_RESERVED` |
| **`BrainHsm64`** | ✅ **RETIRED** — id 35 burned `_RESERVED` |
| **`BrainHsm128`** | ✅ **RETIRED** — id 36 burned `_RESERVED` *(`O7c`-④d)* |

⭐⭐ **An entity's brain state is entirely occurrence-resident.** Discovery is the tier walk;
`BehaviorState.BrainTier` selects the arm; both roots are keyed slots reached through
`RootStateAccess` / `RootHsmAccess`.

### ⭐ What landed, in order *(all pushed)*

| commit | what |
|---|---|
| `449ec04ac` | **①** `BrainHsm64` deleted — free: nothing ever attached it |
| `23bd73c1e` | **②a** `BlueprintTierTable.BuildTierQueries` — the shared tier walk |
| `6f64208d6` | **②** `BrainBTreeState` deleted; the cursor became a keyed slot (`CE-319`) |
| `fca039532` | **the golden PASSES** on the live cluster (§31.12.7) |
| `f284552c9` | **③** size-driven `HsmInstanceManager.Initialize`/`Reset` — `ExtDeps` addition #1 |
| `162410a02` | **④ DESIGNED** — §31.14, three UML diagrams |
| `d3d0db16a` | **④a** `RootHsmAccess` — the HSM instance is a slot, sized from `SelectTier(blob)` |
| `c805ce693` | **④b** `BrainTickSystem` replaces BOTH tick systems; `ExtDeps` addition #2 |
| `9d868710e` | **④c** `O7_R48` — the two-region machine through the REAL system |
| `00c6e61db` | **`CE-322`** — the HSM inertia bug proven and railed (`O7_R49`) |
| `ede0b9fe3` | the lane resumptions consolidated into this file |
| **④d** | **`BrainHsm128` deleted**; hot reload is a slot walk; decoders size-driven; `O7_R50`–`O7_R54` |

---

## 2. ⭐⭐⭐ WHAT IS LEFT IN THE LANE

⛔ **`O7c` has no remaining slice, and `CE-300` / `CE-318` / `CE-321` ③ / `CE-323` are DONE
(`2026-09-23`).** What is open:

| id | what | why it is not ours |
|---|---|---|
| ⚠ **`CE-321` ②** | 🔴 **NARROWED AND RE-SCOPED — it is a COMBAT-PIPELINE defect, not a storage one.** 📄 §31.22. The UrbanCombatNew soldiers disembark, acquire the **correct** target, stand ~**120 m** away inside their 150 m range, fire **all 30 rounds each**, and **bullets are live on 53 ticks** — yet the insurgent's `Health.Current` never leaves **100**. ⇒ the break is in `WeaponFireIntent → FireProcessing → Raycast → HitResolution → Damage`. **3 `UrbanCombatNewScenarioTests` red** | nothing in that chain touches occurrence storage; absorbing it would repeat the mistake `CE-321` was filed to avoid |
| **`CE-301`** | cache the root-params slot offset per entity with a generation dirty-bit — a PERF idea, user-suggested, movers measured | **explicitly excluded by the user `2026-09-23`** |

⭐ **What `CE-323` unblocked, worth knowing before touching `CE-321` ②:** `Fdp.Examples.UrbanCombat.Tests`
went **1 failed → 29/29**. The `HSM TRANSITION` milestone — the failure this lane had carried as
*"pre-dates `O7c` entirely"* since the programme began — **was the missing `BrainInterrupts`
registration all along.**

## 3. ⛔⛔ THE TRAPS — **every one of these cost a build loop in this programme**

| # | trap | how it bites |
|---|---|---|
| **①** | 🔴🔴 **A MISSING TIER REGISTRATION FAILS SILENTLY** | discovery is the tier walk, so an entity with no store is **never enumerated** — no throw, no log, the brain just never ticks. ⭐⭐ **`CE-321` is this trap hitting FOUR example worlds unnoticed for four slices.** 🔒 **The check, one command:** grep `RegisterComponent<…BehaviorState>` and cross-reference `BlueprintTierTable.RegisterAll` |
| **②** | ⚠ **`GlobalComponentIds.cs` CONTAINS MOJIBAKE** | several summary lines store their em-dash as `â€”`. ⛔ An exact-string `Edit` on such a line FAILS. ⭐ Anchor on the code line and patch by line number |
| **③** | 🔴🔴 **41 of 60 TEST PROJECTS ARE UNRESTORED in a fresh container** | ⛔ **an unrestored project is SKIPPED, not run — its silence reads exactly like a pass.** 📌 It has now hidden a defect **three** times, most recently a `Hrot.NodeComposition.Tests` project that did not even COMPILE. ⭐ `dotnet restore <proj>` costs ~10 s; `ls <proj>/obj/project.assets.json` is the check |
| **④** | 🔴🔴🔴 **`ActiveBehaviorHash` IS NOT A FIELD, IT IS AN ADDRESS** | every root key — params, BTree cursor, HSM instance — is COMPUTED from it ⇒ a detach placed AFTER a clear is a silent no-op, and **any site that OVERWRITES it orphans every root slot the entity had**. ⚠⚠ **Bitten THREE times**: `CE-321` ③, the `AssignBehaviorHashEvent` leak (§31.16.7), and a test fixture (§31.19.7). 🔒 **ADOPT the entity's hash; stamp only when it is `0`** |
| **⑤** | ⚠ **SPAWN PUBLISHES NO ASSIGN EVENT** | the translator provisions the BTree cursor at spawn; ⛔ it CANNOT provision the HSM instance *(no registry ⇒ no blob ⇒ no `SelectTier`)*, and §31.15.1 explains why it need not |
| **⑥** | ⚠ **the instance TIER decides EVENT-QUEUE capacity** | 64 B holds ONE event and has **no interrupt slot**; 256 B has an interrupt slot plus a ring of 5. ⭐ A test blob that needs interrupt priority must declare `RegionCount ≥ 2` so `SelectTier` answers 128 |
| **⑦** | 🔴 **`ResolveOrAttachRoot` DETACHES BEFORE IT ATTACHES** | on a width mismatch. ⇒ if the store cannot hold the wider instance the entity is left with **NO machine**, worse than the stale one. ⭐ **Promote the store FIRST** (`BlueprintTierTable.EnsureAtLeast`) — §31.19.2 |
| **⑧** | ⚠ **the golden needs a FRESH ClusterRunner dll and the `Scenario` perspective** | `--mode all` answers for ONE node at a time; a stale binary gave a confident wrong reading once |
| **⑨** | 🔴🔴🔴 **THE REGISTRATION CHECK HAS THREE COLUMNS NOW, NOT TWO** | `CE-323`: `BrainInterrupts` was registered **nowhere in production** and three separate guards skipped silently *(translator attach, `CognitiveInterruptSystem`'s query, `BrainTickSystem`'s enqueue)*. 🔒 **The command:** for every file that does `RegisterComponent<BehaviorState>`, cross-reference **`BlueprintTierTable.RegisterAll` AND `RegisterComponent<BrainInterrupts>`**. 📐 It found five brain-building worlds missing the third |
| **⑩** | ⚠ **`SimTransform.Position` is a JSON LIST `[x,y,z]` over the debug API**, not `{X,Y,Z}` | a reader written for the object shape raises inside a sampling loop and prints **nothing**, which reads exactly like a missing entity |

---

## 4. ⭐ DECISIONS ALREADY MADE — **do not re-litigate**

| | |
|---|---|
| **`BlueprintTickSystem` STAYS SEPARATE** | §31.14.2, four measured grounds — two registration roots outside `CognitiveRuntimeModule`, world singletons, all-slots-vs-one-keyed-slot, no authority gate |
| **the HSM arm SKIPS on a missing slot; the BTree arm THROWS** | §31.16.2 — measured asymmetry, not inconsistency |
| **`Phase = Entry`, and HROT has NO opinion about the phase** | `CE-322` / §31.18 — the fix routes through the kernel's `Initialize`. **Approved by the user `2026-09-23`** |
| **hot reload uses `Initialize`, not `HardReset`** | §31.19.2 — `HardReset` left `Phase = Idle`, which §31.18 proved never advances on an empty queue |
| **the hot-reload walk keeps NO remembered blob** | §31.19.2 — `Header.MachineId` already carries the structure hash, and the remembered one had a latent per-chunk bug |
| **`HotReloadManager` is NOT deleted from `ExtDeps`** | it has no HROT consumer any more, but it is FastHSM's own type — *"unreferenced is not unintentional"* applies across the line hardest |
| **the `_seenThisFrame` sweep SURVIVES, premise restated** | §31.14.6 — keyed by `entity.Index`, which the ECS REUSES ⇒ correctness, not tidiness. `O7_R47` pins it |
| **a deleted component's tests are RE-HOMED, not dropped** | §31.19.4 — except the three *"registry X does not register it"* rows, which the deletion genuinely makes vacuous |
| **`CE-318` is DEFERRED** | §31.13 |
| **`CE-321`'s remainder is NOT folded into `O7c`** | it is `P3`/`P4`-era breakage in EXAMPLES |
| **the two example scenarios keep their `Phase = RTC` workarounds** | they start the APC *already cruising* — a stronger statement than "enter normally" |
| **the 64- and 256-byte tiers are NOT dead** | only the ECS wrappers died; both are now reachable, and `O7_R50`/`O7_R51` decode them |

---

## 5. 📐 GATE BASELINES — *(measured `2026-09-23` at the ④d commit; re-verify, do not quote)*

| suite | result | vs baseline |
|---|---|---|
| `Fdp.Toolkits.Tests` | ✅ **2317 / 2317** | unchanged |
| `Hrot.Blueprints.Tests` | ✅ **4017 / 4035** *(18 pre-existing skips)* | unchanged |
| `Hrot.Hsm.Editor.Tests` | ✅ **565 / 565** | **+6** — the size-driven decode rails |
| `Hrot.Editor.Tests` *(`AiHotReloadCoordinatorTests`)* | ✅ **19 / 19** | **+3** — `O7_R52`–`O7_R54` |
| `Hrot.NodeComposition.Tests` | ✅ **53 / 53** | ⚠ **it did not COMPILE before** — trap ③ |
| ⚠ `Hrot.SimHost.Tests` | **3 failed / 1011** | ≤ the documented 4; same families, the `DEBT-AIB-030` signature |
| ⚠ `Fdp.Examples.UrbanCombat.Tests` | **1 failed / 29** | unchanged — `UrbanAmbush…Milestones` on `HSM TRANSITION`, pre-existing since before `O7c` |
| ⚠ `Fdp.Examples.Scenarios.Tests` | **7 failed / 68** | unchanged — `P3`/`P4`-era breakage. 📋 `CE-321` |
| build-clean, not run | `Hrot.ClusterRunner.Integration.Tests` · `HrotStrideApp.Game.Tests` | |
| doc gates | `design-digest --check` · `rulings-check` **38/38** · `tracker-counts --check` | |

⛔⛔ **Name what you RAN.** With 41 suites unrestored, a table that implies broad coverage overstates it.

### ✅✅✅ THE GOLDEN PASSES AT `O7c` COMPLETE — *(`2026-09-23`, 📄 §31.19.8)*

`hill-attack-close --mode all`, fresh ClusterRunner dll, `Scenario` perspective, run to `simTime 131`:

| | 1001 | 1002 | 1003 | 1004 |
|---|---|---|---|---|
| **this run** | 523.025 | 525.178 | 529.195 | 530.918 |
| **gold** | 523.06 | 525.22 | 529.22 | 530.99 |
| **delta** | −0.035 | −0.042 | −0.025 | **−0.072** |

⭐ Both hostiles `Health.Current == 0` · entity count steady at **8** · all four attackers
`LocomotionChannel.Status: Success` on the baseline · **0 exceptions · 0 FastBTree warnings · 0
root-slot throws · 0 `[AiHotReload] WARNING`**.
⚠ Worst delta **0.072** — larger than `O7c`-②'s 0.02, well inside `P4`'s accepted 0.83, on a wall-clock
sim whose documented drift source is machine load.
⛔ **It runs a BTree, so it cannot see the HSM arm.** What it proves is that the tick-system MERGE did not
break the BTree arm — the likeliest way `O7c`-④ goes wrong. The HSM arm's evidence is `O7_R48`–`O7_R54`.

⚠ **Two method traps, both of which cost a step:** rebuild the ClusterRunner dll first *(trap ⑦)*; and
`SimTransform.Position` is a **JSON LIST** `[x,y,z]`, not `{X,Y,Z}` — a reader written for the object
shape raises inside the sampling loop and prints **nothing**, which reads exactly like a missing entity.
