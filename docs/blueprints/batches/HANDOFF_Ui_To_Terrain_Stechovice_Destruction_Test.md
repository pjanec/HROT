# Handoff: ui → terrain — a Stechovice destruction test scenario (for the Unreal client)

| | |
|---|---|
| **From** | `ui`, 2026-11-24 |
| **For** | the `terrain` lane |
| **Dispatched at** | `ui` (see the commit that adds this file) |
| **Asked by** | `unreal-ig`: [NOTE_UnrealIg_To_Ui_Destruction_Test_Terrains.md](NOTE_UnrealIg_To_Ui_Destruction_Test_Terrains.md) §2 (`unreal-ig` `6435d328d`) |
| **User, verbatim** (relayed by `unreal-ig`) | *"Pls ask HROT to make some selected stechovice building enterable & destructible so we can test that later in stechovice as well."* · scenario work is yours: *"i would like you move the scenario work to terrain lane too"* (`2026-11-23`) |
| **Ids** | none allocated here — yours |
| **Not urgent** | Unreal starts on `bt-range` + `bd-destruction`; this is for "later, when the basics are proven" |

## 1. What is wanted

A scenario like [`scenarios/demos/buildings-and-doors/bd-destruction`](../../../scenarios/demos/buildings-and-doors/bd-destruction/) on a few
Stechovice houses in the village fight of `stechovice-battle` (the Red squads stand around scenario (1690, 2280)):

| act | damage it must show on the wire (`BuildingDamage` via the gateway) |
|---|---|
| a demolition charge on a wall | a **passage** (a breach down to the floor) |
| a tank HE round at a wall | a **hole** |
| a bomb or a heavy shell | a **collapse** |
| rifle fire through windows, then a blast nearby | **cracked** and **broken** windows (`WindowDamage` hits 1–2 and 3) |

Staggered so each act is seen alone; `checks.json` expect rows in `bd-destruction`'s style (it approximates the passage by a position only
reachable through it, the collapse by a death inside); ideally a **playable** soldier who can walk in through the passage.

## 2. What was measured (`ui`, on the imported `assets/terrain/Stechovice`)

| claim | evidence |
|---|---|
| nothing needs converting: the village houses are templates (enterable, with panels and openings) | within 200 m of (1690, 2280): **120 templated, 9 solid** (`Stechovice.world.geojson`; nearest templated `w422408216` at 2 m, `w472360677` 20 m, `w422409017` 33 m) |
| a hole, a passage and a broken window need a template; a collapse does not | holes are cut into wall PANELS, which only templates have; a collapse is decided on the footprint (`BreachRule.Collapses`, `BreachRule.cs:98`), which solid prisms have too (`TerrainWorldParser.cs:123`) — ⚠ read from code, not run |
| the charge, the HE round and the bomb already work on templates | `bd-destruction` on `bt-range` (CE-4391 S-5) |

## 3. Notes

- ⚠ No golden for `bd-destruction` (its perception hashes do not repeat, its `checks.json` `$doc`) — expect the same here; the expect rows
  are the verdict.
- The Unreal side keys damage by building label + panel + opening (`hrot-building-panels`, CE-11052) — so pick houses that are in the
  terrain as imported; ⛔ do not use *Make enterable* on a solid house for this test: the made house gets a NEW label (`<editGuid>/0`,
  `DESIGN_Terrain_Base_Edits.md` D-6) that Unreal's import does not have.
- Unreal's §3 question (the 723 untemplated buildings) is with the user, not part of this handoff.
