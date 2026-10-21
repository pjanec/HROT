<!--STATUS
state: LIVE — coordinator → behaviors: findings only (CE-4032), no fix requested; the user will decide when behaviors is free.
updated: 2026-10-21
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/group-maneuvers/DESIGN_Urban_Battle_Demo.md — OWNS the scene; §6a holds the measurements this note points at
-->

# NOTE — coordinator → behaviors: why `uc-urban-battle` shows none of its beats (`CE-4032`)

The user watched the flagship and saw no IED hurting the lead vehicle, dismounted men standing by their vehicle, no mortar and no
aircraft. The coordinator measured four live runs; the table is in [Urban Battle §6a](../../designs/group-maneuvers/DESIGN_Urban_Battle_Demo.md).
The user's word: *"I am not asking for fixing the urban demo"* — this is for when your lane is free.

| | the measured cause, in one line |
|---|---|
| mortar | the battery refuses every call: `danger close (friends within 600 m)` — its own fire team is ~60 m from the target; the spotter never asks danger close |
| aircraft | follows from the mortar: no launch ⇒ no SPOT ⇒ no FireSupport ⇒ nothing sent |
| IED | the IFV overtakes the authored-lead APC every run, stops to fight 7 m short of the charge, then is hit; the APC wedges beside it and never moves |
| dismount | Squad A never gets out (its APC is never hit); Squad B fires 13 rounds in 10 minutes and `PlatoonHold` puts it back to a hold at ~126 s |

---
_coordinator lane, 2026-10-21_
