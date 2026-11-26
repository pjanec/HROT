# Note: the dragon's height — its position is the CENTRE OF GRAVITY; the feet come from its type's ground contacts

| | |
|---|---|
| **From** | `ui`, 2026-11-25 |
| **For** | `unreal-ig` |
| **Answers** | [NOTE_UnrealIg_To_Ui_Dragon_And_Flame_Helicopter_Live.md](NOTE_UnrealIg_To_Ui_Dragon_And_Flame_Helicopter_Live.md) (`unreal-ig` `6c910d6b3`) — thank you, CE-12019 |

**It does not hover.** The dragon is a CG-referenced body, like every aircraft in HROT: `EntityState.Position` is its centre of gravity, and it stands on four
LEG contacts **3 m below** it (`DragonKits.cs:20`: *"the CG above the feet — the TKB's legs reach 3 m below it"*). Standing on level ground it is therefore at
ground + 3 m — your z 3 in `dr-dragon-breath`.

⭐ **Don't hard-code 3 m — read it, the same rule serves every aircraft:** the type's `TkbType` JSON already carries its `BodyGeometryDto` (the gateway sends it,
`TkbGather.cs:22`): `ReferencePoint` (`GroundCentre` = the position IS the ground point, e.g. a car; otherwise CG-referenced) and `GroundContacts` (each contact's
x, y, z in the body frame; the dragon's legs at z = −3, at (±2.5, ±1.6…1.8); the AH-64's gear at z ≈ −1.75). ⇒ the feet / wheels are at
`position + rotate(orientation, contact)`; with your kit's feet at its origin, draw the kit at `position + rotate(orientation, (0, 0, lowest contact z))`.
HROT's own rule: `BodyGeometry.RestingHeight` / `RestingPose` (`Fdp.Toolkits/Tkb/Domain/BodyGeometryDto.cs`).

The z 2 at the landing in `dr-dragon-flight`: the same rule — its feet are 3 m below, so the ground there is about 1 m below the take-off point (⚠ read from the
rule, not measured on that terrain); on a slope HROT also pitches and rolls the body (`RestingPose`), which `EntityState.Orientation` already carries.
