<!--STATUS
state: LIVE — DISPATCHED at the commit that adds this file (2026-10-16); scope frozen there.
updated: 2026-10-16
current-answer: the whole file. §1 the items, §2 what backend built that you build on, §3 acceptance.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/weather/DESIGN_Weather_And_Time.md — the OWNING design (R-3012): §3 W-8 (the Scenario Details view), §5 S1 / S3, §6 as built
  - docs/DESIGN_Terrain_Zones_And_Assets.md — E3/U5 (BP-542): the map-background Details context this hangs on (CE-3217)
  - docs/DESIGN_Add_Entity_Picker.md — D4: the placement-tool table the Weather Region kind joins
  - docs/DESIGN_Map_3D_Mode.md — S3: the sun, sky, fog and cloud from WeatherField
-->

# HANDOFF — backend → ui: the scenario's start time and weather in the editor (CE-3216 S1-0 / S1 ui half, CE-3217)

**For:** the ui lane. **From:** backend, `2026-10-16`. Dispatched at the commit that adds this file; scope frozen there. ⭐ The engine half
is BUILT on `backend` (merge it): the World Environment entity with its clock and the global weather, Weather Regions, `WeatherField`.
Nothing of it is visible in the editor yet — that is this handoff. 📄 Design: [Weather and Time](../../designs/weather/DESIGN_Weather_And_Time.md)
§3 W-8, §6.

## 1. The items (yours to design in detail; leans given)

| # | item | design basis | lean |
|---|---|---|---|
| **U1** | ⭐ **CE-3217 first** — an empty-map click raises map-background focus, and `EditorSubsystem.ScenarioDetails` registers the Zones view (BP-542, built but never registered) | Terrain Zones E3/U5; [tracker CE-3217](../Blueprint_Issues_Tracker.md) | the empty-map click already clears the selection — raise `SelectionOrigin.MapBackground` there |
| **U2** | ⭐ **the Scenario view** beside the Zones view on that context: the **scenario start time** (date + time, UTC, with the display offset shown as local) and the **global weather** | W-8 ("authored in the scenario's Details view … a runtime change sets a new start time") | read the World Environment (`TkbType` 8810: `ScenarioTime.TryClock`, `WeatherField.GlobalOf`); write the start time through **`EntityAttributeChangeRequests.TryPublishJsonPatch(world, env, {"ScenarioStartUtcTicks": <ticks>})`** (or `"ScenarioUtcOffsetMinutes"`) — it applies directly on the owner and asks the owner otherwise; ⛔ never fill it from the wall clock (W-8). The global weather: edit the `WeatherConditions` component on the World Environment (the editor owns it in edit mode) |
| **U3** | **the area tool's "Weather Region" kind** | design §5 S1 (moved to this handoff: the placement tools are editor UI) | a `PlacementToolRegistry.Graphics` row `TkbEntityTypes.WeatherRegion` (8811) armed through the area mechanism with that TKB type (as `StartZoneAuthoringMode` does for 8804); ⚠ the created entity must carry **`WeatherConditions.Standard`** — a region without it is no region (`WeatherField` ignores it) |
| **U4** | the region's weather in its Details | — | if the generic component inspector already edits `WeatherConditions` (fixed buffers: 8 wind levels, 4 cloud layers), nothing to build; else a small weather editor shared with U2 |
| **U5** | S3 (later, ui's slice) — the 3D map's sun direction, sky, fog, cloud layers; smoke on the wind | design §5 S3 | one call: `WeatherField.At(world, point)` returns the sun's elevation/azimuth, light band, extinction, cloud octas/ceiling, wind |

## 2. What backend built that you build on

| | |
|---|---|
| `ScenarioClock` (component 394) on the **World Environment** (TKB 8810, no `SimTransform`) | every scenario has exactly one — the load creates a default at 2025-06-21 06:00 UTC with the built-in weather when the file has none (C-3), so the view always has something to edit, and the next save writes it |
| `WeatherConditions` (395) — the global on the World Environment, a region's on a **Weather Region** (8811: `EditablePolyline` + `SimTransform` + `WeatherConditions`) | both save as authored (the scenario serializer's default policy) and replicate (`dtScenarioClock` 138, `dtWeather` 139) |
| `ScenarioTime.Utc(world)` · `WeatherField.At(world, point)` · `WeatherField.WindAt(world, point)` | pure functions, any node, no system |

## 3. Acceptance

U1: an empty-map click shows the Zones and Scenario views (a frame rail, R-124). U2: a start time set in the view is saved and reloaded
exactly as typed, and a runtime change on a non-owning host reaches every node (backend's IT-SA-5 rail proves the path; the view's own
rail proves it calls it). U3: a drawn Weather Region carries `WeatherConditions` and changes `WeatherField.At` inside it. Report back in a
`HANDOFF_Ui_To_Backend_*` if any engine piece is missing.
