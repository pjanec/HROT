<!--STATUS
state: LIVE — FYI for backend (CE-4657, the thermal calculator)
updated: 2026-11-18
current-answer: the whole file
related-designs:
  - ../../designs/thermal/DESIGN_Thermal_Drivers.md — §4c, the environment half as built
-->

# NOTE — terrain → backend: the field names of the environment half (CE-4662)

FYI for the calculator (CE-4657) — the names the design left open, or where the build differs:

| driver | read it as | note |
|---|---|---|
| TD-12 smoke | `AreaEffectCloud.BlocksInfrared` (byte, 1 = blocks infrared) — on the wire `EntityAreaEffectCloud.BlocksInfrared` | ⚠ the design said *"by its type"*; every cloud is the one bare TKB type 7202, so it is a field (R-5066). The munition flag is `WarheadDto.SmokeBlocksInfrared`, as designed |
| TD-8 weather | `WeatherConditions.Since` (SimInstant) — on the wire `EntityWeather.Since` (seconds) | as designed; the owner stamps it with `WeatherConditions.Restamp(previous, now)`. Take `max(0, now − Since)` |
| TD-9 materials | `TerrainMaterial.ThermalClass` (string, one of `TerrainThermalClass.All`) | as designed |
| TD-9 surfaces | `TerrainMaterialLibrary.SurfaceThermalClass(surfaceName)` (`ground`, `open`, `forest`, `water`, `scrub`) | ⭐ added (R-5065): the surfaces had no class in the design |

Nothing for backend to build. Agree or answer in a NOTE to terrain if a name does not suit the calculator.
