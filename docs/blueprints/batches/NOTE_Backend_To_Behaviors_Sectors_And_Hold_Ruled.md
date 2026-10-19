<!--STATUS
state: LIVE — backend → behaviors: two of your leans were ruled by the user in the backend lane's conversation.
updated: 2026-10-19
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md — §13c DA-26…DA-30 (R-3020)
  - docs/designs/look-direction/DESIGN_Look_Direction_And_FOV.md — §3.3a the hold flag (R-3021)
-->

# NOTE — backend → behaviors: sectors and the Hold stay flag are RULED

The user asked backend for leans on your DA-26…DA-30 and on CE-2297 slice 2a, then ruled both (ids from backend's block, user words
placed in your designs — cross-lane edit, the ruled line only):

| ruling | what | user |
|---|---|---|
| **R-3020** | DA-26…DA-30 as leaned (already built, `CE-2282`) | *"Sector leans accepted."* |
| **R-3021** | the runner's Hold / Watch asks `CombatPosture` with `hold: true` — your step 1 | *"The hold stay flag approved."* |

For your step 1's "measure first": backend checked two readers. The staff's PROGRESS / SET fire on the COMMANDER's task end
(`StaffSystems.cs:19, 61-66`), and the shared runner judges an element arrived by POSITION (`SquadManeuverRunner.ElementArrived`,
`:598`). A member who never ends his Hold stalls neither. Not checked: a drill with its own completion test outside the runner.

Backend's view, given to the user with the leans: under R-3018 (fire checks move to the muscle) your `HoldNoFireArea` stays on the
brain: it is a point-in-area test on a replicated area, a rules-of-engagement decision, not a terrain read.

---
_backend lane, 2026-10-19_
