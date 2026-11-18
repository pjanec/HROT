<!--STATUS
state: LIVE — FYI for ui (a cross-lane edit in Hrot.Look) + the drawing that is yours
updated: 2026-11-18
current-answer: the whole file (a short note)
related-designs:
  - ../../designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md — §6a / §6b as built (head out, the light switch, the readers)
-->
# NOTE backend → ui: the opening and light READERS exist; drawing them is yours (`2026-11-18`, CE-4670)

**Cross-lane edit, in your library** (`Hrot.Look`, ui's LK-S1) — the design names these readers in `VehicleLook` (VA-2):

| file | change |
|---|---|
| `Hrot/Engine/Hrot.Look/Vehicles/VehicleLook.cs` | ⭐ `OpeningProgress(world, v, id, now)` — by the opening's TKB **id** (map your model by id / socket), delegating to `VehicleOpenings.Progress`. ⭐ `Lights(world, v, now, BrakeLights?)` — the effective light mask (bit i = the type's light group i, `VehicleLightsCatalog` order): the stored `OnMask` + the `Brake` groups while braking (Normal discipline only) |
| `Hrot/Engine/Hrot.Look/Vehicles/BrakeLights.cs` *(new)* | ⚠ brake lights cannot be read from one sample (the wire carries velocity, not acceleration) ⇒ **each renderer owns ONE `BrakeLights`** and passes it every frame. 0 B steady |
| `Hrot/Engine/Hrot.Look.Tests/VehicleLookTests.cs` | 4 rails (progress by id; brakes from deceleration; on a replica; 0 B) |

**Yours, not built (design §6a / §6b ⏳):**

| | |
|---|---|
| our 3D map draws the openings | `OpeningProgress` per opening → the socket's pose; a hatch with `OpeningSlot.CrewExposed` has a crewman's head and shoulders out (new today) |
| our 3D map draws the lights | `Lights(…, _brakes)` with one `BrakeLights` field on the layer; the group's `Kind` / `Socket` say what and where |
| the commander's eye (CE-1200) | `EntityBodyLayer3D` places it "his head out of the hatch" always; `CrewExposed` now says when he really is |
| a station's switches UI | call `Fdp.Toolkit.Behavior.VehicleRequests.SwitchLights(world, vehicle, discipline)` — the transport carries it to the lights owner (interaction kind 16) |
| a possessed crewman's "head out" | `VehicleRequests.HeadOut(world, vehicle, man, out)` (kind 15) |

Test beds: `uc-convoy-hit` (an APC's ramp opens and closes), `va-lights-night` (a lit T-72 and a blacked-out one).
