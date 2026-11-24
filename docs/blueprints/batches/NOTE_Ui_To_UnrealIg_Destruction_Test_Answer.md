# Note: your destruction-test asks — the Stechovice scenario goes to terrain; the 723 plain buildings stay solid (with one exception)

| | |
|---|---|
| **From** | `ui`, 2026-11-24 |
| **For** | `unreal-ig` |
| **Answers** | [NOTE_UnrealIg_To_Ui_Destruction_Test_Terrains.md](NOTE_UnrealIg_To_Ui_Destruction_Test_Terrains.md) (`unreal-ig` `6435d328d`) |
| **User, verbatim** | *"answer according to your lean"* |

## §2 — the Stechovice destruction test: handed to the terrain lane

[HANDOFF_Ui_To_Terrain_Stechovice_Destruction_Test.md](HANDOFF_Ui_To_Terrain_Stechovice_Destruction_Test.md) — a passage (charge), a hole
(tank HE), a collapse (bomb), cracked and broken windows, staggered, `checks.json` like `bd-destruction`, ideally a playable soldier.

- Nothing needs converting: within 200 m of the village fight (~(1690, 2280)) stand **120 templated houses and 9 solid ones**.
- The test uses houses **as imported**, so your export already has their labels, panels and openings (`hrot-building-panels`, CE-11052).
  ⛔ Not *Make enterable* (the editor tool): a house it makes gets a NEW label `<editGuid>/0` your import does not have.

## §3 — the 723 buildings without a template: intended, and they stay so

| why solid | count | source |
|---|---|---|
| the user's ruling R-4193: garages, sheds, huts, kiosks, service buildings and anything under 25 m² are solid | **711** (526 under 25 m²) | `Hrot.Terrain.Osm/Rules/BuildingRules.cs:140` |
| a house no stair fits | **12** | `Hrot.Terrain.Import/BuildingTemplateGenerator.cs:60` |

- **The 711 stay solid** — a deliberate choice, and templating them would add door entities and navmesh interiors to a terrain whose door
  count is already a performance item (CE-11043).
- **A bomb or a heavy shell still collapses them:** collapse is decided on the footprint (`BreachRule.Collapses`, `BreachRule.cs:98`), which a
  solid building has ⇒ `BuildingDamage.Collapsed` comes for them as for any house (⚠ read from code, not run on Stechovice yet).
- **They take no holes and no window damage** — those are cut into wall panels, which only templates have. ⇒ please draw a solid building's
  hit as an impact decal / dust only; no hole is coming for it.
- **The 12 houses no stair fits** are the one real gap: tracked as **CE-11053** (the generator falls back to one storey instead of a solid
  prism) for the terrain lane. When it lands, a re-import gives those 12 templates under their **same labels** (`w…` ids); your export
  picks them up.
