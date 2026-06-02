# Stride Integration — Design v0.1

> **Status.** Detailed design, ready for implementation by an AI coding agent that
> has the full FDP/HROT engine sources and the Stride .NET 8 / Bepu sources available.
> **Audience.** Implementation lead (AI coding agent) and reviewer.
> **Goal.** Add a Stride3D-based node that takes over SimHost's responsibilities —
> physics, movement, perception, navmesh planning, character animation, and 3D
> visualization including gizmos — as the **real** backend behind the engine's
> existing fake/abstracted interfaces. Stride is a temporary placeholder for a future
> proprietary 3D/physics/navmesh/animation engine; this design is therefore written
> so the same seams accept that engine later with no FDP-side changes.
> **Reads alongside.** `DD-1_MuscleCharacterRuntime` (animation backend seam),
> `Navigation_Design_v2_0` (provider interfaces, dual-mode pathing),
> `3D_Cognitive_Spatial_Awareness_Promotion` (authoritative altitude),
> `EQS_Design`, and the HROT subsystem docs for `StrideNodeBootstrapper`,
> `ClusterRunner`, `SimHost`, `IG`, `Editor`.

---

## 0. Reading guide and conventions

This document specifies **one** node type — a combined
`MuscleGround | Perception | NavigationSolver | ImageGenerator` node whose
authoritative physics and geometry come from Stride/Bepu rather than from FDP's
internal kinematics. It specifies that node in **one run mode now** (the all-in-one
authoring editor, no network) and reserves a **second run mode for later** (networked,
no CGF). The seam is identical in both; only network-factory wiring and the presence
of the editor window differ.

**Source-of-truth precedence.** Where this design names an engine type, system, field,
or layout, the **current engine source is authoritative**. This document gives the
*shape and responsibility*; the agent must confirm exact signatures, field names, byte
offsets, constructor parameters, and phase names against the live sources before coding.
Several spots are flagged **[VERIFY-IN-SOURCE]**. Do not hardcode any struct byte layout
from this document.

**Naming.** New projects/namespaces proposed below are recommendations; if the codebase
has an established convention that conflicts, follow the codebase.

**The one load-bearing safety invariant**, stated once here and relied on throughout:

> **Exactly one thread ever touches `EntityRepository` at a time.** `EntityRepository`
> component access is not thread-safe by design. In the all-in-one editor this is
> guaranteed structurally by pumping Stride and the raylib/ImGui editor sequentially on
> a single OS thread. Every Stride GPU call stays on the Stride/host thread; every
> raylib/OpenGL/ImGui call stays on that same thread; there is no second thread to race.

---

## 1. The core idea: Stride is the *real* backend, not a renderer

The FDP/HROT codebase is built, at every authority-bearing layer, around a
**fake-then-real** pattern: an engine-agnostic interface, a deterministic *fake*
implementation that unblocks development, and a *real* implementation that connects to
an actual 3D engine. Animation has `IAnimationBackend` → `FakeAnimationBackend` /
`StrideAnimationBackend`. Navigation has `INavmeshProvider` / `IDtCrowdProvider` /
`IVolumetricPathProvider` → fakes → DotRecast / dtCrowd. SimHost's kinematics,
perception, and ground queries are themselves the *fakes* that let the CGF (Brain) node
be developed against a cheap, flat, deterministic stand-in.

**This integration makes Stride the real backend.** "Stride takes SimHost's
responsibilities" means: implement the real backends behind interfaces that already
exist, and make Stride/Bepu physics authoritative for entity movement. It does **not**
mean bolting physics onto a visualizer, and it does **not** mean Stride merely renders
FDP-computed state.

### 1.1 The seam

`StrideNodeBootstrapper` is the engine-agnostic seam and is reused **unchanged**. Its
tick contract (`Context.Kernel.Update(dt)`) merely advances the FDP module topology and
flushes the event bus; it is oblivious to data-flow direction, and it accepts domain
modules via constructor injection. Everything *above* the bootstrapper is Stride-specific
and lives in the Stride projects; everything *below* it is engine-agnostic FDP and must
never reference Stride. The future proprietary engine swaps in at exactly this line.

### 1.2 Authority model — Stride/Bepu physics authoritative (the target, built now)

Entity movement is owned by **Bepu physics** running inside Stride. FDP's
movement-integrator systems are turned off on this node. Each fixed physics substep,
a **reverse-sync** writes the Bepu-resolved pose back into `SimTransform`, so the rest of
FDP (perception, EQS, navigation status, replication, animation dispatch, Brain-facing
contracts) continues to operate on `SimTransform` exactly as before — it simply now reads
a value sourced from physics rather than from FDP kinematics.

```
Authority direction (this node, locally-owned entities):

   dtCrowd / nav desired-velocity ──► Bepu CharacterComponent.Move() / CarController
                                              │  (fixed 60 Hz substep)
                                       Bepu solves collisions, resting contact, motion
                                              │
                            AfterSimulationUpdate: reverse-sync
                                              │
                              SimTransform.Position / .Rotation / SimVelocity   (authoritative)
                                              │
                          FDP kernel tick: perception, EQS, nav status, replication,
                                           animation dispatch, combat — all read SimTransform
                                              │
                                       Stride render + gizmos
```

This is the production-shaped configuration: the future proprietary engine will likewise
own physics, so building reverse-sync now (rather than an FDP-authoritative forward-sync
that would be thrown away) avoids building the wrong direction first.

### 1.3 Why not a phased FDP-authoritative stage first

An earlier-phase "FDP kinematics authoritative, Stride renders" mode was considered and
**rejected as a target**, because its transform-sync direction is the *opposite* of the
final one: it would be deleted and rebuilt when physics authority arrives, and an AI
agent building it first risks confusing the authority direction. Instead, safe
incremental bring-up comes from the **existing fakes** standing in for not-yet-real
capabilities (run with `FakeNavmeshProvider` before DotRecast lands, `FakeAnimationBackend`
before the Stride animation backend is wired, a trivial kinematic mover before the full
Bepu controllers exist), under the *same* reverse-sync authority model throughout. See
§11 for the bring-up roadmap.

---

## 2. Run modes

| Mode | Network | CGF (Brain) | Editor window | Build now? |
|---|---|---|---|---|
| **1 — All-in-one editor** | none (offline) | in-process | yes (raylib/ImGui, 2nd OS window) | **Yes** |
| **2 — Networked, no CGF** | DDS/NED | separate ClusterRunner process | optional | Later |

Mode 1 is the deliverable. Mode 2 is reserved; §10 records what changes for it so the
design does not foreclose it, but no Mode-2 code is built in this pass.

The combined role `MuscleGround | Perception | NavigationSolver | ImageGenerator` maps
directly onto the existing `StrideNodeBootstrapper.Role`. In Mode 1 the role still drives
which FDP modules/systems are assembled; it just runs with an offline network factory so
no DDS traffic occurs.

---

## 3. Project and namespace layout

Leave the existing `Hrot.StrideMock` (headless fake) and `Hrot.FakeStrideApp`
(raylib host for the mock) **untouched** — they remain useful for CI/headless tests.
Add new projects for the real Stride integration.

```
Hrot.Stride.Core            (net8.0 class library)
  refs: Stride.Engine, Stride.BepuPhysics, Stride.Rendering, Stride.Games,
        DotRecast.*, the FDP/HROT engine assemblies
  namespace Hrot.Stride.Core

    FdpStrideTransform              static: coordinate + rotation conversions (both directions)
    StrideKinematicsModule          IEcsModule: keeps spatial/command/nav systems, omits FDP integrators
    BepuReverseSyncSystem           writes Bepu-resolved pose → SimTransform (locally-owned)
    SplitAuthorityStrideSyncScript  reverse for owned, forward(ghost-filtered) for remote/replayed
    BepuCharacterMotor              feeds dtCrowd desired-velocity into Bepu CharacterComponent
    BepuVehicleMotor                feeds vehicle commands into Bepu CarController
    StrideNavmeshBaker              extracts Stride scene geometry → DotRecast bake (per NavLayerMask)
    DotRecastNavmeshProvider        INavmeshProvider real backend (registered as ECS singleton)
    DotRecastDtCrowdProvider        IDtCrowdProvider real backend (or wraps dtCrowd)
    StrideRaycastService            wraps BepuSimulation.RayCast for perception/ballistics
    DebugPrimitiveRenderer3D        sweeps ProducerBuffer → Stride.DebugRendering immediate primitives

Hrot.Stride.Animation       (net8.0 class library)   [may reuse existing Hrot.MuscleCharacter.Animation.Stride]
  namespace Hrot.MuscleCharacter.Animation.Stride
    StrideAnimationBackend          IAnimationBackend real backend (EXISTS — reuse/extend)
    PerEntityBlendTreeBuilder       IBlendTreeBuilder per registered entity (EXISTS)

Hrot.Stride.App             (Stride game project — created in the Stride editor)
  the .sdpkg, scene, terrain/obstacle/model/animation assets, GameSettings (Bepu config)
  namespace Hrot.Stride.App

    StrideHrotGame                  Stride Game subclass; the process entry point and host loop
    StrideHrotGame.Mode1Editor      composition root for the all-in-one editor
    EditorWindowHost                owns the raylib/ImGui second OS window, pumped on the host thread
    OrchestrationLogicPack          IEcsModule wrapping the in-process ClusterSlave  [VERIFY name in source]
```

`StrideAnimationBackend` already exists in the codebase (`DD-1` §15). Reuse it; only feed
it real montage markers from the Stride asset import and wire it via the bootstrapper's
systems registrar.

---

## 4. `FdpStrideTransform` — the coordinate seam

A single pure static class owns every conversion between FDP world space and Stride world
space, in both directions. It is used by the reverse-sync, the forward-sync (ghost
visuals), the navmesh baker, the gizmo renderer, animation-entity placement, and editor
mouse-picking. Centralizing it is mandatory: a stray ad-hoc swizzle anywhere is the most
likely source of "everything is rotated/mirrored" bugs.

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
    // Position
    public static Stride.Core.Mathematics.Vector3 ToStridePosition(in FdpVector3 p);   // (p.X, p.Z, p.Y)
    public static FdpVector3                       ToFdpPosition(in Stride.Core.Mathematics.Vector3 s); // (s.X, s.Z, s.Y)

    // Rotation: FDP right-handed Z-Y-X (yaw-pitch-roll) <-> Stride left-handed quaternion.
    public static Stride.Core.Mathematics.Quaternion ToStrideRotation(in FdpRotation r);
    public static FdpRotation                         ToFdpRotation(in Stride.Core.Mathematics.Quaternion q);

    // Convenience for picking: Stride screen ray -> FDP world ray (for editor entity placement).
    public static FdpRay ScreenRayToFdp(in Stride.Engine.CameraComponent cam, Vector2 screenPx);
}
```

- `FdpVector3` / `FdpRotation` are the engine's existing math types **[VERIFY exact names]**.
  Do not leak `Stride.Core.Mathematics` types across the bootstrapper seam.
- The rotation conversion must account for the handedness flip (left↔right), not only axis
  relabeling. Validate with a known yaw (e.g. FDP heading due East) producing the correct
  Stride facing. **[VERIFY]** the exact Euler order the engine's `SimTransform.Rotation`
  uses and the quaternion winding Bepu expects.

---

## 5. Authority, modules, and systems on this node

### 5.1 What runs, what is excluded

`GroundKinematicsModule` **bundles** several systems. It must not be omitted wholesale,
because most of its contents are still required. Build a custom module instead.

| System / module | On this node | Notes |
|---|---|---|
| `CarKinematicsSystem` | **Excluded** | Replaced by Bepu `CarController`. |
| `LinearKinematicsSystem` | **Excluded** | Replaced by Bepu rigid/character bodies. |
| `SpatialHashSystem` | **Keep** | Pure consumer; rebuilds spatial grid from `SimTransform` + collider. |
| `FormationTargetSystem` | **Keep** | High-level command processing. |
| `VehicleCommandSystem` | **Keep** | High-level command processing. |
| `NavigationExecutionSystem` | **Keep** | Writes CQRS `NavigationStatus`; solver-agnostic. |
| `CrowdAgentUpdateSystem` | **Keep, refactored** | Velocity-only; must stop mutating `SimTransform` (§5.3). |
| `DeadReckoningSyncSystem` | **Keep, `DriveFromNetwork=false`** | Smoothing restricted to `Ghost` lifecycle (§5.4). |
| `TransformSyncSystem` | **Keep** | Reads `SimTransform`→`NetworkTransform` egress; mutates only remote entities — safe. |
| `TerrainQuerySubmitSystem` | **Excluded** | Geographic ground-clamp pipeline (§5.5). |
| `TerrainQuerySolverSystem` | **Excluded** | " |
| `TerrainQueryResolutionSystem` | **Excluded** | " — Bepu resting contact provides authoritative Z directly. |
| Perception / EQS / combat / ballistics / animation-dispatch / status | **Keep** | Read `SimTransform`; unaffected by authority inversion. |

### 5.2 `StrideKinematicsModule` — exclusion by topological omission

Create `StrideKinematicsModule : IEcsModule` that registers the **kept** systems from the
ground-kinematics bundle (`SpatialHashSystem`, `FormationTargetSystem`,
`VehicleCommandSystem`, `NavigationExecutionSystem`, plus the refactored
`CrowdAgentUpdateSystem`) and simply does **not** register `CarKinematicsSystem` or
`LinearKinematicsSystem`. Inject this module into `StrideNodeBootstrapper` in place of the
stock `GroundKinematicsModule`.

Do **not** attempt exclusion via a capability gate / component removal: the Stride
integration still requires entities to carry `SimTransform` and `SimVelocity` for the
reverse-sync to write into, so removing those components would break the very mechanism we
rely on. Exclusion is purely about which integrator systems get registered.

**[VERIFY-IN-SOURCE]** the exact `IEcsModule` registration API and the precise membership
of `GroundKinematicsModule`, so the custom module mirrors it minus the two integrators.

### 5.3 `CrowdAgentUpdateSystem` refactor (required)

Current behavior hardcodes integration (`tf.Position += velocity * dt`) because
`LinearKinematicsSystem` is configured to skip crowd agents. Under split authority this
fights Bepu. Refactor so the system:

- still polls dtCrowd for desired/steering velocity (`_dtCrowd.GetAgentVelocity(entity)`),
- writes **only** `SimVelocity` (or a dedicated motor-intent component — pick one and use
  it consistently; a dedicated `CrowdMotorIntent` component is cleaner because it keeps
  `SimVelocity` as a *result* of physics rather than an *input*),
- no longer writes `SimTransform`.

`BepuCharacterMotor` (§6.2) reads that velocity/intent and calls
`CharacterComponent.Move(direction)`. Bepu performs the collision-resolved motion; the
reverse-sync writes the resolved position back into `SimTransform`, and `SimVelocity` is
re-derived from the resolved motion in the reverse-sync if you used a dedicated intent
component.

This keeps dtCrowd as the *steering brain* (avoidance, corridor following) while Bepu is
the *mover/collider* — the conventional way to combine a crowd system with a physics
engine.

### 5.4 `DeadReckoningSyncSystem` — `DriveFromNetwork=false`

On a combined `MuscleGround | ImageGenerator` node this is the primary collision risk for
the reverse-sync. Instantiate it with `DriveFromNetwork = false` so dead-reckoning
smoothing applies **only** to `Ghost`-lifecycle entities (those owned by other nodes /
replayed), never to locally-owned physics bodies. Locally-owned bodies are driven solely
by Bepu → reverse-sync.

### 5.5 Terrain query pipeline — excluded

Under the 3D Cognitive Spatial Awareness promotion, `SimTransform.Position.Z` is the
single authoritative altitude (and `GroundClampingState` is deleted). Because Bepu
natively resolves resting contact against the collision terrain, the reverse-sync supplies
authoritative Z for free. The whole geographic query pipeline
(`TerrainQuerySubmitSystem` / `TerrainQuerySolverSystem` / `TerrainQueryResolutionSystem`)
is therefore omitted on this node, which also safely bypasses the now-redundant
`TerrainClampBaseline` jump-rejection filter (a continuous physics engine inherently
prevents the geometric popping that filter guarded against). EQS tests that need altitude
(`DistanceScoreTest`, `NavmeshReachableTest`, etc.) read 3D coordinates directly from
`SimTransform`.

---

## 6. Bepu physics integration

### 6.1 Simulation configuration and the fixed step

Use `Stride.BepuPhysics` (Bepu Physics v2; Stride's recommended engine — Bullet is being
phased out). Configure a **fixed 60 Hz** simulation substep via the Bepu Configuration in
GameSettings. Bepu drives its own substepping; we do **not** hand-roll an accumulator.

The two per-step hooks (via `ISimulationUpdate`, implemented on the relevant systems/
components) define the sync boundaries:

```csharp
// Conceptual placement — exact host wiring in §8.
public void SimulationUpdate(BepuSimulation sim, float simTimeStep)
{
    // BEFORE the physics step:
    // push desired velocities / move-intents into Bepu bodies & character controllers
    // (BepuCharacterMotor / BepuVehicleMotor consume CrowdMotorIntent / vehicle commands).
}

public void AfterSimulationUpdate(BepuSimulation sim, float simTimeStep)
{
    // AFTER the physics step:
    // BepuReverseSyncSystem reads resolved body poses and writes SimTransform/SimVelocity
    // for locally-owned entities (see §7). Wrapped in TogglablePostSimulationGroup (§9).
}
```

**[VERIFY]** how `ISimulationUpdate` participants are registered with the active
`BepuSimulation` in the current `Stride.BepuPhysics` package, and whether multiple
simulations are in play (we use a single simulation unless a reason emerges otherwise).

### 6.2 Motors — feeding Bepu controllers

- `BepuCharacterMotor`: for humanoid `CrowdAgent` entities. Reads the refactored crowd
  output (desired velocity / `CrowdMotorIntent`), applies the stance speed multiplier
  (Standing/Crouched/Prone, per the Navigation animation seam), and calls
  `CharacterComponent.Move(direction)` (and `TryJump()` where a traversal requires it).
- `BepuVehicleMotor`: for wheeled/tracked entities. Translates `VehicleCommandSystem`
  output into Bepu `CarController` inputs (throttle/steer/brake). Naval/flying are out of
  scope for this pass (naval would be a `CarController`-style surface mover later; flying
  is deferred per the navigation design).

Each Stride physics entity (rigid body / character controller) **is** the entity's spatial
representation. The animation entity (carrying `AnimationComponent` + skeletal model) is
the same Stride entity or a child of the physics body, so animation pose composes on the
physics-driven transform with no extra copy (DD-1 §15.3 option B). Off-mesh-link traversal
montages and root motion: see §6.4.

### 6.3 Raycasts — perception and ballistics

`StrideRaycastService` wraps `BepuSimulation.RayCast(origin, dir, maxDistance, out HitInfo,
collisionMask)` (and `RayCastPenetrating` / `Overlap` / sweep as needed). Two consumers:

- **Perception / LOS occlusion.** On this node, line-of-sight visibility checks raycast
  against real Stride scene collision geometry (true occlusion, real Z), replacing
  SimHost's flat spatial-hash approximation. Feed results into the existing
  perception/`TargetMemory` pathway (now 3D per the cognitive promotion). **[VERIFY]** the
  exact perception/vision system entry point that currently performs the fake LOS test, so
  the Stride raycast is injected behind the same interface rather than alongside it.
- **Ballistics.** FDP's `BallisticsSystem` is kept (analytic projectile integration is
  better than a physics engine for fast projectiles, which tunnel). It consumes
  `StrideRaycastService` for hit detection via the existing `RaycastSolver` /
  `HitResolution` seam. **[VERIFY]** that seam's interface so the Stride raycast backs it.

All ray inputs/outputs cross `FdpStrideTransform`.

### 6.4 Animation and root motion

Reuse `StrideAnimationBackend` (`IAnimationBackend`, `IBlendTreeBuilder`-based; DD-1 §15).
Wire it via the bootstrapper's systems registrar / `AnimationMuscleModule`. Continuous
locomotion is driven by the existing `AnimationRuntimeBridgeSystem` reading `SimTransform`
+ `SimVelocity` (now physics-sourced) and calling `UpdateLocomotionInputs`. Discrete
traversal montages come from `OffMeshLinkDetectionSystem` writing `AnimationChannel.PlayMontage`.

**Root motion** is supported at the interface level but **not implemented in this pass**.
DD-1 §19 specifies the additive shape: `IAnimationBackend.ExtractRootMotionDelta(handle)`,
a `RootMotionApplicatorSystem`, a `SuppressLinearKinematics`/`UsesRootMotion` per-entity
flag, and an `IsLocalAuthoritativeOnly` guard. Because some assets need root motion (e.g.
wild rolls) and most do not (walk/jog/run), this stays per-asset and opt-in. Note the
interaction with physics authority: when root motion *is* later enabled for an entity, the
root-motion delta and the Bepu controller both want to move it — the design must choose one
per entity (root-motion-active entities should drive the Bepu body kinematically from the
extracted delta, not be double-moved). Leave hooks; do not implement.

---

## 7. The split-authority sync script

`SyncFdpToStrideScript` (the mock's script) is hardcoded **forward-sync** — in its entity
pass it overwrites `strideEntity.Position = xform.Position` for every entity with
`SimTransform`. Under physics authority this overwrites Bepu's freshly resolved positions
every frame, causing jitter or freezing locally-owned entities. It must be **rewritten**
as `SplitAuthorityStrideSyncScript`, forking by ownership/lifecycle:

- **Locally-owned physics bodies → reverse-sync (Stride → FDP).** Handled by
  `BepuReverseSyncSystem` in `AfterSimulationUpdate`: read resolved Bepu pose, write
  `SimTransform.Position`/`.Rotation` (and re-derive `SimVelocity`) via `FdpStrideTransform`.
  The forward path must **skip** these entities entirely — never write Stride transforms
  for locally-owned bodies (the Bepu body already *is* their transform).

- **Remote / ghost / replayed entities → forward-sync (FDP → Stride).** These have no local
  Bepu authority; their `SimTransform` is driven by `DeadReckoningSyncSystem` (network
  ghosts) or `PlaybackTickSystem` (replay). The forward pass reads their `SimTransform` and
  writes the corresponding Stride visual entity transform (via `FdpStrideTransform`), so
  they render correctly without a local physics body. Filter the forward query explicitly
  to ghosts/remote — e.g. `.WithLifecycle(EntityLifecycle.Ghost)` or
  `.WithoutOwned<SimTransform>()` **[VERIFY exact query API]**.

The two-pass differential upsert/teardown algorithm from the mock's script (generational
`IsAlive` check, reused stale-entity list, cluster-state gate) is still useful for managing
the *Stride-side visual entity set* (spawn on appear, despawn on disappear). Reuse that
*structure*; change the *direction* of the transform write per the fork above.

```
SplitAuthorityStrideSyncScript (runs as part of the host frame; see §8):

  Pass A — entity set reconciliation (both ownership classes):
     for each FDP entity newly alive  -> ensure a backing Stride entity exists
     for each Stride entity whose FDP entity died -> tear down (reuse stale list)

  Pass B — transform application:
     locally-owned physics bodies   -> NO-OP here (Bepu owns; reverse-sync already wrote SimTransform)
     ghost / remote / replayed       -> strideEntity.Transform = FdpStrideTransform.ToStride(SimTransform)
```

`BepuReverseSyncSystem` (the reverse path) is a **separate** system from this script,
wrapped in `TogglablePostSimulationGroup` (§9); the script above handles only entity-set
reconciliation and the *forward* (ghost) transform writes.

---

## 8. The host loop (Mode 1 — all-in-one editor)

### 8.1 Threading and window pumping

Stride owns the process. Run Stride from an **external host loop** via `Game.Tick()`
(Stride supports external-main-loop driving; the internal `ThreadThrottler` is disabled in
this mode and the host is responsible for pumping the Stride window's OS events each
iteration). The raylib/ImGui editor window is a **second OS window pumped on the same
thread**, sequentially, every frame. There is no second thread, so `EntityRepository` is
single-threaded by construction.

```
[single OS host thread, per frame]

  1. pump Stride window OS events                      [VERIFY exact call for current
                                                        Stride Windows backend — SDL2;
                                                        analogous to old Application.DoEvents()]
  2. strideGame.Tick(dt):
        - Bepu fixed substep(s) at 60 Hz:
              SimulationUpdate       -> motors push intents into Bepu bodies
              <physics step>
              AfterSimulationUpdate  -> BepuReverseSyncSystem writes SimTransform (owned)
                                        [TogglablePostSimulationGroup — disabled during replay]
        - StrideNodeBootstrapper tick: _core.Tick(dt)  (FDP kernel: perception, nav,
              EQS, combat, ballistics, animation dispatch, status, replication)
              -> reads the fresh, physics-sourced SimTransform
        - SplitAuthorityStrideSyncScript: entity-set reconcile + ghost forward-sync
        - StrideAnimationBackend tick (blend trees) + Stride render (3D window)
        - DebugPrimitiveRenderer3D sweeps ProducerBuffer -> 3D gizmos
  3. drain editor authoring intents (mutate EntityRepository here — world is idle)
  4. raylib BeginDrawing():
        - 2D map canvas reads EntityRepository (single-threaded, safe)
        - DebugPrimitiveRenderer2D sweeps ProducerBuffer -> 2D gizmos
        - rlImGui panels (inspector, ORBAT, mission, spawner, preview)  [ImGui on this thread only]
     raylib EndDrawing()
```

**Ordering rationale.** The reverse-sync runs inside `Tick` *before* `_core.Tick(dt)` so
that FDP `Simulation`-phase consumers (`SpatialHashSystem`, vision broadphase, EQS) read the
correct physical positions for the frame. The architect's guidance: the reverse-sync should
execute immediately before the FDP kernel's `Simulation` phase — slot it as a custom `Input`
phase system or run it sequentially just prior to `_core.Tick(dt)`. **[VERIFY]** the exact
phase enumeration and how to slot a system into `Input` (or pre-`Simulation`).

**Frame timing / vsync.** Avoid compounding two vsync waits (Stride's and raylib's
`EndDrawing`). Disable Stride's throttler in external-loop mode and let one governor cap the
frame (the 60 Hz physics substep is the real simulation clock regardless of render rate).
**[VERIFY]** the external-loop throttler/vsync flags on the current Stride build.

**Graphics contexts do not conflict.** Stride uses Direct3D (Windows); raylib uses its own
GLFW/OpenGL context; ImGui is the raylib (`rlImGui`) instance only. They are separate APIs,
separate windows, separate device contexts — nothing is shared, so there is no
context-sharing hazard. The only rule is thread-affinity: all Stride GPU calls and all
raylib/OpenGL/ImGui calls stay on this one host thread (trivially satisfied here).

### 8.2 In-process cluster orchestration (no network)

There is no Orchestrator process, so stand up an in-process master/slave on a shared bus.
Reuse the offline path that already exists in `Hrot.Editor.EditorSubsystem` /
`Hrot.Editor.EditorApplication` rather than reinventing it.

- Construct a `ClusterConfiguration` with `Mandatory = Array.Empty<string>()`. The empty
  mandatory list makes `ClusterMaster` immediately release its bootstrap latch and publish
  the initial `Standby` state.
- Construct `ClusterMaster` with that configuration and a dedicated orchestration
  `FdpEventBus`.
- Construct the local `ClusterSlave` on the **same** `FdpEventBus` instance. Wrap the slave
  in an `OrchestrationLogicPack` (`IEcsModule`) and register it with the `ModuleHostKernel`
  so it ticks sequentially within the host loop.
- Drive the cluster state by publishing `Fdp.Toolkit.Orchestration.TransitionStateIntent`
  (with a fresh `TransactionId`, the `TargetState` — e.g. `ClusterState.OperatingEdit` /
  `OperatingLive` — and the `ScenarioId`) to the shared bus via `PublishManaged`. The local
  master consumes it, plans the transition, and fans out `ExecuteNodeOpIntent` to the slave.

Use `OfflineNetworkFactory` (from `Hrot.Editor`) so DDS is a null-object; no DDS allocation
or traffic occurs. **[VERIFY]** exact `ClusterConfiguration` / `ClusterMaster` /
`ClusterSlave` / `OrchestrationLogicPack` constructor signatures and the
`TransitionStateIntent` field names against source.

### 8.3 The editor window — view + input shell only

`EditorSubsystem` (and the rich UI it already provides: 2D map, ORBAT, mission service,
spawner, property inspector, preview/rewind, gizmos, AI hot-reload) is reused, but in Mode
1 it is reduced to a **view + input shell over the shared world**:

- It does **not** instantiate or tick a `ModuleHostKernel`. (This is a real change from how
  the editor is composed today; its current composition root builds a kernel — Mode 1's must
  not.) There is exactly one kernel, ticked by Stride inside `Game.Tick()`.
- It reads the shared `EntityRepository` directly during the editor pump (step 3–4 above),
  which is safe because the world is idle on this single thread at that point.
- Authoring actions are **intents**: the editor enqueues commands
  (`ActivateEditorToolEvent`, gizmo→command, spawn, etc.) that the host thread drains at the
  top of the next frame (step 3). It already works this way (`DrainToolActivationEvents`);
  keep that pattern — it is now mandatory, not merely advisable.

**Shared selection, independent cameras.** `SelectionState` is an ECS component; the raylib
2D selection interaction mutates it in the shared repository (during the editor pump, world
idle — safe), and the Stride 3D view + gizmo projectors read the same component, so
"select in 2D → highlighted in 3D" works for free. Cameras are independent (raylib 2D-ortho
`MapCamera`; Stride native 3D). For "focus both views on a unit," publish a
`CenterOnEntityCommand` on the bus; both the raylib canvas and a small Stride camera script
consume it and independently animate to the entity's `SimTransform`. (The Stride camera
script is a small new piece beside the sync script.)

> **Upgrade path (not built now).** If the shared single-thread frame budget ever becomes
> limiting (heavy 3D frame stalling the 2D pump), the sanctioned concurrent model is:
> raylib on its own thread reading a `DoubleBufferProvider` → `ISimulationView` replica
> published at a tick boundary, with authoring writes (and selection mutation) marshalled
> back to the Stride thread as intents. Do not build this until profiling demands it; under
> it, selection mutation must become an intent rather than a direct write.

---

## 9. Recording and replay

`RecordingModule` / `ReplayModule` run in the all-in-one editor (not only on networked
nodes). Recording captures this node's own authoritative, now physics-sourced `SimTransform`
each tick to local disk. Replay is per-node playback of that recording — it does **not**
re-simulate physics and has **no** dependency on physics determinism. (This is why
Stride/Bepu non-determinism is irrelevant: a checkpoint carries full recorded component
state, and replay restores it rather than recomputing it.)

**Do not** gate physics/reverse-sync by polling cluster state. Use the engine's lifecycle
hook:

- Wrap `BepuReverseSyncSystem` (the reverse-sync) in a **`TogglablePostSimulationGroup`** and
  pass that group to `ReferenceReplayLoadHandler`.
- On `PrepareReplay`, the handler flips the group's `Enabled = false`, cleanly severing the
  physics integration so it cannot overwrite historical positions.
- `ReplayModule` registers its `PlaybackTickSystem` **outside** any togglable group, so it
  keeps driving `SimTransform` from recorded keyframes while reverse-sync is suspended.
  Animation and Stride render read the replayed `SimTransform`; the forward (ghost) path of
  the sync script renders them.
- On `FinalizeReplay` / `PrepareLive`, the handler flips `Enabled = true`, instantly
  returning spatial authority to Bepu.

Also suppress the Bepu physics *step itself* during replay (not just the reverse-sync) so
bodies do not drift while playback drives transforms — **[VERIFY]** whether toggling the
reverse-sync group is sufficient (bodies frozen because no motor intents are applied and
nothing reads their output) or whether the simulation step must be explicitly paused;
prefer binding to the same lifecycle transition rather than a hand-rolled flag.

**[VERIFY-IN-SOURCE]** `TogglablePostSimulationGroup`, `ReferenceReplayLoadHandler`,
`PlaybackTickSystem`, and the `IRecordReplayController` transition names
(`PrepareReplay` / `FinalizeReplay` / `PrepareLive`).

---

## 10. Navigation — dual-mode, real backends

Navigation keeps its full existing contract (`Navigation_Design_v2_0`); this node provides
the **real** providers and both data sources for `Auto` backend selection.

### 10.1 Navmesh mode

`StrideNavmeshBaker` extracts collision/terrain triangles from the loaded Stride scene,
swizzles them via `FdpStrideTransform`, and bakes per-`NavLayerMask` navmeshes with the
layer-specific rasterization parameters (Infantry 0.3 m radius / 60° slope; Vehicle 1.5 m /
20° / 0.1 m step; Naval surface) using **DotRecast** (pure .NET; no Stride coupling at query
time). The result is wrapped in `DotRecastNavmeshProvider : INavmeshProvider` and registered
as the managed `INavmeshProvider` ECS singleton. Bake trigger: **scenario load**.

`DotRecastDtCrowdProvider : IDtCrowdProvider` provides local avoidance / steering. Its
desired velocity feeds `BepuCharacterMotor` (§6.2), per the "dtCrowd steers, Bepu moves"
reconciliation.

Prefer **DotRecast directly** over Stride's built-in navmesh: Stride's built-in does not
cleanly expose the per-layer bakes the navigation design requires, and DotRecast keeps the
provider contract identical to the fakes (drop-in `NavigationFakesModule` →
`NavigationDotRecastModule` swap).

### 10.2 Road-graph mode

The road graph is authored/loaded **independently** of the Stride scene; keep them
consistent by convention (no engine pipeline derives the road graph from scene splines).
Mechanism: the scenario envelope JSON includes a `Zones` section whose `ZoneDefinitionDto`
carries a `RoadNetworkPath`; the standard orchestration (`HrotScenarioLoadHandler` →
`ZoneManagerService` → `RoadNetworkLoader.LoadFromJson`) materializes the `RoadNetworkBlob`
into the `ZoneEnvironmentData` ECS singleton (format requires pre-computed Hermite control
points + spatial grid).

### 10.3 Auto selection

With both singletons present at scenario load, `PathfindingSolverSystem` performs `Auto`
selection by `BackendForce` and `MobilityProfile`: endpoints within `RoadRadiusThresholdSq`
of road nodes → `RoadGraph`; mixed → spliced `Hybrid`; else → `Navmesh`; `Flying` →
`IVolumetricPathProvider` (deferred). Off-mesh-link traversal montages route through the
animation seam unchanged.

**[VERIFY-IN-SOURCE]** `ZoneDefinitionDto` / `RoadNetworkPath` / `ZoneEnvironmentData` /
`RoadRadiusThresholdSq` names, and the `INavmeshProvider` singleton registration point.

---

## 11. 3D gizmo rendering

Local ECS gizmo producers (e.g. the data-driven and stateless gizmo systems) write
`DebugPrimitive`s into the `ProducerBuffer`. In the no-network editor there is no DDS
ingress, so `ConsumerBuffer` stays empty; **both** the raylib 2D renderer and the new
Stride 3D renderer sweep the `ProducerBuffer`.

`DebugPrimitiveRenderer3D` mirrors the existing 2D renderer's two-pass scheme:

1. **Pass 1 — anchors.** Sweep primitives; cache each spatial-anchor primitive by its
   network id, recording its anchor world position and heading/pitch/roll.
2. **Pass 2 — shapes.** For each semantic-shape / line primitive, resolve it against its
   anchor into absolute world coordinates (the renderer writes the resolved world transform
   into the primitive's spare payload, in-place), swizzle via `FdpStrideTransform`, and emit
   as a 3D primitive through the `Stride.DebugRendering` immediate-mode subsystem (lines /
   boxes / spheres in world space). Fallbacks if the immediate API is insufficient: a
   `DebugRenderer` render-stage in the `GraphicsCompositor`, or a dynamic `Mesh`.

> **Do not hardcode `DebugPrimitive` field offsets or the union layout from any external
> document.** The `DebugPrimitive` struct (64-byte cache-line cap, header + union payload)
> is engine source-of-truth and may change before this is implemented. Read the current
> struct definition from source and bind to it. Conceptually: the `SpatialAnchor` payload
> carries a network id, an anchor world position, and heading/pitch/roll; the
> `SemanticShape` payload carries a profile id, length/width, a condition mask, and spare
> bytes the renderer overwrites with the resolved world position and yaw/pitch/roll during
> pass 2. Confirm exact field names, types, and offsets in source.

**[VERIFY]** the current `Stride.DebugRendering` immediate-mode API surface and how to
register/obtain its debug-draw entry point from the running game.

---

## 12. Scenario / asset authoring

The Stride project (`Hrot.Stride.App`) is created in the Stride editor and carries the
scene, terrain, obstacles, skeletal models, animation clips, and the Bepu GameSettings
configuration. Authoring workflow:

- Terrain + obstacles provide collision geometry for Bepu (resting contact → authoritative
  Z) and source geometry for the DotRecast bake.
- Skeletal models + animation clips back `StrideAnimationBackend`; montage markers are
  imported as Stride clip keyframe events (DD-1 §15.4) and mapped to `MontageId` /
  `TraversalKind` via `CharacterAnimationDefDto`.
- The road network JSON is authored separately and referenced from the scenario envelope's
  `Zones` section.
- The scenario envelope drives orchestration (`OperatingEdit` for authoring,
  `OperatingLive` for play, `OperatingReplay` for playback) via `TransitionStateIntent`.

---

## 13. Mode 2 (reserved — not built in this pass)

Recorded here so the design does not foreclose it. Changes from Mode 1:

- Replace `OfflineNetworkFactory` with the real `NedNetworkFactory`; DDS/NED active.
- `ClusterMaster` + Orchestrator live in a separate ClusterRunner process; this node runs
  only the `ClusterSlave`, reaching the remote master over DDS — slave-side code identical
  to Mode 1.
- CGF (Brain) runs in a separate process; navigation/mission/fire intents cross DDS. This
  node remains authoritative for its own entities (`MuscleGround` ⇒ not ghost-only), so its
  `SimTransform` replicates out; remote entities arrive as ghosts driven by
  `DeadReckoningSyncSystem` and rendered by the forward path of the sync script.
- The gizmo 3D renderer additionally sweeps `ConsumerBuffer` (e.g. CGF behavior-tree debug
  overlays arriving over DDS).
- Watch the documented self-loopback hazard for a combined `MuscleGround + ImageGenerator`
  node (`DriveFromNetwork=false` already handles the dead-reckoning side).

The seam (`StrideNodeBootstrapper`, the split-authority sync, the reverse-sync, the
providers) is unchanged; only network-factory wiring and master location differ.

---

## 14. Bring-up roadmap (incremental, under one authority model)

Authority direction is reverse-sync from day one; safe increments come from the existing
fakes standing in for not-yet-real capabilities. Each step is independently runnable.

0. **Scene + render + bootstrap.** Stride scene loads; `StrideHrotGame` drives `Game.Tick()`
   from the host loop; `StrideNodeBootstrapper` boots with `OfflineNetworkFactory` and the
   in-process master/slave; entities spawn and render. Movement stubbed by a trivial Bepu
   kinematic mover (not FDP kinematics) to prove spawn → reverse-sync → render. Smallest
   viable node.
1. **Bepu movement + reverse-sync (core).** `StrideKinematicsModule` (FDP integrators off);
   `BepuCharacterMotor` / `BepuVehicleMotor`; `BepuReverseSyncSystem` in a
   `TogglablePostSimulationGroup`; `SplitAuthorityStrideSyncScript`; fixed 60 Hz. The big
   vehicle/character rewrite lands here.
2. **Navmesh + crowd.** `StrideNavmeshBaker` + `DotRecastNavmeshProvider` (bake at load,
   per-layer); refactored `CrowdAgentUpdateSystem` (velocity-only) → `BepuCharacterMotor`;
   road mode via `ZoneEnvironmentData` in parallel; `Auto` selection. (Run with
   `FakeNavmeshProvider` until DotRecast is wired.)
3. **Perception via Stride raycasts.** `StrideRaycastService` LOS/occlusion into the
   perception/`TargetMemory` path (real Z); ballistics raycasts back `BallisticsSystem`.
4. **Animation.** Wire `StrideAnimationBackend` + montage markers; root-motion hooks left
   unimplemented. (Run with `FakeAnimationBackend` until ready.)
5. **Gizmos + editor dual-window.** `DebugPrimitiveRenderer3D`; the raylib/ImGui editor as
   second OS window pumped on the host thread; shared selection; `CenterOnEntityCommand`;
   record/replay via the togglable group.

---

## 15. Consolidated open items (`[VERIFY-IN-SOURCE]`) for the coding agent

Resolve each against the live sources before/while coding; do not guess.

1. **Math types.** Exact engine names for `SimTransform`'s position/rotation types and the
   Euler order/handedness of `SimTransform.Rotation`; Bepu quaternion winding. (§4)
2. **`GroundKinematicsModule` membership** and the `IEcsModule` registration API, so
   `StrideKinematicsModule` mirrors it minus `CarKinematicsSystem` / `LinearKinematicsSystem`. (§5.2)
3. **`CrowdAgentUpdateSystem`** current code and the cleanest velocity-only refactor
   (dedicated `CrowdMotorIntent` vs `SimVelocity`). (§5.3)
4. **`DeadReckoningSyncSystem`** construction with `DriveFromNetwork=false`. (§5.4)
5. **`ISimulationUpdate` registration** with the active `BepuSimulation`; single vs multi
   simulation; fixed-step config location in GameSettings. (§6.1)
6. **Perception/vision** fake-LOS entry point and the `RaycastSolver` / `HitResolution`
   seam, so Stride raycasts inject behind the same interfaces. (§6.3)
7. **Ghost/remote query filter** API (`.WithLifecycle(EntityLifecycle.Ghost)` /
   `.WithoutOwned<SimTransform>()` or the real equivalent). (§7)
8. **FDP phase enumeration** and how to slot the reverse-sync into `Input` / pre-`Simulation`. (§8.1)
9. **Stride external-loop specifics**: the Windows (SDL2) event-pump call, throttler/vsync
   flags in external-loop mode, `Game.Tick()` usage on the current Stride NuGet. (§8.1)
10. **In-process orchestration** signatures: `ClusterConfiguration`, `ClusterMaster`,
    `ClusterSlave`, `OrchestrationLogicPack`, `TransitionStateIntent`, `PublishManaged`,
    `OfflineNetworkFactory`; the existing `Hrot.Editor` offline path to mirror. (§8.2)
11. **`EditorSubsystem` composition** — how to instantiate it without a kernel and point it
    at the shared world; the authoring-intent drain API. (§8.3)
12. **Record/replay hooks**: `TogglablePostSimulationGroup`, `ReferenceReplayLoadHandler`,
    `PlaybackTickSystem`, `IRecordReplayController` transition names; whether the physics
    step must be explicitly paused in replay or freezing falls out of severing motors. (§9)
13. **Navigation singletons**: `INavmeshProvider` registration point;
    `ZoneDefinitionDto` / `RoadNetworkPath` / `ZoneEnvironmentData` / `RoadRadiusThresholdSq`;
    `NavigationFakesModule` → real-module swap point. (§10)
14. **`DebugPrimitive`** current struct definition (header + union, field names, offsets) —
    read from source, never hardcode; and the `Stride.DebugRendering` immediate-mode API. (§11)
15. **`StrideNodeBootstrapper`** constructor injection points for custom domain modules
    (`StrideKinematicsModule`, animation, providers), confirming the bootstrapper is used
    unchanged. (§1.1, §3)

---

## 16. Summary

- **Seam unchanged.** `StrideNodeBootstrapper` is direction-oblivious and reused as-is; the
  future proprietary engine swaps in at the same line.
- **Authority inverted to Stride/Bepu.** FDP movement integrators off; Bepu owns motion;
  a reverse-sync writes `SimTransform` each fixed 60 Hz substep before the FDP tick reads it.
- **Sync script rewritten split-authority.** Reverse for locally-owned bodies, forward
  (ghost-filtered) for remote/replayed.
- **Existing contracts honored.** Navigation (dual-mode, real DotRecast + kept road graph),
  perception (Stride raycasts, authoritative 3D Z, terrain-query pipeline removed),
  animation (`StrideAnimationBackend`, optional root-motion hooks), recording/replay
  (togglable reverse-sync group + `PlaybackTickSystem`).
- **One run mode built now** (all-in-one editor: Stride owns the loop, raylib/ImGui editor
  as a second window pumped on the same thread, in-process master/slave, offline factory),
  **one reserved** (networked, no CGF).
- **Bring-up is incremental** under a single authority model, using the existing fakes as
  stand-ins, with no throwaway authority direction.

*End of Stride Integration v0.1. All design-level decisions are settled; remaining items in
§15 are source-confirmation tasks, not open design questions.*
