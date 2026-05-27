# BATCH-02: Phase 0 — Action Layer, New Components & ComponentIds

**Batch Number:** BATCH-02
**Tasks:** NAV-P0-T4, NAV-P0-T5 (plus P1 corrective from BATCH-01 review)
**Phase:** Phase 0 — Foundations, contracts & corrective migration
**Estimated Effort:** 12-16 hours
**Priority:** HIGH (blockers for Phase 1+)
**Dependencies:** BATCH-01 (merged)

---

## Onboarding & Workflow

### Developer Instructions

This batch completes Phase 0. It introduces the new action-based command layer
(RouteHandle, param structs, extended NavigationIntent/Status), the full `NavWaypoint`
definition replacing the BATCH-01 stub, and all supporting components + ComponentIds.

Start with the P1 corrective fix (5 minutes). Do not skip it. Then implement T4, then T5.
After each task: build the full solution, run the navigation test suite, fix all failures
before continuing.

**Do NOT stop to ask if it is OK to proceed.** Fix all failures yourself and keep going
until the full batch is done. Write the report only when all tests pass.

### Required Reading (in order)

1. **BATCH-01 review:** `.dev/navig-2/reviews/BATCH-01-REVIEW.md` — P1 corrective to fix first
2. **Workflow guide:** `.dev/.guides/DEV-GUIDE.md`
3. **Code standards:** `.dev/.guides/CODE-STANDARDS.md`
4. **Task definitions:** `.dev/navig-2/TASK-DETAILS.md` — NAV-P0-T4, NAV-P0-T5
5. **Design §4:** `.dev/navig-2/Navigation_Design_v2_0.md` — §4.1 (Brain-side state), §4.2 (CorridorPreview), §4.3 (CorridorMuscle), §4.4 (PathfindingRequestEvent fields), §4.5 (NavWaypoint), §4.6 (TraversalKind/SurfaceType)
6. **Design §6, §8, §13, §14:** Same file — §6.2 (IPathRegistry + handles), §8.3 (NavAgentProfile), §13.1 (param struct layout), §13.6 (action catalog), §14 (forward-compat hook)
7. **DD-Fake-Nav §12:** `.dev/navig-2/DD-Fake-Nav.md` — ComponentId allocation block

### Source Code Locations

- **Navigation components & enums:** `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationComponents.cs`
- **Navigation actions/params:** `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationActions.cs`
- **Navigation constants:** `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationConstants.cs`
- **NavWaypoint stub:** `FDP/Toolkits/Fdp.Toolkits/Navigation/NavWaypoint.cs` (to replace)
- **ComponentId catalog:** `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationContractsComponentIds.cs`
- **Global ComponentId catalog:** `FDP/Engine/Fdp.Core/GlobalComponentIds.cs`
- **DDS descriptors:** `Hrot/Network/Hrot.Network.NED/SimDescriptors.cs`
- **NavigationIntent egress translator:** `Hrot/Network/Hrot.Network.NED/Replication/Map/Egress/NavigationIntentEgressTranslator.cs`
- **NavigationStatus translator:** (search for `NavigationStatus` in `Hrot/Network/Hrot.Network.NED/`)
- **Navigation test world factory:** `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/NavigationTestWorldFactory.cs`
- **Test project:** `FDP/Toolkits/Fdp.Toolkits.Tests/`
- **Translator tests:** `Hrot/Engine/Hrot.Map.Common.Tests/Replication/Egress/NavigationIntentEgressTranslatorTests.cs`

### Build & Test Commands

```powershell
# Build the full solution
dotnet build IOS-IG-SimHost.sln

# Run navigation-related tests only
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj --filter "Navigation" -v quiet

# Run translator tests
dotnet test Hrot/Engine/Hrot.Map.Common.Tests/Hrot.Map.Common.Tests.csproj --filter "NavigationIntent" -v quiet
```

### Report Submission

**When done, submit:** `.dev/navig-2/reports/BATCH-02-REPORT.md`

---

## Context

BATCH-01 established the code placement policy, fixed the KinematicsMode collision (DSC-2),
and redefined `INavmeshProvider` (DSC-1). BATCH-02 resolves DSC-3: the design's action-based
navigation command layout does not match the current `NavigationIntent` (which is Mode-based).

The current `NavigationIntent` carries `Mode`, `FinalDestination`, `TargetSpeed` etc. The
new design adds a `RouteHandle` field and new `ActionId`-based param structs. Crucially, the
existing `MoveTo` flow must keep working — this is an extension, not a replacement.

**Related tasks:**
- [NAV-P0-T4](../TASK-DETAILS.md#nav-p0-t4) — Action command layer
- [NAV-P0-T5](../TASK-DETAILS.md#nav-p0-t5) — NavWaypoint, enums, components, ComponentIds

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: Complete tasks in sequence with passing tests:**

1. **Corrective T0:** Fix test → ALL tests pass ✅
2. **Task 4:** Implement → Write tests → ALL tests pass ✅
3. **Task 5:** Implement → Write tests → ALL tests pass ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ All tests passing (including previous tasks' tests)

---

## Tasks

---

### Corrective Task 0: Fix `NoneIntent_IsSkipped_NavStateUnchanged` (P1 from BATCH-01)

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/NavigationIntentBridgeSystemTests.cs`

The test has an incorrect assumption. `NavigationIntentBridgeSystem` **correctly** sets
`NavState.Mode = KinematicsMode.None` and `TargetSpeed = 0` when `NavigationMode.None` is
encountered — this is "halt navigation". The test must reflect this correct behavior.

**Fix:**
1. Rename the test from `NoneIntent_IsSkipped_NavStateUnchanged` to
   `NoneIntent_HaltsNavigation_NavStateSetToNone`.
2. Update the comment to: "None intent halts navigation — NavState.Mode = KinematicsMode.None, TargetSpeed = 0".
3. Change the assertions:
   ```csharp
   Assert.Equal(KinematicsMode.None, nav.Mode);    // was KinematicsMode.Direct
   Assert.Equal(0f, nav.TargetSpeed);               // was 99f
   ```

Verify: `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/ --filter "Navigation" -v quiet` → 0 failures.

---

### Task 4: Action-Based Command Layer (NAV-P0-T4)

**Task Definition:** [TASK-DETAILS.md#nav-p0-t4](./../TASK-DETAILS.md#nav-p0-t4)

**Design refs:** Navigation_Design_v2_0.md §4.1, §4.2, §13.1, §13.6; DSC-3.

#### 4a. New ActionIds in NavigationConstants

**File:** `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationConstants.cs` (UPDATE)

Add to the action ID constants (after ActionIdJoinFormation = 5):

```csharp
/// <summary>Plan a route and store the result in the path registry (handle returned to Brain).</summary>
public const ushort ActionIdPlanRoute         = 6;

/// <summary>Follow a previously-planned route identified by a RouteHandle.</summary>
public const ushort ActionIdFollowPath        = 7;

/// <summary>Fetch the full path details for a RouteHandle into BrainPathRegistry.</summary>
public const ushort ActionIdFetchPathDetails  = 8;

/// <summary>Release (remove) a RouteHandle from the path registry.</summary>
public const ushort ActionIdReleasePath       = 9;
```

Also remove `ActionIdFollowRoadGraph = 4` — this action is subsumed by `MoveTo` with
`BackendForce=RoadGraph` per §17. Remove or deprecate with `[Obsolete]`. If you add
`[Obsolete]`, also add a comment `// Subsumed by MoveTo+BackendForce=RoadGraph — see NAV-P4-T2`.

#### 4b. New 32-Byte Param Structs

**File:** `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationActions.cs` (UPDATE)

Add the following four param structs after the existing `FleeParams`. Each must be exactly
32 bytes via `[StructLayout(LayoutKind.Sequential)]` — pad explicitly if needed.
Assert sizes in tests (see §4d below).

Refer to Navigation_Design_v2_0.md §13.1 for the canonical field layout. The exact structs:

```csharp
/// <summary>Parameters for the ActionIdPlanRoute action. 32 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct PlanRouteParams
{
    /// <summary>Target position (Cartesian XY metres).</summary>
    public Vector2 Destination;     // 8 bytes

    /// <summary>Distance from destination counted as arrival (metres).</summary>
    public float ArrivalRadius;     // 4 bytes

    /// <summary>Desired travel speed (m/s).</summary>
    public float Speed;             // 4 bytes

    /// <summary>Navmesh layer mask. 0xFFFFFFFF = all layers.</summary>
    public uint LayerMask;          // 4 bytes

    /// <summary>Force a specific backend (0 = Auto).</summary>
    public byte BackendForce;       // 1 byte

    /// <summary>When 1, include full path details in the response (for caching).</summary>
    public byte IncludeFullPathDetails; // 1 byte

    /// <summary>Padding to reach 32 bytes.</summary>
    private byte _pad0;
    private byte _pad1;             // total = 8+4+4+4+1+1+2 = 24 → need 8 more bytes

    /// <summary>Maximum allowed path cost (metres). 0 = unlimited.</summary>
    public float MaxCost;           // 4 bytes (now at 28)

    private uint _pad2;             // 4 bytes (now at 32)
}

/// <summary>Parameters for the ActionIdFollowPath action. 32 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct FollowPathParams
{
    /// <summary>Handle to a previously planned route (from PlanRoute).</summary>
    public int RouteHandle;         // 4 bytes

    /// <summary>When 1, allowed to drive in reverse.</summary>
    public byte ReverseAllowed;     // 1 byte

    private byte _pad0;
    private byte _pad1;
    private byte _pad2;             // 3 bytes pad → 8 bytes total

    /// <summary>Desired travel speed (m/s).</summary>
    public float Speed;             // 4 bytes

    /// <summary>Distance from destination counted as arrival (metres).</summary>
    public float ArrivalRadius;     // 4 bytes

    // remaining 16 bytes reserved
    private ulong _reserved0;       // 8 bytes
    private ulong _reserved1;       // 8 bytes
}

/// <summary>Parameters for the ActionIdFetchPathDetails action. 32 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct FetchPathDetailsParams
{
    /// <summary>Handle to the route to fetch details for.</summary>
    public int RouteHandle;         // 4 bytes

    /// <summary>When 1, the fetch is non-blocking (return Success immediately if cached).</summary>
    public byte NonBlocking;        // 1 byte

    private byte _pad0;
    private byte _pad1;
    private byte _pad2;             // 3 bytes pad → 8 total

    // reserved 24 bytes
    private ulong _reserved0;
    private ulong _reserved1;
    private ulong _reserved2;
}

/// <summary>Parameters for the ActionIdReleasePath action. 32 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct ReleasePathParams
{
    /// <summary>Handle to release. Idempotent — releasing an unknown handle is not an error.</summary>
    public int RouteHandle;         // 4 bytes

    private uint _pad0;             // 4 bytes

    // reserved 24 bytes
    private ulong _reserved0;
    private ulong _reserved1;
    private ulong _reserved2;
}
```

> **⚠️ Size check:** After writing the structs, add layout assertions in tests (§4d) to verify
> each is exactly 32 bytes. Adjust padding if your layout differs.

Also extend `MoveToParams` to add `RouteHandle`, `LayerMask`, `BackendForce`:

```csharp
// Add to the END of MoveToParams (after existing fields, before padding):
/// <summary>
/// Pre-allocated route handle (0 = fire-and-forget; solver allocates its own).
/// When non-zero, the Muscle reuses this handle on replan.
/// </summary>
public int RouteHandle;   // 4 bytes

/// <summary>Navmesh layer mask. 0xFFFFFFFF = all layers.</summary>
public uint LayerMask;    // 4 bytes

/// <summary>Force a specific backend (0 = Auto, 1 = NavMesh, 2 = RoadGraph, 3 = Volumetric).</summary>
public byte BackendForce; // 1 byte
```

Adjust padding bytes so `MoveToParams` stays ≤ 32 bytes (with the new fields it should fit
in 32 bytes — verify with a layout test).

#### 4c. Extend NavigationIntent and NavigationStatus

**File:** `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationComponents.cs` (UPDATE)

**Extend `NavigationIntent`:**
Add after the existing fields (before the closing brace):
```csharp
/// <summary>
/// Pre-allocated route handle. 0 = fire-and-forget (solver assigns its own handle).
/// Non-zero = reuse this handle (Brain-allocated; used by PlanRoute + FollowPath flows).
/// </summary>
public int RouteHandle;
```

**Extend `NavigationStatus` Result enum** — add new members:
```csharp
// New members (keep existing 0-3 values unchanged):
/// <summary>A valid path was found and stored; handle is ready for FollowPath.</summary>
PathFound = 4,
/// <summary>No path exists to the destination.</summary>
NoPath = 5,
/// <summary>The specified NavLayer has no navmesh data for the agent type.</summary>
FailedNoLayer = 6,
/// <summary>The RouteHandle passed to FollowPath/FetchPathDetails does not exist.</summary>
FailedInvalidHandle = 7,
```

**Extend `NavigationStatus` struct** — add new fields:
```csharp
/// <summary>Current execution phase of the navigation command.</summary>
public NavigationPhase Phase;

/// <summary>Reason for the last failure (only valid when Result is a failed state).</summary>
public NavigationResult LastFailureReason;

/// <summary>Number of times the path has been replanned for the current intent.</summary>
public ushort ReplanCount;

/// <summary>The RouteHandle associated with the current navigation command (0 = none).</summary>
public int RouteHandle;

/// <summary>
/// Estimated time remaining to destination (seconds, 0 = unknown).
/// Forward-compat hook (§14) — carried but always 0 in initial implementation.
/// </summary>
public float EstimatedTimeRemaining;

/// <summary>
/// Navmesh version at which the current path was planned (§14 forward-compat hook).
/// Always 0 in initial implementation; invalidated-path logic uses this later.
/// </summary>
public uint NavmeshVersionObserved;
```

Also add the `NavigationPhase` enum (in the same file, alongside `NavigationMode`/`NavigationResult`):
```csharp
/// <summary>Execution phase of an active navigation command.</summary>
public enum NavigationPhase : byte
{
    /// <summary>No active command or waiting for path.</summary>
    Idle = 0,
    /// <summary>Path request sent, awaiting solver response.</summary>
    AwaitingPath = 1,
    /// <summary>Path found, actively following the corridor.</summary>
    Following = 2,
    /// <summary>Traversing an off-mesh link (jump, ladder, door).</summary>
    AwaitingTraversal = 3,
    /// <summary>Command complete (arrived or failed).</summary>
    Completed = 4,
}
```

#### 4d. Update DDS Descriptors

**File:** `Hrot/Network/Hrot.Network.NED/SimDescriptors.cs` (UPDATE)

Add `RouteHandle` to the DDS `NavigationIntent` descriptor:
```csharp
/// <summary>Pre-allocated route handle (0 = fire-and-forget).</summary>
public int RouteHandle;
```

Add new fields to the DDS `NavigationStatus` descriptor:
```csharp
/// <summary>Execution phase.</summary>
public byte Phase;
/// <summary>Number of replans for the current intent.</summary>
public ushort ReplanCount;
/// <summary>Route handle associated with this status.</summary>
public int RouteHandle;
/// <summary>Forward-compat: navmesh version when path was planned.</summary>
public uint NavmeshVersionObserved;
```

#### 4e. Update Translators

**File:** `Hrot/Network/Hrot.Network.NED/Replication/Map/Egress/NavigationIntentEgressTranslator.cs` (UPDATE)

In the `TranslateIntentToDescriptor` (or similar method that maps ECS fields to DDS descriptor):
add mapping for `RouteHandle`: `descriptor.RouteHandle = intent.RouteHandle;`

Find and update the corresponding ingress translator for `NavigationStatus` and add the new fields.
Search for `NavigationStatusEgressTranslator` or similar in `Hrot/Network/Hrot.Network.NED/`.

Update `MoveToExecutor` to read the new `RouteHandle` field from `MoveToParams` and write it into `NavigationIntent`:
```csharp
intent.RouteHandle = p.RouteHandle;   // add after existing field assignments
```

#### 4f. Tests Required

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/NavigationActionTests.cs` (UPDATE or CREATE)

1. `PlanRouteParams_SizeIs32Bytes` — `Assert.Equal(32, Unsafe.SizeOf<PlanRouteParams>())`
2. `FollowPathParams_SizeIs32Bytes` — same pattern
3. `FetchPathDetailsParams_SizeIs32Bytes` — same pattern
4. `ReleasePathParams_SizeIs32Bytes` — same pattern
5. `MoveToParams_SizeIsAtMost32Bytes` — `Assert.True(Unsafe.SizeOf<MoveToParams>() <= 32)`
6. `NavigationResult_NewValuesNotColliding` — verify PathFound=4, NoPath=5, FailedNoLayer=6, FailedInvalidHandle=7 don't collide with existing (0-3)

**File:** Extend `NavigationContractsTests.cs` or a new test file with:
7. `NavigationStatus_RouteHandle_DefaultIsZero` — verify zero-init gives `RouteHandle == 0`
8. `NavigationStatus_Phase_DefaultIsIdle` — verify zero-init gives `Phase == NavigationPhase.Idle`

**Translator tests:** update `Hrot/Engine/Hrot.Map.Common.Tests/Replication/Egress/NavigationIntentEgressTranslatorTests.cs` to verify `RouteHandle` round-trips through the translator.

**Success conditions:** See TASK-DETAILS.md#nav-p0-t4.

---

### Task 5: NavWaypoint, Enums, Components, ComponentIds (NAV-P0-T5)

**Task Definition:** [TASK-DETAILS.md#nav-p0-t5](./../TASK-DETAILS.md#nav-p0-t5)

**Design refs:** Navigation_Design_v2_0.md §4.3, §4.5, §4.6, §8.3; DD-Fake-Nav §12.

#### 5a. Full NavWaypoint Definition

**File:** `FDP/Toolkits/Fdp.Toolkits/Navigation/NavWaypoint.cs` (REPLACE stub)

Replace the stub with the full 24-byte struct per §4.5:

```csharp
using System.Numerics;
using System.Runtime.InteropServices;

namespace Fdp.Toolkit.Navigation
{
    /// <summary>
    /// A single waypoint in a planned navigation corridor. 24 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct NavWaypoint
    {
        /// <summary>World-space position of the waypoint (metres, FDP Cartesian).</summary>
        public Vector3 Position { get; init; }       // 12 bytes

        /// <summary>How the agent traverses from the previous waypoint to this one.</summary>
        public TraversalKind Traversal { get; init; } // 1 byte

        /// <summary>Surface type of this waypoint (for cost/animation selection).</summary>
        public SurfaceType Surface { get; init; }     // 1 byte

        /// <summary>Padding to maintain 4-byte alignment.</summary>
        private readonly byte _pad0;
        private readonly byte _pad1;                   // 2 bytes → subtotal 16

        /// <summary>Estimated time-of-arrival offset from route start (seconds). 0 = unknown.</summary>
        public float TimeOffset { get; init; }        // 4 bytes → 20

        /// <summary>Reserved for future use (e.g., speed limit at this waypoint).</summary>
        private readonly float _reserved;              // 4 bytes → 24
    }
}
```

#### 5b. TraversalKind and SurfaceType Enums

**File:** `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationEnums.cs` (CREATE — new file for nav enums)
or add to `NavigationComponents.cs` alongside the other enums.

```csharp
/// <summary>How an agent traverses a segment ending at a NavWaypoint.</summary>
public enum TraversalKind : byte
{
    /// <summary>Normal ground movement (walk, drive, swim).</summary>
    Walk    = 0,
    /// <summary>Jump over a gap or small obstacle (off-mesh link).</summary>
    Jump    = 1,
    /// <summary>Climb a ladder or vertical surface.</summary>
    Climb   = 2,
    /// <summary>Open and pass through a door (interaction required).</summary>
    Door    = 3,
    /// <summary>Fly through air (volumetric path).</summary>
    Fly     = 4,
}

/// <summary>Surface type at a NavWaypoint, used for cost weighting and animation selection.</summary>
public enum SurfaceType : byte
{
    /// <summary>Default / unknown surface.</summary>
    Default  = 0,
    /// <summary>Paved road or hard surface.</summary>
    Road     = 1,
    /// <summary>Open terrain (grass, dirt, sand).</summary>
    Terrain  = 2,
    /// <summary>Water surface (naval / amphibious).</summary>
    Water    = 3,
    /// <summary>Indoor floor.</summary>
    Indoor   = 4,
}
```

#### 5c. NavAgentProfile Component

**File:** `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationComponents.cs` (UPDATE — add after NavigationStatus)

Per §8.3, `NavAgentProfile` is a TKB-authored component carrying:

```csharp
/// <summary>
/// Brain-side component authored in the TKB template that describes how this entity navigates.
/// Written by the TKB loader; read by Navigation systems to select layers and backends.
/// </summary>
[ComponentId(NavigationContractsComponentIds.NavAgentProfile)]
[StructLayout(LayoutKind.Sequential)]
public struct NavAgentProfile
{
    /// <summary>Bitmask of navmesh layers this agent can traverse. 0xFFFFFFFF = all.</summary>
    public uint PreferredLayerMask;

    /// <summary>Agent radius for dtCrowd avoidance (metres). Standard: 0.4 for infantry, 1.5 for vehicles.</summary>
    public float AgentRadius;

    /// <summary>Agent height for dtCrowd collision cylinder (metres).</summary>
    public float AgentHeight;

    /// <summary>Maximum slope angle this agent can traverse (degrees).</summary>
    public float MaxSlopeDeg;
}
```

#### 5d. NavigationCorridorMuscle Component (Muscle-internal, no replication)

**File:** `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationComponents.cs` (UPDATE)

Per §4.3:

```csharp
/// <summary>
/// Muscle-internal corridor state for an entity currently following a planned path.
/// Written by PathfindingResultMaterializationSystem; read by NavigationExecutionSystem.
/// NOT replicated to DDS.
/// </summary>
[ComponentId(NavigationContractsComponentIds.NavigationCorridorMuscle)]
[DataPolicy(DataPolicy.NoSave)]
[StructLayout(LayoutKind.Sequential)]
public struct NavigationCorridorMuscle
{
    /// <summary>Handle into the path registry identifying this corridor.</summary>
    public int RouteHandle;

    /// <summary>Navmesh version when this path was planned (forward-compat, §14).</summary>
    public uint NavmeshVersion;

    /// <summary>Index of the waypoint the agent is currently heading towards.</summary>
    public int CurrentSegmentIndex;

    /// <summary>Total number of waypoints in the corridor.</summary>
    public int TotalSegmentCount;

    /// <summary>Total arc-length of the full corridor (metres).</summary>
    public float TotalDistance;

    /// <summary>Which backend planned this path.</summary>
    public byte PrimaryBackend;

    /// <summary>Corridor status flags.</summary>
    public byte Flags;

    private byte _pad0;
    private byte _pad1;
}
```

#### 5e. NavigationCorridorPreview Component (opt-in, replicated, §4.2)

**File:** `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationComponents.cs` (UPDATE)

Per §4.2 — 8 inline `PreviewWaypoint`s:

```csharp
/// <summary>
/// Opt-in sliding window of the next 8 waypoints ahead of the current position.
/// Present only when <c>NavigationIntent.Flags.StreamCorridorPreview</c> is set.
/// Absence = zero replication traffic. Replicated Brain→Brain only.
/// </summary>
[ComponentId(NavigationContractsComponentIds.NavigationCorridorPreview)]
[StructLayout(LayoutKind.Sequential)]
public struct NavigationCorridorPreview
{
    /// <summary>Version counter — bumped on every slide or replan.</summary>
    public uint PreviewVersion;

    /// <summary>Number of valid waypoints in <see cref="Waypoints"/> (1–8).</summary>
    public int WaypointCount;

    /// <summary>Index of the first waypoint in the full corridor (0-based global segment index).</summary>
    public int GlobalSegmentStart;

    private int _pad;

    /// <summary>Inline array of up to 8 preview waypoints (24 bytes each).</summary>
    public PreviewWaypoint W0, W1, W2, W3, W4, W5, W6, W7;
}

/// <summary>Compact waypoint for the preview window (avoids referencing NavWaypoint directly).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct PreviewWaypoint
{
    public System.Numerics.Vector3 Position;  // 12 bytes
    public TraversalKind Traversal;            // 1 byte
    public SurfaceType Surface;                // 1 byte
    private byte _pad0;
    private byte _pad1;                        // → 16 bytes total
}
```

> **Size check:** `NavigationCorridorPreview` = 4+4+4+4 header + 8 × 16 bytes = 144 bytes. Assert this in a test.

#### 5f. NavigationPathDetailsBuffer and CrowdAgent

**File:** `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationComponents.cs` (UPDATE)

```csharp
/// <summary>
/// Brain-side cache backing for path details received via NavigationPathDetailsResponseEvent.
/// Read by FetchPathDetailsExecutor; written by NavigationPathDetailsUpdateSystem.
/// </summary>
[ComponentId(NavigationContractsComponentIds.NavigationPathDetailsBuffer)]
[StructLayout(LayoutKind.Sequential)]
public struct NavigationPathDetailsBuffer
{
    /// <summary>RouteHandle this buffer is for (0 = empty).</summary>
    public int RouteHandle;
    /// <summary>Replan count at which these details were fetched. Used for stale-miss detection.</summary>
    public ushort ReplanCountAtFetch;
    /// <summary>Number of waypoints stored in the registry entry.</summary>
    public ushort WaypointCount;
    /// <summary>Total arc-length of the stored path (metres).</summary>
    public float TotalDistance;
}

/// <summary>
/// Tag component marking an entity managed by the dtCrowd provider.
/// Added by NavigationIntentBridgeSystem when KinematicsMode.Crowd is selected;
/// removed by OffMeshLinkDetectionSystem during traversal.
/// </summary>
[ComponentId(NavigationContractsComponentIds.CrowdAgent)]
public struct CrowdAgent { }
```

#### 5g. ComponentId Allocations

**File:** `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationContractsComponentIds.cs` (UPDATE)

The current file has `NavigationIntent = 67` and `NavigationStatus = 68`. Extend the class
to add production nav components in the **69–79** reserved block (per the architect decision
in TASK-DETAILS.md#nav-p0-t5). Do NOT add fake-only components here (they go in 250–279).

```csharp
// ── Production nav components (69–79) ────────────────────────────────────────
// Architect-confirmed: use the free slots in the existing 50–79 navigation-contracts
// block. 67=NavigationIntent, 68=NavigationStatus already occupy this range.
// Fake-only components (FakeNavmeshState etc.) go in 250–279 per DD-Fake-Nav §12.

public const byte NavAgentProfile             = 69;
public const byte NavigationCorridorMuscle    = 70;
public const byte NavigationCorridorPreview   = 71;
public const byte NavigationPathDetailsBuffer = 72;
public const byte CrowdAgent                  = 73;
// IDs 74–79 reserved for future nav components.

// ── Toolkit expansion spill (215–249) — only if 69–79 overflows ─────────────
// (Not used in this batch; reserved for future Phase 0+ tasks.)
```

**Do NOT add these to `GlobalComponentIds.cs`** — that file now points to
`NavigationContractsComponentIds` for nav IDs. Just add them to `NavigationContractsComponentIds.cs`.

#### 5h. Tests Required

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/NavigationContractsTests.cs` (UPDATE)

Add:
1. `NavWaypoint_SizeIs24Bytes` — `Assert.Equal(24, Unsafe.SizeOf<NavWaypoint>())`
2. `NavigationCorridorPreview_SizeIs144Bytes` — `Assert.Equal(144, Unsafe.SizeOf<NavigationCorridorPreview>())`
3. `PreviewWaypoint_SizeIs16Bytes` — `Assert.Equal(16, Unsafe.SizeOf<PreviewWaypoint>())`
4. `NavContractsComponentIds_NoDuplicateValues` — collect all `const byte` values and
   assert all unique (like the GlobalComponentIds duplicate test):
   ```csharp
   var ids = typeof(NavigationContractsComponentIds)
       .GetFields(...)
       .Select(f => (byte)f.GetValue(null))
       .ToList();
   Assert.Equal(ids.Count, ids.Distinct().Count());
   ```
5. `NavContractsComponentIds_ProductionRange_In69To79` — assert NavAgentProfile through
   CrowdAgent are all in range 69–79.
6. `CrowdAgent_ComponentId_IsDistinctFromNavigationIntent` — `Assert.NotEqual(67, NavigationContractsComponentIds.CrowdAgent)`

**Success conditions:** See TASK-DETAILS.md#nav-p0-t5.

---

## Quality Standards

**Code:**
- All new public types: XML doc comments.
- All structs with `[StructLayout(Sequential)]`: assert size in tests.
- No new warnings in `Fdp.Toolkits` (TreatWarningsAsErrors=true).
- Backward compat: existing `MoveTo` tests (`MoveToExecutorTests`) must still pass after
  extending `MoveToParams` and `NavigationIntent`.

**Tests:**
- Layout tests must use `Unsafe.SizeOf<T>()`.
- ComponentId uniqueness test must use reflection to be future-proof.
- No Assert.NotNull-style shallow tests.

---

## Success Criteria

- [ ] Corrective fix: `NoneIntent_HaltsNavigation_NavStateSetToNone` passes
- [ ] NAV-P0-T4: 4 new param structs each 32 bytes; MoveToParams ≤ 32 bytes; RouteHandle in NavigationIntent; new NavigationResult values; DDS descriptors updated; translators updated; 8+ new tests
- [ ] NAV-P0-T5: NavWaypoint 24 bytes; CorridorPreview 144 bytes; ComponentIds 69–73 allocated; 6+ new tests; no ID collisions
- [ ] All nav tests passing: `dotnet test --filter Navigation` → 0 failures
- [ ] Translator tests passing
- [ ] `dotnet build IOS-IG-SimHost.sln` → 0 errors
- [ ] Report submitted to `.dev/navig-2/reports/BATCH-02-REPORT.md`

---

## Common Pitfalls

- **Size assertions may fail** if padding bytes are not enough. Use `Marshal.SizeOf<T>()` 
  or `Unsafe.SizeOf<T>()` (prefer the latter for `readonly struct` with `init`).
- **Extending MoveToParams** must not break `MoveToExecutorTests` — the existing tests use
  `fixed(byte* src = channel.Params)` pattern; the new fields will be zero-initialized.
- **NavigationStatus is replicated** — adding fields changes DDS wire format. Always update
  the DDS descriptor alongside the ECS struct.
- **ComponentId 67+68 are already taken** — start new allocations at 69.
- **Fake-only component IDs (250–279)** are NOT allocated in this batch; only production IDs.

---

## Reference Materials

- **Task definitions:** `.dev/navig-2/TASK-DETAILS.md` — NAV-P0-T4, NAV-P0-T5
- **Design §4, §8.3, §13, §14:** `.dev/navig-2/Navigation_Design_v2_0.md`
- **DD-Fake-Nav §12:** `.dev/navig-2/DD-Fake-Nav.md` (ComponentId blocks)
- **NavigationComponents.cs:** `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationComponents.cs`
- **NavigationActions.cs:** `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationActions.cs`
- **NavigationConstants.cs:** `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationConstants.cs`
- **DDS SimDescriptors:** `Hrot/Network/Hrot.Network.NED/SimDescriptors.cs`
- **MoveToExecutor:** `FDP/Toolkits/Fdp.Toolkits/Navigation/Executors/MoveToExecutor.cs`
- **NavigationIntentEgressTranslator:** `Hrot/Network/Hrot.Network.NED/Replication/Map/Egress/NavigationIntentEgressTranslator.cs`
- **NavigationContractsComponentIds:** `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationContractsComponentIds.cs`
