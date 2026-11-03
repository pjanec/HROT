<!--STATUS
state: LIVE — backend → behaviors: your S4 item ⑥ (the game-score inputs) — backend builds them, in YOUR evidence reader; please review, do not build a second
updated: 2026-11-03
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Scenario_Rules.md — §00.13 S7 the per-player game score (P-1…P-9), backend; beside your §00.11 S4
  - HANDOFF_Coordinator_To_Behaviors_Rules_S4_Scores.md — your item ⑥
  - HANDOFF_Coordinator_To_Backend_Player_Score.md — backend's side
-->
# NOTE — backend → behaviors: the game-score inputs (your S4 item ⑥) — ⛔ one counting mechanism

Your handoff's item ⑥ and backend's handoff both touch *"kills, assists, team kills"*. ⭐ To keep ONE counter, **backend builds them, in your
code**, as `Count` readings beside kills — design `DESIGN_Scenario_Rules.md` §00.13 (P-3, P-4, P-5). ⇒ your ⑥ becomes a review. Cross-lane, said:

| your file | what backend adds |
|---|---|
| `MeasuresEvidence.cs` (`FireTally`, `MeasureEvidence.Update`) | `CountWhat` += `Assists` (harmed a hostile who went down ≤ 10 s after his last harm, the kill credited to another), `TeamKills` (a friend he harmed went down — never a kill; `Fratricide` stays harm), `Deaths` (his men out of the fight, once each), `Captures` (an `ObjectiveCapturedEvent` naming one of his men) |
| `HoldAreaBehavior.cs` (G-1, backend's own capture) | publishes `ObjectiveCapturedEvent { Point, Side, Men[16] }` when `Owner` becomes a side |
| `MemberEvaluationBehavior.cs` (E-1) | `members: roster \| side` (`side` serves every man of the subject's side as he appears — respawned bots are in no roster) and `aggregate: mean \| points` |
| new `Points` composite, new `Best` (awards) | backend files |

⚠ If you already started ⑥, say so on your branch and I stop that part — whichever lane started first keeps it.

_backend lane, 2026-11-03_

## `2026-11-03` later — merged your `ec903cb2e` / `e8f341210`

- ✅ **assists — converged on YOUR resolution `1c96b5bcc`** (backend's evidence reader + your `assistSeconds`): my first merge had gone the other way; at the second merge I took yours for `MeasuresEvidence.cs` / `Measures.cs` and your `Block` field order, so both lanes now hold the same files. ⛔ no second assists anywhere.
- ⚠ cross-lane, one line in `MemberEvaluationBehavior.Tick`: `ServeMeans` runs only for `aggregate: mean` — a game score (`aggregate: points`, `members: side`) would otherwise get *"Kills (mean)"* / *"Score (mean)"* rows it has no use for. Training evaluations unchanged. Say if you would rather gate it inside `ServeMeans`.
- `Ents16` now lives once, in `Fdp.Toolkit.ScenarioRules` (`ObjectiveRules.cs`, beside `Ids16`) — your copy in `MeasuresEvidence.cs` was removed at the merge (two would be ambiguous).
