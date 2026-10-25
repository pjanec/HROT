<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-10-25
current-answer: the whole file
stale-below: nothing
related-designs:
  - ../Architect_Question_106_Sound_Paths.md — ✅ R-3050: L ③ (this) — the route drawn on the hearing layer; §7 the as-built solver
  - ../../DESIGN_Terrain_Combat_Tuning.md — §5a OWNS the hearing debug layer (HearingGizmo, CE-3117)
  - ../../DESIGN_Thermal_And_Acoustic_Sensing.md — OWNS hearing
-->

# HANDOFF — ui: draw where a sound went (AQ106 L ③)

**For:** the ui lane. **From:** backend. **Dispatched at `0bdf2f532`** (the solver; the route lands in the next backend commit). Scope FROZEN at
that sha — later documents are FYI. 🔒 User, `2026-10-25`: *"how could we debug that sounds 'travel' properly? (what sounds could be heard and what
could not by an entity)"* → L approved (*"K and L approved, go ahead"*).

| # | item | the data (backend, built) | acceptance |
|---|---|---|---|
| 1 | ⭐ on the hearing layer (`HearingGizmo`, your file), for the SELECTED listener: each sound near him drawn along its chosen ROUTE — a polyline through the doorways, windows, stairs or street corners — **green heard, red not heard**, labelled with its verdict and dB; the point it seems to come from (`heardFrom`) marked | `Fdp.Toolkit.Perception.Sensors.HearingExplain.TryExplain(view, unit, seconds, list, out why)` — each `HeardSoundExplanation` has `Waypoints`, `Verdict` (`Reason`, `Distance`, `EffectiveCarry`, `Route.Via/HeardFrom/StraightDb/PathDb/Corners`); the same over HTTP `GET /perception/hearing?listener=&seconds=` (MCP `explain_hearing`) | a step behind the open hall door of `house-2f` draws green through the doorway; the same with the door shut draws red, straight through the wall |
| 2 | ⚠ it runs where hearing runs (the Perception node — SimHost, the editor); on a node without it `TryExplain` returns false with the reason | — | the layer says "no hearing on this node" rather than drawing nothing |

Report: the as-built in Terrain Combat Tuning §5a, the frame rail and its red-proof, the sha.
