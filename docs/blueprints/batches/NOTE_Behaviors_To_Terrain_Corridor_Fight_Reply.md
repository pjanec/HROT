<!--STATUS
state: FYI — behaviors → terrain, the reply to NOTE_Terrain_To_Behaviors_Corridor_Fight.md
updated: 2026-11-19
current-answer: the whole note
related-designs:
  - NOTE_Terrain_To_Behaviors_Corridor_Fight.md — the note this answers
  - DECISIONS_Behaviors.md — D-11, the two calls the run produced
-->
# NOTE behaviors → terrain: `pc-corridor-fight` — your question, and what the scenario showed (CE-4672 / CE-4666)

**Your dead-man row (CE-4668):** it is not vacuous. Death does not remove `BehaviorState`: `HealthApplicationSystem.StopTheDown` strips the man's
`CanMove` / `CanShoot` (`CombatLife.ApplyHitCapabilities`) and leaves his components — so `BehaviorState.ActiveBehaviorHash != 0` on Health ≤ 0 is the right
probe; keep it as it is.

**Your step-1 split, run (stepped, `behaviors@2c14d016c`):** with Red's posts at `SearchRadius` 25 the defence plan DOES post the line at the corridor's edge
on cover — and the rows stay red, because the men BREAK under Blue's fire (from ~160 m, 5.4 s) at 11–15 s and lie prone short of their posts. Your scenario
found that, and it is now `DECISIONS_Behaviors.md` **D-11** (A: the line breaks before its cover; B: the base of fire holds every man at one point — Blue
Squad 2's two red rows). Nothing to change in the scenario meanwhile; its rows are the targets.

**The ammo / target row you could not express (CE-4667):** the man's rounds are `WeaponState.Ammo` (and his mount children's); a squad's "no live target at
its posts" is the play's own state, not in the dump — the nearest dump-visible signal is the base of fire's PROGRESS report `TargetsDown` (D-7) in the squad's
report queue. A `report` cue on it would express "it stopped firing because its targets are down".
