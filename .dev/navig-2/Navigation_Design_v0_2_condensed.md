# Navigation Subsystem — Design v0.2 (condensed, architect review)

> **Status.** Second condensed pass after architect resolution of O1–O10 from v0.1.
> All open items closed; this pass folds the rulings in and surfaces any second-order issues.
> Dense bullets, code-shape over prose, no re-justification of established rulings.
> Resolved questions cited as `[ArchQ.N]` (from initial batch) or `[O.N]` (from O1–O10 batch).
>
> **v0.1 → v0.2 changes summary** (§18 carries the full resolution table):
> - O1: `NavigationIntent` exempt from 96B channel budget — N=8 inline corridor stays inline; no side-buffer split.
> - O2: No `RequestDeadlineTick`; compute-and-discard pattern preserved.
> - O3: `KinematicsMode` enum extended with `Naval`, `Flying`; no new state component.
> - O4: `INavmeshProvider` amended in place; no façade.
> - O5: Separate navmesh per layer; `dtArea`-filter alternative dropped.
> - O6: Flying folds into `PathRequestBatch` with `MobilityProfile = Flying`; no new DDS topic.
> - O7: `SurfaceType` confirmed on `NavWaypoint`.
> - O8: `PathfindingBatchData.DefaultCapacity = 256`.
> - O9: `IDtCrowdProvider`'s host module implements `IDisposable`.
> - O10: `CrowdAgentUpdateSystem` early-outs on `NavigationStatus.Phase == AwaitingTraversal`.

---

## 1. Scope & deferral

- Stride3D buildable now; APIs accommodate final voxelized-patchable Recast stage.
- **Agent kinds in scope (v1 interfaces):** Humanoid infantry, Wheeled, Tracked, Naval (surface), Flying.
- **Implementation in v1:** Humanoid + Wheeled + Tracked. Naval interface only. Flying interface only.
- **Deferred:** Formations, squad cohesion, flow fields, threat-aware path cost, navmesh-patch propagation impl, root-motion authority flip, submarine depth, FollowRoute polish, JoinFormation polish.
- **Out of scope (separate designs):** Animation runtime (DD-1..5 own that); EQS revisions to add `NavLayerMask` (small follow-up doc, mandatory but mechanical).

## 2. Topology — three deployment modes

Same API contracts in all three. Only `NedReplicationModule` role flags differ. DDS loopback transparent when sender = receiver.

| Mode | Brain | Muscle | NavigationSolver | Use |
|---|---|---|---|---|
| **Distributed** | own node | own node | own node | Production |
| **Simplified** | own node | hosts `NavigationSolverModule` | folded | Smaller deployments |
| **All-in-one** | one process | one process | one process | Editor, headless tests |

Design assumes **distributed** as the contract baseline; simpler modes are collapses.

## 3. The end-to-end pipeline

```
Brain                              NavigationSolver                Muscle
─────                              ─────────────────               ──────
BTree → Action_PlanRoute
  publishes PathfindingRequestEvent ─DDS PathRequestBatch─►
                                   PathfindingSolverSystem
                                     (SlowBackground 10Hz, snapshotted)
                                     multi-modal route by MobilityProfile
                                     [navmesh | road-graph | spliced]
                                     full path → local TrajectoryPoolManager
                                     ◄─DDS PathResponseBatch (targeted to source Brain)─
  PathResponseBrainIngressTranslator
    registers waypoints in Brain's local TrajectoryPoolManager
    materializes NavigationCorridorMacro { BrainRouteHandle, NavmeshVersionAtPlan,
                                            TotalDistance, WaypointTraversalKinds[] }

BTree → Action_MoveTo (with corridor available)
  MoveToExecutor (Brain) tick:
    slides N=8 inline window from current ProgressS
    writes NavigationIntent { Vector2 Destination, MoveToParams (32B),
                              NearFutureCorridor [InlineArray<Waypoint, 8>],
                              CorridorPlanVersion, ActionInstanceId }
  NavigationIntentEgressTranslator (Brain) ─DDS NavigationIntent─►
                                                                  NavigationIntentIngressTranslator
                                                                  
                                                                  LocomotionDispatcherSystem
                                                                    → NavigationIntentBridgeSystem
                                                                  
                                                                  NavigationIntentBridgeSystem:
                                                                    if Humanoid + MoveTo:
                                                                      add CrowdAgent tag, register dtCrowd
                                                                    elif Vehicle:
                                                                      set NavState (existing path)
                                                                    elif Naval:
                                                                      set NavState (NavLayer.Naval)
                                                                    elif Flying:
                                                                      NavState.Mode := Flying
                                                                      (volumetric kinematics — §9)
                                                                  
                                                                  CrowdAgentUpdateSystem
                                                                    (Simulation, early)
                                                                    dtCrowd.Update(dt) → SimVelocity
                                                                  
                                                                  CarKinematicsSystem / LinearKinematicsSystem
                                                                    (PostSimulation, .Without<CrowdAgent>)
                                                                    integrate SimVelocity → SimTransform
                                                                  
                                                                  AnimationRuntimeBridgeSystem (DD-1 §2)
                                                                    UpdateLocomotionInputs(handle, horizV, vV, grounded)
                                                                  
                                                                  NavigationExecutionSystem
                                                                    (Simulation, universal — pre-existing)
                                                                    SimVelocity frustration watchdog
                                                                    ProgressS, segment advance
                                                                    on K+1.TraversalKind != Walk:
                                                                      remove CrowdAgent tag
                                                                      write AnimationChannel.PlayMontage
                                                                      emit OffMeshTraversalStartedEvent
                                                                      await MontageEndedEvent
                                                                      restore CrowdAgent, retarget
                                                                      emit OffMeshTraversalEndedEvent
                                                                    writes NavigationStatus
  ◄─DDS NavigationStatus─────────────────────────────────────────  NavigationStatusEgressTranslator

MoveToExecutor (Brain) observes:
  ProgressS → slide window, refresh NearFutureCorridor (no ActionInstanceId bump)
  Result == Arrived          → emit MoveCompletedEvent(Arrived);   BTree Success
  Result == FailedBlocked    → if ReplanCount < MaxReplans and elapsed < ReplanTimeBudget:
                                  re-publish PathfindingRequestEvent, ReplanCount++,
                                  emit PathReplannedEvent
                               else: emit MoveCompletedEvent(FailedBlocked); BTree Failure
  Result == FailedUnreachable→ emit MoveCompletedEvent(Unreachable); BTree Failure
  NavmeshVersionObserved != CorridorPlanVersion → (final-stage) trigger replan
```

## 4. CQRS contract — components

### 4.1 Brain-owned

```csharp
// existing, extended
struct NavigationIntent {
    // header (existing channel base fields)
    uint   ActionInstanceId;
    uint   BehaviorInstanceId;
    uint   DispatchedInstanceId;
    ushort ActiveAction;          // ActionIdMoveTo | FollowRoute | Flee | JoinFormation
    byte   Status;                // channel base
    // [32B] MoveToParams (§13.2)
    // [N=8 InlineArray<NavWaypoint>] NearFutureCorridor (§4.5)
    uint   CorridorPlanVersion;
    ushort CorridorWindowStart;   // segment index of NearFutureCorridor[0]
    byte   CorridorWindowCount;   // 0..8; <8 only on final window
    // total: 184 B — see §4.4 definitive layout. NavigationIntent is exempt
    // from the 96B MaxChannelSizeBytes channel budget [O1]: it's a CQRS
    // command component, not a channel component.
}

// new, Brain-local only (not replicated)
struct NavigationCorridorMacro {
    int    BrainRouteHandle;       // into Brain's local TrajectoryPoolManager
    uint   NavmeshVersionAtPlan;
    float  TotalDistanceMeters;
    byte   MobilityProfile;        // 0=Wheeled, 1=Tracked, 2=Infantry, 3=Naval, 4=Flying
    byte   WaypointCount;
    // TraversalKinds[] live in trajectory pool entry alongside waypoints
}
```

### 4.2 Muscle-owned

```csharp
// existing, extended
struct NavigationStatus {
    byte   Result;                  // InProgress | Arrived | FailedBlocked | FailedUnreachable | FailedNoLayer | FailedTimeout
    byte   Phase;                   // Idle | Planning | Following | AwaitingTraversal | Stuck
    byte   SegmentIndex;            // index into the corridor window
    byte   FrustrationTicks;        // existing
    float  ProgressS;               // existing — arc-length progress
    float  EstimatedTimeRemaining;  // seconds
    uint   NavmeshVersionObserved;  // stub-constant in v1
    // [8B for traversal metadata: CurrentTraversalKind byte, AwaitingMontageId int, padding]
}
```

### 4.3 Solver-owned (transient)

```csharp
struct PathfindingRequest {
    long    RequestId;            // (entityIndex << 32) | world.GlobalVersion — existing
    Vector2 Start;                // RelativeVector3.XY on the wire
    Vector2 End;
    ushort  NavLayerMask;         // NEW — Infantry/Vehicle/Naval flags
    byte    MobilityProfile;      // existing — extended: Wheeled=0, Tracked=1, Infantry=2, Naval=3, Flying=4 [O6]
    byte    BackendForce;         // NEW — 0=Auto, 1=Navmesh, 2=RoadGraph
    float   MaxCost;              // NEW — cost budget; 0 = unbounded
    uint    NavmeshVersionAtRequest;  // NEW — for final-stage stamp comparison; stub-constant v1
    // No RequestDeadlineTick [O2]: compute-and-discard pattern preserved.
    // If the Brain abandons the request, the result lands in the ring buffer
    // and is silently ignored — RequestId no longer matches any live intent.
}

struct PathResult {
    long     RequestId;
    bool     IsReachable;
    int      RouteHandle;          // -1 on failure; into solver's local TrajectoryPoolManager
    float    TotalDistanceMeters;
    uint     NavmeshVersionAtPlan; // NEW
    byte     FailureReason;        // NEW — None | Unreachable | NoLayerPath | Timeout | NavmeshUnavailable
    // wire shape: DdsPathResult carries [DdsManaged] List<RelativeVector3> CoarseWaypoints
    //             + [DdsManaged] List<byte> WaypointTraversalKinds (parallel array)
}
```

### 4.4 `NavigationIntent` byte-layout (definitive) [O1]

`NavigationIntent` is a Brain-owned CQRS command component, **not a channel component**. It is exempt from the 96B `MaxChannelSizeBytes` budget (which constrains `LocomotionChannel` only). The N=8 inline corridor sits directly in `NavigationIntent`.

```
Channel-base header (existing):
  ActiveAction          2
  Status                1
  (pad)                 1
  BehaviorInstanceId    4
  ActionInstanceId      4
  DispatchedInstanceId  4
                       = 16 B
MoveToParams (§13.2):                                          32 B
NearFutureCorridor: InlineArray<NavWaypoint, 8> @ 16 B/wpt:   128 B
CorridorPlanVersion (uint):                                     4 B
CorridorWindowStart (ushort, segment index of NearFuture[0]):   2 B
CorridorWindowCount (byte, 0..8 — last window may be partial):  1 B
(pad)                                                            1 B
                                                            ──────
                                                              184 B
```

Notes:
- `RouteHandleBrain` is **not** carried on the wire — Brain-local handle, never resolvable on Muscle [Q12 ruling]. Muscle reads the corridor directly from `NearFutureCorridor`; it has no need for a handle.
- `CorridorWindowStart` lets Muscle's `NavigationExecutionSystem` compute global `SegmentIndex` from local window index: `globalIdx = WindowStart + localIdx`. Required for `NavigationStatus.SegmentIndex` to be globally meaningful.
- `CorridorWindowCount < 8` only on the final window (path tail shorter than window).
- Window-slide writes update `NearFutureCorridor`, `CorridorPlanVersion`, `CorridorWindowStart`, `CorridorWindowCount`. No `ActionInstanceId` bump (sub-instance update, dispatcher ignores) [§6.2].

### 4.5 `NavWaypoint` shape

```csharp
struct NavWaypoint {                      // 16 B
    Vector2 Position;                     // 8 — XY ground; elevation projected by Muscle
    byte    TraversalKind;                // 1 — see §4.6
    byte    SurfaceType;                  // 1 — see §4.6
    ushort  LayerMask;                    // 2 — which navmesh layer the segment is on
    float   SegmentLengthMeters;          // 4 — for ETA calculation
}
```

### 4.6 `TraversalKind` and `SurfaceType` enums [ArchQ6.2]

Both live in core navigation contracts (likely `NavigationComponents.cs` alongside `KinematicsMode` and `NavigationResult`), per architect ruling Q6.2.

```csharp
enum TraversalKind : byte {
    Walk         = 0,   // default — pull through normal corridor following
    Jump         = 1,   // small-gap horizontal jump
    JumpDown     = 2,   // drop down off a ledge
    JumpAcross   = 3,   // long horizontal jump across a gap
    Climb        = 4,   // ladder / wall climb
    Door         = 5,   // interact with door (animation-mediated)
    // Future: Vault, Slide, Mantle, Swim
}

enum SurfaceType : byte {
    Default      = 0,   // generic / unknown
    Grass        = 1,
    Concrete     = 2,
    Mud          = 3,
    Water        = 4,
    Metal        = 5,
    Wood         = 6,
    Snow         = 7,
    // Extensible; consumed by AnimationRuntimeBridgeSystem for footstep/gait selection.
}
```

`TraversalKind` derived by the solver from navmesh off-mesh-link `userId`. The mapping `dtOffMeshConnection.userId → TraversalKind` is defined in the `INavmeshProvider` implementation. **Animation runtime resolves `TraversalKind → MontageId`** via `CharacterAnimationDefDto` (per [ArchQ6.1]) — navigation never knows about specific montage assets.

## 5. Path query: request → corridor

### 5.1 Request flow

- Brain `Action_PlanRoute` (or implicit pre-step of `Action_MoveTo`):
  - constructs `PathfindingRequest` from BTree blackboard
  - publishes `PathfindingRequestEvent` (unmanaged) on Brain's local bus
  - `PathRequestBrainEgressTranslator` batches and emits `PathRequestBatch` DDS

### 5.2 Solver

- `PathfindingSolverSystem` (in `NavigationSolverModule`, `ExecutionPolicy.SlowBackground(10Hz)`)
  - **multi-modal backend selection** [ArchQ8.1: inside the solver]
    ```
    pick backend by:
      MobilityProfile (Wheeled/Tracked/Naval/Flying/Infantry)
      BackendForce (Auto/Navmesh/RoadGraph)
      heuristic: if start & end both within R of road network → RoadGraph;
                 if mixed → splice (navmesh → road → navmesh);
                 else → Navmesh
    ```
  - registers full path into local `TrajectoryPoolManager`, returns `RouteHandle`
  - per-waypoint `TraversalKind` derived from navmesh off-mesh-link `userId` (mapped via §4.6 enum)
  - on failure: `IsReachable = false`, `FailureReason` set

### 5.3 Response demux

- `PathResponseSolverEgressTranslator`: routes back only to `req.SourceNodeId`
- `PathResponseBrainIngressTranslator`:
  - registers waypoints + traversal kinds into Brain's local `TrajectoryPoolManager`
  - publishes `NavigationCorridorReadyEvent` on Brain's bus
- `NavigationCorridorUpdateSystem` (Brain): consumes event, populates `NavigationCorridorMacro` component on the requesting entity. EQS-style materialization [ArchQ4.1 pattern]

## 6. Path execution: Brain windowing

### 6.1 `MoveToExecutor` (Brain) — corridor windowing state machine

```
state Idle:
  on NavigationIntent.ActiveAction == MoveTo and corridor ready:
    SegmentIndex := 0
    populate NearFutureCorridor (or NavigationCorridorSlice — §4.4) with macro[0..8]
    bump ActionInstanceId, emit MoveStartedEvent
    → Following

state Following:
  per tick:
    read NavigationStatus.ProgressS, SegmentIndex
    if SegmentIndex advanced near window end:
      shift window: macro[SegmentIndex..SegmentIndex+8] → NearFutureCorridor
      bump CorridorVersion (no ActionInstanceId bump)
    case NavigationStatus.Result:
      InProgress:           continue
      Arrived:              emit MoveCompletedEvent(Arrived); → Idle, BTree Success
      FailedBlocked:        → Replanning
      FailedUnreachable:    emit MoveCompletedEvent(Unreachable); → Idle, BTree Failure
      FailedNoLayer:        emit MoveCompletedEvent(NoLayer); → Idle, BTree Failure
    if NavmeshVersionObserved != CorridorPlanVersion (final stage):
      → Replanning

state Replanning:
  if ReplanCount >= MaxReplans or elapsed > ReplanTimeBudget:
    emit MoveCompletedEvent(FailedBlocked); → Idle, BTree Failure
  re-publish PathfindingRequestEvent with current SimTransform.XY as Start
  ReplanCount++
  emit PathReplannedEvent
  → wait for NavigationCorridorReadyEvent → Following

on BTree InstanceId bump (preemption):
  → Idle, no event (channel arbitration handles ActionInstanceId)
```

### 6.2 Window-slide policy

- Window slides when `SegmentIndex ≥ window.Start + 4` (half-consumed). Tunable.
- No `ActionInstanceId` bump — sub-`InstanceId` change, dispatcher ignores.
- `CorridorVersion` bumps for diagnostic / Brain-side `WhenNode(ValueChanged)`-style reactivity.

## 7. Muscle-side execution

### 7.1 `NavigationIntentBridgeSystem` — routing by entity kind

`KinematicsMode` enum (byte, on `NavState`) extended [O3]:

```csharp
enum KinematicsMode : byte {
    None              = 0,
    DirectPoint       = 1,    // existing
    RoadGraph         = 2,    // existing
    CustomTrajectory  = 3,    // existing
    Crowd             = 4,    // NEW — entity driven by dtCrowd
    Naval             = 5,    // NEW — surface-water vehicles
    Flying            = 6,    // NEW — volumetric-pathed agents
    // future: Submarine, Amphibious, ...
}
```

`NavState.Mode` is set by `NavigationIntentBridgeSystem` based on the routing decision below.

```
on ActionInstanceId mismatch (new intent):
  switch (intent.ActiveAction):
    MoveTo:
      switch entity's MobilityProfile (from VehicleParametersDto on TKB → ECS):
        Infantry:
          NavState.Mode := Crowd
          if not has<CrowdAgent>: ECB.AddComponent
          dtCrowd.RegisterOrUpdateAgent(entity, target=NearFutureCorridor[0].Position,
                                       radius=Width/2, maxSpeed=MaxSpeedFwd, ...)
        Wheeled | Tracked:
          ensure no CrowdAgent tag
          NavState.Mode := DirectPoint | RoadGraph | (spliced — see below)
          (CarKinematicsSystem takes over via existing path)
        Naval:
          ensure no CrowdAgent
          NavState.Mode := Naval
          (CarKinematicsSystem-shaped integration; surface kinematics — impl TBD)
        Flying:
          ensure no CrowdAgent
          NavState.Mode := Flying
          (volumetric kinematics — impl deferred, §9)
    FollowRoute:
      ensure no CrowdAgent (scripted, no avoidance) [ArchQ13]
      NavState.Mode := CustomTrajectory
      NavState.TrajectoryId := intent payload trajectory id
    Flee:
      Infantry: as MoveTo/Crowd with dynamic re-target each tick
      Vehicles: existing FleeExecutor path, NavState.Mode := DirectPoint
    JoinFormation:
      [deferred — formations section]
```

For spliced vehicle routes (navmesh + road-graph), the solver returns a per-segment hint encoded in `NavWaypoint.LayerMask` / `TraversalKind`. Muscle's `CarKinematicsSystem` switches between `DirectPoint`-style following and `RoadGraph` segment progression as `SegmentIndex` advances. **[Resolved within solver]** — the executor on Muscle reads waypoint metadata; no per-segment intent rewrite from Brain.

### 7.2 dtCrowd integration [ArchQ3.1, Q3.2, Q3.3, Q11]

- **Service:** `IDtCrowdProvider` singleton, lifecycle = scenario load/unload, parallel to `INavmeshProvider`. Host module implements `IDisposable` for teardown [O9].
- **Agent admission:** all humanoid entities tagged `CrowdAgent` at TKB-injection time (`AnimationTkbTranslator` or sibling) — even idle [ArchQ11: all-in is Detour default]
- **Velocity authorship:** `CrowdAgentUpdateSystem` writes `SimVelocity` for tagged entities each tick — **except when `NavigationStatus.Phase == AwaitingTraversal`** (see §7.2.2 below) [O10]
- **Kinematics exclusion:** `LinearKinematicsSystem` and `CarKinematicsSystem` query `.Without<CrowdAgent>()` — already filter-clean per existing pattern
- **Phase placement:**
  ```
  Simulation:
    LocomotionDispatcherSystem        (existing)
    NavigationIntentBridgeSystem      (existing, extended)
    OffMeshLinkDetectionSystem        (NEW — early Simulation, pre-CrowdAgentUpdate
                                              writes Phase=AwaitingTraversal before
                                              velocity write — see §7.2.2 OPEN-v0.2-A)
    CrowdAgentUpdateSystem            (NEW — early Simulation, after OffMeshLinkDetect)
    NavigationExecutionSystem         (existing — Simulation, frustration watchdog
                                              and ProgressS advance — reads velocity)
    AnimationRuntimeBridgeSystem      (DD-1 — mid Simulation, reads SimVelocity)
  PostSimulation:
    LinearKinematicsSystem            (existing, .Without<CrowdAgent>)
    CarKinematicsSystem               (existing, .Without<CrowdAgent>)
    SpatialHashSystem                 (existing)
    TransformSyncSystem               (existing)
  ```

#### 7.2.1 `CrowdAgentUpdateSystem` — pseudo

```
foreach entity in query.With<CrowdAgent, SimVelocity, NavigationStatus>():
    if entity.NavigationStatus.Phase == AwaitingTraversal:
        continue                                    // [O10] suppress velocity write
                                                    // entity is mid-montage; animation owns
                                                    // SimTransform via the (future) root-motion
                                                    // path or kinematic teleport via the
                                                    // off-mesh-link endpoints.
    dtCrowd.UpdateAgent(entity, ...)
    SimVelocity := dtCrowd.GetAgentVelocity(entity)
```

#### 7.2.2 Off-mesh traversal sequence

Triggered when `NavigationExecutionSystem` observes segment K reached and segment K+1 has `TraversalKind != Walk`:

```
Tick T (NavigationExecutionSystem detects link):
    1. write NavigationStatus.Phase = AwaitingTraversal           (immediate, same-tick)
    2. write NavigationStatus.CurrentTraversalKind = K+1.TraversalKind
    3. write AnimationChannel.PlayMontage with TraversalKind discriminant
       (AnimationDispatcherSystem will pick this up next tick — 1-frame latency
        on montage start is acceptable [ArchQ3.3])
    4. ECB.Remove<CrowdAgent>(entity)                              (defers to BeforeSync flush)
    5. emit OffMeshTraversalStartedEvent { Target, TraversalKind, LinkWorldPos }

Tick T+1:
    CrowdAgentUpdateSystem sees Phase == AwaitingTraversal → continue (no velocity write).
    Even though CrowdAgent tag still present (ECB hasn't flushed structural change),
    the Phase check suppresses the velocity slide. [O10 — solves the visual artifact.]
    
    BeforeSync: ECB flushes; CrowdAgent tag removed.
    AnimationDispatcherSystem picks up the new PlayMontage intent, OnEnter the executor.
    Animation runtime begins the montage; SimTransform driven by montage endpoints
    (or root-motion in future; for v1 the montage is authored against the off-mesh-link
    endpoint positions so the visual lands correctly).

Tick T+M (MontageEndedEvent fires for the traversal montage):
    NavigationExecutionSystem observes the event:
    1. case MontageEndedEvent.EndReason:
         NaturalEnd | BlendedOutByNext:
             write NavigationStatus.Phase = Following
             write NavigationStatus.CurrentTraversalKind = None
             ECB.Add<CrowdAgent>(entity)
             dtCrowd.RegisterOrUpdateAgent(entity, target=segment K+2.Position)
             advance SegmentIndex past the traversal segment
             emit OffMeshTraversalEndedEvent { Target, TraversalKind, Success=true }
         Failed | Interrupted:
             write NavigationStatus.Phase = Stuck
             write NavigationStatus.Result = FailedBlocked
             emit OffMeshTraversalEndedEvent { Target, TraversalKind, Success=false }
             (Brain MoveToExecutor observes FailedBlocked, decides replan or fail — §6.1)

Tick T+M+1:
    CrowdAgentUpdateSystem sees Phase == Following → resumes velocity authorship.
    BeforeSync: ECB flushes; CrowdAgent tag re-added (if it was removed).
    dtCrowd target already set to next walkable waypoint.
```

**Critical correctness note** [O10]: the suppression only works if `CrowdAgentUpdateSystem` reads a `Phase` that was set *before* it ran this tick. Three possible system orderings need to be evaluated by the architect:

1. **Link detection runs *before* `CrowdAgentUpdateSystem` in `Simulation`.** `NavigationExecutionSystem` (or a small new `OffMeshLinkDetectionSystem` siblings to it) detects the link approach and writes `Phase = AwaitingTraversal` in early `Simulation`, *before* `CrowdAgentUpdateSystem` runs. Suppression takes effect same-tick. Cleanest.
2. **Link detection runs *after* `CrowdAgentUpdateSystem`.** This is what the existing arch implies — `NavigationExecutionSystem` measures velocity (which `CrowdAgentUpdateSystem` just wrote), then writes Phase. Suppression takes effect tick T+1 — one frame of unwanted velocity at the link entry. May be visually acceptable at 60Hz; architect to judge.
3. **Split the responsibility.** Keep `NavigationExecutionSystem` in `Simulation` for its existing frustration/ProgressS watchdog role (which *does* need to read velocity post-kinematics-integration). Add a separate `OffMeshLinkDetectionSystem` in early `Simulation` that runs *before* `CrowdAgentUpdateSystem` purely for the link-approach check.

**[OPEN-v0.2-A]** Architect: which of (1)/(2)/(3) does the design adopt? My recommendation is (3) — clean separation of "detect ProgressS approaching a link" (pure read, runs early) from "watch SimVelocity for frustration" (needs post-kinematics velocity, runs late). The detection system is small (one query, two reads, conditional Phase + AnimationChannel write).

Note: the existing architect Q14 answer placed `NavigationExecutionSystem` in `Simulation` and described it watching `SimVelocity`. That role keeps it in `Simulation`. The O10 link-detection logic is a *different* responsibility that wants to run in *early* `Simulation`, which argues for (3).

### 7.3 `NavigationExecutionSystem` — solver-agnostic [ArchQ14]

Pre-existing system, gains nothing new beyond reading `NearFutureCorridor` to update `SegmentIndex` and `ProgressS`. Frustration watchdog already universal.

## 8. Multi-layer navmesh

### 8.1 `INavmeshProvider` — amended in place [O4]

```csharp
interface INavmeshProvider {
    bool      IsWalkable(Vector2 point, ushort layerMask);
    Vector3   ProjectToNavmesh(Vector2 point, float maxDist, ushort layerMask);
    void      SampleNavmeshPoints(BoundingVolume v, float density, ushort layerMask, ICandidateSink sink);
    bool      PathExists(Vector2 a, Vector2 b, ushort layerMask, float maxCost);
    float     PathCost(Vector2 a, Vector2 b, ushort layerMask);
    uint      QueryVersion(BoundingBox2D bounds, ushort layerMask);  // stub-constant v1
}
```

Interface amended in place per [O4]: no `INavmeshProvider2` façade. EQS template authors who use the v1 single-layer signatures get a one-time mechanical migration adding the `layerMask` parameter (default = entity's `NavAgentProfile.PreferredLayerMask`). EQS migration is mechanical and tracked in the EQS follow-up doc.

### 8.2 Layers

```csharp
[Flags] enum NavLayerMask : ushort {
    None     = 0,
    Infantry = 1,
    Vehicle  = 2,
    Naval    = 4,
    // future expansion: Amphibious = Infantry | Naval, etc.
}
```

**Per-layer separate navmesh** [O5]: each `NavLayerMask` value bakes a fundamentally separate navmesh with different rasterization parameters (radius, slope, step height). Infantry bake: 0.3 m radius, 60° max slope. Vehicle bake: 1.5 m radius, 20° max slope, 0.1 m step. Naval bake: water-surface polygons only.

`INavmeshProvider` implementation maintains an internal lookup table `{ NavLayerMask → dtNavMesh }` and dispatches queries against the right mesh per `layerMask` argument. The API surface stays unified — only baking diverges.

`NavLayerMask` is a flag mask to support queries like "reachable on either Infantry OR Naval layers" for amphibious agents in the future. v1 callers always pass exactly one bit set.

Baking pipeline is **outside this design's scope** — TBD when DotRecast integration lands. Doc carries the layer-mask contract; baking tool work is a separate ticket.

### 8.3 `NavAgentProfile` component

```csharp
struct NavAgentProfile {
    ushort PreferredLayerMask;     // from VehicleParametersDto-derived TKB extension
    float  AgentRadius;
    float  AgentHeight;
    float  MaxSlope;
    float  MaxStepHeight;
}
```

EQS `NavmeshReachable`/`PathCost` default `layerMask` from `ctx.Self`'s `NavAgentProfile.PreferredLayerMask`.

### 8.4 EQS revision (separate doc, mentioned here for completeness)

Mandatory but mechanical: add `NavLayerMask` parameter to `NavmeshReachable` and `PathCost` tests. Default = entity's `PreferredLayerMask`. Backwards-compatible at the BTree-author level if default is auto-supplied.

## 9. Flying agents — `IVolumetricPathProvider`

```csharp
interface IVolumetricPathProvider {
    bool   IsFlyable(Vector3 point);
    bool   PathExists(Vector3 a, Vector3 b, FlyProfile profile, float maxCost);
    int    Plan(Vector3 a, Vector3 b, FlyProfile profile, Span<Vector3> output);
    uint   QueryVersion(BoundingBox3D bounds);
}
```

- Implementation deferred.
- **Folded into existing `PathRequestBatch`** [O6]: `MobilityProfile = 4 = Flying` discriminant routes the request inside `PathfindingSolverSystem` to `IVolumetricPathProvider` instead of `INavmeshProvider`. No new DDS topic.
- `PathfindingRequest` already carries `RelativeVector3`-encoded `Start`/`End` (effectively 3D) — XY components used by ground planners, full 3D used by `IVolumetricPathProvider`. No wire-format change needed.
- The `NavWaypoint` shape on the response is unchanged: `Position` is `Vector2` (XY), elevation projected on Muscle from the corridor's 3D source data in `TrajectoryPoolManager`. **[OPEN-v0.2-B]** Architect: confirm that flying corridors retain their 3D Y-component somewhere. Likely needs a `NavWaypoint.AltitudeMeters` float — adds 4 B per waypoint (16 B → 20 B). Or a parallel `[InlineArray<float, 8>] AltitudeProfile` on `NavigationIntent`. Or `NavWaypoint.Position` becomes `Vector3` for all agents (32 B per waypoint, `NarrowCorridor` bumps to 256 B — still fine since `NavigationIntent` is exempt from channel budget).
- Flying agents are **not** `CrowdAgent`-tagged. `NavState.Mode = Flying`. Air steering / avoidance is a separate later design.

## 10. Naval agents

- v1: ships `NavLayer.Naval` and bakes a surface navmesh through `INavmeshProvider`.
- Surface boats use `CarKinematicsSystem`-style integration (no crowd) — they're vehicles in a different layer.
- Submarine depth control: deferred.

## 11. Animation seam [DD-1, ArchQ6.1]

- **Continuous locomotion:** zero new contract — `AnimationRuntimeBridgeSystem` reads `SimTransform`/`SimVelocity` per-tick via existing `UpdateLocomotionInputs`. Navigation writes velocity (via dtCrowd or kinematics); animation reads. Done.
- **Discrete traversal:** `NavigationExecutionSystem` writes `AnimationChannel.PlayMontage` with a `TraversalKind` discriminant. **Animation runtime owns the `TraversalKind → MontageId` lookup** in its `CharacterAnimationDefDto` (per [ArchQ6.1]).
- **Surface-type animation hint:** each `NavWaypoint` carries a `SurfaceType` byte (Grass/Concrete/Mud/...). `AnimationRuntimeBridgeSystem` consumes the current segment's `SurfaceType` to drive footstep/gait variant blending [O7]. Per-waypoint placement (vs. a separate component) lets the animation bridge anticipate terrain changes and blend gaits as the agent crosses segment boundaries, naturally synchronized with `ProgressS`.
- **Stance interaction:** Brain writes `StanceIntent` (DD-1 design); naval/vehicle paths ignore it; humanoid path adjusts `MaxMoveSpeed` based on `StanceStatus.Current` (Standing=full, Crouched=0.5, Prone=0.2 — default ratios, TKB-configurable). The reduction is applied in `NavigationIntentBridgeSystem` when registering the dtCrowd agent.

## 12. Engine Event Catalog entries [DD-3 pattern]

| Event | Target field | Brain-visible | QoS | Notes |
|---|---|---|---|---|
| `MoveStartedEvent { Target, ActionInstanceId, TotalDistance, EstDuration, BackendKind }` | Target | Yes | Reliable | Fired by `MoveToExecutor` on `Following` entry |
| `MoveCompletedEvent { Target, ActionInstanceId, Reason }` | Target | Yes | Reliable | `Reason ∈ {Arrived, Unreachable, FailedBlocked, NoLayer, Preempted}` |
| `PathReplannedEvent { Target, ReplanCount, Reason }` | Target | Yes | Reliable | Brain-published when re-issuing request |
| `OffMeshTraversalStartedEvent { Target, TraversalKind, LinkWorldPos }` | Target | Yes | Reliable | Muscle-published, bridged to Brain |
| `OffMeshTraversalEndedEvent { Target, TraversalKind, Success }` | Target | Yes | Reliable | Muscle-published, bridged to Brain |
| `MoveBlockedEvent { Target, BlockedDurationSec, NearestObstacleEntity }` | Target | Yes | Reliable | Throttled — fires once per blocking episode; emitted from `NavigationExecutionSystem` when `FrustrationTicks > N/2` (early warning) |
| `WaypointReachedEvent { Target, WaypointIndex, RemainingCount }` | Target | **No** (Muscle-local) | BestEffort | Cosmetic — VFX trigger; never crosses network |

All registered in `EngineEventCatalog` per DD-3 §4 pattern. Brain consumers reach via `WhenNode(EventFired)`. `TargetFieldName = "Target"` auto-filter to Self.

## 13. Authoring surfaces

### 13.1 `LocomotionChannel` action surface (post-rationalization) [ArchQ7.1]

| ActionId | Status | Notes |
|---|---|---|
| `ActionIdMoveTo` | Kept, enriched | New `MoveToParams` (§7.2) |
| `ActionIdFollowRoute` | Kept | Scripted spline, no dtCrowd [ArchQ13] |
| `ActionIdFlee` | Kept | 8-byte `Entity` threat handle [ArchQ7.1] |
| `ActionIdJoinFormation` | Kept (deferred design) | Will surface in formations doc |
| `ActionIdFollowRoadGraph` | **Removed** | Subsumed by `MoveTo` with `Backend = ForcedRoadGraph` |

### 13.2 `MoveToParams` 32B layout [ArchQ7.2 confirmed]

```csharp
struct MoveToParams {                     // 32 B
    Vector2 Destination;                  //  8
    float   ArrivalRadius;                //  4
    float   MaxMoveSpeed;                 //  4
    float   ReplanTimeBudget;             //  4
    ushort  NavLayerMask;                 //  2
    byte    BackendForce;                 //  1  // 0=Auto, 1=Navmesh, 2=RoadGraph
    byte    Flags;                        //  1  // AllowReplan, FailOnBlocked, NoStop, ...
    byte    ReverseAllowed;               //  1
    byte    MaxReplans;                   //  1
    fixed byte _reserved[6];              //  6  // explicit padding to 32B
}
```

### 13.3 Brain-side primitives

- **BTree actions** (existing pattern):
  - `Action_PlanRoute` (existing — issues `PathfindingRequestEvent`, waits for `NavigationCorridorReadyEvent`)
  - `Action_MoveTo` (existing, extended to consume the macro corridor — internally calls `Action_PlanRoute` if no corridor)
  - `Action_Flee`, `Action_FollowRoute`, `Action_JoinFormation` — existing
- **Blueprint Channel Command Catalog entries:**
  - `ChannelCommand(Locomotion/MoveTo)` with TKB-driven layer-mask filter (only layers the entity's `NavAgentProfile.PreferredLayerMask` permits)
  - `WaitForChannel(LocomotionChannel)` — blocks until `Status = Success/Failure`
- **`WhenNode(EventFired)`** on any of the §12 events
- **`WhenNode(ValueChanged)`** on `NavigationStatus.Result` for non-blocking reactions

## 14. Patch-propagation forward-compatibility [ArchQ9.1 confirmed]

- API surface in place:
  - `INavmeshProvider.QueryVersion(bounds, layerMask)` → returns constant `1` in v1
  - `PathResult.NavmeshVersionAtPlan` carried but never differs in v1
  - `NavigationStatus.NavmeshVersionObserved` carried but never differs in v1
  - `MoveToExecutor` replan-on-version-mismatch logic in place but never fires in v1
- Final stage: `INavmeshProvider` implementation maintains regional version vectors; patches bump regional versions; `QueryVersion` becomes meaningful. No Brain-side code changes required.
- Patch propagation DDS shape: deferred, brief sketch in §14.x of the final doc.

## 15. Performance & budgeting

- **`PathfindingSolverSystem`:** `SlowBackground(10Hz)`, budget bands [Critical 50% / Normal 35% / Low 15%] mirroring EQS §6. Snapshot-on-demand, `EventAccumulator` integration for missed-frame events.
- **`PathfindingBatchData` capacity:** raised from 64 to **256** [O8]. `NativeArray` of lightweight structs; memory is practically free. Headroom prevents silent modulo-overwrites during mass replans (e.g., navmesh patch invalidating many corridors at once in the final stage). Exhaustion behavior remains as-is (silent overwrite); no formal failure state needed at 256 slots.
- **`CrowdAgentUpdateSystem`:** synchronous in-tick (`ExecutionPolicy.Synchronous`, `DataStrategy.Direct`). dtCrowd agent slot pool sized at startup from TKB humanoid count + headroom (default 2x).
- **DDS bandwidth (`NavigationIntent`):**
  - Per-entity worst case: 184 B (or 96 B + side-buffer slice — depends on §4.4 resolution)
  - 1000 active movers, window slides ~every 1.5 seconds avg (8 waypoints / ~5 m each / ~3 m/s) = ~125 KB/s aggregate. Acceptable per DDS norms.
  - Idle entities: `SmartEgressUtil` dirty-flag, zero traffic when `ActionInstanceId` and corridor unchanged.
- **`MoveToExecutor` per-tick cost (Brain):** O(1) per active mover (read status, compare ProgressS to window boundary, slide if needed). Cost ≈ that of `MissionDirectorSystem`'s task-advance scan.

## 16. Hot reload

- **`VehicleParametersDto` and its TKB-companion descriptors:** existing TKB hot-reload pipeline. New fields (radius/agent-height for dtCrowd) follow standard `ANIM00x`-style validators [DD-4 pattern].
- **Compiled navmesh data:** not hot-reloadable. Scenario reload required for navmesh changes.
- **`IDtCrowdProvider` lifecycle around scenario reload:** the `IEcsModule` hosting the crowd systems implements `IDisposable` [O9]. On scenario unload, the orchestrator tears down the active execution topology and calls `Dispose()`, which clears the `dtCrowd` agent table and releases the native `dtCrowd` instance. New scenario load creates a fresh provider. Entities re-register on first `NavigationIntentBridgeSystem` tick of the new scenario.

## 17. Migration from current POC

| Element | Status |
|---|---|
| `LocomotionChannel` + dispatcher | **Keep** |
| `NavigationIntent`/`NavigationStatus` | **Keep, extend** — Vector2 destination, NavLayerMask, NearFutureCorridor (or sliced), Result enum extension |
| `MoveToExecutor`, `FollowRouteExecutor`, `FleeExecutor`, `JoinFormationExecutor` | **Keep, extend** for new corridor + dtCrowd path |
| `FollowRoadGraphExecutor` | **Remove** — collapsed into `MoveToExecutor` with `Backend = ForcedRoadGraph` |
| `PathfindingSolverSystem` (Dijkstra over RoadNetworkBlob) | **Keep, extend** — multi-modal backend selection |
| `RoadGraphNavigator` | **Keep** — used by spliced planner |
| `RoadNetworkBlob` | **Keep** |
| `PathfindingBatchData` | **Keep, possibly resize** (§15) |
| `NavigationExecutionSystem` | **Keep** — already solver-agnostic [ArchQ14] |
| `INavmeshProvider` | **Amended in place** — `NavLayerMask` added to all queries [O4] |
| `IDtCrowdProvider` | **New** |
| `CrowdAgent` tag, `CrowdAgentUpdateSystem` | **New** |
| `NavAgentProfile` component | **New** |
| `NavigationCorridorMacro` (Brain-local) | **New** — holds `BrainRouteHandle` into `TrajectoryPoolManager` |
| Engine Event Catalog entries (§12) | **New** |
| `IVolumetricPathProvider` | **New (interface only)** |
| `TraversalKind` enum, `NavWaypoint` struct | **New** |
| `OffMeshLinkDetectionSystem` | **New** — early Simulation, link-approach detection (see §7.2 OPEN-v0.2-A) |

## 18. Open items — resolution status

### 18.1 O1–O10 resolved (v0.1 → v0.2)

| # | Topic | Resolution |
|---|---|---|
| O1 | `NavigationIntent` 96B budget | Exempt; N=8 corridor stays inline. See §4.4. |
| O2 | `RequestDeadlineTick` | Not added; compute-and-discard preserved. See §5.1. |
| O3 | `NavState.Mode` extensibility | `KinematicsMode` enum extended with `Naval`, `Flying`, `Crowd`. See §7.1. |
| O4 | `INavmeshProvider` v1→v2 | Amended in place. See §8.1. |
| O5 | Layer baking | Separate navmesh per layer. See §8.2. |
| O6 | Flying path-request topic | Folded into `PathRequestBatch` with `MobilityProfile = 4`. See §9. |
| O7 | `SurfaceType` on `NavWaypoint` | Confirmed inline on waypoint. See §11. |
| O8 | `PathfindingBatchData` capacity | Raised to 256. See §15. |
| O9 | `IDtCrowdProvider` teardown | `IDisposable` on host `IEcsModule`. See §16. |
| O10 | Off-mesh ECB latency | `CrowdAgentUpdateSystem` early-out on `Phase == AwaitingTraversal`. See §7.2.1. |

### 18.2 New open items surfaced by v0.2 patches

- **OPEN-v0.2-A (§7.2.2):** Phase ordering for the O10 mechanism. Three options outlined; recommendation is option (3) — a new `OffMeshLinkDetectionSystem` in early `Simulation`, distinct from `NavigationExecutionSystem` which stays in its existing `Simulation` slot. Architect to confirm.
- **OPEN-v0.2-B (§9):** Flying corridor 3D shape. `NavWaypoint.Position` is currently `Vector2` (XY ground); flying needs Y. Architect to confirm one of:
  - (a) Add `AltitudeMeters` float to `NavWaypoint` (20 B/waypoint, `NearFutureCorridor` 160 B);
  - (b) Promote `NavWaypoint.Position` to `Vector3` for all agents (24 B/waypoint, corridor 192 B — `NavigationIntent` total ~248 B, still exempt from channel budget);
  - (c) Parallel altitude array on `NavigationIntent` for flying only (32 B extra when present).
  - Recommendation: **(b)** for uniformity — ground agents pay 8 B/waypoint overhead but Z = SimTransform.Z is trivially set by the solver. Simpler code, no per-agent-type branching.

### 18.3 Items genuinely deferred (not blocking the design)

- Formations, squad cohesion, flow fields (separate doc).
- Threat-aware path cost (separate doc).
- Patch-propagation impl (final stage; API hooks in place — §14).
- Flying steering (separate doc).
- Submarine depth control.
- Root-motion authority flip (DD-1 future-work).
- FollowRoute / Flee / JoinFormation executor polish (kept as-is for v1; revisit if usage patterns shift).

## 19. Roadmap (rough, behind feature flag)

Suggested order, each step independently shippable:

1. **NavLayerMask + INavmeshProvider v2.** EQS migration (mechanical).
2. **MoveToParams extension + NavigationIntent layout change** (32B params + corridor side-buffer). Existing executors keep working.
3. **DotRecast integration + `INavmeshProvider` Stride3D impl.** First real navmesh queries.
4. **dtCrowd + `IDtCrowdProvider`** + `CrowdAgentUpdateSystem` + kinematics filtering. Humanoid avoidance.
5. **Multi-modal planner** in `PathfindingSolverSystem` (navmesh + road-graph splice).
6. **TraversalKind + off-mesh montage path.** Connects to existing animation infra.
7. **Engine Event Catalog entries.** Brain-side `WhenNode(EventFired)` authoring works.
8. **Corridor windowing** — `NavigationCorridorMacro`, Brain-side slide-window state machine, `NavigationCorridorReadyEvent`.
9. **Naval bake-able layer.** Boat scenarios.
10. **`IVolumetricPathProvider` stub** + `PathRequestBatch` extension. Air agents minimally orderable.

Deferred to follow-up designs (each its own doc):
- Formations & squad cohesion
- Flow fields
- Threat-aware path cost
- Patch propagation impl
- Flying steering
- Submarine depth
- Root-motion authority flip

---

*End v0.2 condensed. Two open items (OPEN-v0.2-A phase ordering, OPEN-v0.2-B flying corridor 3D) remain for architect input before expansion to human-readable v1.0.*
