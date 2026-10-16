<!--STATUS
state: LIVE
updated: 2026-10-16
build-state: DESIGN — all decisions RULED by the user 2026-10-16 (§0, R-4020…R-4029) except two follow-ups with leans
  (§4 E2b flat nets, E7b no LFS). Nothing is dispatched; §6 is the slice plan.
current-answer: §0 (the approved decisions) over §3 (the diagrams); §4 the sub-decisions (ruled + the two open); §5 the known limits.
stale-below: nothing. ⚠ The first chat draft (2026-10-16) published SUMO cars through NED as Lightweight entities and
  carried traffic lights as door-like entities — both SUPERSEDED the same day by R-4020; recorded in §7 HISTORY only.
known-rot: none.
known-conflict: none. ⚠ Relation to §2.1e ① of DESIGN_Terrain_Zones_And_Assets.md (the scenario names only a terrain) —
  CONSISTENT: the SUMO net is a terrain asset (R-4021); only the traffic DEMAND is a scenario entity.
related-designs:
  - docs/DESIGN_Terrain_Zones_And_Assets.md — owns WHAT a terrain asset is (§2.1a: the road net is an external asset,
    "like sumo road net"; §2.1e: terrain.json, roadNetworks[]). This question adds a second road-net FORMAT to that slot.
  - docs/DESIGN_Terrain_World.md — owns what a terrain CONTAINS and the one-file-every-query-derived rule; §7.3 W12 the
    terrain FOLDER and TerrainCatalog that distribute the .net.xml to every node (R-4021).
  - docs/DESIGN_Cluster_Load_Phase.md — owns WHEN terrain loads and the role→load-part contract (terrain is universal);
    the traffic node's SUMO load runs inside that step.
  - docs/DESIGN_Geo_Origin.md — owns the terrain's geo origin (R-229); §4 E1 here maps the SUMO net's UTM frame onto it.
  - docs/designs/navig-2/Navigation_Design_v2_0.md — §5.2a owns the road GRAPH our own units route on (R-230/R-231).
    ⛔ R-4026: it stays SEPARATE from and independent of the SUMO net — no derivation either way.
  - docs/DESIGN_Entity_Control.md — M-6 rejected moving a unit's ownership to a controlling station; R-4023 (the
    handover by a follow mode) follows that precedent. M-1's KinematicsMode.Manual is the shape KinematicsMode.Traffic copies.
  - docs/DESIGN_Entity_Ownership_Transfer.md — TransferEntityOwnershipRequest: the REJECTED handover alternative (§0 D7).
  - docs/DESIGN_Dead_Reckoning.md — owns NED's dead reckoning; SUMO cars use SumoSharp's lane-based dead reckoning
    instead (R-4020), so the R6 caveat there (no DR of Active replicas on Brain/Muscle) does not reach them.
  - docs/DESIGN_Node_Roles_And_Policies.md — owns the roles; R-4022 adds a Traffic role (R-138: a convention, not a
    protocol restriction — a non-muscle node may own a pose).
  - docs/DESIGN_Map_3D_Mode.md — owns the 3-D map mode and its ground draping (§6f), which the traffic road layer draws through.
  - docs/blueprints/Architect_Question_91_Player_Nodes.md — R-4012 (replay by span) and R-4007 (ingress frozen while
    restoring), which the mirror module obeys (§4 E5).
  - External: github.com/pjanec/SumoSharp — docs/SUMOSHARP-API.md, docs/PACKAGES.md, docs/SUMOSHARP-DEADRECKONING.md,
    docs/EXTERNAL-AGENTS-VIZ.md, docs/PEDESTRIANS.md, demos/City3D (CityLib).
-->

# Architect Question 93 — **a SUMO traffic node: SumoSharp's own wire, local mirrors everywhere, roads from the terrain**

🔒 **User, `2026-10-16`, verbatim:**
- *"integrate the ground traffic simulation … into the engine as a new type of node, which is running the traffic simulation and publishing the entities on the network so that the other nodes can see them"* · *"The sumo car entities would have no brain. They will be fully controlled by the sumo node."* · *"would be just adopted by all the other nodes as unowned, completely unowned."*
- *"the sumo sharp repository also handles pedestrians. It would be nice to use them as well."*
- *"The vehicles and entities from our own engine should be exposed back to the Sumo simulation."*
- *"take our vehicle and hand it over to the Sumo node so Sumo can control its navigation over its own road graph and be able to take the ownership back to our simulator."*
- *"whether it makes sense to integrate the Sumo engine into our muscle node completely"*
- *"It is essential that every node that receives the cards from the sumo node does dead reckoning, because the sumo cards are sent only on a significant change."* · *"The sumo node must use the optimized dead reckoning that the sumo sharp project is using, because this is reducing network traffic tremendously."*
- *"Your idea of referencing the sumo road network from the terrain is correct. Sumo scenario parameters being part of our scenario as an entity is also accepted. The rendering of the roads and traffic lights should be inspired by the one from the CityLib."*
- *"because we need to use the optimized DDS network scheme for sumo sharp cars, we do not need to solve the issue with sending thousands of cars using our DDS scheme."*
- *"A local entities ok, B ok, C ok, D1 and D7 ok."*
- *"we can store the sumo road network as part of the terrain and synchronize this the same way as the terrain."*
- *"E1 ok. E2 no,.keep height from sumo, as roads are rendeded from sumo net. E3 no, keep them separate and independent, sumo ones are complex, ours are simple. E4 ok.E5 ok. To ensure unique network ids, sumo could use our allocator. E5 ok. E7 packages ok, just need to be compiled and stored as part of hrot repo (lfs?) as they are not on nuget.org. E9 ok. E10 ok."*

## 0. The approved decisions

| id | decision | ruling |
|---|---|---|
| **D-wire** | ⭐⭐ SUMO cars (and traffic lights) travel ONLY on SumoSharp's own DDS topics (`sumo/vehicles`, `sumo/lifecycle`, `sumo/tl`), published with its DR-error policy (`DrErrorPublishPolicy`: 0.3 m along / 0.2 m lateral / 3 s heartbeat). **Every receiving node dead-reckons with SumoSharp's `DrExtrapolation` curve** — the one the publisher's policy also uses. ⛔ Never through NED: no SUMO car is a networked HROT entity, so "unowned" needs no ownership mode at all | **R-4020** |
| **D3** | ⭐⭐ The SUMO net (`.net.xml`) is a **terrain asset**, named by `terrain.json` `roadNetworks[]` and synchronised with the terrain folder. ⛔ `sumo/geometry` is not needed. The traffic **demand** (density, flows/routes, seed, pedestrians on/off) is a **scenario entity** | **R-4021** |
| **D1 + A + B + C** | ⭐⭐ A **separate traffic node** (an ECS host with a new `Traffic` role) runs SumoSharp, whose engine keeps its own data as the truth. **A** — every receiving node keeps one **local-only** ECS entity per car (pose + collider; never published, never saved) so the map, line of sight, avoidance and picking see traffic. **B** — the dead-reckoning clock is the **cluster sim time** (`GlobalTime`), and the traffic node steps SUMO on that same clock. **C** — the lanes dead reckoning needs come from the receiver's own parse of the terrain's net file, checked against the publisher by a **net hash** | **R-4022** |
| **D7** | ⭐⭐ Handing one of our vehicles to SUMO is a **follow mode, not an ownership move**: SimHost keeps ownership; SUMO drives a shadow car named `hrot:<networkId>`; SimHost's new `KinematicsMode.Traffic` follows its dead-reckoned pose; leaving the mode takes it back. ⛔ Rejected: `TransferEntityOwnershipRequest` of the kinematic group — Entity Control M-6 already rejected an ownership move for control; on a traffic-node crash the keys return to the creator (CGF, no kinematics) and the vehicle freezes; on hand-back SimHost resumes from stale `VehicleState`/`NavState` | **R-4023** |
| **D4** | ⭐ Roads and traffic lights are drawn by one new map layer **styled on SumoSharp's CityLib** (lane ribbons, lane markings, crosswalks, signal heads), drawn through the shared gizmo seam so all five map hosts get it | **R-4024** |
| **E1 + E2** | ⭐ SUMO coordinates map onto ours UTM → WGS84 → local ENU through the terrain origin (E1). **Heights come from SUMO** — the roads are rendered from the SUMO net, so the cars ride the same lane z (E2; our lean "our ground is the height" was REJECTED) | **R-4025** |
| **E3** | ⛔ Our own road graph (CarKinem `RoadNetworkBlob`, `roads.json`) stays **separate and independent** of the SUMO net — SUMO's is complex, ours is simple; no derivation either way | **R-4026** |
| **E4 + E5 + E9 + E10** | our entities reach SUMO as lane obstacles + crowd discs (E4); each node records its local cars, the traffic node does not step SUMO in replay (E5); received samples are stamped with SUMO sim time (E9); the traffic node is an optional cluster member (E10) | **R-4027** |
| **E7** | SumoSharp is consumed as its two NuGet packages, **compiled from a pinned SumoSharp commit and stored in the HROT repo** (they are not on nuget.org) | **R-4028** |
| **E11** | every SUMO car gets a cluster-unique **network id from our allocator** (`DdsIdAllocator`), carried on SumoSharp's lifecycle record, so a car is the same id on every node | **R-4029** |

## 1. INVENTORY *(2026-10-16 — codebase-memory `search_graph` (MCP + CLI), grep, three read-only sweeps; `check_index_coverage` NOT run)*

| query | total | result |
|---|---|---|
| HROT `search_graph name_pattern=".*(Road\|Traffic\|Sumo).*" label=Class` | **38** | the CarKinem road graph (`RoadNetworkBlob`/`Builder`/`Holder`/`Json`/`Loader`, `RoadGraphNavigator`, `RoadGraphRouter`, `RoadHeights`), `RoadNetworkGizmo`, `RoadNetworkMigrationModule`; ⛔ **no SUMO or traffic production class** — `TrafficBrainSystem` is in `FDP/Examples` only, `TrafficLightExample` is an FHSM sample |
| HROT `search_graph ".*(Unowned\|Ownerless\|NoOwner\|ForeignEntity\|External(Entity\|Sim)).*"` | **12** | tests only — ⛔ no ownerless-entity type exists; `EntityMaster` has no owner field (`GenericDescriptors.cs:120-122`) |
| HROT `search_graph ".*(LocalOnly\|NotReplicated\|NoEgress\|LocalEntity).*"` + grep of egress queries | 17 | ⭐ egress is gated on `NetworkIdentity`: `GeoSpatialEgressTranslator.cs:121-123`, `EntityMasterEgressTranslator.cs:62-63` ⇒ an entity created **without** `NetworkIdentity` is local-only by construction (R-4022 A) |
| sumosharp `search_graph ".*(External\|Agent\|Injector\|Obstacle).*" label=Class file=src/**` | **13** | `ExternalObstacle` (lane-relative, `Sim.Core/ExternalObstacle.cs:54`), `ObstacleStore`, `ObstacleHandle` (32+16, a distinct id space), `OrcaSolver.Agent`, `ShapedVoSolver.ShapedAgent` |
| sumosharp grep — receive side | — | `DdsSubscriber : IReplicationSource` (`Sim.Replication.Dds/DdsSubscriber.cs:22`) · `DrClock.Pump` / `Resolve` / **`ResolveAt(history, sampleT, lanes)`** (`Sim.Viewer.Motion/DrClock.cs:145,295,317`) · `DrExtrapolation.Arc` (`Sim.Replication/DrExtrapolation.cs`) · `NetworkLaneSource` (`Sim.Core`) |
| sumosharp grep — publish side | — | `ReplicationPublisher.PublishStep(snap, sink)` (`Sim.Host/ReplicationPublisher.cs:62`), `DdsReplicationSink`, topic names `DdsTopicNames.cs:51-70` |
| sumosharp grep — engine | — | `Engine.LoadNetwork`/`LoadScenario` (`Engine.cs:1562,1303`), `SpawnVehicle(…departPos, departSpeed, departLane)` (`:2877`), `Despawn` (`:2963`), `AddMovingObstacle`/`UpdateObstacle` (`:1235-1262`), `CrowdSource : ICrowdFootprintSource` (`:783`), `SimulationRunner` (manual `Tick()`), `NetworkParser.Parse` (`Sim.Ingest/NetworkParser.cs:17`), `LaneProjection.Project` (`Sim.Ingest/LaneProjection.cs:19`) |
| CityLib | — | `demos/City3D/CityLib`: `RoadMeshBuilder`, `LaneMarkingBuilder`, `CrosswalkBuilder`, `TrafficLightPlacer` — engine-agnostic net8.0, Godot axes, NOT packaged |
| DDS binding | — | both repos pin **`CycloneDDS.NET` 0.3.2** (HROT 25 csproj refs; `SumoSharp.Dds` csproj :36) ⇒ one native runtime in the process |

## 2. Claim table — what the decisions rest on

| claim | code — how it IS | design — how it was MEANT |
|---|---|---|
| a non-muscle node may own a pose | ✅ `CarKinematicsSystem.cs:80` gates physics on `WithOwned<SimTransform>`, not on role | ✅ R-138 — "SimHost owns SimTransform" is an outcome, not a rule |
| a local entity without `NetworkIdentity` is never published | ✅ egress queries above | ⛔ searched `docs/`, no design names it — ⚠ it is a property of the queries; a rail must pin it (§6 slice 3) |
| the SUMO net can ride the terrain | ✅ `TerrainDefinition.RoadNetworks` (`TerrainDefinition.cs`), `TerrainCatalog` + `AssetTreeSync` mirror the folder (`StorageGatewayModule.cs:446-476`) | ✅ Terrain_Zones §2.1a/§2.1e, Terrain_World §7.3 W12 |
| receivers can dead-reckon on cluster time without a wall-clock fit | ✅ `DrClock.ResolveAt(history, sampleT, lanes)` takes the time explicitly; ⚠ `DdsSubscriber` stamps samples with the DDS **SourceTimestamp** (wall clock) | ⛔ SumoSharp's DEADRECKONING doc §8 paces from wall-clock arrival by design ⇒ §4 E9 |
| SUMO keeps a car's insertion safe | ✅ `SpawnVehicle` queues until a gap (`VehicleLifecycle.cs:122-128`) | ✅ TUTORIAL-VEHICLES |
| only the FIRST terrain road network loads today | ✅ `TerrainResidency.cs:214-232` | ⚠ Terrain_World §2 lists `roadNetworks[]` as a list ⇒ §5 |

## 3. Diagrams

### 3.1 Classes

```mermaid
classDiagram
  direction LR
  class TerrainDefinition { <<existing>> +RoadNetworks }
  class TerrainResidency { <<existing, grows>> Prepare parses net.xml }
  class TrafficNetCache { <<NEW singleton>> +NetworkModel Model +string NetHash +Transform ToLocal }
  class TrafficDemand { <<NEW component, scenario entity>> +DemandFile +Density +Seed +Pedestrians +StepLength }
  class NodeRole { <<existing flags, grows>> +Traffic }
  class NedReplicationModule { <<existing, grows>> admits Traffic }
  class TrafficNodeModule { <<NEW, traffic node>> steps Engine on GlobalTime, publishes }
  class TrafficObstacleFeed { <<NEW, traffic node>> our replicas to obstacles and discs }
  class TrafficHandoverSystem { <<NEW, traffic node>> intent to SpawnVehicle or Despawn }
  class TrafficMirrorModule { <<NEW, every receiving node>> DR at GlobalTime, local entities, IReplayRestoreGated }
  class TrafficCar { <<NEW component, local only>> +long NetworkId +VehicleHandle +byte Kind }
  class TrafficRoadGizmo { <<NEW, map hosts>> ribbons markings crosswalks signal heads }
  class KinematicsMode { <<existing enum, grows>> +Traffic }
  class CarKinematicsSystem { <<existing, grows>> Traffic follows the hrot shadow car }
  class IReplayRestoreGated { <<existing>> }
  class IGlobalStatelessGizmo { <<existing>> }
  class Engine { <<SumoSharp>> LoadNetwork SpawnVehicle Despawn AddMovingObstacle }
  class SimulationRunner { <<SumoSharp>> Tick }
  class ReplicationPublisher { <<SumoSharp>> PublishStep }
  class DrErrorPublishPolicy { <<SumoSharp>> }
  class DdsReplicationSink { <<SumoSharp.Dds>> }
  class DdsSubscriber { <<SumoSharp.Dds>> IReplicationSource }
  class DrClock { <<SumoSharp>> ResolveAt }
  class NetworkParser { <<SumoSharp>> Parse }
  class LaneProjection { <<SumoSharp>> Project }
  class CityLibBuilders { <<SumoSharp demo, ported>> RoadMesh LaneMarking Crosswalk TrafficLightPlacer }
  TerrainResidency --> TerrainDefinition
  TerrainResidency ..> NetworkParser
  TerrainResidency --> TrafficNetCache : publishes
  TrafficNodeModule ..> TrafficNetCache
  TrafficNodeModule ..> TrafficDemand : reads replica
  TrafficNodeModule --> SimulationRunner
  SimulationRunner --> Engine
  TrafficNodeModule --> ReplicationPublisher
  ReplicationPublisher --> DrErrorPublishPolicy
  ReplicationPublisher ..> DdsReplicationSink
  TrafficObstacleFeed ..> LaneProjection
  TrafficObstacleFeed ..> Engine
  TrafficHandoverSystem ..> Engine
  TrafficMirrorModule --> DdsSubscriber
  TrafficMirrorModule --> DrClock
  TrafficMirrorModule ..> TrafficNetCache
  TrafficMirrorModule ..|> IReplayRestoreGated
  TrafficMirrorModule --> TrafficCar : creates local entities
  TrafficRoadGizmo ..|> IGlobalStatelessGizmo
  TrafficRoadGizmo ..> TrafficNetCache
  TrafficRoadGizmo ..> DdsSubscriber : sumo/tl
  TrafficRoadGizmo ..> CityLibBuilders
  CarKinematicsSystem ..> KinematicsMode
  CarKinematicsSystem ..> TrafficMirrorModule : shadow pose
  NedReplicationModule ..> NodeRole
```

*What the picture shows that prose hid:* every NEW box sits on an existing seam — the terrain step parses the net once
(`TrafficNetCache`) and three consumers read it (the SUMO engine, the mirror's dead reckoning, the road layer); the only
networked HROT data the traffic node READS are replicas it already receives (the demand entity, our vehicles, their intents);
and nothing the traffic node WRITES goes through NED.

### 3.2 Sequence — one live step

```mermaid
sequenceDiagram
  participant GT as GlobalTime (cluster sim time)
  participant TN as TrafficNodeModule
  participant OF as TrafficObstacleFeed
  participant E as SumoSharp Engine
  participant P as ReplicationPublisher
  participant W as sumo topics (DDS)
  participant M as TrafficMirrorModule (each node)
  participant R as local TrafficCar entities
  GT->>TN: frame, sim time t
  TN->>OF: our replicas this frame
  OF->>E: UpdateObstacle (vehicles, lane-projected), crowd discs (infantry)
  loop while SUMO time + dt <= t
    TN->>E: Tick (fixed dt)
  end
  TN->>TN: on Departed, take a network id from our allocator (R-4029)
  TN->>P: PublishStep(snapshot)
  P->>W: only cars whose DR error exceeds the policy, plus lifecycle and TL changes
  W-->>M: samples stamped with SUMO sim time
  M->>M: DrClock.ResolveAt(history, t, lanes from TrafficNetCache)
  M->>R: create on Departed, write SimTransform, destroy on lifecycle dispose
```

*What the picture shows that prose hid:* SUMO advances only to the cluster's sim time, so pause and speed-up need no
SumoSharp change on the publisher; and the receiver resolves poses at the SAME `t`, so a paused cluster freezes traffic on
every node.

### 3.3 Sequence — handing our vehicle to SUMO and back (R-4023)

```mermaid
sequenceDiagram
  participant B as CGF brain
  participant N as NED (replicas)
  participant S as SimHost CarKinematicsSystem
  participant T as TrafficHandoverSystem
  participant E as SumoSharp Engine
  participant M as SimHost TrafficMirrorModule
  B->>N: NavigationIntent mode Traffic, destination
  N-->>S: intent, so KinematicsMode.Traffic, hold position
  N-->>T: intent on the replica
  T->>T: stop feeding this entity as an obstacle
  T->>E: SpawnVehicle id hrot:netId, route to destination, departPos, departSpeed, departLane
  E-->>M: lifecycle and frames for hrot:netId (via sumo topics)
  M-->>S: dead-reckoned shadow pose
  S->>S: write SimTransform (SimHost still owns it), NED publishes as usual
  alt SUMO reports Arrived, or the brain changes the intent
    T->>E: Despawn
    E-->>M: lifecycle dispose of hrot:netId
    M-->>S: shadow gone, so leave Traffic mode, NavigationStatus Arrived
  end
```

*What the picture shows that prose hid:* ownership never moves — the only new edge into SimHost is a pose it FOLLOWS, the
same shape as M-1's manual drive; and the shadow car's id carries our entity id, so the renderers' traffic layer skips
`hrot:*` cars and the vehicle is never drawn twice.

### 3.4 Modules — who registers it, who calls it each frame

```mermaid
graph TD
  subgraph TrafficNode["Traffic node (new ECS host, role Traffic)"]
    TLS1[TerrainLoadStep] --> TNC1[TrafficNetCache]
    TNM[TrafficNodeModule] -->|ModuleHost tick| SR[SimulationRunner.Tick]
    TOF[TrafficObstacleFeed]
    THS[TrafficHandoverSystem]
    NED1[NedReplicationModule admits Traffic]
  end
  subgraph SimHost
    TLS2[TerrainLoadStep] --> TNC2[TrafficNetCache]
    TMM2[TrafficMirrorModule] -->|shadow pose| CKS[CarKinematicsSystem Traffic mode]
  end
  subgraph MapHosts["CGF, Editor, Player, IG raylib, Replay browser"]
    TLS3[TerrainLoadStep] --> TNC3[TrafficNetCache]
    TMM3[TrafficMirrorModule]
    MIP[MapInteractionPack] -->|Draw each frame| TRG[TrafficRoadGizmo]
  end
  subgraph Stride["Stride app"]
    DEAD1[no TerrainLoadStep, flat slab]
    DEAD2[no TrafficRoadGizmo]
  end
  TNM -.->|sumo/vehicles lifecycle tl| TMM2
  TNM -.->|sumo/vehicles lifecycle tl| TMM3
  style DEAD1 fill:#f66,color:#000
  style DEAD2 fill:#f66,color:#000
```

*What the picture shows that prose hid:* the red boxes — **Stride runs no terrain step** (`StrideHrotGame.cs:1006-1009`, a
flat slab) and no map gizmo pack, so Stride shows no roads and no traffic until Stride gets terrain (AQ81 T8). The other
edge the picture forces: `TrafficMirrorModule` is ticked by each host's ModuleHost, so a host that does not compose it sees
no traffic — the composition list in §6 slice 3 is the place that decides it.

## 4. Open sub-decisions — each with a lean *(not yet ruled)*

| id | question | ⭐ lean | rejected — one line each | blast radius |
|---|---|---|---|---|
| **E1** | coordinates | ✅ **RULED (R-4025)** — at terrain load, convert the net's `<location>` (`netOffset` + `projParameter`) **UTM → WGS84 → local ENU** through the terrain origin (R-229), fit once as a 2-D rigid transform in `TrafficNetCache.ToLocal`; `+proj=utm` only, others refused with a load error | identity: wrong for any georeferenced net · a projection library: a large dependency for one projection | new UTM converter (~100 lines); SumoSharp's parser must read `<location>` (it ignores it today) |
| **E2** | car and road height | ✅ **RULED (R-4025)** — **SUMO's z**: roads are drawn at the lane z and cars ride `PosZ`. ⛔ The lean (our `IWorldQuery.SurfaceZ`) was REJECTED: the roads come from the SUMO net, so the cars must match them | — | mirror module; ⚠ consequence in §5 |
| **E2b** | ⚠ OPEN — a **flat (2-D) net** has no z (`Lane.ShapeZ` null ⇒ `PosZ` 0) | ⭐ when the net carries no z, BOTH roads and cars **drape onto our ground** — the 3-D map's existing rule (Z = 0 is "drape", `GizmoRenderer3D` M18/P4) for the roads, `IWorldQuery.GroundHeightAt` for the cars; a net WITH z is used as-is (R-4025) | cars and roads at z 0: everything sinks into or floats above any non-flat terrain | mirror module + road layer |
| **E3** | our units on SUMO roads | ⛔ **RULED NO (R-4026)** — the two road systems stay separate and independent | derive `RoadNetworkBlob` from the net (our lean) — rejected: SUMO's model is complex, ours is simple | none |
| **E4** | how our entities reach SUMO | ✅ **RULED (R-4027)** — vehicles → `AddMovingObstacle` after `LaneProjection.Project` (≤ 3 m from a lane, else ignored); infantry and every off-lane unit → crowd discs (`ICrowdFootprintSource`), which cars and pedestrians both react to | discs only: cars would not queue behind a convoy · obstacles only: off-lane infantry crossing a road invisible | traffic node only |
| **E5** | replay | ✅ **RULED (R-4027)** — each node's recorder records its local `TrafficCar` entities (R-4012 Plan A); in replay the mirror module is gated like ingress (`IReplayRestoreGated`) and the traffic node does not step SUMO. Known limit: a node OUTSIDE its recording span shows no cars (mirrors are not flooded) | SumoSharp's `.simrec` replayed onto the topics: a second recording mechanism, and its seek re-reads from the start | mirror module; recording size to measure |
| **E6** | pedestrians | phase 2, same pattern: SumoSharp's 4 pedestrian topics, local mirror entities for **promoted** pedestrians only (full collision avoidance, the ones cars see), realism zone around our units; ambient pedestrians drawn from their route-seed-time function, no entities | all pedestrians as entities: 20 000 entities per node for background walkers · now, in slice 1: pedestrians are SumoSharp's real-time bottleneck (7–15 steps/s on 4–8 cores) | phase 2 |
| **E7** | how SumoSharp enters the build | ✅ **RULED (R-4028)** — the packages `SumoSharp` + `SumoSharp.Dds`, **built from a pinned SumoSharp commit and committed into HROT's existing local feed `nugets/`** (`nuget.config` already declares `LocalFeed = ./nugets`); a script `scripts/update-sumosharp.sh` packs them with a version that names the commit (⚠ SumoSharp's own pack defaults to `0.1.0` every time, which a stale NuGet cache silently reuses); licence `EPL-2.0 OR GPL-2.0-or-later` | a git submodule / project reference: 5 000 files and a second build system · a CI feed: SumoSharp is not published anywhere | build only; `.gitignore` gains `!nugets/SumoSharp*.nupkg` (it ignores `*.nupkg` today) |
| **E7b** | ⚠ OPEN — Git LFS for the `.nupkg` files? | ⭐ **plain git, no LFS.** HROT uses no LFS today (no `.gitattributes`, `git lfs ls-files` empty); cloud sessions clone with LFS smudging skipped, so an LFS package would arrive as a pointer stub and every cloud build would fail restore. The packages are small managed assemblies (the native DDS runtime comes from `CycloneDDS.NET` on nuget.org, which HROT already uses) — ⚠ size not measured yet; if one exceeds ~10 MB, revisit | LFS: breaks restore in every session that skips LFS, and adds a tool to every clone | `.gitignore`, `nugets/` |
| **E8** | `sumo/geometry` | OFF (R-4021); the publisher still exposes the net hash (on `sumo/status`) for C's check | keep it on as a fallback: two sources for the same lanes | publisher config |
| **E9** | the receiver's time stamps | ✅ **RULED (R-4027)** — stamp each sample with the frame's own **SUMO sim time** (= cluster sim time, §3.2), not the DDS SourceTimestamp — a small SumoSharp option on `DdsSubscriber` | keep wall-clock stamps and fit a rate: breaks on pause, speed-up and replay | one SumoSharp change |
| **E10** | traffic-node id and lifetime | ✅ **RULED (R-4027)** — an ordinary cluster member (master-assigned id, R-4009), **optional** in `ClusterConfiguration` — if it dies, traffic simply stops (lifecycle disposes reach every mirror; a mirror whose feed falls silent for N s clears its cars) | mandatory: a crashed traffic node would fail the exercise | orchestrator config |
| **E11** | unique ids for SUMO cars | ✅ **RULED (R-4029)** — the traffic node takes one network id per car from **our allocator** (`DdsIdAllocator`, chunks of 100, `DdsIdAllocator.cs:31-33`) and the id rides SumoSharp's lifecycle record (its `Name`, or a new numeric field if a flow-spawned car cannot be named — a small SumoSharp change); the mirror stores it in **`TrafficCar.NetworkId`, ⛔ NEVER in `NetworkIdentity`** — egress publishes every entity that has one (`GeoSpatialEgressTranslator.cs:121-123`). Shadow cars (R-4023) carry the handed vehicle's own id | SumoSharp's `VehicleHandle` as the id: a separate 32+16 space whose slots are recycled, and different on every node's mirror | traffic node + mirror; ⚠ a system that resolves a network id through NED's entity map will not find a mirror — a follow-up when the first such reader appears |

## 5. Known limits *(measured unless marked)*

- Traffic lights carry ONE state per approach lane (`Engine.cs:2613-2637`): a separate left-turn arrow is lost.
- No brake lights or turn signals in SumoSharp (searched `src`, none); `Acceleration` and the lane-change phase byte are the only inputs.
- `CivilianCar` (TKB 1002) has no `Health`: a hit does nothing (`CombatTkbTranslator.cs:49-53`). Destroying a SUMO car is a later question (it would need `Despawn` on the traffic node).
- `TerrainResidency` loads only the FIRST entry of `roadNetworks[]` (`:214-232`).
- ⚠ R-4025's consequence: with SUMO's z, roads and cars sit where the NET says, which can differ from our height grid where the net's z was not built from it — a road may hover over, or sink into, our terrain mesh. A terrain must ship a net whose z matches its height grid (or a flat net, E2b).
- *(Inference, not run)* `RoadNetworkGizmo` emits Z = 0 (drape) and so would draw a bridge road on the ground below it; the new layer must use the lane z on bridges.
- The vehicle-type id is always 0 on the wire (`ReplicationPublisher.cs:84-88`): car / bus / truck models need the type carried — lifecycle carries the SUMO id and dims only.

## 6. Slice plan *(not dispatched)*

| slice | content | acceptance |
|---|---|---|
| 1 | traffic node host + role (`NodeRole.Traffic`, `NedReplicationModule` admits it, ClusterRunner `--mode traffic`); `TrafficNetCache` from `terrain.json`; `TrafficDemand` scenario entity; SUMO stepped on `GlobalTime`; publisher on `sumo/*` | `ddsmonitor` shows `sumo/vehicles` samples only on DR error; pause stops them |
| 2 | `TrafficMirrorModule` on every map host (E9 stamps, hash check) + `TrafficRoadGizmo` (CityLib-styled) | a car on IG matches the traffic node's own pose within the policy's 0.3 m |
| 3 | local entities carry `SimTransform` + `PhysicsCollider` (R-4022 A); rail: a mirror entity is never egressed; SimHost avoidance and LOS see cars | our vehicle stops behind a SUMO car; a rail fails if a `TrafficCar` entity gains `NetworkIdentity` |
| 4 | E4 obstacle feed | a SUMO car brakes for our convoy on its lane |
| 5 | R-4023 handover (`KinematicsMode.Traffic`) | a vehicle handed over arrives at its destination on SUMO roads and is taken back; ownership never changes |
| 6 | E6 pedestrians | promoted pedestrians near our units react to them; ambient ones are drawn without entities |

## 7. ⛔ HISTORY

- 2026-10-16, first chat draft — SUMO cars as NED entities with `EntityMasterFlags.Lightweight` (owned by the traffic node,
  type `CivilianCar`), traffic lights as door-like terrain-object entities, the network cost of thousands of per-entity
  samples as the main unknown. ⛔ SUPERSEDED by R-4020: SumoSharp's own wire carries cars and lights, NED carries none.
