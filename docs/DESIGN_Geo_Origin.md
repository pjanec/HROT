<!--STATUS
state: LIVE
build-state: BUILT — A–F built 2026-10-08 (CE-3126); §4 is the as-built, C deviated (no GeoOrigin class)
updated: 2026-10-08
current-answer: §2 the decisions · §3 the UML (as-built) · §4 as-built notes
stale-below: §2 row C's "GeoOrigin" wording is SUPERSEDED by §4 ① — the class was not needed
known-rot: none
known-conflict: none
related-designs:
  - DESIGN_Terrain_Zones_And_Assets.md — owns the terrain asset and TerrainResidency's prepare/commit; this adds an origin to the asset and one line to Commit.
  - DESIGN_Cluster_Load_Phase.md — owns the cluster-wide load transaction the terrain step runs in; it is what makes every node switch origin together.
  - DESIGN_Terrain_World.md — terrain coordinates are local metres (X east, Y north, Z up); the origin is what ties them to lat/lon.
  - DESIGN_Terrain_Combat_Tuning.md §5a — CE-3118 (Replay Browser loads the recording's terrain by name) is where the recording learns its terrain; this adds the origin beside the name.
  - DESIGN_Uniform_Gizmo_Membership.md §10 — the map's MapServices; the Replay Browser's missing geo transform (mission lines) closes here.
-->
# DESIGN — **the geo origin comes from the terrain** *(`CE-3126`, backend, `2026-10-08`)*

> 🔒 **User, `2026-10-08`:** *"Geo origin should be specidied as part of terrain file and passed to geoconverter service from there. Origin can be saved in recording metadata in case terrain with remembered name no longer exists"* — **R-229**.
> 🔒 **User, on the leans:** *"No default berlin. Missing geo = zeros. B-f ok."*

Today every node turns local metres into lat/lon with a hard-coded Berlin origin (`HrotEnvironment.CreateGeoTransform`,
`HrotEnvironment.cs:45`). ⇒ a terrain anywhere else is placed in Berlin on the wire and in every exported position.

## 1. INVENTORY *(two read-only sweeps, `2026-10-08`)*

| query | result |
|---|---|
| where the origin is set | ONLY `HrotEnvironment.CreateGeoTransform` → `SetOrigin(Berlin)`. `NodeConfiguration.GeodeticOrigin` (Tel Aviv, `NodeConfiguration.cs:14-18`) and `IgNetworkConstants` exist but only tests read them |
| how many transforms one process holds | ⚠ SEVERAL, each from its own `CreateGeoTransform()`: `HrotNodeBuilder.cs:244` (→ `Context.GeoTransform`) · `HrotNodeBuilderReplicationExtensions.cs:108,180` (replication module, participant) · `NedNetworkFactory.cs:231` / `BdcNetworkFactory.cs:111` (fallback) · `SimHostApp.cs:339` · `ClusterRunner/Program.cs:208,440` · `StrideNodeShell.cs:141,620` |
| who holds one by construction | the DDS translators (`GeoSpatialEgress/Ingress`, NavigationIntent, MapRoute, overlays, spawn/update commands, perception, `BdcWorldPosTranslator`) — **the wire carries lat/lon**; `GeographicModule` systems; the JSON attribute compiler; `MissionPresentationGizmo`, `AreaAuthoringArm`, IG panels |
| who reads it per call | the world singleton (`SetSingletonManaged<IGeographicTransform>`: Editor, SimHost, CGF, Stride — ⛔ not IG): debug API, map pick, drag gizmo, blueprint/CGF nodes |
| the earlier ruling | `CE-236` (`2026-09-08`): *"one single GeographicTransform implementation"*, read through the interface — ⚠ still instances agreeing by copy-paste (`CE-151` open) |
| the terrain file | `terrain.json` → `TerrainDefinition` (`schemaVersion`, `name`, `roadNetworks`, `world`) — **no origin field** in the format or in the 3 shipped terrains |
| the terrain load | `TerrainResidency.Prepare` (off-thread) / `Commit` (main thread) inside the cluster load transaction (`TerrainLoadStep`, `DESIGN_Cluster_Load_Phase.md`) on SimHost, IG, CGF, Editor ⇒ **every node commits the same terrain in the same transaction** |
| recording metadata | `RecordingMetadata` — no scenario, terrain or origin field (`CE-3118` plans the terrain name) |
| `WGS84Transform.SetOrigin` | can be called again, but is NOT thread-safe (no lock/volatile: a reader can see the new origin with the old matrix) |

## 2. Decisions *(leans — for the user)*

| # | ⭐ lean | rejected (one line each) |
|---|---|---|
| **A** ✅ *(revised)* | `terrain.json` gains `"origin": { "lat", "lon", "alt" }` → `TerrainDefinition.Origin`. ⛔ **No Berlin default in code** (`HrotEnvironment.CreateGeoTransform`'s hard-coded origin goes). The shipped terrains carry Berlin as their OWN DATA — measured: `scenarios/tt-nav-los` (test-town) stores its move target as Berlin lat/lon | origin in the scenario — two scenarios on one terrain could disagree about where it is |
| **B** ✅ *(revised)* | a terrain WITHOUT an origin — and a node with no terrain loaded — has origin **0, 0, 0**, said once in the log. ⚠ Measured consequence: `scenarios/test-move` has no terrain and stores a Berlin lat/lon target ⇒ its value is rewritten to the same local point under 0,0,0 | a Berlin default — ruled out; refuse to load — breaks every terrain without an origin |
| **C** ✅ *(⚠ as-built differs — §4 ①)* | ONE transform per node: `GeoOrigin` (Hrot.Core) — an `IGeographicTransform` that holds an IMMUTABLE `WGS84Transform` snapshot and swaps it atomically. `HrotNodeBuilder` creates it; the replication module, the network factories, `SimHostApp`, the runner and Stride take `Context.GeoTransform` instead of calling `CreateGeoTransform()` again; it is the world singleton too | call `SetOrigin` on each of today's instances — they are not all reachable, and `SetOrigin` is not thread-safe |
| **D** ✅ | `TerrainResidency.Commit` sets the origin (`GeoOrigin.Set(definition.Origin)`) — on every node, in the same cluster transaction that commits the terrain | a separate "set origin" message — a second protocol that can disagree with the terrain |
| **E** ✅ | `RecordingMetadata` gains `TerrainName` (with `CE-3118`) AND `GeoOrigin`; the recorder writes the node's current origin | name only — the user's case: the named terrain may be gone, or its origin edited since |
| **F** ✅ | the Replay Browser sets its `GeoOrigin` from the recording's metadata (authoritative for that recording), then loads the terrain by name if it still exists (`CE-3118`) | origin from the terrain file at replay time — wrong if the file changed after recording |

⚠ **What a switch does to in-flight data:** samples converted with the old origin and read with the new one land in the wrong place for
one frame. The switch only happens at a terrain commit, inside a scenario load that rebuilds the world anyway — so accepted, not engineered around.

## 3. UML

### Classes

```mermaid
classDiagram
  direction LR
  class IGeographicTransform { <<existing>> ToCartesian(lat,lon,alt) ToGeodetic(v) Origin }
  class WGS84Transform { <<existing>> SetOrigin is NOT thread-safe }
  class WGS84Transform { <<existing, changed>> immutable State swapped atomically; starts at 0,0,0 }
  class INetworkFactory { <<existing, grows>> +GeoTransform }
  class TerrainDefinition { <<existing, grows>> +Origin TerrainGeoOrigin? }
  class TerrainDefinitionParser { <<existing, grows>> reads origin; absent = null; malformed throws }
  class TerrainResidency { <<existing, grows>> Commit / Unload call ApplyGeoOrigin }
  class HrotNodeBuilder { <<existing, changes>> adopts ExternalGeoTransform or the factory's; publishes the world singleton }
  class RecordingMetadata { <<existing, grows>> +TerrainName +GeoOrigin GeoOriginRecord }
  class RecorderTickSystem { <<existing, grows>> StampWorld per keyframe }
  class ReplayBrowserSubsystem { <<existing, grows>> own WGS84Transform, origin from metadata }
  class DdsTranslators { <<existing>> hold IGeographicTransform }
  WGS84Transform ..|> IGeographicTransform
  INetworkFactory --> IGeographicTransform
  HrotNodeBuilder ..> INetworkFactory
  HrotNodeBuilder --> WGS84Transform
  TerrainResidency ..> TerrainDefinition
  TerrainResidency ..> IGeographicTransform : world singleton
  TerrainDefinitionParser ..> TerrainDefinition
  DdsTranslators ..> IGeographicTransform
  RecorderTickSystem ..> RecordingMetadata
  ReplayBrowserSubsystem ..> RecordingMetadata
  ReplayBrowserSubsystem --> WGS84Transform
```

*What the picture shows that prose hid:* every converter holds the interface, and on a node it is ONE object — the network factory's,
adopted by the builder and published as the world singleton — so the terrain commit reaches it through the world without any wiring.

### Sequence — a cluster load switches every node's origin together

```mermaid
sequenceDiagram
  participant O as Orchestrator (load transaction)
  participant T as TerrainLoadStep (each node)
  participant R as TerrainResidency
  participant G as World singleton IGeographicTransform
  participant X as DDS translators
  O->>T: prepare(terrain name)
  T->>R: Prepare (off-thread) reads terrain.json incl. origin
  O->>T: commit
  T->>R: Commit (main thread)
  R->>G: ApplyGeoOrigin, SetOrigin(definition.Origin or 0,0,0)
  Note over G: atomic State swap
  X->>G: ToGeodetic / ToCartesian (next frame, new origin)
```

*What it shows:* the origin rides the terrain's own two-phase commit — no node can hold the new terrain with the old origin.

### Modules — who creates it, who sets it, who reads it

```mermaid
graph TD
  NF[Network factory, runner / Stride] -->|holds| GO[the node's WGS84Transform]
  NB[HrotNodeBuilder, every node] -->|adopts + publishes| GO
  GO -->|Context.GeoTransform| REP[Replication module + network factories]
  GO -->|world singleton| WS[debug API, map pick, drag gizmo, AI nodes]
  GO -->|MapServices| MAP[map gizmos]
  TR[TerrainResidency.Commit, cluster load] -->|Set origin| GO
  REC[Recorder] -->|writes origin + terrain name| META[RecordingMetadata]
  META -->|SetOrigin| RGO[Replay Browser WGS84Transform]
  OLD[today: 6+ CreateGeoTransform calls per process]
  style OLD stroke:#c00,stroke-dasharray: 5 5
```

*What it shows:* the dashed box is what goes away — every extra `CreateGeoTransform()` on a node becomes a reference to the node's one transform.

## 4. As-built *(`CE-3126`, `2026-10-08`)*

| # | what was built | where |
|---|---|---|
| ① ⚠ **deviation from C** | ⛔ no `GeoOrigin` class. `WGS84Transform` itself now keeps everything derived from the origin in one immutable `State`, swapped by a single volatile write, and starts at 0,0,0 with valid matrices. ⭐ Reuse over build: that makes EVERY instance safe to switch, not only a new wrapper's — and a bare `new WGS84Transform()` no longer maps everything to the origin (its matrices used to be all zeros until `SetOrigin`) | `WGS84Transform.cs` |
| ② | one transform per node: `INetworkFactory.GeoTransform` (NED/BDC return theirs) → `HrotNodeBuilder` adopts `HrotNodeConfig.ExternalGeoTransform` ?? the factory's ?? a new one, and **publishes it as the world singleton on every node** (IG published none before). The replication extensions take `context.GeoTransform`; SimHostApp adopts the factory's and passes it as `ExternalGeoTransform`; the runner's cluster debug API passes `null` so it reads the active perspective's singleton | `HrotNodeBuilder.cs`, `HrotNodeBuilderReplicationExtensions.cs`, `SimHostApp.cs`, `ClusterRunner/Program.cs` |
| ③ | `TerrainResidency.ApplyGeoOrigin(world, origin, name)` sets the world singleton's origin — called by `Commit` (before the definition is published) and `Unload` (zeros). Missing origin ⇒ 0,0,0 plus one warning | `TerrainResidency.cs` |
| ④ | `HrotEnvironment.CreateGeoTransform()` returns a transform at 0,0,0 — **no Berlin in code**. The three shipped terrains carry `"origin": {52.52, 13.405, 0}` as data; `scenarios/test-move` (no terrain) has its move target rewritten to the same local point (489, 296) under 0,0,0 | `HrotEnvironment.cs`, `Recipes/Terrain/*/terrain.json`, `scenarios/test-move` |
| ⑤ | `RecordingMetadata.TerrainName` + `GeoOrigin` (`GeoOriginRecord`), stamped by `RecorderTickSystem.StampWorld` at each keyframe from the world singletons (the only production keyframe path) | `RecordingMetadata.cs`, `RecorderTickSystem.cs` |
| ⑥ | the Replay Browser owns a `WGS84Transform`, publishes it on every repo it binds, gives it to the map (`MapServices`, so mission lines now draw in replay), and sets its origin from the loaded recording's metadata; a recording without one ⇒ 0,0,0 plus a warning | `ReplayBrowserSubsystem.cs` |

| ⑦ | tests that author positions against Berlin without a terrain now SAY so: `HrotEnvironment.CreateGeoTransform(52.52, 13.405, 0)` (an explicit-origin overload; 24 test call sites). The unread `IgNetworkConstants.GeoOrigin*` Berlin constants are deleted | test projects, `IgNetworkConstants.cs` |

⚠ **Left, flagged:** `NodeConfiguration.GeodeticOrigin` (a config default, Tel Aviv) is read by nothing but its own config tests — a second origin source in name only. Lean: retire it with the next SimHost config change; not removed here (no rush removals).

⚠ **Not done here:** loading the recording's terrain BY NAME in the Replay Browser is `CE-3118` — the metadata now carries the name it needs.
