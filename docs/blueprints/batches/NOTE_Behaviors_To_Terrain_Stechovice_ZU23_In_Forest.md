<!--STATUS
state: LIVE — a NOTE (protocol §5), one ask · updated: 2026-11-26 · current-answer: the whole file
related-designs:
  - NOTE_Terrain_To_Behaviors_UI_Stechovice_Battle_Reds.md — the note this answers (CE-5062)
  - ../../designs/air-attack/DESIGN_Air_Operations.md — owns EngageAircraft
-->
# NOTE — `behaviors` → `terrain`: CE-5062, the ZU-23 never fires because it stands INSIDE a forest

**Measured** (`stechovice-battle` at `3d5138bd1`, headless editor, `/perception/los` from `Red ZU-23` (1758, 2404) to `Blue Apache`):

| t (s) | the Apache | the line |
|---|---|---|
| 12–14 | 410–550 m away, ~55 m above the gun | every body point **blocked by terrain: woods, `along: 0`, material `forest`, transmittance 0.0001–0.0004** (threshold 0.5) |

⇒ the gun stands IN the wood: its sight never sees an aircraft. Its memory holds the aircraft only as ANONYMOUS (heard) contacts — up to 11
of them — so `EngageAircraft` starts on the `AirThreat` edge but has no seen aircraft to aim at (`AirSense.Newest` → null, `seen` false every
sample) and never fires. The behaviour is right: an air-defence gun fires at what it SEES; doctrine sites one in the open with a clear sky arc.

**Ask:** move `Red ZU-23` (TKB 110) to open ground — a clearing or the hill's bare top — with a clear sky arc toward the aircraft's routes. The
same check for `Red Radar SAM` costs one `/perception/los` call (it fires by radar, so it may not need it).

Not changed by behaviors: the scenario (yours) and the terrain data.
