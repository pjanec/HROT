<!--STATUS
state: LIVE
updated: 2026-10-21
current-answer: the whole note
related-designs:
  - ../../DESIGN_Entity_Interactions.md — §7.4.8 / R-2011 C9: a vehicle's passengers die with it unless its type says they survive (`DiesWithCarrier`)
  - ../../designs/group-maneuvers/DESIGN_Urban_Battle_Demo.md — §6a.1, where it was measured
  - ../../designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md — §11a, the convoy task the wreck stalls
-->

# NOTE — behaviors → backend: a destroyed APC keeps its passengers alive and seated (`CE-2348`), and its velocity freezes

**Found while measuring `uc-urban-battle`** (`--mode all`, `2026-10-21`, probe every 10 sim s through the debug API).

## What was measured

| sim s | Convoy APC (2001) | Squad A's five men |
|---|---|---|
| 10 | (652,101), `NetworkVelocity` (2.3, 7.4), Health 500, `CanMove` | aboard, health 100 |
| 21 | (666,155), Health 480, **`CanMove` gone** (mobility kill), `NetworkVelocity` **(0.12, 0.31)** | aboard |
| 31 → 161 | the same place, **Health 0**, `NetworkVelocity` **still (0.12, 0.31)**, task still `FollowRoute` | ⛔ **aboard, health 100** (an earlier run: until ~450 s) |
| 171 | `NetworkVelocity` (0, 0) | the squad's task changes |

## Two things that look wrong — both in backend's code, neither fixed here

| # | what | expected (design) | lean |
|---|---|---|---|
| ① | ⛔ **the passengers of a destroyed APC live on, seated, unhurt** | R-2011 C9: a vehicle's passengers die with it unless its type sets `DiesWithCarrier = false` (`CarrierSeats.PassengersSurvive`); the APC's carrier is `CarrierDefDto.PassengerSeats(8)` | `EmbarkedStateSystem` kills a passenger only when `damage` is set that frame, the vehicle `IsDestroyed`, and THIS node `OwnsHealth(passenger)` — measure on which node the vehicle's destroying hit lands versus who owns the passengers' health (CGF owns the squad) |
| ② | ⚠ **a dead vehicle's replicated velocity freezes at its last sample** for ~140 s | a destroyed or immobile vehicle reads as still | the owner stops publishing it at death; a reader on another node keeps the last sample |

## What behaviors did about ②, in its own code

The squad's standing orders read the carrier as "driving" (`VehicleCover.IsStanding` on the frozen replica) and never got out — Squad A sat in
the wreck 55 m from the enemy and the whole convoy stalled. ⭐ `SquadSopConditions` now treats a carrier that **cannot move**
(`ConvoyConditions.CanMove` false: destroyed or a mobility kill) as standing for getting out, whatever its velocity says
(`SquadManeuverTests.CE2347_BasicSquadSop_ACarrierThatCannotMove_*`). ⚠ `VehicleCover.IsStanding` itself is unchanged — its other readers
(cover, EQS) may want the same rule; yours to decide.

_behaviors lane, 2026-10-21_
