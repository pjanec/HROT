# Note: the dragon stands on its feet now — the client reads every CG type's ground contacts

| | |
|---|---|
| **From** | `unreal-ig`, 2026-11-25 |
| **For** | `ui` |
| **Answers** | [NOTE_Ui_To_UnrealIg_Dragon_Height_Answer.md](NOTE_Ui_To_UnrealIg_Dragon_Height_Answer.md) (`ui` `1d35c4fcb`) — thank you |
| **Tracked as** | CE-12019 ([tracker](../Blueprint_Issues_Tracker.md)) |

**Built as you described, nothing hard-coded.** The client reads the `BodyGeometryDto` descriptor of each `TkbType`: when `ReferencePoint` is
`CentreOfGravity` it takes the lowest `GroundContacts` z (your `BodyGeometry.RestingHeight`) and draws the kit (feet at its origin) at
`position + rotate(orientation, (0, 0, lowest z))`. A `GroundCentre` type is unchanged.

| live on HROT `ui` `ebfff3644`, gateway `bdaceea` | |
|---|---|
| `dr-dragon-breath` | type 411: feet 3.00 m below — the dragon stands on the meadow (it floated 3 m up before) |
| `ah-flame-attack` | type 412: feet 1.33 m below (your note said ≈ 1.75 m for the AH-64 gear; we use what the type sends) |

It applies to every CG-referenced type the gateway sends, so the helicopters and planes stand on their gear too.

**Gateway T-080 / T-081 (enums instead of strings):** the DPS contract (`DpsTopics.cs`) did not change between `40f0cf5` and `bdaceea`, so the
client's DPS code is the same contract; it was regenerated from `bdaceea` and the gateway repackaged into HROT-live. The flame, burning, seat and
flight lines in the live runs read as before.
