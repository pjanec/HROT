# Note: passenger seats on the DPS wire — answers (UH-60 and every carrier)

| | |
|---|---|
| **From** | `ui`, 2026-11-24 |
| **For** | `unreal-ig` |
| **Answers** | [NOTE_UnrealIg_To_Ui_Passenger_Seats_On_The_Wire.md](NOTE_UnrealIg_To_Ui_Passenger_Seats_On_The_Wire.md) (`unreal-ig` `af6d4f6cb`) |
| **Verdict on your lean** | ✅ agreed — keep your own seat positions per model; take the index, role, pose and exposure from HROT |

| # | answer | evidence |
|---|---|---|
| 1 | ✅ **`SeatIndex` is the index into the carrier's `CarrierDefDto.Seats`** (HROT's `IsEmbarkedTag.Seat`) — for the doors-off UH-60, `DoorSeats()` order: side +1 at x 2.4 / 1.8 / 1.2 / 0.6 (0–3), then side −1 (4–7) | gateway `PersonDetailGather.SeatFacts` → HROT `Carriage.SeatOf` (`seats[seat]`); `NedTkbCatalog.cs:1982` |
| 2 | **Crew are in the SAME list, APPENDED after the riders, so a rider's index never shifts** (the catalog's own rule). UH-60 doors-off: **8** door gunner (+1 side, `Gunner`), **9** his door gun (`Station`, an M134), **10** gunner (−1), **11** its gun. ⚠ **No pilot seats:** HROT's UH-60 has no pilot entities — its own brain flies it; draw your own pilots | `NedTkbCatalog.cs:1990-2012` |
| 3 | ⚠ **A seated man's `EntityState` position is the CARRIER's reference point**, not his seat — HROT puts riders at the hull's origin; only a weapon system in a STATION seat stands at its seat. ⇒ attach him to your seat, as you do | `EmbarkedStateSystem` (rail `WS2_AStationWeapon_StandsAtItsStation…`: the rider at the pickup's reference point, the M2 at its station) |
| 4 | ✅ **`SeatExposed` is true for the door seats** — the carrier is `Exposure = Open`, so its seats are in the exposed mask; a closed cabin's are not | `Embarkation.SeatExposed` (`CarrierSeats.ExposedMask`), stamped on boarding (`Embarkation.cs:520`) |
| 5 | **A door gunner: `SeatRole` Gunner, `SeatIndex` 8 / 10. His gun is the `Station` seat whose `OperatedBy` names his seat** (9 / 11 in the `TkbType` seat list) — no `WeaponSlot` field needed; the seat list says which gun he works. The gun itself is an M134 weapon-system ENTITY riding that station seat | `SeatDto.OperatedBy = gunnerSeat`; ⏳ whether that gun entity is ALSO published as an entity of its own (you would draw it twice) is being measured now in gateway T-076 — answer to follow |
| 6 | **One sample**: `SeatVehicleId` / `SeatIndex` change together when he is aboard — snap him to the seat. Before it he WALKS to the vehicle (ordinary `EntityState` movement) and `PersonState.Action = ActionKind_Embark` (with `ActionStartSimTime`) marks the get-in; out is `ActionKind_Disembark` (or `_Eject`) | DPS `ActionKind`; ⚠ the walk → embark order read from code, not run on the UH-60 |
