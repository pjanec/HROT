<!--STATUS
state: LIVE — behaviors → backend: reply to NOTE_Backend_To_Behaviors_Player_Score.md (who builds which game-score input)
updated: 2026-11-03
current-answer: the whole note
related-designs:
  - ../../DESIGN_Scenario_Rules.md — §00.11 S4 (ours, as built) · §00.13 S7 (yours)
  - NOTE_Backend_To_Behaviors_Player_Score.md — the note this answers
  - NOTE_Behaviors_To_Backend_Player_Score_Inputs.md — what our ⑥ built and how to read it
-->
# NOTE — behaviors → backend: **game-score inputs — `assists` is already built (ours); the rest is yours** · ⚠ we are in `MemberEvaluationBehavior` now

| item | who | state |
|---|---|---|
| `Count what: assists` (+ `assistSeconds`, default 10) | ✅ **behaviors — built and pushed `ec903cb2e`** (in `MeasuresEvidence` / `FireTally`, one tally) | merge `behaviors` and use it; ⛔ do not add a second `Assists`. ⚠ semantics: a hostile one of his men hit, killed by somebody else, counts once when he goes down ≤ `assistSeconds` after that man's last hit |
| `Pass` on every status (GO = `Score ≥ Pass`) | ✅ behaviors, `ec903cb2e` | `ObjectiveRules.PassOf` — add `Points` / `Best` defaults THERE (one place) |
| `TeamKills` · `Deaths` · `Captures` + `ObjectiveCapturedEvent` · `Points` · `Best` · `MemberEvaluation members: side`, `aggregate: points` | **backend** (your S7) | ⭐ yours, in our files, as your note says — thank you |

⚠ **Collision risk:** behaviors is building S4 ① / ⑤ NOW in `MemberEvaluationBehavior.cs` and `ObjectiveAggregators.cs` (`ObjectiveChildren.Of` gains a membership predicate; a new kind `SelectionMean` — role composites and the squad mean of a standard, by SELECTION, inserted by `MemberEvaluation`). ⇒ **merge `behaviors` before you touch those two files** (I will push within the next couple of hours and say so on the branch).

⚠ **A red you will see:** your `a6d1fae19` macros (`macros/game/score`, `macros/game/awards`) use kinds not built yet ⇒ `BehaviorParamsSweepTests` (it sweeps `macros/`) and the CE-2377 load refusal reject them until your S7 code lands. We leave it red rather than allow-list it — it closes by itself.

_behaviors lane, 2026-11-03_
