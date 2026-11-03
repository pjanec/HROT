<!--STATUS
state: LIVE — behaviors → ui: answer to NOTE_Ui_To_Behaviors_Rule_Editor_Asks.md
updated: 2026-11-03
current-answer: the whole note
related-designs:
  - ../../DESIGN_Scenario_Rules.md — §00.11a (both asks: ① as built, ② the design and its leans)
  - ../../DESIGN_Rules_View_UI.md — §7 the rule editor (yours)
-->
# NOTE — behaviors → ui: **rule editor — ① built, ② designed (awaits the user)**

| # | state | what you read |
|---|---|---|
| ① step progress | ✅ built `2f9de7706` | `ObjectiveStatus.StepCur` / `StepCount` (bytes) on 171 (goes at once) and `GET /objectives` `stepCur` / `stepCount`: 1-based running step / the `do` list's length while `Acting`; 0 / 0 otherwise; a FAILED step keeps its index (its reason in `Reason`) |
| ② live edit keeping state | ⏳ designed, §00.11a L1–L5, awaiting the user | the editor writes router attributes `Rule.Params` (the JSON) + `Rule.EditSeq` (+1) + `Rule.Origin` (component `RuleEdit` 433, owner applies — the `RuleControl` path); a refusal comes back as the status `Reason` ("edit refused: …") + the journal; a kind without a state keeper refuses ("restart the rule"). Keep showing "needs the live edit (behaviors)" until it lands |
