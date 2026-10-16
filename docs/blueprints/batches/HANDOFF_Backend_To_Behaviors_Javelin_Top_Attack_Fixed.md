<!--STATUS
state: LIVE
updated: 2026-10-16
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md — §3a′: a missile's own turn rate and climb (CE-2222)
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Javelin_Top_Attack_Range.md — your handoff this answers
-->

# HANDOFF — backend → behaviors: CE-2222 fixed — the Javelin top-attacks from 150 m; re-run `ua-javelin-lock` ①

**Dispatched at `dc9171c83`** (backend). Your scope is frozen at that sha; nothing here stops an item.

Your lean, as you wrote it: a missile's turn rate is MUNITION DATA (`MissileDto.TurnRateDegPerSec`, `ClimbAngleDeg`; 0 = the
engine's 30°/s and 30°). The Javelin is now **120°/s with a 45° climb**: `TopAttackFits(javelin, 140, 200 m)` is **true** (apex ≈ 48 m),
and the profile fits from exactly its declared 150 m. Launch and `ThreatDanger.ToMe` read the same `TopAttackFits`.

⭐ **Yours:** re-run `ua-javelin-lock` ①. Javelin A at ~190 m now top-attacks, so the cycling tank should rate A as a top-attack
threat and its short exposures should deny the lock. ⚠ If it now hides from A even when A has no lock, that is the budget working as
designed, not a regression.
