<!--STATUS
state: LIVE — behaviors → backend: what the per-player game score reads (answers item ⑥ of the S4 handoff)
updated: 2026-11-03
current-answer: the whole note
related-designs:
  - ../../DESIGN_Scenario_Rules.md — §00.11 "S4 scores ② / ⑥ AS BUILT" (the measures and the pass mark)
  - HANDOFF_Coordinator_To_Backend_Player_Score.md — your side (score per player, team kill, awards, bounty)
  - HANDOFF_Coordinator_To_Behaviors_Rules_S4_Scores.md — item ⑥, this note's origin
-->
# NOTE — behaviors → backend: **count the per-player game score through the `Count` measure**

**The choice: (a) a per-player `Count` measure, inserted by your awards macro on each possessed person.** No new API, one tally, recorded and replicated like every status. ⛔ A public static reader on `MeasureEvidence` was rejected: a second tally with no owner and no recording.

| you need | read | notes |
|---|---|---|
| kills | `Count { what: "kills", subject: <person> }` → `ObjectiveStatus.Value` | a hostile out of the fight after the person's hit, once per victim |
| assists ⭐ new | `Count { what: "assists", assistSeconds: 10 }` | a hostile the person hit and somebody else finished, within `assistSeconds` (default 10) of the person's last hit; never for the person's own kill, once per victim |
| team kills | `Count { what: "fratricide" }` | a hit on his own side, not himself. ⚠ it counts HITS (damage events), not deaths; a kill is not split out — score the −200 from `Value` as you decide |
| shots / hits | `Count { what: "shots" }` · `"hits"` · `HitRatio` | |
| captures | the capture objective's own `Owner` / `Achieved` (not a Count) | |

- **Attribution** (CE-4155): `attribute: "last" | "original" | "both"` on every `Count` — `last` credits the damage event's `Source`, `original` its `OriginalSource` (a rethrown grenade blames the rethrower on `last`, the thrower on `original`), `both` either. Pick per rule; a team-kill rule that blames the rethrower is `last`.
- **Subject**: the measure's `common.subject` — a person gives that person's counts; a squad counts all its men.
- **Counts start when the measure is inserted** — it reads the frame's events, never history. Insert at the player's join (or the round start) and the score covers the round.
- Every status now also carries **`Pass`** (0..1): GO / NO-GO is `Score ≥ Pass`. A `Count` measure's `Pass` defaults to 1 (its `target` met); author `common.pass` to change it. A `Counter` (the scenario-moved pool) keeps Pass 0.
- Read from any node: the statuses replicate (`EntityObjectiveStatus`, descriptor 171), `GET /objectives` shows `value` / `target` / `score` / `pass`.

_behaviors lane, 2026-11-03_
