<!--STATUS
state: LIVE — backend → behaviors: a NOTE (the backend half of the gas reactions frame is built).
updated: 2026-10-17
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Civilians_And_Crowd_Control.md — §7.3d the expected behaviours, §7.3d-B/C the backend build (THIS note's basis)
  - docs/blueprints/batches/HANDOFF_Backend_To_Behaviors_Gas_Reactions.md — the frame this answers (not amended)
-->

# NOTE — backend → behaviors: gas is a danger area now *(CE-3227, at `backend@fd1cac3da`)*

The frame [HANDOFF_Backend_To_Behaviors_Gas_Reactions](HANDOFF_Backend_To_Behaviors_Gas_Reactions.md) promised it; it is built
([§7.3d-C](../../DESIGN_Civilians_And_Crowd_Control.md)).

| what you do | what you get |
|---|---|
| ask for it: `DangerAreaSettings.Kinds |= DangerAreaKinds.GasCloud` (a sensor that asks for nothing still gets only the v1 crossings) | `DangerAreaKind.GasCloud` areas in the sensor's `DangerAreaCognitiveBuffer`, in route order with the others |
| read the area | `NearSideHandle` = 4 m short of where the route enters the gas · `FarSideHandle` = 4 m past where it leaves — the detour points; the footprint already includes where the wind carries the cloud in 15 s (+ 2 m) |
| react | it is rated **1** ⇒ `SensorChange.AreaThreatened` fires on it as the next area ahead, whoever sees it; `FeatureId` is the cloud's, stable while it drifts |
| ⚠ a man already INSIDE | the area starts at distance 0 — but your "escape blind" reads `EffectState` Irritated, not this |
| ⚠ a map with no road graph | gas still answers (crossings do not) |

⚠ Not railed end to end on the backend (SimHost solve over a live cloud → Brain): your reaction rails will be the first to; tell me what breaks.

**Ids allocated:** none.
