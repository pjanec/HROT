<!--STATUS
state: LIVE — dispatched to the ui lane
updated: 2026-11-28
current-answer: the whole file
related-designs:
  - ../../designs/devices/DESIGN_Simulated_Devices.md — OWNS this: §3.8a (SD-S6a — D-158–D-162, the class diagram), §3n (D-31–D-36), §3.8 (LS-5, LS-13, R5-10)
  - ../../designs/sensor-view/DESIGN_Sensor_View.md — OWNS the sensor views (Off / Night Vision / Thermal) and SceneSensors.Emissive the beam goes through
  - ../../designs/weather/DESIGN_Weather_And_Time.md — OWNS the WeatherSample the haze is read from
-->

# HANDOFF — coordinator → ui: the laser beam and its spots on the 3-D map (SD-S6a)

**Dispatched at:** the `coordinator` commit that adds this file. ⛔ **Your scope is FROZEN at that commit** — documents that change after it are FYI only;
a later document that invalidates an item ⇒ STOP that item and report it (never adapt silently). Merge `coordinator` first (it carries the laser data,
S1c–S1e, and the design section this cites).

🔒 User, `2026-11-28`: *"is the laser beam and laser spot stuff already settled so that we could start rendering them in 3d map in view modes that are able to
'see' the laser light reflecting on air dust? if so let's pls write handoff to the ui lane"*.

## 1. Goal

Every 3-D map host draws a laser's BEAM (its scatter off haze / dust) and its SPOTS, as each viewer's sensor would see them — naked eye, night vision,
thermal — from the replicated laser components, stateless (a pause freezes it, a replay shows it as it was).

## 2. Design basis — build THIS, do not redesign it

[`DESIGN_Simulated_Devices.md`](../../designs/devices/DESIGN_Simulated_Devices.md) **§3.8a** — the class diagram there is the build contract:

| decision | what |
|---|---|
| D-158 | ONE pure rule `LaserVisibility` in `Fdp.Toolkits/Behavior/Devices/` (`Beam`, `Spot`, `Aerosol`; a toolkit-side `LaserSensor { Eye, NightVision, Thermal }`) — AI perception and the IG will call the same rule later; ⛔ no table in the layer |
| D-159 | the band table: 0 visible (eye ✓ NV ✓ thermal ✗) · 1 eye-safe rangefinder (nothing sees it) · 2 designator (NV faint ×0.25 only) · 3 near-IR pointer (NV bright only) |
| D-160 | the haze: aerosol from `VisibilityM`, humidity > 80 %, fog; the beam fades with `exp(−d / VisibilityM)`; the spot is a surface return (no aerosol) |
| D-161 | `LaserLayer3D` (Effects, beside `ProjectileLayer`, attached in `MapInteractionPack.AttachMapLayers`): additive line origin → spot points, a glow per spot; night vision through `SceneSensors.Emissive`; thermal draws nothing; a `Preview` spot IS drawn (observation); the trainee's own beam from his local look (D-34); 0 B per steady frame |
| D-162 | editor and replay only for now — on a cluster the station sees an emitter once SD-C replicates `LaserPointing` / `LaserSpot` |

## 3. What exists (measured `2026-11-28`)

| | where |
|---|---|
| `LaserPointing { On, Band, Power, Direction, Seq }`, `LaserSpot` (≤ 4 points: point, range, strength, hit, part; `Count`, `Preview`) | `FDP/Toolkits/Fdp.Toolkits/Behavior/Devices/Laser.cs` |
| the muscle writes `LaserSpot` every frame | `LaserSpotSystem.cs` (`CombatModule` PostSimulation) |
| an emitter's world origin + frame | `DevicePoses.TryWorld` (`DevicePose.cs:81-95`); `Direction` is in the device frame |
| the sensor mode in a draw | `RenderContext.Sensor` / `SceneSensor` + `SceneSensors.Emissive` (`FDP/Engine/Fdp.Presentation/Vis2D/Abstractions/CoreInterfaces.cs:156-175`) |
| the weather | `WeatherSample.VisibilityM / RelativeHumidity / FogVisibilityM` |
| the layer pattern to copy | `Hrot/Engine/Hrot.Presentation/Effects/ProjectileLayer.cs`, `EffectLayer` (additive draws, emissive under NV) |
| a scene that has a laser today | the rangefinder scenarios (`scenarios/demos/devices/dv-range-targets`) — ⚠ band 1, invisible by D-159; a band-0 / band-2 / band-3 emitter for the rails is a test fixture (a `LaserPointing` + `LaserSpot` written by the rail) |

## 4. Fences

- ⛔ No change to `LaserPointing` / `LaserSpot` / `LaserSpotSystem` (the coordinator lane owns them; the designator's `Code` field is coming in SD-S5c).
- ⛔ No AI perception of a spot (that is SD-S6's other half) and no IG / gateway forwarding (D-36).
- ⛔ Not a gizmo — a world layer (D-32).
- ⭐ R-4143: draw a small mockup of the beam under eye / night vision / thermal FIRST (house style, `scripts/mockup_kit.py`), commit it beside the design, then build to it and check fidelity yourself (R-4146).

## 5. Acceptance — rails in the layer's own suite (frame rails under `xvfb-run -a`; a skip is not a pass)

1. `LaserVisibility` unit rails: the full band × sensor table; the haze monotonic in `VisibilityM`; 0 B.
2. Frame rails (one fixture emitter, a known spot): band 0 drawn to the eye and under NV · band 2 NOT to the eye, faint under NV · band 3 not to the eye, bright under NV · band 1 nowhere · nothing under thermal · a hazy weather's beam brighter than a clear one's · a `Preview` spot drawn · emitter `On = 0` draws nothing.
3. 0 B per steady frame for the layer (warm 30–300 frames).
4. Attached on every map host by construction (the existing "every map host attaches the shared layers" rail extended).
5. Red-prove one rail with a compiling mutation.

## 6. Report

The gate table (exact command · pass / fail / skip, reds confirmed pre-existing at your base sha), the mockup path, the render paths (scratchpad, never git),
an "SD-S6a AS BUILT" paragraph in §3.8a of the design (and any deviation folded there), the ids you allocated (none expected).
