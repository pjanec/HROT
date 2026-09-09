<!--STATUS
state: LIVE
build-state: DISPATCH — UI/CGF lane. AQ60 Slice A = Axis-C increment E1: extract the shared scenario-session
  facade, instantiate in both, DISTINCT File-menu items on both hosts (no chameleons). Toolbar unchanged (R3).
updated: 2026-08-26
current-answer: pointer + autonomy. DESIGN (with UML): DESIGN_Cgf_Scenario_Session_Slice.md (READY-TO-BUILD).
  Decision trail + the user rulings: Architect_Question_60 (§3b, §4, §4b). Roadmap: gap map §2c Axis C.
known-conflict: moves EditorApplication's scenario half into Hrot.Editor.AiShared; edits CgfSubsystem.cs +
  EditorSubsystem.cs/EditorApplication.cs + ScenarioMenuCommands.cs (UI/CGF hot files) ⇒ rule-4 re-pull.
-->
# HANDOFF — **CGF scenario session: shared facade + distinct File menu** *(AQ60 Slice A = Axis-C E1 — UI/CGF lane)*

> 📌 **Dispatched at `<coordinator HEAD>`** *(confirm `git rev-parse origin/claude/blueprint-authoring-status-6sr5ld`)*.
> ⭐ Branch FRESH from the coordinator *(rule 7)*; **rule 1b started-marker before any code.** ⛔ No PR.
> ⭐ Continue **`CE-`** ids; **you allocate the numbers** *(rule 3; last used `CE-045`)*; state every id *(rule 5)*.

## 0. ⭐⭐⭐ BUILD FROM THE DESIGN — **do NOT design here**
📄 **[`DESIGN_Cgf_Scenario_Session_Slice.md`](../../DESIGN_Cgf_Scenario_Session_Slice.md)** — READY-TO-BUILD.
⭐⭐ Class + sequence UML in **§4/§5**; the distinct menu items in **§3a**. Check before building *(obligation ③)*, report match/deviation. Rulings: `Architect_Question_60` §3b/§4/§4b *(R1 whole-editor→shared · **R2 distinct menu items, NO chameleons** · R3 toolbar-selection is its own design)*. This is **Axis-C increment E1** *(gap map §2c)*.

## 1. ⛔ AUTONOMY + BUILD RULES
§0-style autonomy *(decide-and-log; stop the ITEM not the batch — R-106; DONE = design §6 rails green)*. Codebase-memory not connected ⇒ the **CLI**, ⛔ not grep-only. Build the AFFECTED PROJECTS *(`Hrot.Editor.AiShared` · `Hrot.Editor` · `Hrot.CGF` · `Hrot.Presentation` · `Hrot.SystemTests`)*, ⛔ never the whole solution in the fix loop; build once then `--no-build`; conformance suite **T3 — background it**.

## 2. ⭐⭐⭐ WHAT TO BUILD *(design §3 — five items)*
| # | task | the one thing not to get wrong |
|---|---|---|
| ⭐ **①** | **Extract `IScenarioSession` + `EditorScenarioSession`** *(the scenario half of `EditorApplication`)* into `Hrot.Editor.AiShared` *(+ `MigrationAlertManager`, the `ScenariosRoot` constant)*. Members: `NewExercise/LoadForLive/OpenForEdit/SaveCurrent/SaveAs/LoadedScenarioName/GetMigrationSidecars` | ⚠⚠ **HN-037 lesson: MEASURE captures FIRST** — a lift, ⛔ not an `s/old/new/`; ⛔ do NOT drag the tool/view/mode half *(that is E3/E4)* |
| ⭐⭐ **②** | **Editor delegates** its scenario members to the shared session | ⛔⛔ **editor byte-identical** *(the gate)* |
| ⭐⭐ **③** | **CGF instantiates** `EditorScenarioSession` over **CGF's own world + orchestration bus** | ⭐ the parameterised-world finding makes this free |
| ⭐⭐⭐ **④** | **`ScenarioMenuCommands` takes `IScenarioSession`**; register the **DISTINCT items §3a** on BOTH hosts: `File/Live/New Exercise` *(clear+fresh, CONFIRM dialog)* · `File/Live/Load Scenario` *(→ `/scenario/load/live`)* · `File/Edit/Open Scenario` *(→ `/scenario/load/edit`)* · `File/Save` *(edit-mode)* · `File/Checkpoint/Take Checkpoint` *(→ `TakeCheckpointIntent`)* | ⛔⛔ **NO chameleons, no per-host default in the menu (R2)** — each a distinct action, shown per serviceability *(ruling 49)*. ⛔ **Open Asset / New Asset from Recipe = E2, NOT here**; ⛔ **Restore Checkpoint = Feature X** |
| ⭐ **⑤** | **Conformance** — extend the `SUBSET-BY-DESIGN` menu verdict *(CE-045 `SubsetShape`)* to the new items + a routing rail *(live vs edit, and the NewExercise confirm-branch)* | reuse `SubsetShape`, ⛔ no new verdict type |

⛔⛔ **NO TOOLBAR CHANGES (R3)** — which actions get a toolbar button is the future toolbar-customization AQ. Leave the toolbar exactly as `CE-037..045` shipped it; ⭐ a rail asserting the toolbar set is unchanged is a cheap guard.

## 3. ⭐ DONE — rails *(design §6)*
- editor byte-identical *(delegation + label gate)*; CGF `File` menu dumps the five E1 items + the `SUBSET-BY-DESIGN` verdict holds; routing rails green; NewExercise confirms before clearing; toolbar set unchanged.
- affected-project builds; conformance suite named + run *(T3, background)*; reds proven pre-existing by `git diff`.

## 4. ⭐ LANE & COLLISION
⭐ **Yours:** `Hrot.Editor.AiShared/**` *(the session + moved types)* · `EditorSubsystem.cs`/`EditorApplication.cs` *(delegate)* · `CgfSubsystem.cs` *(instantiate)* · `ScenarioMenuCommands.cs` · `ClusterConformanceRails.cs`. ⚠ hot files — **rule-4 re-pull**. ⛔ Do NOT touch the toolbar registration *(R3)*, the diagnostics lane's files, or build checkpoint restore.

## 5. GATES *(rule 8)* + WHEN DONE
one row per gate · counts · Δ vs the started-marker · `--no-build` column · reds by `git diff` · `tracker-counts.py` · `rulings-check.py` · `design-digest.py --check` · `mermaid-check.mjs` on the design · the `CE-` ids. **When done:** fold any as-built deviation into `DESIGN_Cgf_Scenario_Session_Slice.md` *(obligation ⑤)*; the report points at the design + carries the DECISION LOG.
