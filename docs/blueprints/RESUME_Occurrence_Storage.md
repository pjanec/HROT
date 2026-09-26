<!--STATUS
state: LIVE
doc-type: THE resumption doc for the `behaviors` lane — programme: OCCURRENCE-SCOPED STORAGE.
  ⚠ A STATE doc, not canon. Every "green"/"pushed"/"HEAD" line is a snapshot dated below.
  ⛔ VERIFY against git before acting ("THE LEDGER MAY NOT ASSERT WHAT THE CODE IS").
updated: 2026-09-26
build-state: ✅ **`E5` IS COMPLETE (all 7 items + `A1`) AND THE EDITOR⇄CGF DEDUPLICATION IS FINISHED.**
  ✅ **`CE-349` IS DONE `2026-09-26` — the AI debuggers use the host's ONE time control on BOTH
  hosts, and the per-host subclass is deleted (§32.24).**
  ✅ **`CE-351` IS DONE `2026-09-26` — the runtime panes reach CGF (§32.25). 🔴 But the ROW'S OWN
  LEAN WAS WRONG and the `T3` red it promised to fix is NOT fixable by this lane — `CE-303` deleted
  the panel kind the rail asks for, and the rail lives in the BACKEND lane's harness. Filed `CE-353`.**
  ⭐⭐⭐ **NEXT IS `CE-350` — see §0.** Branch `behaviors`, everything PUSHED.
  ⛔ HISTORY of this programme's slices follows; read §0 FIRST, not this block.
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
  🔴 `CE-341` DONE `2026-09-26` — **the user caught a duplication `CE-338` created**: the editor and
  CGF carried **BYTE-IDENTICAL** copies of the rule-8/8b resolvers. ⛔ The justification *("only this
  assembly sees both asset types")* had been dissolved by `IStatefulScopeAsset` **in the same commit**,
  and I did not re-check it. ⭐ One `StatefulScopeQueries` in AiShared; `HsmValidator` derives both
  from the catalogue; production passes ONE argument; both copies deleted. §32.17.
  ✅ `CE-340` DONE `2026-09-26` (§32.18) — **ONE `AiDocumentViewStateBinder`, called by both hosts.**
  📐 Capture measurement first (`Q60`/`HN-037`): of a dozen inputs only TWO differ — the debug
  sessions and the tail — and two "different" ones were **the same object under another local name**.
  ⛔ Needed a NEW assembly `Hrot.Editor.AiComposition`: the binder must call all three factories and
  the editors reference AiShared, not the reverse; **only 4 csproj in the repo see all three**, two of
  them the hosts themselves — which IS why it was duplicated.
  ✅ `CE-342` + `CE-343` DONE `2026-09-26` (§32.19) — **THE DEDUPLICATION IS FINISHED.**
  `AiAssetCatalogComposer` *(the catalogue — note most of that path was ALREADY shared by J1/J2; only
  the CONSTRUCTION was duplicated, and AiShared could never hold it because it cannot name the
  contributor types)* and `AiActiveDocumentBinder` *(the three stores + the 7-arg outline retarget)*.
  🔴 `CE-343` nearly introduced the exact bug this programme closes: the editor wires
  `ActiveChanged` ~900 lines BEFORE it assigns `_blueprintMyBlueprintWindow`, so capturing it by value
  would have passed null forever — **editor only**. Fixed as a provider, pinned by a rail.
  ⚠ An existing rail (`TheAssetRootsComeFromTheOneResolverTests`) reddened and was **re-pointed**, not
  weakened — its setup no longer produces its situation; the new pair is strictly stronger.
  ⭐⭐ **All four copies CE-340 found are now shared. All four had ONE cause: AiShared was FROZEN, so
  copying was the only legal move — and the freeze was lifted `2026-08-25` with nothing revisiting
  them.** 🔒 A lifted freeze does not un-write its workarounds; someone has to go back.
  🔴🔴 `CE-344` + `CE-346` DONE, `CE-345`/`CE-347` FILED (§32.20) — **the user challenged my
  closing line that the hosts "legitimately differ" in debug sessions / picker drawers / scenario
  sources. ALL THREE FAILED MEASUREMENT.** CGF HAS a `BlueprintDebugSession` (built + attached 362
  lines before the shell) and passed `null`; the two scenario roots are the SAME path spelled twice;
  CGF has the BTree/HSM registrars with **0** picker calls vs the editor's **13**, and no design says
  it should. ⚠ A stale comment *("CGF constructs NO debug sessions")* made the omission look
  deliberate to author AND reviewer.
  ✅ `CE-345` + `CE-347` DONE `2026-09-26` (§32.21). **`CE-347` was a genuine wiring job** — every
  prerequisite already on CGF — and the arms moved to a shared `AiFacetPickerBinder` rather than
  being copied *(a fifth duplicate in the session that removed four)*. 🔴 **`CE-345`'s premise was
  HALF WRONG:** the sessions buy REAL symbolication (`SetDebugMetadata`), but
  `BTreeDebugSession.Update` — the trace poll — has **ZERO callers on EITHER host** ⇒ wiring CGF
  makes them *equally unwired*. Filed as `CE-348`; ⛔ not "BTree debugging now works on CGF".
  ✅ `CE-348` DONE `2026-09-26` (§32.22) — **the kernel adapter the design deferred at "Slice 3+".**
  🔒 The user corrected my READING *(under-adoption, not un-necessity)* and the corpus agreed:
  `Hrot.BTree.Editor.md:294` says the `Record*` methods exist *"for the FUTURE kernel adapter"*.
  ⭐ Anchor = **entity-inspector selection** (user's option 1), which turned out to be the DESIGNED
  anchor (`SharedEntitySelection`, `CE-301`). `AiDebugSessionPump` in AiComposition, both hosts,
  pumped from the canvas `AfterDraw`. ⭐⭐ The snapshot half needs **no arming** — `Update` reads the
  running node from `RootStateAccess`, the occurrence root slot **we** built in `CE-319` ⇒ four
  already-registered overlays stop sitting dark. ⚠ History/heatmap still need arming; pause/step on
  CGF still needs an `ITimeCommands`.
  📋 OPEN: HSM subtree AUTHORING (**the real blocker**) · BTree-hosts-BTree (not built).
  ✅ `O7c` COMPLETE · `CE-325`..`CE-331` all DONE.
current-answer: ⭐⭐⭐ START AT §0 — it names the NEXT ACTION (`CE-350`), the ONE document to read
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

## 0. ⭐⭐⭐ NEXT: **`CE-350`** — ✅ **`CE-349` AND `CE-351` ARE DONE**

> ✅ **NOTHING IS IN FLIGHT.** Branch `behaviors`, tree clean, everything pushed.
> ⛔ `git stash@{0}` holds *"EXPERIMENT: RootParamsBytes always 100 — probe only"* — **a diagnostic
> that must NEVER be committed.** Leave it stashed.
> ⚠⚠ **ONE VERIFICATION IS UNRESOLVED** — see §0z.

### ✅ `CE-349` IS BUILT — **do not re-derive it; 📄 `DESIGN_Occurrence_Scoped_Storage.md` §32.24 is the as-built**

🔒 **User, `2026-09-26`:** *"cgf should use the cluster time control but i think editor should do the
same as editor also has its local cluster orchestrator so for consistency they should be using same
time control means."* → **"go with 1, file 2 as follow-up."** ⇒ ✅ **option 1 shipped.**

| ⭐ what landed | |
|---|---|
| ⭐ `AiTracerCoordinator` takes `IEngineDebugTimeController?` | its three virtuals **forward** (`Continue`→`Resume`) instead of being empty |
| ⛔ `Hrot.Editor.Debug.EditorAiTracerCoordinator` **DELETED** | ⚠⚠ **NOT** the same-named `Hrot.Editor.DebugApi.EditorAiTracerCoordinator`, which arms trace buffers and is untouched — **two live classes share that name** |
| ⭐⭐ new `AiDebugSessionComposer` *(`Hrot.Editor.AiComposition`)* | builds coordinator + BOTH sessions, and **throws on a null controller** — the rail that makes the `T4d` defect unreachable rather than fixed-once |
| ⭐⭐ `EditorSubsystem` **hoists** `_bpTimeAdapter` | from the breakpoint block to the AI-debug block, so ONE adapter serves both. ⚠ Verified straight-line: **no `return` between `:1047` and `:1760`** |
| ⭐ `CgfSubsystem` passes `_debugTimeController` | ⇒ **BTree/HSM pause, step and continue are LIVE on CGF for the first time** |
| ⭐ the DI registration **resolves** the controller | instead of silently defaulting it away |

⚠⚠ **THE PROPERTY GIVEN UP, so nobody rediscovers it as a regression:** the deleted subclass published
time **INTENTS** *(cluster fan-out for free)*; the adapter calls the local `MasterSyncController`
directly. ⭐ That is the path the editor's OWN Blueprint session and breakpoint manager always took —
so the asymmetry is **removed**, not created. ⇒ if direct-vs-intent is wrong it is now wrong in ONE
place for all four debuggers, which is `CE-350`'s to fix.

⭐ **Gates:** the feature's own rail `TheTracerCoordinatorActuallyControlsTimeTests` **4/4**, and
**red-proved** — reintroducing the `T4d` no-op reddens 2 of the 4. `Hrot.Editor.Tests` **424/0/1skip**,
`Hrot.BTree.Editor.Tests` **629/0**, `Hrot.Hsm.Editor.Tests` **579/0**,
⭐⭐ `Hrot.Editor.AiShared.Tests` **2073/0/1skip — BETTER than the baseline, which carried one red.**
`Hrot.CGF` + every touched project build clean. ⚠ Needed one **new project reference**
(`Hrot.Editor.Tests` → `Hrot.Editor.AiComposition`): the transitive one through `Hrot.Editor` did not
surface the namespace at compile time.

✅ **`CE-352` — and it is OURS, not `CE-349`'s.** Gating turned up
`Aie030DebugSessionRegistryIntegrationTests.Contributor_WiresDebugMetadata_IntoSession` RED; a
**worktree at `3ed397ef9`** proved it red at the base commit too, and it was last touched by
`6f64208d6` *(`CE-319`)*. 🔴 Two stages: ① a `BehaviorState` added without being registered ⇒ the
fixture **threw in setup**, asserting nothing while looking like a passing shape; ② once fixed, the
snapshot was **null**, because `RootStateAccess.EnsureRootState` **skips silently when the tier
components are not registered** *(deliberate §27.2 behaviour for `Fdp.Toolkits` hosts)*. ⭐ Fixed by
registering `BehaviorState` + `BlueprintTierTable.RegisterAll` — what `RootParamsTestHarness` already
demands. 📄 §32.24.5. 🔒 **Carry the lesson:** a correct silent-skip in production provisioning makes
a test fixture fail as a plausible `null`, not as an error.

### ✅ `CE-351` IS BUILT — **and its recorded lean was WRONG; 📄 §32.25 is the as-built**

🔴🔴 **MEASURED BEFORE BUILDING, and it inverted the task.** The row promised *"register the
runtime-inspector pane on CGF → turns the conformance rail green."* 📐 `scripts/find.sh
runtime-inspector` returns **ZERO production `.cs` hits** — the only code hit is the rail itself —
because **`CE-303` (`2026-09-21`) DISSOLVED `RuntimeInspectorWindow`**; `IRuntimeInspectorPane`'s own
header says *"a pane is now reached through `details.runtime.<kind>` and nothing else."*
⇒ ⛔ **neither host publishes that panel kind, so no CGF wiring could ever have turned that rail
green, and the rail would fail identically against the EDITOR.**

🔒 **The method note, because it is the mirror of this session's other error:** the wrong half was
read off the conformance **baseline COMMENT at `:242`**, written before `CE-303`. ⭐⭐⭐ **A stale
comment is not a measurement** — one error claimed an absence from a type NAME, this one a presence
from a COMMENT; both are *"the corpus said so"* standing in for *"I looked."*

⭐⭐ **THE GAP THE ROW HALF-SAW IS REAL, AND IS FIXED.** `RegisterRuntimePane` had **3** call sites,
all `EditorSubsystem`, and **0** on CGF ⇒ CGF offered **no** `details.runtime.<kind>` view at all.
New `AiRuntimePaneBinder` in `Hrot.Editor.AiComposition`, called by both hosts; the editor's three
guarded blocks **deleted, not copied**; the per-kind `if (session != null)` guard preserved; the
Blueprint asset-id resolver moved in; `Documents` is a **`Func<>` provider**, ⛔ **red-proved** —
capturing it *(the `CE-343` shape)* reddens exactly one of the four new rails.

### ⭐ NEXT — **`CE-350`, and `CE-353` belongs to another lane**

| # | | |
|---|---|---|
| **1** | 📋 **`CE-350`** *(move `IEngineDebugTimeController` out of `Hrot.Blueprints.Core`)* | the follow-up the user asked to be filed separately: neutral home *(lean: `Hrot.Diagnostics.Breakpoints`)* + retire the `[Obsolete] IBlueprintTimeController` alias. ⛔ Its own batch — it touches Blueprints, Breakpoints, CGF and the editor at once |
| **2** | ⛔ **`CE-353`** *(the stale conformance-rail entry)* | **NOT OURS.** Delete `"runtime-inspector"` from `ported[]`, delete `DivergesByDesign["runtime-inspector"]`, trim reason (2) of `DivergesByDesign["details"]`. ⚠ All in `Hrot/Runner/Hrot.SystemTests/Conformance/ClusterConformanceRails.cs` — the **BACKEND lane's** harness ⇒ STOP-and-report, not a judgement call |

### ⭐ AFTER THAT — the open queue, in the order last discussed

| # | | |
|---|---|---|
| 1 | ⭐⭐ **HSM subtree AUTHORING** | 🔴 **THE REAL BLOCKER for everything `E5` built.** `HsmFacets.StateFacet` exposes only `OnEntryAction`/`OnExitAction`/`ActivityAction`/`TimerAction` — **no subtree fields** — and nothing outside the mapper writes `StateNode.SubtreeAssetId`/`SubtreeName` ⇒ **no asset can declare a hosting state**, so `E5`'s runtime and validator rules 8/8b/10 are unreachable on a real asset |
| 2 | ⚠ **arm the trace buffers** | `CE-348` pumps the session, so the **snapshot** half is live; the **history + heatmap** halves still need `BTreeTraceWorkingMemory1024` armed — one opt-in away via `EditorAiTracerCoordinator.ArmEntity`'s `PatchDebugStateCommand` |
| 3 | ⛔ **BTree-hosts-BTree** | not built; `E5`'s shape with the NODE's visual id as the site |
| 4 | ⚠ **`CE-321` ②** *(combat pipeline)* · **`CE-332`** *(7 `Hrot.IG.Tests` rails)* | ⛔ **not ours** — other lanes |

### ⚠⚠ §0z — THE ONE UNRESOLVED VERIFICATION

⛔ **The `T3` system suite has NOT been confirmed green for this session's work.** 📐 What happened:
the first run was piped through `tail -40`, so the exit code observed was **`tail`'s, not the
suite's**, and the summary was truncated away. ⭐ What IS known from that run: the editor booted
headless, cycled Scenario → BTree → Blueprint → HSM → Scenario, the cluster stepped six times and it
shut down cleanly — ⛔ **encouraging, but NOT a verdict.** A re-run with full capture
(`scripts/run-system-tests.sh --no-build > <file> 2>&1`) was started and had not finished.

✅ **RESOLVED `2026-09-26` — the re-run landed. Read this instead of re-running blind:**

| 📐 result | |
|---|---|
| ⭐⭐⭐ **`SAME: 11 · DIFFERENT: 0`** *(+3 declared by design, +2 declared subset)* | 🔒 **editor⇄cluster parity is INTACT** — the four composition extractions this session made to CGF (`CE-340`/`342`/`343`) did **not** regress it. That is the signal that mattered |
| ⚠ **ONE RED: `ClusterConformanceRails.The_ported_kinds_are_really_published_by_the_cluster`** | *"kind(s) [runtime-inspector] were removed from the known-absent baseline but `--mode all` does not publish them"* |
| ⭐ **PRE-EXISTING, measured** | `runtime-inspector` entered the ported list in **`3d5743a84`** (the harness commit); **no commit of this session touches `ClusterConformanceRails.cs`** |
| ⭐⭐⭐ **…but WE met its exit condition** | the baseline entry (`:242`) says *"…CGF constructs none (no `IBlueprintDebugSession`) … **Deleted when debug sessions reach CGF**"* — 🔴 `CE-344`/`CE-345` made that true ⇒ **register the pane on CGF**. Filed as **`CE-351`** |

🔒 **STILL TRUE, and the next session should keep it in mind:** ⚠ This
matters because **none of this session's CGF changes have end-to-end coverage** — they are verified by
build + the editor-side unit suites only, and `CE-340`/`342`/`343`/`345`/`347`/`348` all touched CGF's
composition root.

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
| **24** | ⛔⛔⛔ **THE INVERSE SEAM-LAW ERROR: READING `0 CALLERS` AS *DEAD* INSTEAD OF *UNADOPTED*.** 📌 `CE-348`: I measured zero callers of `BTreeDebugSession.Update` and wrote *"the trace loop is dead on both hosts."* 🔒 **The user corrected the READING, not the measurement** — *"again a sign of under adoption, not un-necessity"* — and the corpus then showed the buffers have **six live consumers** *(inspector renderers on three hosts, two flight-recorder translators, the TKB translator, breakpoint menus, the debug API)* and that the missing piece had **a name and a slice number**: *"the FUTURE kernel adapter … no-ops until kernel wiring (Slice 3+)"*. ⇒ ⭐⭐ **grep reports a count truthfully; only the owning design says what the count MEANS.** ⚠ Same mechanism as the familiar failure — characterising a measurement **before** searching `docs/`. |
| **23** | ⭐⭐⭐ **COMPARING TWO HOSTS FINDS BUGS IN THE ONE YOU TRUSTED.** 📌 `CE-345`: costing *"give CGF what the editor has"* measured that `BTreeDebugSession.Update` — the trace-poll entry point — has **ZERO callers in `Hrot/` or `FDP/`, on BOTH hosts** ⇒ the row's premise was false and the capability is inert everywhere (`CE-348`). ⇒ ⭐⭐ **asking *"why does host A lack X?"* is how you discover that host B lacks it too** — the comparison is the INSTRUMENT, not the goal. ⚠ And the corollary for reporting: *"now CGF equals the editor"* is only good news **if you also measured that the editor's version works.** |
| **21** | ⛔⛔⛔ **A LIST OF "LEGITIMATE DIFFERENCES" IS A CLAIM TABLE — AND I SHIPPED ONE WITH ZERO MEASURED ROWS.** 📌 `§32.20`: I closed two dedup reports with *"the hosts still differ in everything they should — debug sessions, picker drawers, scenario sources…"*. 🔒 The user asked why; **all three failed on the first measurement** — a constructed-and-attached `BlueprintDebugSession` passed as `null`, two spellings of one scenario path, and 0-vs-13 picker calls with no design behind the gap. ⇒ ⭐⭐ **naming a difference is exactly as load-bearing as naming a duplicate: it is what tells the next reader to STOP LOOKING.** ⛔ *"host X has none of these"* is an ABSENCE claim and falls under the enumeration rule — `grep -c` on both hosts settles it in seconds. ⚠ And it was the **third** "two spellings of one thing" that day *(`catalog`, `bpChannelCatalog`, now the scenario root)*: after the first two, the third should have been a PRIOR, not a surprise. |
| **22** | ⚠⚠ **A STALE COMMENT CAN MANUFACTURE A DIFFERENCE THAT NOBODY THEN QUESTIONS.** 📌 `CE-344`: the call site said *"CGF constructs NO debug sessions (slice 1 §9.4)"* — **true when written, false once `CE-059` built the session.** ⇒ ⛔ it made a live omission read as a deliberate one, and I repeated it as fact in two reports without opening the file. ⭐ **When a comment EXPLAINS an absence, check the absence, not the comment** — an explanation is not evidence. |
| **19** | ⛔⛔⛔ **WHEN YOU LIFT A LAMBDA'S BODY INTO A RECORD, EVERY FIELD IT READ *LATE* BECOMES A *CAPTURE*.** 📌 `CE-343`: the editor wires `ActiveChanged` at `:3575` and assigns `_blueprintMyBlueprintWindow` at `:4449`; the original read it late through `?.`. ⇒ ⛔ capturing it into the services record would have passed **null forever**, silently disabling the panel **on the editor only** — the exact split the extraction existed to close. ⚠ **A `?.` on a not-yet-assigned field is INVISIBLE at the lift site.** ⭐ The habit: for each field the old lambda touched, ask *"is this assigned before the Bind?"* — and when unsure, pass a provider, not a value. |
| **20** | ⭐⭐ **A RAIL THAT REDDENS ON A REFACTOR IS EVIDENCE, NOT AN OBSTACLE — BUT SAY WHICH KIND IT IS.** 📌 `CE-342` reddened `TheAssetRootsComeFromTheOneResolverTests` because CGF **legitimately stopped** calling `ResolveAssetsRoot`. 🔒 Per the ruling: *an assertion that must WEAKEN is a regression; a setup that no longer produces its situation is bookkeeping.* ⭐ This was the second ⇒ the anti-vacuity guard moved to the composer **and** a new rail proves both hosts reach it, which is **strictly stronger**. ⛔ The wrong move would have been deleting the theory case or loosening its assertion. |
| **17** | ⛔⛔⛔ **RE-CHECK A JUSTIFICATION AFTER YOU REMOVE ITS CAUSE — I DUPLICATED CODE ONE SCREEN BELOW THE SEAM THAT MADE IT UNNECESSARY.** 📌 `CE-338`: the rule-8/8b resolvers were delegates *because* the predicate needed a `switch` over both asset types, writable only in the one assembly that sees both. ⭐ That commit added `IStatefulScopeAsset`, which **dissolved exactly that constraint** — and then, to wire CGF, I **copied both resolvers byte-identically** into a second host. ⛔ Two implementations of one policy, which is the very mechanism that had left CGF silently missing those rules. 🔒 **The user found it by reading the REPORT** *("editor is running different code, not unified")* — no rail, no review. ⇒ ⭐⭐ **the seam law's usual failure is not adopting an EXISTING seam; this is the sharper form — not adopting the seam you just built.** ⭐ The checkable habit: **when a change removes the REASON for a workaround, grep for the workaround in the same pass.** |
| **18** | ⭐⭐ **A DUPLICATE CAN BE A FROZEN-ERA ARTEFACT — CHECK THE FREEZE BEFORE JUDGING IT.** 📐 `CE-340`: the editor/CGF document-factory duplication looks like carelessness and is not — slice-2's STATUS says `must NOT modify Hrot.Editor.AiShared (freeze owner = variable-model lane)`, so **copying was the only LEGAL move at the time.** ⚠ That freeze was **LIFTED `2026-08-25`** and nothing revisited the copies. ⇒ ⭐ **when you find duplication, ask what was forbidden when it was written** — and ⛔ **a lifted freeze does not un-write its workarounds; someone has to go back.** |
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
| `Hrot.Hsm.Editor.Tests` | **579 / 579** ⭐ re-measured `2026-09-26` *(+3 for `A1`/`CE-338`, then +7 for `CE-339`: 6 cycle rails + 1 forwarding rail, then +2 for `CE-341`)* |
| `Fhsm.Tests` | **307 / 307** ⚠ out of the root solution — build it, never `--no-build` |
| `Hrot.Editor.AiShared.Tests` | **2072 / 2074** — ⭐ +13 `SubtreeCycleDetectorTests` (`CE-339`) and +5 `AiDocumentViewStateBinderTests` (`CE-340`) and +4/+1 for `CE-342`/`CE-343`; ⛔ the SAME 1 red `Aie030`, **baselined pre-existing** |
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
