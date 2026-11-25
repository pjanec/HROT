# Note: passenger seats on the DPS wire — what `SeatIndex` means for a carrier like the UH-60 (questions)

| | |
|---|---|
| **From** | `unreal-ig`, 2026-11-24 |
| **For** | `ui` (HROT) and the gateway |
| **User, verbatim** | *"Ask hrot if not clear about passenger seats in the dps protocol"* |
| **Context** | Unreal now draws men seated in a UH-60's doors ([NOTE_UnrealIg_To_Ui_Uh60_Door_Gunners.md](NOTE_UnrealIg_To_Ui_Uh60_Door_Gunners.md); the kit `DA_Air_UH60`, `e4553ee08`): a man whose `PersonState.SeatVehicleId` is the helicopter sits on the kit's seat `SeatIndex`, with the seated clip |

## What the contract says (gateway DD-018 §Riders, `DpsTopics.idl` `PersonState`)

`SeatVehicleId`, `SeatRole` (None, Driver, Gunner, Commander, Passenger, Station), `SeatIndex` (255 = not seated), `SeatExposed`, `SeatPose`
(Sit, SitLegsOut, …); *"the seat's pose and position come from the carrier's type data (`TkbType`: HROT's carrier seats)"*.

## Questions

1. **Is `SeatIndex` the index into the carrier's `CarrierDefDto.Seats` list** — for `Heli_UH60_DoorsOff` the `DoorSeats()` order (side +1
   x 2.4 / 1.8 / 1.2 / 0.6, then side −1)? Unreal's kit lists its seats in exactly that order.
2. **The crew** (pilots; later the door gunners): do they have a `SeatIndex` in the SAME list (shifting the passengers), their own list, or
   255 with only a `SeatRole`? Lean: crew roles never shift the passengers' indices.
3. **Where is a seated man's `EntityState` position** — on his seat (HROT's seat offset applied), or the carrier's? Unreal attaches him to
   its own seat, so either works; we want to know which to expect (a seat drawn ~0.3 m from HROT's, the model being narrower, is ours).
4. **`SeatExposed`** is true for the door seats (Gen.Carrier OPEN)? Unreal draws an exposed seat and hides a closed one.
5. **A door gunner** (after the mounts in the UH-60 note): `SeatRole` Gunner + which mount he works — a `WeaponSlot` on `PersonState`, or
   the mount index = the gunner seat's order? Lean: the gunner seat names its mount (the `Fire.WeaponSlot` the gun's shots carry).
6. **Getting in / out**: does a man change from not seated (255) to a seat index in one sample (the client snaps him to the seat), or is there
   a boarding phase we should draw (walking to the door)?

Lean overall: the client keeps its OWN seat positions per model (it adapts them to the drawn cabin) and takes from HROT only the index,
role, pose and exposure — no seat geometry needed on the wire beyond what `TkbType` already carries.
