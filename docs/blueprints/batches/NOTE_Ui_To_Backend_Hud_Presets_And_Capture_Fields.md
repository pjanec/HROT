<!--STATUS
state: LIVE — ui → backend: three things the player HUD (H1/H2, built) needs from the scenario / game side
updated: 2026-11-03
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Player_HUD_And_Debrief.md — PH-8 amended (R-4115: all hud.* / game.* ON, a scenario PRESET switches the other profile's elements off), §6 as built H1/H2
  - ../../DESIGN_Node_Features.md — F-22 (the layers: type ▸ composition ▸ the scenario's preset (P-7) ▸ the node override)
  - ../../PLAN_Training_And_Gameplay_Engine.md — P-7 scenario feature presets ("proposed with Node Features", no owner)
  - ../../DESIGN_Scenario_Rules.md — §00.12 S6 G-1 (HoldArea capture-by-side), §00.13 S7
-->
# NOTE — ui → backend: **the player HUD is built; three things it needs from your side**

The training HUD, game bar, kill feed, streak and bounty marker are built on `ui` (`d3d1494c5`, `00accb512`; CE-4226…4230), live-checked on both showcases.

| # | ask | measured | ui's lean |
|---|---|---|---|
| ① | **P-7: a scenario's feature preset**, the scenario layer of F-22 | ⛔ not built: `scenario.json` has no feature field. Features switch only per process (`--feature id=off`) or via a cluster `FeatureOverrides` run setting. P-7 is *"proposed with Node Features"* with no owner; the scenario load path is yours | ⭐ `scenario.json` `"features": { "<id>": "on" \| "off" \| {config} }`, applied at load as F-22's scenario layer, below the node override. Then add to **`showcase-bridgehead`**: `game.tickets`, `game.capture-points`, `game.round-timer`, `game.score-popups`, `game.bounty-marker`, `hud.kill-feed` = off. Add to **`showcase-crossfire`**: `hud.objectives`, `hud.own-evaluation` = off. ⚠ Until then everything is ON; an element with no data draws nothing, so the screens are fine but not curated |
| ② | **capture-point fields on the status** (CE-4230) | `ObjectiveStatus` has no capture flag, no challenger and no area. The HUD infers a capture point as a `HoldArea` whose `Reason` contains *"owner"*, parses the challenger from the reason text, and reads the area from `BehaviorStartRecord`, which exists only on the Brain node, so a remote Player node gets no *Capturing B* strip | ⭐ on the HoldArea status: `Capture` (byte), `Challenger` (ForceId), and the area as a ref or centre + radius. ui will then drop the text parsing |
| ③ | **`deadlineSeconds` on Crossfire's End trigger** | it has none, so the round timer counts up there | ⭐ add it if the round has a fixed length |
| ④ | ❓ **`ObjectiveStatus.Owner` is Neutral on every rule** in both showcases, counters included | the rules view, summary and HUD fall back to the subject's side | intended? If Owner is meant to be the side a rule belongs to, the generator or the kind should set it (this may be behaviors' field — say if so and ui will ask them) |

_ui lane, 2026-11-03_
