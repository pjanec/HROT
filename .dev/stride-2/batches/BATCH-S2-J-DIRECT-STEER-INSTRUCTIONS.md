# BATCH-S2-J — DirectPoint = straight-line steer (bypass navmesh)

## Goal
Make vehicle `NavigationMode.DirectPoint` intents steer **straight to `FinalDestination`**
(single virtual corner), bypassing the navmesh `PlanPath`. This is step 1 of motion control:
get the vehicle to move at all in the tiny arena. The navmesh path-following code is **kept**
and re-enabled behind an env flag for the later navmesh workstream.

### Why
In the small walled arena the navmesh `PlanPath` plans flaky paths whose corners fall **outside**
the ±10 walls (e.g. first corner (-12.4, 11.3)). The vehicle drives into a wall, wedges, and the
stuck-guard fakes an "Arrived". Direct straight-line steering sidesteps the broken navmesh entirely
and is exactly what `DirectPoint` ("drive directly to FinalDestination") should mean. It also backs
the upcoming click-to-move-in-Stride test tool.

## Scope — EDIT ONE FILE ONLY
`Stride/Hrot.Stride.Core/VehicleNavigationIntentSystem.cs`

Do **NOT** touch any other file. Do **NOT** rename/move anything. Do **NOT** change the navmesh
provider, the controller, or the FDP↔Stride swizzle.

## Changes

### 1. Add an instance flag read once from env (default = direct steer, navmesh OFF)
Add a `private readonly bool _useNavmesh;` field. Initialize it in the constructor body:

```csharp
_useNavmesh = string.Equals(
    Environment.GetEnvironmentVariable("STRIDE_VEHICLE_NAVMESH"), "1",
    StringComparison.Ordinal);
```

So when `STRIDE_VEHICLE_NAVMESH=1` → existing navmesh path-following (today's behavior).
Otherwise (default) → new direct straight-line steer.

### 2. `PlanRoute` — branch on the flag
`PlanRoute` is currently `private static`. Change it to a **non-static** instance method (it now
reads `_useNavmesh`). Keep the signature otherwise identical (still takes `navmesh`, `entity`,
`curPos`, `in intent`).

At the very top of `PlanRoute`, before the navmesh `PlanPath` call, add the direct branch:

```csharp
if (!_useNavmesh)
{
    // Direct straight-line steer: single virtual corner at the destination (FDP X/Y).
    // Bypasses the navmesh entirely (see BATCH-S2-J).
    var dest = intent.FinalDestination;
    Log.Info("[VehicleNav] entity #{0} DIRECT steer to FDP ({1:F1},{2:F1}) for IntentId={3} " +
             "(navmesh bypassed; set STRIDE_VEHICLE_NAVMESH=1 to re-enable navmesh).",
        entity.Index, dest.X, dest.Y, intent.IntentId);
    return new RouteState
    {
        PlannedIntentId     = intent.IntentId,
        Corners             = new[] { new Vector2(dest.X, dest.Y) },
        CurrentCorner       = 0,
        StuckWindowStartPos = curPos,
        StuckWindowElapsed  = 0f,
    };
}
```

Leave the existing navmesh body (the `PlanPath` call and corner conversion) UNCHANGED below this
branch — it runs when `_useNavmesh` is true.

Update the caller at line ~219 if needed (it already calls `PlanRoute(navmesh, entity, curPos, intent)`
— now an instance call, which is fine since `Execute` is an instance method).

### 3. Stuck-guard must NOT fake "Arrived" on a single-corner direct route
Today the movement stuck-guard (the `isStuck` branch around line 257-270) calls `AdvanceCorner`,
which on the last corner marks the route **Completed** and writes `NavigationResult.Arrived` — a
**false** arrival when the body never actually moved. For the direct single-corner route we must
not lie. Change the `isStuck` condition so it only fake-advances when there is more than one corner
(i.e. a multi-corner navmesh route):

```csharp
bool isStuck = !output.Arrived
               && route.Corners.Length > 1          // never fake-advance a single-corner direct route
               && route.StuckWindowElapsed >= StuckWindowSec
               && displacement < StuckDisplacementThresholdM;
```

For a direct route that is genuinely stuck (e.g. against a wall) the vehicle simply keeps being
commanded toward the destination — it does NOT report Arrived. (Crawling/getting-stuck quality is
explicitly out of scope right now; correctness of the Arrived signal is what matters.)

## Constraints
- One file only.
- No behavior change when `STRIDE_VEHICLE_NAVMESH=1` (must compile to today's navmesh path).
- `Arrived` must only be written when the controller actually reports arrival at the final corner
  (natural arrival), never via the stuck guard on a direct route.

## Acceptance (lead verifies)
- Builds clean.
- Default run: a `DirectPoint` intent yields a single corner = FinalDestination; `[VehicleNav] ...
  DIRECT steer ...` is logged once per intent; vehicle is commanded straight at the destination.
- The autonomous harness DRIVE phase (`STRIDE_SELFTEST=1`) drives (-7,5)→(4,11) without wedging on
  a wall and without a false early Arrived.
- `STRIDE_VEHICLE_NAVMESH=1` restores navmesh corner-following (regression path intact).
