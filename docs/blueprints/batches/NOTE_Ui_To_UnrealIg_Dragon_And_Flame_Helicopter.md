# Note: the dragon and the flame helicopter — how to recognise them on DPS and what to draw

| | |
|---|---|
| **From** | `ui`, 2026-11-25 |
| **For** | `unreal-ig` |
| **User, verbatim** | *"do we support flamethrower helicopter/dragon in the gateway? pls write unreal how to identify it and to implement it"* |
| **HROT's own look (the reference)** | [DESIGN_Dragon.md](../../designs/fun/DESIGN_Dragon.md) §7 (as built) and §8 (the look; mockup [`docs/UX/dragon-look.svg`](../../UX/dragon-look.svg)) · [DESIGN_Breath_Attack.md](../../designs/air-attack/DESIGN_Breath_Attack.md) §7 (the flame helicopter) |
| **Scenarios to test on** | `scenarios/fun/dragons/dr-dragon-flight` (lift-off, landing on its legs, take-off) · `dr-dragon-breath` (the breath, riders) · `dr-dragon-attack` · `dr-dragon-village` · `dr-dragon-holds` |

## 1. How to recognise them

| | the DRAGON | the FLAME HELICOPTER |
|---|---|---|
| `Entity.TypeId` | **411** | **412** |
| `Entity.Dis` | **1.2.0.20.99** — key the look on this DIS class (HROT's own rule keys the dragon family by it) | **1.2.225.20.0.1** — the AH-64's, specific 1 |
| `TkbType` (JSON) | a 20 × 26 × 6 m body, colour `#7A1F1F` (dark red), rotorcraft locomotion; mount 0 category **`Flamethrower`** on turret 0 (the neck) | the AH-64's body; its chin-turret mount category **`Flamethrower`** in place of the M134; the Hydra-70 pod kept |

⭐ Generic rule for both: a mount whose category is `Flamethrower` is a flame projector — the same rule as the flamethrower soldier (fire answer note).

## 2. What arrives on DPS — all already on the wire

| what | topic | for the dragon | for the flame helicopter |
|---|---|---|---|
| where it is | `EntityState` | position, orientation, velocities | same |
| the neck / the chin turret | `Articulation` | turret 0 = the NECK (azimuth ±70°), its gun = the HEAD + jaw (elevation −60…+15°) — `Azimuth` / `Elevation` | the chin turret, as on the AH-64 |
| the breath / the flame | `FlameJet` (key `EntityId` + `Mount`) | mount 0: aim point, range (50 m), half angle (9°), intensity, burst start, burn seconds — the jet leaves the MOUTH (1.5 m past the head) | the chin projector's jet, 60 m |
| what burns afterwards | `FireSpots`, `GroundMarks`, `PersonState.Burning`, `BuildingDamage.FireSince` | as any fire | same |
| the riders | `PersonState`: `SeatVehicleId` = the dragon, `SeatIndex` 0 (the rider, a `Driver` seat) or 1 (the pillion, a firing `Passenger` seat), **`SeatPose = SeatPoseRide`** — draw them MOUNTED (astride, as on a horse) | crew as on the AH-64 |
| damage, death | `EntityState.Damage`, `VehicleLook` | the dragon is fireproof (it never burns); dead = the body as it fell, wings flat, legs folded, no wreck colour | as the AH-64 |
| flying or landed | ✅ **`VehicleLook.Airborne`** — on the wire since gateway `40f0cf5` (T-079, HROT's `Airborne.Of`; regenerate from the IDL, one line added) | same |

## 3. Drawing the dragon (HROT's numbers — DESIGN_Dragon §8.2)

| | |
|---|---|
| wings | BEAT ±33° at **1 Hz** while climbing (> 1 m/s) or slow (< 10 m/s) in the air; GLIDE at +6° at speed; FOLDED on the ground; blend 1 s between them; phase on SIM time (a paused sim stops the beat) |
| the bob | the body heaves up and down with the beat, and the riders and a rider's camera move with it — the motion itself keeps a constant altitude (the bob is the look only) |
| legs | planted on the ground; tucked back 70° in flight |
| neck and head | from `Articulation`: neck = turret azimuth, head = its elevation; the jet leaves the mouth |
| sounds (yours) | a wing WHOOSH at each down-stroke while flying (HROT: 400 m); a ROAR on a breath start, take-off, a health drop, now and then in flight, a dying roar (HROT's moving sound class `Roar`, heard 1.5 km) |

The flame helicopter is the AH-64 model with a flame nozzle in place of the chin gun; its jet comes from `FlameJet` like the flame tank's.
