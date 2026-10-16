<!--STATUS
state: LIVE
updated: 2026-10-16
build-state: READY-TO-BUILD — 🔒 user, 2026-10-16: W-1, W-2, the clock (C-1…C-4), humidity and precipitation approved ("approved, write the weather design with W-1 W-2 too, humidity and precipitation of course yes, no day rate"); ruling R-3012. Programme CE-3216.
current-answer: §1 the model · §2 the diagrams · §3 decisions · §4 the derived quantities · §5 slices and rails. §0 is the measured ground.
stale-below: nothing.
known-rot: none.
known-conflict: docs/designs/cgf-1/CGF-1-DESIGN.md (~line 1113) — the Orchestrator writes a "global weather descriptor" to Orchestrator.json and publishes it on OrchestratorContextTopic. Never built (the save arm was retired, CE-278); SUPERSEDED by §1 here — weather is scenario ENTITIES, not orchestrator context.
related-designs:
  - docs/designs/tkb-1/DESIGN.md — the "should every entity have a SimTransform?" analysis (2026-09-13): a positionless entity is safe as long as it carries NO SimTransform; this file builds the first one
  - docs/designs/airborne/DESIGN_Airborne_Insertion.md — §7d D-13 (how the scenario's wind reaches every node): SUPERSEDED by this file — the World Environment entity replicates; the parachute model reads WeatherField's wind profile
  - docs/DESIGN_Terrain_Zones_And_Assets.md — §2.1: a zone is its OWN type (8804), not a flag on the tactical area — the precedent for the Weather Region type
  - docs/DESIGN_Thermal_And_Acoustic_Sensing.md — row F ("weather / night attenuation — not asked"): S2 here gives thermal and hearing the weather
  - docs/DESIGN_Map_3D_Mode.md — the lit shader + fog: S3 (ui) draws the sun, the sky, fog and cloud layers from WeatherField
  - docs/DESIGN_Visual_Effects.md — W3's fixed breeze for smoke: S3 drifts it on the real wind
  - docs/designs/audio/DESIGN_FPS_Audio.md — the ambient beds by time of day and the wind bed: S4
  - docs/DESIGN_Geo_Origin.md — the geographic transform (latitude / longitude of a local point) the sun position needs
-->

# Weather and time of day — regional weather, one global environment, a sun

**What it does:** the scenario states the weather — globally, and in regions that blend smoothly into it — and the date and time.
Every node derives, at any point and moment, the air temperature and pressure, the wind at any height, visibility, cloud, the sun's
position and the light level. Sensors, motion models, rendering and audio read that one answer.

🔒 **User, `2026-10-16`:** *"Wind as well as another properties of the weather is by nature regional, and defaulting to a "global"
weather where no region is explicitly defined. weather blends between regions (the closer to the region cetroid the stroger that region
applies the region setting) so the transition from global weather into regional one is smooth."* · *"daytime clock could be a special
entity (singleton, global, replicated) definig what UTC time there is relative to simulation clock. so current day time = currentSimTime
+ Offstet. This entity needs to be saved to scenario and needs to be changeable at runtime and replicated to other nodes."* ·
*"approved, write the weather design with W-1 W-2 too, humidity and precipitation of course yes, no day rate"* · *"does it make sense to
combine them into one single entity which has weather component and scenario time component?"* — ⭐ yes (§3 W-0).

## 0. INVENTORY — what exists (measured `2026-10-16`, codebase-memory CLI + grep)

| query | result |
|---|---|
| `search_graph` `.*Weather.*` · `.*Fog.*` · `.*Cloud.*` · `.*Temperature.*\|.*Pressure.*` · `.*TimeOfDay.*\|.*Sun[A-Z].*` (Class) | **0** production types |
| grep `Weather\|TimeOfDay\|SunElevation\|FogDensity\|CloudLayer\|AirTemperature\|struct Wind` over `FDP Hrot Stride` | ONE: `Fdp.Toolkit.Motion.Wind` (`Descent.cs:81`) — a world SINGLETON, one 2-D velocity, no altitude, no region |
| readers of `Wind` | `DescentModels.cs:313` (the parachute) · `AirborneDrillBehaviors.cs:406` (the release point) |
| grep `Night\|LightLevel\|Illumination\|Daylight` | **0** — no light level anywhere; Thermal and Acoustic Sensing row F: *"weather / night attenuation — not asked"* |
| area entities | `TacGraphic_Area` 8803 and `TerrainZone` 8804 (`TkbEntityTypes.cs:68-76`): an `EditablePolyline` + a `SimTransform`; authored by `AreaAuthoringArm`, drawn by `TacticalAreaGizmo` |
| positionless replicated entities | ⛔ **none** — the nearest, `Door` (8805), carries a `SimTransform` (`TerrainObjectRequests.cs:61`) |
| runtime edit of a remote entity | `UpdateEntityAttributeCommand` → DDS → the OWNER's `UpdateEntityAttributeRequestSystem` applies the patch |
| a load's clock | an exercise load JUMPS the clock to its saved sim time (`OrchestratorCore.cs:100`, `time.SnapTo(… TotalTime = simTimeSeconds)`); ⛔ a plain scenario load at sim time 0 — not measured |
| latitude / longitude of a local point | `WGS84Transform.ToGeodetic` (every node holds its geographic transform) |

## 1. The model — ⭐ weather is DATA, everything derived is a PURE FUNCTION

| | kind | carries |
|---|---|---|
| **World Environment** *(new type)* | an entity with **no `SimTransform`**, one per scenario | `ScenarioClock` + `WeatherConditions` (the GLOBAL weather) |
| **Weather Region** *(new type)* | an AREA entity (`EditablePolyline` + `SimTransform` at its centroid) | `WeatherConditions` (a regional weather) |
| `WeatherField` | a pure function on every node | blends the regions into the global weather at a point (§3 W-1/W-2) and derives §4's quantities from the result, the point's height and the clock |

⇒ ⭐ **no weather STATE at runtime**: the only replicated values are what was authored (and what an operator changes). Every node gets
the same answer from the same replicated entities and the cluster's synced sim time — a replay or a rewind reproduces it by construction.

## 2. The diagrams

```mermaid
classDiagram
    direction LR
    class WorldEnvironment { <<NEW TKB type, no SimTransform, one per scenario>> }
    class WeatherRegion { <<NEW TKB type, an area>> }
    class EditablePolyline { <<existing>> the area's points }
    class ScenarioClock { <<NEW component, dtScenarioClock>> long UtcTicksAtSimZero; short DisplayUtcOffsetMinutes }
    class WeatherConditions { <<NEW component, dtWeather>> ground temperature, lapse rate, sea-level pressure, humidity; Wind[8]; CloudLayer[4]; visibility, fog top; precipitation kind and rate; FeatherM }
    class WindLevel { altitude MSL; direction from; speed; gust }
    class CloudLayer { base MSL; thickness; cover in eighths; kind }
    class WeatherField { <<NEW, pure>> At(world, point) WeatherSample; Utc(world) }
    class WeatherSample { temperature; pressure; density; wind; visibility; extinction; cloud cover above; ceiling; precipitation; sun elevation and azimuth; irradiance; light level }
    class SolarPosition { <<NEW, pure>> Of(utc, lat, lon) }
    class GlobalTime { <<existing singleton>> TotalTime }
    class WGS84Transform { <<existing>> ToGeodetic }
    class Wind { <<existing singleton, RETIRED>> }
    class UpdateEntityAttributeRequestSystem { <<existing>> applies a remote edit on the owner }
    WorldEnvironment *-- ScenarioClock
    WorldEnvironment *-- WeatherConditions
    WeatherRegion *-- WeatherConditions
    WeatherRegion *-- EditablePolyline
    WeatherConditions *-- WindLevel
    WeatherConditions *-- CloudLayer
    WeatherField ..> WeatherConditions : blends
    WeatherField ..> ScenarioClock : Utc = ticks + sim time
    WeatherField ..> GlobalTime
    WeatherField ..> SolarPosition
    WeatherField ..> WGS84Transform
    WeatherField --> WeatherSample
    UpdateEntityAttributeRequestSystem ..> ScenarioClock : set time
    Wind ..> WeatherField : replaced by
```

*What the picture shows that prose hid:* **one component type for the global and the regional weather** — so the blend has one input
type, an editor has one inspector, and the global weather is just the region that is everywhere. The clock and the weather share an
ENTITY but not a component: each replicates as its own descriptor, so time and weather stay separate data.

```mermaid
sequenceDiagram
    participant L as ScenarioLoadStep (the Brain)
    participant C as entity creation
    participant N as every node
    participant O as operator (any node)
    participant A as the owner (the Brain)
    participant U as a consumer (parachute, sensor, renderer)
    L->>C: the scenario's World Environment, Weather Regions (a default World Environment when it has none, C-3)
    C->>N: replicated: ScenarioClock, WeatherConditions, the regions' polylines
    Note over N: the clock's ticks set at load = saved UTC minus sim time after load (C-1)
    O->>A: UpdateEntityAttributeCommand: target UTC (or a weather edit)
    A->>A: ticks = target UTC minus ITS sim time (C-2)
    A->>N: the clock (or the weather) replicates
    U->>N: WeatherField.At(point)
    N->>N: blend the regions into the global weather (W-1, W-2), then derive the sample (section 4)
    N-->>U: WeatherSample
```

*What it shows:* nothing is pushed per frame — a consumer ASKS, and the answer is computed from replicated data. The only writes are the
load and an operator's edit, both through paths that already exist (entity creation, the attribute request).

```mermaid
graph TD
    subgraph EN["Every node"]
      WF[WeatherField - pure, no system]
      REP[replication: dtScenarioClock, dtWeather, the polyline]
    end
    subgraph BR["Brain: CGF and the editor"]
      SL[ScenarioLoadStep - creates the entities]
      UA[UpdateEntityAttributeRequestSystem - applies edits]
    end
    subgraph MU["Muscle: SimHost"]
      PM[ParachuteModel - wind profile]
    end
    subgraph PE["Perception"]
      VS[vision, thermal, hearing - S2]
    end
    subgraph IGM["IG and the editor map"]
      R3[3D map: sun, sky, fog, clouds - S3 ui]
    end
    SL --> REP
    UA --> REP
    REP --> WF
    PM --> WF
    VS --> WF
    R3 --> WF
    OLD[Wind singleton - RETIRED in S1]:::dead
    classDef dead fill:#fdd,stroke:#c00,stroke-dasharray: 5 5
```

*What it shows that prose could not:* **there is no WeatherSystem to schedule** — so there is no host on which the weather "never ticks"
(the failure mode a scheduled system has on hosts that do not compose it). Each consumer calls the pure function on whatever node it
runs on; the only per-node requirement is that the entities replicate there, which every role already does. The retired `Wind`
singleton is drawn dead: S1 moves its two readers onto `WeatherField`.

## 3. Decisions — ✅ APPROVED `2026-10-16` (R-3012)

| # | decision | rejected — one line each |
|---|---|---|
| **W-0** | ⭐ the clock and the GLOBAL weather are two components on ONE positionless **World Environment** entity; a region carries only `WeatherConditions` | two singleton entities — two creations, two uniqueness rules, two rails for one "the scenario's environment" |
| **W-1** | ⭐ a region's weight at a point: `t = |p − c| / |b − c|` along the ray from the centroid `c` through `p` to the polygon's boundary `b`; `w = smoothstep(1 − t)` — 1 at the centroid, 0 at the edge; an optional per-region `FeatherM` carries the fade past the edge | a circle round the centroid — ignores the shape (a long valley region bleeds sideways) · a hard edge — not smooth |
| **W-2** | ⭐ overlapping regions ADD; the global weather fills what is left to 1 (`g = max(0, 1 − Σw)`); past 1 the regions normalise among themselves. Scalars blend linearly; WIND blends as a vector, never as an angle; a CATEGORY (cloud kind, precipitation kind) takes the strongest contributor | averaging directions (350° and 10° would make 180°) |
| **W-3** | wind levels and cloud bases in metres above sea level (aviation practice); fog top above ground; below the lowest wind level the wind falls off logarithmically to the ground | mixing references per field |
| **W-4** | the clock: `UTC = UtcTicksAtSimZero + sim time` — pause, time acceleration, rewind and replay follow the cluster clock for free; ⛔ **no day rate** (user) | a free-running wall clock — diverges on pause, replay and rewind |
| **C-1** | the SAVE carries **UTC at the moment of saving**; the load sets the ticks = that UTC − the sim time after load — right whether the load restores sim time (an exercise) or starts at 0 (an authored scenario) | saving the raw offset — wrong on one of the two loads |
| **C-2** | a runtime change carries the TARGET UTC (or a delta); the OWNER computes the ticks against its own sim time, then replicates | computing the offset on the requesting node — off by the request's travel and the sim time meanwhile |
| **C-3** | exactly one World Environment: a scenario with none gets a DEFAULT one at load (so it is always there to change, and is saved next time); a second is ignored with a warning (the first kept) | creating it only on the first edit — nothing to edit in the editor's inspector before then |
| **C-4** | ⭐ the FIRST rail of S1: a positionless entity created on one node, edited at runtime, read on another — nothing has ever replicated an entity with no `SimTransform` | building the weather first and finding the gap at the end |
| **W-5** | the existing `Wind` singleton is RETIRED: the parachute model and the airborne drills read `WeatherField` (a real wind PROFILE at their height) | two ways to read wind (ruling 9) |
| **W-6** | the entities are owned by their creator (the Brain that loads the scenario) — one descriptor each, the Brain group | the CGF-1 orchestrator topic — a second channel for one thing |
| **W-7** | humidity and precipitation are data now (user) — consumers in S2/S4 | — |

## 4. The derived quantities (`WeatherField.At`) — the physics, simple and honest

| quantity | rule | why this rule |
|---|---|---|
| UTC | `UtcTicksAtSimZero + GlobalTime.TotalTime` | W-4 |
| sun elevation, azimuth | the NOAA solar-position algorithm from UTC and the point's latitude / longitude (`ToGeodetic`) | the standard; no time zone needed |
| irradiance at the ground | `1361 · sin(elevation) · τ` (clear-sky transmittance τ ≈ 0.75) × cloud factor `1 − 0.75·(octas/8)^3.4` (Kasten–Czeplak) | the cloud cut is the published fit |
| ground air temperature | the authored DAILY MEAN + a closed-form diurnal swing: amplitude ±6 K clear … ±2 K overcast (scaled by the cloud factor), the peak ~2–3 h after solar noon, a cooler clear night; no integrated state | deterministic, replay-safe, every node equal |
| temperature at height h | ground − lapse rate × h (default 6.5 K/km — the ICAO standard atmosphere) | ISA |
| pressure at h | the barometric formula from the sea-level pressure and the local temperature | ISA |
| air density | `p / (R_specific · T)` | for later aircraft performance and drag |
| wind at h | interpolate the levels; below the lowest, a log profile to zero at the ground; gust as stated | the surface boundary layer |
| visibility, extinction | authored visibility V (m); extinction `σ = 3.912 / V` (Koschmieder); inside a fog layer below its top, else the clear-air value | one number for vision, thermal and the renderer's fog |
| light level | from the sun's elevation: day > 0° · civil twilight to −6° · nautical to −12° · astronomical to −18° · night; dimmed by cloud | the twilight bands are the standard ones |
| thermal crossover | falls out of the temperature model: target–background contrast near zero around dawn and dusk | a real, tactically important effect |

## 5. Slices and rails *(each red-proved; each green before the next)*

| slice | lane | builds | ★ acceptance |
|---|---|---|---|
| **S1-0** | backend | ⭐ C-4 first: the World Environment type with `ScenarioClock` only — created on CGF, edited at runtime, read on SimHost | two-node rail: the clock arrives, an edit replicates, UTC agrees on both nodes within one frame |
| **S1** | backend | `WeatherConditions` + `dtWeather`, the Weather Region type, `WeatherField` (§4), C-1 save/load, C-3 default, the editor's inspector for both, the area tool's "weather region" kind, W-5 (the parachute and the drills on the wind profile, `Wind` retired) | rails: a region's weight is 1 at its centroid, 0 at its edge and smooth between; two overlapping regions and the global fill; wind blends as a vector across north; the clock across save → exercise load and save → scenario load; the parachute drifts on the wind at its height |
| **S2** | backend | perception: vision range by visibility and light level (night), thermal contrast from the temperature model (crossover), hearing by wind | rails in the sensors' suites |
| **S3** | ui | the 3D map: sun direction and sky colour, fog from the extinction, cloud layers; smoke drifts on the wind | frame rails + screenshots |
| **S4** | backend + ui | air density for aircraft and long-range ballistics; wind drift on rounds; ambient audio from wind and time of day | — |
