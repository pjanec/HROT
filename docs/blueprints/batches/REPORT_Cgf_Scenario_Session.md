<!--STATUS
state: LIVE
updated: 2026-08-26
current-answer: this file is a REPORT — ephemeral. ⭐ The durable record is
  docs/DESIGN_Cgf_Scenario_Session_Slice.md (§9 = the as-built delta, folded per obligation ⑤) and the
  tracker rows CE-046..CE-048. ⛔ Do not quote this file as design.
-->
# REPORT — **CGF scenario session: shared facade + distinct File menu** *(AQ60 Slice A = Axis-C E1)*

📄 **Design (durable, UML + as-built):** [`docs/DESIGN_Cgf_Scenario_Session_Slice.md`](../../DESIGN_Cgf_Scenario_Session_Slice.md)
📄 **Handoff:** [`HANDOFF_Cgf_Scenario_Session.md`](HANDOFF_Cgf_Scenario_Session.md) · **Dispatched at `dbdc5e783`**
⭐ **IDs allocated (rule 5): `CE-046` *(the slice)* · `CE-047` · `CE-048` *(two findings, filed open)*.**

## 0. ⚠⚠ TWO PROCESS DEVIATIONS, DECLARED FIRST

| # | |
|---|---|
| **P1** | ⛔ **The handoff asks for a branch FRESH from the coordinator; this session is harness-bound to `claude/reset-working-branch-qd1qpv`** and cannot create a new branch. ⭐ **Rule 7 was satisfied instead by `git merge --ff-only origin/claude/blueprint-authoring-status-6sr5ld`** — 📐 measured: my prior HEAD `0f15ba6b1` **is an ancestor of** the coordinator head, so the merge was a clean fast-forward with zero conflicts, and my tree IS the dispatch tree plus this batch. ⭐ Rule 1b started-marker pushed as `6ece1631f` **before any code**. |
| **P2** | ⚠ **The coordinator lane has MOVED and `.claude/CLAUDE.md` is stale about it.** The lane table names `claude/blueprint-authoring-status-gm0akp` and calls `…-6sr5ld` *"a different, now-retired session"* — 📐 but `6sr5ld` is the branch carrying this handoff, was updated `2026-08-26`, and **contains `gm0akp` as an ancestor**. ⇒ I re-synced from `6sr5ld`. ⭐ **The lane table needs one line changed**; I did not edit it *(coordinator-owned file, and CLAUDE.md is not this batch's scope)*. |

## 1. ⭐⭐⭐ OBLIGATION ③ — the design's UML, checked BEFORE building

⭐ **The design carried 1 `classDiagram` (13 boxes) + 1 `sequenceDiagram` (4 participants).** What I built matches
the structure — one shared interface, one implementation instantiated by both hosts, the registrar taking the
interface — and **deviates in seven places, every one argued in the design's own §9** *(obligation ⑤: the design
was updated, and §4/§5's diagrams were rewritten to the as-built so they are TRUE again rather than asserted)*.

| # | the deviation, in one line |
|---|---|
| **D1** | ⭐⭐ **`NewExercise()` became TWO verbs** — `ClearWorld()` *(local wipe)* + `NewExercise()` *(cluster reset)*. 📐 The deferred-load state machine calls the local wipe as **step 1 of its own sequence**, so one verb would publish a second `Idle` intent from inside the handler for the first |
| **D2** | ⭐ **`TakeCheckpoint` moved from the MENU to the SESSION.** §4 drew `ScenarioMenuCommands ..> TakeCheckpointIntent`; the menu has no bus, so that arrow means the same publish in **both** composition roots — ruling 9 |
| **D3** | ⭐⭐ **`ScenarioMenuCommands` MOVED to `Hrot.Editor.AiShared.Scenarios`** *(with `MigrationAlertManager`, now `public`)*. Taking the interface was necessary and **not sufficient** — CGF cannot reference `Hrot.Editor`, which is the wall |
| **D4** | 🔴 **NO `File/Save` item was registered** — see §2, this is the seam-law find of the batch |
| **D5** | ⭐ **Nine items, not five** — the pre-existing scenario group was **rehomed** into the new structure rather than left beside it |
| **D6** | ⭐ **`scenariosRoot` is a `Func<string>`** — the editor's is a computed property over `ClusterConfiguration.Default`, so a snapshot would change *when* it is read |
| **D7** | ⭐⭐ **the `SUBSET-BY-DESIGN` verdict needed NO extension** — see §2 |

## 2. ⭐⭐⭐ THE TWO SEAM-LAW FINDS *(the prior-art pass paid twice)*

### F-A — **`File/Save` already existed, scenario branch and all** ⇒ item ④'s hardest row was a WIRING gap

📐 **Measured `2026-08-26`.** `CgfEditorShellToolbar.Layout` — the ONE shared toolbar+menu table ruling 58
mandates — already carries `new(SaveId, -9, Group: 1, MenuPath: "File/Save")`, bound to
`ShellSaveCommands.SaveId` (`"shell.save"`). And `ShellSaveCommands.Register` **already** has the entire
scenario branch: `isScenarioContext` · `hasLoadedScenario` · `saveScenarioAction` · `requestScenarioSaveAs`,
with `shell.save`'s handler checking scenario context **first**.

⛔ **CGF passed none of them**, behind the comment *"No scenario save on this host: CGF has no `IEditorLogic`
scenario session."* ⇒ ⭐⭐ **the design's `File/Save` row is discharged by supplying three seams**, not by
registering an item. Registering a second `File/Save` would have been two controls for one action **and** would
have touched the toolbar table's own menu row — **which R3 forbids**. ⇒ ⭐ **R3 is respected by construction,
not by care.**

### F-B — **the conformance verdict was already general** ⇒ item ⑤ needed a DIFFERENT rail, not an extension

📐 `ClusterConformanceRails`' `global-menu` `SubsetShape` is `("items", "path", ["visible"], "menu item")` —
keyed by path, compared by visibility. ⇒ **the new items are covered with zero edits.**

⛔ **But a SUBSET check cannot fail where the sets are supposed to be EQUAL** — a CGF registering *none* of these
items is still *"a subset"*, and the anti-vacuity guard only fires on a **completely** empty list. ⇒ ⭐ built
**`TheScenarioMenuIsSharedByBothHostsTests`**: it runs the production registrar twice — once with the
editor-shaped seam set, once with the CGF-shaped one — and asserts the **item sets are EQUAL** and that the only
difference is *enablement*. ⭐ Headless, 6 rails, milliseconds; ⛔ no booted two-host cluster required.

## 3. ⭐⭐ THE LABEL CHANGE — **visible, deliberate, argued** *(design §6's explicit caveat)*

⚠ **The editor's `File` menu LOOKS different after this batch, and that is the deliverable.**

| before | after | why |
|---|---|---|
| `File/Scenario/New Scenario` | `File/Edit/New Scenario` | grouped by MODE |
| `File/Scenario/Load Scenario…` | ⭐⭐ **`File/Edit/Open Scenario`** | 🔴 **THIS was the chameleon R2 names** — labelled generically, meant *load for AUTHORING* |
| — | ⭐⭐ **`File/Live/Load Scenario`** | the live mode had **no menu affordance at all** on any host |
| — | ⭐⭐ **`File/Live/New Exercise`** | the cluster-wide reset had none either |
| — | ⭐ **`File/Checkpoint/Take Checkpoint`** | `TakeCheckpointIntent` existed and only the orchestrator panel could reach it |
| `File/Scenario/{Save, Save As, Migration History, Save Curated…}` | `File/Edit/{same}` | one home, not two |

⭐⭐⭐ **Every command ID is unchanged** *(`scenario.new`, `scenario.load`, `scenario.save`, `scenario.saveAs`,
`scenario.migrationHistory`, `scenario.updateCurated`)*, so hotkeys, MCP identity and every id-keyed rail still
resolve. ⚠ The three NEW ids are `scenario.newExercise` · `scenario.loadLive` · `scenario.takeCheckpoint`.

⛔ **The alternative — keep `File/Scenario/*` and ADD `File/Live|Edit/*`** — was rejected: it keeps the chameleon
**and** adds a duplicate surface for the same action, which is the worse of the two visible changes.

## 4. ⭐ RULING 49 / VC-3 ON CGF — **registered, disabled, and SAYING WHY**

CGF composes no `AssetPickerLauncher` and no modal browser. ⇒ `openPicker`/`openSaveAsDialog` are passed
**`null`**, and the three seam-backed items render **greyed with the cause in the label**
*(`"Open Scenario (unavailable — no scenario picker on this host)"`)*. ⛔ Not hidden *(ruling 49)*, ⛔ not
live-looking no-ops *(VC-3)*. ⭐ They light up with **zero menu code** the day E2 composes a picker.

⭐ **`New Exercise` on CGF LOGS AND PROCEEDS** — ruling 53 + `UX_Feature_Modal_Surfaces.md` §2.0b: a modal on an
unattended node is a hang, and *"the origin-side log IS THE WHOLE SAFETY NET"*. ⛔ Passing `null` would proceed
**silently**, which is the one option the ruling forbids — so the seam is passed explicitly.

## 5. ⭐ GATES *(rule 8 — the report substitutes for a coordinator re-run)*

**Base for every "pre-existing" claim: the started-marker `6ece1631f`.** Build once, then `--no-build`.

| gate | command | `--no-build` | result | Δ vs `6ece1631f` |
|---|---|---|---|---|
| build `Hrot.Editor.AiShared` | `dotnet build …/Hrot.Editor.AiShared.csproj --no-restore` | n/a | ✅ **0 errors, 0 warnings** | — |
| build `Hrot.Editor` | `dotnet build …/Hrot.Editor.csproj --no-restore` | n/a | ✅ 0/0 | — |
| build `Hrot.CGF` | `dotnet build …/Hrot.CGF.csproj --no-restore` | n/a | ✅ 0/0 | — |
| build `Hrot.SystemTests` · `ClusterRunner` · `ClusterRunner.Integration.Tests` | `dotnet build … --no-restore` | n/a | ✅ 0 errors each | — |
| **T1** `Hrot.Editor.Tests` | `dotnet test … --no-build` | ✅ | ✅ **264 passed / 0 failed / 1 skipped** | **0** |
| **T2** `Hrot.Editor.AiShared.Tests` | `dotnet test … --no-build` | ✅ | ✅ **2027 / 0 / 1 skip** | **0** |
| **T2** `Hrot.Blueprints.Tests` | `dotnet test … --no-build` | ✅ | ✅ **3965 / 0 / 18 skip** | **0** |
| **R3 GUARD** `TheToolbarLayoutIsOneListTests` | `dotnet test …Blueprints.Tests --no-build --filter …` | ✅ | ✅ **7 / 0** | **0** |
| **RED PROOF** *(inverse edit)* | live→edit routing **and** confirm bypass | ✅ | ✅ **5 reds**, restored **byte-identical** *(`diff -q` clean)* | — |
| `mermaid-check.mjs` | `MERMAID_PREFIX=/tmp/mm node scripts/mermaid-check.mjs docs/DESIGN_Cgf_Scenario_Session_Slice.md` | n/a | ✅ **2/2 blocks parse** | — |
| `design-digest.py --check` | as written | n/a | ✅ 91 docs: STATUS + INVENTORY + UML all present | — |
| `rulings-check.py` | as written | n/a | ✅ **25/25 verified** | — |
| `tracker-counts.py --check` | as written | n/a | ✅ *"open 102 / done 346 (+1 refuted)"* | **0 — see the caveat** |
| **T3** conformance / system suite | `bash scripts/run-system-tests.sh` | ⛔ built once by the script | ✅ **107 passed / 0 failed / 0 skipped** *(11 m 10 s)* | **0** |

### ⚠ Two caveats on the rows above, stated rather than glossed

1. ⛔⛔ **`tracker-counts.py` DOES NOT COUNT `CE-` ROWS.** 📐 Measured: its row filter is
   `re.search(r"\*\*\[?BP-\d+", line)` ⇒ `CE-`/`AX-`/`TM-`/`HN-`/`ST-` ids have **never** been in the totals.
   ⭐ So *"counts OK"* means *the BP tally is consistent*, ⛔ **not** *my three rows were verified*. This is
   pre-existing and not something this batch changed; ⚠ naming it so the green is not over-read.
2. ⚠ **Working tree clean after every suite run** *(`git status --short` empty; no golden moved, and this batch
   touches no golden)*. **Quarantine/skip counts unchanged** — the 1 + 1 + 18 skips are all pre-existing;
   ⛔ **this batch adds no skip**.

## 6. ⭐ WHAT I DID NOT DO

| ⛔ | why |
|---|---|
| **No toolbar change** | R3. 📐 And gated: `TheToolbarLayoutIsOneListTests` pins the `(Id, SortOrder)` list and stays 7/0 |
| **No `Restore Checkpoint` item** | design §8 — the save exists cluster-wide, the restore **does not**. A registered item would be a control that cannot work; a rail asserts its absence |
| **No `Open Asset` / `New Asset from Recipe`** | Axis-C **E2**. ⭐ E1 built the `Edit/` submenu they slot into with zero menu code |
| **`DebugApiService.LoadScenarioLive` not routed through the session** | filed as **`CE-048`** — outside the five items and it touches the MCP surface. ⚠ Its **edit** arm is already unified for free |
| **`MigrationAlertManager.Draw()` not wired** | filed as **`CE-047`**. ⛔ Not deleted *(unreferenced ≠ unintentional)*; **where** a global banner is drawn is a UX call I will not guess |
| **`AX-023`/`AX-024`** *(the flake + the process-wide allocation counters)* | ⚠ **now the BACKEND lane's `HANDOFF_Test_Suite_Reliability.md` W2/W3.** 🔴 **And its W2 explicitly rules out the `[Collection]` approach** — *"Fix by ISOLATION, not by ordering… ⛔ NOT `[Collection]` ordering hacks that just hide it"* — which is what I had been asked to try. ⇒ **left entirely to that lane**; see §8 |

## 7. ✅ T3 — the system/conformance suite: **107 / 0 / 0**

⭐ **Backgrounded per the handoff §1 and CLAUDE.md's T3 rule** *(never a foreground blocker)*.
📐 `bash scripts/run-system-tests.sh` — `Category=SystemSmoke|Category=SystemModes` — **107 passed, 0 failed,
0 skipped, 11 m 10 s. Zero delta vs the started-marker.**

⭐⭐ **The prediction held, and the trap it names did NOT fire.** I wrote in advance: *"the `global-menu` panel
model now carries 9 more items on BOTH hosts, so `SUBSET-BY-DESIGN` should report a LARGER shared set and still
hold — ⚠ but if the `global-menu` golden is a full-array capture rather than a subset check, it will move
(CE-040 records exactly that trap)."* ⇒ ⭐ **`CE-045`'s generalisation is what absorbed it**: the verdict is a
by-key subset, so adding 9 shared items on both hosts moved no golden and reddened nothing. ⛔ Had the menu still
been compared by full array — as the toolbar was before `CE-040` — this batch would have reddened three rails.

⚠ **What this run does NOT prove:** that CGF's File menu renders the new items to a human. The suite asserts the
published panel MODEL, not pixels; the greyed-with-cause labels in particular are a `DynamicDisplayName`
assertion, not a screenshot. ⭐ Stated so the green is not over-read.

## 8. ⚠⚠ ONE CROSS-LANE COLLISION TO FLAG

⭐ `HANDOFF_Test_Suite_Reliability.md` §3 lists **`EntityDragGizmoTests` ×3 (`Hrot.Presentation.Tests`)** for the
BACKEND lane and warns *"coordinate with UI/CGF lane if the fix touches gizmo production"*. 📐 **This batch
touches no gizmo code and no `Hrot.Presentation` source** *(only a `.csproj` ProjectReference was added, to
`Hrot.Editor.AiShared`)* ⇒ **no collision from my side.** ⭐ Flagged so the other lane can proceed without asking.

## 9. ⭐ DECISION LOG *(decide-and-log autonomy)*

| # | decision | basis |
|---|---|---|
| 1 | `NewScenario` → `ClearWorld`, **not** `NewExercise` | 📐 measured: the load state machine's own step 1 |
| 2 | `TakeCheckpoint` on the session, not the menu | ruling 9 — one implementation |
| 3 | Move `ScenarioMenuCommands` to `AiShared` | the assembly wall; there is no other way for CGF to call it |
| 4 | **No second `File/Save`**; supply the seams instead | F-A + ruling 58 + R3 |
| 5 | Rehome the old items rather than add beside them | R2 — keeping them keeps the chameleon |
| 6 | `openPicker`/`openSaveAsDialog` became **nullable** | ruling 49 + VC-3 — CGF genuinely has neither |
| 7 | CGF's `confirmNewExercise` = **log-and-proceed**, explicitly | ruling 53; `null` would be the silent variant the ruling forbids |
| 8 | New `ConfirmPromptController` mirrors `AppExitPromptController` | the editor already splits *"what the buttons mean"* from *"how it's drawn"*; a second shape would be the duplicate |
| 9 | `zoneService: null` on CGF is an ABSENCE, not a silent default | CLAUDE.md's rule is *"a caller that HAS a dependency must pass it"* — CGF composes no `ZoneManagerService` at all |
| 10 | CGF's scenarios root = `OrchestrationConstants.GetNodeScenariosRoot(nodeId)` | 📐 it IS the directory CGF's own `HrotScenarioLoader` reads *(`isolatedTempRoot` == `GetNodeStagingRoot(nodeId)`)*; the editor's NAS path is unreachable from this assembly **and** would be wrong on a single box |
| 11 | Equality rail instead of extending `SubsetShape` | F-B — a subset check cannot fail where the sets should be equal |
