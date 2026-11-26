<!--STATUS
state: LIVE — a NOTE (protocol §5), FYI + two asks · updated: 2026-11-26 · current-answer: the whole file
related-designs:
  - STATUS_Terrain_Stechovice_Showcase.md — §4 (CE-4694, the bridge and the bridgehead) — the run these come from
-->
# NOTE — `terrain` → `behaviors`, `ui`: what keeps `stechovice-battle` red now that its bridgehead passes

⭐ The scenario's regression (`scripts/scenario-regression.sh stechovice-battle`, 180 s): **18 of 23 rows pass** — every bridgehead row (CE-4694) and the
riverbank and air flyovers. The 5 reds are behaviour, measured live (headless editor, the same scenario):

| row | measured | owner |
|---|---|---|
| Air defence: the ZU-23 fires at an aircraft | **CE-5062** — `EngageAircraft` runs three times (7 s, 64 s, 118 s) and never fires a round; the Apache passed 181 m from it | ⭐ **behaviors** |
| Air defence: the SAM fires at the Apache | the SAM fires 2–4 rounds per 30 s sample; WHOM it fires at was not read (the run crashed, CE-5061) | behaviors (row may be the A-10) |
| Village: Blue enters the village (120 m) · a Blue man falls · the Red defenders fire | the Blue squads are still 390–460 m out at 180 s; Red fired from ~150 s in one run, not by 180 s in the regression's | ⭐ **behaviors — CE-5051** (the village attack stays in its support phase) |
| — the editor ABORTS on a second preview | **CE-5061** — `NetworkId 5633 already registered` from `LightweightRoundIdentitySystem` (`:68`) after `/preview/exit` + `/preview/enter` + `run-to` | ⭐ **ui** (CE-11050) |

⭐ The asks: behaviors — CE-5062 and CE-5051; ui — CE-5061. Rows in the [tracker](../Blueprint_Issues_Tracker.md).
