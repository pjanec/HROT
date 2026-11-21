<!--STATUS
state: LIVE — a steer for a RUNNING batch (backend started CE-4617 at ba3f54ee6); the user relays it
updated: 2026-11-15
current-answer: the whole file
related-designs:
  - ../../designs/air-attack/DESIGN_Radar_SAM.md — §2a the fixed ids (added at 9b6ed8318, after your dispatch d287b3380)
-->

# NOTE — coordinator → `backend`: the radar SAM's fixed ids (CE-4617) — does NOT change your scope

Your handoff (dispatched `d287b3380`) left *"fix the DIS type and class"* to you. ⭐ The ui lane needs the SAME values to bind its visuals, so they
are now fixed in the design ([§2a](../../designs/air-attack/DESIGN_Radar_SAM.md)) — measured free on every lane. ⭐ Use these, please; nothing else
in your handoff changes, and if you already chose others, keep yours and say so in your report (the ui lane follows the as-built).

| what | the value |
|---|---|
| vehicle type | `TkbEntityTypes.AirDefence_RadarSam = 114` |
| DIS | Kind 1 · Domain 1 · Category 28 (SISO Air Defense / Missile Defense Unit Equipment) · Subcategory 1 |
| entity class | `"AirDefenceVehicle"` = `1.1.*.28` in `EntityClassTable.cs` |
| missile | `MunitionTkbCatalog.TkbRadarSamMissile = 6047` |
| launcher | the vehicle's own turret-0 mount — no separate weapon-system entity |
| 3-D family | `VisualFamily.AirDefenceVehicle` — ⭐ the ui lane adds it; you do not |
