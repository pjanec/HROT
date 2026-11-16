<!--STATUS
state: LIVE — in progress, overnight unattended
updated: 2026-11-16
current-answer: the whole file (the task table first, then "Next step")
related-designs:
  - ../../designs/animals/DESIGN_Animals.md — THE design; as-built folded there (§9)
  - HANDOFF_Coordinator_To_Backend_Animals.md — the dispatch (frozen at d6d76b479)
  - NOTE_Backend_To_Ui_Animals_Contract.md — the contract ui binds to (component ids, layouts, enum values)
-->

# STATUS — the animals, backend (CE-4623) — overnight, unattended

Started at `c24f127cb` (merged `coordinator@d367b6a04`). Contract note to ui: `3fb384efa`.

| task | state | result |
|---|---|---|
| **AB-1** taxonomy | ✅ built | TKB 700…711 (`AnimalTkbCatalog`, the species table AR-21 in ONE `Row[]`), DIS `3.1.0.200–208` / `3.2.0.205`, classes `LandAnimal` `AirAnimal` + the nine species; `VehicleClass.Quadruped` (a legged preset: steers while moving, no reverse); spawned Neutral; `AnimalTkbTranslator` (AnimalState / FlockState at spawn, on every node) and the ONE question `Animals.IsAnimal`; guards: whole-body hits (no man's zones), no crowd incident, no collateral veto (air weapon, frag grenade, room grenade), no civilian casualty count, no "no harm" count, never changes side |
| **AB-2** movement | ✅ built | the ≈60-site audit: person-only rules (hit band, fire hold, gas, turn-on-the-spot gait, SUMO discs) exclude a Quadruped automatically; WIDENED to animals: lying still when down / dead (no wreck), stun, ledges, floors, blocker height; EXCLUDED: `Locomotions.IsVehicle`, `HullClearance.HasHull`; `FromVehicleClass` maps it; animals path on the INFANTRY layer; `BirdFlightModel` (cruise, capped banked turn, comes down onto a perch; never an aircraft); `MotionModelTable` no longer throws for the new classes; SUMO: an animal is a body cars brake for at its footprint |
| **AB-3** fear | ✅ built | the ONE fear system (`CivilianFearSystem`) frightens animals too, through `AnimalFear`: what it HEARS (the civilians' impulse table, from EVERY force — the hearing sensor lifts the own-side skip for an animal), what is DONE to it (near miss, hit), and the FLIGHT ZONE (every person and vehicle in the fight and not aboard, ~5 Hz per animal, staggered; a vehicle's zone ×1.5): an approaching threat → frightened, a passing one → uneasy, inside the panic zone → panic; fades over the species' recovery time; a fearless species only flinches at a blast. `Fear` (500) holds it; the band goes to `AnimalState.FearBand` only when it changes. `AnimalState` / `FlockState` replicate as `dtAnimalState` 189 / `dtFlockState` 190 (Brain group, on change). Civilians unchanged |
| **AB-4** brain, herds | ⏳ | — |
| **AB-5** species plays | ⏳ | — |
| **AB-6** jump links | ⏳ | — |
| **AB-7** panic breach | ⏳ | — |
| **AB-8** sounds | ⏳ | — |
| **AB-9** animal area | ⏳ | — |
| **AB-10** demo | ⏳ | — |

## Lane decisions (for the user's review — each says what would change it)

| # | decision | why · what would change it |
|---|---|---|
| L-A1 | `AirAnimal` = **`3.2.*.205-209`** (a range), not the exact `3.2.*.205` of §6.1 | the class table refuses two classes with ONE mask, and `Bird` (ui keys its family on it) is exactly `3.2.*.205`; the range still holds 205 and nothing else uses 206–209 in the air |
| L-A2 | component ids **500–505** (`Fear` `AnimalState` `FlockState` `HerdMember` `AnimalHome` `HerdState`) | the backend block 434–449 is full; 500+ is beyond every lane block, measured free on every lane |
| L-A3 | "is an animal" = **carries `AnimalState`** (`Animals.IsAnimal`), with a DIS-byte form for templates | one lookup per question, works in FDP (no class table there); every animal gets it from the one translator |
| L-A4 | an animal is hit as a WHOLE body (no zones) rather than a quadruped zone plan | AR-19 asks only that it never gets the human plan; a zone plan (head / body / legs) can be a `BodyZonesDto` on the type later |
| L-A6 | animals path on the INFANTRY nav layer (no dedicated animal layer yet) | an animal goes where a man goes (fields, woods, gaps) and never prefers roads; a separate layer (no building interiors for cattle) is filed |
| L-A7 | a hen on the road is a SUMO body at its own tiny footprint (a "vehicle" kind), not a person disc | one rule for every animal; SUMO brakes for both kinds |
| L-A8 | civilians keep their fear on `CivilianState`; only animals use the new `Fear` component — ONE fear SYSTEM, two storages for now | moving every civilian reader (HSM, crowd, stress, callouts) onto `Fear` is a large blast radius for an unattended run; filed as the follow-up. One system, one table, so they cannot drift |
| L-A9 | "approaching" = closing AND heading at it (its track passes within half the flight zone); a passing threat caps fear at UNEASY (0.3) | the handoff's "a busy road does not panic it for good": a car on the road beside the field closes too, but it is not coming for the herd. A real habituation curve (fear lowered by repeated harmless passes) would change it |
| L-A10 | the flight-zone threats are ground bodies (people and vehicles, `VehicleParams`); a low helicopter frightens through its SOUND class only if it is a weapon — not yet through its rotor | aircraft carry no `VehicleParams`; a rotor rule (a low aircraft inside a wider zone) is a small follow-up |
| L-A11 | descriptor numbers 189 / 190 (measured free on ui, behaviors, coordinator, backend 2026-11-16) | — |
| L-A5 | the CE-1208 sprint rail now means PEOPLE (`Pedestrian`), not every Kind-3 type | the same "life form = a man" assumption the review warned about, in a test |

## Gates
| gate | result |
|---|---|
| Hrot.Core (whole) | 374 / 374 (the class rail red-proved: no `LandAnimal` row ⇒ red) |
| toolkit: motion + kinematics + navigation + physics + animals + civilians + crowds (AB-2) | 925 / 925; rails red-proved: Bird dropped from IsAircraft's exclusions · the yaw cap lifted · the cow steered as a person ⇒ each red |
| Hrot.Traffic (whole) | 86 / 86 (the cow-on-the-road rail red-proved: no Quadruped arm ⇒ red) |
| toolkit: taxonomy + body zones + crowd + hostile acts + grenade rules + hidden throw | 74 / 74 (the taxonomy rail red-proved: the animal skip dropped from `FriendlyInBurst` ⇒ red) |

## Next step
AB-4: `AnimalHsm` (Graze / Wander / Rest → Alert → Flee → Recover), temperament-driven; herds as `HerdMember` + a herd entity; grazing / perching; night.
