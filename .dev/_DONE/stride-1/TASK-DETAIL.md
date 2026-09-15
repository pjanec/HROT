# Stride Integration — Task Details

**Design reference:** [Stride-Integration_v0_3.md](./Stride-Integration_v0_3.md). Section
citations below (e.g. *v0.3 §6.1*) point into that document; this file does **not** duplicate
design prose — read the cited sections for rationale and shape.

**Tracker:** [TASK-TRACKER.md](./TASK-TRACKER.md) · **Debt:** [DEBT-TRACKER.md](./DEBT-TRACKER.md)

**Conventions.**
- Each task has a unique id `STR-P<phase>-T<n>` and is independently buildable + testable.
- *Success conditions* are stated as unit/integration test specifications — implement them as
  real tests where a test project exists for the target assembly.
- "[VERIFY]" inside a task means: confirm the exact engine symbol against live source before
  coding (see v0.3 §15). Record any deviation in the debt tracker.
- Mode 1 (steps P0–P5) is built first; Mode 2 (P6) last. Building Mode 1 builds nearly all of Mode 2.
- Pre-task work already committed: `StrideRenderModelDefDto` + `CollisionShapeKind`
  ([StrideRenderModelDefDto.cs](../../FDP/Toolkits/Fdp.Toolkits/Tkb/Domain/StrideRenderModelDefDto.cs))
  and the demo `Stride.RenderModelDef` content on the UrbanCombat templates (v0.3 §6.5, §12).
  The Stride app solution exists at `Stride/HrotStrideApp.sln` (Bullet; Bepu removed).

---

## Phase 0 — Scaffolding, coordinate seam, first render

Goal: the `editor_stride` single shared world boots under `OfflineNetworkFactory`, Stride drives
its own loop, entities spawn (owned instantly) and **render** — movement still stubbed. Matches
v0.3 §14 step 0.

### STR-P0-T1 Create Hrot.Stride.Core project and references

**What.** Create `Stride/Hrot.Stride.Core/Hrot.Stride.Core.csproj` (`net8.0-windows` class
library) per v0.3 §3. References: `Stride.Engine`, `Stride.Physics`, `Stride.Rendering`,
`Stride.Games` (NuGet 4.2.1.2487), `DotRecast.*`, and `ProjectReference`s to the FDP/HROT
assemblies it needs (`Fdp.Core`, `Fdp.Toolkits`, `Hrot.Common`, …). **No** Raylib, **no**
`Hrot.StrideMock` reference.

**Success conditions.**
- Project builds clean (0 errors).
- A guard test/inspection confirms the assembly references neither `Raylib-cs` nor
  `Hrot.StrideMock`.
- A trivial type in the assembly can reference both a `Stride.Engine` type and an FDP
  `net8.0` type (e.g. `EntityRepository`) — i.e. the cross-TFM reference compiles (already
  proven for `net8.0-windows → net8.0`).

### STR-P0-T2 Create Hrot.Stride.Animation project and references

**What.** Create `Stride/Hrot.Stride.Animation/Hrot.Stride.Animation.csproj` (`net8.0-windows`)
per v0.3 §3. References: `Stride.Engine`, `Stride.Animations`, `ProjectReference` to
`Hrot.MuscleCharacter.Animation` (for `IAnimationBackend`/`IBlendTreeBuilder` and the
descriptor/runtime types). Distinct from the fake skeleton `Hrot.MuscleCharacter.Animation.Stride`.

**Success conditions.**
- Project builds clean.
- A stub `StrideAnimationBackend : IAnimationBackend` compiles against the real interface
  (implementation deferred to P4).

### STR-P0-T3 Wire HrotStrideApp.Game references

**What.** Add `ProjectReference`s from `HrotStrideApp.Game` to `Hrot.Stride.Core`,
`Hrot.Stride.Animation`, `Hrot.StrideMock` (for `StrideNodeBootstrapper`, v0.3 §1.1), and the
FDP/HROT assemblies. Confirm the Raylib tag-along from `Hrot.StrideMock` is acceptable (both
modes host diagnostic raylib windows, v0.3 §8.3).

**Success conditions.**
- `Stride/HrotStrideApp.sln` builds clean (Game + Windows head).
- Stride asset compilation still succeeds (no asset pipeline regression).
- `StrideNodeBootstrapper` is resolvable from `HrotStrideApp.Game`.

### STR-P0-T4 FdpStrideTransform coordinate seam

**What.** Implement the pure static `FdpStrideTransform` (v0.3 §4) in `Hrot.Stride.Core`:
position/rotation/velocity/angular-velocity conversions both directions, plus
`ScreenRayToFdp`. [VERIFY] `SimTransform` math type names + Euler order + Bullet quaternion winding.

**Success conditions (unit).**
- Position round-trips: `ToFdp(ToStride(p)) ≈ p` for a battery of vectors.
- Axis mapping: FDP `(E, N, Up)` → Stride `(X=E, Y=Up, Z=N)` exactly (v0.3 §4 table).
- Rotation: a known FDP yaw "due East" maps to the correct Stride facing (handedness flip
  asserted, not just axis relabel); rotation round-trips within tolerance.
- Velocity uses the same swizzle as position (no translation term); angular velocity converts.

### STR-P0-T5 StrideHrotGame external host loop

**What.** `StrideHrotGame : Game` in `HrotStrideApp` driving an **external** loop via
`Game.Tick()` with the internal throttler disabled, pumping the Stride window's OS events each
iteration (v0.3 §8.3). [VERIFY] external-loop / throttler / SDL2 event-pump calls on 4.2.1.2487.

**Success conditions.**
- Headless/smoke: the game advances a fixed number of `Tick(dt)` iterations driven by the host
  loop (not the internal loop) without throwing, and shuts down cleanly.
- The fixed physics timestep is the sim clock regardless of how many render frames elapse.

### STR-P0-T6 EditorStrideSubsystem composition skeleton

**What.** `EditorStrideSubsystem` mirroring `EditorSubsystem` (v0.3 §8.1–§8.2): one shared
`EntityRepository`/sim-`FdpEventBus`/`ModuleHostKernel`; `OfflineNetworkFactory`;
`NetworkSpawningSystem(localNodeId = 0)`; `CgfLogicPack` + `StrideKinematicsModule` (placeholder
from P1) in the logic-pack list; **separate** `_orchestrationBus`; in-process
`ClusterSlave(0,"Editor",_orchestrationBus)` wrapped in `OrchestrationLogicPack`; and
`ClusterMaster(_orchestrationBus, new ClusterConfiguration { Mandatory = Array.Empty<string>() })`.

**Success conditions (integration).**
- Subsystem initializes headless without throwing; world/kernel/time-controller created.
- The orchestration bus is a *different* `FdpEventBus` instance from `world.Bus` (asserted).
- The `ClusterMaster` releases its bootstrap latch immediately (empty `Mandatory`) and the
  initial cluster state observed is `Standby`.
- Spawning via the Brain path stamps `OwnerNodeId = 0`; spawned entities are `WithOwned` from
  birth (no deferred grant), asserted via `.WithOwned<SimTransform>()`.

### STR-P0-T7 StrideVisualBindingSystem and procedural fallback

**What.** `StrideVisualBindingSystem` + `StrideVisualReference` in `Hrot.Stride.Core` (v0.3 §6.5):
resolve the entity class's `StrideRenderModelDefDto`; on entity-appear instantiate the Stride
visual entity — load `Content.Load<Model>(ModelAssetRef)` (+ skeleton/`AnimationComponent` for
skinned) or a **procedural primitive** matching `ShapeKind` when `ModelAssetRef` is empty; apply
`Scale`/`Offset`; tear down on entity death. [VERIFY] runtime asset load + instantiate API.

**Success conditions (integration).**
- Entity whose class has `ModelAssetRef = "Models/mannequinModel"` → a Stride entity with a
  `ModelComponent` (and skeleton present) appears.
- Entity with empty `ModelAssetRef` → a procedural capsule (ShapeKind=Capsule) or box
  (OrientedBox) sized from `ShapeRadius`/`ShapeHeight`/box half-extents (defaulting from
  `PhysicsCollider`/`VehicleParametersDto`).
- Entity death removes the Stride visual entity and `StrideVisualReference`.

### STR-P0-T8 End to end spawn and render smoke

**What.** Tie P0 together: boot `editor_stride`, spawn the UrbanCombat demo entities through the
Brain spawn path, render them in Stride 3D at swizzled positions; movement driven by a trivial
kinematic position stub (no Bullet yet). Matches v0.3 §14 step 0.

**Success conditions (integration).**
- N spawned entities each produce a Stride visual at `FdpStrideTransform.ToStride(SimTransform)`.
- Visual set reconciles: spawning adds, destroying removes (Pass-A reconciliation, v0.3 §7).
- Single-thread invariant holds (no second thread touches `EntityRepository`).

---

## Phase 1 — Bullet movement + reverse-sync

Goal: Bullet is authoritative; the reverse-sync writes `SimTransform`/`SimVelocity`; FDP
integrators are off. Matches v0.3 §14 step 1.

### STR-P1-T1 StrideKinematicsModule

**What.** `StrideKinematicsModule : IEcsModule` (v0.3 §5.1–§5.2) registering the kept systems
(`SpatialHashSystem`, `FormationTargetSystem`, `VehicleCommandSystem`, `NavigationExecutionSystem`,
refactored `CrowdAgentUpdateSystem`) and **omitting** `CarKinematicsSystem` / `LinearKinematicsSystem`.
[VERIFY] `GroundKinematicsModule`/`SimHostCoreLogicPack` membership + `IEcsModule` API.

**Success conditions (unit).**
- The module's registered systems include the five kept systems and exclude the two integrators.
- Entities still carry `SimTransform` + `SimVelocity` (exclusion is topological, not by component
  removal).
- The terrain-query pipeline (`TerrainQuerySubmitSystem` / `TerrainQuerySolverSystem` /
  `TerrainQueryResolutionSystem`) is **not** registered (v0.3 §5.5); EQS altitude consumers read
  `SimTransform.Z` directly.
- `DeadReckoningSyncSystem` is registered with **`DriveFromNetwork = false`** (v0.3 §5.4) so
  smoothing applies only to non-owned/ghost entities (idle in Mode 1, active in Mode 2); a unit
  test asserts an owned entity is not dead-reckoning-smoothed.

### STR-P1-T2 PhysicsBodyLifecycleSystem and PhysicsBodyReference

**What.** `PhysicsBodyReference` shadow component + `PhysicsBodyLifecycleSystem` (v0.3 §5.6),
keyed on the authority bit (`.WithOwned`/`.WithoutOwned`), building the Bullet body using the
shape from `StrideVisualBindingSystem`/`StrideRenderModelDefDto`. [VERIFY] `DestructionOrder`
event shape.

**Success conditions (unit/integration).**
- `.WithOwned<SimTransform>().Without<PhysicsBodyReference>()` → body created + `PhysicsBodyReference` added.
- Authority revoked (`.WithoutOwned` with ref) → body removed + ref removed.
- `DestructionOrder` consumed → body torn down + ref removed.
- Capsule built for ShapeKind=Capsule; oriented box for OrientedBox.

### STR-P1-T3 BulletCharacterMotor

**What.** `BulletCharacterMotor` (v0.3 §6.2): read `CrowdMotorIntent`, apply stance speed
multiplier, drive Bullet `CharacterComponent` (`SetVelocity`, `Jump`). Reference: template
`PlayerController`.

**Success conditions (unit).**
- A `CrowdMotorIntent` of magnitude v yields character velocity v (direction preserved).
- Stance Standing/Crouched/Prone scales the applied speed by the configured multipliers.
- Jump traversal calls the jump path; `IsGrounded` consulted.

### STR-P1-T4 KinematicVehicleMotor

**What.** `KinematicVehicleMotor` (v0.3 §6.2 + §6.1 "kinematic bodies own their velocity"):
integrate `VehicleCommandSystem` output as a kinematic move **with owned collision response**
(swept/penetration-tested block-or-slide against the static world), then compute the
post-collision linear + angular velocity and hand it to the reverse-sync.

**Success conditions (unit).**
- Unobstructed command integrates the kinematic transform along the commanded heading.
- A move into a static obstacle is **blocked or slid** (no tunneling, no solver pop-out), and a
  fully blocked move yields **zero** output velocity.
- The motor's computed post-collision linear + angular velocity is exposed for the reverse-sync
  (consumed in STR-P1-T5).

### STR-P1-T5 BulletReverseSyncSystem

**What.** `BulletReverseSyncSystem` in a `TogglablePostSimulationGroup` (v0.3 §6.1, §7, §9):
once per frame after the physics processor, for `.WithOwned<SimTransform>()`, write
`SimTransform.Position/.Rotation` + `SimVelocity` (lin+ang) via `FdpStrideTransform`, honoring
the velocity invariant. Dynamic bodies → read `RigidbodyComponent` velocity; kinematic bodies →
use the motor's computed velocity / per-frame delta.

**Success conditions (unit/integration).**
- Owned body pose → `SimTransform` (swizzled correctly).
- Dynamic body velocity → `SimVelocity` lin+ang.
- Collision arrest → `SimVelocity` written **zero** that frame (no stale velocity).
- Kinematic body → `SimVelocity` from the motor's post-collision velocity.
- Group `Enabled=false` → no writes occur (replay severability, used by P5).

### STR-P1-T6 SplitAuthorityStrideSyncScript

**What.** Replace the mock's hardcoded forward-sync with the authority-forked
`SplitAuthorityStrideSyncScript` (v0.3 §7): Pass A reconciles the Stride visual set (via
`StrideVisualBindingSystem`); Pass B forward-syncs `.WithoutOwned<SimTransform>()` only.

**Success conditions (unit).**
- Owned entities are **not** forward-synced (their Stride body is physics-driven).
- Non-owned entities' Stride visual transform follows `SimTransform` via `FdpStrideTransform`.
- Appear/disappear reconciliation spawns/tears down visuals.

### STR-P1-T7 Fixed timestep and reverse-sync ordering

**What.** Configure the Bullet `Simulation` fixed timestep + sub-stepping, and order the
reverse-sync so FDP `Simulation`-phase consumers read post-physics positions the same frame
(v0.3 §6.1, §8.3). [VERIFY] post-`PhysicsProcessor` ordering hook + FDP phase enumeration.

**Success conditions (integration).**
- `SpatialHashSystem` / vision broadphase / EQS read the post-physics `SimTransform` within the
  same frame (no one-frame lag).
- Simulation advances on the fixed clock independent of render rate.

---

## Phase 2 — Navigation (DotRecast navmesh + dtCrowd + road graph)

Goal: real navmesh/crowd providers behind the existing contracts; `Auto` selection. v0.3 §14 step 2.

### STR-P2-T1 StrideNavmeshBaker

**What.** `StrideNavmeshBaker` (v0.3 §10.1): extract collision/terrain triangles from the loaded
`MainScene`, swizzle via `FdpStrideTransform`, bake per-`NavLayerMask` DotRecast navmeshes at
scenario load.

**Success conditions (integration).**
- Bake produces a non-empty navmesh from `MainScene` geometry.
- Per-layer params applied (Infantry 0.3 m/60°; Vehicle 1.5 m/20°/0.1 m step).
- Baked geometry is in FDP coordinates (swizzle verified against a known scene feature).

### STR-P2-T2 DotRecastNavmeshProvider

**What.** `DotRecastNavmeshProvider : INavmeshProvider` registered as the managed ECS singleton
(v0.3 §10.1). Drop-in for `FakeNavmeshProvider`. [VERIFY] `INavmeshProvider` singleton registration.

**Success conditions (unit).**
- Implements the full `INavmeshProvider` contract; `PlanPath`/reachability answer over the baked
  mesh.
- Registered as the `INavmeshProvider` singleton (replaces the fake).

### STR-P2-T3 DotRecastDtCrowdProvider

**What.** `DotRecastDtCrowdProvider : IDtCrowdProvider` (v0.3 §10.1) providing local
avoidance/steering; `GetAgentVelocity` feeds `CrowdMotorIntent`.

**Success conditions (unit).**
- Agent add/remove; `GetAgentVelocity(entity)` returns a steering velocity toward the agent target.
- Contract parity with the fake dtCrowd provider.

### STR-P2-T4 CrowdAgentUpdateSystem refactor

**What.** Refactor `CrowdAgentUpdateSystem` (v0.3 §5.3): poll dtCrowd, write **only**
`CrowdMotorIntent`, stop mutating `SimTransform`. [VERIFY] current system code.

**Success conditions (unit).**
- Writes `CrowdMotorIntent` from `_dtCrowd.GetAgentVelocity`.
- Does **not** write `SimTransform` or `SimVelocity`.
- `BulletCharacterMotor` consumes the intent (integration with STR-P1-T3).

### STR-P2-T5 Road-graph mode and Auto selection

**What.** Materialize `ZoneEnvironmentData` from the scenario `Zones`/`RoadNetworkPath`
(v0.3 §10.2) and verify `PathfindingSolverSystem` `Auto` selection (v0.3 §10.3). [VERIFY]
`ZoneDefinitionDto`/`RoadNetworkPath`/`ZoneEnvironmentData`/`RoadRadiusThresholdSq`.

**Success conditions (integration).**
- With both singletons present, endpoints near road nodes → `RoadGraph`; off-road → `Navmesh`;
  mixed → `Hybrid` (per thresholds).

---

## Phase 3 — Perception via Stride raycasts

Goal: real LOS/occlusion + ballistics against scene geometry. v0.3 §14 step 3.

### STR-P3-T1 StrideRaycastService

**What.** `StrideRaycastService` wrapping `Simulation.Raycast` (+ penetrating/overlap/sweep),
all I/O crossing `FdpStrideTransform` (v0.3 §6.3). [VERIFY] Bullet `Simulation.Raycast` signature.

**Success conditions (unit).**
- A ray to a known static collider returns the expected hit point/normal (in FDP coords).
- Collision-mask filtering respected.
- A ray through empty space misses.

### STR-P3-T2 Perception LOS via Stride raycasts

**What.** Inject `StrideRaycastService` LOS/occlusion behind the existing vision/LOS seam, feeding
`TargetMemory` (3D) (v0.3 §6.3). [VERIFY] the fake-LOS entry point.

**Success conditions (integration).**
- LOS is blocked by scene geometry (wall between observer and target ⇒ not visible).
- Clear LOS ⇒ visible; `TargetMemory` updated with 3D-correct data.
- Replaces the flat spatial-hash approximation behind the same interface.

### STR-P3-T3 Ballistics raycast seam

**What.** Back `BallisticsSystem` hit resolution with `StrideRaycastService` via the existing
`RaycastSolver`/`HitResolution` seam (v0.3 §6.3). [VERIFY] that seam.

**Success conditions (unit/integration).**
- Analytic projectile integration retained; hit tests use `StrideRaycastService`.
- A shot blocked by geometry resolves an impact at the obstacle, not the target.

---

## Phase 4 — Animation

Goal: real Stride animation backend; locomotion blend + traversal montages. v0.3 §14 step 4.

### STR-P4-T1 StrideAnimationBackend and PerEntityBlendTreeBuilder

**What.** Implement `StrideAnimationBackend : IAnimationBackend` + `PerEntityBlendTreeBuilder :
IBlendTreeBuilder` in `Hrot.Stride.Animation` (v0.3 §6.4), modeled on the template
`AnimationController` (idle/walk/run blend).

**Success conditions (unit).**
- Backend registers/unregisters per entity; per-entity blend tree builds.
- Idle/Walk/Run blend weights derive from locomotion inputs (speed thresholds).

### STR-P4-T2 CharacterAnimationDefDto demo content

**What.** Author `CharacterAnimationDefDto` content for the mannequin class: locomotion clip refs
(`Animations/Idle|Walk|Run`) and Jump montages (`Animations/Jump_Start|Jump_Loop|Jump_End`),
wired to Stride asset URLs (v0.3 §6.4, §12). Attach to `InfantrySoldier`/`Insurgent` templates.

**Success conditions (unit).**
- Descriptor bakes into `CharacterAnimationDefRuntime` via `AnimationTkbTranslator`.
- Montage `AssetRef`s resolve to the `Animations/*` URLs; slots/stances validate.

### STR-P4-T3 Locomotion bridge

**What.** Drive locomotion via `AnimationRuntimeBridgeSystem` reading physics-sourced
`SimTransform` + `SimVelocity` → `UpdateLocomotionInputs` (v0.3 §6.4).

**Success conditions (integration).**
- A moving entity blends walk→run by speed; at rest it idles.
- Inputs are sourced from the reverse-synced `SimVelocity` (physics-driven).

### STR-P4-T4 Montage dispatch

**What.** Off-mesh-link traversal: `OffMeshLinkDetectionSystem` → `AnimationChannel.PlayMontage`
→ Jump montage plays through the backend (v0.3 §6.4).

**Success conditions (integration).**
- Crossing an off-mesh link triggers the jump montage (start/loop/end) on the correct slot.

---

## Phase 5 — Gizmos, editor dual-window, record/replay

Goal: 3D gizmos, the raylib/ImGui editor as a second window on the host thread, shared selection,
and replay via the togglable group. v0.3 §14 step 5.

### STR-P5-T1 DebugPrimitiveRenderer3D

**What.** `DebugPrimitiveRenderer3D` (v0.3 §11): two-pass (anchors, then shapes) sweep of
`ProducerBuffer` → `Stride.DebugRendering`, swizzling via `FdpStrideTransform`. [VERIFY] current
`DebugPrimitive` struct + `Stride.DebugRendering` API (do not hardcode offsets).

**Success conditions (unit/integration).**
- Anchor + shape primitives render at the correct swizzled world transform.
- Binds to the live `DebugPrimitive` struct (no hardcoded layout).

### STR-P5-T2 Raylib ImGui editor second window

**What.** Host the raylib/ImGui editor as a second OS window pumped on the **same** thread
(v0.3 §8.2–§8.3), reusing `EditorApplication`/`IEditorLogic`, the AI-editor registrars, and the
ImGui panels via `WindowManager`.

**Success conditions (integration).**
- Editor panels render; the editor pump and Stride tick run on one OS thread.
- No second thread touches `EntityRepository` (single-thread invariant asserted).

### STR-P5-T3 Shared selection and CenterOnEntityCommand

**What.** Shared `SelectionState` (ECS) across the 2D and 3D views; `CenterOnEntityCommand`
consumed by both cameras (v0.3 §8.4).

**Success conditions (integration).**
- Selecting in 2D marks `SelectionState`; the 3D view + gizmos reflect it.
- Publishing `CenterOnEntityCommand` focuses both views on the entity's `SimTransform`.

### STR-P5-T4 Record and replay togglable reverse-sync

**What.** Wrap `BulletReverseSyncSystem` in `TogglablePostSimulationGroup` and pass to
`ReferenceReplayLoadHandler`; `PlaybackTickSystem` drives `SimTransform` during replay; ensure
the Bullet step does not advance owned bodies during replay (v0.3 §9). [VERIFY] whether severing
the group suffices or the simulation must be paused.

**Success conditions (integration).**
- `PrepareReplay` → reverse-sync group `Enabled=false`; `PlaybackTickSystem` drives `SimTransform`
  from recorded keyframes; rendered entities follow replay.
- `FinalizeReplay`/`PrepareLive` → group `Enabled=true`; Bullet authority restored.
- No physics writes occur to owned `SimTransform` while replaying.

---

## Phase 6 — Mode 2 (networked Stride node)

Goal: slave-only Stride node over real DDS to a remote Brain+Master; deferred handover; egress.
v0.3 §13, §14 step 6.

### STR-P6-T1 StrideMuscleNodeBootstrapper

**What.** `StrideMuscleNodeBootstrapper` mirroring `SimHostNodeBootstrapper` (v0.3 §13.2):
`DdsParticipant`, `NedNetworkFactory`, `NodeBootstrapper.BuildOrchestration`, `ConfigureForNode`
→ `ISlaveOrchestrationTranslator`, slave translator `Tick()` ordered before the `ClusterSlave`
tick. [VERIFY] all §13 symbols.

**Success conditions (integration).**
- Bootstraps a `ClusterSlave` whose I/O is over the local bus; the translator bridges to DDS.
- Translator `Tick()` publishes ingress before the slave consumes it; drains heartbeats/status to DDS.

### STR-P6-T2 StrideMuscleNodeApp

**What.** `StrideMuscleNodeApp` mirroring `SimHostApp` (v0.3 §13.1–§13.3): slave-only, **no**
editor authoring surface, diagnostic raylib/ImGui windows retained; identity from
`HrotNodeConfig.NodeId`.

**Success conditions (integration).**
- Node boots over loopback DDS, emits a `Standby`/Idle heartbeat; a remote master with this node
  in `Mandatory` releases its bootstrap latch.
- No editor surface is constructed; diagnostic windows are.

### STR-P6-T3 Deferred authority handover

**What.** Verify the deferred path (v0.3 §13.3): Brain creates an entity (ghost here);
`DeferredTakeoverSystem` flips `SimTransform`/`SimVelocity` authority over DDS; the bit-flip
drives `PhysicsBodyLifecycleSystem` and the sync fork.

**Success conditions (integration).**
- Pre-handover: entity is `.WithoutOwned` here (ghost, forward-synced).
- On authority grant: `PhysicsBodyLifecycleSystem` materializes the Bullet body, and the entity
  switches to reverse-sync (`.WithOwned`) with no extra code path.

### STR-P6-T4 Egress and dead-reckoning velocity invariant

**What.** Verify `GeoSpatialEgressTranslator` event-driven egress + remote dead-reckoning
(v0.3 §13.4) with the velocity invariant. [VERIFY] egress/`NetworkTransform`/`NetworkVelocity`/
`SmoothingRate` + thresholds.

**Success conditions (integration).**
- A displacement > threshold (or rotation > threshold) emits a `WorldPos` packet immediately.
- A remote ghost extrapolates via received `NetworkVelocity`.
- A collision-arrested body publishes **zero** velocity, so the remote ghost does **not**
  extrapolate through the obstacle (the invariant's network-visible effect).

### STR-P6-T5 Two-process end to end bring-up

**What.** End-to-end: a `ClusterRunner` started with `cgf,orchestrator` (Brain + Master) paired
with the Stride Muscle node process over loopback DDS (v0.3 §13.1).

**Success conditions (end-to-end).**
- An entity spawned by the Brain process appears and **moves under Bullet** on the Stride node.
- Record/replay works on the Stride node (P5 reused).
- Remote debug gizmos arrive via `ConsumerBuffer` and render in 3D (v0.3 §13.5).

---

## Appendix — task dependency summary

- P0 is foundational; **P1 depends on P0** (esp. T1/T4/T6/T7).
- P1 reverse-sync (T5) underpins P2–P6 (everything reads physics-sourced `SimTransform`).
- P2 `CrowdMotorIntent` (T4) closes the loop with P1 `BulletCharacterMotor` (T3).
- P4 locomotion (T3) depends on P1 reverse-synced `SimVelocity`.
- P5-T4 replay depends on P1-T5 (togglable reverse-sync group).
- **P6 reuses the entire Mode-1 node**; only composition + DDS wiring differ. The deferred
  handshake (T3) is the only Mode-2-exclusive behavior.
