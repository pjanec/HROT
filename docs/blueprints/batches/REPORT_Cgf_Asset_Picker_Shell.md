<!--STATUS
state: LIVE
updated: 2026-08-26
current-answer: this file is a REPORT — ephemeral. ⭐ The durable record is
  docs/DESIGN_Cgf_Asset_Picker_Shell_Slice.md (§8 = the as-built delta, folded per obligation ⑤) and the
  tracker rows CE-049 / CE-050. ⛔ Do not quote this file as design.
-->
# REPORT — **CGF asset-picker / new-asset shell** *(Axis-C E2)*

📄 **Design (durable, UML + as-built):** [`docs/DESIGN_Cgf_Asset_Picker_Shell_Slice.md`](../../DESIGN_Cgf_Asset_Picker_Shell_Slice.md)
📄 **Handoff:** [`HANDOFF_Cgf_Asset_Picker_Shell.md`](HANDOFF_Cgf_Asset_Picker_Shell.md) · **Dispatched at `bdd71821e`**
⭐ **IDs allocated (rule 5): `CE-049` *(the slice)* · `CE-050` *(one finding, filed open)*.**
⭐ **Started-marker: `d5efcf7ca`** *(pushed before any code — rule 1b)*. **Base for every Δ below.**

## 0. ⭐ PROCESS

| # | |
|---|---|
| **P1** | ⛔ **Still harness-bound to `claude/reset-working-branch-qd1qpv`** — cannot branch fresh. ⭐ Rule 7 satisfied by `git merge --ff-only origin/claude/blueprint-authoring-status-6sr5ld`: 📐 my prior HEAD was an **ancestor** of the coordinator head, so it was a clean 15-commit fast-forward *(which brought in the backend lane's `QA-001..013` merge)*. |
| **P2** | ✅ **My Slice A finding is FIXED by the coordinator** — `dabd35715` *"correct stale lane table — 6sr5ld is the live coordinator lane"*. ⭐ Noting it so the finding closes rather than lingering. |

## 1. ⭐⭐⭐ OBLIGATION ③ — the design's UML, checked BEFORE building

⭐ The design carried a `classDiagram` (9 boxes) + a `sequenceDiagram` (6 participants). **What I built matches
the structure** — three lifted seam classes, one new shared create-core, both hosts composing it — and adds or
deviates in **seven** places, all argued in the design's own **§8** *(obligation ⑤: §2's risk paragraph is
marked RESOLVED, and §4's diagram was updated)*.

⭐⭐ **The design's §2 inventory verdicts were all CORRECT when re-measured**, which is worth saying because the
last two batches each overturned an inventory row. All three launchers really are pure delegate glue; the
create-core really was duplicated; the service layer really was already there.

## 2. ⭐⭐⭐ THE MEASURED RISK — resolved structurally, not hopefully

The design flagged one risk: *"confirm CGF can compose an equivalent `openPicker` / save-as modal … if a host
genuinely cannot host a modal, the items stay greyed-with-cause — that is the correct end state."*

📐 **It can, and the argument is structural rather than empirical:** `BuildAiShell` is reached **only** from
`RegisterWindows`, which `return`s early when `_headless`. ⇒ **if the shell is built at all, the node has a
`WindowManager` and an ImGui context.** CGF also already constructed an `AiEditorAdapterBundle`, so the icon
provider and theme the picker needs were already there.

⚠⚠ **One consequence worth naming, because it changes where the greyed-with-cause contract lives.** On a
genuinely headless CGF the shell is never built ⇒ `ScenarioMenuCommands` is never registered either. ⇒ **the
greyed-with-cause end state is a UNIT-RAIL property, not something a headless run exhibits.** That is why the
rail for it moved to a `NoModalShape` rather than being deleted with the pre-E2 CGF wiring — see §4.

## 3. ⭐⭐⭐ THE DEDUP — and what measuring the two copies actually showed

⛔ **The two create-cores had already DRIFTED, in three places.** This is the argument *for* the shared type,
not against it, and I would have missed it by assuming "near-verbatim" meant "identical":

| | `EditorSubsystem.CreateAssetCore` | `CgfSubsystem.AssetShellCreate` |
|---|---|---|
| non-document kinds | ⭐ early-returns `(id, "[OK]")` | ⛔ **no branch** — always runs the document path |
| Blueprint mint-write | ⭐ wrapped in `try/catch` with a log | ⛔ unguarded |
| *"not in the catalog"* text | ⛔ *"check the asset roots (ruling 67)"* | ⭐⭐ **names the remedy**: *"ruling 67: pass `--asset-root` on a deployed node"* |
| the string layer | a **separate** `AttachAssetAuthoring` lambda | ⛔ **fused** into the create body |

⇒ ⭐⭐ **The survivor is a MERGE, not either original**: the editor's branch **and** CGF's better message, with
the string layer split back out as `CreateByName` so the `MA-021` recipe-by-name resolve is shared too. ⭐
**Neither host regressed; CGF's error text got strictly better.**

⭐⭐ **And the guard against a third copy is a SOURCE SCAN, which is structurally necessary here.** The duplicate
was a **local function** and an **inline lambda** ⇒ ⛔ neither reflection nor the call graph can see it, and a
reference count cannot either *(a re-derived copy calls the same primitives and references nothing new — which
is exactly how the first duplicate passed review)*. The rail fails any composition root that both calls
`.CreateNew(` **and** resolves `FindByAssetId`.

## 4. ⭐⭐ THE RAIL THAT INVERTED — and why that is correct

Slice A's `OnlyTheEnablementDiffersBetweenTheHosts` asserted CGF's three seam-backed items were
**DISABLED-with-cause**. 📌 Slice A's own note said they *"light up for free the day a picker is composed here
(Axis-C E2)"*. ⇒ ⭐ **that day arrived, so the assertion flipped** to
`AfterE2TheTwoHostsAgreeOnEnablementToo`.

⛔ **The greyed behaviour was NOT deleted** — it moved to a new **`NoModalShape`**, which is the honest owner:
it is a property of *"no modal composed"*, ⛔ **never of "being CGF"**. ⭐ Keeping it as a first-class shape
means the ruling-49 contract still has a rail for the next host that genuinely cannot host a modal.

⭐ Plus a new rail the E2 wiring specifically earns: **`OnCgfTheEnabledLoadItemsReachTheSession`** — ⚠ *enabled*
is not *functional*, and the post-E2 failure worth pinning is a live, clickable item that silently does
nothing.

## 5. ⚠⚠ A RAIL DEFECT I INTRODUCED AND FIXED — reported because it is the repo's own flake disease

📐 `TheJsonContributorRefreshesBeforeTheCatalogIsAsked` first asserted an **exact step list**:

| cut | result | why |
|---|---|---|
| **1** | ⛔ **red alone** | I expected an unconditional `refresh:assembly`; the controller guards it with `if (aiAsm != null)` and the test host had not loaded `Hrot.AI.Behaviors`. ⭐ Both originals had the same guard — I would have pinned something production never did |
| **2** | ⛔⛔ **green alone, RED in the full suite** | a test running earlier HAD loaded that assembly, so the guarded step fired |
| **3** | ✅ **stable** | filter the conditional step, pin only the invariant order; assert the assembly refresh **separately and conditionally** |

⇒ ⭐⭐⭐ **An exact-list assertion there is order-dependent on the whole suite — a rail that lies.** ⚠ Named
because it is the same shape as `DEBT-AIB-030`, and accidentally writing one is evidence of how easy that is.

## 6. ⭐ GATES *(rule 8 — the report substitutes for a coordinator re-run)*

**Base for every "pre-existing" claim: the started-marker `d5efcf7ca`.** Build once, then `--no-build`.

| gate | command | `--no-build` | result | Δ vs `d5efcf7ca` |
|---|---|---|---|---|
| build `Hrot.Editor.AiShared` | `dotnet build … --no-restore` | n/a | ✅ **0 errors, 0 warnings** | — |
| build `Hrot.Editor` | `dotnet build … --no-restore` | n/a | ✅ 0 errors | — |
| build `Hrot.CGF` | `dotnet build … --no-restore` | n/a | ✅ 0 errors | — |
| build `Hrot.Editor.Tests` · `AiShared.Tests` · `Blueprints.Tests` | `dotnet build … --no-restore` | n/a | ✅ 0 errors each | — |
| **T1** `Hrot.Editor.Tests` | `dotnet test … --no-build` | ✅ | ⚠ **277 passed / 1 failed / 1 skipped** — the 1 red is `CE-050`, see below | **0 attributable** |
| **T1 stability** — same suite ×3 | `dotnet test … --no-build` ×3 | ✅ | 1 · 1 · **0** reds — the identity did not rotate; always the same test | — |
| **T2** `Hrot.Editor.AiShared.Tests` | `dotnet test … --no-build` | ✅ | ✅ **2027 / 0 / 1 skip** | **0** |
| **T2** `Hrot.Blueprints.Tests` | `dotnet test … --no-build` | ✅ | ✅ **3965 / 0 / 18 skip** | **0** |
| **R3 GUARD** `TheToolbarLayoutIsOneListTests` | `--filter …` | ✅ | ✅ **7 / 0** *(inside the Blueprints run)* | **0** |
| **NEW rails** | `--filter TheCreateCoreIsOneImplementationTests\|TheScenarioMenuIsSharedByBothHostsTests` | ✅ | ✅ **18 / 0** *(11 new + 7 updated)* | — |
| **RED PROOF** *(inverse edit)* | catalog lookup moved **before** the JSON refresh | ✅ | ✅ **3 reds**, restored **byte-identical** *(`diff -q` clean on both files)* | — |
| `mermaid-check.mjs` | on the design | n/a | ✅ **2/2 blocks parse** | — |
| `design-digest.py --check` | as written | n/a | ✅ 91 docs: STATUS + INVENTORY + UML present | — |
| `rulings-check.py` | as written | n/a | ⚠ **25/25 quotes verify**, 2 staleness WARNs — see the caveat | — |
| `tracker-counts.py --check` | as written | n/a | ✅ *"open 102 / done 346 (+1 refuted)"* | **0 — see the caveat** |
| **T3** conformance / system suite | `bash scripts/run-system-tests.sh` | ⛔ built once by the script | ⏳ **BACKGROUNDED — result in §8** | — |

### ⚠ Three caveats, so no row is over-read

1. ⚠ **`rulings-check`'s 2 staleness WARNs are INHERITED, not mine.** 📐 The two cited sources are
   `.claude/CLAUDE.md` *(changed by `dabd35715`, the coordinator's lane-table fix)* and
   `DataBreakpointManager.cs` *(changed by `0c1121c69`, the backend lane's `QA-001..006`)* — both arrived in my
   tree through the rule-7 fast-forward. ⛔ My diff touches neither file. **Every quote still verifies.**
2. ⛔ **`tracker-counts.py` still does not count `CE-` rows** — its filter is `\*\*\[?BP-\d+`. ⭐ Same
   pre-existing caveat as the Slice A report; *"counts OK"* means the BP tally is consistent, ⛔ not that
   `CE-049`/`CE-050` were verified.
3. ⚠ **Working tree clean after every suite run** *(`git status --short` empty)*; **no golden moved** — this
   batch touches none. **No skip added**: the 1 + 1 + 18 skips are all pre-existing.

### ⚠⚠ The one red, and how I attributed it — `CE-050`

📐 `AiHotReloadCoordinatorTests.TwoReloadCycles_OldAlcIsCollected` — failed on **2 of 3** full-suite runs,
passed **4/4 in isolation** on the same tree.

⭐⭐ **This MATCHES the backend lane's just-merged root cause rather than contradicting it.**
`REPORT_Test_Suite_Reliability.md` records the signature verbatim: *"under pressure, whichever test allocates
at the wrong moment loses"* and *"a filtered run never reaches the pressure"*. ⚠ And the assertion is a
`WeakReference`-died-after-`GC.Collect` check on a collected `AssemblyLoadContext` — **exactly the assertion
that flips under memory pressure.** ⇒ either `Hrot.Editor.Tests` has its own leak the `QA-004` instrument
would find *(`FDP_TRACK_REPO_LEAKS=1`)*, or the rail should stop asserting GC timing.

⛔ **Not attributable to this batch:** 📐 my diff touches no ALC or hot-reload file, and the test is green in
isolation on the same tree. ⚠⚠ **Stated plainly: I did NOT build a base worktree to prove it pre-existing.**
The attribution rests on the isolation result, zero diff overlap, and the backend lane's own documented
signature — ⛔ not on a base-sha run. Filed as `CE-050` so it is the backend lane's to confirm, since
`DEBT-AIB-030` is marked closed and this is one red with that shape still standing.

## 7. ⭐ WHAT I DID NOT DO

| ⛔ | why |
|---|---|
| **No toolbar model change** | R3 / item ④'s constraint. ⭐ `OpenAsset`/`NewAsset` are *supplied* to the existing shared `HostServices`; the `Layout` table is untouched and its rail stays 7/0 |
| **No `SubsetShape` edit** | ⭐ it generalises for free *(the same finding as Slice A's F-B)*; what needed changing was the enablement claim, §4 |
| **Scenario create on CGF** | 📐 `ScenarioNewAssetService` needs a session adapter; CGF composes none, so `POST /assets {"kind":"Scenario"}` still explains rather than creating something unopenable. ⭐ The controller refuses it with the composition reason, and a rail pins that |
| **`DebugApiService.LoadScenarioLive` routing** | still `CE-048`, untouched — outside E2's five items |
| **`MigrationAlertManager.Draw()` wiring** | still `CE-047`, untouched |
| **Anything in the backend lane's files** | ⚠ their handoff flags `EntityDragGizmoTests`/`Hrot.Presentation` as a possible overlap. 📐 **This batch touches no `Hrot.Presentation` source at all** ⇒ no collision |

## 8. ⏳ T3 — the system/conformance suite

⭐ Backgrounded per the handoff §1. Result appended here when it lands; if it is not in this file, it is in the
next session's first message.

> **Expectation, stated in advance so the result can contradict it:** CGF's `main-toolbar` panel model gains
> **two entries** *(`shell.openAsset`, `shell.newAsset`)* and its `global-menu` gains their two File items. ⇒
> the `SUBSET-BY-DESIGN` verdict should report a **larger** shared set and still hold, because both were
> already on the editor. ⚠ **If it reddens, the likely cause is the `main-toolbar` verdict comparing
> `sortOrder`** — the two new CGF entries take the editor's `-11`/`-10`, so they should match, but that is the
> one field a subset check does compare and I have not run it.

## 9. ⭐ DECISION LOG *(decide-and-log autonomy)*

| # | decision | basis |
|---|---|---|
| 1 | Lift the three launchers unchanged; fix only a doc `cref` that pointed at the editor-only `IEditorLogic` | measured: delegate fields only |
| 2 | `AssetCreateController` gets **two** surfaces, `Create` + `CreateByName` | the two copies split the string layer differently; one body, two layers |
| 3 | Keep the editor's non-document branch **and** CGF's better error text | §3 — the survivor must not regress either host |
| 4 | **Add `AssetSaveAsRequests`** although it is not in the item list | ⛔ the alternative was a third copy of a save-path helper inside the dedup batch |
| 5 | Reorder `WireAssetCreation`/`WirePickerShell` **before** `WireSaveAndReload` | the menu's seams ARE the launchers; verified the two were independent first |
| 6 | CGF gets its **own** `PickerRegistry`, not the canvas bundle's | the editor's `BATCH-29` note: double-`DrawFrame` |
| 7 | Keep the seams **null-conditional** on CGF rather than asserting non-null | ⭐ if the shell somehow did not compose, greyed-with-cause stays the honest fallback |
| 8 | Pass `describe`/`recipeCategory` to CGF's `NewAssetLauncher` | `MA-020`'s silent-default rule: this caller HAS them |
| 9 | Guard the dedup with a **source scan**, not reflection | the duplicate was a local function + a lambda — invisible to both reflection and the call graph |
| 10 | Move the greyed-with-cause rail to `NoModalShape` rather than delete it | it is a property of *no modal*, not of *being CGF* |
| 11 | File the rotating red as `CE-050` rather than filter or fix it | `R-131` — no filter-around; and it is the backend lane's neighbourhood, with their root cause matching |
