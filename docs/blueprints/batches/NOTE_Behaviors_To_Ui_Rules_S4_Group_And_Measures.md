<!--STATUS
state: LIVE — behaviors → ui: S4's per-member evaluation, the measures and `group` are on the wire
updated: 2026-11-02
current-answer: the whole note
related-designs:
  - ../../DESIGN_Scenario_Rules.md — §00.11 E-1…E-3 (as built)
  - NOTE_Behaviors_To_Ui_Rules_View_Data.md — the earlier data contract this extends (it promised this note)
  - ../../DESIGN_Rules_View_UI.md — your design
-->
# NOTE — behaviors → ui: **`group`, the measures and the per-member evaluation have landed** (`behaviors@02c50f84c`)

| what | where | for the screens |
|---|---|---|
| ⭐ `group` | `ObjectiveStatus.Group` (`FixedString32`) on `EntityObjectiveStatus` (171, appended, goes at once) · `GET /objectives` row `group` | the section a standard is shown under: `mission` · `fire` · `reports` · `conduct` … (authored; "" = none) |
| measures | `Role = Measure`, `Kind` = `HitRatio` · `Count` · `ReportLatency` · `TimeTo` · `Sample` (+ backend's `Counter`) | draw `Value` against `Target`, colour by `Score` (0…1). ⚠ the "lower is better" direction is NOT on the wire — `Score` already folds it in; say "≤ Target" only if you need the words (ask and I add a flag) |
| per-member evaluation | `Kind = MemberEvaluation`: a composite whose children are one inserted macro root per member (each with `Subject` = that man) | the trainee debrief: a man's standards = the statuses whose `Subject` is him, grouped by `group`; his squad / platoon up the `Parent` chain (SR-112) |

Sample library: `macros/evaluation/infantry/` (`Default`, `SquadLeader`). ⭐ No new topic, no new component id — the debrief reads what is already replicated.
