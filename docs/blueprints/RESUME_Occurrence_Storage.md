<!--STATUS
state: LIVE
doc-type: THE resumption doc for the `behaviors` lane — programme: OCCURRENCE-SCOPED STORAGE.
  ⚠ A STATE doc, not canon. Every "green"/"pushed"/"HEAD" line is a snapshot dated below.
  ⛔ VERIFY against git before acting ("THE LEDGER MAY NOT ASSERT WHAT THE CODE IS").
updated: 2026-09-26
build-state: ✅ **`E5` IS BUILT — items 1-5 of 7, runtime half PROVEN.** Branch `behaviors`.
  ⭐⭐⭐ An HSM state hosts a BTree: `StateNode.SubtreeName`, `HsmHostedSubtrees`, `HostedChildren`,
  the slot + table emission in `HsmBridgeEmitCore`, and `BrainTickSystem.TickHostedChildren`.
  Rails `E5_R1`..`E5_R4` in `HsmOccurrenceKeyTests`, red-proved. 📄 §32.11 is the AS-BUILT.
  ✅ `CE-333` ROUTED (the HSM alias arm is retired) · ✅ `CE-335` FIXED · ✅ `CE-336` BUILT — the
  compile rail, and it found THREE more latent defects on its first run (§32.11.5) ⇒ `A2` is MET.
  ✅ `CE-337` DONE — **both BTree orchestrator arms RETIRED** (user: "retire the arm"), §32.12,
  **and its three sweeps are CLOSED** (§32.13): none of the three answers was "delete" — the collector
  is KEPT, the slice declaration is STOPPED, the `BrainBlackboard` name is an IDENTITY TOKEN and must
  NOT be renamed. ⭐ Three silent no-ops became one loud `BTREE0002`, rails `CE337_R1`..`R3`.
  ✅ `CE-334` DONE `2026-09-26` — **the ENGINE change, approved and made**: an active state's
  `ActivityAction` now runs EVERY TICK (§32.14). It exposed a real unbounded `GetState` walk, and
  found workarounds ④ and ⑤ — **both inside test suites**, one of them the engine's own.
  ✅ `E5` `A1` DONE — `SubtreeName` round-trips through real JSON (§32.10).
  ✅ `E5` **ITEM 6 DONE `2026-09-26`** as `CE-338` (§32.15) — **the canvas now gets the resolvers the
  Diagnostics window already had.** 🔴 §32.11.3's deferral was WRONG: `IAssetCatalog` and BOTH
  resolvers already existed; what was missing was ONE ARGUMENT to `HsmGraphModel`. New
  `IStatefulScopeAsset` (`Hrot.Editor.AiShared`) let `CgfSubsystem` wire it too — it had been
  *structurally* unable to, not negligent. ⚠ **Rules 8/8b still cannot fire on a real asset** — the
  blocker MOVED from wiring to **AUTHORING** (`HsmFacets.StateFacet` has no subtree fields).
  ✅ `E5` **ITEM 7 DONE `2026-09-26`** as `CE-339` (§32.16) — **the `A` hosts `B` hosts `A` cycle.**
  ⭐⭐ New `ISubtreeHostingAsset` + `SubtreeCycleDetector`, shared by BOTH validators (ruling 9).
  🔴 Two seams that LOOKED like they held the edge do not: `IAssetCatalog.WhereDependsOn` is a
  **stub** returning empty, and `ReferenceCatalog` models asset→SUB-ELEMENT, not asset→asset.
  ⚠ A rail caught my own design error mid-build (no target element ⇒ nothing to badge).
  ⭐ **`E5` IS NOW COMPLETE — all 7 items + A1.**
  📋 OPEN: HSM subtree AUTHORING (**the real blocker**) · BTree-hosts-BTree (not built).
  ✅ `O7c` COMPLETE · `CE-325`..`CE-331` all DONE.
current-answer: ⭐⭐⭐ START AT §0 — it names the next slice (`E5`), the ONE document to read
  (`DESIGN_Occurrence_Scoped_Storage.md` §32) and the READING ORDER inside it (§32.2 the review first).
  §0a is what this session landed, §0b what is open and whose it is, §0c the method lessons, §0d the
  gate baselines, §0e the standing constraints.
  ⛔ §1-§5 are the O7c-era account and are HISTORY — see stale-below.
stale-below: ⚠⚠ §1-§5 describe the programme AS OF `O7c`-④d and are superseded by §0a-§0e.
  ⛔ §2's "what is left" predates CE-325..333 and Q36's approval — use §0b.
  ⛔ §5's gate baselines are superseded by §0d (Fdp.Toolkits.Tests 2328, Fhsm.Tests 307, plus five
  suites §5 never listed). Re-verify, do not quote either.
  ⭐ §3's TRAPS and §4's DECISIONS remain TRUE and are still worth reading.
known-rot: nothing.
known-conflict: none.
related-designs:
  - DESIGN_Occurrence_Scoped_Storage.md — ⭐ THE OWNING DESIGN. §30 = P4, §31 = O7c end to end,
    ⭐⭐ §32 = E5, THE NEXT SLICE (READY-TO-BUILD).
  - PLAN_Occurrence_Storage_Build.md — the task ladder; row E4 carries O7c's re-rating.
  - Architect_Question_36_Subtree_Hosting_Runtime.md — APPROVED; the options and the ruling behind §32.
  - Blueprint_Issues_Tracker.md — CE-320/325..331 DONE · open: CE-321 ② (combat), CE-332 (UI),
    CE-333 + CE-334 + CE-335 (ours)
    · CE-322 (inertia, DONE) · CE-318 (deferred) · CE-300/301 (open).
  - RUNBOOK_Cluster_Debugging_Over_Http.md — how to run the golden. §1.1 and §4 are load-bearing.
-->

# RESUMPTION — **occurrence-scoped storage**, `behaviors` lane

RELEARN

---

## 0. ⭐⭐⭐ NEXT: **`E5` — an HSM state hosts a BTree.** 📄 `DESIGN_Occurrence_Scoped_Storage.md` **§32**

> ✅ **NOTHING IS IN FLIGHT.** Branch `behaviors`, tree clean, everything pushed.
> ⛔ `git stash@{0}` holds *"EXPERIMENT: RootParamsBytes always 100 — probe only"* — **a diagnostic
> that must NEVER be committed.** Leave it stashed.

### 🔒 FIRST ACTION ON RESUME

⭐ **Read `DESIGN_Occurrence_Scoped_Storage.md` §32 end to end**, in this order: **§32.2** *(the review
— eight findings, the two blocking ones)* → **§32.3** *(the decision)* → §32.4–§32.6 *(the three UML
diagrams)* → §32.8 *(seven items)* → §32.10 *(acceptance `A1`–`A8`)*.
⛔ **Do NOT re-derive it, and do NOT start from `Q36`** — that document holds the options and the
approval; §32 is the buildable shape.
⚠⚠ **`build-state` is `DESIGN`, not `READY-TO-BUILD`** — it was demoted on `2026-09-23` by its own
review. ⛔ **The pre-review §32 is SUPERSEDED and its diagrams were DELETED**; what it claimed is
recorded in the file's closing `## ⛔ HISTORY — §32's pre-review shape`. **Never quote it.**

### ⭐⭐ WHAT `E5` IS, AFTER THE REVIEW

✅ **`Q36` is APPROVED** *(user, `2026-09-23`: "Q36 approved")* — `Q36-A` = **B**, the host ticks the
child inline; `Q36-B` = **A**, `SubtreeName` beside the Guid. **Both still stand.**
🔒 **And the HOST is now `BrainTickSystem`, not a generated `[HsmAction]`** *(user, `2026-09-23`:
**"go with b"**)*. ⛔⛔ **`E5` is therefore NO LONGER a pure code-generation slice** — it adds a
registry (`HsmHostedSubtrees`, copied from `HsmParamBindings`) and a branch in `BrainTickSystem`.
⭐ Zero `ExtDeps` change: `HsmKernel.GetActiveLeafIds`, `HsmDefinitionBlob.GetState` and
`StateDef.ParentIndex` are **already public** *(§32.1 ⑭)*.

### ⛔⛔ THE TWO BLOCKING FINDINGS — **why the old plan could not have worked**

| # | |
|---|---|
| **F1** | 🔴 **A hosted child wired to an HSM action would tick ONCE, not every frame.** `UpdateBatchCore` runs **one phase per tick** (`HsmKernelCore.cs:49-71`); `Idle` advances only on a non-empty queue (`:108-116`); `Activity` ends by setting `Idle` (`:442-458`). ⇒ on a quiescent machine `ActivityAction` fires **exactly once**. 📌 The same fixed point §31.18.1 already proved for `CE-322`. 📋 **`CE-334`** — and the shipped APC machine pays it today (`ApcHsmSetup.cs:70`, no timers) |
| **F2** | 🔴 **Nothing declared the tree-state slot, and `HostedSubtree.Tick` THROWS on one** (`HostedSubtree.cs:144-149`). The BTree side ships it with the call on purpose — *"THIS AND THE ORCHESTRATOR'S HOSTING CALL SHIP TOGETHER OR NEITHER"* (`BTreeBridgeEmitCore.cs:1045`). ⭐ Now **§32.8 item 3** |

### ⛔⛔ FOUR THINGS THAT WILL MISLEAD YOU — **all drawn in §32.6's module diagram**

| # | |
|---|---|
| **1** | 🔴 **`HsmOrchestratorEmitCore` is the ALIAS arm, NOT the state-hosting arm** — it collects from `dto.Aliases`, returns `null` for every shipped asset, and **its emission does not compile** *(`CE-333`)*. ⛔ Do not copy it |
| **2** | 🔴🔴 **NEITHER IS `BTreeOrchestratorEmitCore` — it does not compile either** *(`CE-335`, found `2026-09-23`)*. Both arms emit `{Child}.GetInterpreter()`, which is **defined nowhere**: `find.sh` agrees across graph and grep — 3 emitter sites, 3 text assertions, **0 definitions**. ⛔⛔ §32 called it *"the MODEL to copy"* and that was wrong |
| **3** | 🔴 **`NodeType.Subtree` is a STUB** — `Interpreter.cs:249` returns `NodeStatus.Failure`. ⛔ Not the mechanism |
| **4** | ⚠ **`StateNodeDto.SubtreeAssetId` is read by NOTHING that emits a tick** — the Guid ships, the name does not, and item 1 is adding it |

### ⭐ AND THE ONE PIECE OF PRIOR ART TO ACTUALLY COPY

⭐⭐⭐ **`HsmParamBindings.Register(blob, (Guid StableId, int Offset)[])`** *(`Behavior/HsmParamBindings.cs:43,58-80`)* —
a side table baked as authoring `StableId`s and joined to the kernel's flat state indices at runtime
through `MachineMetadata.StateStableIds`. 🔒 **`HsmHostedSubtrees` is that, with a different payload.**
⛔ It is what keeps the emitter off the flattener's ordering, and the first INVENTORY missed it.

---

## 0a. ✅ WHAT THIS SESSION LANDED *(`2026-09-23`)* — **all pushed, all gated**

| row | |
|---|---|
| **`CE-325`** ✅ | `SelectTier` defers to `CheckTierBudget`. `Fhsm.Tests` **307/307**. Tier 1 keeps a measured `regions <= 1` POLICY gate — 64 is the only tier with no reserved interrupt slot. 📄 §31.24 |
| **`CE-326`** ✅ | `BP1200`/`BP1201` read the tier ladder, not the widths of two deleted components. 📄 §30.30.1 |
| **`CE-327`** ✅ | `[SharedAiHeavy*]` retired — superseded on BOTH arms. 📄 §30.29 |
| **`CE-328`** ✅ | `Register` throws on an undeclared params width; the 100-byte fallback is gone. 📄 §30.30.2 |
| **`CE-329`** ✅ | the two projects `P4`/`CE-314` left non-compiling. 📄 §30.31.1 |
| **`CE-330`** ✅ | `ActionSchemaEntry.HeavyDtoType` + `ActionHosting.Heavy` deleted, 62 sites / 19 files. 📄 §30.31.2 |
| **`CE-331`** ✅ | `ParseParamsDelegate` takes an `int capacity`. 📄 §30.31.3 |
| **`Q36`** ✅ | APPROVED, corrected, and folded into **§32** |

---

## 0b. ⛔ OPEN, AND NONE OF IT IS OURS TO FIX UNASKED

| row | what | lane |
|---|---|---|
| **`CE-321` ②** | 120 rounds, correct target, in range, bullets live on 53 ticks, `Health.Current` never leaves 100 ⇒ `WeaponFireIntent → FireProcessing → Raycast → HitResolution → Damage` | **combat pipeline** |
| **`CE-332`** | 7 `Hrot.IG.Tests` translator rails that have **never run** — measured to contain zero references to anything this programme touched | **UI** |
| **BTree-hosts-BTree** | ⛔ **NOT BUILT, and no longer expressible the old way** — both orchestrator arms are retired (`CE-337`). ⭐ It is `E5`'s shape with the NODE's visual id as the site: a `{SubtreeAssetId, SubtreeName}` pair, `ComputeTreeStateKey`, a `HostedChildren` binding, and a per-frame tick. ⛔ A SLICE, not a patch — and nothing should re-wire an emitter to `OrchestratorAliasCollector` to fake it | ⭐ **ours, when wanted** |
| ⚠ **`CE-334`'s residual risk** | an activity action that **assumes a component** now runs in states and on entities where it previously lay dormant ⇒ it throws where it used to be silent. 📐 Today's only shipped activity action is `Activity_Cruise` and production APCs carry `LocomotionChannel` — but **this is the failure mode to expect if a new HSM misbehaves** | ⭐ watch, not a task |
| ✅ **`E5` item 6** | ✅ **DONE `2026-09-26` as `CE-338`** (§32.15) — both resolvers now reach `HsmGraphModel` from **both** production call sites; new `IStatefulScopeAsset` unblocked `CgfSubsystem`. 🔴 **The deferral below was WRONG** and is kept only so the correction is legible |
| ⛔ ~~**`E5` items 6-7**~~ | ~~wire `HsmValidator`'s `isStatefulSubtree`/`sharedScopeKeys` … `AiEditorAdapterBundle` carries **no asset catalogue** to build one from ⇒ it needs a service the HSM editor's composition root does not have~~ 🔴 **REFUTED `2026-09-26`:** `IAssetCatalog` exists with five contributors, **and both resolvers were already written and already handed to `HsmAssetValidator`** — what was missing was **one argument**. ⚠ The "no catalogue" half was TRUE and **irrelevant**: the fix passes the ANSWER, not the source | — |
| ✅ **`E5` item 7** | ✅ **DONE `2026-09-26` as `CE-339`** (§32.16) — one `SubtreeCycleDetector` over `ISubtreeHostingAsset`, wired into **both** validators. ⭐ The BTree arm can fire on a shipped asset; the HSM arm waits on authoring |
| ⛔ **HSM subtree AUTHORING** | 🔴 **the REAL blocker, and `CE-338` is what exposed it:** rules 8/8b are wired and still cannot fire, because **no asset can declare a hosting state** — `HsmFacets.StateFacet` exposes `OnEntryAction`/`OnExitAction`/`ActivityAction`/`TimerAction` and **no subtree fields**, and nothing outside the mapper writes `StateNode.SubtreeAssetId` | ⚠ **editor-lane shaped** — ask before absorbing |
| **`E5` `A1`** | round-trip `SubtreeName` through real JSON text into a fresh model | ⭐ ours, small |
| **`CE-334`** | 🔴 **an HSM `ActivityAction` runs ONCE on a quiescent machine** — the per-tick hook does not exist. Bites a shipped asset today. ⛔ Needs its own approval: the candidate fix is an `ExtDeps` change touching every HSM | ⭐ **ours** — `E5` ROUTES AROUND it (decision `B`), it does not fix it |
| **`CE-335`** | 🔴 **the BTree alias arm does not compile either** — `{Child}.GetInterpreter()` is defined nowhere | ⭐ **ours** — one rail that COMPILES an emitted orchestrator closes it and `CE-333` together, and that rail is `E5`'s `A2` |

---

## 0c. 🔒 METHOD LESSONS FROM THIS SESSION — **each one cost real time**

| # | |
|---|---|
| **1** | ⛔⛔ **For a DELEGATE SIGNATURE change, do not predict the site count — change it and let the COMPILER enumerate.** 📐 I estimated 21 and it was ~double, found over four build rounds: a grep for the shape misses **untyped lambdas**, **alternate parameter names** *(`ptr` vs `mem`)* and **`!`-suffixed invocations** *(`def.ParseParams!(`)* |
| **2** | ⛔⛔ **A mechanical refactor's COMPLETENESS claim comes from a BUILD, never from the script's own count.** 📐 My sweep said *"61 of 62"* and was wrong about the **denominator**: it missed target-typed `=> new(…)` and aborted its whole walk on a **non-UTF-8 file** |
| **3** | 🔴 **An unrestored project is SKIPPED, and its silence reads as a pass** — paid out **twice** in one session: it hid a compile break *(`CE-329`)* and, behind it, **seven failing rails** *(`CE-332`)*. ⇒ **a full-solution build is only a gate AFTER a full restore** |
| **4** | ⚠ **BASELINE BEFORE YOU RUN, not after.** I ran suites first and had to go back and measure to separate my reds from pre-existing ones |
| **5** | ⚠ **A fresh worktree is not automatically a valid baseline environment**, and ⛔ **do not tear one down while a run is still using it** — I killed the `Hrot.Blueprints.Tests` leg that way |
| **6** | ⛔⛔ **A TEXT-ASSERTING golden cannot tell you the code it pins is not valid C#** — that is exactly how `CE-333` survived. 🔒 §32.8 `A2` makes "it COMPILES" an acceptance item |
| **7** | 🔴🔴 **A DESIGN CAN CONTRADICT ITS OWN DOCUMENT.** §32 assumed a per-frame HSM hook while §31.18.1 — **in the same file, written the same day** — already proved the phase machine's fixed point. ⇒ `R-129`'s *"read the owning design"* bites hardest when the owning design **is the one you are writing**: re-read the sections your new one depends on, not only the ones it cites |
| **15** | ⛔⛔ **FOUR PRIOR ARTS AND NONE APPLICABLE — the seam law's OTHER failure mode.** 📐 `CE-339`: `search_graph` found four cycle detectors; every one was INTRA-asset and reusing any would have pinned the wrong graph. ⭐ **The usual risk is missing a seam; this was four seams inviting the wrong reuse.** 🔒 And two more that LOOKED right failed on measurement — `IAssetCatalog.WhereDependsOn` is a **stub whose own rail is named `WhereDependsOn_ReturnsEmpty`**, and `ReferenceCatalog` models asset→sub-element, not asset→asset. ⇒ ⭐⭐ **"a seam exists with the right NAME" is not "the seam holds the DATA"** — open the implementation, every time. |
| **16** | ⭐⭐⭐ **A RAIL CAUGHT MY DESIGN ERROR THAT REVIEW DID NOT.** 📌 `CE-339`'s first cut emitted the cycle diagnostic with **no target element**, justified in prose by *"for `A→B→A` opened at `B`, no state of `B` is at fault"* — which is **wrong** *(a ring has no innocent edge)* and also made the rule **invisible on the canvas**. ⛔ I had written that reasoning into the design AND two doc-comments before the forwarding rail reddened. ⇒ ⭐ **the forwarding rail earns its keep twice: once against a caller that forgets to pass, once against an author who is confidently wrong about what the dependency is FOR.** |
| **14** | ⛔⛔⛔ **A DEFERRAL IS A CLAIM, AND IT NEEDS THE SAME MEASUREMENT AS A LEAN.** 📐 `2026-09-26`: I deferred `E5` item 6 as *"it needs a service the composition root does not have"* — ⭐ **the service existed, with five contributors; both resolvers were already written; both were already handed to the OTHER consumer.** What was missing was **one argument**. 🔒 The deferral's one measured fact *(`AiEditorAdapterBundle` has no catalogue)* was **true and irrelevant** — it settled *"can this class build the answer?"* when the question was *"does the answer exist anywhere?"* ⇒ ⭐⭐ **"X is not available" is an ABSENCE claim and falls under the enumeration rule** — `search_graph` first, never a glance at one composition root. ⚠ **The user overturned it in one sentence** *("if nothing provides what the HSM code needs, that is a sign we might need to build it")* — ⛔ **a deferral that survives only until someone questions it was never measured.** |
| **13** | 🔒🔒 **A WORKAROUND INSIDE A RAIL IS WHY NO RAIL SEES THE DEFECT.** 📐 `CE-334`'s one-shot had **five** workarounds; two lived in test suites — the engine's own rail enqueued a **dummy event matching no transition** to *"hit Activity phase again"*, and an HROT rail relied on *"Idle with an empty queue is a no-op tick"*. ⇒ ⭐ when a fixture does something ODD to provoke normal behaviour, the oddity is the bug report. **Read fixtures for what they work around, not just what they assert** |
| **12** | ⛔⛔ **WATCH FOR A JUSTIFICATION THAT KEEPS MOVING.** 📌 I defended keeping one orphaned type THREE times — *a named future use* · *a contract worth pinning* · *awkward plumbing to re-add* — and each fell to one measurement *(the payload already carries it · `P4` dissolved its question · it is ONE LINE, `rawFiles.Collect()`)*. 🔒 **A conclusion that survives by changing its reason is not surviving.** ⭐ When the argument moves twice, measure the thing instead of arguing for it |
| **11** | 🔒🔒 **"UNREFERENCED" IS ONLY EVIDENCE WHEN SOMEONE ELSE MADE IT SO.** 📌 I removed a per-compile parse justifying it as *"zero production callers"* — an orphan **my own commit had created minutes earlier**, and in the same pass I deleted its only rail. ⇒ ⭐ when YOUR change orphans something, the test is not *"who calls it"* but ***"what capability is this, and is it still wanted?"*** — and if yes, **keep it AND keep it tested**; a kept-but-unexercised type is a capability nobody dares re-wire |
| **10** | ⛔⛔ **A RETIREMENT CASCADES, AND THE CASCADE IS THE WORK.** 📐 Retiring one emitter orphaned a collector, a projection's output, a per-compile catalogue parse and 12 rails. ⭐ Each needed its own verdict — KEEP-and-tombstone, STOP-declaring, DOCUMENT, re-home or delete — and *"delete it too"* was the right answer for **none** of them. 🔒 Budget the sweep, not just the removal |
| **9** | ⭐⭐⭐ **ONE COMPILE RAIL BEAT FOUR TEXT RAILS.** 📐 `CE-336`'s first run found **three** further defects in an emitter four text rails had been asserting past for months — a `Name =` argument on an attribute with no such property, two EMPTY type names, and a fallback naming a type `P4` deleted. 🔒 When a rail asserts EMITTED CODE, assert that it **compiles**; the text assertions are then about intent, not validity |
| **8** | ⛔⛔ **"The model to copy" is a CLAIM, and it needs the same measurement as any other.** §32 rated `BTreeOrchestratorEmitCore` ⭐ from its text and 🔴 the HSM twin — 📐 they are the SAME arm, same collector, same emptiness, and **both** emit a method that does not exist (`CE-335`) |

---

## 0d. 📐 GATE BASELINES — **measured `2026-09-23` at `b6d2501b6`; re-verify, do not quote**

| suite | |
|---|---|
| `Fdp.Toolkits.Tests` | **2328 / 2328** |
| `Hrot.Blueprints.Tests` | **4054**, 18 skipped — ⚠ `DEBT-AIB-030`-style rotating flake seen twice; a single red whose identity CHANGES between runs is not a regression |
| `Hrot.BTree.Editor.Tests` | **629 / 629** ⭐ re-measured `2026-09-26` *(+5: `CE-339`'s BTree cycle rails)*. ⚠ **633 → 624 is ACCOUNTED FOR, not a loss:** `CE-337` removed exactly **9 `[Fact]`s** from `BTreeOrchestratorEmitterTests` *(git-diff measured)* — the rails that asserted the emitted text of both now-retired arms. ⭐ Four rails remain and pin the SUPERSESSION by name |
| `Hrot.Hsm.Editor.Tests` | **577 / 577** ⭐ re-measured `2026-09-26` *(+3 for `A1`/`CE-338`, then +7 for `CE-339`: 6 cycle rails + 1 forwarding rail)* |
| `Fhsm.Tests` | **307 / 307** ⚠ out of the root solution — build it, never `--no-build` |
| `Hrot.Editor.AiShared.Tests` | **2057 / 2059** — ⭐ +13 `SubtreeCycleDetectorTests` (`CE-339`); ⛔ the SAME 1 red `Aie030`, **baselined pre-existing** |
| `Hrot.Editor.Tests` | 422 / 424 — ⛔ 1 red `AiHotReloadCoordinatorTests.TwoReloadCycles_OldAlcIsCollected`, **pre-existing and already filed as `BP-548`** *(a GC/ALC-collection assertion; measured there against base `2500ced2d` as failing on BOTH trees in full runs)*. ⭐ **19/19 green under `--filter` `2026-09-26`**, matching `BP-548`'s documented shape exactly |
| `Hrot.AiEditor.Generators.Tests` | 282 / 286 — ⛔ 4 red `S3`/`T30` shared-slot demos, **baselined pre-existing at HEAD in a worktree** |
| `Hrot.IG.Tests` | 427 / 435 — ⛔ 7 red, **`CE-332`**, never ran before this session |
| doc gates | `tracker-counts` OK · `rulings-check` **38/38** · `design-digest` OK · `mermaid-check` **30/30** |

🔒 **Golden regeneration:** `AI_REGENERATE_SNAPSHOTS=1` for the AI corpus. ⭐ Report movement as a
**diff SHAPE** — `CE-331`'s was *16 files, 16 insertions, 16 deletions, ONE unique change*.

---

## 0e. 🔒 STANDING CONSTRAINTS — **binding, carried verbatim**

| | |
|---|---|
| ⭐ **branch** | push only to `behaviors`: `git push -u origin behaviors`, retry network errors 2s/4s/8s/16s |
| ⛔⛔ **the stash** | `stash@{0}` is a diagnostic that must **NEVER** be committed |
| ⭐ **questions** | plain chat prose, ⛔ **never** the `AskUserQuestion` widget |
| ⭐ **links** | docs AND task ids as **GitHub blob links on `behaviors`** — the user is on mobile — and gloss every id on first mention |
| ⭐ **slow work** | builds, tests and wide searches **in the background** |
| ⛔ **`RW-S` rows** | do not "fix" the tracker's `RW-S` counting gap *(known, `CE-259at`)* |
| ⛔ **attribution** | no model identifier in commits, PR titles/bodies, code comments or any repo artefact |
| ⭐ **commit trailer** | `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>` + `Claude-Session: https://claude.ai/code/session_01Y7DZG7BBQ8rNTesibu9NXo` |
| ⛔ **PRs** | do not open one unless explicitly asked |
---

> ⭐⭐ **Branch `behaviors`. Tree clean, everything pushed.**
> ⛔ `git stash@{0}` holds *"EXPERIMENT: RootParamsBytes always 100 — probe only"* — **a diagnostic
> that must NEVER be committed.** Leave it stashed.

---

## 1. ⭐ WHERE IT STANDS — **`O7c` is finished** *(still true; §0a adds what came after)*

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

## 2. ⛔ WHAT IS LEFT IN THE LANE — **SUPERSEDED `2026-09-23`, use §0b**

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

## 5. ⛔ GATE BASELINES — **SUPERSEDED `2026-09-23`, use §0d** *(measured at the ④d commit)*

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
