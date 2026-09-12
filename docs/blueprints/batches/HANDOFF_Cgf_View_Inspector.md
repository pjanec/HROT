<!--STATUS
state: LIVE
build-state: FRAME — UI/CGF lane. Axis-C increment E4. Coordinator gives the FRAME; the SESSION does the
  inventory + authors the class/sequence UML in a DESIGN_* doc as step 1, then builds (WHO-DESIGNS amendment).
updated: 2026-08-26
current-answer: this handoff is the FRAME. Roadmap: gap map §2c line 172. Precursors: E1/E2/E3 (all merged).
known-conflict: will edit EditorSubsystem.cs + CgfSubsystem.cs (hot files) — rule-4 re-pull.
-->
# FRAME-HANDOFF — **CGF view / inspector / property-edit** *(Axis-C E4 — UI/CGF lane, `CE-`)*

> 📌 **Dispatched at `<coordinator HEAD>`** *(confirm)*. ⭐ Rule 7; **rule 1b started-marker before code.** ⛔ No PR.
> ⭐ Continue `CE-` ids *(last `CE-051`)*; you allocate them *(rule 5)*.
> ⭐⭐⭐ **This is a FRAME.** Per the WHO-DESIGNS amendment: **YOU do the inventory *(`search_graph`)* and author
> `DESIGN_Cgf_View_Inspector_Slice.md` with a class + sequence UML as step 1**, then build, then fold the as-built.
> The coordinator verifies the design+UML on return.

## 1. ⭐⭐⭐ THE FRAME — the goal
Give CGF the editor's **view / inspector / property-edit** surface, the next editor→shared extraction after
E3's viewport interaction. 📄 Gap map §2c line 172 names it: **`View`/`DerRepo`, `CommitPropertyEdit`, +
the `RebuildAndReloadAI` dev-loop → shared.** ⭐ This is the "select an entity → see + edit its properties"
half that pairs with E3's "select/tool/camera" half — together they make CGF's inspection loop the editor's.

## 2. ⭐⭐ WHAT THE FRAME ASKS FOR *(you design the HOW — inventory first)*
- **Extract the view/inspector/property-edit orchestration to shared** *(the same pattern E1–E3 used: lift host-agnostic pieces to `Hrot.Editor.AiShared` / a shared module, editor delegates byte-identical, CGF composes)*.
- **`CommitPropertyEdit`** *(the write seam the Details/inspector uses)* + **`View`/`DerRepo`** read surface → shared, so CGF's inspector edits an entity's properties exactly as the editor does.
- **The `RebuildAndReloadAI` dev-loop** — decide, once measured, whether it belongs in E4 or is its own increment *(decide-and-log)*.

## 3. ⭐⭐⭐ THE TWO THINGS THE LAST THREE INCREMENTS TAUGHT — watch for both *(HN-037)*
| ⭐ | |
|---|---|
| ⛔⛔ **Expect a TWO-WAY reconciliation, not a one-way lift** | E2's create-core had DRIFTED in 3 places; E3 found CGF hand-rolled parallels. **Measure whether CGF already has an inspector/property surface** *(it composes Details/property panels?)* and, if so, the shared type must unify both and **CGF's copy must die** *(ruling 9)* |
| ⭐ **Measure captures before writing the shared body** | a lift, ⛔ not `s/old/new/`; the report states, per host, what was deleted and what it now routes through |

⚠ **Selection is already shared (E3, `DefaultSelectionState`)** — E4 READS it to know what to inspect; ⛔ do not re-do selection. ⛔ Property-edit ≠ the live `stage_entity_variable` debug seam — measure which write path the inspector actually uses.

## 4. ⭐ DESIGN BASIS TO READ *(set intent before UML — R-129)*
The E1–E3 designs *(`DESIGN_Cgf_Scenario_Session_Slice.md` · `DESIGN_Cgf_Asset_Picker_Shell_Slice.md` ·
`DESIGN_Cgf_Tool_Selection_Camera_Slice.md`)* as the extraction-pattern precedent · gap map §2c *(the E-series +
the assembly wall §2c.2)* · any `DESIGN_*`/`Architect_Question` for the Details/inspector/property model *(grep first)*.

## 5. ⭐ LANE FENCES + ACCEPTANCE + PROCESS
⭐ **Yours:** `Hrot.Editor.AiShared/**` *(the extracted view/inspector pieces)* · `EditorSubsystem.cs`/`CgfSubsystem.cs` *(delegate/compose)* · `ClusterConformanceRails.cs`. ⚠ hot files — **rule-4 re-pull**. ⛔ Do NOT touch DebugApi/blueprint-serialization *(MCP lane)* or test-harness *(backend lane)*.
- **Acceptance:** editor byte-identical *(the delegation gate)*; CGF's inspector shows + edits an entity's properties through the shared path; any CGF parallel deleted *(a source-scan rail, like E2/E3)*; conformance verdict holds. Affected-project builds; conformance suite named + backgrounded *(T3)*; reds pre-existing by `git diff`.
- ⭐⭐ **Process (frame-delegation):** ① rule-7 + started-marker · ② **INVENTORY + author `DESIGN_Cgf_View_Inspector_Slice.md` with class+sequence UML** · ③ build affected only · ④ fold as-built *(obligation ⑤)* · ⑤ report → design + DECISION LOG + `CE-` ids + gates.
