<!--STATUS
state: LIVE — all seven rows handled (5 fixed, 2 not reproduced, with the measurements)
updated: 2026-11-18
current-answer: the row table
related-designs:
  - HANDOFF_Coordinator_To_Ui_User_Observations.md — the dispatch
  - ../../DESIGN_Entity_Control.md — owns the possession station (CE-1961 keys, CE-1962 the driver's gun)
  - ../../designs/air-attack/DESIGN_Air_Operations.md — owns the fixed gun's nose cone (AW-1; CE-1960)
-->

# STATUS — ui: the user's editor observations (CE-1960, CE-1961, CE-1971, CE-1962, CE-1965, CE-1969, CE-1970)

Reproduction: the editor headless over HTTP (`--mode editor`, port 8131), possess + `POST /entities/{id}/control` + `GET /combat/shots`.

| row | verdict | reproduction · root cause · fix | rail |
|---|---|---|---|
| **CE-1960** jet gun fires backwards | ✅ fixed | `ac-guns-drone`, possessed Viper: aim 800 m behind ⇒ every round left the nose toward the tail. The operator's shot at a point skipped the fixed gun's nose cone (`obeyArc: false`), and the burst stream was drawn to the posture point. `BodyAim.IntoFixedCone` (along the bore when outside the cone); `StreamToward` keeps a point only within 45° of the launch | `AimAndFireExecutorTests` (AW-1 cone rail), `EffectTests.CE1960_…` |
| **CE-1961** jet cannot pitch | ⚠ not reproduced | `vertical: 1` for 6 s ⇒ +231 m; the station sends Vertical on Q/Space · Z/C (CE-1351), the HUD says "Q/Z climb/dive". W/S = throttle, mouse = free look. Lean: also pitch on the mouse's vertical look (user decides) | `PossessionStationTests` CE-1095 (existing) |
| **CE-1971** aircraft labels vanish | ✅ fixed | the name's 2 m/px zoom limit (CE-4388) hid aircraft too. `AircraftNameGizmo`: fixed / rotary wing, no zoom limit | `Map3DGizmoTests.CE1971_…` |
| **CE-1962** driver cannot fire with no gunner | ⚠ not reproduced | Bradley (no crew) fired 180 rounds; HMMWV 102 (crewed) and 111 (no crew) fired their M2 from the driver seat. Needs the vehicle type + its HUD line | `PossessionStationTests.CE1962_…` (new, green) |
| **CE-1965** toolbar split button | ✅ fixed | `ToolStripWindow` dropdown tile opened its menu on every click ⇒ a split: the ▾ corner (42 %) or a right-click opens the menu, the icon runs the last choice. ⚠ still a square tile (grid), not the main toolbar's face + ▾ | `EditorToolStripsTests.CE1965_…` |
| **CE-1969** Finish with nothing to finish | ✅ fixed | `PointSequenceGizmo.ToolMenuJson` returns the menu only once a point is placed | `AreaShapeGizmoTests.CE1969_…` |
| **CE-1970** stuck in 3-D | ✅ fixed | `MapViewSwitch._toggling` was cleared only by the animation's end callback, which `MapCamera3D` drops when a driver takes the camera or a focus replaces the animation ⇒ every later toggle ignored. `Switching()`: a switch runs only while its animation runs | `Map3DTests.CE1970_…` |

## Gates

| gate | result |
|---|---|
| `Fdp.Toolkits.Tests` `AimAndFireExecutorTests` | 145 / 0 |
| `Hrot.Presentation.Tests` Map3DTests · Map3DGizmoTests · EffectTests · PossessionStationTests · FilmTests · area / tool rails (xvfb) | 417 / 4 — the 4 reds predate this work (CE-1955: `S1_TheIconFallback…`, `LR1_…` (the M61 warhead), `CE1309_…`, `CE1086_…`) |
| `Fdp.Presentation.Tests` AreaShapeGizmoTests · GizmoEditStyleTests | 20 / 0 |
| `Hrot.Editor.Tests` EditorToolStripsTests · EditorToolbarsSectionTests · AdapterTests | see the commit |
