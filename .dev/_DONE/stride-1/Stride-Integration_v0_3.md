# Stride Integration — Design v0.3

> **Status.** Detailed design, ready for implementation by an AI coding agent that
> has the full FDP/HROT engine sources and the Stride .NET 8 sources/packages available.
> All design-level decisions are settled and architect-reviewed; remaining `[VERIFY-IN-SOURCE]`
> items are source-confirmation tasks, not open questions.
> **Audience.** Implementation lead (AI coding agent) and reviewer.
> **Goal.** Add a Stride3D-based node that takes over SimHost's responsibilities —
> physics, movement, perception, navmesh planning, character animation, and 3D
> visualization including gizmos — as the **real** backend behind the engine's existing
> fake/abstracted interfaces. Stride is a temporary placeholder for a future proprietary
> 3D/physics/navmesh/animation engine; this design is written so the same seams accept
> that engine later with no FDP-side changes.
>
> **Changelog v0.2 → v0.3 (this revision — aligns the design with the verified codebase and the
> confirmed implementation decisions).**
> - **Physics engine: Bullet, not Bepu.** Bepu in Stride 4.2 is an opt-in community add-on
>   (`Stride.BepuPhysics`), not a first-class citizen; Bullet (`Stride.Physics`) is the built-in
>   default, is battle-tested, and the Stride app's authored scene already uses it. Since Stride is
>   itself a throwaway placeholder for the future proprietary engine, Bullet's "works now, zero
>   integration risk, scene already authored for it" outweighs Bepu's niceties. All `Bepu*` adapters
>   are renamed/retargeted to Bullet; the per-substep `AfterSimulationUpdate` hook becomes a
>   **once-per-frame post-physics reverse-sync**; `CarController` becomes a **kinematic vehicle
>   mover**; the "bake arena into Bepu statics" step is **removed** (the scene's Bullet static
>   colliders are the environment as-is). The FDP-facing seams are unchanged.
> - **Entity → model/collider binding made explicit.** v0.2 assumed but never specified how an FDP
>   entity class maps to a concrete 3D model + collision shape. v0.3 adds the engine-specific TKB
>   descriptor **`StrideRenderModelDefDto`** (`[TkbDescriptor("Stride.RenderModelDef")]`, in
>   `Fdp.Toolkit.Tkb.Domain`) and the **`StrideVisualBindingSystem`** that consumes it (§6.5). A
>   future engine gets its own descriptor. (Supersedes the v0.2 working name `StrideVisualDefDto`.)
> - **Project layout pinned to the generated Stride solution** (`HrotStrideApp`, §3), and the
>   animation backend is a **new `Hrot.Stride.Animation`** project (not a reuse of the existing
>   `Hrot.MuscleCharacter.Animation.Stride` skeleton, which is fake/mock-oriented).
> - **Verified facts folded in:** authority lives in `EntityMetadataCold.AuthorityMask`
>   (queried via `.WithOwned<T>()`/`.WithoutOwned<T>()`), **not** `EntityHeader`; the offline
>   orchestration bus is a **separate** `FdpEventBus` from the simulation bus; Mode 1's in-process
>   `ClusterMaster` + `ClusterSlave` (empty `Mandatory`) is confirmed in `EditorSubsystem`; the
>   Mode-2 companion is a `ClusterRunner` started with subsystems `cgf,orchestrator`.
>
> **Changelog v0.1 → v0.2 (retained for history).** Mode 1 clarified to the engine's real
> offline-editor model: a **single shared ECS simulation world** plus an in-process
> `ClusterMaster` + `ClusterSlave` orchestration pair over a shared in-memory bus (empty mandatory
> list) driving state transitions and the replay flow (§8). Mode 2 promoted from "reserved" to
> fully specified, mirroring `SimHostSubsystem`/`SimHostApp`/`SimHostNodeBootstrapper` (§13).
> `SimVelocity` (linear + angular) elevated to a mandatory continuous reverse-sync output (§6.1, §7).
> Authority predicate fixed to `.WithOwned<T>()` / `.WithoutOwned<T>()` and a reactive body
> lifecycle system added (§5.6, §7).
>
> **Reads alongside.** `DD-1_MuscleCharacterRuntime` (animation backend seam),
> `Navigation_Design_v2_0` (provider interfaces, dual-mode pathing),
> `3D_Cognitive_Spatial_Awareness_Promotion` (authoritative altitude), `EQS_Design`,
> and the HROT subsystem docs for `StrideNodeBootstrapper`, `ClusterRunner`, `SimHost`, `IG`, `Editor`.

---

## 0. Reading guide and conventions

This document specifies **one node implementation** — a combined
`MuscleGround | Perception | NavigationSolver | ImageGenerator` node whose authoritative
physics and geometry come from Stride/Bullet rather than FDP's internal kinematics — deployed
in **two configurations**:

- **Mode 1 — `editor_stride`**: an all-in-one authoring/dev configuration. A **single
  shared ECS world** runs both the Brain (CGF) logic and the Stride Muscle logic in one
  process with **no network**, plus the editor authoring surface. This is the analog of the
  existing `editor` subsystem, with the Stride real-3D Muscle swapped in for SimHost.
- **Mode 2 — networked Stride node**: a production-like configuration. The Stride node runs
  as a slave-only Muscle+IG node in its own process, joined over real DDS to a separate
  `ClusterRunner` process hosting the Brain (CGF) and the `ClusterMaster`. No editor; diagnostic
  windows retained.

**The Stride Muscle node itself — reverse-sync, `StrideKinematicsModule`, Bullet integration,
`FdpStrideTransform`, the split-authority sync, the visual binding, the gizmo renderer, the
providers — is identical in both modes.** The modes differ only in: (a) network factory
(`OfflineNetworkFactory` vs `NedNetworkFactory` + DDS), (b) whether the Brain is co-located
in the same world (Mode 1) or remote (Mode 2), and (c) whether the editor authoring surface
is present (Mode 1) or not (Mode 2). Building Mode 1 correctly builds nearly all of Mode 2.

**Source-of-truth precedence.** Where this design names an engine type, system, field, or
layout, the **current engine source is authoritative**. This document gives shape and
responsibility; the agent must confirm exact signatures, field names, byte offsets,
constructor parameters, and phase names against the live sources before coding. Several
spots are flagged **[VERIFY-IN-SOURCE]**. Do not hardcode any struct byte layout from this
document.

**The one load-bearing safety invariant:**

> **Exactly one thread ever touches `EntityRepository` at a time.** Component access is not
> thread-safe by design. Both modes pump Stride and (where present) the raylib/ImGui windows
> sequentially on a single OS thread. Every Stride GPU call stays on that thread; every
> raylib/OpenGL/ImGui call stays on that thread; there is no second thread to race.

**Stride version.** Authored and verified against **Stride 4.2.1.2487** (.NET 8). Physics is
**Bullet** via the built-in `Stride.Physics` package — *not* `Stride.BepuPhysics`.

---

## 1. The core idea: Stride is the *real* backend, not a renderer

The FDP/HROT codebase is built, at every authority-bearing layer, around a
**fake-then-real** pattern: an engine-agnostic interface, a deterministic *fake* that
unblocks development, and a *real* implementation that connects to an actual 3D engine.
Animation: `IAnimationBackend` → `FakeAnimationBackend` / `StrideAnimationBackend`.
Navigation: `INavmeshProvider` / `IDtCrowdProvider` / `IVolumetricPathProvider` → fakes →
DotRecast / dtCrowd. SimHost's kinematics, perception, and ground queries are themselves the
*fakes* that let the CGF (Brain) node be developed against a cheap, flat, deterministic
stand-in.

**This integration makes Stride the real backend.** "Stride takes SimHost's
responsibilities" means: implement the real backends behind interfaces that already exist,
and make Stride/Bullet physics authoritative for entity movement. It does **not** mean bolting
physics onto a visualizer, nor making Stride merely render FDP-computed state.

### 1.1 The seam

`StrideNodeBootstrapper` (in `Hrot.StrideMock`) is the engine-agnostic seam and is reused
**unchanged**. Its tick contract (`Context.Kernel.Update(dt)`) merely advances the FDP module
topology and flushes the event bus; it is oblivious to data-flow direction and accepts domain
modules via constructor injection (`kinematicsModule`, `perceptionModule`, `combatModule`,
`navigationModule` — all optional `IEcsModule`). Everything *above* it is Stride-specific (the
Stride projects); everything *below* it is engine-agnostic FDP and never references Stride. The
future proprietary engine swaps in at exactly this line.

> **Note.** `StrideNodeBootstrapper` physically lives in `Hrot.StrideMock`, which references
> Raylib/rlImGui (the headless mock renders 2D with Raylib). The new Stride app references
> `Hrot.StrideMock` to reuse the bootstrapper; the Raylib dependency comes along, which is fine
> because both modes already host the diagnostic raylib/ImGui windows (§8.3, §13). `Hrot.Stride.Core`
> (the engine-real library) does **not** reference `Hrot.StrideMock` and stays Raylib-free.

### 1.2 Authority model — Stride/Bullet physics authoritative

Entity movement is owned by **Bullet physics** inside Stride. FDP's movement-integrator
systems are off on this node. Each frame, after Stride's physics processor steps the
simulation, a **reverse-sync** writes the Bullet-resolved pose and velocity back into
`SimTransform` / `SimVelocity`, so the rest of FDP (perception, EQS, navigation status,
replication, animation dispatch, Brain-facing contracts) continues to read those components
exactly as before — now sourced from physics.

```
Authority direction (locally-owned entities):

   dtCrowd / nav desired-velocity ──► Bullet CharacterComponent.SetVelocity() / kinematic vehicle
                                              │  (Stride physics step, fixed sub-stepping)
                                       Bullet solves collisions, resting contact, motion
                                              │
                            post-physics (after PhysicsProcessor): reverse-sync
                                              │
                  SimTransform.Position/.Rotation + SimVelocity (lin+ang)   (authoritative)
                                              │
                          FDP kernel tick: perception, EQS, nav status, replication,
                                           animation dispatch, combat — all read SimTransform
                                              │
                                       Stride render + gizmos
```

### 1.3 Authority is per-component and runtime-transferable

Authority is held per ECS component via an `AuthorityMask` bit in
**`EntityMetadataCold`** (accessed through the query helpers `.WithOwned<T>()` /
`.WithoutOwned<T>()` and `EntityIndex.GetMetadata()`; **not** the long-removed `EntityHeader`),
and is **dynamically transferable at runtime**. In Mode 2, the Brain creates an entity, owns its
components initially, and hands `SimTransform`/`SimVelocity` authority to the Muscle via the
`DeferredTakeoverSystem`; the entity transitions from read-only ghost to locally-owned
physics body the instant the bit flips. In Mode 1 (single shared world, `localNodeId = 0`),
`NetworkSpawningSystem` grants authority **instantly at spawn** and bypasses the deferred
handshake — so every entity is owned from birth. Both paths converge on the same observable
state, so all downstream logic keys off the authority bit (not the spawn path), and the same
code serves both modes. **The deferred-takeover handshake itself is exercised only in
Mode 2**; this is acceptable — it is a network-layer behavior and Mode 2 is the network test.

### 1.4 Why not a phased FDP-authoritative stage first

An FDP-authoritative "Stride renders" stage was rejected as a target: its transform-sync
direction is the opposite of the final one and would be thrown away. Safe incremental
bring-up comes instead from the existing **fakes** standing in for not-yet-real capabilities
(run `FakeNavmeshProvider` before DotRecast, `FakeAnimationBackend` before the Stride
backend is wired, a trivial kinematic mover before full controllers), under the *same*
reverse-sync authority model throughout (§14).

---

## 2. The two deployment configurations

| | **Mode 1 — `editor_stride`** | **Mode 2 — networked Stride node** |
|---|---|---|
| Purpose | authoring, dev, testing | production-like; tests HROT networking/distribution |
| Brain (CGF) | co-located in the **same shared ECS world** | separate `ClusterRunner` process (`cgf,orchestrator`) |
| Stride Muscle | same world/process | own process, same machine (loopback DDS) or remote |
| Transport | **none — networkless, single world** | **real DDS** (CycloneDDS) |
| Network factory | **`OfflineNetworkFactory`** (no-op DDS stubs) | **`NedNetworkFactory`** (real) |
| Cluster orchestration | in-process `ClusterMaster` + `ClusterSlave` over a shared in-memory bus (empty mandatory list) | `ClusterSlave` on this node; `ClusterMaster` in the `ClusterRunner` |
| Authority handover | instant at spawn (`localNodeId = 0`) | deferred via `DeferredTakeoverSystem` over DDS |
| Editor authoring surface | **yes** | no |
| Diagnostic raylib/ImGui windows | yes | yes (debug/monitoring) |
| Stride owns its game loop | yes | yes |

The Stride Muscle node implementation (§4–§7, §10–§11) is identical across both. §8 covers
Mode 1 composition; §13 covers Mode 2 composition.

---

## 3. Project and namespace layout

Leave `Hrot.StrideMock` (Raylib headless fake) and `Hrot.FakeStrideApp` (Raylib host for the
mock) **untouched** — useful for CI/headless tests, and the source of the reused
`StrideNodeBootstrapper` (§1.1). The Stride solution was generated by Game Studio under a
top-level `Stride/` folder; keep its internal layout (Game Studio owns it).

```
Stride/                                   (Game-Studio-generated solution, repo top level)
  HrotStrideApp.sln
  HrotStrideApp.Game/        (net8.0-windows; RootNamespace HrotStrideApp; the code+assets project)
       refs: Stride.Engine, Stride.Physics, Stride.Particles, Stride.UI, Stride.Core(.Assets.CompilerApp)
       —> add ProjectReferences to Hrot.Stride.Core, Hrot.Stride.Animation, Hrot.StrideMock, and FDP/HROT assemblies
  HrotStrideApp.Windows/     (net8.0-windows; WinExe head; process entry point)
  Assets/                    (MainScene.sdscene, GameSettings (Bullet PhysicsSettings), GraphicsCompositor,
                              Models/ (mannequinModel + skeleton, Box2x1x1, GridBase10x10, walls/ramps/…),
                              Animations/ (Idle, Walk, Run, Jump_Start/Loop/End), Materials/, Textures/)
  Resources/                 (source FBX/PNG used to import the above)

Hrot.Stride.Core            (net8.0-windows class library)
  refs: Stride.Engine, Stride.Physics, Stride.Rendering, Stride.Games,
        DotRecast.*, the FDP/HROT engine assemblies.  NO Raylib, NO Hrot.StrideMock.
  namespace Hrot.Stride.Core

    FdpStrideTransform              static: coordinate + rotation conversions (both directions)
    StrideKinematicsModule          IEcsModule: keeps spatial/command/nav systems, omits FDP integrators
    PhysicsBodyLifecycleSystem      reactive: creates/destroys Bullet bodies on authority change
    BulletReverseSyncSystem         writes Bullet-resolved pose + velocity → SimTransform/SimVelocity (owned)
    SplitAuthorityStrideSyncScript  forward-sync (FDP→Stride visual) for non-owned entities
    BulletCharacterMotor            feeds steering velocity into Bullet CharacterComponent
    KinematicVehicleMotor           feeds vehicle commands into a kinematic vehicle body
    StrideVisualBindingSystem       instantiates model+collider per entity from StrideRenderModelDefDto (§6.5)
    PhysicsBodyReference            shadow component: ECS entity <-> Bullet body
    StrideNavmeshBaker              Stride scene geometry → DotRecast bake (per NavLayerMask)
    DotRecastNavmeshProvider        INavmeshProvider real backend (ECS singleton)
    DotRecastDtCrowdProvider        IDtCrowdProvider real backend
    StrideRaycastService            wraps Simulation.Raycast for perception/ballistics
    DebugPrimitiveRenderer3D        sweeps ProducerBuffer → Stride.DebugRendering primitives
    StrideCameraScript              3D camera; consumes CenterOnEntityCommand from the bus

Hrot.Stride.Animation       (net8.0-windows class library — NEW; real Stride animation backend)
  refs: Stride.Engine, Stride.Animations, Hrot.MuscleCharacter.Animation, (FDP)
  namespace Hrot.Stride.Animation
    StrideAnimationBackend          IAnimationBackend real backend (idle/walk/run blend + montages)
    PerEntityBlendTreeBuilder       IBlendTreeBuilder per entity (modeled on the template AnimationController)

(TKB contract — lives in FDP, engine-specific by design)
  Fdp.Toolkit.Tkb.Domain.StrideRenderModelDefDto   [TkbDescriptor("Stride.RenderModelDef")]  (§6.5)
  Fdp.Toolkit.Tkb.Domain.CollisionShapeKind        enum (None/Capsule/OrientedBox/Sphere/Cylinder/MeshFromModel)
```

Composition roots and the external host loop live in the Stride app:

```
  HrotStrideApp (HrotStrideApp.Game / .Windows)
    StrideHrotGame                  Stride Game subclass; process entry point + external host loop (§8.3)
    EditorStrideSubsystem           Mode-1 composition root (mirrors EditorSubsystem; §8)
    StrideMuscleNodeApp             Mode-2 composition root (mirrors SimHostApp; §13)
    StrideMuscleNodeBootstrapper    Mode-2 node bootstrap (mirrors SimHostNodeBootstrapper; §13)
```

> **Why a new `Hrot.Stride.Animation` rather than reusing `Hrot.MuscleCharacter.Animation.Stride`.**
> The existing `Hrot.MuscleCharacter.Animation.Stride` is a skeleton with **no real Stride
> dependency** (mock/fake-oriented). The real backend needs `Stride.Engine`/`Stride.Animations`
> and a real blend tree; it gets its own project. `IAnimationBackend` / `IBlendTreeBuilder` wiring
> is identical; only the implementation differs.

---

## 4. `FdpStrideTransform` — the coordinate seam

A single pure static class owns every conversion between FDP world space and Stride world
space, both directions. Used by the reverse-sync, the forward-sync, the navmesh baker, the
gizmo renderer, the visual-binding system, animation-entity placement, and editor
mouse-picking. Centralizing it is mandatory: a stray ad-hoc swizzle is the most likely source
of "everything is rotated/mirrored" bugs.

**Axis mapping** (architect-confirmed; same mapping the engine's Recast integration uses):
FDP is right-handed, X = East, Y = North, Z = Up. Stride is Y-up, left-handed.

```
FDP (X=East, Y=North, Z=Up)            Stride (X, Y=up, Z)
   X  ───────────────────────────────►   X
   Z  (altitude) ─────────────────────►   Y
   Y  (North) ────────────────────────►   Z
```

```csharp
public static class FdpStrideTransform
{
    public static Stride.Core.Mathematics.Vector3 ToStridePosition(in FdpVector3 p);   // (p.X, p.Z, p.Y)
    public static FdpVector3                       ToFdpPosition(in Stride.Core.Mathematics.Vector3 s);

    // FDP right-handed Z-Y-X (yaw-pitch-roll) <-> Stride left-handed quaternion (handedness flip).
    public static Stride.Core.Mathematics.Quaternion ToStrideRotation(in FdpRotation r);
    public static FdpRotation                         ToFdpRotation(in Stride.Core.Mathematics.Quaternion q);

    // Velocity uses the same axis swizzle as position (no translation component).
    public static Stride.Core.Mathematics.Vector3 ToStrideVelocity(in FdpVector3 v);
    public static FdpVector3                       ToFdpVelocity(in Stride.Core.Mathematics.Vector3 s);
    public static FdpVector3                       ToFdpAngularVelocity(in Stride.Core.Mathematics.Vector3 s);

    // Editor picking: Stride screen ray -> FDP world ray.
    public static FdpRay ScreenRayToFdp(in Stride.Engine.CameraComponent cam, Vector2 screenPx);
}
```

- `FdpVector3` / `FdpRotation` are the engine's existing math types **[VERIFY exact names]** as
  used by `SimTransform`/`SimVelocity` ([SimComponents.cs]). Do not leak `Stride.Core.Mathematics`
  types across the bootstrapper seam.
- The rotation conversion must handle the handedness flip, not only axis relabeling. Validate
  a known yaw (FDP heading due East → correct Stride facing). **[VERIFY]** the exact Euler
  order of `SimTransform.Rotation` and the quaternion winding Bullet expects.

---

## 5. Authority, modules, and systems on this node

### 5.1 What runs, what is excluded

`GroundKinematicsModule` (`FDP.Toolkits/CarKinem/Modules`) **bundles** several systems; most are
still required. Build a custom module rather than omitting the bundle.

| System | On this node | Notes |
|---|---|---|
| `CarKinematicsSystem` | **Excluded** | Replaced by the kinematic vehicle mover. |
| `LinearKinematicsSystem` | **Excluded** | Replaced by Bullet rigid/character bodies. |
| `SpatialHashSystem` | **Keep** | Pure consumer; rebuilds spatial grid from `SimTransform` + collider. |
| `FormationTargetSystem` | **Keep** | High-level command processing. |
| `VehicleCommandSystem` | **Keep** | High-level command processing. |
| `NavigationExecutionSystem` | **Keep** | Writes CQRS `NavigationStatus`; solver-agnostic. |
| `CrowdAgentUpdateSystem` | **Keep, refactored** | Velocity-only; stop mutating `SimTransform` (§5.3). |
| `DeadReckoningSyncSystem` | **Keep, `DriveFromNetwork=false`** | Smoothing restricted to non-owned/ghost entities (§5.4). |
| network egress (`GeoSpatialEgressTranslator`) | **Keep** | Reads `SimTransform`/`SimVelocity` → DDS; locally-owned entities only (§13.4). |
| `TerrainQuerySubmitSystem` | **Excluded** | Geographic ground-clamp pipeline (§5.5). |
| `TerrainQuerySolverSystem` | **Excluded** | " |
| `TerrainQueryResolutionSystem` | **Excluded** | " — Bullet resting contact provides authoritative Z. |
| Perception / EQS / combat / ballistics / animation-dispatch / status | **Keep** | Read `SimTransform`; unaffected by authority inversion. |

> Note: v0.2 listed `TransformSyncSystem` here. That system exists only in `FDP.Examples.Common`;
> the production network egress on this node is `GeoSpatialEgressTranslator` (Hrot.Network.NED),
> which is what the table now names (§13.4).

### 5.2 `StrideKinematicsModule` — exclusion by topological omission

Create `StrideKinematicsModule : IEcsModule` that registers the **kept** systems
(`SpatialHashSystem`, `FormationTargetSystem`, `VehicleCommandSystem`,
`NavigationExecutionSystem`, the refactored `CrowdAgentUpdateSystem`) and does **not**
register `CarKinematicsSystem` or `LinearKinematicsSystem`. This module is what `editor_stride`
and the Mode-2 bootstrapper register in place of the stock `SimHostCoreLogicPack` /
`GroundKinematicsModule`.

Do **not** exclude via component removal: entities must still carry `SimTransform` /
`SimVelocity` for the reverse-sync to write into. Exclusion is purely about which integrator
systems get registered.

**[VERIFY-IN-SOURCE]** the `IEcsModule` registration API and the precise membership of
`GroundKinematicsModule` / `SimHostCoreLogicPack`, so the custom module mirrors it minus the
two integrators.

### 5.3 `CrowdAgentUpdateSystem` refactor (required)

Current behavior hardcodes integration (`tf.Position += velocity * dt`) because
`LinearKinematicsSystem` is configured to skip crowd agents. Under split authority this
fights Bullet. Refactor so it:

- polls dtCrowd for desired/steering velocity (`_dtCrowd.GetAgentVelocity(entity)`),
- writes **only** a steering output — prefer a dedicated `CrowdMotorIntent` component over
  reusing `SimVelocity`, because under physics authority `SimVelocity` is a *result* of the
  physics step (written by the reverse-sync, §6.1), not an input. Using a separate intent
  component avoids conflating the steering request with the resolved velocity,
- no longer writes `SimTransform`.

`BulletCharacterMotor` (§6.2) reads `CrowdMotorIntent` and calls `CharacterComponent.SetVelocity()`.
dtCrowd stays the *steering brain*; Bullet is the *mover/collider*.

### 5.4 `DeadReckoningSyncSystem` — `DriveFromNetwork=false`

Instantiate with `DriveFromNetwork = false` so dead-reckoning smoothing applies **only** to
non-owned entities (network ghosts / replayed), never to locally-owned physics bodies.
Locally-owned bodies are driven solely by Bullet → reverse-sync. In Mode 1 there are no ghosts,
so this system effectively idles; in Mode 2 it drives remote entities' visuals.

### 5.5 Terrain query pipeline — excluded

Under the 3D Cognitive Spatial Awareness promotion, `SimTransform.Position.Z` is the single
authoritative altitude (`GroundClampingState` deleted). Bullet resting contact against the
collision terrain (the MainScene's static colliders, §12) supplies authoritative Z for free via
the reverse-sync. Omit the whole geographic query pipeline (`TerrainQuerySubmitSystem` /
`TerrainQuerySolverSystem` / `TerrainQueryResolutionSystem`), which also bypasses the
now-redundant `TerrainClampBaseline` jump-rejection filter (a continuous physics engine prevents
the geometric popping it guarded). EQS altitude consumers (`DistanceScoreTest`,
`NavmeshReachableTest`) read 3D coordinates directly from `SimTransform`.

### 5.6 `PhysicsBodyLifecycleSystem` — reactive body creation/teardown (required)

Bind Bullet body lifecycle to the **authority bit**, never to the network
`DescriptorAuthorityChanged` event — because that event fires only in Mode 2, whereas Mode 1
grants authority instantly without it. A reactive sweep keyed on `WithOwned` works identically
in both modes. Runs before the physics step. Body creation defers to the visual-binding system
(§6.5), which holds the per-entity shape/model from `StrideRenderModelDefDto`.

```
PhysicsBodyLifecycleSystem (pre-physics):

  Creation:
     query .WithOwned<SimTransform>().Without<PhysicsBodyReference>()
       -> resolve StrideRenderModelDefDto for the entity class (§6.5)
       -> instantiate the Bullet body/character/vehicle (shape from CollisionShapeKind)
       -> add PhysicsBodyReference shadow component (links ECS entity <-> Bullet body)

  Revocation / destruction:
     consume DestructionOrder events  -> tear down body, remove PhysicsBodyReference
     query .WithoutOwned<SimTransform>().With<PhysicsBodyReference>()
       -> authority was revoked: remove body from the simulation, remove PhysicsBodyReference
```

`PhysicsBodyReference` is a new Muscle-internal shadow component mapping an ECS entity to its
Bullet body. **[VERIFY]** the `WithOwned`/`WithoutOwned`/`Without` query API
([QueryBuilder.cs] — confirmed present) and the `DestructionOrder` event shape.

---

## 6. Bullet physics integration

### 6.1 Simulation configuration, the step, and the velocity invariant

Use `Stride.Physics` (Bullet; Stride's built-in default engine). Configure a **fixed
timestep** with sub-stepping via the Bullet `Simulation` (`Simulation.FixedTimeStep`,
`Simulation.MaxSubSteps`) so the simulation clock is deterministic w.r.t. render rate; do
**not** hand-roll an accumulator. The GameSettings `PhysicsSettings` selects Bullet.

Stride's `PhysicsProcessor` steps the simulation during the engine's Physics stage each frame.
The two sync boundaries are:

- **Before the step** (`BulletCharacterMotor` / `KinematicVehicleMotor`, §6.2): push
  `CrowdMotorIntent` / vehicle commands into the Bullet bodies.
- **After the step** (`BulletReverseSyncSystem`, §7): once per frame, after the
  `PhysicsProcessor` has run, write the resolved pose + velocity into `SimTransform` /
  `SimVelocity` for `.WithOwned<SimTransform>()` entities. Wrapped in
  `TogglablePostSimulationGroup` (§9).

> **Per-frame, not per-substep.** Unlike Bepu's `AfterSimulationUpdate` per-substep hook, the
> Bullet reverse-sync runs **once per frame** after the physics processor. This is sufficient:
> the FDP kernel ticks once per frame and reads `SimTransform`/`SimVelocity` once per frame, so
> intermediate substep states are never observed by FDP. **[VERIFY]** the cleanest ordering hook
> to run a system immediately after `PhysicsProcessor` (a custom processor/script ordered after
> physics, or an `Input`/pre-`Simulation` FDP-phase system reading the post-step bodies — see §8.3).

> **Velocity invariant (network-critical).** The reverse-sync must, every frame, write the
> Bullet-resolved **linear and angular** velocity into `SimVelocity` — including **zeroing it
> when a collision arrests the body**. For dynamic rigid bodies read
> `RigidbodyComponent.LinearVelocity` / `.AngularVelocity`; for kinematic character bodies
> (which Bullet drives without exposing a solved velocity) derive it from the
> commanded velocity and/or the per-frame position delta. The network egress
> (`GeoSpatialEgressTranslator`, §13.4) reads `SimVelocity` directly to populate the `Vel`/`RotVel`
> fields used by remote dead-reckoning. If a hard collision stops a body but `SimVelocity` is left
> stale, remote ghosts extrapolate *through* the obstacle at the pre-collision velocity until the
> next spatial packet corrects them. `SimVelocity` is a first-class reverse-sync output, not an
> afterthought.
>
> **Kinematic bodies own their velocity.** Bullet does not produce a solved velocity for kinematic
> character or vehicle bodies. For those, the motor (§6.2) computes the **post-collision** linear and
> angular velocity each frame (from the executed, collision-clamped move) and the reverse-sync writes
> that into `SimVelocity` — falling back to the per-frame position/orientation delta. A blocked move
> must yield a **zero** velocity, exactly as a dynamic arrest would.

**[VERIFY]** the Stride Bullet `Simulation` access point from a system/script, the fixed-step
config location in GameSettings, and whether one shared `Simulation` is used (use one unless a
reason emerges).

### 6.2 Motors — feeding Bullet controllers

- `BulletCharacterMotor`: humanoid `CrowdAgent` entities. Reads `CrowdMotorIntent`, applies the
  stance speed multiplier (Standing/Crouched/Prone), and drives the Bullet `CharacterComponent`
  (`SetVelocity(direction*speed)`, `Jump()` for traversals). The template's
  `PlayerController` is the working reference for `CharacterComponent` usage.
- `KinematicVehicleMotor`: wheeled/tracked entities. Translates `VehicleCommandSystem` output
  (throttle/steer) into motion of a **kinematic** vehicle body. Because a kinematic body is **not**
  resolved by the solver, this motor **owns collision response**: integrate the commanded motion
  against the **static** Bullet world with a swept / penetration-tested move (block-or-slide on
  contact) rather than relying on Bullet to push the body out; then **compute the resulting
  post-collision linear + angular velocity** (zeroed on a blocked move) and hand it to the
  reverse-sync so `SimVelocity` satisfies the velocity invariant (§6.1) for network egress. A full
  dynamic raycast-vehicle (suspension/wheels) is out of scope; naval/flying out of scope this pass.

Each Stride physics entity (rigid body / character controller) **is** the entity's spatial
representation. The animation entity (`AnimationComponent` + skeletal model) is the same
Stride entity or a child of the physics body, so animation pose composes on the physics-driven
transform with no extra copy (DD-1 §15.3 option B). The visual-binding system (§6.5) wires this.

### 6.3 Raycasts — perception and ballistics

`StrideRaycastService` wraps `Simulation.Raycast(from, to, out HitResult, collisionGroups,
collisionFilter)` (and penetrating/overlap/sweep variants). Two consumers:

- **Perception / LOS occlusion.** LOS checks raycast against real Stride scene collision
  geometry (true occlusion, real Z), replacing SimHost's flat spatial-hash approximation.
  Feed results into the existing perception/`TargetMemory` pathway (3D per the cognitive
  promotion). **[VERIFY]** the perception/vision entry point doing the fake LOS test today,
  so the Stride raycast injects behind the same interface.
- **Ballistics.** FDP's `BallisticsSystem` is kept (analytic integration beats a physics
  engine for fast projectiles, which tunnel). It consumes `StrideRaycastService` via the
  existing `RaycastSolver` / `HitResolution` seam. **[VERIFY]** that seam.

All ray inputs/outputs cross `FdpStrideTransform`.

### 6.4 Animation and root motion

Use the new `StrideAnimationBackend` (`IAnimationBackend`, `IBlendTreeBuilder`; DD-1 §15) in
`Hrot.Stride.Animation`, wired via the bootstrapper's systems registrar / `AnimationMuscleModule`.
Continuous locomotion: the existing `AnimationRuntimeBridgeSystem` reads `SimTransform` +
`SimVelocity` (now physics-sourced) and calls `UpdateLocomotionInputs`; the backend blends
`Idle`/`Walk`/`Run` (modeled on the template `AnimationController`'s `IBlendTreeBuilder`).
Discrete traversal montages (`Jump_Start`/`Jump_Loop`/`Jump_End`): `OffMeshLinkDetectionSystem`
writes `AnimationChannel.PlayMontage`. Clip asset URLs come from the animation TKB descriptor
(`CharacterAnimationDefDto`, authored in the animation bring-up task; §14 step 4).

**Root motion** is supported at the interface level but **not implemented this pass**. DD-1
§19 specifies the additive shape (`ExtractRootMotionDelta`, `RootMotionApplicatorSystem`,
`SuppressLinearKinematics`/`UsesRootMotion`, `IsLocalAuthoritativeOnly`). Per-asset, opt-in.
**Interaction with physics authority** (deferred decision): when root motion is later enabled
for an entity, the root-motion delta and the physics controller both want to move it — choose
one per entity (root-motion-active entities should drive the body kinematically from the
extracted delta, not be double-moved). Leave hooks; do not implement.

### 6.5 Entity → model + collider binding (`StrideRenderModelDefDto`, `StrideVisualBindingSystem`)

How an FDP entity class maps to a concrete Stride model and collision shape is **explicit** and
data-driven via a TKB descriptor:

- **`StrideRenderModelDefDto`** — `[TkbDescriptor("Stride.RenderModelDef")]`, in
  `Fdp.Toolkit.Tkb.Domain` (engine-specific by design; a future engine gets its own descriptor).
  Carries: `ModelAssetRef` (Stride asset URL of the Model; empty ⇒ procedural primitive fallback),
  `SkeletonAssetRef` (skinned models), `Scale`, `Offset{X,Y,Z}` (render offset from body origin),
  `ShapeKind` (`CollisionShapeKind`: Capsule/OrientedBox/Sphere/Cylinder/MeshFromModel/None),
  `ShapeRadius` (0 ⇒ default from `PhysicsCollider.Radius`), `ShapeHeight`, and
  `BoxHalf{X,Y,Z}` (0 ⇒ default from the entity's `VehicleParametersDto` Length/Width + height).
  *(This file already exists with demo content on the UrbanCombat templates — see §12.)*

- **`StrideVisualBindingSystem`** (in `Hrot.Stride.Core`) — resolves the descriptor for an
  entity's class and, on entity-appear (the §7 Pass-A reconciliation), instantiates the backing
  Stride entity: load/clone the `ModelAssetRef` model (or a procedural primitive matching
  `ShapeKind` when empty), attach the `AnimationComponent` + skeleton for skinned models, and
  inform `PhysicsBodyLifecycleSystem` (§5.6) of the shape so it builds the matching Bullet
  collider. Records a `StrideVisualReference` (ECS entity ↔ Stride visual entity) alongside
  `PhysicsBodyReference`.

- Capsule for upright humans, oriented box for vehicles; the **placeholder primitive** path
  (empty `ModelAssetRef`) keeps bring-up steps 0–3 runnable before/without real art.

**[VERIFY]** how to load a Stride asset by URL at runtime (`Content.Load<Model>(url)`),
clone/instantiate it under a parent entity, and attach `ModelComponent` / `AnimationComponent`.

---

## 7. The split-authority sync

`SyncFdpToStrideScript` (the mock's Raylib script) is hardcoded **forward-sync**
(`strideEntity.Position = xform.Position` for every entity with `SimTransform`). Under physics
authority this overwrites Bullet's resolved positions every frame — jitter/freeze. It is
replaced by two cooperating pieces that fork on the **authority bit**:

**Reverse path — `BulletReverseSyncSystem` (Stride → FDP), for owned entities.** Runs once per
frame after the physics processor, wrapped in `TogglablePostSimulationGroup` (§9). Query
`.With<SimTransform>().WithOwned<SimTransform>()`. For each: read the resolved Bullet pose +
linear/angular velocity, write `SimTransform.Position`/`.Rotation` and `SimVelocity` via
`FdpStrideTransform` (honoring the velocity invariant, §6.1).

**Forward path — `SplitAuthorityStrideSyncScript` (FDP → Stride visual), for non-owned
entities.** Query `.With<SimTransform>().WithoutOwned<SimTransform>()`. These have no local
physics authority; their `SimTransform` comes from `DeadReckoningSyncSystem` (ghosts) or
`PlaybackTickSystem` (replay). Write the Stride visual entity transform from `SimTransform`
via `FdpStrideTransform`. Locally-owned entities are **skipped** here (the Bullet body already
is their transform; `PhysicsBodyReference` exists for them).

> **Why `.WithOwned`/`.WithoutOwned`, not lifecycle checks.** These are O(1) bitwise tests
> against the `AuthorityMask` (in `EntityMetadataCold`), and they reflect runtime authority
> transfer automatically (Mode 2's `DeferredTakeoverSystem` handover flips an entity from forward
> to reverse with no extra code). Do **not** manually test `EntityLifecycle.Ghost` or
> `NetworkAuthority` owner ids. In Mode 1 every entity is owned, so the forward path simply matches
> nothing and every entity is reverse-synced; in Mode 2 the fork does real work. Same code, both modes.

**Entity-set reconciliation.** The two-pass differential upsert/teardown from the mock's
script (generational `IsAlive`, reused stale list) is still used to manage the *Stride-side
visual entity set* (spawn a backing Stride entity when an FDP entity appears — via
`StrideVisualBindingSystem`, §6.5 — tear it down when it dies). Keep that structure in
`SplitAuthorityStrideSyncScript`; it is orthogonal to the authority fork (it manages existence,
not transform direction).

```
Per frame:
  After PhysicsProcessor (in TogglablePostSimulationGroup):
     BulletReverseSyncSystem: .WithOwned<SimTransform>()
        -> SimTransform/.Rotation/SimVelocity = FdpStrideTransform.ToFdp(bullet body)

  SplitAuthorityStrideSyncScript (after _core.Tick):
     Pass A — reconcile Stride visual entity set (appear/disappear) via StrideVisualBindingSystem
     Pass B — .WithoutOwned<SimTransform>()
        -> strideEntity.Transform = FdpStrideTransform.ToStride(SimTransform)
        (owned entities skipped: their Stride body is physics-driven)
```

---

## 8. Mode 1 — `editor_stride` composition (single shared world, no network)

### 8.1 The composition model

Mode 1 reuses the engine's **existing offline-editor model exactly** (confirmed by reading
`EditorSubsystem`). That model has **two layers** that coexist, both networkless: a single
shared *simulation* world, and an in-process *orchestration* master/slave pair. `editor_stride`
replicates both, swapping the Muscle half from SimHost to Stride.

**Simulation layer — one shared world.** The current `editor` does **not** run two nodes over
a transport; it runs Brain + Muscle logic against one shared world:

- Exactly one `EntityRepository`, one simulation `FdpEventBus` (`_world.Bus`), one `ModuleHostKernel`.
- The Brain's `CgfLogicPack` and the Muscle's logic, with their input/simulation systems
  grouped into shared `TogglableInputGroup` / `TogglableSimulationGroup` / `TogglablePostSimulationGroup`,
  run sequentially on one thread.
- `OfflineNetworkFactory` (concrete, in `Hrot.Editor`) returns no-op DDS stubs and empty
  translator lists. No DDS traffic anywhere.
- `NetworkSpawningSystem` runs with `localNodeId = 0` (`EditorNodeId`); `SpawnEntityCommand`
  is stamped `OwnerNodeId = 0`, so `isLocalAuthority` is true and authority bits are set
  instantly — the deferred handover (`DeferredTakeoverSystem`, `PendingAuthorityGrants`) is
  bypassed. Every entity is `WithOwned` from birth.

**Orchestration layer — in-process `ClusterMaster` + `ClusterSlave` over a separate bus.** The
editor stands up a local master/slave pair so the cluster state machine
(`OperatingEdit` / `OperatingLive` / `OperatingReplay`) works for authoring and replay. The
exact offline sequence (mirror it in `editor_stride`), **as verified in `EditorSubsystem`**:

1. Create a **separate** orchestration `FdpEventBus` (`_orchestrationBus`) — *not* the
   simulation `_world.Bus`. (This resolves the v0.2 open question: the orchestration bus is a
   distinct instance.)
2. Create `ClusterSlave` on `_orchestrationBus`, node id 0, name `"Editor"`:
   `new ClusterSlave(EditorNodeId, "Editor", _orchestrationBus)`.
3. Wrap the slave in an `OrchestrationLogicPack` (`IEcsModule`) and register it with the
   `ModuleHostKernel` so it ticks sequentially in the host loop.
4. Create the master with an empty mandatory list:
   `new ClusterMaster(_orchestrationBus, new ClusterConfiguration { Mandatory = Array.Empty<string>() })`.
   The empty mandatory list forces the master to release its bootstrap latch immediately and
   publish the initial `Standby` state.

Authoring/state changes publish a `TransitionStateIntent` to `_orchestrationBus`; the in-process
master computes the transition trajectory and fans out `ExecuteNodeOpIntent` commands back onto
the bus, which the local slave consumes and executes. No DDS is touched. This is the mechanism
the replay flow (§9) binds to — the master/slave drives the `OperatingReplay` transition that
toggles the reverse-sync group.

**`editor_stride` composition**, putting both layers together: `OfflineNetworkFactory`; one
shared simulation repository/bus/kernel registering `CgfLogicPack` unchanged plus
`StrideKinematicsModule` (+ Stride providers, animation backend, reverse-sync, body lifecycle,
visual binding) in place of `SimHostCoreLogicPack`; and the in-process `ClusterMaster`/`ClusterSlave`
on the orchestration bus exactly as above. Because Brain and Muscle share the simulation world and
bus, the Brain's `MoveToExecutor` writes a `NavigationIntent` component the Stride Muscle
systems read the same frame, with no serialization.

**[VERIFY-IN-SOURCE]** the exact `OrchestrationLogicPack` / `TransitionStateIntent` field names
and the `ReferenceReplayLoadHandler` group wiring (all present and used in `EditorSubsystem`).

### 8.2 Reuse boundary — mirror `EditorSubsystem`, don't disturb it

The existing `editor` subsystem stays untouched. Build a new `EditorStrideSubsystem`
(application root) mirroring `EditorSubsystem`, reusing at three established levels:

1. **Application facade.** `EditorApplication` implements `IEditorLogic` (scenario load/save,
   tool interactions) and accepts an `IReadOnlyList<IEcsModule>` in its constructor. Construct
   it exactly as `EditorSubsystem` does, passing `StrideKinematicsModule` (+ Stride modules)
   in the list instead of `SimHostCoreLogicPack`.
2. **Shared AI editor.** The BTree / HSM / Blueprint toolsets are modularized via
   `PerspectiveWorkspaceRegistrar` and `AiAssetCatalogBuilder`. Instantiate these registrars
   with the shared catalog and selection store, as the current editor does.
3. **ImGui panels.** `MissionPanel`, `SpawnerPanel`, `ConfigPanel`, `EditorOrbatPanel` are
   pure view classes taking the `IEditorLogic` facade and data models (TKB catalog, etc.).
   Instantiate them directly and register with the engine `WindowManager` in the new
   subsystem's `RegisterWindows`.

No extraction from `EditorApplication`/`EditorSubsystem` is required; the boundary already
exists via `IEditorLogic` and the decoupled panels.

### 8.3 Host loop and threading

Stride owns the process and is driven from an **external host loop** via `Game.Tick()`
(Stride supports external-main-loop driving; the internal `ThreadThrottler` is disabled in
that mode and the host pumps the Stride window's OS events each iteration). The raylib/ImGui
editor windows are **second OS windows pumped on the same thread**, sequentially. No second
thread ⇒ `EntityRepository` single-threaded by construction.

```
[single OS host thread, per frame]

  1. pump Stride window OS events            [VERIFY exact call for current Stride Windows
                                              backend (SDL2); analog of Application.DoEvents()]
  2. strideGame.Tick(dt):
        - Stride Physics stage: PhysicsProcessor steps Bullet (fixed sub-steps)
              (before: motors push CrowdMotorIntent / vehicle cmds into bodies — §6.2)
        - post-physics: BulletReverseSyncSystem writes SimTransform/SimVelocity (owned)
              [TogglablePostSimulationGroup — disabled during replay]
        - PhysicsBodyLifecycleSystem (create/destroy bodies on authority) runs pre-physics next frame
        - StrideNodeBootstrapper tick: _core.Tick(dt)
              (FDP kernel: perception, nav, EQS, combat, ballistics, animation dispatch,
               status, replication egress — all read fresh physics-sourced SimTransform;
               OrchestrationLogicPack's ClusterSlave also ticks here, draining _orchestrationBus)
        - SplitAuthorityStrideSyncScript: reconcile visual set + forward-sync non-owned
        - StrideAnimationBackend tick + Stride 3D render
        - DebugPrimitiveRenderer3D sweeps ProducerBuffer -> 3D gizmos
  3. drain editor authoring intents (mutate EntityRepository here — world idle)
  4. raylib BeginDrawing():
        - 2D map reads EntityRepository (single-threaded, safe)
        - DebugPrimitiveRenderer2D sweeps ProducerBuffer -> 2D gizmos
        - rlImGui panels (inspector, ORBAT, mission, spawner, preview) [ImGui this thread only]
     raylib EndDrawing()
```

**Ordering rationale.** The reverse-sync must run before `_core.Tick(dt)` so FDP
`Simulation`-phase consumers (`SpatialHashSystem`, vision broadphase, EQS) read correct
positions. Architect guidance: run the reverse-sync as a custom `Input`-phase system or
sequentially just before `_core.Tick(dt)`, reading the Bullet bodies that the prior frame's
physics stage resolved. **[VERIFY]** the phase enumeration and the exact post-`PhysicsProcessor`
ordering hook.

**Frame timing.** Disable Stride's throttler in external-loop mode; avoid compounding two
vsync waits (Stride + raylib `EndDrawing`). The fixed Bullet timestep is the real simulation
clock regardless of render rate. **[VERIFY]** external-loop throttler/vsync flags.

**Graphics contexts don't conflict.** Stride = Direct3D; raylib = its own GLFW/OpenGL;
ImGui = the raylib (`rlImGui`) instance only. Separate APIs, windows, device contexts —
nothing shared. The only rule is thread-affinity, trivially satisfied here.

### 8.4 Shared selection, independent cameras

`SelectionState` is an ECS component; the raylib 2D selection interaction mutates it during
the editor pump (world idle — safe), and the Stride 3D view + gizmo projectors read the same
component, so "select in 2D → highlighted in 3D" works for free. Cameras are independent
(raylib 2D-ortho `MapCamera`; Stride native 3D via `StrideCameraScript`). For "focus both
views on a unit," publish `CenterOnEntityCommand` on the bus; both views consume it and
animate to the entity's `SimTransform` independently.

> **Concurrency upgrade path (not built now).** If the single-thread frame budget ever binds
> (heavy 3D frame stalling the 2D pump), the sanctioned model is raylib on its own thread
> reading a `DoubleBufferProvider` → `ISimulationView` replica published at a tick boundary,
> with authoring writes *and* selection mutation marshalled back to the Stride thread as
> intents. Do not build until profiling demands it.

---

## 9. Recording and replay (both modes)

`RecordingModule` / `ReplayModule` run in both configurations, capturing this node's own
authoritative (physics-sourced) `SimTransform` each tick to local disk. Replay is per-node
playback of that recording — it does **not** re-simulate physics and has no dependency on
physics determinism (a checkpoint carries full recorded component state; replay restores it).

**Do not** gate physics/reverse-sync by polling cluster state. Use the engine hook:

- Wrap `BulletReverseSyncSystem` in a **`TogglablePostSimulationGroup`** and pass that group to
  `ReferenceReplayLoadHandler` (this is exactly how `EditorSubsystem` wires the SimHost post-sim
  group today).
- On `PrepareReplay`, the handler flips `Enabled = false`, severing the reverse-sync so it
  cannot overwrite historical positions.
- `ReplayModule` registers its `PlaybackTickSystem` **outside** any togglable group, so it
  drives `SimTransform` from recorded keyframes while reverse-sync is suspended. Animation and
  render read the replayed `SimTransform`; the forward path renders these entities (they read
  as non-owned during replay or are driven by playback).
- On `FinalizeReplay` / `PrepareLive`, `Enabled` flips back to `true`, returning authority to
  Bullet.

Also ensure the Bullet **step** does not advance owned bodies during replay (no motor intents
applied, nothing reading body output once reverse-sync is severed); **[VERIFY]** whether severing
the reverse-sync group suffices or the simulation must be explicitly paused (e.g.,
`Simulation.Enabled = false` or skipping the physics processor) — bind to the same lifecycle
transition, not a hand-rolled flag.

**[VERIFY-IN-SOURCE]** `TogglablePostSimulationGroup`, `ReferenceReplayLoadHandler`,
`PlaybackTickSystem`, and the `IRecordReplayController` transition names (all present; see
`EditorSubsystem` for the live wiring).

---

## 10. Navigation — dual-mode, real backends (both modes)

Navigation keeps its full existing contract (`Navigation_Design_v2_0`); this node provides
the real providers and both data sources for `Auto` backend selection.

### 10.1 Navmesh mode

`StrideNavmeshBaker` extracts collision/terrain triangles from the loaded Stride scene
(the MainScene arena, §12), swizzles via `FdpStrideTransform`, and bakes per-`NavLayerMask`
navmeshes with layer-specific parameters (Infantry 0.3 m / 60°; Vehicle 1.5 m / 20° / 0.1 m step;
Naval surface) using **DotRecast** (pure .NET; no Stride coupling at query time). Wrapped in
`DotRecastNavmeshProvider : INavmeshProvider`, registered as the managed `INavmeshProvider`
ECS singleton. Bake trigger: **scenario load**. (The bake reads scene mesh geometry directly, so
it is independent of which physics engine owns collision.)

`DotRecastDtCrowdProvider : IDtCrowdProvider` provides local avoidance/steering; its desired
velocity feeds `BulletCharacterMotor` (§6.2) via `CrowdMotorIntent` — "dtCrowd steers, Bullet
moves."

Prefer DotRecast directly over Stride's built-in navmesh: the built-in doesn't cleanly expose
per-layer bakes, and DotRecast keeps the provider contract identical to the fakes (drop-in
`NavigationFakesModule` → `NavigationDotRecastModule`).

### 10.2 Road-graph mode

The road graph is authored/loaded **independently** of the Stride scene; keep consistent by
convention (no engine pipeline derives it from scene splines). The scenario envelope JSON
includes a `Zones` section whose `ZoneDefinitionDto` carries `RoadNetworkPath`; standard
orchestration (`HrotScenarioLoadHandler` → `ZoneManagerService` →
`RoadNetworkLoader.LoadFromJson`) materializes the `RoadNetworkBlob` into the
`ZoneEnvironmentData` ECS singleton (format: pre-computed Hermite control points + spatial
grid).

### 10.3 Auto selection

With both singletons present at scenario load, `PathfindingSolverSystem` does `Auto` selection
by `BackendForce` + `MobilityProfile`: endpoints within `RoadRadiusThresholdSq` of road nodes
→ `RoadGraph`; mixed → spliced `Hybrid`; else → `Navmesh`; `Flying` →
`IVolumetricPathProvider` (deferred). Off-mesh-link traversal montages route through the
animation seam unchanged.

**[VERIFY-IN-SOURCE]** `ZoneDefinitionDto` / `RoadNetworkPath` / `ZoneEnvironmentData` /
`RoadRadiusThresholdSq`; the `INavmeshProvider` singleton registration point.

---

## 11. 3D gizmo rendering (both modes)

Local ECS gizmo producers (data-driven and stateless gizmo systems) write `DebugPrimitive`s
into the `ProducerBuffer`. In Mode 1 (no DDS) and for local gizmos in Mode 2, `ConsumerBuffer`
is empty/irrelevant for local draw; **both** the raylib 2D renderer and the Stride 3D renderer
sweep the `ProducerBuffer`. (In Mode 2, the 3D renderer may additionally sweep `ConsumerBuffer`
for remote overlays, e.g. CGF behavior-tree debug — §13.)

`DebugPrimitiveRenderer3D` mirrors the existing 2D renderer's two-pass scheme:

1. **Pass 1 — anchors.** Cache each spatial-anchor primitive by its network id (anchor world
   position + heading/pitch/roll).
2. **Pass 2 — shapes.** For each semantic-shape / line primitive, resolve against its anchor
   into absolute world coordinates (the renderer writes the resolved world transform into the
   primitive's spare payload, in-place), swizzle via `FdpStrideTransform`, emit through the
   `Stride.DebugRendering` immediate-mode subsystem. Fallbacks: a `DebugRenderer` render-stage
   in the `GraphicsCompositor`, or a dynamic `Mesh`.

> **Do not hardcode `DebugPrimitive` field offsets or the union layout from any document** —
> the struct (64-byte cache-line cap, header + union payload) is engine source-of-truth
> (`GizmoMap.Contracts`) and may change before implementation. Read the current struct from
> source and bind to it. **[VERIFY]** the current `Stride.DebugRendering` immediate-mode API and
> how to obtain its debug-draw entry point from the running game.

---

## 12. Scenario / asset authoring

`HrotStrideApp` (the `Stride/` solution) is authored in the Stride editor and carries the scene,
terrain, obstacles, skeletal models, animation clips, and the GameSettings (Bullet
`PhysicsSettings`). The Third-Person Platformer template seeded a complete, usable asset set —
**no new art authoring is required for the demo**:

- **`MainScene.sdscene`** is the authored arena: 135 `ModelComponent`s (floors, walls, ramps,
  stairs, pillars, tables) and **144 `StaticColliderComponent`s** (Bullet box/plane/mesh shapes).
  This static geometry is both the Bullet collision world (resting contact → authoritative Z)
  and the source geometry for the DotRecast bake (§10.1). The template's Bullet-driven player
  entity + its scripts (`PlayerController`/`AnimationController`/`ThirdPersonCamera`/`PlayerInput`)
  are throwaway and get removed/repurposed; the static arena stays.
- **Dynamic actors** are FDP-spawned entities (not authored in the scene). Their model + collider
  come from `StrideRenderModelDefDto` (§6.5):
  - **`Models/mannequinModel`** + `Models/mannequinModel Skeleton` + `Animations/{Idle,Walk,Run,
    Jump_Start,Jump_Loop,Jump_End}` → infantry (capsule).
  - **`Models/Box2x1x1`** → vehicles (oriented box).
- The road network JSON is authored separately, referenced from the scenario envelope's
  `Zones` section.
- In Mode 1 the editor authoring surface drives scenario state; entity creation goes through
  the Brain's spawn path (`SpawnEntityCommand`, `OwnerNodeId = 0`) in the shared world.

**Demo TKB content already in place.** `UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates`
attaches a `StrideRenderModelDefDto` to each of `CivilianPedestrian` / `InfantrySoldier` /
`Insurgent` (mannequin + capsule) and `CivilianCar` / `MilitaryAPC` (Box2x1x1 + oriented box).
These descriptors are inert until `StrideVisualBindingSystem` (§6.5) consumes them.

---

## 13. Mode 2 — networked Stride node (production-like)

The Stride Muscle node implementation is **identical** to Mode 1's; Mode 2 changes only the
composition root and network wiring. Mirror `SimHostSubsystem` / `SimHostApp` /
`SimHostNodeBootstrapper` for a slave-only node joining a remote master.

### 13.1 Topology

Two OS processes on one machine (extensible to multiple machines), loopback DDS:

- **Process A — `ClusterRunner`**: started with subsystems **`cgf,orchestrator`** (verified
  supported by `HrotRunnerConfiguration`), hosting the Brain (CGF) and the `ClusterMaster`. Loads
  scenarios, creates entities, owns components initially, hands `SimTransform`/`SimVelocity`
  authority to the Muscle via `DeferredTakeoverSystem`.
- **Process B — Stride Muscle node** (`StrideMuscleNodeApp`): the combined
  `MuscleGround | Perception | NavigationSolver | ImageGenerator` node. Runs a `ClusterSlave`
  only; reaches the remote master over DDS. Stride owns its game loop; diagnostic raylib/ImGui
  windows present; **no editor authoring surface**.

### 13.2 Bootstrap sequence (mirror `SimHostApp`)

1. Instantiate a `DdsParticipant` for the DDS domain; configure a `NedNetworkFactory` using
   that participant.
2. Delegate orchestration construction to `NodeBootstrapper.BuildOrchestration`, which
   instantiates the `ClusterSlave` and calls `ConfigureForNode` on the network factory to
   obtain the `ISlaveOrchestrationTranslator` (e.g. `NedSlaveOrchestrationTranslator`).
3. Each frame, call the slave translator's `Tick()` **before** ticking the `ClusterSlave`, so
   DDS ingress (`NodeOpCommand` → `ExecuteNodeOpIntent` on the bus) is published before the
   slave processes it; the translator also drains the bus to write `NodeHeartbeat` /
   `NodeOpStatus` to DDS.

`ClusterSlave` is transport-agnostic — **the exact same slave code as Mode 1's underlying
node**; all its I/O is over the local `FdpEventBus`, and the translator bridges to DDS. No
co-located master is assumed.

### 13.3 Discovery, identity, ownership

- **Discovery** is native CycloneDDS SPDP/SEDP: participants match on the same **DDS Domain Id**.
- **Identity**: this node's id is configured locally via `HrotNodeConfig.NodeId`, passed
  through the bootstrapper.
- **Mandatory list**: unlike Mode 1, the remote Orchestrator's `ClusterConfiguration.Mandatory`
  **must include this node's `SubsystemName`**. The master discovers node ids dynamically from
  DDS heartbeats and will not release its bootstrap latch until it receives a `Standby`/Idle
  heartbeat from this node.
- **Authority handover** uses the deferred path: the Brain creates the entity (owned remotely,
  a ghost here), then hands `SimTransform`/`SimVelocity` authority via `DeferredTakeoverSystem`
  over DDS. The instant the `AuthorityMask` bit flips, `PhysicsBodyLifecycleSystem` (§5.6)
  materializes the Bullet body and the sync fork (§7) switches that entity to reverse-sync. This
  is the only path that exercises the deferred handshake (Mode 1 grants instantly).

### 13.4 Replication egress and the velocity invariant

The egress (`GeoSpatialEgressTranslator`) is **event-driven, not periodic**: it compares each
locally-owned entity's live `SimTransform` against its `NetworkTransform` shadow every frame
and dispatches a `WorldPos` packet immediately when displacement exceeds ~1 cm² or rotation
exceeds ~0.5°. Sharp Bullet collision changes thus trigger instantaneous updates — **no tuning
of publish rate or smoothing is needed** for physics-sourced motion. Remote ghosts extrapolate
via `DeadReckoningSyncSystem` using received `NetworkVelocity`, blending toward the projection
at a fixed `SmoothingRate`; this is agnostic to motion source.

The **one** hard requirement is the velocity invariant (§6.1): the reverse-sync must
continuously write resolved linear and angular velocity into `SimVelocity` (the egress reads
it for `Vel`/`RotVel`), including zeroing on collision arrest — otherwise remote ghosts
extrapolate through obstacles until the next spatial packet corrects them.

### 13.5 Gizmos over the wire

The 3D gizmo renderer additionally sweeps `ConsumerBuffer` for remote debug overlays arriving
over DDS (e.g. CGF behavior-tree debug). Local gizmos remain in `ProducerBuffer` as in Mode 1.

**[VERIFY-IN-SOURCE]** `DdsParticipant`, `NedNetworkFactory`, `NodeBootstrapper.BuildOrchestration`,
`ConfigureForNode`, `ISlaveOrchestrationTranslator` / `NedSlaveOrchestrationTranslator`,
`HrotNodeConfig.NodeId`, `ClusterConfiguration.Mandatory`, `GeoSpatialEgressTranslator`,
`NetworkTransform`, `NetworkVelocity`, `SmoothingRate`.

### 13.6 The single-process "dream" topology (deferred — advanced)

The ideal dev convenience would be a single process hosting both the Brain/ClusterRunner and
the Stride Muscle as parallel ClusterRunner subsystems over loopback-yet-real DDS. This is
**deferred** because it collides with loop ownership: ClusterRunner's subsystem host wants to
own the process/loop, while Stride wants to own the game loop. It adds operational convenience,
not additional architectural coverage (it tests the same paths as the two-process topology), so
it is not required for Mode 2.

---

## 14. Bring-up roadmap (incremental, one authority model)

Authority direction is reverse-sync from day one; safe increments come from the existing fakes
standing in for not-yet-real capabilities. Each step independently runnable, in Mode 1 first.

0. **Scene + render + bootstrap.** Create `Hrot.Stride.Core` / `Hrot.Stride.Animation`; wire
   `HrotStrideApp.Game` → those + `Hrot.StrideMock` + FDP/HROT. `editor_stride` single shared
   world with `OfflineNetworkFactory`; `StrideHrotGame` drives `Game.Tick()`;
   `StrideNodeBootstrapper` boots; entities spawn (Brain spawn path, owned instantly).
   `StrideVisualBindingSystem` (§6.5) instantiates models/placeholders from
   `StrideRenderModelDefDto`; movement stubbed by a trivial kinematic mover.
1. **Bullet movement + reverse-sync (core).** `StrideKinematicsModule` (FDP integrators off);
   `PhysicsBodyLifecycleSystem`; `BulletCharacterMotor` / `KinematicVehicleMotor`;
   `BulletReverseSyncSystem` in a `TogglablePostSimulationGroup` honoring the velocity invariant;
   `SplitAuthorityStrideSyncScript`; fixed timestep. The character/vehicle wiring lands here, on
   the MainScene's existing Bullet static collision.
2. **Navmesh + crowd.** `StrideNavmeshBaker` + `DotRecastNavmeshProvider` (bake at load,
   per-layer, from MainScene geometry); refactored `CrowdAgentUpdateSystem` (`CrowdMotorIntent`)
   → `BulletCharacterMotor`; road mode via `ZoneEnvironmentData` in parallel; `Auto` selection.
   (Run `FakeNavmeshProvider` until DotRecast is wired.)
3. **Perception via Stride raycasts.** `StrideRaycastService` LOS/occlusion into the
   perception/`TargetMemory` path (real Z); ballistics raycasts back `BallisticsSystem`.
4. **Animation.** Wire `StrideAnimationBackend` (`Hrot.Stride.Animation`) + the
   `CharacterAnimationDefDto` content for the mannequin (Idle/Walk/Run blend + Jump montages);
   root-motion hooks unimplemented. (Run `FakeAnimationBackend` until ready.)
5. **Gizmos + editor dual-window.** `DebugPrimitiveRenderer3D`; raylib/ImGui editor as second
   OS window on the host thread; shared selection; `CenterOnEntityCommand`; record/replay via
   the togglable group.
6. **Mode 2 bring-up.** `StrideMuscleNodeApp` / `StrideMuscleNodeBootstrapper` mirroring
   `SimHostApp`; `NedNetworkFactory` + `DdsParticipant`; slave-only join to a separate
   `ClusterRunner` started with `cgf,orchestrator`; verify deferred authority handover drives
   `PhysicsBodyLifecycleSystem` and the sync fork; verify egress/dead-reckoning with the velocity
   invariant; diagnostic windows, no editor.

---

## 15. Consolidated open items (`[VERIFY-IN-SOURCE]`)

Resolve against live sources before/while coding; do not guess. (Items confirmed during the
v0.3 revision are marked ✅.)

1. **Math types** — engine names for `SimTransform` position/rotation; Euler order/handedness;
   Bullet quaternion winding. (§4)
2. ✅ **Module membership** — `GroundKinematicsModule` (`FDP.Toolkits/CarKinem/Modules`) /
   `SimHostCoreLogicPack` (`Hrot.SimHost`); still **[VERIFY]** the exact kept/excluded system
   list and the `IEcsModule` registration API. (§5.2)
3. **`CrowdAgentUpdateSystem`** current code; the velocity-only refactor with `CrowdMotorIntent`. (§5.3)
4. **`DeadReckoningSyncSystem`** construction with `DriveFromNetwork=false`. (§5.4)
5. ✅ **Authority query API** — `.WithOwned<T>()` / `.WithoutOwned<T>()` / `.Without<T>()`
   confirmed in `Fdp.Core.QueryBuilder`; authority lives in `EntityMetadataCold.AuthorityMask`
   (not `EntityHeader`). Still **[VERIFY]** the `DestructionOrder` event shape. (§5.6, §7)
6. **Bullet `Simulation`** — access point from a system/script; fixed-step config in GameSettings;
   the post-`PhysicsProcessor` ordering hook for the reverse-sync; single vs multi simulation. (§6.1)
7. **Perception/vision** fake-LOS entry point; the `RaycastSolver` / `HitResolution` seam;
   Bullet `Simulation.Raycast` signature. (§6.3)
8. **FDP phase enumeration** and slotting the reverse-sync into `Input` / pre-`Simulation`. (§8.3)
9. **Stride external-loop specifics** — Windows (SDL2) event-pump call; throttler/vsync flags;
   `Game.Tick()` on the 4.2.1.2487 NuGet. (§8.3)
10. ✅ **Offline editor composition** — `OfflineNetworkFactory`, `CgfLogicPack`, `SimHostCoreLogicPack`,
    `TogglableInputGroup`/`TogglableSimulationGroup`/`TogglablePostSimulationGroup`,
    `NetworkSpawningSystem` (`EditorNodeId = 0`), and the in-process orchestration pair —
    `ClusterConfiguration { Mandatory = Array.Empty<string>() }`, `ClusterMaster`, `ClusterSlave`,
    `OrchestrationLogicPack`, the **separate** `_orchestrationBus`, `TransitionStateIntent`,
    `ExecuteNodeOpIntent` — all confirmed in `EditorSubsystem`. (§8.1)
11. **Editor reuse** — `EditorApplication`/`IEditorLogic` ctor (`IReadOnlyList<IEcsModule>`),
    `PerspectiveWorkspaceRegistrar`, `AiAssetCatalogBuilder`, the panel classes and `WindowManager`
    registration. (§8.2)
12. **Record/replay hooks** — `TogglablePostSimulationGroup`, `ReferenceReplayLoadHandler`,
    `PlaybackTickSystem`, `IRecordReplayController` transitions; whether the physics step must be
    explicitly paused in replay. (§9)
13. **Navigation singletons** — `INavmeshProvider` registration; `ZoneDefinitionDto` /
    `RoadNetworkPath` / `ZoneEnvironmentData` / `RoadRadiusThresholdSq`; fakes→real module swap. (§10)
14. **`DebugPrimitive`** current struct (header + union, names, offsets) — read from source,
    never hardcode; `Stride.DebugRendering` immediate-mode API. (§11)
15. ✅ **`StrideNodeBootstrapper`** constructor injection points confirmed (4 optional `IEcsModule`,
    in `Hrot.StrideMock`). (§1.1, §3)
16. **Mode-2 node bootstrap** — `DdsParticipant`, `NedNetworkFactory`,
    `NodeBootstrapper.BuildOrchestration`, `ConfigureForNode`, `ISlaveOrchestrationTranslator`,
    `HrotNodeConfig.NodeId`, `ClusterConfiguration.Mandatory`. (§13)
17. **Egress/dead-reckoning** — `GeoSpatialEgressTranslator`, `NetworkTransform`,
    `NetworkVelocity`, `SmoothingRate`, the displacement/rotation thresholds. (§13.4)
18. **Stride asset loading** — `Content.Load<Model>(url)`, clone/instantiate under a parent,
    attach `ModelComponent`/`AnimationComponent`; building a Bullet collider in code per
    `CollisionShapeKind`. (§6.5)

---

## 16. Summary

- **Seam unchanged.** `StrideNodeBootstrapper` (in `Hrot.StrideMock`) is direction-oblivious and
  reused as-is; the future proprietary engine swaps in at the same line.
- **Authority is Stride/Bullet, per-component, runtime-transferable.** FDP integrators off; Bullet
  owns motion; the reverse-sync writes `SimTransform` *and* `SimVelocity` (lin+ang, zeroed on
  arrest) once per frame after the physics processor, before the FDP tick reads them. Body
  lifecycle and the sync fork key off `.WithOwned<SimTransform>()` (authority in
  `EntityMetadataCold`), so they serve Mode 1 (instant ownership) and Mode 2 (deferred handover)
  with identical code.
- **Entity → model/collider binding is explicit** via the engine-specific TKB descriptor
  `StrideRenderModelDefDto` + `StrideVisualBindingSystem` (capsule humans, oriented-box vehicles,
  procedural-primitive fallback). The arena environment is the authored `MainScene` (native Bullet
  collision; no conversion).
- **Mode 1 = single shared ECS simulation world** (Brain + Stride-Muscle logic packs,
  `OfflineNetworkFactory`) **plus** an in-process `ClusterMaster`/`ClusterSlave` orchestration
  pair over a **separate** in-memory bus (empty mandatory list) driving state transitions and
  replay; editor authoring surface reused via `IEditorLogic` and decoupled panels — Stride owns the
  loop, raylib/ImGui editor pumped on the same thread.
- **Mode 2 = slave-only networked node** mirroring `SimHostApp`: `DdsParticipant` +
  `NedNetworkFactory`, `ClusterSlave` + `ISlaveOrchestrationTranslator`, remote
  Brain/ClusterMaster (a `ClusterRunner` started with `cgf,orchestrator`), no editor, diagnostic
  windows kept. The single-process "dream" topology is deferred.
- **Existing contracts honored** — navigation (dual-mode DotRecast + kept road graph),
  perception (Stride raycasts, authoritative 3D Z, terrain-query pipeline removed), animation
  (`StrideAnimationBackend` in `Hrot.Stride.Animation`, optional root-motion hooks), record/replay
  (togglable reverse-sync group + `PlaybackTickSystem`).
- **Bring-up is incremental** under one authority model, fakes as stand-ins, no throwaway
  authority direction; Mode 1 first, Mode 2 last.

*End of Stride Integration v0.3. Design-level decisions are settled and architect-reviewed;
§15 items are source-confirmation tasks.*
