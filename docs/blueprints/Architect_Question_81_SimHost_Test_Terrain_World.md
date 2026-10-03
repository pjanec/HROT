<!--STATUS
state: LIVE
updated: 2026-10-03
build-state: DESIGN — ✅ T1–T10 APPROVED (user, 2026-10-03); the WHAT goes into a DESIGN doc with the UML before any build.
current-answer: §0 (the APPROVED decisions and the first slice); §1 the INVENTORY; §2 the claim table.
stale-below: nothing — new document.
known-rot: nothing yet.
known-conflict: docs/DESIGN_Node_Roles_And_Policies.md §3.2 and docs/DESIGN_Cluster_Load_Phase.md §4.1a give terrain to MuscleGround + NavigationSolver only; T4 adds Perception (the reopen trigger §3.2 itself names). Both must be updated when the design lands.
related-designs:
  - docs/DESIGN_Terrain_Zones_And_Assets.md — owns WHAT terrain is (a named asset with a JSON definition, §2.1e) and the asset build for static obstacles (§2.1c); this question fills in the content it postponed (§7) and extends the definition.
  - docs/blueprints/Architect_Question_71_Terrain_Zones_And_The_Asset_Build.md — the WHY of zones and the asset build; ruled the heavy asset semantics POSTPONED (§5), which is what this answers.
  - docs/DESIGN_Cluster_Load_Phase.md — owns WHEN terrain loads and WHICH ROLES load it (§4.1a); T4 extends the role list.
  - docs/DESIGN_Node_Roles_And_Policies.md — owns the role→data table (§3.2) that T4 reopens.
  - docs/designs/navig-2/Navigation_Design_v2_0.md — owns the navigation contract (solver on the Muscle, per-layer navmesh, INavmeshProvider); T3 supplies the headless navmesh it assumed.
  - docs/DESIGN_Subsystem_Composition_Unification.md — owns CE-210 (the 3-D LOS seam, §4.1ab) that T4 builds, and B5 (role composition) under which the solver gets composed (CE-3006).
  - docs/designs/eqs-2/EQS_Design_v1.3_final.md — owns cover and the 2.5D occluder grid (an optional accelerator, not this model).
  - docs/designs/promote-to-3d/3D_Cognitive_Spatial_Awareness_Promotion_Design_v1_1.md — owns "SimTransform.Position.Z is authoritative", which T5 relies on.
  - docs/DESIGN_Stride_Node_Modes.md — owns the Stride host's scene-baked navmesh; T8 makes it render the same world file later.
-->

# Architect Question 81 — **what terrain does a SimHost hold, so navigation and perception work for behaviour tests?**

Tracker: [`CE-3006`](Blueprint_Issues_Tracker.md) (the POC navigation solver, which waited on this). Ledger: `R-181`.

## 0. The decisions — ✅ **APPROVED** *(user, `2026-10-03`)*

> 🔒 **User, verbatim (the requirement):** *"For testing behaviors, i need both navugation and perception to work in
> simple terrain representation, like a 2d-like world simulation a real 3d world. I need obstacles (with height),
> roads, open areas etc. I need static ibstacles (part of terrain) as well as dynamic one (runtime placeable
> buildings, other vehicles). I need the terrain to be loadable from file, simple standard text format you can
> handle (author and manage) yourself. Urban environments with multi-story building or multi level garages would be
> good to support. Terrain asset management handling was designed and not yet implemented."*
>
> 🔒 **User, verbatim (the approval):** *"Local meters are welcome, small numbers over large ones. Perceception need
> terrain of course. Placed building baked in ok. 1‐3 ideal first slice. 2d map gizmo that draws this world is
> required."*

**The one rule:** ⭐ **one world file, one in-memory world model, and every query derived from it** — navmesh,
ground height, line of sight and the 2D map drawing all read the same primitives, so they cannot disagree.

| # | sub-question | ✅ decision | rejected (one fact each) |
|---|---|---|---|
| **T1** | geometry model | **2.5D primitives**: ground (flat or heightfield), **prisms** (footprint polygon + base Z + height), **slabs** (walkable floor polygons at a Z), **ramps** (polygons with per-vertex Z), **surface areas** (road / open / forest / water). A garage = slabs + ramps | triangle mesh (OBJ): not hand-authorable, carries no meaning · voxel grid: large, multi-level awkward |
| **T2** | file format | **GeoJSON FeatureCollection in LOCAL METRES** (small numbers, user-approved), feature `properties` carry `kind`/`baseZ`/`height`/`surface`…; ramps and slabs use GeoJSON's optional 3rd coordinate for Z. Optional heightmap as ESRI ASCII grid. Referenced from the existing terrain definition JSON (schema v2) | NavTestMap JSON: it is DERIVED data (a navmesh) and cannot drive height or LOS · custom ASCII-art grid: cannot carry heights or floors |
| **T3** | navigation | **Headless DotRecast**: the provider, crowd and baker move from `Hrot.Stride.Core` to an FDP toolkit (they have NO Stride dependency); a terrain-world geometry source implements the existing `ISceneGeometrySource`; SimHost's NavigationSolver role composes the path solver (`CE-3006`, then `CE-524`). One implementation, both hosts | `FakeNavmeshProvider` polygons: a 2nd implementation, whole-polygon blocking only · a new grid A*: another pathfinder, no multi-level |
| **T4** | perception | **Build `CE-210`'s 3-D LOS seam with a terrain-world strategy** (segment vs prisms / slabs / ground + dynamic colliders with height). ✅ *"Perception need terrain of course"* ⇒ the Perception role LOADS TERRAIN | keep the 2-D sweep: a wall of any height blocks everything; no floors |
| **T5** | height + motion | `ITerrainProvider` over the world model (the entity's Z picks the floor); SimHost clamps `SimTransform.Z` to it | IG-only visual clamp: the simulation would stay flat |
| **T6** | dynamic obstacles | **moving** (vehicles): never in the navmesh — crowd / RVO avoid them, a collider WITH HEIGHT occludes LOS. **placed buildings**: entities with a new static-obstacle `TkbType` + footprint + height, ✅ **baked** through the existing `PrepareTerrainAsset`/`CommitTerrainAsset` op pair (affected navmesh tiles rebuilt) | DotRecast TileCache obstacles: boxes/cylinders only, and a 2nd mechanism beside the ruled asset build |
| **T7** | multi-level | **in the FORMAT from v1, BUILT after the first slice** | flat-only v1: a breaking format change later |
| **T8** | Stride | later: Stride renders the same file, so both hosts share one world | two worlds: SimHost and Stride results never comparable |
| **T9** | world extent | both spatial grids sized from the terrain bounds (today perception sees x,y ∈ [0,1000), the collider grid [-750,750), and outside entities drop silently) | — |
| **T10** | 2D map | ✅ **REQUIRED by the user**: a 2D map layer that draws the world (footprints shaded by height, slabs/ramps, surfaces, roads) — see §3 | — |

**✅ First slice = steps 1–3 + the map layer:**

| step | content |
|---|---|
| **1** | format + parser + `TerrainWorld` model + height query + the 2D map layer drawing it |
| **2** | headless DotRecast navmesh + compose the solver on SimHost (`CE-3006`, then `CE-524`) |
| **3** | 3-D LOS over the world model (`CE-210`) |
| later | 4 placed buildings via the asset build · 5 multi-level built · 6 Stride renders the file |

## 1. INVENTORY — enumerated `2026-10-03` (codebase-memory graph CLI + grep; ⚠ `check_index_coverage` is not available through the CLI, so absences are strong, not proof)

| query | result | what it settled |
|---|---|---|
| graph `.*Terrain.*` (Class/Interface) + grep `TerrainDefinition` | `TerrainDefinition` (`Fdp.Toolkits/Terrain/TerrainDefinition.cs:28`, only `SchemaVersion`/`Name`/`RoadNetworks`), `TerrainResidency` (`Hrot.Core/Services/TerrainResidency.cs:36`, reads `{staging}/Terrain/{name}.json` `:104`, loads the FIRST road net only `:96-113`), `TerrainLoadStep`, `AnnouncingZoneTileLoader` (stub) | the terrain asset pipeline is BUILT; its content is roads only |
| graph `.*Nav[mM]esh.*Provider.*` | `EngineBackedNavmeshProvider` (everything walkable, straight-line `PlanPath`), `FakeNavmeshProvider` (test polygons), `StubNavmeshProvider` (EQS), `DotRecastNavmeshProvider` (Stride, real) | SimHost has no real navmesh |
| grep `new NavigationSolverModule(` outside tests | **0** | `CE-3006` confirmed: no host runs a path solver |
| using-lists of `DotRecastNavmeshProvider`/`DotRecastDtCrowdProvider`/`StrideNavmeshBaker`/`ISceneGeometrySource` | DotRecast + Fdp only — **no Stride type** | ⭐ the real navmesh can run headless; T3 is a MOVE, not a rewrite |
| graph `.*(Occlu\|Floor\|Height).*` (Class/Interface) | **0** | no building/floor/occlusion types exist |
| read `LosRequestBatchingSystem.cs:94-131` | 2-D segment vs `PhysicsCollider.Radius` circles; Z never read | LOS is 2-D on every host |
| grep `: ITerrainProvider` | only `MockTerrainProvider` (examples) + test stubs; `InstallGroundClamping` has no production caller | no production height source |
| `CarKinematicsSystem.cs:287` | writes `new Vector3(x, y, tf.Position.Z)` | Z is frozen at spawn on SimHost |
| `PerceptionConstants.cs:48-54`, `SpatialHashConstants.cs:10-22` | perception grid [0,1000), collider grid [-750,750) | T9 |
| `find -ipath '*terrain*'` data files | none; seed `basic-desert` names a terrain whose file does not exist | no terrain asset has ever been authored |

⚠ Measured with the graph and grep; to be extended by the design's own inventory (map layers, the LOS seam, the move's blast radius).

## 2. Claim table

| the decision rests on | code — how it IS | design — how it was MEANT to be |
|---|---|---|
| the definition is meant to grow beyond roads | ✅ `TerrainDefinition.cs` versioned root | ✅ terrain design §2.1e ①a *"later its terrain DB / heightmap / navmesh / built-in buildings"* |
| the navmesh can run headless | ✅ the four using-lists | ⛔ searched `docs/`+`.dev/`, no design moves it |
| the geometry seam already exists | ✅ `ISceneGeometrySource.cs:32` | ✅ nav design v2: *"Baking pipeline is outside this design's scope"* |
| LOS is 2-D and must not be | ✅ `LosRequestBatchingSystem.cs:94-131` | ✅ `CE-210`: *"LOS API can't be 2d of course"* |
| buildings are baked, movers are not | — | ✅ terrain design §2.1c (user ruling) |
| Perception did not load terrain | ✅ load step role filter | ✅ Node Roles §3.2 — and it names LOS-against-terrain as its reopen trigger |
| the solver belongs on the Muscle | ✅ no host composes it (`CE-3006`) | ✅ nav design v2: *"Muscle hosts `NavigationSolverModule`"* |

## 3. T10 — the 2D map layer

*(lean filled in by the design's map-layer inventory)*
