# BATCH-09: Phase 5 Replan Flow + NAV-P9-T4 Tests

**Batch Number:** BATCH-09
**Tasks:** NAV-P5-T1, NAV-P5-T3, NAV-P9-T4
**Phase:** Phase 5 (Replan, auto-refresh) + Phase 9 (system tests)
**Estimated Effort:** 4-6 hours
**Priority:** HIGH
**Dependencies:** BATCH-08 (committed, hash 2097ec67)

---

## Onboarding & Workflow

### Developer Instructions

BATCH-09 extends the Muscle-side navigation system with an internal replan flow. When an entity
gets stuck (frustration watchdog fires), the Muscle layer now silently re-requests a path from
the solver instead of immediately reporting failure. This is the last pure-logic batch before the
engine-backed module (Phase 6). The batch also adds `NavigationProgressTrackerSystemTests`
(NAV-P9-T4), which validates the new events.

### Required Reading (IN ORDER)

1. `.dev/navig-2/Navigation_Design_v2_0.md` §3.4 (Replan flow), §5.4 (Flags), §12 (events)
2. `.dev/navig-2/DD-Tests-Nav.md` §4.4 (`NavigationProgressTrackerSystemTests`)
3. `.dev/navig-2/TASK-DETAILS.md` — NAV-P5-T1, NAV-P5-T3, NAV-P9-T4
4. BATCH-08-REPORT.md (this session's context)

### Source Code Locations

- **Primary system:** `FDP/Toolkits/Fdp.Toolkits/CarKinem/Systems/NavigationExecutionSystem.cs`
- **Actions (params structs):** `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationActions.cs`
- **Components:** `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationComponents.cs`
- **Constants:** `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationConstants.cs`
- **FrustrationTicks:** `FDP/Toolkits/Fdp.Toolkits/CarKinem/Core/FrustrationTicks.cs`
- **Executor:** `FDP/Toolkits/Fdp.Toolkits/Navigation/Executors/MoveToExecutor.cs`
- **Test factory:** `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/NavigationTestWorldFactory.cs`
- **Test project:** `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/`

### Build & Test Command

```powershell
cd "d:\Work\IOS-IG-SimHost-FDP-2"
dotnet build "FDP\FDP.sln" --configuration Debug
dotnet test "FDP\Toolkits\Fdp.Toolkits.Tests" --filter "Navigation" --configuration Debug
```

### Report Submission

Create `.dev/navig-2/batches/BATCH-09-REPORT.md` when done.

---

## Context

`NavigationExecutionSystem` currently detects frustration and immediately writes `FailedBlocked`.
Phase 5 extends this: on frustration, the system first tries an internal replan (re-publishes
`PathfindingRequestEvent` with the same `RouteHandle`) and only reports hard failure after the
replan budget is exhausted.

Additionally, the system now fires lifecycle events (`MoveStartedEvent`, `MoveCompletedEvent`,
`PathReplannedEvent`, `MoveBlockedEvent`) that are consumed by the Brain side and test harnesses.

NAV-P9-T4 (`NavigationProgressTrackerSystemTests`) validates all of this.

---

## Task 1: Extend `MoveToParams` with `Flags` and `MaxReplans`

**File:** `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationActions.cs`

The current `MoveToParams` has three padding bytes after `ReverseAllowed` that are unused.
Replace `_pad0` and `_pad1` with named fields:

```csharp
// Current layout (32 bytes total):
public byte ReverseAllowed;
private byte _pad0;
private byte _pad1;
private byte _pad2;

// New layout (same 32 bytes, just naming the pads):
public byte ReverseAllowed;
/// <summary>
/// Behavioural flags for the MoveTo action.
/// Bit 0: AllowReplan — Muscle is allowed to internally replan on frustration.
/// Bit 4: AutoSendPathOnReplan — Each internal replan also fires
///         <see cref="NavigationPathDetailsResponseEvent"/> with IsAutoRefresh=true.
/// </summary>
public byte Flags;
/// <summary>
/// Maximum number of internal Muscle replans before hard failure (0 = use
/// <see cref="NavigationConstants.DefaultMaxReplans"/>).
/// </summary>
public byte MaxReplans;
private byte _pad0;
```

**Important:** Keep `ReverseAllowed` in its existing position; just rename the pads after it.
This keeps the struct at exactly 32 bytes. Existing tests that set `ReverseAllowed = 0/1`
are unaffected.

---

## Task 2: Extend `NavigationIntent` with `Flags` and `MaxReplans`

**File:** `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationComponents.cs`

The `NavigationIntent` struct currently has two unnamed padding bytes after `Mode`.
Replace `_pad0` and `_pad1` with:

```csharp
// Current:
private byte _pad0;
private byte _pad1;

// New:
/// <summary>
/// Behavioural flags for the active navigation command.
/// Bit 0: AllowReplan. Bit 4: AutoSendPathOnReplan.
/// Copied from <see cref="MoveToParams.Flags"/> by <c>MoveToExecutor</c>.
/// </summary>
public byte Flags;

/// <summary>
/// Maximum internal Muscle replans for this command (0 = use
/// <see cref="NavigationConstants.DefaultMaxReplans"/>).
/// Copied from <see cref="MoveToParams.MaxReplans"/> by <c>MoveToExecutor</c>.
/// </summary>
public byte MaxReplans;
```

Layout: `Mode`(1) + `Flags`(1) + `MaxReplans`(1) + `ReverseAllowed`(1) + `FinalDestination`(8) + ...
Total struct size is unchanged.

---

## Task 3: Add constants to `NavigationConstants`

**File:** `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationConstants.cs`

Add after the `FleeReplanIntervalTicks` constant:

```csharp
// ── Replan policy defaults ─────────────────────────────────────────────────

/// <summary>
/// Default maximum number of Muscle-internal replans per intent episode when
/// <see cref="MoveToParams.MaxReplans"/> is 0 (caller did not specify a limit).
/// </summary>
public const byte DefaultMaxReplans = 3;

// ── Intent Flags bits ──────────────────────────────────────────────────────

/// <summary>Bit index in <see cref="NavigationIntent.Flags"/>: allow internal Muscle replan.</summary>
public const byte FlagBitAllowReplan = 0;

/// <summary>Bit index in <see cref="NavigationIntent.Flags"/>: fire auto-refresh path details on replan.</summary>
public const byte FlagBitAutoSendPathOnReplan = 4;
```

---

## Task 4: Extend `FrustrationTicks`

**File:** `FDP/Toolkits/Fdp.Toolkits/CarKinem/Core/FrustrationTicks.cs`

Add two flag bytes after `Ticks`:

```csharp
public struct FrustrationTicks
{
    /// <summary>...</summary>
    public int Ticks;

    /// <summary>
    /// Set to 1 after <c>MoveStartedEvent</c> has been fired for the current intent.
    /// Reset to 0 when a new intent is detected (<c>IntentId</c> mismatch).
    /// </summary>
    public byte MoveStartedFired;

    /// <summary>
    /// Set to 1 after <c>MoveBlockedEvent</c> has been fired for the current
    /// frustration episode. Reset to 0 when the entity starts moving again.
    /// This throttles the event to once per blocking episode.
    /// </summary>
    public byte BlockedEventFired;

    // 2 bytes explicit padding (struct alignment).
    private byte _pad0;
    private byte _pad1;
}
```

---

## Task 5: Update `MoveToExecutor` to copy flags into `NavigationIntent`

**File:** `FDP/Toolkits/Fdp.Toolkits/Navigation/Executors/MoveToExecutor.cs`

In `OnEnter`, after the existing `intent.ReverseAllowed = p.ReverseAllowed;` line, add:

```csharp
intent.Flags      = p.Flags;
intent.MaxReplans = p.MaxReplans;
```

---

## Task 6: Extend `NavigationExecutionSystem` with replan flow and events

**File:** `FDP/Toolkits/Fdp.Toolkits/CarKinem/Systems/NavigationExecutionSystem.cs`

This is the largest change. The system must now:
1. Fire `MoveStartedEvent` on the **first tick** of a new intent.
2. Fire `MoveCompletedEvent` when navigation reaches a terminal state (Arrived or hard failure).
3. On frustration threshold: attempt an internal replan if allowed.
4. Fire `PathReplannedEvent` + optionally `NavigationPathDetailsResponseEvent` on each replan.
5. Fire `MoveBlockedEvent` **once per blocking episode** (throttled via `BlockedEventFired`).
6. On budget exhaustion: write `FailedBlocked` + fire `MoveCompletedEvent`.

### Constants to add to the system class

```csharp
public const int FrustrationTickLimit = 120; // already exists
```

### New query requirements

The query must now include `FrustrationTicks` (already in query).
Events to publish: `MoveStartedEvent`, `MoveCompletedEvent`, `PathReplannedEvent`,
`MoveBlockedEvent`, `NavigationPathDetailsResponseEvent`, `PathfindingRequestEvent`.

The events are published via `repo.Bus.Publish(...)`.

### Replan logic

Helper constant for Flags:
```csharp
const byte AllowReplanBit = 1 << NavigationConstants.FlagBitAllowReplan;           // = 0x01
const byte AutoSendBit    = 1 << NavigationConstants.FlagBitAutoSendPathOnReplan;  // = 0x10
```

In the frustration guard block, instead of directly writing `FailedBlocked`:

```csharp
if (speed < FrustrationSpeedThreshold)
{
    frustration.Ticks++;
    repo.SetComponent(entity, frustration);

    if (frustration.Ticks > FrustrationTickLimit)
    {
        bool allowReplan  = (intent.Flags & (1 << NavigationConstants.FlagBitAllowReplan)) != 0;
        byte maxReplans   = intent.MaxReplans != 0
                            ? intent.MaxReplans
                            : NavigationConstants.DefaultMaxReplans;

        if (allowReplan && status.ReplanCount < maxReplans)
        {
            // Internal Muscle replan: re-publish pathfinding request with same handle.
            var request = new PathfindingRequestEvent
            {
                RequestId  = (long)entity.Index << 32 | (uint)status.ReplanCount,
                Start      = tf.Position,
                End        = new System.Numerics.Vector3(
                                 intent.FinalDestination.X,
                                 intent.FinalDestination.Y,
                                 tf.Position.Z),
                RouteHandle = intent.RouteHandle,
            };
            repo.Bus.Publish(request);

            status.ReplanCount++;
            repo.SetComponent(entity, status);

            // Fire PathReplannedEvent.
            repo.Bus.Publish(new PathReplannedEvent
            {
                Target      = entity,
                RouteHandle = intent.RouteHandle,
                ReplanCount = status.ReplanCount,
            });

            // Auto-refresh: also fire path-details response if flag is set.
            if ((intent.Flags & (1 << NavigationConstants.FlagBitAutoSendPathOnReplan)) != 0)
            {
                repo.Bus.Publish(new NavigationPathDetailsResponseEvent
                {
                    Entity      = entity,
                    RouteHandle = intent.RouteHandle,
                    IsAutoRefresh = 1,
                });
            }

            // Fire MoveBlockedEvent (throttled: once per episode).
            if (frustration.BlockedEventFired == 0)
            {
                repo.Bus.Publish(new MoveBlockedEvent { Target = entity });
                frustration.BlockedEventFired = 1;
                repo.SetComponent(entity, frustration);
            }

            // Reset frustration counter to allow a new episode after the replan.
            frustration.Ticks = 0;
            repo.SetComponent(entity, frustration);

            status.Result = NavResult.InProgress;
            repo.SetComponent(entity, status);
            continue;
        }
        else
        {
            // Budget exhausted — hard failure.
            status.Result = NavResult.FailedBlocked;
            repo.SetComponent(entity, status);

            repo.Bus.Publish(new MoveCompletedEvent
            {
                Target     = entity,
                Reason     = NavResult.FailedBlocked,
                RouteHandle = intent.RouteHandle,
            });

            frustration.Ticks = 0;
            repo.SetComponent(entity, frustration);
            continue;
        }
    }
}
else
{
    // Vehicle is moving — reset frustration counter AND BlockedEventFired.
    if (frustration.Ticks != 0 || frustration.BlockedEventFired != 0)
    {
        frustration.Ticks            = 0;
        frustration.BlockedEventFired = 0;
        repo.SetComponent(entity, frustration);
    }
}
```

### MoveStartedEvent firing (new intent detection block)

In the new intent detection block (where `status.IntentId != intent.IntentId`):

```csharp
if (status.IntentId != intent.IntentId)
{
    // ... existing reset logic ...

    // Fire MoveStartedEvent once on the first tick of each new intent.
    if (frustration.MoveStartedFired == 0)
    {
        repo.Bus.Publish(new MoveStartedEvent
        {
            RouteHandle = intent.RouteHandle,
        });
        frustration.MoveStartedFired = 1;
        repo.SetComponent(entity, frustration);
    }
}
```

Wait — actually the MoveStartedEvent should fire on the FIRST tick of a new intent. The new intent is detected when `status.IntentId != intent.IntentId`. But `MoveStartedFired` is also reset when a new intent is detected (below). The cleanest approach: reset `MoveStartedFired = 0` when the intent changes, then set it to `1` after firing.

**Full new-intent detection block** (replace existing one):

```csharp
if (status.IntentId != intent.IntentId)
{
    status = new NavigationStatus
    {
        IntentId  = intent.IntentId,
        Result    = NavResult.InProgress,
        ProgressS = progressAtThisTick,
    };
    repo.SetComponent(entity, status);
    
    frustration = new FrustrationTicks(); // resets all fields to 0
    repo.SetComponent(entity, frustration);

    // Fire MoveStartedEvent on the first tick of the new intent.
    repo.Bus.Publish(new MoveStartedEvent
    {
        RouteHandle = intent.RouteHandle,
    });
    frustration.MoveStartedFired = 1;
    repo.SetComponent(entity, frustration);
}
```

### MoveCompletedEvent on arrival

In the arrival block:
```csharp
if (arrived)
{
    status.Result = NavResult.Arrived;
    repo.SetComponent(entity, status);
    
    repo.Bus.Publish(new MoveCompletedEvent
    {
        Target      = entity,
        Reason      = NavResult.Arrived,
        RouteHandle = intent.RouteHandle,
    });
    
    frustration.Ticks = 0;
    repo.SetComponent(entity, frustration);
    continue;
}
```

### Required using directives

The system file needs:
```csharp
using Fdp.Toolkit.Navigation;
```

It already has `using Fdp.Toolkit.Navigation;` via the aliased `NavResult` import.
Verify that `PathfindingRequestEvent`, `PathReplannedEvent`, `MoveBlockedEvent`,
`MoveCompletedEvent`, `MoveStartedEvent`, `NavigationPathDetailsResponseEvent`
are all in `Fdp.Toolkit.Navigation` namespace (they are, in `PathfindingEvents.cs`).

### NavigationPathDetailsResponseEvent field check

Look at the struct definition to confirm field names before using them:

```csharp
[EventId(2041)]
public struct NavigationPathDetailsResponseEvent
{
    public Entity Entity;      // or similar — check the actual struct
    public int RouteHandle;
    public byte IsAutoRefresh; // 1 = true
    ...
}
```

Read `FDP/Toolkits/Fdp.Toolkits/Navigation/PathfindingEvents.cs` to confirm actual field names.

---

## Task 7: Update `NavigationTestWorldFactory`

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/NavigationTestWorldFactory.cs`

Add the following registrations to the `Create()` method:

```csharp
// Frustration tracking — required by NavigationExecutionSystem.
world.RegisterComponent<FrustrationTicks>();

// Replan-flow events — required by Phase 5 tests.
world.RegisterEvent<MoveStartedEvent>();
world.RegisterEvent<PathReplannedEvent>();
world.RegisterEvent<MoveBlockedEvent>();
world.RegisterEvent<PathfindingRequestEvent>();
world.RegisterEvent<WaypointReachedEvent>();   // catalog completeness
```

Also add the `CarKinem.Core` using if not present.

---

## Task 8: Create `NavigationProgressTrackerSystemTests.cs`

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/NavigationProgressTrackerSystemTests.cs`

This file adds 10 tests from DD-Tests-Nav §4.4. The test class uses
`NavigationExecutionSystem` directly (not via a module — just `system.Execute(repo, dt)`).

### Namespace & using block

```csharp
using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Systems;
using Xunit;
```

### Helper: `CreateMovingEntity(EntityRepository repo, NavigationMode mode = NavigationMode.DirectPoint)`

Creates an entity with `SimTransform`, `SimVelocity`, `NavigationIntent`, `NavigationStatus`,
`FrustrationTicks`, `NavState`. Sets `SimVelocity.Linear = new Vector3(5f, 0f, 0f)` (moving).

```csharp
private static Entity CreateMovingEntity(EntityRepository repo,
    NavigationMode mode = NavigationMode.DirectPoint,
    byte intentFlags = 0,
    byte maxReplans = 0)
{
    var entity = repo.CreateEntity();
    repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero });
    repo.AddComponent(entity, new SimVelocity { Linear = new Vector3(5f, 0f, 0f) });
    repo.AddComponent(entity, new NavigationIntent
    {
        Mode             = mode,
        IntentId         = 1,
        FinalDestination = new Vector2(100f, 0f),
        ArrivalRadius    = 1f,
        Flags            = intentFlags,
        MaxReplans       = maxReplans,
    });
    repo.AddComponent(entity, new NavigationStatus());
    repo.AddComponent(entity, new FrustrationTicks());
    repo.AddComponent(entity, new NavState());
    return entity;
}
```

### Helper: `CreateStuckEntity(EntityRepository repo, byte intentFlags = 0, byte maxReplans = 0)`

Same as above but `SimVelocity.Linear = Vector3.Zero` (stuck).

### Helper: `DriveToFrustration(EntityRepository repo, NavigationExecutionSystem sys, Entity entity, int ticks)`

Calls `sys.Execute(repo, 0.016f)` for `ticks` iterations. Does NOT swap buffers (tests check accumulated events).

Actually, for event reads, the test should call `repo.Bus.SwapBuffers()` between reads.
Use `(ISimulationView)repo` when calling `Execute`.

### View helper

```csharp
private ISimulationView AsView(EntityRepository repo) => (ISimulationView)repo;
```

### Test 1: `FirstTickOfMove_EmitsMoveStartedEvent`

```csharp
[Fact]
public void FirstTickOfMove_EmitsMoveStartedEvent()
{
    using var repo = NavigationTestWorldFactory.Create();
    var view = (ISimulationView)repo;
    var sys  = new NavigationExecutionSystem();

    var entity = CreateMovingEntity(repo);
    sys.Execute(view, 0.016f);
    repo.Bus.SwapBuffers();

    var events = view.ReadEvents<MoveStartedEvent>();
    Assert.Equal(1, events.Length);
}
```

### Test 2: `FirstTickOfMove_MoveStartedEvent_NotFiredOnSubsequentTicks`

```csharp
[Fact]
public void FirstTickOfMove_MoveStartedEvent_NotFiredOnSubsequentTicks()
{
    using var repo = NavigationTestWorldFactory.Create();
    var view = (ISimulationView)repo;
    var sys  = new NavigationExecutionSystem();

    var entity = CreateMovingEntity(repo);

    // First tick fires the event.
    sys.Execute(view, 0.016f);
    repo.Bus.SwapBuffers();
    view.ReadEvents<MoveStartedEvent>(); // drain

    // Second tick must NOT fire again.
    sys.Execute(view, 0.016f);
    repo.Bus.SwapBuffers();
    var events = view.ReadEvents<MoveStartedEvent>();
    Assert.Equal(0, events.Length);
}
```

### Test 3: `Arrived_EmitsMoveCompletedEventWithArrived`

```csharp
[Fact]
public void Arrived_EmitsMoveCompletedEventWithArrived()
{
    using var repo = NavigationTestWorldFactory.Create();
    var view = (ISimulationView)repo;
    var sys  = new NavigationExecutionSystem();

    var entity = repo.CreateEntity();
    repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero });
    repo.AddComponent(entity, new SimVelocity { Linear = new Vector3(5f, 0f, 0f) });
    // Place destination within arrival radius.
    repo.AddComponent(entity, new NavigationIntent
    {
        Mode             = NavigationMode.DirectPoint,
        IntentId         = 1,
        FinalDestination = new Vector2(0.1f, 0f), // 0.1 m away
        ArrivalRadius    = 1f,                     // radius = 1 m > 0.1 m
        Flags            = 0,
    });
    repo.AddComponent(entity, new NavigationStatus());
    repo.AddComponent(entity, new FrustrationTicks());
    repo.AddComponent(entity, new NavState { HasArrived = 1 });

    sys.Execute(view, 0.016f);
    repo.Bus.SwapBuffers();

    var events = view.ReadEvents<MoveCompletedEvent>();
    Assert.Equal(1, events.Length);
    Assert.Equal(NavigationResult.Arrived, events[0].Reason);
}
```

### Test 4: `FailedBlocked_WithoutReplan_WritesMoveCompletedFailedBlocked`

Entity has no AllowReplan flag. After FrustrationTickLimit+1 ticks of zero velocity,
`MoveCompletedEvent{Reason=FailedBlocked}` fires.

```csharp
[Fact]
public void FailedBlocked_WithoutReplan_WritesMoveCompletedFailedBlocked()
{
    using var repo = NavigationTestWorldFactory.Create();
    var view = (ISimulationView)repo;
    var sys  = new NavigationExecutionSystem();

    var entity = CreateStuckEntity(repo, intentFlags: 0 /* AllowReplan off */);

    // Drive past the frustration limit.
    for (int i = 0; i <= NavigationExecutionSystem.FrustrationTickLimit + 1; i++)
        sys.Execute(view, 0.016f);
    repo.Bus.SwapBuffers();

    var completed = view.ReadEvents<MoveCompletedEvent>();
    Assert.Equal(1, completed.Length);
    Assert.Equal(NavigationResult.FailedBlocked, completed[0].Reason);
}
```

### Test 5: `MoveBlocked_ThrottledEmission`

Entity has `AllowReplan` but after each replan it re-frustrates. Verify `MoveBlockedEvent`
fires once per episode (once, not on every stuck tick).

```csharp
[Fact]
public void MoveBlocked_ThrottledEmission()
{
    using var repo = NavigationTestWorldFactory.Create();
    var view = (ISimulationView)repo;
    var sys  = new NavigationExecutionSystem();

    const byte AllowReplan = 1; // bit 0
    var entity = CreateStuckEntity(repo, intentFlags: AllowReplan, maxReplans: 1);

    // Drive past frustration limit once.
    for (int i = 0; i <= NavigationExecutionSystem.FrustrationTickLimit + 1; i++)
        sys.Execute(view, 0.016f);
    repo.Bus.SwapBuffers();

    var blocked = view.ReadEvents<MoveBlockedEvent>();
    // Exactly one event per blocking episode, not one per tick.
    Assert.Equal(1, blocked.Length);
}
```

### Test 6: `MuscleInternalReplan_EmitsPathReplannedEvent`

Entity has `AllowReplan` flag. After frustration, `PathReplannedEvent` fires.

```csharp
[Fact]
public void MuscleInternalReplan_EmitsPathReplannedEvent()
{
    using var repo = NavigationTestWorldFactory.Create();
    var view = (ISimulationView)repo;
    var sys  = new NavigationExecutionSystem();

    const byte AllowReplan = 1;
    var entity = CreateStuckEntity(repo, intentFlags: AllowReplan, maxReplans: 3);

    for (int i = 0; i <= NavigationExecutionSystem.FrustrationTickLimit + 1; i++)
        sys.Execute(view, 0.016f);
    repo.Bus.SwapBuffers();

    var replanned = view.ReadEvents<PathReplannedEvent>();
    Assert.Equal(1, replanned.Length);
    Assert.Equal(entity, replanned[0].Target);
}
```

### Test 7: `MuscleInternalReplan_BumpsReplanCount`

```csharp
[Fact]
public void MuscleInternalReplan_BumpsReplanCount()
{
    using var repo = NavigationTestWorldFactory.Create();
    var view = (ISimulationView)repo;
    var sys  = new NavigationExecutionSystem();

    const byte AllowReplan = 1;
    var entity = CreateStuckEntity(repo, intentFlags: AllowReplan, maxReplans: 3);

    for (int i = 0; i <= NavigationExecutionSystem.FrustrationTickLimit + 1; i++)
        sys.Execute(view, 0.016f);
    repo.Bus.SwapBuffers();

    var status = repo.GetComponent<NavigationStatus>(entity);
    Assert.Equal(1, status.ReplanCount);
}
```

### Test 8: `AutoSendPathOnReplan_FiresPathDetailsResponse`

Flag: `AllowReplan | AutoSendPathOnReplan`.

```csharp
[Fact]
public void AutoSendPathOnReplan_FiresPathDetailsResponse()
{
    using var repo = NavigationTestWorldFactory.Create();
    var view = (ISimulationView)repo;
    var sys  = new NavigationExecutionSystem();

    const byte AllowReplan         = 1 << NavigationConstants.FlagBitAllowReplan;
    const byte AutoSendPathOnReplan = 1 << NavigationConstants.FlagBitAutoSendPathOnReplan;
    byte flags = (byte)(AllowReplan | AutoSendPathOnReplan);

    var entity = CreateStuckEntity(repo, intentFlags: flags, maxReplans: 3);

    for (int i = 0; i <= NavigationExecutionSystem.FrustrationTickLimit + 1; i++)
        sys.Execute(view, 0.016f);
    repo.Bus.SwapBuffers();

    var details = view.ReadEvents<NavigationPathDetailsResponseEvent>();
    Assert.Equal(1, details.Length);
    Assert.Equal(1, details[0].IsAutoRefresh); // check actual field name in struct
}
```

**Note:** Check the exact field names in `NavigationPathDetailsResponseEvent` before writing
the assertion. The struct is in `PathfindingEvents.cs`.

### Test 9: `AutoSendPathOnReplan_NotSet_NoResponseFired`

Only `AllowReplan` flag; no `AutoSendPathOnReplan`.

```csharp
[Fact]
public void AutoSendPathOnReplan_NotSet_NoResponseFired()
{
    using var repo = NavigationTestWorldFactory.Create();
    var view = (ISimulationView)repo;
    var sys  = new NavigationExecutionSystem();

    const byte AllowReplan = 1 << NavigationConstants.FlagBitAllowReplan;
    var entity = CreateStuckEntity(repo, intentFlags: AllowReplan, maxReplans: 3);

    for (int i = 0; i <= NavigationExecutionSystem.FrustrationTickLimit + 1; i++)
        sys.Execute(view, 0.016f);
    repo.Bus.SwapBuffers();

    var details = view.ReadEvents<NavigationPathDetailsResponseEvent>();
    Assert.Equal(0, details.Length);
}
```

### Test 10: `ReplanBudgetExhausted_WritesFailedBlocked`

Exhaust `MaxReplans = 1`. After 2 frustration episodes, `FailedBlocked` written.

```csharp
[Fact]
public void ReplanBudgetExhausted_WritesFailedBlocked()
{
    using var repo = NavigationTestWorldFactory.Create();
    var view = (ISimulationView)repo;
    var sys  = new NavigationExecutionSystem();

    const byte AllowReplan = 1 << NavigationConstants.FlagBitAllowReplan;
    var entity = CreateStuckEntity(repo, intentFlags: AllowReplan, maxReplans: 1);

    // Drive through 2 frustration episodes (episode 1 replans, episode 2 hard-fails).
    int ticksPerEpisode = NavigationExecutionSystem.FrustrationTickLimit + 2;
    for (int ep = 0; ep < 2; ep++)
    {
        for (int i = 0; i <= ticksPerEpisode; i++)
            sys.Execute(view, 0.016f);
        repo.Bus.SwapBuffers();
        // Drain events between episodes.
        view.ReadEvents<PathReplannedEvent>();
        view.ReadEvents<MoveBlockedEvent>();
    }

    var status = repo.GetComponent<NavigationStatus>(entity);
    Assert.Equal(NavigationResult.FailedBlocked, status.Result);
}
```

---

## Verification

After implementing all tasks:

1. Build: `dotnet build "FDP\FDP.sln" --configuration Debug` — must have **0 errors**.
2. Test: `dotnet test "FDP\Toolkits\Fdp.Toolkits.Tests" --filter "Navigation" --configuration Debug`
   — must have **0 failures**.

Expected total Navigation test count: ~214 (204 existing + 10 new).

---

## Important Notes

### Field names in `NavigationPathDetailsResponseEvent`

Before writing tests for this event, read the actual struct definition in
`FDP/Toolkits/Fdp.Toolkits/Navigation/PathfindingEvents.cs`.
The struct may use `Target` instead of `Entity`, or `IsAutoRefresh` may be a `bool` or `byte`.
Adjust assertions accordingly.

### Event bus registration

Every event type published by `NavigationExecutionSystem` MUST be registered in the test repo
(via `RegisterEvent<T>()`). See Task 7 — `NavigationTestWorldFactory` already registers some;
add the missing ones there.

### Existing `NavigationExecutionSystemTests`

After modifying `FrustrationTicks` (adding fields), the existing `NavigationExecutionSystemTests`
must still pass. Check `FDP/Toolkits/Fdp.Toolkits.Tests/CarKinem/Systems/NavigationExecutionSystemTests.cs`
for any hardcoded struct initialization that may fail after the new fields.

### `FrustrationTicks` reset on new intent

When a new intent is detected (`status.IntentId != intent.IntentId`), reset `FrustrationTicks`
using `new FrustrationTicks()` (sets ALL fields to 0 in one assignment) before setting
`MoveStartedFired = 1` and writing back.

### Replan counter reset

After a successful replan, `frustration.Ticks = 0` is set so the entity gets a fresh
frustration budget for the next blocking episode. `BlockedEventFired` is also reset
in the moving branch so the next blocking episode can fire `MoveBlockedEvent` again.

### Do NOT add `NavState.HasArrived` dependency in the helper

The test helper creates `NavState` with `HasArrived = 0` by default. Only the arrival test
sets `HasArrived = 1`. The system reads `NavState.HasArrived` if `NavState` is present.
