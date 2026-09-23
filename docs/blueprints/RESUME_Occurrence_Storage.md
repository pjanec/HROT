<!--STATUS
state: LIVE
doc-type: THE resumption doc for the `behaviors` lane — programme: OCCURRENCE-SCOPED STORAGE.
  ⭐ CONSOLIDATED 2026-09-23 into ONE file. Three slice-level resumptions were DELETED, not lost:
  RESUME_O7c_Retire_Brain_Components.md, RESUME_P4_Retire_Blackboards.md and
  RESUME_CE304_Params_Regression.md. Their durable content is in DESIGN_Occurrence_Scoped_Storage.md
  (P4 = §30, O7c = §31, CE-304 = §19a) and in the tracker; git history holds the files themselves.
  ⚠ A STATE doc, not canon. Every "green"/"pushed"/"HEAD" line is a snapshot dated below.
  ⛔ VERIFY against git before acting ("THE LEDGER MAY NOT ASSERT WHAT THE CODE IS").
updated: 2026-09-23
build-state: BUILDING — O7c ①②③④a④b④c DONE and pushed; ④d is the LAST slice and is NOT started.
current-answer: ⭐⭐⭐ START AT §1 "WHERE IT STANDS", THEN §2 "THE NEXT ACTION — O7c-④d".
  📐 VERIFIED 2026-09-23 by grepping for the struct declarations, not from memory:
     BrainBTreeState  ✅ DELETED, id 29 burned _RESERVED
     BrainHsm64       ✅ DELETED, id 35 burned _RESERVED
     BrainHsm128      ❌ STILL EXISTS, id 36 live  ⇐ THE ONLY REMAINING WORK
  ⭐ And: BTreeTickSystem + HsmTickSystem<T> are GONE — ONE BrainTickSystem.cs replaces both.
  ⛔⛔ DO NOT re-derive any design. §31.14 has the merge with three UML diagrams; §31.15–§31.18 are
  the as-built for ④a/④b/④c and the HSM inertia bug. ④d's own scope is §2 below, MEASURED.
stale-below: nothing — this doc was rewritten whole on 2026-09-23.
known-rot: nothing.
known-conflict: none.
related-designs:
  - DESIGN_Occurrence_Scoped_Storage.md — ⭐ THE OWNING DESIGN. §30 = P4, §31 = O7c end to end.
  - PLAN_Occurrence_Storage_Build.md — the task ladder; row E4 carries O7c's re-rating.
  - Blueprint_Issues_Tracker.md — CE-320 (O7c) · CE-321 (example rot) · CE-322 (inertia, DONE)
    · CE-318 (deferred) · CE-300/301 (open).
  - RUNBOOK_Cluster_Debugging_Over_Http.md — how to run the golden. §1.1 and §4 are load-bearing.
-->

# RESUMPTION — **occurrence-scoped storage**, `behaviors` lane

RELEARN

> ⭐⭐ **Branch `behaviors`. HEAD `00c6e61db` at the time of writing, tree clean, everything pushed.**
> ⛔ `git stash@{0}` holds *"EXPERIMENT: RootParamsBytes always 100 — probe only"* — **a diagnostic
> that must NEVER be committed.** Leave it stashed.

---

## 1. ⭐⭐⭐ WHERE IT STANDS

| brain component | state |
|---|---|
| **`BrainBlackboard`** / **`Blackboard1024`** | ✅ **RETIRED** by `P4` |
| **`BrainBTreeState`** | ✅ **RETIRED** — id 29 burned `_RESERVED` |
| **`BrainHsm64`** | ✅ **RETIRED** — id 35 burned `_RESERVED` |
| 🔴 **`BrainHsm128`** | ❌ **ALIVE**, id 36 — **the last one** |

⭐⭐ **Nothing READS `BrainHsm128` for execution any more.** `O7c`-④b moved the reader onto the slot;
what is left is the component's own declaration and six registration/attach/debug sites (§2).

### ⭐ What landed, in order *(all pushed)*

| commit | what |
|---|---|
| `449ec04ac` | **①** `BrainHsm64` deleted — free: nothing ever attached it, so its tick ran an always-empty query |
| `23bd73c1e` | **②a** `BlueprintTierTable.BuildTierQueries` — the shared tier walk |
| `6f64208d6` | **②** `BrainBTreeState` deleted; the cursor became a keyed slot (`CE-319`) |
| `fca039532` | **the golden PASSES** on the live cluster (§31.12.7) |
| `f284552c9` | **③** size-driven `HsmInstanceManager.Initialize`/`Reset` — `ExtDeps` addition #1 |
| `162410a02` | **④ DESIGNED** — §31.14, three UML diagrams |
| `d3d0db16a` | **④a** `RootHsmAccess` — the HSM instance is a slot, sized from `SelectTier(blob)` |
| `c805ce693` | **④b** `BrainTickSystem` replaces BOTH tick systems; `ExtDeps` addition #2 (`GetActiveLeafIds`) |
| `9d868710e` | **④c** `O7_R48` — the two-region machine through the REAL system, slot-resident |
| `00c6e61db` | **`CE-322`** — the HSM inertia bug proven and railed (`O7_R49`) |

---

## 2. ⭐⭐⭐ THE NEXT ACTION — **`O7c`-④d, and here is its EXACT surface**

📐 **Measured `2026-09-23`** *(`grep BrainHsm128`, production only — tests are additional)*. ⛔ Do not
re-discover this list; verify it is still nine and go.

| # | site | what it needs |
|---|---|---|
| **1** | `Behavior/Components/BrainComponents.cs:37-41` | **delete the struct** + its `[ComponentId]` |
| **2** | `Fdp.Core/GlobalComponentIds.cs:124-125` | **burn id 36** as `BrainHsm128_RESERVED`. ⚠ **line 124 CONTAINS MOJIBAKE** (`â€”`) — see trap ② |
| **3** | `Translators/BehaviorTkbTranslator.cs:45` | drop the `yield return typeof(BrainHsm128)` |
| **4** | `Translators/BehaviorTkbTranslator.cs:137-138` | drop the spawn attach — ⚠ the whole `else if (BrainTierHsm)` arm. ⭐ §31.15.1: it provisions NOTHING usable, so there is nothing to replace it with |
| **5** | `Hrot.SimHost/CognitiveComponentRegistry.cs:51` | drop `RegisterComponent<BrainHsm128>()` |
| **6** | `Hrot.Core/HrotRoleComponentSets.cs:137` | drop `brainOnly.SetBit(ComponentType<BrainHsm128>.ID)` |
| **7** | `FDP/Examples/…/HeadlessDemoApp.cs:255` | drop `RegisterComponent<BrainHsm128>()` |
| **8** | 🔴 `Hrot.Editor/AiHotReloadCoordinator.cs:313, 382, 588` | **`ReloadHsmChunks<T>` → a SLOT walk.** 📄 `btree-hsm-unif` §Q6 predicted this. ⭐ The component-chunk walk cannot work: slot payloads are not contiguous |
| **9** | 🔴 `Hrot.Hsm.Editor/Debug/HsmDebugSession.cs:97-99` | **size-driven decode.** ⭐⭐ `RootHsmAccess.TryCopyInstanceInView` was built in ④a **for exactly this and STILL HAS NO CONSUMER** — ④d is where it gets one |

⭐⭐ **8 and 9 are the only real work.** 1–7 are deletions whose call sites are already dead.

### ⚠ ACCEPTANCE

① `Fdp.Toolkits.Tests` green *(baseline §5)* · ② `O7_R37`/`O7_R38`/`O7_R48`/`O7_R49` still green ·
③ the four showcase `.hsm.json` assets still run · ④ **the golden still green** — it proves the BTree
arm survived, and ⛔ **it cannot see the HSM arm at all** *(`hill-attack-close` runs a BTree)*.

---

## 3. ⛔⛔ THE TRAPS — **every one of these cost a build loop in this programme**

| # | trap | how it bites |
|---|---|---|
| **①** | 🔴🔴 **A MISSING TIER REGISTRATION FAILS SILENTLY** | discovery is the tier walk, so an entity with no store is **never enumerated** — no throw, no log, the brain just never ticks. ⭐⭐ **`CE-321` is this trap hitting FOUR example worlds unnoticed for four slices.** 🔒 **The check, one command:** grep `RegisterComponent<…BehaviorState>` and cross-reference `BlueprintTierTable.RegisterAll` |
| **②** | ⚠ **`GlobalComponentIds.cs` CONTAINS MOJIBAKE** | line 124's em-dash is stored as `â€”`. ⛔ An exact-string `Edit` on that line FAILS. ⭐ Anchor on the code line and `sed` the summary, as `O7c`-① did |
| **③** | 🔴🔴 **41 of 60 TEST PROJECTS ARE UNRESTORED in a fresh container** | ⛔ **an unrestored project is SKIPPED, not run — its silence reads exactly like a pass.** ⭐ `dotnet restore <proj>` costs ~10 s. ⚠ `ls <proj>/obj/project.assets.json` is the check |
| **④** | 🔴 **EVERY ROOT KEY DIES WHEN `ActiveBehaviorHash` IS CLEARED OR CHANGED** | keys are COMPUTED from it ⇒ a detach placed AFTER the clear is a silent no-op, and a site that overwrites the hash orphans the slot the translator attached *(`CE-321` ③)*. ⭐ Both handlers are correct now — **do not re-break the ordering** |
| **⑤** | ⚠ **SPAWN PUBLISHES NO ASSIGN EVENT** | the translator provisions the BTree cursor at spawn; ⛔ it CANNOT provision the HSM instance *(no registry ⇒ no blob ⇒ no `SelectTier`)*, and §31.15.1 explains why it need not |
| **⑥** | ⚠ **the instance TIER decides EVENT-QUEUE capacity** | 64 B holds ONE event and has **no interrupt slot**. ⭐ A test blob that needs interrupt priority must declare `RegionCount = 2` so `SelectTier` answers 128 |
| **⑦** | ⚠ **the golden needs a FRESH ClusterRunner dll and the `Scenario` perspective** | `--mode all` answers for ONE node at a time; a stale binary gave a confident wrong reading once |

---

## 4. ⭐ DECISIONS ALREADY MADE — **do not re-litigate**

| | |
|---|---|
| **`BlueprintTickSystem` STAYS SEPARATE** | §31.14.2, four measured grounds — two registration roots outside `CognitiveRuntimeModule`, world singletons, all-slots-vs-one-keyed-slot, no authority gate |
| **the HSM arm SKIPS on a missing slot; the BTree arm THROWS** | §31.16.2 — measured asymmetry, not inconsistency: every BTree behaviour has a cursor the translator provisions, an HSM one may legitimately never have been assigned |
| **`Phase = Entry`, and HROT has NO opinion about the phase** | `CE-322` / §31.18 — the fix routes through the kernel's `Initialize`; ⛔ there is no `Phase =` assignment in HROT any more. **Approved by the user `2026-09-23`** |
| **the `_seenThisFrame` sweep SURVIVES, premise restated** | §31.14.6 — keyed by `entity.Index`, which the ECS REUSES ⇒ correctness, not tidiness. `O7_R47` pins it |
| **`CE-318` is DEFERRED** | §31.13 — it moves tiers for EVERY entity, so it must not ride along and blur a golden regression |
| **`CE-321`'s remainder is NOT folded into `O7c`** | it is `P3`/`P4`-era breakage in EXAMPLES; absorbing it would hide which slice broke what |
| **the two example scenarios keep their `Phase = RTC` workarounds** | they start the APC *already cruising* — a stronger statement than "enter normally" |
| **the 64-byte tier is NOT dead** | only the ECS wrapper died; `SelectTier` still returns 64, and a 64-byte instance is now reachable for the first time |

---

## 5. 📐 GATE BASELINES — *(measured `2026-09-23`; re-verify, do not quote)*

| suite | at HEAD `00c6e61db` |
|---|---|
| `Fdp.Toolkits.Tests` | ✅ **2317 / 2317** |
| `Hrot.Blueprints.Tests` | ✅ 4017 / 4035 *(18 pre-existing skips)* |
| ⚠ `Hrot.SimHost.Tests` | **4 failed / 1004** — SAME COUNT at base `d3d0db16a`; three names stable, **one rotating inside the record/replay family** *(`LiveFromReplay` ↔ `EcsRecordReplayController`)* — the `DEBT-AIB-030` signature |
| ⚠ `Hrot.AiEditor.Generators.Tests` | **4 failed / 282** — **IDENTICAL FOUR at base**; they are `O7c`-②'s un-re-baselined `+1` root-slot counts *(1→2, 1→2, 3→4)*. 📋 `CE-321` |
| ⚠ `Fdp.Examples.UrbanCombat.Tests` | **1 failed / 29** — `UrbanAmbush…Milestones` on `HSM TRANSITION`, **pre-existing since before `O7c`** |
| ⚠ `Fdp.Examples.Scenarios.Tests` | **7 failed / 68** — `P3`/`P4`-era breakage. 📋 `CE-321` |
| build-clean, not run | `Hrot.ClusterRunner.Integration.Tests` · `HrotStrideApp.Game.Tests` |
| doc gates | `design-digest --check` · `rulings-check` **38/38** · `tracker-counts --check` · mermaid **26/26** |

⛔⛔ **Name what you RAN.** With 41 suites unrestored, a table that implies broad coverage overstates it.

---

## 6. ⏳ OPEN ELSEWHERE

| id | |
|---|---|
| **`CE-321`** | the example-scenario rot — ② fixed, ①/③ open *(the `ref byte`-as-component and the generator slot counts)* |
| **`CE-318`** | the tier demand double-charges each slot's 16-byte entry — deferred, needs its own golden run |
| **`CE-300` / `CE-301`** | open, unrelated to `O7c` |
