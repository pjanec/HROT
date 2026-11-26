# Note: bridges laid and recovered during play — on DPS now (`Deck`, `VehicleLook.BridgeLayer`)

| | |
|---|---|
| **From** | the HROT-integration session, 2026-11-26 |
| **For** | `unreal-ig` |
| **Gateway** | `ccr-458b1360-afzl8f` `8d1b5e8` (T-083, [DD-035](https://github.com/pjanec/SimNetGateway/blob/ccr-458b1360-afzl8f/docs/design/DD-035-Dps-Bridges.md)), against HROT `unreal-ig` `76c4210b9` — regenerate from the IDL (+42 lines, none removed); the client guide's new "Bridges" section |
| **HROT's design** | [DESIGN_Bridge_Laying.md](../../designs/engineering/DESIGN_Bridge_Laying.md) §11 (the deck) / §12f (the engineer) |
| **Scenarios** | HROT `scenarios/demos/engineering/`: `bl-placed-bridge` (a bridge already laid, vehicles cross), `bl-assault-crossing` (a Wolverine lays, the column crosses), `bl-recover` (laid, crossed, recovered), `bl-blow-the-bridge` (a charge drops the span) |

## 1. What to recognise

| | type (`Entity.TypeId`) | what it is |
|---|---|---|
| the layer vehicles | **118** M104 Wolverine (tracked, DIS 1.1.*.9.3) · **119** Improved Ribbon Bridge transporter (wheeled, 1.1.*.7.1) | they carry the span and launch / take it back |
| a laid span | **8825** assault bridge · **8826** floating bridge (DIS kind 5, a cultural feature) | an entity — ⛔ do not draw it as a box: draw it from its `Deck` |

## 2. What arrives

| topic | what | how to draw |
|---|---|---|
| **`Deck`** (key `EntityId` = the span's entity) | `Kind` (Assault / Floating / Unknown), `Length` (between the deck ends), `Width`, `Height` (the deck above the bank), and `Parts`: the DECK slab and two RAMPS, each four corners | ⭐ corners: x, y in the sim frame, **z = the height ABOVE THE GROUND under that corner** — add YOUR terrain height there (HROT draws it the same way, so your deck is exactly where HROT's units drive). Ramps run onto the bank at 1 : 4 |
| `Deck` disposed | the span is gone | if the span's `EntityState.Damage` went to destroyed just before ⇒ it FELL (a charge, fire): play the collapse / splash; otherwise it was RECOVERED: it goes back onto the vehicle |
| **`VehicleLook.BridgeLayer`** (on 118 / 119) | `NoSpan` · `Carried` · `Laying` · `Recovering` | `Carried` = the span on the vehicle; `Laying` = the launch (the Wolverine's span over its front onto the far bank; the ribbon bays leaving the truck); `Recovering` = the reverse; `NoSpan` = empty. ⚠ No progress value (HROT has none in v1): play the animation over the time it shows the state |

Vehicles crossing need nothing new: their `EntityState` heights already follow the deck.
