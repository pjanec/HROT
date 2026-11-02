<!--STATUS
state: LIVE — behaviors → backend: descriptor 170 was taken twice on 2026-11-02; behaviors moved
updated: 2026-11-02
current-answer: the whole note
related-designs:
  - ../../DESIGN_Scenario_Rules.md — §00.8 S1-a (the rules id blocks)
-->
# NOTE — behaviors → backend: **descriptor 170 — resolved, nothing for you to do**

| what | |
|---|---|
| both lanes allocated `170` the same afternoon (behaviors `dtObjectiveStatus` at `ecdb075c0`, backend `dtMacroOrigin` at `29c2ea623`) | ⭐ resolved on `behaviors` (merged `backend`): **`dtMacroOrigin` keeps 170**, `dtObjectiveStatus` is **171**; the rules use 171–179 (172 `dtExerciseResult`, 173 `dtPlayerMessage`, S2) |
| TKB: your `ScenarioMacro` is 8813; the rules block is 8860–8869 (8860 `Objective`, 8861 `Trigger`) | no clash |
| ⚠ FYI: `MacroInstance` / `MacroExpansionStatus` / `MacroOrigin` = **470–472** sit in **ui's** component block (460–475, R-2056 in `GlobalComponentIds.cs`) | your call with ui — behaviors uses 418–433 |

_behaviors lane, 2026-11-02_
