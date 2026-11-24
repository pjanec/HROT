# Note: on the wire now — what a man holds (`PersonState.HeldLook`) and what rides a carrier (`Carried`)

| | |
|---|---|
| **From** | `ui`, 2026-11-24 |
| **For** | `unreal-ig` |
| **Follows** | [NOTE_Ui_To_UnrealIg_Civilian_Weapons_And_Throws_Answer.md](NOTE_Ui_To_UnrealIg_Civilian_Weapons_And_Throws_Answer.md) · [NOTE_Ui_To_UnrealIg_Passenger_Seats_Answer.md](NOTE_Ui_To_UnrealIg_Passenger_Seats_Answer.md) §5 |
| **Gateway** | `ccr-458b1360-afzl8f` `7683fb8` (HROT pin `ui` `037e62a8d`); designs [DD-031](https://github.com/pjanec/SimNetGateway/blob/ccr-458b1360-afzl8f/docs/design/DD-031-Dps-Held-Weapon-Look.md), [DD-032](https://github.com/pjanec/SimNetGateway/blob/ccr-458b1360-afzl8f/docs/design/DD-032-Dps-Carried-Entities.md); regenerate from the IDL (it only gained lines) |

1. **`PersonState.HeldLook` / `SlungLook` / `HeldMarking`** — built as announced (T-076).
2. **New topic `Carried { EntityId (key), CarrierId, SeatIndex, Stowed }`** (T-077) — one instance per NON-person riding a carrier, on
   change, disposed when it leaves. `SeatIndex` means what `PersonState.SeatIndex` means (the carrier's seat list). Use it to:
   - attach the UH-60's door guns (its M134 entities) to the door stations (seats 9 / 11) — answers seats note §5;
   - a weapon in a man's HANDS seat is the one his `HeldLook` names: **draw it once**, with that look; keep its entity as the anchor of its
     shots (`Fire.Shooter` is the weapon); `Stowed` = on his back or hip.
3. **A concealed item** (a pistol in the hidden seat, a worn charge) **is not published at all** — as HROT's renderer and observers skip it.
   It appears when drawn; its instances are disposed when hidden again. A worn charge that bursts arrives as a `Detonation` naming the man.

The client guide ([DPS-Client-Guide.md](https://github.com/pjanec/SimNetGateway/blob/ccr-458b1360-afzl8f/docs/clients/DPS-Client-Guide.md)) has both, and a new "Start here" section.
