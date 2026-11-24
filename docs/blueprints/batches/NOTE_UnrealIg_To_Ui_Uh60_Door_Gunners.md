# Note: the UH-60 with door miniguns and gunners — what Unreal needs from HROT

| | |
|---|---|
| **From** | `unreal-ig`, 2026-11-24 |
| **For** | `ui` (HROT); FYI the gateway |
| **User, verbatim** | *"the helicopter which transports the guys sitting in the side opening. I think HROT is using UH-60 … I needed to have a minigun on both sides with seats for gunners, meaning that it could be operated by either player or by game character. And I need the guys to be shown sitting in the side opening and or behind the minigun on the sides … when the miniguns are firing, I need to see the tracer stream. Similar to what HROT 3D map is showing … the stream should originate from the minigun on the side where the weapon mount is."* |
| **Asks** | §2 |

## 1. What HROT has today (measured, `ui` `a85340796`)

`Heli_UH60_DoorsOff` (`NedTkbCatalog.cs:1349`): eight `SitLegsOut` door seats (`DoorSeats()`, x 0.6–2.4 m, y ±1.05 m, z −0.85 m), flight
(Air Ops slice 2), health, a brain — and **no door guns**: its only mount is the flare / chaff dispenser (`:1367`).

## 2. Asks

1. **Two door miniguns** (M134, one per cabin door — the real UH-60 gunner windows sit just ahead of the cabin doors), each a weapon mount
   with its pivot position and arc, and **a gunner seat behind each** (a `SeatRole` gunner, `SeatPose` seated). Operable by a **player**
   (possession of the gunner, the existing drive / use hand-over) **or the AI** (the gunner's brain engages).
2. **`Fire` per mount with its own `Origin`** at that gun's muzzle and its `WeaponSlot` — so the client's tracer stream starts at the side
   that fired (the client already draws streams from `Fire.Origin`; HROT's 3-D map look is the target).
3. Passengers: the eight door seats stay (legs out) — the client will draw them seated. Tell us if seats move to make room for the guns.

The Unreal side (a UH-60 model with doors off, its two guns, seated men, the rotor turning) starts once the mounts and seats exist; the
model itself is ours to find.
