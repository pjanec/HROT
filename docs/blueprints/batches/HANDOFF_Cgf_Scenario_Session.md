<!--STATUS
state: LIVE
build-state: DISPATCH — UI/CGF lane. AQ60 Slice A: extract the shared scenario-session facade, instantiate
  in both hosts, wire File/Scenario on both. Checkpoint RESTORE + capability-gating are FUTURE (design §8).
updated: 2026-08-26
current-answer: pointer + autonomy. DESIGN (with UML): DESIGN_Cgf_Scenario_Session_Slice.md.
  Decision trail + the user ruling: Architect_Question_60 (§3b, §4).
known-conflict: moves EditorApplication's scenario half into Hrot.Editor.AiShared; edits CgfSubsystem.cs +
  EditorSubsystem.cs + CgfEditorShellToolbar.cs (UI/CGF lane's hot files) ⇒ rule-4 re-pull.
-->
# HANDOFF — **CGF scenario session: shared facade + File/Scenario on both hosts** *(AQ60 Slice A — UI/CGF lane)*

> 📌 **Dispatched at `<coordinator HEAD>`** *(confirm `git rev-parse origin/claude/blueprint-authoring-status-6sr5ld`)*.
> ⭐ Branch FRESH from the coordinator *(rule 7)*; **rule 1b started-marker before any code.** ⛔ No PR.
> ⭐ Continue **`CE-`** ids; **you allocate the numbers** *(rule 3; last used `CE-045`)*; state every id *(rule 5)*.

## 0. ⭐⭐⭐ BUILD FROM THE DESIGN — **do NOT design here**
📄 **[`DESIGN_Cgf_Scenario_Session_Slice.md`](../../DESIGN_Cgf_Scenario_Session_Slice.md)** — READY-TO-BUILD.
⭐⭐ Class + sequence UML in **§4/§5** — check before building *(obligation ③)*, report match/deviation. Decision trail + the user ruling *(near-verbatim)*: [`Architect_Question_60`](Architect_Question_60_Cgf_Scenario_Session.md) §3b/§4. ⭐ **The user's governing principle:** CGF ≡ editor bar distributed-vs-no-network; **most stuff shared, minimal duplication.**

## 1. ⛔ AUTONOMY + BUILD RULES
§0-style autonomy *(decide-and-log; stop the ITEM not the batch — R-106; DONE = design §6 rails green)*. Codebase-memory not connected ⇒ the **CLI**, ⛔ not grep-only. Build the AFFECTED PROJECTS *(`Hrot.Editor.AiShared` · `Hrot.Editor` · `Hrot.CGF` · `Hrot.Presentation` · `Hrot.SystemTests`)*, ⛔ never the whole solution in the fix loop; build once then `--no-build`; conformance suite **T3 — background it**.

## 2. ⭐⭐⭐ WHAT TO BUILD *(design §3 — six items)*
| # | task | the one thing not to get wrong |
|---|---|---|
| ⭐ **①** | **Extract `IScenarioSession` + `EditorScenarioSession`** *(the scenario half of `EditorApplication`)* into `Hrot.Editor.AiShared` *(+ `MigrationAlertManager`, the `ScenariosRoot` constant)* | ⚠⚠ **HN-037 lesson: MEASURE the scenario methods' captures FIRST** — this is a lift, ⛔ not an `s/old/new/`; ⛔ do NOT drag the tool/view/mode half |
| ⭐⭐ **②** | **Editor delegates** its scenario members to the shared session | ⛔⛔ **editor byte-identical** *(the gate — `File/Scenario` menu + `IEditorLogic` scenario surface unchanged)* |
| ⭐⭐ **③** | **CGF instantiates** `EditorScenarioSession` over **CGF's own world + orchestration bus** | ⭐ same class, CGF's world — the parameterised-world finding makes this free |
| ⭐ **④** | **`ScenarioMenuCommands` takes `IScenarioSession`**; two load items *(Load-for-Edit → `/scenario/load/edit`, Load-for-Live → `/scenario/load/live`, confirmed-at-origin)*; **New** mode-branched *(live: clear+fresh + confirm dialog; edit: new-from-recipe)*; **Save** *(edit-mode)* = the editor's save via the session | ⛔ both load items on BOTH hosts; default differs by subsystem, capability does not |
| ⭐ **⑤** | **Checkpoint Save** item → publishes the existing `TakeCheckpointIntent` *(to the master)* | ⛔ NOT in the scenario facade — a separate slot; ⛔ **do NOT build restore** *(design §8, deferred)* |
| ⭐ **⑥** | **Conformance** — extend the `SUBSET-BY-DESIGN` menu verdict to `File/Scenario/*`; a unit rail for load-edit-vs-live routing | reuse `SubsetShape` *(CE-045)* |

⭐ **Fully-featured — ⛔ NO host-conditionals** *(capability-gating is a future layer, design §8)*.

## 3. ⭐ DONE — rails *(design §6)*
- **editor byte-identical** *(the delegation gate)*; CGF `File/Scenario` dumps the five items + the `SUBSET-BY-DESIGN` verdict holds; load-routing rail asserts the edit/live variant; New live-branch confirms before clearing.
- affected-project builds; conformance suite named + run *(T3, background)*; reds proven pre-existing by `git diff`.

## 4. ⭐ LANE & COLLISION
⭐ **Yours:** `Hrot.Editor.AiShared/**` *(the new session + moved types)* · `EditorSubsystem.cs`/`EditorApplication.cs` *(delegate)* · `CgfSubsystem.cs` *(instantiate + adopt)* · `CgfEditorShellToolbar.cs` *(File/Scenario + Checkpoint-Save slots)* · `ScenarioMenuCommands.cs` · `ClusterConformanceRails.cs`. ⚠ hot files — **rule-4 re-pull**. ⛔ Do NOT touch the diagnostics lane's files; ⛔ do NOT build checkpoint restore.

## 5. GATES *(rule 8)* + WHEN DONE
one row per gate · counts · Δ vs the started-marker · `--no-build` column · reds by `git diff` · `tracker-counts.py` · `rulings-check.py` · `design-digest.py --check` · `mermaid-check.mjs` on the design · the `CE-` ids. **When done:** fold any as-built deviation into `DESIGN_Cgf_Scenario_Session_Slice.md` *(obligation ⑤)*; the report points at the design + carries the DECISION LOG.
