# DD-Tests-Nav — Navigation Control Test Strategy — Detailed Design

> **Scope:** Three test layers (unit, system, integration), the twelve
> integration scenarios that prove the navigation mechanism, the
> `NavTestHarness` and `PumpUntil`-style helpers, inline TKB test
> data conventions, failure-mode policy.
>
> **Out of scope:** Tests for the eventual real backends (DotRecast,
> dtCrowd) — those will be a separate doc when those backends land.
> Test-data fixture content itself (DD-Fake-Nav §7 carries the
> `NavTestMap` schema and canonical-map list).
>
> **Audience:** Navigation implementation team, QA / test
> infrastructure team.
>
> **Reads alongside:** Navigation Design (architecture), DD-Fake-Nav
> (the fakes the tests run against), DD-Tests Animation Control
> (existing test-strategy precedent).

---

## Table of contents

1. Why three test layers
2. Test infrastructure conventions
3. Layer 1 — Unit tests for the fakes
4. Layer 2 — System tests for individual Muscle / Brain systems
5. Layer 3 — Integration tests (in-process, all-in-one mode)
6. The twelve integration scenarios
7. Helper utilities — `PumpUntil` and friends
8. Inline TKB test data
9. Failure-mode policy for AI agents
10. Scale-out variant (deferred)

---

## 1. Why three test layers

The navigation subsystem spans cognitive Brain code, async solver code, Muscle execution code, native fake providers, and event-catalog wiring. A single integration test that exercises all of them is unmaintainable when one breaks — you can't tell *which* layer failed.

| Layer | Tests | What it proves | Speed |
|---|---|---|---|
| **1. Unit** | Each fake provider in isolation | Algorithms inside the fake match their interface contracts; determinism; test-API correctness | ms |
| **2. System** | One Muscle/Brain system at a time (e.g. `OffMeshLinkDetectionSystem` only) | Each system's per-tick logic against synthetic ECS state | ms–tens of ms |
| **3. Integration** | The twelve scenarios in §6 — full pipeline | The mechanisms compose correctly across Brain ↔ Muscle ↔ Solver | hundreds of ms each |

Each layer feeds the next: unit tests prove the fake; system tests prove a system against fake state; integration tests prove the assembled pipeline against fake providers.

---

## 2. Test infrastructure conventions

### 2.1 Test projects

Following existing engine convention:

- **`Hrot.Navigation.Fake.Tests`** — layer 1 unit tests for `FakeNavmeshProvider`, `FakeDtCrowdProvider`, `FakeVolumetricPathProvider`, `MusclePathRegistry` / `BrainPathRegistry`. Shares assembly with `Hrot.Navigation.Fake` via `[InternalsVisibleTo]`.
- **`Hrot.Navigation.Tests`** — layer 2 system tests for `NavigationIntentBridgeSystem`, `OffMeshLinkDetectionSystem`, `CrowdAgentUpdateSystem`, `NavigationProgressTrackerSystem`, `NavigationPathDetailsUpdateSystem`, the Brain-side `MoveToExecutor`. One system per fixture file.
- **`Hrot.Navigation.Integration.Tests`** — layer 3 integration tests; the twelve scenarios from §6. Run in the all-in-one deployment mode (Brain + Muscle + NavigationSolver in one process, zero DDS — every navigation message flows on the local `FdpEventBus`).

### 2.2 NUnit conventions

- NUnit 3.x (engine standard).
- One test fixture per system / per integration scenario.
- `[Test]` methods named `<MechanismUnderTest>_<Conditions>_<Expected>` (e.g. `OffMeshTraversal_AgentApproachingJumpAcross_EmitsStartEvent`).
- `[SetUp]` builds a fresh `NavTestHarness`; `[TearDown]` disposes it.
- Tests are deterministic; flaky-test policy = "any test that fails intermittently is muted and fixed before merge."

### 2.3 The `NavTestHarness` — central convenience

```csharp
public sealed class NavTestHarness : IDisposable
{
    public ISimulationView View { get; }
    public EntityRepository Repo { get; }
    public IFdpEventBus    BrainBus { get; }
    public IFdpEventBus    MuscleBus { get; }       // physically the same bus
                                                    // as BrainBus in all-in-one mode
    public IModuleHostKernel Kernel { get; }
    public CapturedEventLog EventLog { get; }       // see §7.3

    // Fakes — accessible directly for test-API calls
    public FakeNavmeshProvider          Navmesh    { get; }
    public FakeDtCrowdProvider          Crowd      { get; }
    public FakeVolumetricPathProvider   Volumetric { get; }
    public IPathRegistry                MusclePaths { get; }   // MusclePathRegistry
    public IPathRegistry                BrainPaths  { get; }   // BrainPathRegistry
                                                    // (== MusclePaths in all-in-one,
                                                    //  see DD-Fake-Nav §6.4)

    public IFakeNavmeshProviderTestApi      NavmeshApi    => (IFakeNavmeshProviderTestApi)Navmesh;
    public IFakeDtCrowdProviderTestApi      CrowdApi      => (IFakeDtCrowdProviderTestApi)Crowd;
    public IFakeMusclePathRegistryTestApi   MusclePathApi => (IFakeMusclePathRegistryTestApi)MusclePaths;
    public IFakeBrainPathRegistryTestApi    BrainPathApi  => (IFakeBrainPathRegistryTestApi)BrainPaths;

    // Construction
    public static NavTestHarness LoadMap(string mapName)         { /* ... */ }
    public static NavTestHarness LoadMap(NavTestMap inlineMap)   { /* ... */ }

    // Tick advancement (see §7 helpers)
    public void Tick(int count = 1);
    public bool PumpUntil(Func<bool> condition, int maxTicks = 600, string failMessage = null);

    // Entity spawning (see §8)
    public Entity SpawnInfantry(Vector2 pos, NavLayerMask layer = NavLayerMask.Infantry);
    public Entity SpawnVehicle(Vector2 pos, VehicleClass cls = VehicleClass.Wheeled);
    public Entity SpawnNaval(Vector2 pos);
    public Entity SpawnFlying(Vector3 pos);

    // BTree action convenience — each writes a NavigationIntent and returns the
    // handle (0 for MoveTo without a Brain-allocated handle).
    public void IssueMoveTo(Entity e, Vector2 destination,
                            MoveToFlags flags = MoveToFlags.None,
                            int routeHandle = 0);
    public int  IssuePlanRoute(Entity e, Vector2 destination,
                               PlanRouteFlags flags = PlanRouteFlags.None);
    public void IssueFollowPath(Entity e, int routeHandle,
                                FollowPathFlags flags = FollowPathFlags.None);
    public void IssueFetchPathDetails(Entity e, int routeHandle, bool blocking = true);
    public void IssueReleasePath(Entity e, int routeHandle);
    public void IssueFlee(Entity e, Entity threat);
}
```

The harness wires up:
1. A single `ModuleHostKernel` with all systems in one process — the **all-in-one** deployment mode (Navigation Design §2). No DDS; the `NavigationIntentEgressTranslator` / `NavigationStatusIngressTranslator` / `PathRequestEgressTranslator` / `PathResponseIngressTranslator` are not registered — all messages flow on the local `FdpEventBus`.
2. An `EntityRepository` with all required component types registered.
3. A `NavigationFakesModule` instance providing all four fakes (the three world-model fakes plus the `IPathRegistry` implementations as a `SharedPathRegistry` per DD-Fake-Nav §6.4).
4. The `NavigationSolverModule` with `PathfindingSolverSystem`.
5. The Muscle-side systems: `NavigationIntentBridgeSystem`, `OffMeshLinkDetectionSystem`, `CrowdAgentUpdateSystem`, `NavigationExecutionSystem`, `NavigationProgressTrackerSystem`, the path-response handler that materializes `NavigationCorridorMuscle`.
6. The Brain-side systems: `LocomotionDispatcherSystem`, `MoveToExecutor` (the thin BTree dispatcher), `NavigationPathDetailsUpdateSystem` (consumes `NavigationPathDetailsResponseEvent` and updates `BrainPathRegistry`).
7. Event-catalog registrations for all Brain-visible navigation events.
8. A `CapturedEventLog` (§7.3) that subscribes to `MoveStartedEvent`, `MoveCompletedEvent`, `PathReplannedEvent`, `OffMeshTraversalStartedEvent`, `OffMeshTraversalEndedEvent`, `MoveBlockedEvent`, and `NavigationPathDetailsArrivedEvent` so tests can assert on what fired.

### 2.4 Time and tick discipline

- `NavTestHarness.Tick(N)` advances by exactly N kernel ticks at the configured fixed timestep (default 1/60 s).
- All `dt`-sensitive logic in the fakes reads `dt` from the kernel — no internal clocks.
- Tests assert on tick counts when timing matters, not wall-clock durations.

---

## 3. Layer 1 — Unit tests for the fakes

One fixture per fake, exercising the fake's interface contract and test API directly without any of the surrounding navigation systems.

### 3.1 `FakeNavmeshProviderTests`

| Test | Asserts |
|---|---|
| `IsWalkable_PointInsidePolygon_ReturnsTrue` | Basic point-in-polygon for a known map |
| `IsWalkable_PointOutsideAllPolygons_ReturnsFalse` | Negative case |
| `IsWalkable_PointInBlockedPolygon_ReturnsFalse` | `BlockPolygon` honored |
| `IsWalkable_LayerMaskExclusion_RespectsMask` | Vehicle point not walkable for Infantry mask |
| `ProjectToNavmesh_PointInPolygon_ReturnsSamePoint` | Identity for already-on-mesh point |
| `ProjectToNavmesh_PointFarFromPolygon_ReturnsNaN` | When `maxDist` exceeded |
| `PathExists_ConnectedPolygons_True` | Two adjacent polygons |
| `PathExists_DisconnectedPolygons_False` | No graph path |
| `PathExists_BlockedIntermediatePolygon_FalseAfterBlock` | Path becomes unreachable after `BlockPolygon` |
| `PathCost_StraightCorridor_EqualsEuclideanDistance` | Single layer, no off-mesh, no obstacles |
| `PathCost_WithOffMeshLink_IncludesLinkCost` | A* uses the link's `cost` field |
| `PlanPath_OffMeshLinkInRoute_EmitsTraversalKindOnWaypoint` | Returned `NavWaypoint.TraversalKind` matches link |
| `BumpVersion_QueryVersionReflectsBump` | `QueryVersion` returns incremented value after `BumpVersion` |
| `SameMap_SameQueries_SameResults` | Determinism across repeated calls |

### 3.2 `FakeDtCrowdProviderTests`

| Test | Asserts |
|---|---|
| `RegisterAgent_NewEntity_ReturnsTrue` | First registration |
| `RegisterAgent_AlreadyRegistered_ReturnsFalse` | Re-register fails |
| `UnregisterAgent_PreviouslyRegistered_Removes` | State component removed |
| `Update_OneAgent_StraightToTarget_Converges` | Agent reaches target within expected ticks |
| `Update_AgentAtTarget_VelocityZero_ReachedFlag` | Idle behavior |
| `Update_TwoAgentsCrossingPaths_Avoid` | Both reach their targets; minimum separation ≥ combined radius × ~0.8 (looser than real ORCA) |
| `Update_AgentSurroundedByThreeStationary_VelocityNearZero` | Deadlock case — agent can't make progress |
| `OverrideAgentVelocity_TestApiBypassesSteering` | Velocity override returns through `GetAgentVelocity` |
| `Determinism_SameInputs_SameOutputs` | Identical sequence of updates → identical positions |
| `Update_LargeAgentCount_Completes` | 200 agents, no exceptions, no NaNs |

### 3.3 `FakeVolumetricPathProviderTests`

| Test | Asserts |
|---|---|
| `Plan_StraightLineNoObstacle_SingleWaypoint` | Direct route returns 1 waypoint at end |
| `Plan_StraightLineThroughNoFlyZone_RoutesAround` | Returned path avoids zone |
| `Plan_StartInsideNoFlyZone_ReturnsNoPath` | Edge case — invalid start |
| `Plan_EndInsideNoFlyZone_ReturnsNoPath` | Edge case — invalid end |
| `Plan_AltitudeExceedsProfileMax_ReturnsNoPath` | `FlyProfile.MaxAltitude` honored |

### 3.4 `MusclePathRegistryTests`

| Test | Asserts |
|---|---|
| `RegisterOrReplace_NewHandle_StoresEntry` | Registry now contains the handle |
| `RegisterOrReplace_ExistingHandle_ReplacesInPlace` | Same handle, new waypoint payload — old data evicted |
| `Free_ExistingHandle_RemovesEntry` | `IsCached` returns false after free |
| `Free_UnknownHandle_ReturnsFalse` | Idempotent on non-existent handles |
| `TryGetWaypoints_PopulatedHandle_ReturnsWaypoints` | Read-back matches what was registered |
| `TryGetWaypoints_UnknownHandle_ReturnsFalse` | Strict miss policy |
| `BrainAllocated_AndMuscleAllocated_NoCollision` | Handle ranges (`< 0x40000000` vs `>= 0x40000000`) don't collide |
| `TryGetWaypointsSlice_ValidRange_ReturnsSlice` | Partial-window read works |
| `TryGetWaypointsSlice_RangeBeyondPath_ReturnsClampedCount` | Final partial slice |

### 3.5 `BrainPathRegistryTests`

| Test | Asserts |
|---|---|
| `TryGetWaypoints_NeverIngested_ReturnsFalse` | Strict cache-miss |
| `TryGetWaypoints_AfterIngest_ReturnsWaypoints` | Happy path |
| `TryGetWaypoints_ReplanCountAdvanced_ReturnsFalseStaleMiss` | Stale-detection works; `Stats.StaleMisses` increments |
| `Ingest_BeyondLruCap_EvictsOldest` | LRU eviction at cap (default 32) |
| `EvictEntry_ExistingHandle_Removes` | Explicit eviction works |
| `EvictEntry_PerEntity_DoesNotAffectOtherEntities` | Eviction is per-entity |
| `Stats_Reset_ZeroesCounters` | Stats inspection / reset |

### 3.6 `SharedPathRegistryTests` (all-in-one mode)

| Test | Asserts |
|---|---|
| `BrainAndMuscleViews_ReturnSameDataForSameHandle` | Two `IPathRegistry` references point at one impl |
| `MuscleWrite_VisibleToBrainRead_SameTick` | No replication delay |
| `BrainStaleness_NeverObserved_InSharedMode` | `ReplanCount` matches by definition in shared mode |

---

## 4. Layer 2 — System tests for individual Muscle / Brain systems

One fixture per system. Each test sets up a tiny ECS world with hand-crafted component values, runs *only that system* for one or a few ticks, and asserts on the resulting state.

### 4.1 `OffMeshLinkDetectionSystemTests`

The zero-frame-suppression mechanism — high-value correctness item.

| Test | Asserts |
|---|---|
| `NoLink_PhaseUnchanged` | No off-mesh in corridor → no `Phase` write |
| `LinkBeyondLookahead_PhaseUnchanged` | Link present but agent far from it → no write |
| `LinkWithinLookahead_PhaseSetToAwaitingTraversal` | Agent near link → `Phase = AwaitingTraversal` |
| `LinkDetected_PlayMontageWritten` | `AnimationChannel.PlayMontage` populated with `TraversalKind` discriminant |
| `LinkDetected_CrowdAgentTagRemovedViaECB` | After ECB flush, `CrowdAgent` is gone |
| `LinkDetected_OffMeshTraversalStartedEventEmitted` | Event with correct `TraversalKind` and `LinkWorldPos` |
| `MultipleAgentsAtSameLink_AllDetectedSameTick` | Two agents at the same jump trigger together |

### 4.2 `CrowdAgentUpdateSystemTests`

The matched-side of the suppression mechanism.

| Test | Asserts |
|---|---|
| `Phase_Following_VelocityWritten` | Normal path — `SimVelocity` gets the crowd output |
| `Phase_AwaitingTraversal_VelocitySuppressed` | Phase set → no `SimVelocity` write |
| `MissingCrowdAgentTag_EntitySkipped` | Filter exclusion works |
| `Phase_TransitionsFromAwaitingToFollowing_VelocityResumes` | Recovery after montage end |

### 4.3 `NavigationIntentBridgeSystemTests`

| Test | Asserts |
|---|---|
| `Humanoid_MoveTo_TagsCrowdAgent` | `CrowdAgent` tag added via ECB |
| `Humanoid_MoveTo_RegistersWithCrowdProvider` | Agent appears in `FakeDtCrowdProvider`'s agent table |
| `Humanoid_MoveTo_PublishesPathfindingRequestLocally` | `PathfindingRequestEvent` appears on Muscle's local bus |
| `Wheeled_MoveTo_NoCrowdTag` | Vehicles stay out of crowd |
| `Wheeled_MoveTo_NavStateModeSet` | `NavState.Mode = DirectPoint` |
| `FollowRoute_AnyMobility_NoCrowdTag` | Scripted routes skip crowd entirely |
| `PlanRoute_NoFollowingStarted_NoCrowdRegistration` | Plan-only intent doesn't start movement |
| `PlanRoute_PublishesPathfindingRequestWithHandle` | `PathfindingRequestEvent.RouteHandle` carries the Brain-allocated value |
| `FollowPath_LooksUpHandleInMusclePool_StartsFollowing` | Existing path is consumed; no new path request |
| `FollowPath_UnknownHandle_WritesFailedInvalidHandleStatus` | Defensive failure on bad handle |
| `FetchPathDetails_FiresResponseEvent` | `NavigationPathDetailsResponseEvent` published with the requested handle's data |
| `FetchPathDetails_UnknownHandle_NoEventFired` | Silent ignore on bad handle (or `FailedInvalidHandle`) |
| `ReleasePath_FreesMusclePoolEntry` | After release, `MusclePathRegistry.IsCached` returns false |
| `ReleasePath_DoesNotStopMovement` | Following continues; only the cache entry is freed |
| `ActionInstanceIdMismatch_TriggersRouting` | Existing channel-dispatch pattern fires |
| `ActionInstanceIdUnchanged_NoOp` | Idempotent on identical intent |

### 4.4 `NavigationProgressTrackerSystemTests`

| Test | Asserts |
|---|---|
| `FirstTickOfMove_EmitsMoveStartedEvent` | `MoveStartedEvent` fires once, with `ActionInstanceId` |
| `WaypointAdvance_NoBrainEvent` | `WaypointReachedEvent` is Muscle-local only |
| `Arrived_EmitsMoveCompletedEventWithArrived` | Correct `Reason` |
| `FailedBlocked_EmitsMoveCompletedEventWithFailedBlocked` | Correct `Reason` |
| `MoveBlocked_ThrottledEmission` | One `MoveBlockedEvent` per blocking episode, not per tick |
| `MuscleInternalReplan_EmitsPathReplannedEvent` | `PathReplannedEvent` fires when Muscle silently replans within budget |
| `MuscleInternalReplan_BumpsReplanCount` | `NavigationStatus.ReplanCount` increments by 1 per replan |
| `AutoSendPathOnReplan_FiresPathDetailsResponse` | When the flag was set, replan additionally fires `NavigationPathDetailsResponseEvent` with `IsAutoRefresh=true` |
| `AutoSendPathOnReplan_NotSet_NoResponseFired` | Without the flag, replan only bumps `ReplanCount` |
| `ReplanBudgetExhausted_WritesFailedBlocked` | After `MaxReplans` or `ReplanTimeBudget`, hard failure surfaces |

### 4.5 Brain-side `MoveToExecutorTests`

The Brain side is intentionally simple. Each test verifies the dispatcher per action.

| Test | Asserts |
|---|---|
| `MoveTo_WritesNavigationIntent_ActiveActionMoveTo` | Intent carries correct action ID |
| `MoveTo_DefaultHandle_IsZero` | Fire-and-forget produces `RouteHandle=0` |
| `MoveTo_ExplicitHandle_PassedThrough` | Brain-allocated handle reaches the intent |
| `MoveTo_StatusArrived_ReturnsBTreeSuccess` | Standard happy path |
| `MoveTo_StatusFailedBlocked_ReturnsBTreeFailure` | Muscle-side replans already exhausted; Brain just sees the verdict |
| `MoveTo_StatusFailedUnreachable_ReturnsBTreeFailure` | No path existed |
| `PlanRoute_WritesNavigationIntent_ActiveActionPlanRoute` | Intent carries the PlanRoute action ID and allocated handle |
| `PlanRoute_StatusPathFound_ReturnsBTreeSuccess` | Path exists, handle is now usable |
| `PlanRoute_StatusNoPath_ReturnsBTreeFailure` | No path |
| `FollowPath_WritesNavigationIntent_WithProvidedHandle` | Handle from blackboard reaches the intent |
| `FetchPathDetails_Blocking_PollsRegistryUntilCached` | Action returns Running until `BrainPathRegistry.IsCached(handle)` true |
| `FetchPathDetails_NonBlocking_ReturnsImmediatelySuccess` | No polling, BTree can chain to other work |
| `ReleasePath_WritesNavigationIntent_ActiveActionReleasePath` | Intent carries action ID and handle |
| `BTreeInstanceIdBump_AbandonsCurrentMove` | Preemption is honored via `ActionInstanceId` bump |

### 4.6 Brain-side `NavigationPathDetailsUpdateSystemTests`

| Test | Asserts |
|---|---|
| `ResponseEventArrives_PopulatesBrainPathRegistry` | After event, `BrainPathRegistry.IsCached(handle)` returns true |
| `ResponseEventArrives_FiresArrivedEvent` | `NavigationPathDetailsArrivedEvent` fires on Brain bus |
| `ResponseEvent_IsAutoRefresh_PreservesFlag` | Brain-side event reflects the auto-refresh discriminator |
| `ResponseEventReceived_LastObservedReplanCountUpdated` | Cache entry's `LastObservedReplanCount` matches current status |
| `LruCapExceeded_OldestEvicted` | Cap honored as new entries arrive |

---

## 5. Layer 3 — Integration tests (in-process, all-in-one mode)

Integration tests run in the **all-in-one deployment mode** described in Navigation Design §2: Brain + Muscle + NavigationSolver in one process, sharing one `ModuleHostKernel`. No DDS — every navigation message (intent, status, path-request, path-response, path-details-response) flows on the local `FdpEventBus`.

### 5.1 Why all-in-one

Two reasons:
1. **Fast** — ~100ms vs. several seconds per scenario.
2. **Determinism** — no UDP packet timing, no clock-sync jitter, no DDS QoS surprises.

This matches the editor and headless build modes — the same code paths that ship to users running the editor are the ones being tested. The scale-out variant (Brain / Muscle / NavigationSolver in three processes with real DDS) is exercised by a separate test suite (§10) that proves the wire format hasn't broken.

### 5.2 Shared scenario template

```csharp
[TestFixture]
public sealed class <ScenarioName>IntegrationTests
{
    private NavTestHarness _h;

    [SetUp]
    public void Setup() => _h = NavTestHarness.LoadMap(NavTestMaps.<MapName>());

    [TearDown]
    public void TearDown() => _h.Dispose();

    [Test]
    public void <ExpectedBehavior>()
    {
        var entity = _h.SpawnInfantry(start);
        _h.IssueMoveTo(entity, destination);
        _h.PumpUntil(() => _h.EventLog.Has<MoveCompletedEvent>(entity), maxTicks: 600);
        Assert.That(_h.EventLog.Get<MoveCompletedEvent>(entity).Reason, Is.EqualTo(...));
        // ... further assertions
    }
}
```

---

## 6. The twelve integration scenarios

Each scenario corresponds to a canonical fixture map (DD-Fake-Nav §7.3). Each test proves one mechanism. The set covers the full surface — if all twelve pass, the navigation mechanism is structurally proven.

### 6.1 `S1_SimpleCorridor`

**Map:** `corridor.json` (single layer, straight 30 m path).
**Setup:** Spawn one Infantry at (0,0); `IssueMoveTo((28,0))`.
**Pump:** until `MoveCompletedEvent` or 600 ticks (10 s).
**Asserts:**
- `MoveStartedEvent` fired once, with `TotalDistance ≈ 28 m`.
- `MoveCompletedEvent.Reason == Arrived`.
- Final `SimTransform` within 0.5 m of (28,0).
- `NavigationStatus.ReplanCount == 0`.
- `NavigationStatus.FrustrationTicks` never exceeded 5 (no avoidance issues).
- No `PathReplannedEvent`, no `OffMeshTraversalStartedEvent`, no `MoveBlockedEvent`.

**Proves:** the full happy-path pipeline — Brain intent, Muscle-side path request on local bus, Solver response, corridor materialization, dtCrowd registration, velocity output, arrival detection, event emission, BTree completion.

### 6.2 `S2_LBendFollow`

**Map:** `l_bend.json` (two 20 m polygons meeting at right angle).
**Setup:** Infantry at one end; `IssueMoveTo` at far end.
**Pump:** until arrival or 1000 ticks.
**Asserts:**
- `MoveCompletedEvent.Reason == Arrived`.
- During the run, `NavigationCorridorMuscle.CurrentSegmentIndex` advances from 0 through `TotalSegmentCount - 1` (verified at midpoint and just before completion).
- Path actually goes around the bend, not through walls — verified by `SimTransform` trace passing within 2 m of the bend's inner corner at some tick.
- Final position within 0.5 m of destination.

**Proves:** corridor-following works across multi-segment paths and the Muscle-side state machine correctly advances `CurrentSegmentIndex` as the agent progresses.

### 6.2b `S2b_LBendWithCorridorPreview`

**Map:** `l_bend.json` (same).
**Setup:** Infantry at one end; `IssueMoveTo` at far end with `MoveToFlags.StreamCorridorPreview` set.
**Pump:** until arrival or 1000 ticks.
**Asserts:**
- Same arrival assertions as S2.
- The entity has a `NavigationCorridorPreview` component throughout the move (assertion: component present at multiple ticks).
- `NavigationCorridorPreview.PreviewVersion` increases ≥ 2 times during the move (the preview's lookahead window advances as the agent progresses).
- `NavigationCorridorPreview.GlobalSegmentStart` final value is > 0 (window has slid forward).
- `NavigationCorridorPreview.WaypointCount` is ≤ 8 at all observation points.
- After completion: the entity does NOT have a `NavigationCorridorPreview` component (or it's been reset).

**Sibling control:** S2 ran without the flag — verify the entity never gained a `NavigationCorridorPreview` component (zero-replication cost when not opted in).

**Proves:** opt-in `NavigationCorridorPreview` works correctly — the window is maintained by Muscle, replicated to Brain only for entities that asked.

### 6.3 `S3_TwoLayersRouting`

**Map:** `two_layers.json`.
**Setup:** One Infantry and one Wheeled vehicle, both spawned at the same start position; both issued the same destination.
**Pump:** until both arrive or 1500 ticks.
**Asserts:**
- Both `MoveCompletedEvent.Reason == Arrived`.
- The Infantry's path takes the narrow Infantry-only passage (assertion: shortest expected path length on Infantry layer).
- The Wheeled vehicle's path takes the longer detour (assertion: total `ProgressS` ≥ K, where K is the known shortest path on Vehicle layer).
- Each entity's planner used the correct layer (assertion: `FakeNavmeshProvider`'s recorded `PathExists` calls had the right `layerMask`).

**Proves:** `NavLayerMask` properly threads from action params through `PathfindingRequestEvent` to the right per-layer fake-navmesh data.

### 6.4 `S4_OffMeshJumpAcross`

**Map:** `off_mesh_jump.json`.
**Setup:** Infantry at left platform; `IssueMoveTo` at right platform across the 4 m gap.
**Pump:** until arrival or 800 ticks.
**Asserts:**
- `OffMeshTraversalStartedEvent` fired exactly once, with `TraversalKind == JumpAcross`.
- `OffMeshTraversalEndedEvent` fired exactly once after start, with `Success == true`.
- During the gap between the two events, the entity's `NavigationStatus.Phase == AwaitingTraversal` (assertion at midpoint tick).
- During the same gap, the entity's `CrowdAgent` tag was removed (assertion: `_h.Repo.HasComponent<CrowdAgent>(entity) == false`).
- During the same gap, `SimVelocity` was either zero or whatever the montage end-position kinematics drove (no dtCrowd output bleeding through). Specific assertion depends on the `FakeAnimationBackend` traversal-montage behavior — coordinated cross-team.
- After `OffMeshTraversalEndedEvent`, `Phase == Following` and `CrowdAgent` tag is back.
- `MoveCompletedEvent.Reason == Arrived`; final position within 0.5 m of destination.

**Proves:** the off-mesh sequence including the zero-frame-latency suppression — between the two events, dtCrowd does not write velocity for this agent.

### 6.5 `S5_ReplanOnNavmeshPatch`

**Map:** `replan.json` (path through middle polygon; alternate route around the side).
**Setup:** Infantry at start; `IssueMoveTo` to far end. Default flags (no `AutoSendPathOnReplan`).
**Pump:** for 100 ticks (agent moves into corridor).
**Action:** `_h.NavmeshApi.BlockPolygon(middlePolygonId, NavLayerMask.Infantry)`.
**Pump:** until `MoveCompletedEvent` or 1500 ticks.
**Asserts:**
- `PathReplannedEvent` fired at least once during the run (Muscle-published).
- `NavigationStatus.ReplanCount > 0` at completion.
- Final `MoveCompletedEvent.Reason == Arrived` (agent re-routed and succeeded).
- Final position within 1 m of destination.
- `NavigationStatus.Result` never transitioned to `FailedBlocked` — Muscle's internal replan budget covered the recovery, Brain never saw a hard failure.

**Proves:** the Muscle-internal replan flow — Muscle detects the path is unfollowable, re-publishes `PathfindingRequestEvent` locally, refreshes the `TrajectoryPoolManager` entry in place under the same `RouteHandle`, bumps `ReplanCount` in status, and Brain sees only the `PathReplannedEvent` notification — never an actionable failure.

### 6.5b `S5b_ReplanWithAutoRefresh`

**Map:** `replan.json` (same).
**Setup:** Infantry at start. BTree:
1. `IssuePlanRoute(destination, PlanRouteFlags.IncludeFullPathDetails | PlanRouteFlags.AutoSendPathOnReplan)` → handle `H`.
2. After `PathFound`: `IssueFollowPath(H, FollowPathFlags.AutoSendPathOnReplan)`.

**Pump:** for 100 ticks (agent moves into corridor, initial path is in `BrainPathRegistry`).
**Action:** `_h.NavmeshApi.BlockPolygon(middlePolygonId, NavLayerMask.Infantry)`.
**Pump:** until `MoveCompletedEvent` or 1500 ticks.
**Asserts:**
- `BrainPathRegistry.TryGetWaypoints(H, ...)` returned the initial path successfully before the blocking action.
- After the blocking action, a `NavigationPathDetailsResponseEvent` with `IsAutoRefresh == true` is captured in the event log.
- `NavigationPathDetailsArrivedEvent` fires on Brain bus with the same handle and `IsAutoRefresh == true`.
- After event arrival, `BrainPathRegistry.TryGetWaypoints(H, ...)` returns the *new* path (different waypoint sequence from the original).
- `BrainPathRegistry.GetStats().StaleMisses == 0` — the cache was refreshed before Brain ever observed a stale entry.
- Final `MoveCompletedEvent.Reason == Arrived`.

**Sibling control:** S5 (above) — without `AutoSendPathOnReplan`, no `NavigationPathDetailsResponseEvent` fires during the run.

**Proves:** the auto-refresh mechanism keeps Brain's cache fresh transparently when the flag is set.

### 6.6 `S6_CrowdAvoidance`

**Map:** `crowded.json` (10 × 10 m open polygon).
**Setup:** Four Infantry entities at corners; each issued a MoveTo at the diagonally-opposite corner. Paths physically cross at the center.
**Pump:** until all four `MoveCompletedEvent.Arrived` or 2000 ticks.
**Asserts:**
- All four `MoveCompletedEvent.Reason == Arrived` (no deadlocks).
- At any tick during the run, no two entities were closer than `0.6 × (radius_a + radius_b)` (looser than real-ORCA tolerance, suitable for fake).
- Average per-entity total path length ≤ 1.7 × straight-line distance (avoidance maneuvers added at most 70%).

**Proves:** `FakeDtCrowdProvider` separation forces produce non-colliding behavior on simple crossing flows.

### 6.7 `S7_FailedUnreachable`

**Map:** `stuck.json` (start polygon and destination polygon in disconnected components).
**Setup:** Infantry at start; `IssueMoveTo` at destination.
**Pump:** until `MoveCompletedEvent` or 200 ticks.
**Asserts:**
- `MoveStartedEvent` *not* fired (corridor never built; the solver returned `IsReachable=false`).
- `MoveCompletedEvent.Reason == Unreachable`.
- Returned within ≤ 200 ticks (Brain doesn't block forever waiting for impossible path).
- `NavigationStatus.Result == FailedUnreachable`.
- BTree (sketched in `_h.IssueMoveTo`) returned `Failure`.

**Proves:** unreachable destinations produce fast, correct failure all the way to BTree.

### 6.8 `S8_FrustrationWatchdog`

**Map:** `frustration.json` (dead-end pocket forcing 3-agent deadlock).
**Setup:** Three Infantry pinning each other in a corner; each issued a MoveTo that requires going through the others.
**Pump:** until any `MoveCompletedEvent` or 400 ticks.
**Asserts:**
- At least one `MoveBlockedEvent` fired (throttled — at most one per agent per blocking episode).
- At least one `MoveCompletedEvent.Reason == FailedBlocked` for at least one of the agents (after `FrustrationTickLimit = 120` ticks of low velocity).
- The failing agent's `NavigationStatus.FrustrationTicks` reached 120 before the event.
- For the failing agent: `NavigationStatus.ReplanCount` may be > 0 (Muscle tried internal replans first) but eventually reached the `MaxReplans` budget and surfaced the failure.
- After the failing agent's BTree returned Failure, the remaining two agents could make progress (loose assertion — design intent is "frustration unblocks others by removing the failing agent's crowd presence").

**Proves:** the universal frustration watchdog works correctly when fed by dtCrowd-output velocities; Muscle exhausts its internal replan budget before surfacing the hard failure to Brain; the `MoveBlockedEvent` throttling is correct.

### 6.9 `S9_FlyingAgentRouting`

**Map:** `flying.json` (open volume with one no-fly box).
**Setup:** Flying agent at start; `IssueMoveTo` at destination across the no-fly zone.
**Pump:** until `MoveCompletedEvent` or 800 ticks.
**Asserts:**
- The `PathfindingRequestEvent` carried `MobilityProfile == Flying`.
- The solver invoked `FakeVolumetricPathProvider.Plan` (assertion via fake's instrumentation).
- The waypoints in `NavigationCorridorMuscle` have non-zero `Position.Z` values (it's a 3D path).
- The corridor avoids the no-fly box (no waypoint inside `NoFlyZone.Bounds`).
- The agent never entered `CrowdAgent` membership (assertion: tag absent throughout).
- `MoveCompletedEvent.Reason == Arrived`.

**Proves:** the `MobilityProfile = Flying` branch routes to the volumetric provider; `NavWaypoint.Position` is genuinely 3D; flying agents bypass dtCrowd.

### 6.10 `S10_NavalLayerRouting`

**Map:** `naval.json` (water polygon with a land obstacle island).
**Setup:** Naval entity at start; `IssueMoveTo` at far side of island.
**Pump:** until `MoveCompletedEvent` or 1500 ticks.
**Asserts:**
- The `PathfindingRequestEvent` carried `NavLayerMask == Naval`.
- The corridor uses only Naval-layer polygons.
- The path goes around the island (not through it).
- `NavState.Mode == Naval` while the entity is moving.
- Naval entity never gets `CrowdAgent` tag.
- `MoveCompletedEvent.Reason == Arrived`.

**Proves:** the Naval layer integrates with the rest of the system; multi-layer navmesh queries correctly select the right per-layer fake data; naval vehicles route through `CarKinematicsSystem`-shaped integration (verified loosely by checking final position; full kinematics correctness is a separate test concern).

### 6.11 `S11_PlanRouteThenFollowPath`

**Map:** `corridor.json` (straight 30 m path).
**Setup:** Infantry at (0,0). BTree:
1. Allocate `H = NavigationHandleAllocator.Allocate(entity)`.
2. `IssuePlanRoute((28,0), routeHandle=H, PlanRouteFlags.IncludeFullPathDetails)`.
3. Pump until `NavigationStatus.Result == PathFound`.
4. Assert `BrainPathRegistry.TryGetWaypoints(H, ...)` succeeds — Brain has the waypoints.
5. Verify the entity has NOT moved (`SimTransform` unchanged) — `PlanRoute` doesn't trigger following.
6. `IssueFollowPath(H)`.

**Pump:** until `MoveCompletedEvent` or 1000 ticks.
**Asserts:**
- After step 3: `NavigationStatus.Result == PathFound`, `NavigationStatus.RouteHandle == H`.
- After step 4: Brain's cache contains H with the full waypoint list.
- After step 5: agent has not moved (verified by tick-after-tick `SimTransform` constancy).
- After step 6: agent starts moving; `MoveStartedEvent` fires.
- `MoveCompletedEvent.Reason == Arrived`; final position within 0.5 m of (28,0).
- No new `PathfindingRequestEvent` fired in step 6 — Muscle looked up the cached path under H.

**Proves:** the Mode-2 plan-then-commit workflow. `PlanRoute` returns a handle Brain can introspect; `FollowPath(H)` starts movement against the cached path without re-planning.

### 6.12 `S12_FetchPathDetailsAndCacheInvalidation`

**Map:** `replan.json`.
**Setup:** Infantry at start. BTree:
1. Allocate `H = NavigationHandleAllocator.Allocate(entity)`.
2. `IssueMoveTo(destination, routeHandle=H)` — fire-and-forget with introspection-enabled.
3. Wait for `NavigationStatus.Result == InProgress` (path has materialized on Muscle).
4. `IssueFetchPathDetails(H, blocking=true)`.
5. After fetch completes, assert cache populated; record `BrainPathRegistry`'s `LastObservedReplanCount` for H.

**Action:** Block a polygon in the path to force a Muscle-internal replan.
**Pump:** for 200 ticks (replan completes, `NavigationStatus.ReplanCount` is now > 0).

**Verify cache invalidation:**
6. `BrainPathRegistry.TryGetWaypoints(H, ...)` — should return false (stale entry, `ReplanCount` mismatch).
7. `BrainPathRegistry.GetStats().StaleMisses` increased by 1.
8. `IssueFetchPathDetails(H)` again to refresh.
9. After refresh, `BrainPathRegistry.TryGetWaypoints(H, ...)` returns the new waypoints; new `LastObservedReplanCount` matches current `ReplanCount`.

**Pump:** until `MoveCompletedEvent`.
**Asserts:**
- All step-by-step assertions above.
- `MoveCompletedEvent.Reason == Arrived`.

**Proves:** the on-demand fetch mechanism, strict cache-miss policy, and `ReplanCount`-based staleness detection all work together. Brain knows when its cache is stale without `AutoSendPathOnReplan`, and an explicit refetch restores freshness.

---

## 7. Helper utilities — `PumpUntil` and friends

### 7.1 `PumpUntil`

```csharp
public bool PumpUntil(Func<bool> condition, int maxTicks = 600, string failMessage = null)
{
    for (int i = 0; i < maxTicks; i++)
    {
        if (condition()) return true;
        Tick(1);
    }
    Assert.Fail(failMessage ?? $"PumpUntil timed out after {maxTicks} ticks");
    return false;
}
```

Convention: caller provides a maxTicks ceiling and an optional explanatory message. The harness asserts on timeout. Used by every integration test.

### 7.2 `PumpFor`

Used when the test wants to advance time without waiting for a condition:

```csharp
public void PumpFor(int ticks);
public void PumpForSeconds(float seconds); // converts to ticks at fixed timestep
```

### 7.3 The `CapturedEventLog`

```csharp
public sealed class CapturedEventLog
{
    public bool Has<T>(Entity target) where T : unmanaged;
    public T    Get<T>(Entity target) where T : unmanaged;  // throws if missing
    public T[]  GetAll<T>(Entity target) where T : unmanaged;
    public int  Count<T>() where T : unmanaged;
    public void Clear();
}
```

Subscribes to every Brain-visible event in the navigation event catalog and records them with their full payloads. Tests assert on:
- Was a given event fired for a given entity?
- How many times?
- What were the field values?

The log is the primary assertion surface for integration tests (alongside ECS-state assertions).

### 7.4 `AssertNoBrainEvent<T>`

```csharp
public void AssertNoBrainEvent<T>(Entity entity, string scenario) where T : unmanaged;
```

Negative-space assertion — proves *absence* of an event. Useful for "the WaypointReachedEvent is Muscle-local only" tests (assert: `EventLog.Count<WaypointReachedEvent>() == 0` after Brain-side observation).

---

## 8. Inline TKB test data

Integration tests construct entity templates in-code rather than loading from disk JSON, to keep test setup self-contained and editor-discoverable.

```csharp
public static class NavTestTemplates
{
    public static EntityTemplate Infantry => new EntityTemplateBuilder()
        .Add(new VehicleParametersDto {
            Length = 0.6f, Width = 0.6f,    // → CrowdAgent radius 0.3
            MaxSpeedFwd = 3.5f,
            MaxAccel = 8.0f, Mass = 80
        })
        .Add(new NavAgentProfileDto {
            PreferredLayerMask = NavLayerMask.Infantry,
            AgentRadius = 0.3f, AgentHeight = 1.8f,
            MaxSlope = 60, MaxStepHeight = 0.4f
        })
        .Add(new CharacterAnimationDefDto {
            // (minimal — just the off-mesh montage mapping for tests)
            Montages = new[] {
                ("traversal_jump_across", "anim_jump"),
                ("traversal_climb", "anim_climb"),
                ("traversal_door", "anim_door_open"),
            }
        })
        .Build();

    public static EntityTemplate Wheeled => new EntityTemplateBuilder()
        .Add(new VehicleParametersDto {
            Length = 4.5f, Width = 1.8f,
            MaxSpeedFwd = 25.0f, MaxSpeedRev = 5.0f,
            MaxAccel = 3.0f, Mass = 1500
        })
        .Add(new NavAgentProfileDto {
            PreferredLayerMask = NavLayerMask.Vehicle,
            AgentRadius = 0.9f, AgentHeight = 1.5f,
            MaxSlope = 20, MaxStepHeight = 0.1f
        })
        .Build();

    public static EntityTemplate Naval => ...;
    public static EntityTemplate Flying => ...;
}
```

The `SpawnInfantry()`/`SpawnVehicle()`/etc. helpers on `NavTestHarness` apply these templates internally — most tests never need to reference them directly.

---

## 9. Failure-mode policy for AI agents

The integration tests must define what AI behaviors do when navigation fails, because the assertions depend on it.

**Default test BTree** is a single `Action_MoveTo` node — when MoveTo returns `Failure`, the BTree returns `Failure` and the agent stops. Tests assert on this exact behavior unless they explicitly override.

**Custom test BTrees** can be supplied for replan/recovery scenarios:

```csharp
_h.SetBehavior(entity, new BTreeSelector(
    new Action_MoveTo(primaryDestination),     // first attempt
    new Action_MoveTo(fallbackDestination)      // on failure, try alternate
));
```

For scenarios S5 (replan) and S8 (frustration), the default single-attempt BTree is sufficient — the navigation system's own internal replan logic (`ReplanCount` mechanism) handles the soft failures before they reach the BTree. The BTree only sees the hard-fail.

---

## 10. Scale-out variant (deferred)

A second-stage test suite exercises the scale-out topology (Brain / Muscle / NavigationSolver each in their own process, with real DDS between them) to prove the wire format hasn't broken. This is a separate test project, mostly re-running a subset of the twelve scenarios with a different harness setup. Scope sketch:

- One process per node role.
- Real CycloneDDS in loopback (single host, no UDP across hosts).
- A subset of the twelve scenarios re-run with real DDS — primarily S1 (simple), S4 (off-mesh), S5 (replan), S5b (auto-refresh), S6 (crowd), and S12 (`FetchPathDetails`) — to prove the DDS topics and translators round-trip correctly.
- Same fakes; only the transport changes.

This document does not specify the scale-out tests in detail; they live in their own follow-up doc once the all-in-one suite is green.

---

*End DD-Tests-Nav.*
