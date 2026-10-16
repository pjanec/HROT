<!--STATUS
state: LIVE
updated: 2026-10-16
current-answer: the whole file — the report for CE-3216 S1-0 and S1 (weather and time of day), backend half.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/weather/DESIGN_Weather_And_Time.md — §6.1 / §6.2 hold the as-built (this report only points there)
  - docs/designs/airborne/DESIGN_Airborne_Insertion.md — D-6 marked SUPERSEDED by W-5
  - docs/blueprints/batches/HANDOFF_Backend_To_Ui_Weather_And_Scenario_Time.md — the editor half, dispatched to ui
-->

# Report — backend: weather and time of day, S1-0 and S1 (`CE-3216`)

Branch **`backend`**, started at `38fc06b12` + `ui@931f3ee8a` (behaviors' `6231138f0` was a WIP commit, not merged). Design:
[Weather and Time](../../designs/weather/DESIGN_Weather_And_Time.md) — the as-built is its §6.1 / §6.2.

## 1. What is built

| | |
|---|---|
| **the clock (S1-0)** | the first positionless replicated entity — the **World Environment** (TKB 8810) — carries `ScenarioClock`, the authored start time; every node reads UTC = start + its sim time (`ScenarioTime.Utc`). A runtime change is one attribute, `ScenarioStartUtcTicks`, through the existing attribute request: the owner applies it as sent. A scenario without one gets a default at load, at a FIXED 2025-06-21 06:00 UTC (never the wall clock); a second is ignored |
| **the weather (S1)** | `WeatherConditions` on the World Environment (global) and on **Weather Regions** (TKB 8811, areas); `WeatherField.At(world, point)` blends them (a region weighs 1 at its centroid and 0 at its edge along the ray; overlaps add; the global fills the rest; the wind blends as a vector) and derives temperature (with a day–night swing), pressure, density, the wind at a height, visibility (fog), cloud, precipitation, the sun (NOAA) and the light band |
| **W-5** | the parachute and the airborne release point read `WeatherField.WindAt`; the `Wind` singleton is gone |
| **handed to ui** | the Scenario Details view (start time + global weather), the Weather Region tool, inspectors, and CE-3217 — [handoff](HANDOFF_Backend_To_Ui_Weather_And_Scenario_Time.md) |

## 2. Deviations (all folded into the design §6)

| # | deviation | why |
|---|---|---|
| D1 | the built-in weather is **calm** | the retired singleton was *absent ⇒ calm*; a breeze in the default reddened `DescentModelsTests.B2_TheBank_*` and would move every airborne demo |
| D2 | `IAreaOutline` (toolkit) implemented by `EditablePolyline` (Hrot) | lets `WeatherField` read a region's outline from FDP; moving the type or a host-registered delegate were rejected |
| D3 | the editor UI of S1 moved to the ui lane | editor surfaces are theirs |
| D4 | the wind's surface fall-off applies only below the lowest level | a level at or below the ground means "this wind reaches the ground" (`SteadyWind`) |
| D5 | a runtime WEATHER edit from a non-owning host has no path yet | only the clock has an attribute; the editor owns the World Environment in edit mode and writes directly |

## 3. Gates

Base: `backend@7a0079d8d` (S1-0) + the S1 working tree. One full-solution build, then every suite `--no-build`.

| gate | command | result | delta / note | `--no-build` |
|---|---|---|---|---|
| T-1 split authority (the feature's suite) | `ClusterRunner.Integration.Tests --filter SplitAuthoritySpawnTests\|AttributeChangeRequestRoundTripTests` | **9 / 0** | +1 (IT-SA-5) | ✅ |
| red-proofs (run) | drop the clock egress · a no-op JSON route | IT-SA-5 red both times; restored, green | the C-3 / W-8 load rails' red-proofs are by construction, not run (marked so) | built |
| full build | `dotnet build HROT.sln --no-restore` | 0 errors, 107 s | | — |
| toolkits | `dotnet test Fdp.Toolkits.Tests` | **3674 / 0 / 1 skip** (re-run after the profile-rail tolerance fix, built) | +WeatherFieldTests, +1 parachute rail, 3 wind rails ported | ✅ |
| core | `dotnet test Hrot.Core.Tests` | **271 / 0** | +3 `WeatherRegionBlendTests` | ✅ |
| simhost | `dotnet test Hrot.SimHost.Tests` | **1481 / 0 / 6 skip** | +2 C-3/W-8, routing + descriptor rails extended | ✅ |
| presentation | `dotnet test Hrot.Presentation.Tests` | **665 / 0 / 28 skip** after re-pinning the built-in type golden (+8810 / 8811, deliberate, like the Door 8805 row) | 1 red on the first run = that golden | ✅ |
| NED | `dotnet test Hrot.Network.NED.Tests` | **142 / 0** after re-pinning `RoleGroupOwnershipStrategyTests`' Brain list (+`dtScenarioClock` / `dtWeather`) | 3 reds on the first run = that list | ✅ |
| docs | `tracker-counts.py --check` · `design-digest.py --check` · `mermaid-check.mjs` | OK · OK · 3 blocks parse | | — |
| tree | `git status` after every suite | clean of test output | | — |

⚠ Not run: T3 E2E and a live cluster run (`ua-air-assault` / `ua-air-rescue` should behave exactly as before — the default is calm).

## 4. Ids

None allocated in the tracker (CE-3216 / CE-3217 already existed); component ids 394 / 395, descriptors 138 / 139, TKB types 8810 /
8811, attribute ids 20 / 21 (all measured free on the three lanes).
