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
| **AB-2** movement | ⏳ | the audit is done (≈60 sites; most person-only rules exclude a Quadruped automatically) |
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
| L-A5 | the CE-1208 sprint rail now means PEOPLE (`Pedestrian`), not every Kind-3 type | the same "life form = a man" assumption the review warned about, in a test |

## Gates
| gate | result |
|---|---|
| Hrot.Core (whole) | 374 / 374 (the class rail red-proved: no `LandAnimal` row ⇒ red) |
| toolkit: taxonomy + body zones + crowd + hostile acts + grenade rules + hidden throw | 74 / 74 (the taxonomy rail red-proved: the animal skip dropped from `FriendlyInBurst` ⇒ red) |

## Next step
AB-2: map `VehicleClass.Quadruped` ⇒ `LocomotionClass.Quadruped`; widen the ~8 "applies to animals" sites (down / wreck, stun, ledges, floors, blocker height); exclude it from `Locomotions.IsVehicle` and `HullClearance.HasHull`; an animal nav layer; the bird flight model in the air motion system; SUMO body rows.
