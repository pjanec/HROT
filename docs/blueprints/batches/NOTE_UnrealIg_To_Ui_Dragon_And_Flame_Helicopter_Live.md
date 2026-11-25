# Note: the dragon and the flame helicopter are drawn live — and one question on the dragon's height

| | |
|---|---|
| **From** | `unreal-ig`, 2026-11-25 |
| **For** | `ui` |
| **Answers** | [NOTE_Ui_To_UnrealIg_Dragon_And_Flame_Helicopter.md](NOTE_Ui_To_UnrealIg_Dragon_And_Flame_Helicopter.md) (`ac8d50df9`) |
| **Tracked as** | CE-12019 ([tracker](../Blueprint_Issues_Tracker.md)) |

Run with our usual recipe on HROT `ui` `ebfff3644`, gateway `40f0cf5` (T-079) packaged against it, the client's DPS code regenerated from the same commit.

| scenario | seen in Unreal |
|---|---|
| `dr-dragon-flight` | the dragon (DIS `1.2.0.20.99` → our dragon kit) lifts off, flies the route, lands on the meadow, takes off again; wings beat / glide from `VehicleLook.Airborne` |
| `dr-dragon-breath` | the jet (`FlameJet` 1000, mount 0, 50 m) from the MOUTH; Field Man 1 and 2 burn; the Pillion is seated (seat 1); the Dragon Hunter's jet on its flank; the dragon does not burn |
| `ah-flame-attack` | the Flame Helicopter (412, the AH-64's kit) breathes from under its nose; Squad Man 1 burns |

`VehicleLook.Airborne` also turns a helicopter's rotor live now (nothing told the client "flying" before). Thank you for T-079.

**Not drawn yet (our side):** the neck and head from `Articulation` (our dragon mesh is one piece), the legs tucked in flight, the riders astride (they use the seated clip).

## ❓ One question

In `dr-dragon-breath` the dragon "stands" at **z 3 m** and is drawn 3 m above the ground (our kit puts its feet at the entity's position). In
`dr-dragon-flight` it lands at **z 2 m**, and before take-off it is at z 3 there too. Is the dragon's `EntityState` position its body centre
rather than its feet, or does it hover when it stands? If it is a fixed offset, tell us the number and we draw the feet at position − offset.
