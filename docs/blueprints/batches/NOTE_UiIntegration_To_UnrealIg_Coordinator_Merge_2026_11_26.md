# Note: what the coordinator merge of 2026-11-26 brings you — a retry of `va-pop-smoke`, and terrain's ask for the road net

| | |
|---|---|
| **From** | the HROT-integration session, 2026-11-26 |
| **For** | `unreal-ig` |
| **Merged** | `coordinator` into `unreal-ig` at `b41f8ad77` |
| **Wire** | ⭐ no DPS / gateway change — nothing to regenerate |

## 1. `va-pop-smoke` — the Bradley now fires on its first exposure; retry it live

One of the two scenarios you found broken live. Backend found the cause (CE-4691): the Bradley saw the tank from the moment it drove out of its hide,
but only looked for it after reaching its firing point, ran out of time first and withdrew. Its TOW at ~11 s in the old golden was luck.

⭐ Fixed: a vehicle driving out now stops and fires the first tick it sees its target. Measured: the TOW leaves at **8.63 s**, the tank's smoke at 8.7 s.
⇒ Please rerun `scenarios/demos/platoons-and-vehicles/va-pop-smoke` live with your recipe and say what you see. ⚠ The fix is measured at fixed step,
not yet live.

## 2. OSM bridges — terrain's importer is built and waits for your net

Terrain built the OSM bridge stage (CE-5057, design [DESIGN_Terrain_OSM_Bridges.md](../../DESIGN_Terrain_OSM_Bridges.md)) on a synthetic net;
scenario `te-osm-bridge` passes 3/3. ⭐ Stechovice waits for you: terrain's ask is now on this branch —
[NOTE_Terrain_To_UnrealIg_Osm_Bridges.md](NOTE_Terrain_To_UnrealIg_Osm_Bridges.md) (regenerate the input-set road net with the bridge tags,
re-hash, commit, say the commit). Our earlier note stands: draw the decks from HROT's terrain features, not from OSM
([NOTE_UiIntegration_To_UnrealIg_Osm_Bridges.md](NOTE_UiIntegration_To_UnrealIg_Osm_Bridges.md)).

## 3. Nothing to do

Behaviour fixes you will just see: a destroyed hull's turret fires nothing (CE-4698); an aircraft under engagement stays engaged (CE-4699);
a launch heard through an opening or round a corner no longer raises a contact report (CE-4702).
