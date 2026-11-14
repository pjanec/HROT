<!--STATUS
state: LIVE
updated: 2026-08-21
current-answer: this whole file — what L5 retired, what it refused to retire, and why.
stale-below: nothing.
known-rot: none.
known-conflict: none.
-->
# ⭐⭐⭐ REPORT — **Details panel migration, layer `L5`** *(retire)*

> **Design:** 📄 [`DESIGN_Details_Panel_View_Switching.md`](../DESIGN_Details_Panel_View_Switching.md) §6 `L5`
> · 📄 [`Architect_Question_38_One_Details_Panel.md`](../Architect_Question_38_One_Details_Panel.md)'s
> retire/stay table *(`R-113`, `R-114`, ruled `2026-08-20`)*
> **started at** `71b30c3d` *(marker `53a76051`)* · **branch** `claude/hrot-implementation-j1jvin`
> ⭐ Re-synced from the coordinator at the start *(rule 7)* — ⭐ **this run's tree includes the time
> lane's drain** *(`ResumeAndDrainSystem`, `IStagedWrites`, the `PreFrame` phase)*.
> ⛔ **No diagram in this report.**

| target *(§6 `L5`'s list)* | verdict | one line |
|---|---|---|
| `LiveBlackboardPanel` | ✅ **retired** | in-degree **0**; nothing hosted it |
| `InspectorWindow` *(Blueprints)* | ✅ **retired** | a 70-line **stub** — placeholder text, not a surface |
| `BlueprintVariablesManagedWindow` | ✅ **retired** | wrapper over the window below |
| `BlueprintVariablesWindow` | ✅ **retired** | ⚠ **the CLASS, not the file** — see §2 |
| **`WatchPanelWindow`** | 🛑 **BLOCKED** | `Q44-B` has not run — see §3 |
| `AiWatchWindow` | ⭐ **stays, by ruling** | `R-113` — a curated list kept across selections |

⭐ **IDs I allocated:** **`BP-404`** *(the layer)* · **`BP-405`** *(the blocked retirement)*.

---

## 1. ⭐⭐ THE ENUMERATION, AND A CAVEAT I MUST STATE

⚠⚠ **The codebase-memory DB was CORRUPT at the start of this run** —
`store.auto_clean … backing_up_corrupt_db_to_.corrupt_—_re-index_required`.
⭐ **I re-indexed rather than falling back to grep** *(181,225 nodes / 443,442 edges, clean)*, and every
inventory claim below is from the **re-indexed** graph plus a grep cross-check. ⛔ No claim here was made
while the server was down.

```
search_graph(".*(WatchPanelWindow|LiveBlackboardPanel|BlueprintVariablesWindow|
  BlueprintVariablesManagedWindow|AiWatchWindow).*")   → 31
```

⭐⭐ **Both directions of the `CLAUDE.md` caveat mattered again:** the graph gave the in-degrees, and
**grep gave the file-mates** *(§2)* that the type query could not.

---

## 2. ⚠⚠ THE FINDING — **the file is not the unit, the class is**

📐 `BlueprintVariablesWindow.cs` is **557 lines** and holds **three** types:

| type | measured usage | verdict |
|---|---|---|
| `BlueprintVariablesWindow` *(the window)* | the retirement target | ⛔ **deleted** |
| `BlueprintEditableAssetAdapter` | ⭐ **`BlueprintNewAssetService` at 5 sites** + the smoke tests | ✅ **STAYS** |
| `BlueprintVariableSchemaSource` | ⭐⭐ **`BlueprintMyBlueprintWindow:533` — PRODUCTION** | ✅ **STAYS** |

⇒ ⛔⛔ **Deleting the FILE — the obvious move, and the one the retire-list phrasing invites — would have
taken two live types with it**, one of them on a production path.

📌 `CLAUDE.md`: *"prefer ROUTING to DELETING."* ⭐ The routing here is the least clever possible one:
leave the two live types exactly where their callers already find them, and delete only the class the
design named.

---

## 3. 🛑 `BP-405` — **the retirement I REFUSED, on the design's own words**

📄 **`Q38`, verbatim:**

> ⭐⭐ **And it REORDERS `Q38-E`:** `Q44-B` *(send the breakpoint rows home)* now runs **BEFORE**
> `Q38-E` step 1 — ⛔ otherwise step 1 merges a **heterogeneous** surface.

📐 **Measured:** `AiBreakpointsWindow` has **no watch list**. ⇒ **`Q44-B` has not run.**

⇒ ⛔ Retiring `WatchPanelWindow` today deletes the **only** surface showing
`IBlueprintDebugSession.GetWatches()`. ⚠ That is a **capability loss**, not a cleanup — and §6 `L5`'s
own clause is *"per item, **after its replacement is live**."*

⭐⭐ **The refusal is RAILED** — `WatchPanelWindow_IsNotRetiredYet_BecauseQ44BHasNotRun` — so the
retirement cannot be done by accident before the move. ⛔ A comment would have decayed.

---

## 4. ⭐⭐ FIVE RAILS RE-EXPRESSED, **none deleted**

⚠ A retirement's rails are the dangerous part: they assert the old world, and the lazy fix is to delete
them. 📌 That is `BP-402` ②'s lesson pointed forward.

| rail | ⭐ what I did |
|---|---|
| `RegisterWindows_Registers_All_Expected_Windows` | dropped `"Inspector"` **and added `Assert.DoesNotContain("Inspector", …)`** — ⛔ dropping a row from an expected-list rail proves nothing on its own; the rail would pass if the registrar registered it anyway |
| `…RegistersAllWindows_ViaEngineInterface` | same removal, pointing at the sibling for the reason |
| `BothVariablesWindows_SurviveRegistration_OnBlueprint` | renamed to **`TheSurvivingVariablesTable_KeepsItsOwnId_AfterTheLegacyRetirement`** — ⭐ the retired id must now resolve to **nothing**, which is *stronger* than what it replaced |
| `EveryKnownWindowId_ResolvesToADistinctWindow` | the retired id's row removed, with a pointer to where its absence IS asserted |
| `EditorSubsystem_…RegistersVariablesWindow_ForBlueprint` | ⭐⭐ **re-pointed, not removed** — ⛔ deleting it would have dropped the only check that Blueprint has a variables surface **at all** |

---

## 5. ⭐ GATES — **run ONCE, at the end** *(`M-37`)*

⭐ Baseline = **`L0.4`+`L4`'s table**, ⚠ **rebased**: the tree now contains the coordinator's merge of
both lanes, so two suites move for reasons that are not mine. Base sha **`71b30c3d`**.

| gate | env | result | Δ |
|---|---|---|---|
| **solution build** | — | ⭐ **0 errors, 0 warnings** | — |
| `Hrot.Editor.AiShared.Tests` | **Xvfb** | **1836 / 0 / 0** | ⭐ **+7 — mine** |
| `Hrot.Blueprints.Tests` | **Xvfb** | **3886 / 0 / 10** | ⭐ **−1** — the retired stub's own rail, deleted with it |
| `Hrot.BTree.Editor.Tests` | **Xvfb** | **622 / 0 / 0** | **0** |
| `Hrot.Hsm.Editor.Tests` | **Xvfb** | **555 / 0 / 0** | **0** |
| `Hrot.Editor.Tests` | **Xvfb** | **209 / 0 / 0** | **+3** — ⚠ **the coordinator's merge**, not mine |
| `Hrot.Diagnostics.Breakpoints.Tests` | **Xvfb** | **151 / 0 / 0** | **0** |
| `Hrot.Smoke.Tests` | **Xvfb** | **4 / 0 / 0** | **0** |
| `Hrot.ClusterRunner.Tests` | **Xvfb** | ⚠ **262 / 2 / 0** | **+2 passed** *(merge)*; the `D003_*` pair unchanged |
| ⚠⚠ `Fdp.ModuleHost.Tests` | **Xvfb** | 🔴 **192 / 6 / 0** | **NEW ROW — see §6** |
| **tracker** | — | ⭐ **OK — open 85 / done 255 (+1 refuted)** | +1 done, +1 open |
| **rulings** | — | ⭐ **22/22 verified**, no staleness warnings | — |
| **design digest** | — | ⭐ **OK** | — |
| **working tree** | — | ⭐ **CLEAN after every suite run** | — |

### ⭐⭐ Golden movement, as a diff shape

⭐⭐⭐ **ZERO goldens moved.** 📐 **12 files: 8 modified, 3 DELETED, 1 added.** ⛔ No `.approved.` /
golden / snapshot file in the diff. ⚠ **The three deletions are the point of the batch** and are named
in §0's table.

---

## 6. ⚠⚠ `Fdp.ModuleHost.Tests` — **6 RED, CROSS-LANE, NOT MINE, NOT FIXED**

⭐ **First appearance in my gate table** — the suite arrived in my tree with the coordinator's merge of
the time lane.

```
ProviderAssignmentTests.ProviderAssignment_AsyncSoD_MultipleModules_Convoy
ConvoyAutoGroupingTests.AutoGrouping_SameTierAndFreq_SharesProvider
HonestSodGdbTests.UnionMask_Expansion_NewSodModule_ExpandsSharedProvider
HonestSodGdbTests.BatchInstall_SodModules_ActivatedAtomically
ConvoyIntegrationTests.ConvoyIntegration_5Modules_ShareSnapshot
ConvoyIntegrationTests.ConvoyIntegration_MemoryUsage_Reduced
```

| ⭐ | |
|---|---|
| **not mine** | 📐 my diff touches **zero** files under `Fdp.ModuleHost`, `Fdp.Toolkits/Time/` or `Hrot.Orchestrator` — and the subjects *(Convoy · SoD · provider assignment)* have no relationship to the Details panel |
| ⛔ **NOT FIXED, deliberately** | 📌 `R-128`: `ModuleHostKernel` is the **TIME lane's**, and *"a cross-lane edit is a STOP-and-report, not a judgement call"* |
| ⭐ **reported, with names** | so the time lane can triage rather than rediscover — ⚠ and I am **not** filing a `BP-` row for it: 📌 `R-128`'s id rule is `BP-` = UI lane, `TM-` = time lane |

⚠ **I did not establish whether these are pre-existing on the coordinator branch** — that would need a
worktree build of `71b30c3d`, which is the other lane's call to make. ⭐ **What I can say** is that they
are unrelated to this diff by subject and by file.

---

## 7. ⭐ LANE CHECK

⭐ Files touched: `Hrot.Blueprints.Editor` + tests · `Hrot.BTree.Editor` · `Hrot.Editor`
*(composition root)* · `AiShared.Tests`. ⛔ **Nothing under `Fdp.Toolkits/Time/`, `Hrot.Orchestrator`,
`ModuleHostKernel` or the integration tests** *(`R-128`)*. ⭐ ids are **`BP-`**.
⛔ **Untouched, as instructed:** `MIN`'s `WriteFieldNow` — **`W3` is NOT in this batch.**

---

## 8. ⭐ WHAT IS OPEN

| | |
|---|---|
| 🛑 **`BP-405`** | `WatchPanelWindow` — unblocks when **`Q44-B`** lands |
| ⛔ **`BP-399`** | `L3`'s four remaining rows |
| ⛔ **`BP-403`** | `L4.4`'s View-menu half |
| ⭐ **`L6`** | `L6.1` extracts `PerspectiveWorkspace` and carries the registry, context builder and entity source across — the last of §2's 13 classes |
