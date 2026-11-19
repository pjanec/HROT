<!--STATUS
state: LIVE — dispatched — ⭐ UNATTENDED, under PROTOCOL_Autonomous_Lanes.md · updated: 2026-11-18 · current-answer: the whole file
related-designs:
  - HANDOFF_Terrain_To_UI_Glass_Look.md — the glass look (CE-5034), terrain's own handoff to you
  - NOTE_Backend_To_Ui_Vehicle_Openings_And_Lights_Readers.md — the opening and light readers exist; drawing them is yours (CE-4670)
  - ../../designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md — §6 the slices, §6a / §6b the drawing
  - PROTOCOL_Autonomous_Lanes.md — §2 ④ ⑤ now look at EVERY lane's branch, case-insensitively (why you missed these)
-->
# HANDOFF — coordinator → `ui`: WORK WAITING FOR YOU (glass look, vehicle openings and lights drawn)

You reported idle while two other lanes had handed you work: terrain's `HANDOFF_Terrain_To_UI_Glass_Look.md` sat on the `terrain` branch (and its
`To_UI` did not match the protocol's case-sensitive `To_Ui`), and backend's notes said "drawing them is yours". ⭐ The protocol is fixed (§2 ④ ⑤);
this handoff just puts the work in your queue. Priority: next, in this order.

| order | row | what | spec |
|---|---|---|---|
| 1 | **CE-5034** | GW-3 the look of glass — the pane quads and the crack stars from the replicated window damage, rebuilt when a window changes | [`HANDOFF_Terrain_To_UI_Glass_Look.md`](HANDOFF_Terrain_To_UI_Glass_Look.md) (frozen at terrain's `36a1ad8f3`), [`DESIGN_Glass_Windows.md`](../../DESIGN_Glass_Windows.md) |
| 2 | **CE-4670** (drawing) | our 3D map draws the vehicle OPENINGS (`VehicleLook.OpeningProgress` per opening → the socket's pose; a `CrewExposed` hatch shows a crewman's head and shoulders; the commander's eye of CE-1200 follows it) and the LIGHTS (`VehicleLook.Lights` with one `BrakeLights` on the layer); the station's light switches and a possessed crewman's "head out" (`VehicleRequests.SwitchLights` / `HeadOut`) | [`NOTE_Backend_To_Ui_Vehicle_Openings_And_Lights_Readers.md`](NOTE_Backend_To_Ui_Vehicle_Openings_And_Lights_Readers.md); scenarios `uc-convoy-hit` (an APC's ramp), `va-lights-night` |
| 3 | **CE-1980** / CE-3451 ②④ | ONLY once `DECISIONS_Ui.md` says DECIDED for it | — |

Gates as usual (rule 8; display rails under `xvfb-run -a`; §6a). A visual change has a mockup or the design's picture as its acceptance — render and
compare yourself (R-4146).
