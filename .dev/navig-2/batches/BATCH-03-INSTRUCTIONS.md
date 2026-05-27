# BATCH-03: Phase 1 — Muscle Solver Path-Query Pipeline

**Batch Number:** BATCH-03
**Tasks:** NAV-P1-T1, NAV-P1-T2, NAV-P1-T3, NAV-P1-T4
**Phase:** Phase 1 — Muscle Solver Path-Query Pipeline
**Estimated Effort:** 16-20 hours
**Priority:** HIGH — core pipeline required by Phase 2+ fakes and integration tests
**Dependencies:** BATCH-01, BATCH-02 (Phase 0 complete)

---

## Onboarding & Workflow

### Developer Instructions

This batch implements the full path-query pipeline that connects the Muscle side's
intent dispatch to the solver and materializes the resulting corridor. Phase 0
established all the data contracts (events, components, action params, handles).
Phase 1 wires them together into a working pipeline.

Work tasks in order: T1 (event extension) → T2 (bridge publish) → T3 (backend selection)
→ T4 (materialization + status). Each task builds on the previous one; do not skip ahead.
After each task: **build the full solution, run the nav tests, fix all failures before
continuing to the next task.**

**Do NOT stop to ask questions unless there is a breaking design flaw.** Fix all
failures yourself and keep going until all tests pass. Write the report only once all
tasks are done and the test suite is green.

### Required Reading (in order)

1. **Previous review:** `.dev/navig-2/reviews/BATCH-02-REVIEW.md`
2. **Workflow guide:** `.dev/.guides/DEV-GUIDE.md`
3. **Code standards:** `.dev/.guides/CODE-STANDARDS.md`
4. **Task definitions:** `.dev/navig-2/TASK-DETAILS.md` — sections NAV-P1-T1 through NAV-P1-T4
5. **Design §2 (topology):** `.dev/navig-2/Navigation_Design_v2_0.md` — understand all-in-one vs
   default modes; in all-in-one / default modes the Muscle-Solver hop is local bus only (no DDS)
6. **Design §3.1, §3.2 (end-to-end pipeline):** same file — `MoveTo` and `PlanRoute` flow diagrams
7. **Design §4.3 (NavigationCorridorMuscle):** same file — structure written by T4
8. **Design §5.1, §5.2 (PathfindingRequestEvent / PathfindingResultEvent):** same file — fields
   added in T1; backend-selection pseudocode in §5.2 used in T3
9. **Design §6.2 (IPathRegistry, handle allocation):** same file — handle semantics for T3/T4
10. **Design §7.1 (NavigationIntentBridgeSystem routing):** same file — action routing in T2
11. **Design §15 (budget bands):** same file — batch capacity 256, `[Critical 50% / Normal 35% / Low 15%]`

### Source Code Locations

- **PathfindingEvents.cs:** `FDP/Toolkits/Fdp.Toolkits/Navigation/PathfindingEvents.cs`
- **PathfindingBatchData.cs:** `FDP/Toolkits/Fdp.Toolkits/Navigation/PathfindingBatchData.cs`
- **NavigationIntentBridgeSystem.cs:** `FDP/Toolkits/Fdp.Toolkits/Navigation/Systems/NavigationIntentBridgeSystem.cs`
- **PathfindingSolverSystem.cs:** `FDP/Toolkits/Fdp.Toolkits/Navigation/Systems/PathfindingSolverSystem.cs`
- **PathfindingResultMaterializationSystem.cs:** `FDP/Toolkits/Fdp.Toolkits/Navigation/Systems/PathfindingResultMaterializationSystem.cs`
- **NavigationSolverModule.cs:** `FDP/Toolkits/Fdp.Toolkits/Navigation/Modules/NavigationSolverModule.cs`
- **NavigationComponents.cs:** `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationComponents.cs` (NavigationCorridorMuscle lives here)
- **NavigationActions.cs:** `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationActions.cs`
- **NavigationConstants.cs:** `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationConstants.cs`
- **Hrot.Network.NED DDS translators:** `Hrot/Network/Hrot.Network.NED/` (PathRequestEgressTranslator, PathResponseIngressTranslator)
- **Test project:** `FDP/Toolkits/Fdp.Toolkits.Tests/`
- **Solver tests:** `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/PathfindingSolverSystemTests.cs`
- **Translator tests:** `Hrot/Engine/Hrot.Map.Common.Tests/`

### Build & Test Commands

```powershell
# Build full solution
dotnet build IOS-IG-SimHost.sln

# Run navigation tests only
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj --filter "Navigation" -v quiet

# Run translator tests
dotnet test Hrot/Engine/Hrot.Map.Common.Tests/Hrot.Map.Common.Tests.csproj -v quiet

# Run all tests (final gate)
dotnet test IOS-IG-SimHost.sln -v quiet
```

### Report Submission

**When done, submit:** `.dev/navig-2/reports/BATCH-03-REPORT.md`

**If you have questions:** `.dev/navig-2/questions/BATCH-03-QUESTIONS.md`

---

## Context

Phase 0 delivered: action IDs 6-9, 32-byte param structs, `NavigationIntent.RouteHandle`,
`NavigationStatus` extension (Phase/LastFailureReason/ReplanCount/RouteHandle/ETR),
`NavigationPhase` enum, `NavigationResult` values 4-7, `NavWaypoint` 24B, `TraversalKind`/
`SurfaceType`, `NavAgentProfile`(69)/`NavigationCorridorMuscle`(70)/`NavigationCorridorPreview`(71)/
`NavigationPathDetailsBuffer`(72)/`CrowdAgent`(73).

Phase 1 wires those contracts into a live pipeline: extend the path-request/result events
(T1), teach `NavigationIntentBridgeSystem` to publish requests from the new action intents (T2),
add backend-selection logic to `PathfindingSolverSystem` (T3), and extend
`PathfindingResultMaterializationSystem` to write `NavigationCorridorMuscle` and proper
`NavigationStatus` (T4).

**Key constraint:** All three topology modes (default, scale-out, all-in-one) share identical
API contracts. The Muscle↔Solver hop is always on the **local** `FdpEventBus` in default and
all-in-one modes. Only in scale-out mode do translators bridge across DDS. Do not add DDS
translator registrations into the default/all-in-one module paths.

---

## Tasks

### T1 — Extend `PathfindingRequestEvent` and `PathfindingResultEvent`
*Full spec:* `.dev/navig-2/TASK-DETAILS.md#nav-p1-t1`

Extend `PathfindingEvents.cs` with new fields per Design §4.4 / §5.1:

**`PathfindingRequestEvent` additions:**
- `int RouteHandle` — Brain-allocated handle (0 = anonymous MoveTo)
- `int NavLayerMask` — layer filter bitmask (§5.1)
- `NavigationBackend BackendForce` — explicit backend override (`Auto = 0`)
- `float MaxCost` — budget limit (0 = unlimited)
- `int NavmeshVersionAtRequest` — navmesh version at time of request (§4.4)

Extend `MobilityProfile` doc comment / enum to include `Naval = 3` and `Flying = 4` (values
should not break existing `Wheeled=0, Tracked=1, Infantry=2` assignments).

**`PathfindingResultEvent` additions:**
- `int RouteHandle` — echoed from request
- `int NavmeshVersionAtPlan` — navmesh version used when path was computed
- `NavigationFailureReason FailureReason` — why path failed (`None = 0` when succeeded)
- `NavigationBackend PrimaryBackend` — which backend actually computed this path

Define `NavigationBackend` enum (`Auto=0, RoadGraph=1, Navmesh=2, Hybrid=3, Volumetric=4`) and
`NavigationFailureReason` enum (`None=0, Unreachable=1, Timeout=2, InvalidHandle=3, ProviderError=4`)
in `NavigationComponents.cs` or a nearby enums file (keep all nav enums together).

**DDS translator forward-compat (scale-out only):**
Update `PathRequestEgressTranslator` and `PathResponseIngressTranslator` in
`Hrot/Network/Hrot.Network.NED/` to include the new fields. These translators are
NOT registered in default/all-in-one mode — update them only for structural forward
compatibility, not for test coverage in this batch.

**Tests:**
- New fields present and zero-defaulted on `new PathfindingRequestEvent()`.
- Handle echo: a round-trip test (set `RouteHandle = 42` on request, verify result echoes it).
- Existing `PathfindingSolverSystemTests.RunSolverPipeline` remains green (may need updating
  to assert `RouteHandle` echo if the solver is updated in T3 to propagate it).

---

### T2 — `NavigationIntentBridgeSystem` — publish requests & route by action
*Full spec:* `.dev/navig-2/TASK-DETAILS.md#nav-p1-t2`

Extend `NavigationIntentBridgeSystem.cs` to:

1. **Detect new intent** via `ActionInstanceId` / `IntentId` change (existing mechanism).
2. **Route by `ActiveAction`:**
   - `MoveTo` → construct `PathfindingRequestEvent` from `MoveToParams` (Destination, speed,
     RouteHandle=0 unless explicitly set, MobilityProfile from entity) and publish on the local
     Muscle `FdpEventBus`.
   - `PlanRoute` → construct `PathfindingRequestEvent` from `PlanRouteParams`; carry the
     Brain-allocated `RouteHandle` through.
   - `FollowPath` → look up `RouteHandle` in `TrajectoryPoolManager`; if found, start following
     (skip the solver); if not found, write `NavigationStatus.Result = FailedInvalidHandle`.
   - `FetchPathDetails` → handled directly (no solver); fire `NavigationPathDetailsResponseEvent`
     from current corridor state. Stub OK for now — Phase 4 (NAV-P4-T3) owns the full Brain
     ingress side.
   - `ReleasePath` → remove the handle from `TrajectoryPoolManager`; clear corridor.

3. **Set `NavState.Mode`** per `MobilityProfile` routing (§7.1):
   use corrected `KinematicsMode` values from NAV-P0-T2.

4. **Idempotency:** unchanged `ActionInstanceId` must not re-publish.

**Do not add the crowd-tag branch here** — that is NAV-P3-T1. Establish the request/route
plumbing only.

**Tests:** The full system-test suite (NAV-P9-T3 `NavigationIntentBridgeSystemTests`) is
deferred to Phase 9. However, write at least the following unit tests in `Fdp.Toolkits.Tests`
now:
- `MoveTo_PublishesExactlyOnePathRequest` — new MoveTo intent → exactly one
  `PathfindingRequestEvent` on the local bus with matching destination.
- `PlanRoute_PublishesRequestWithBrainHandle` — PlanRoute with non-zero `RouteHandle` →
  request carries that handle.
- `FollowPath_UnknownHandle_SetsFailedInvalidHandle` — `FollowPath` with handle not in the
  pool → `NavigationStatus.Result == FailedInvalidHandle`.
- `IdempotencyOnUnchangedActionInstanceId` — firing the same intent twice → only one request.

---

### T3 — Multi-modal backend selection in `PathfindingSolverSystem`
*Full spec:* `.dev/navig-2/TASK-DETAILS.md#nav-p1-t3`

Extend `PathfindingSolverSystem.cs` (runs in `NavigationSolverModule` at `SlowBackground(10Hz)`):

1. **Keep road-graph Dijkstra as the `RoadGraph` backend** — no changes to the existing O(N²)
   Dijkstra; it must remain the behavior when `BackendForce = RoadGraph` or when `Auto`
   selects it.

2. **Add backend selection** by `MobilityProfile` + `BackendForce` using the §5.2 pseudocode:
   ```
   if BackendForce != Auto: use BackendForce directly
   if MobilityProfile == Flying: use IVolumetricPathProvider
   else Auto heuristic:
     road-radius test → if near road → RoadGraph
     else if navmesh available → Navmesh (INavmeshProvider.PlanPath)
     else → Hybrid (splice)
   ```
   At this point `INavmeshProvider` and `IVolumetricPathProvider` are available via DI/module
   registration from Phase 0. In the absence of fake providers (not yet implemented in Phase 2),
   fall back gracefully: if the selected provider is null/unregistered → use RoadGraph and set
   `PrimaryBackend = RoadGraph` in the result.

3. **Register waypoints in `TrajectoryPoolManager`** keyed by `RouteHandle`:
   - If `RouteHandle == 0` (anonymous MoveTo): allocate a Muscle-internal handle `>= 0x40000000`
     (§6.3 `NavigationHandleAllocator` — implement as a simple `Interlocked.Increment` counter
     starting at `0x40000000` if it does not already exist).
   - If `RouteHandle != 0` (Brain-allocated): use directly.

4. **Emit `PrimaryBackend`** in `PathfindingResultEvent`.

5. **Apply §15 budget bands** `[Critical 50% / Normal 35% / Low 15%]` with snapshot-on-demand
   (mirror the EQS §6 pattern). Use `EventAccumulator` for missed-frame events if the pattern
   exists in the codebase; otherwise a simple ring-buffer with oldest-evict is acceptable.

**Tests:**
- Existing `PathfindingSolverSystemTests.RunSolverPipeline` must stay green (regression).
- `BackendForce_RoadGraph_UsesRoadGraph` — explicitly forced RoadGraph → result
  `PrimaryBackend == RoadGraph`, path identical to pre-existing behavior.
- `MobilityProfile_Flying_InvokesVolumetricProvider` — if a fake `IVolumetricPathProvider` is
  injected, `Flying` requests call its `PlanPath`; `INavmeshProvider` is NOT called.
- `HandleEcho_AnonymousMoveTo_AssignsInternalHandle` — `RouteHandle == 0` request →
  result handle `>= 0x40000000`.
- `HandleEcho_BrainHandle_IsPreserved` — `RouteHandle == 99` → result handle `== 99`.

---

### T4 — Response materialization → `NavigationCorridorMuscle` + status; resize batch to 256
*Full spec:* `.dev/navig-2/TASK-DETAILS.md#nav-p1-t4`

Extend `PathfindingResultMaterializationSystem.cs`:

1. **Look up originating entity** from `PathfindingResultEvent.RequestId` or `SourceNodeId`
   (whichever the existing system uses).

2. **Write `NavigationCorridorMuscle`** on the entity:
   - `RouteHandle`, `NavmeshVersion`, segment counts, estimated distance, `PrimaryBackend`, flags.
   - Per Design §4.3.

3. **Branch by action:**
   - `MoveTo` → start following + write `NavigationStatus { Phase = Following, Result = InProgress }`
     + fire `MoveStartedEvent` (once).
   - `PlanRoute` → write `NavigationStatus { Phase = Idle, Result = PathFound, RouteHandle = handle }`
     — do NOT start following. Fire `NavigationPathDetailsResponseEvent` if
     `PlanRouteParams.Flags.IncludeFullPathDetails` is set (stub the event struct if it does not
     exist yet — Phase 4-T3 owns the full ingress side).
   - Unreachable (solver says no path):
     - `MoveTo` → `NavigationStatus { Result = FailedUnreachable }`; do NOT fire `MoveStartedEvent`.
     - `PlanRoute` → `NavigationStatus { Result = NoPath }`.

4. **Resize `PathfindingBatchData` capacity 64 → 256** (§15):
   update the constant in `PathfindingBatchData.cs`.

**Tests:**
- `MoveTo_Reachable_PopulatesCorridorAndFiresMoveStartedEvent` — after a reachable MoveTo:
  `NavigationCorridorMuscle.RouteHandle != 0`, `MoveStartedEvent` fired exactly once,
  `NavigationStatus.Phase == Following`.
- `MoveTo_Unreachable_SetsFailedUnreachable_NoMoveStartedEvent` — no `MoveStartedEvent`,
  `Result == FailedUnreachable`.
- `PlanRoute_Reachable_SetsPathFound_NoCorridor_NoMoveStartedEvent` — `Result == PathFound`,
  `Phase == Idle`, no `MoveStartedEvent`.
- `PlanRoute_Unreachable_SetsNoPath` — `Result == NoPath`.
- `BatchCapacity_Is256` — layout / constant assertion.

---

## Mandatory Workflow: Test-Driven Task Progression

For each task in this batch, follow this exact sequence:

1. **Write failing tests first** (at minimum the "Success condition" tests listed above).
2. **Implement** the feature.
3. **Run tests** — all must pass before moving to the next task.
4. **Build full solution** — 0 errors.
5. **Only then proceed** to the next task.

Do not batch all implementation first and test at the end.

---

## Developer Insights Required in Report

Your report MUST answer:

1. **What issues were encountered?** (build errors, design ambiguities, missing types, etc.)
2. **What weak points were spotted in the codebase?** (fragile patterns, missing abstractions, etc.)
3. **What design decisions were made beyond the spec?** (anything not explicitly specified)
4. **Test coverage gaps:** Are there scenarios you couldn't test because a dependency isn't
   implemented yet? List them explicitly.
5. **Handle allocation:** How did you implement `NavigationHandleAllocator`? Thread-safe?

---

## Success Criteria

- [ ] `PathfindingRequestEvent` has all 5 new fields; `PathfindingResultEvent` has all 4 new fields.
- [ ] `NavigationBackend` and `NavigationFailureReason` enums defined.
- [ ] `NavigationIntentBridgeSystem` routes MoveTo/PlanRoute to solver, FollowPath/FetchPathDetails/ReleasePath directly.
- [ ] `PathfindingSolverSystem` selects backend by `MobilityProfile + BackendForce`; handles `RouteHandle` allocation/echo.
- [ ] `PathfindingResultMaterializationSystem` writes `NavigationCorridorMuscle`, branches by action, fires `MoveStartedEvent` correctly.
- [ ] `PathfindingBatchData` capacity is 256.
- [ ] All listed tests pass; all pre-existing tests remain green.
- [ ] `dotnet build IOS-IG-SimHost.sln` → 0 errors.
