# Navigation Subsystem — Design v0.1 (condensed, architect review)

> **Status.** Condensed first-pass design for architect review.
> Dense bullets, code-shape over prose, no re-justification of established rulings.
> Resolved questions cited as `[ArchQ.N]` where N is from the navigation question batch.
> Open items as `[OPEN]`.

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
                                                                      [OPEN] separate provider
                                                                  
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
    // [32B] MoveToParams (§7.2)
    // [N=8 InlineArray<NavWaypoint>] NearFutureCorridor (§5.3)
    // CorridorPlanVersion uint
    // RouteHandleBrain int — debugging only on the wire; Muscle never resolves it
    // total bytes: see §4.4 layout audit
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
    byte    MobilityProfile;      // existing
    byte    BackendForce;         // NEW — 0=Auto, 1=Navmesh, 2=RoadGraph
    float   MaxCost;              // NEW — cost budget; 0 = unbounded
    uint    NavmeshVersionAtRequest;  // NEW — for final-stage stamp comparison; stub-constant v1
    // RequestDeadlineTick — [OPEN] do we need a soft deadline?
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

### 4.4 `NavigationIntent` byte-layout audit

```
Existing channel header:
  ActiveAction       2
  Status             1
  (pad)              1
  BehaviorInstanceId 4
  ActionInstanceId   4
  DispatchedInstId   4
                    = 16 B
ActionParams (MoveToParams, §7.2):                            32 B
NearFutureCorridor: InlineArray<NavWaypoint, 8>, ~16 B/wpt: 128 B
CorridorPlanVersion (uint):                                    4 B
RouteHandleBrain (int):                                        4 B
                                                          ──────
                                                            184 B
```

[OPEN] **This breaks the 96B `MaxChannelSizeBytes` budget.** The inline corridor pushes `NavigationIntent` to ~184 B. Two paths to resolve:

- **(a)** Keep `NavigationIntent` strictly at 96B (header + 32B params + 32B state). Move `NearFutureCorridor`, `CorridorPlanVersion`, `RouteHandleBrain` into a **separate Brain→Muscle side-buffer component** `NavigationCorridorSlice` (replicates via its own DDS topic + translator). This mirrors `AnimationMontageQueue`'s relationship to `AnimationChannel`.
- **(b)** Channels have `MaxChannelSizeBytes = 96`; `NavigationIntent` is not strictly a channel component — it's a CQRS descriptor pair component (architect Q1.1 noted Brain writes intent, Muscle writes status, mirroring `NavigationIntent`/`NavigationStatus` precedent). If `NavigationIntent` is exempt from the 96B channel budget, the size is acceptable.

**Architect:** confirm which. (b) is simpler if permitted. (a) is the safe path matching the animation precedent if not.

### 4.5 `NavWaypoint` shape

```csharp
struct NavWaypoint {                      // 16 B
    Vector2 Position;                     // 8 — XY ground; elevation projected by Muscle
    byte    TraversalKind;                // 1 — Walk | Jump | JumpDown | JumpAcross | Climb | Door
    byte    SurfaceType;                  // 1 — for animation locomotion-input hint (Grass/Concrete/Mud/...)
    ushort  LayerMask;                    // 2 — which navmesh layer the segment is on
    float   SegmentLengthMeters;          // 4 — for ETA calculation
}
```

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
  - per-waypoint `TraversalKind` derived from navmesh off-mesh-link `userId` (mapped via §10.2 enum)
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

```
on ActionInstanceId mismatch (new intent):
  switch (intent.ActiveAction):
    MoveTo:
      switch entity's MobilityProfile (from VehicleParametersDto on TKB → ECS):
        Infantry:
          if not has<CrowdAgent>: AddComponent (ECB)
          dtCrowd.RegisterOrUpdateAgent(entity, target=NearFutureCorridor[0].Position,
                                       radius=Width/2, maxSpeed=MaxSpeedFwd, ...)
        Wheeled | Tracked:
          ensure no CrowdAgent tag
          set NavState.Mode = DirectPoint / RoadGraph / Spliced (per intent.Backend)
          (CarKinematicsSystem takes over)
        Naval:
          ensure no CrowdAgent
          set NavState.Mode = Naval [OPEN — confirmation that NavState can host naval]
        Flying:
          [OPEN — separate volumetric provider drives this path; details deferred]
    FollowRoute:
      ensure no CrowdAgent (scripted, no avoidance) [ArchQ13]
      set NavState.TrajectoryId from intent payload
    Flee:
      Infantry: as MoveTo with dynamic re-target each tick
      Vehicles: existing FleeExecutor path
    JoinFormation:
      [deferred — formations section]
```

### 7.2 dtCrowd integration [ArchQ3.1, Q3.2, Q3.3, Q11]

- **Service:** `IDtCrowdProvider` singleton, lifecycle = scenario load/unload, parallel to `INavmeshProvider`
- **Agent admission:** all humanoid entities tagged `CrowdAgent` at TKB-injection time (`AnimationTkbTranslator` or sibling) — even idle [ArchQ11: all-in is Detour default]
- **Velocity authorship:** `CrowdAgentUpdateSystem` writes `SimVelocity` for tagged entities each tick
- **Kinematics exclusion:** `LinearKinematicsSystem` and `CarKinematicsSystem` query `.Without<CrowdAgent>()` — already filter-clean per existing pattern
- **Phase placement:**
  ```
  Simulation:
    LocomotionDispatcherSystem        (existing)
    NavigationIntentBridgeSystem      (existing, extended)
    CrowdAgentUpdateSystem            (NEW — early Simulation)
    AnimationRuntimeBridgeSystem      (DD-1 — mid Simulation, reads SimVelocity)
  PostSimulation:
    LinearKinematicsSystem            (existing)
    CarKinematicsSystem               (existing)
    NavigationExecutionSystem         (existing — universal frustration watchdog + ProgressS)
    SpatialHashSystem                 (existing)
    TransformSyncSystem               (existing)
  ```
- **Off-mesh traversal** (handled in `NavigationExecutionSystem` extension or a small new `OffMeshTraversalSystem` siblings to it):
  - on segment K with `Walk` advancing to segment K+1 with non-`Walk` `TraversalKind`:
    1. ECB: remove `CrowdAgent` from entity
    2. write `AnimationChannel.PlayMontage` with `TraversalKind`-coded request (animation runtime resolves to montage asset via `CharacterAnimationDefDto` — ArchQ6.1)
    3. set `NavigationStatus.Phase = AwaitingTraversal`
    4. emit `OffMeshTraversalStartedEvent`
    5. await `MontageEndedEvent` (Brain or Muscle bus — Muscle-local handler)
    6. on end: ECB add `CrowdAgent`, retarget dtCrowd to segment K+2
    7. emit `OffMeshTraversalEndedEvent`
    8. `Phase = Following`
  - on `MontageEndedEvent` with `EndReason = Failed/Interrupted`:
    - write `NavigationStatus.Result = FailedBlocked` (Brain decides replan)
- [ArchQ3.3 note]: 1-frame ECB latency on tag toggle is acceptable; if not, switch to a `CrowdAgentSuspended` bool flag on `CrowdAgent` (component-data toggle, no structural change).

### 7.3 `NavigationExecutionSystem` — solver-agnostic [ArchQ14]

Pre-existing system, gains nothing new beyond reading `NearFutureCorridor` to update `SegmentIndex` and `ProgressS`. Frustration watchdog already universal.

## 8. Multi-layer navmesh

### 8.1 `INavmeshProvider` v2

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

[OPEN ArchQ5.1] Amend interface in place vs. `INavmeshProvider2`? EQS template authors call this — preference for breaking change (one-time migration) or façade?

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

[OPEN ArchQ5.2] Per-layer baking strategy:
- **(a)** Single navmesh, multiple Recast `dtArea` types, per-agent `dtQueryFilter`. Cheaper bake, single source. Vehicle area ⊆ Infantry area.
- **(b)** Separate navmesh per layer (different agent radius / slope limits). Standard Detour vehicle pattern.

Recommendation: **(b)** for v1 because vehicle navmeshes want fundamentally different bake parameters (larger radius, gentler max-slope, narrower passable corridors). DotRecast supports independent builds per profile. The `NavLayerMask` API is identical in both cases — only baking differs.

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

- Implementation deferred. v1 ships interface + DDS request/response variants (`AirPathRequestBatch`/`AirPathResponseBatch`)? **[OPEN]** — or fold into the existing `PathRequestBatch` with a `MobilityProfile = Flying` discriminant and a 3D-vs-2D union? Latter is simpler but bloats the wire struct.
- Recommendation: **fold into existing `PathRequestBatch`**. `Start`/`End` already `RelativeVector3` (encoded 3D). The solver branches by `MobilityProfile` and dispatches to `IVolumetricPathProvider` instead of `INavmeshProvider`.
- Flying agents are **not** `CrowdAgent`-tagged. Air steering / avoidance is a separate later design.

## 10. Naval agents

- v1: ships `NavLayer.Naval` and bakes a surface navmesh through `INavmeshProvider`.
- Surface boats use `CarKinematicsSystem`-style integration (no crowd) — they're vehicles in a different layer.
- Submarine depth control: deferred.

## 11. Animation seam [DD-1, ArchQ6.1]

- **Continuous locomotion:** zero new contract — `AnimationRuntimeBridgeSystem` reads `SimTransform`/`SimVelocity` per-tick via existing `UpdateLocomotionInputs`. Navigation writes velocity (via dtCrowd or kinematics); animation reads. Done.
- **Discrete traversal:** `NavigationExecutionSystem` writes `AnimationChannel.PlayMontage` with a `TraversalKind` discriminant. **Animation runtime owns the `TraversalKind → MontageId` lookup** in its `CharacterAnimationDefDto` (per [ArchQ6.1]).
- **Surface-type animation hint:** each `NavWaypoint` carries a `SurfaceType` byte (Grass/Concrete/Mud/...). `AnimationRuntimeBridgeSystem` consumes the current segment's `SurfaceType` to drive footstep/gait variant blending. **[OPEN]** — confirm this is the right place for the hint vs. a separate component read by the bridge.
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
- **`PathfindingBatchData` capacity:** current 64. **[OPEN ArchQ2.2]** AAA-scale scenarios (500+ active movers, with periodic mass replans) may exhaust 64-slot ring. Architect: raise to 256? Or formalize the exhaustion behavior (currently silently overwrites)?
- **`CrowdAgentUpdateSystem`:** synchronous in-tick (`ExecutionPolicy.Synchronous`, `DataStrategy.Direct`). dtCrowd agent slot pool sized at startup from TKB humanoid count + headroom (default 2x).
- **DDS bandwidth (`NavigationIntent`):**
  - Per-entity worst case: 184 B (or 96 B + side-buffer slice — depends on §4.4 resolution)
  - 1000 active movers, window slides ~every 1.5 seconds avg (8 waypoints / ~5 m each / ~3 m/s) = ~125 KB/s aggregate. Acceptable per DDS norms.
  - Idle entities: `SmartEgressUtil` dirty-flag, zero traffic when `ActionInstanceId` and corridor unchanged.
- **`MoveToExecutor` per-tick cost (Brain):** O(1) per active mover (read status, compare ProgressS to window boundary, slide if needed). Cost ≈ that of `MissionDirectorSystem`'s task-advance scan.

## 16. Hot reload

- **`VehicleParametersDto` and its TKB-companion descriptors:** existing TKB hot-reload pipeline. New fields (radius/agent-height for dtCrowd) follow standard `ANIM00x`-style validators [DD-4 pattern].
- **Compiled navmesh data:** not hot-reloadable. Scenario reload required for navmesh changes.
- **`IDtCrowdProvider` lifecycle around scenario reload:** destroy + recreate, agent table empties; entities re-register on next `NavigationIntentBridgeSystem` tick. **[OPEN]** — confirm whether scenario reload triggers the right teardown hook.

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
| `INavmeshProvider` | v1 → v2 (NavLayerMask) [OPEN: ArchQ5.1] |
| `IDtCrowdProvider` | **New** |
| `CrowdAgent` tag, `CrowdAgentUpdateSystem` | **New** |
| `NavAgentProfile` component | **New** |
| `NavigationCorridorMacro` (Brain-local), `NavigationCorridorSlice` (if §4.4(a)) | **New** |
| Engine Event Catalog entries (§12) | **New** |
| `IVolumetricPathProvider` | **New (interface only)** |
| TraversalKind enum, NavWaypoint struct | **New** |

## 18. Open items requiring architect input

Numbered for tracking:

- **O1 (§4.4):** Is `NavigationIntent` subject to the 96B `MaxChannelSizeBytes`? If yes → split inline corridor into separate `NavigationCorridorSlice` side-buffer component. If no → keep inline.
- **O2 (§5.1):** Does `PathfindingRequest` need a soft `RequestDeadlineTick` field, or is the current "compute and discard" pattern sufficient?
- **O3 (§7.1):** `NavState.Mode` extensibility for Naval (and later Flying)? Or new sibling state component?
- **O4 (§8.1):** `INavmeshProvider` v1 → v2 — in-place amend or `INavmeshProvider2` façade?
- **O5 (§8.2):** Layer baking — per-layer separate navmesh, or single navmesh with `dtArea` filters?
- **O6 (§9):** Flying — fold into existing `PathRequestBatch` with `MobilityProfile = Flying`, or new `AirPathRequestBatch` topic?
- **O7 (§11):** `SurfaceType` byte on `NavWaypoint` for animation hint — right place, or separate component?
- **O8 (§15):** `PathfindingBatchData` 64-slot ring sufficient for AAA scale (500+ movers)? Raise to 256? Formalize exhaustion?
- **O9 (§16):** Scenario reload triggers correct `IDtCrowdProvider` teardown? Already covered by existing lifecycle, or new hook needed?
- **O10 (§7.2):** `CrowdAgent` tag toggle for off-mesh suspend is ECB-deferred (1-frame latency). Architect noted this acceptable, but the entity is in a half-state for one tick (`CrowdAgent` still present, executor wants montage to play). Confirm `NavigationStatus.Phase = AwaitingTraversal` is read by `CrowdAgentUpdateSystem` to suppress velocity write that tick, OR confirm 1-frame stale velocity is acceptable visually.

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

*End v0.1 condensed.*
