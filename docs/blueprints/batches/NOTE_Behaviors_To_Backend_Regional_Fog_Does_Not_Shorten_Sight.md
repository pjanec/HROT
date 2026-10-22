<!--STATUS
state: LIVE
updated: 2026-10-22
current-answer: the whole note
related-designs:
  - ../../designs/weather/DESIGN_Weather_And_Time.md — §8 S2-1 (sight capped by the visibility), owns the rule
  - ../../DESIGN_Sensors_And_Doctrine.md — §5.5 the implicit visual sensor the observers use
-->

# NOTE — behaviors → backend: a fog REGION does not shorten sight live (`CE-2350`)

**Found** building CE-4035 G-3 (`scenarios/ua-weather-sensing`, `--mode all`, `2026-10-22`).

| measured | |
|---|---|
| ❌ two identical riflemen (sight 150 m) watch an insurgent walk to them from 220 m at noon; one pair stands inside a `Fog bank` region (visibility 60 m, fog top 40 m, feather 10 m, x −50…270, y 230…370) | **both first see their target at ~148 m** (visual slot in `/entities/{id}/memory`) |
| ✅ the same pairs at night (`ua-night-sensing`, global weather) | seen at **37 m** = 150 × 0.25 — so `PerceptionWeather.TrySample` / `VisionRange` DO run in the solver |
| ✅ the region on every node (`GET /entities/1001` per perspective) | Scenario, SimHost and IG all hold `EditablePolyline` (the four points) + `WeatherConditions` (VisibilityM 60) + `SimTransform` (110, 300) |
| ⚠ rails | `PerceptionWeatherTests.S2_1_*` covers GLOBAL fog only — no rail puts a REGION under an observer |

⇒ the global path works and the region is replicated; something between `WeatherField.Contributors` and the solver's view drops the
region (candidates, unmeasured: the region's outline or `WeatherConditions` in the solver's background snapshot; `RegionWeight` for a
point 50 m inside). **Ask:** a rail with a region under the observer, then the fix. Reproduce: `python3 scripts/utility-demo-check.py
--launch ua-weather-sensing` (② fails at ~148 m).

_behaviors lane, 2026-10-22_
