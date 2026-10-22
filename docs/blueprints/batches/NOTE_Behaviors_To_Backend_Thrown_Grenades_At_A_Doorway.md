<!--STATUS
state: LIVE
updated: 2026-10-21
current-answer: the whole note
related-designs:
  - ../../designs/group-maneuvers/DESIGN_Building_Clearing.md — §7.3 BC-4 (CB-68…CB-70): the room drill's "bang out" this blocks
  - ../../DESIGN_Weapon_Layers.md — §5f WL-4 L-1: ThrowArc (the low throw, else the lob) and the weapon brain's hidden-target throw
-->

# NOTE — behaviors → backend: a grenade thrown at a doorway bursts in the air, or in the thrower's face (blocks BC-4, `CE-2260`)

**Found measuring `bc-room-clear` live** (`ClearingScenarioTests.CE2257_*`, 2026-10-21, with the room drill's new "bang out": AssaultRiflemen
2009 — rifle, 2 stun grenades, 2 M67 — at a room the order marks Suspected). The burst log (`DetonationLog` on SimHost) of one run:

| burst | by | at | what reached whom |
|---|---|---|---|
| #0 stun | Rifleman 2 (the drill's throw, from beside the door, aim 2 m into room A) | (45.8, 109.5, **12.4**) | **nobody** |
| #1, #2 stun | Rifleman 3 (the drill) | (46.2, 110.1, **12.4**) | **nobody** |
| #3 M67 | Rifleman 3 (**not the drill** — the weapon brain's hidden-target throw) | (46.1, 110.2, 2.8) — the door's east jamb | Rifleman 4 **493**, Leader **104**, two others 27; the insurgent 1 |
| #4 M67 | Rifleman 3 (the weapon brain) | (46.0, 110.1, 1.1) — the jamb, at his feet | himself **143**; the insurgent 10 |

## ① A short hand throw is LOBBED at full speed and its time fuze fires in the air

`ThrowArc.Launch` tries the low arc and, when the terrain would stop it, flies the high arc at the mount's full speed (15 m/s). For a 2–3 m
throw that is near-vertical: apex ≈ 11 m, flight ≈ 3 s — and the stun grenade's time fuze (1.5 s) fires at ≈ 12 m. ⚠ The low arc was judged
blocked although the 2-D line passes the opening (behaviors' `RoomDrill.ThrowClears`: the line crosses the wall's plane inside the doorway,
0.1 m clear of the edges) — so either the throw's origin (`ShotOrigin.From`, the hand / muzzle offset) clips the jamb, or the open door's leaf
stands in the opening. ⭐ Lean (yours): a hand throw solves its SPEED for a short target (a soft toss, below the mount's maximum) rather than
lobbing at full speed; and a time-fused round never lobbed so its fuze ends above the point.

## ② The weapon brain throws an M67 from beside a doorway into the jamb

`HiddenTargetThrow.TryThrow` (R-261) fired at a hidden insurgent inside the room from a man stacked BESIDE the door: the round hit the jamb
and burst there. Its friend check is a flat 2-D radius round the TARGET (`FriendlyInBurst`) — it never asks whether the throw clears the wall
or where the round would stop. ⭐ Lean (yours): the same arc check the flight uses (`ThrowArc.Clears`) before the weapon brain throws; and the
friend check at the point the round would actually stop.

## What behaviors did, in its own lane

- The drill's grenade is built and rail-tested (Building Clearing §7.3a; `SquadManeuverTests.CE2260_*`): who throws, the friend check by the
  area effect's own laws, the jamb point, the bang-out timing.
- ⛔ **No demo fields it until ① and ② are fixed.** `bc-room-clear` is back on InfantrySoldiers (no grenades); `bc-house-high-intensity` is
  not shipped; `bc-house-defended` (BC-5) fields InfantrySoldiers. The AssaultRifleman type (2009) stays in the catalogue, unused.
- The twins now print every burst (`DumpBursts`) — the check for your fix is `CE2257_*` with the scenario's soldiers back on 2009 and its room
  `Suspected`.

_behaviors lane, 2026-10-21_
