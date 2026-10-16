<!--STATUS
state: LIVE
updated: 2026-10-16
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md — OWNS the M8 loadout (the round by the target) whose HE / HEAT rounds miss here
  - docs/designs/vehicle-tactics/DESIGN_Armour_Hit_Model.md — OWNS the hit chain (the face, the part, the penetration) these rounds never reach
  - docs/designs/vehicle-tactics/DESIGN_Vehicle_Exposure_Cycle.md — §4: the live demo findings (where it was measured)
-->

# HANDOFF — behaviors → backend: a tank's HE / HEAT rounds fly through soft targets, and one of its rounds hit its own hull (CE-2203, CE-2204)

**Dispatched at `923fc9352`** (behaviors). Your scope is frozen at that sha; nothing here stops an item.

## The measurement (live `ua-tank-ammo`, basic-desert, `2026-10-16`)

An M1 (100) on `BasicVehicleSop` at (300, 280), facing north. Its targets: an idle T-72 (103) 240 m away, an HMG technical (104) at
(120, 430), 230 m, and two riflemen (2002) advancing on it. Three runs. `/combat/shots`:

| at | rounds | outcome |
|---|---|---|
| T-72 | APFSDS (penetration 650) | **Hit 3 / 3**, T-72 killed |
| technical (104) | HE / HEAT (50 / 600) | **Detonated 13 / 13, no hit** (and a bare pickup 106 in an earlier run: 22 / 22) |
| riflemen (~140–230 m) | HE (50) | Detonated 8 / 9, 1 hit |

### ① CE-2203 — a tank's HE / HEAT rounds fly through a soft target

One round in full: `penetrationSource` "loaded round 6018 (M8 loadout)", penetration 50, damage 600, `sigmaRad` 0, `deflectionRad` 0,
`tick` 410 → `endTick` 414. It went from muzzle (294.7, 283.9, 1.88), aimed at the technical (120, 430, 0.95). It ended **Detonated at
(−58.6, 579.4, 0.0)**: on the ground, exactly on the extended aim line, 460 m out and 230 m BEYOND the technical. `crossings` [] and
`hit` null.

So the round passed the technical's 5.3 × 1.9 × 1.9 m body at 0.95 m height and never met it. Every repeat ended at the same point
(sigma 0). The same gun's APFSDS meets the T-72 every time.

⭐ **Ask:** an HE / HEAT main-gun round meets the body on its line. Our guesses at the cause, ⛔ unmeasured: the per-tick sweep, or a
fuze / visible-round (LR-2) path that skips the entity test. A rail: a tank gun's HE round at a pickup 230 m off hits it.

### ② CE-2204 — one of the tank's own rounds hit its own hull

`rounds at the tank`: `('Tank', 'Hit', 'Hull', 'Side'): 1`, i.e. a shot whose shooter AND hit entity are the M1 itself. The M1 ended at 2760 / 3000.
The 119 rifle hits on it all arrived at penetration 5 (`arrivingDamage` 25) against its 350 mm side, which should do nothing. ⇒ the lost 240 is most likely
the self-hit (⛔ not separated per hit).

⭐ **Ask:** a round never hits its own shooter (or its shooter's hull). Confirm where the 240 came from; if the 5 mm rifle hits did
any of it, that is a second defect.

## What behaviors did meanwhile (no dependency on you)

- `ua-tank-ammo` + its check (`scripts/utility-demo-check.py`) print every round's outcome / hit part / face and one round in full.
  Its two checks pass (it fires at all three, and a different round for the tank); ① here is why the soft targets survive.
- Recorded in `DESIGN_Vehicle_Exposure_Cycle.md` §4 (live findings) and the tracker rows CE-2203 / CE-2204.
