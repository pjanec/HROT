<!--STATUS
state: LIVE
updated: 2026-10-16
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md — OWNS the lock (M4, D-D "the lock IS the aim time") and the top-attack profile (§3)
  - docs/designs/vehicle-tactics/DESIGN_Vehicle_Exposure_Cycle.md — §4: the exposure budget that denies a lock (where it was measured)
  - docs/designs/vehicle-tactics/DESIGN_Urban_Armour_Tactics.md — the V7 demo table (`ua-javelin-lock`)
-->

# HANDOFF — behaviors → backend: a Javelin fires blind without a lock, and its top attack overflies a near target (CE-2200, CE-2201)

**Dispatched at `b86497d1e`** (behaviors). Your scope is frozen at that sha; nothing here stops an item.

## The measurement (live `ua-javelin-lock`, test-town, `2026-10-16`)

Two M1s (100), two Javelin teams (2006, `CombatPosture {target, assault}`). "Tank Cycling" runs `BasicVehicleSop` (the exposure cycle:
short exposures, back to its hide); "Tank Open" stands idle in the open 230 m from "Javelin B". `/combat/shots`:

| shooter → tank | `target` | outcome | where it ended | face |
|---|---|---|---|---|
| Javelin A → Tank Cycling | **null** (an at-point round) | **Hit** | (248, 72, 0.5) — the tank's running gear | **Side** |
| Javelin A → Tank Cycling | the tank | Detonated | (658, −351, 0.1) — in the sand, far away | — |
| Javelin B → Tank Open | **null** (an at-point round) | **Hit** | (380, 388, 0.5) — the running gear | **Side** |
| Javelin B → Tank Open | the tank (230 m) | Detonated | (380, **970**, 0.1) — **580 m beyond the tank** | — |

### ① CE-2200 — a lock-needing guided mount fires BLIND

The two hits are `AimAndFireExecutor.FireBlind` rounds (AQ89 A / R-269): `Target = null`, `AtPoint = true`, at the hidden target's
last-known point. `FireProcessingSystem` then flies them direct (`!evt.AtPoint` gates the top attack), so they meet the side. A Javelin
cannot fire without a lock (M4 D-D: *"the lock IS the aim time"*), and the blind path skips the lock clock. ⚠ That is exactly the
gap the exposure cycle exists to deny: the cycling tank stays exposed less than 4 s, and still gets hit by a blind round sent at where it last was.

⭐ **Ask:** a mount that **locks** (a guided `MissileDto` with `AimSeconds > 0` — the M4 lock) never fires blind. This is the same gate
the executor already has for an indirect mount (`CombatTkb.WarheadOf(...) is { Indirect: true }` ⇒ `HoldNotSeen`). Say `HoldNotSeen`
(or a new `NoLock`). An unguided RPG / LAW keeps its blind round.

### ② CE-2201 — the top attack overflies a near target and is lost

The 230 m shot was launched in top attack (`TopAttackMinRangeM` 150). The profile climbs at `ClimbAngleDeg` 30° to `CruiseZ` (muzzle + 150 m)
at 140 m/s, and turns at most `TurnRateDegPerSec` 30°/s (radius ≈ 267 m):

- reaching 150 m takes ≈ 260 m of ground, so it is past the tank while still climbing;
- the dive then needs a turn back that a 267 m radius cannot make;
- it hit the ground 580 m beyond.

The one rail, `GuidedRoundSystemTests.M4_AJavelin_ClimbsAndDives_AndArrivesOnTheRoof`, fires at 1200 m only.

⭐ **Ask (your design — lean):** the top-attack profile arrives on the roof at every range it is chosen for. Our lean: at launch, cap the
apex to what the range allows (e.g. a lofted apex ∝ range, like the real missile's short-range trajectory), and fall back to a direct
flight below the profile's geometric minimum. Alternatively, raise `TopAttackMinRangeM` to that minimum. Add a rail at 200–300 m.

## What behaviors did meanwhile (no dependency on you)

- `ua-javelin-lock` + its check (`scripts/utility-demo-check.py`) are in; the checks ① *"its short exposures deny Javelin A a lock"*
  and ② *"from above"* stay red until ① / ② land.
- Recorded in `DESIGN_Vehicle_Exposure_Cycle.md` §4 (the live findings) and the tracker rows CE-2200 / CE-2201.
