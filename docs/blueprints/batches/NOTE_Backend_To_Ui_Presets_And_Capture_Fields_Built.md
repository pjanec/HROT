<!--STATUS
state: LIVE — backend → ui: ① the scenario feature preset and ② the capture point's facts are BUILT (user-approved); the names to read
updated: 2026-11-03
current-answer: the whole note
stale-below: nothing
related-designs:
  - NOTE_Ui_To_Backend_Hud_Presets_And_Capture_Fields.md — your asks ① ②
  - NOTE_Backend_To_Ui_Hud_Asks_Answered.md — ③ ④ answered there (③ → behaviors' ExpectedAt, NOTE_Backend_To_Behaviors_Trigger_ExpectedAt_From_Time.md)
  - ../../DESIGN_Node_Features.md — §11 P-7 (F-27…F-31) + as-built
  - ../../DESIGN_Scenario_Rules.md — §00.12 G-8 + as-built
-->
# NOTE — backend → ui: **presets and capture fields are built**

| | read |
|---|---|
| ① the preset | scenario header `"features": { "<id>": "on" \| "off" \| {partial config} }` — applied on every node at the load's COMMIT as the `scenario` layer (over the role default, under the node / cluster overrides); `GET /features` names its source `"scenario"`. ⭐ Your lists are in: `showcase-bridgehead` turns off `game.tickets` · `game.capture-points` · `game.round-timer` · `game.score-popups` · `game.bounty-marker` · `hud.kill-feed`; `showcase-crossfire` turns off `hud.objectives` · `hud.own-evaluation` (`c24b4aafe`) |
| ② the capture point (CE-4230) | on the HoldArea status — `GET /objectives` row: **`capture`** (bool) · **`challenger`** (`"Friend"` / `"Hostile"` / `"Neutral"` = none) · **`areaCenter`** `[x, y, z]` · **`areaRadius`** (the area's bounding circle); wire `EntityObjectiveStatus`: `Capture` · `Challenger` · `AreaX` · `AreaY` · `AreaZ` · `AreaRadius`. Display fields — they go at once. ⇒ ⭐ you can drop the `Reason` parsing and the `BehaviorStartRecord` read; a remote Player node now has everything |

⚠ Not cleared on Idle: a preset lives until the next load replaces it (a load with no `features` clears it).

_backend lane, 2026-11-03_
