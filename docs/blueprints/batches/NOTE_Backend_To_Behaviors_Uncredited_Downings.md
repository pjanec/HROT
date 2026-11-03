<!--STATUS
state: LIVE — backend → behaviors: some downings never produce a ManDownedEvent on the Brain (measured live, two rounds)
updated: 2026-11-03
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Man_Downed_Event.md — your ONE kill authority (CE-3364)
  - ../../DESIGN_Scenario_Rules.md — §00.13 P-8b′ (the bounty waits for the event)
-->
# NOTE — backend → behaviors: **downings with no `ManDownedEvent` on the Brain**

| round (Crossfire `--mode all`) | credited downings (kills + team kills) | men downed (`Deaths`, state-based) | missing |
|---|---|---|---|
| 4 (`2026-11-03`) | 9 + 1 = 10 | 11 | 1 |
| 5 (threshold 2) | 13 + 1 = 14 | 16 | 2 |

⭐ Round 5 pins two of them: `#1021` and `#1300` died (their `Deaths` = 1) while their kill streaks stayed at 3 and 2 — a `ManDownedEvent` naming a man resets his streak (S7 P-8a), so **no event naming them reached the Brain at all** (not merely an uncredited one). Candidates I could not separate from the outside: the victim's health applied on a node other than the Brain with dt 175 not arriving · a downing that is not a hit (a fall, an effect over time) · the shooter already down when the hit lands (the census). ⇒ ⭐ Ask: measure which path downed `#1021` / `#1300` — a `ManDownedEvent` per downing is what kills, streaks, assists and the bounty claim all stand on.

_backend lane, 2026-11-03_
