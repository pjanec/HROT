<!--STATUS
state: LIVE
doc-type: BUILD RESUMPTION for O7c — retiring the root brain components, on the `behaviors` lane.
  ⚠ A STATE doc, not canon. Every "green"/"pushed"/"HEAD" line is a snapshot dated below.
  ⛔ VERIFY against git before acting ("THE LEDGER MAY NOT ASSERT WHAT THE CODE IS").
updated: 2026-09-23
build-state: BUILDING — ①②③ DONE and pushed; ④ is designed (§31.14) and NOT started.
current-answer: ⭐⭐⭐ START AT §1 "WHERE IT STANDS", THEN §2 "THE NEXT ACTION".
  📐 TWO OF THE THREE COMPONENTS ARE ALREADY RETIRED — verified 2026-09-23 by grepping for the
  struct declarations, not from memory:
     BrainBTreeState  ✅ DELETED, id 29 burned as _RESERVED
     BrainHsm64       ✅ DELETED, id 35 burned as _RESERVED
     BrainHsm128      ❌ STILL EXISTS, id 36 live  ⇐ THIS IS THE REMAINING WORK
  ⭐ The BTree half is DONE and PROVEN ON THE RUNNING PRODUCT: hill-attack-close --mode all,
  all six invariant links, positions within 0.02 of gold, 0 exceptions / 0 FastBTree warnings /
  0 RootStateAccess throws (§31.12.7). CE-319 is closed.
  ⛔⛔ DO NOT re-derive the merge design — §31.14 has it with all three UML diagrams, the
  inventory that decided its scope, and a ruling on the _seenThisFrame asymmetry.
  ⚠ The one thing to read BEFORE writing any HSM code is §3 (the traps), because three of them
  cost a build loop each during the BTree half and all three recur in the HSM half.
stale-below: nothing — this doc is younger than the work it describes.
known-rot: nothing.
known-conflict: none.
related-designs:
  - DESIGN_Occurrence_Scoped_Storage.md — ⭐ THE OWNING DESIGN. §31 is O7c end to end;
    §31.14 is the merge; §31.12 is the BTree as-built; §31.13 is the CE-318 deviation.
  - PLAN_Occurrence_Storage_Build.md — row E4 carries O7c's re-rating and the five-step order.
  - RESUME_Occurrence_Storage.md — the PROGRAMME view (P0–P4 + O7c). Its §0c names the path.
  - RESUME_P4_Retire_Blackboards.md — ⛔ CLOSED. A record of the slice before this one.
  - Blueprint_Issues_Tracker.md — CE-319 DONE; CE-318 OPEN (deferred, §31.13); CE-300/301 open.
  - RUNBOOK_Cluster_Debugging_Over_Http.md — how to run the golden. §1.1 and §4 are load-bearing.
-->

# RESUMPTION — **`O7c`, retiring the root brain components** *(`behaviors` lane)*

RELEARN

> ⭐⭐ **Branch `behaviors`. HEAD `162410a02` at the time of writing, tree clean, everything pushed.**
> ⛔ `git stash@{0}` holds *"EXPERIMENT: RootParamsBytes always 100 — probe only"* — **a diagnostic that
> must NEVER be committed.** Leave it stashed.

---

## 1. ⭐⭐⭐ WHERE IT STANDS

| component | state |
|---|---|
| **`BrainBTreeState`** | ✅ **RETIRED** — id 29 burned `_RESERVED` |
| **`BrainHsm64`** | ✅ **RETIRED** — id 35 burned `_RESERVED` |
| 🔴 **`BrainHsm128`** | ❌ **ALIVE**, id 36 — **the remaining work** |

### ⭐ What landed, in order *(all pushed)*

| commit | what |
|---|---|
| `449ec04ac` | **`O7c`-① — `BrainHsm64` deleted.** Free: nothing in production ever attached it, so `HsmTickSystem<BrainHsm64>` ticked an **always-empty query** every frame. ⭐ Rail `EveryHsmTickSystem_IsRegisteredForAnAttachableComponent_O7c1` went **red first**, naming the orphan |
| `23bd73c1e` | **`O7c`-②a — `BlueprintTierTable.BuildTierQueries`.** ⚠ Also CORRECTED §31.7: the proposed `BrainTickWalk` was mostly already there |
| `6f64208d6` | **`O7c`-② — `BrainBTreeState` deleted**, cursor into a keyed slot via `RootStateAccess` |
| `fca039532` | **the golden PASSES** (§31.12.7) — `CE-319` closed |
| `f284552c9` | **`O7c`-③ — the ONE `ExtDeps` addition**: public size-driven `HsmInstanceManager.Initialize/Reset`. `Fhsm.Tests` 300/300 |
| `162410a02` | **`O7c`-④ DESIGNED** — §31.14, three UML diagrams, scope decided by inventory |

---

## 2. ⭐⭐⭐ THE NEXT ACTION — **`O7c`-④, and §31.14 already designed it**

⛔⛔ **Do NOT re-derive the design.** §31.14 carries the `classDiagram`, `sequenceDiagram`, module
diagram, the inventory that set the scope, and the `_seenThisFrame` ruling. Build to it.

| # | slice | the one thing to know |
|---|---|---|
| **④a** | `RootHsmAccess` + ingress provisioning | ⚠ **THE ONE PLACE HSM IS HARDER THAN BTREE:** the width is a RUNTIME value from `HsmInstanceManager.SelectTier(blob)` → 64/128/256, **not** a `sizeof` constant. ⭐ Otherwise a member-for-member mirror of `RootStateAccess` |
| **④b** | **merge** `BTreeTickSystem` + `HsmTickSystem` → **`BrainTickSystem`** | ⛔ `HsmTickSystem<T>` **cannot survive** — it is generic over the component. ⭐ The HSM arm calls `HsmKernel.Update(blob, ptr, size, …)`, **never a generic** |
| **④c** | **the new rail** *(user-requested)* | drive the **two-region** machine through the REAL system with the instance in a slot. ⛔ `O7_R37`/`O7_R38` stay **UNCHANGED** and must stay green |
| **④d** | hot-reload slot walk · decoders size-driven · **delete `BrainHsm128`** | 📄 `btree-hsm-unif` §Q6 |

### ⚠⚠ ACCEPTANCE — **the golden CANNOT see the HSM arm**

📐 `hill-attack-close` runs `PlatoonHillAttack`, a **BTree**. Four `.hsm.json` assets ship and
essentially **no production entity runs one**. ⇒ ⭐ the acceptance is ① `O7_R37`/`O7_R38` still green ·
② the ④c rail · ③ the four showcase assets · ④ **the golden still green** — which proves the MERGE did
not break the **BTree** arm, and that is the most likely way ④ goes wrong.

---

## 3. ⛔⛔ THE TRAPS — **read this BEFORE writing HSM code; all three recur**

| # | trap | how it presented in the BTree half |
|---|---|---|
| **①** | 🔴🔴 **SPAWN PUBLISHES NO ASSIGN EVENT** | `BehaviorTkbTranslator` stamps `ActiveBehaviorHash` from the template's DEFAULT behaviour, and the assign path runs only from mission/intent. ⇒ **ingress is NOT the only provisioner — the TRANSLATOR must provision at spawn too.** ⚠ The same translator attaches `BrainHsm128` today, so **the HSM arm has the identical requirement** |
| **②** | 🔴🔴 **A MISSING TIER REGISTRATION FAILS SILENTLY** | discovery is the tier walk, so an entity with no store is **never enumerated** — no throw, no log, the brain just never ticks. ⚠ The `CE-315` shape. 8 tests caught it as *"Expected 1, Actual 0"*; the fix was `TestWorldFactory` mirroring production |
| **③** | 🔴 **EVERY ROOT KEY DIES WHEN `ActiveBehaviorHash` IS CLEARED** | so a detach placed AFTER `behavior.ActiveBehaviorHash = None` is a **silent no-op**. ⚠ My first draft had exactly that. ⭐ The clear handler is now already correct for BOTH root slots — **do not re-break it** |
| **④** | ⚠ **`project.assets.json` missing ⇒ an unrestored project CANNOT FAIL** | its silence reads exactly like a pass. Hit **three times** this session; it also made a "full-solution build" prove far less than claimed (corrected in `cebd861a5`) |
| **⑤** | ⚠ **CAPTURE THE RUN WHOLE THE FIRST TIME** | a filtered capture of an intermittent red gets ONE chance. I spent it once (§31.11.6); the second time it paid — the red was `O7_R41`, `CE-302`'s leak rail, and reading the ACTUAL number (2, not 5) turned it from a failure into evidence the detach works |
| **⑥** | ⚠ **the golden needs a FRESH ClusterRunner dll and the `Scenario` perspective** | a stale binary produced a confident wrong reading once; and `--mode all` answers for ONE node at a time, so a first read showed 8 entities with **no `BehaviorState` at all** |

---

## 4. ⭐ DECISIONS ALREADY MADE — **do not re-litigate**

| | |
|---|---|
| **`CE-318` is DEFERRED** | §31.13. It shifts tier selection for EVERY entity, so it must not ride along with the HSM move and blur a golden regression. ⛔ **Not dropped** — the row is open and gets its own golden run |
| **`BlueprintTickSystem` STAYS SEPARATE** | §31.14.2, four measured reasons — two registration roots outside `CognitiveRuntimeModule`, world singletons, all-slots-vs-one-keyed-slot, no authority gate |
| **the `_seenThisFrame` sweep SURVIVES, with a restated premise** | §31.14.6. It is keyed by `entity.Index`, which the ECS **reuses** ⇒ a stale entry on a recycled index would suppress a genuine `BehaviorFinishedEvent` for a different entity. ⭐ The merge FIXES a latent BTree gap rather than importing an HSM quirk |
| **the 64-byte TIER is not dead** | only the ECS wrapper died. `SelectTier` still returns 64, and after ④ a 64-byte instance becomes **reachable for the first time** — §9.4's *"the tier stops being a TYPE and becomes a PAYLOAD SIZE"*, literally |

---

## 5. 📐 GATE BASELINES — *(measured `2026-09-22`/`23`; re-verify, do not quote)*

| suite | at HEAD |
|---|---|
| `Fdp.Toolkits.Tests` | ✅ **2309 / 2309** |
| `Hrot.BTree.Editor.Tests` | ✅ 633 / 633 |
| `Hrot.Presentation.Tests` | ✅ 298 / 298 |
| `Fhsm.Tests` | ✅ 300 / 300 *(needs `dotnet restore` first)* |
| `Hrot.Blueprints.Tests` *(tick suite)* | ✅ 156 / 158, 2 pre-existing skips |
| ⚠ `Hrot.SimHost.Tests` | **3 failures, PROVEN pre-existing** — a worktree at `23bd73c1e` reproduces the same three names: `NodeRolePersistenceRails.TheSaveHandlerSetIsStillComplete`, `MapPresentationParityRails…(EditorStrideSubsystem.cs)`, `FullBranchPipelineTests.BranchedRecording_CapturesHistoricalStateAsKeyframe` |
| doc gates | mermaid **26/26**, `design-digest --check`, `rulings-check` 38/38, `tracker-counts --check` |
