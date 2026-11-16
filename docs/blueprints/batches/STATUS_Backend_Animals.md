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
| **AB-3** fear | ⏳ | — |
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
| L-A5 | the CE-1208 sprint rail now means PEOPLE (`Pedestrian`), not every Kind-3 type | the same "life form = a man" assumption the review warned about, in a test |

## Gates
| gate | result |
|---|---|
| Hrot.Core (whole) | 374 / 374 (the class rail red-proved: no `LandAnimal` row ⇒ red) |
| toolkit: motion + kinematics + navigation + physics + animals + civilians + crowds (AB-2) | 925 / 925; rails red-proved: Bird dropped from IsAircraft's exclusions · the yaw cap lifted · the cow steered as a person ⇒ each red |
| Hrot.Traffic (whole) | 86 / 86 (the cow-on-the-road rail red-proved: no Quadruped arm ⇒ red) |
| toolkit: taxonomy + body zones + crowd + hostile acts + grenade rules + hidden throw | 74 / 74 (the taxonomy rail red-proved: the animal skip dropped from `FriendlyInBurst` ⇒ red) |

## Next step
AB-3: ONE shared `Fear` component (id 500) written by the one fear system (`CivilianFearSystem`, generalised) with a per-species impulse table; the flight-zone proximity rule; continuous sounds through the flight zone only; `AnimalState` replicated.
