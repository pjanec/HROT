<!--STATUS
state: LIVE — behaviors → ui: the SR-103 rules lanes provider is built, with the S4 score history in it
updated: 2026-11-03
current-answer: the whole note
related-designs:
  - ../../DESIGN_Rules_View_UI.md — RV-4 (yours): the two lanes it designs are built exactly as designed
  - ../../DESIGN_Scenario_Rules.md — SR-103; §00.11 E-4 (score history, as built)
  - HANDOFF_Coordinator_To_Behaviors_Rules_S4_Scores.md — item ④ ("RV-4's provider owner is undecided — coordinate on the branch")
-->
# NOTE — behaviors → ui: **`ScenarioRulesTimelineLanes` is built** — RV-4's two lanes + the score history, in the ONE shipped catalogue

The coordinator's S4 handoff (④) needs a score-history lane in RV-4's provider and left the owner open. Nothing had built the provider yet
(grep: 0 hits), so behaviors built it ONCE — your two lanes as RV-4 designs them, plus the score lane — to avoid a second provider. ⭐ It is yours
to extend: add lanes to the same class, never a second one.

| lane | id | Value (· X) | from |
|---|---|---|---|
| Objectives | `objective.state` | the `ObjectiveState` number | value change of `ObjectiveStatus.State` |
| Triggers | `rules.fired` | `FireCount` (⚠ the first sight carries the count it has — skip Value 0) | value change of `ObjectiveStatus.FireCount` |
| Score *(hidden)* | `objective.score` | `Score` · X = `Target` | value change of `ObjectiveStatus.Score` |

`FDP/Toolkits/Fdp.Toolkits/ScenarioRules/ScenarioRulesTimelineLanes.cs`, registered in `ShippedTimelineLanes.Catalog()` ⇒ the editor, the Replay
Browser and every node's live timeline index it (CE-3330); ⛔ no new store — the recording already holds every frame. ⚠ RV-4 said `rules.fired` =
`TriggerState → Fired`; built as the fire COUNT moving instead (a fire that returns to Armed / CoolingDown within one frame is still one entry).
Say if the screens need the state edge instead.
