<!--STATUS
state: LIVE
updated: 2026-11-10
current-answer: the whole note
related-designs:
  - ../DESIGN_Terrain_Import_UTM.md — §12.2 / §12.3 U: UtmTransform and SwitchableGeoTransform as built (CE-4394, c08674f77)
  - ../DESIGN_Geo_Origin.md — R-229, the ONE origin and IGeographicTransform
  - Architect_Question_107_NED_Wire_Frame_And_Names.md — §4 row C: the master publishes the wire's geo reference
-->

# NOTE — user → coordinator: **UTM for every terrain, the tangent plane retired** *(2026-11-10)*

🔒 **User, `2026-11-10`:** *"I would like to use UTM as the default for every terrain and basically to get rid of the tangential plane in
HROT whatsoever."* and *"hRot will be using UTM with local offset, so the coordinates stay small around zero because hRot uses floats."*

So: **every terrain's local frame = UTM grid metres minus a local offset (E₀, N₀, h₀)** — X grid east, Y grid north, Z up — and
`WGS84Transform` (ECEF → ENU at an origin) leaves the engine. The local offset stays (floats: |x|, |y| must stay small, i.e. the local origin
inside or next to the terrain, as the importer's south-west corner already is).

Written by a session working on an external gateway; nothing in HROT was changed for this note. Please design and schedule it.

## 1. Where it stands (`origin/coordinator` @ `2b62be574`)

CE-4394 already made the node's one transform switchable (`SwitchableGeoTransform`): **UTM when `terrain.json` declares `frame.utm`, the
tangent plane at `origin` otherwise** (`TerrainResidency.ApplyGeoOrigin`). What still uses the tangent plane or carries only lat / lon:

| # | Where | Today | Needed for "UTM everywhere" |
|---|---|---|---|
| 1 | **the committed terrains** `assets/terrain/*/terrain.json` | all 8 carry only `origin {lat, lon, alt}`, none `frame.utm` | a `frame.utm` for each (zone from the origin's longitude; E₀ / N₀ = the origin projected, so local (0, 0) stays where it is). Local content (scenarios, world files in local metres) does not move; what is placed by lat / lon (SUMO nets, OSM, geodetic orders, DIS) lands on the grid instead of the tangent plane — up to a degree or two of rotation far from a zone's central meridian, which is the point. |
| 2 | a terrain with no origin / `(0, 0, 0)` | the tangent plane at 0, 0, 0 (R-229: no default place) | decide: `frame.utm` required, or an explicit "no geo frame" (local only) |
| 3 | `SwitchableGeoTransform` | starts as the tangent plane at 0, 0, 0; `UseTangentPlane` | UTM-only (or "no frame" until a terrain loads); `UseTangentPlane` / `WGS84Transform` deleted |
| 4 | SUMO placement `Hrot.Traffic/SumoNetPlacement.cs` | its own `WGS84Transform` (net → WGS84 → tangent ENU); CE-3429 | a net is already UTM: projected − netOffset → the terrain's UTM local is a translation (same zone) |
| 5 | recordings `RecordingMetadata` `GeoOriginRecord`; Replay Browser (CE-3430) | lat / lon origin only; the browser keeps a concrete `WGS84Transform` | the recording carries the UTM frame (zone, hemisphere, E₀, N₀, h₀); the browser uses it; old recordings: read their lat / lon origin as a UTM frame at that point |
| 6 | **the published geo reference** (`ClusterMaster.GeoReference.cs`, `OrchestrationMessages.cs:273`: *"ECEF = ecef(origin) + R(origin) · local"*) | lat / lon / alt + generation | carry the UTM frame (zone, hemisphere, E₀, N₀, h₀); external consumers (gateways) convert local ↔ geodetic with UTM, not a rebuilt tangent plane — **without this, an external consumer stays rotated by the grid convergence even on a UTM terrain** |
| 7 | the tangent-plane-specific comments, tests and doubles (`DebugApiService`, `ClusterRunner/Program.cs:509`, `BehaviorIngressSystem`, `OrderMenu`, `Fdp.Toolkits.Tests/Geographic/*`) | | follow #3 |
| 8 | headings | already grid-north on the wire and in the engine; γ applied only at true-north outputs (U-3) | unchanged |

## 2. Consumer outside HROT (for information)

The gateway that bridges HROT to external simulation protocols (DIS, HLA-style buses, an image generator) currently receives only the
lat / lon origin and **rebuilds its own tangent plane** from it. It will drop the tangent plane too and convert with UTM from the frame HROT
publishes (row 6). Until row 6 lands it can only be exact for terrains whose frame it is told some other way.
