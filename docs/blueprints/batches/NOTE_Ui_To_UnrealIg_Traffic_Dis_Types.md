# Note: every traffic kind now has its own DIS type (your §1 ask)

| | |
|---|---|
| **From** | `ui`, 2026-11-23 |
| **For** | `unreal-ig`; FYI the traffic (SUMO) session |
| **Answers** | [NOTE_UnrealIg_To_Ui_Models_And_Freezes.md](NOTE_UnrealIg_To_Ui_Models_And_Freezes.md) §1 (`unreal-ig` `40289d7b1`) |
| **User, verbatim** | *"yes go ahead"* (R-11020, CE-11042) |

| TKB type | kind | DIS (kind.domain.country.category.subcategory) | SISO-REF-010 name |
|---|---|---|---|
| 1002 | CivilianCar | 1.1.0.81.10 | Car › Generic |
| 1054 | Sedan | 1.1.0.81.43 | Car › 4-Door Sedan, Intermediate/Standard |
| 1055 | Estate | 1.1.0.81.63 | Car › Wagon, Intermediate/Standard |
| 1050 | Hatchback | 1.1.0.81.52 | Car › 5-Door Hatchback, Economy/Compact |
| 1056 | Suv | 1.1.0.81.113 | Car › SUV, Intermediate/Standard |
| 1051 | DeliveryVan | 1.1.0.83.74 | Single Unit Cargo Truck › Van, Delivery |
| 1053 | BoxTruck | 1.1.0.83.101 | Single Unit Cargo Truck › Delivery Truck, Box |
| 1052 | CityBus | 1.1.0.82.1 | Bus › Commuter (Flat Nose) |

Values from open-dis7's enumerations (generated from SISO-REF-010). Specific and extra stay 0. HROT's own 3-D map draws them as before
(new classes `Bus` 1.1.*.82 and `CargoTruck` 1.1.*.83 keep the wheeled-car look); the Add Entity picker groups them under Car / Bus /
Cargo Truck. A SUMO car HROT takes over keeps its TKB type, so it carries the same DIS type. Your §2 (freezes) and §3 (tilt): noted, nothing
for HROT.
