# BATCH-17 Instructions — IG NED Removal Completion + Debt Cleanup

**Prerequisite reading:**
- [DESIGN.md](./DESIGN.md)
- [BATCH-16-REVIEW.md](./reviews/BATCH-16-REVIEW.md)
- [DEBT-TRACKER.md](./DEBT-TRACKER.md)

---

## Context

BATCH-16 delivered the IG network decoupling infrastructure but left Task 19 (remove
Hrot.Network.NED from Hrot.IG.csproj) blocked. The blocker is `OrchestratePersonalRouteAsync`
in `IgApplication.cs`, which builds a NED `CreateEntityRequest` with four `EntityDescriptorUnion`
entries (EntityMaster, WorldPos, MapRoute, EntityInfo). The neutral `CreateEntityCommand` cannot
represent this.

BATCH-17 resolves the blocker by extending `IIgNetworkAdapter` with a route-specific operation,
completing the `SendGeoSpatialUpdate` neutral path, and cleaning up leftover issues.

---

## Mandatory Workflow: Test-Driven Task Progression

For each task:
1. **Understand** existing tests before modifying production code.
2. **Write or update tests first** when adding new functionality.
3. **Make tests pass** by writing the production code.
4. **Verify** the full test suite after each task: `dotnet test <project>`.
5. Never mark a task done if any related test fails.

---

## Priority 1 — DEBT-011: Complete IG NED Removal

### Task 1: Extend IIgNetworkAdapter with CreateRouteEntityAsync

**File:** `Hrot.Core/Network/IIgNetworkAdapter.cs` (UPDATE)

Add a route-entity creation method to the neutral adapter interface:

```csharp
/// <summary>
/// Creates a route entity with the given waypoints and returns the assigned entity ID.
/// Returns 0 on failure.
/// </summary>
Task<int> CreateRouteEntityAsync(
    long tkbRouteType,
    IReadOnlyList<(double Lat, double Lon, double Alt)> waypoints,
    double anchorLat, double anchorLon, double anchorAlt,
    int commanderEntityId,
    CancellationToken ct = default);
```

Also update `NullIgNetworkAdapter`:
```csharp
public Task<int> CreateRouteEntityAsync(
    long tkbRouteType,
    IReadOnlyList<(double Lat, double Lon, double Alt)> waypoints,
    double anchorLat, double anchorLon, double anchorAlt,
    int commanderEntityId,
    CancellationToken ct = default)
    => Task.FromResult(0);
```

### Task 2: Implement CreateRouteEntityAsync in NedIgNetworkAdapter

**File:** `Hrot.Network.NED/IG/NedIgNetworkAdapter.cs` (UPDATE)

Implement by building the NED multi-descriptor request:

```csharp
public async Task<int> CreateRouteEntityAsync(
    long tkbRouteType,
    IReadOnlyList<(double Lat, double Lon, double Alt)> waypoints,
    double anchorLat, double anchorLon, double anchorAlt,
    int commanderEntityId,
    CancellationToken ct = default)
{
    var waypointStructs = waypoints.Select(w => new Waypoint
    {
        Position = new GeoPoint { Latitude = w.Lat, Longitude = w.Lon, Altitude = w.Alt },
        SpeedMetersPerSec = 0.0,
    }).ToList();

    var createReq = new CreateEntityRequest
    {
        RequestId = Guid.NewGuid(),
        Owner     = default,
        Flags     = 0,
        InitialDescriptors = new List<EntityDescriptorUnion>
        {
            new EntityDescriptorUnion
            {
                _d           = EDescriptorType.dtEntityMaster,
                EntityMaster = new EntityMaster { TkbType = tkbRouteType }
            },
            new EntityDescriptorUnion
            {
                _d       = EDescriptorType.dtWorldPos,
                WorldPos = new WorldPos { Pos = new GeoPoint { Latitude = anchorLat, Longitude = anchorLon, Altitude = anchorAlt } }
            },
            new EntityDescriptorUnion
            {
                _d       = EDescriptorType.dtMapRoute,
                MapRoute = new MapRoute { Points = waypointStructs, IsLoop = false }
            },
            new EntityDescriptorUnion
            {
                _d         = EDescriptorType.dtEntityInfo,
                EntityInfo = new Hrot.NED.Descriptors.EntityInfo { CommanderId = commanderEntityId }
            },
        }
    };

    try
    {
        var ack = await ((Hrot.Map.Common.Commands.NedCommandGateway)_commandGateway).CreateEntityAsync(createReq);
        return ack.StatusCode < 2 ? ack.EntityId : 0;
    }
    catch (Exception ex)
    {
        FdpLog<NedIgNetworkAdapter>.Error("CreateRouteEntityAsync failed: {0}", ex.Message);
        return 0;
    }
}
```

**Note:** Cast `_commandGateway` to `NedCommandGateway` using `as` — if null (offline), return 0.

### Task 3: Refactor OrchestratePersonalRouteAsync

**File:** `Hrot.IG/IgApplication.cs` (UPDATE)

Locate `OrchestratePersonalRouteAsync` (currently uses NED types directly).
Replace it to use `_networkAdapter.CreateRouteEntityAsync`:

**OLD logic summary:**
1. Convert canvas points to geodetic waypoints (GeoPoint structs)
2. Build `CreateEntityRequest` with EntityMaster, WorldPos, MapRoute, EntityInfo descriptors
3. Await `_commandGatewayInterface.CreateEntityAsync(createReq)` → `CreateUpdateDeleteEntityAck`
4. Check `ack.StatusCode`
5. Build `MissionControlRequest` with NED `MissionCommandUnion`
6. Await `_commandGatewayInterface.SendMissionControlRequestAsync(missionRequest)`

**NEW logic:**
1. Convert canvas points to `(double Lat, double Lon, double Alt)` tuples using `_geoTransform`
2. Call `await _networkAdapter!.CreateRouteEntityAsync(TkbEntityTypes.TacGraphic_Route, waypoints, anchorLat, anchorLon, anchorAlt, vehicleId)`
3. If returned ID <= 0, cancel and return
4. Build `MissionControlCommand` with neutral types:
   ```csharp
   var missionCmd = new MissionControlCommand
   {
       EntityId    = vehicleId,
       CommandType = Hrot.Core.Mission.eMissionCommandType.CMD_REPLACE_MISSION,
       Plan = new Hrot.Core.Mission.MissionPlan
       {
           ActiveTaskId = taskId,
           Tasks = new List<Hrot.Core.Mission.MissionTask>
           {
               new Hrot.Core.Mission.MissionTask
               {
                   TaskId           = taskId,
                   ExecutingEngine  = "CGFX",
                   BehaviorId       = "FollowRoute",
                   BehaviorParams   = $"{{\"routeEntityId\":{routeEntityId}}}",
                   Triggers         = new List<Hrot.Core.Mission.MissionTrigger>(),
                   State            = Hrot.Core.Mission.eTaskState.TASK_PLANNED,
               }
           }
       }
   };
   await _commandGateway!.SendMissionControlRequestAsync(missionCmd);
   ```
5. Send ack via `_mapCommandController` or the ack callback

Also update `ParseCommandAndActivatePersonalRoute` — the cancel path writes to `_mapCommandAckWriter`.
Change `if (_mapCommandAckWriter != null) _mapCommandAckWriter.Write(new MapCommandAck {...})` to
`_mapCommandController?.SendCancelledAck(requestId)` or inline via the ack callback.

**IMPORTANT:** The ack for the cancel path must go through the same callback used by `MapCommandController`.
Check how `MapCommandController.StatusCancelled` acks are sent and reuse the same pattern.

### Task 4: Update DrawPersonalRouteCommandTests

**File:** `Hrot.IG.Tests/CommandHandling/DrawPersonalRouteCommandTests.cs` (UPDATE)

Replace `MockGateway : INedCommandGateway` with a mock that implements `IIgNetworkAdapter`.

The test needs to:
1. Verify `CreateRouteEntityAsync` is called with the correct parameters
2. Verify the mission command is sent after successful entity creation
3. Verify that failure (entityId <= 0) prevents mission from being sent

New mock structure:
```csharp
private sealed class MockNetworkAdapter : Hrot.Core.Network.IIgNetworkAdapter
{
    // Route creation tracking
    public int CreateRouteCallCount { get; private set; }
    public long LastTkbType { get; private set; }
    public IReadOnlyList<(double, double, double)>? LastWaypoints { get; private set; }
    public int LastCommanderEntityId { get; private set; }
    public int CreateRouteReturnId { get; set; } = 77;

    // Mission tracking via CommandGateway mock
    public List<MissionControlCommand> MissionCalls { get; } = new();

    // ICommandGateway stub for mission calls
    private sealed class StubCommandGateway : Hrot.Core.Network.ICommandGateway
    {
        public List<MissionControlCommand> MissionCalls { get; } = new();
        public Task<int> CreateEntityAsync(CreateEntityCommand cmd, CancellationToken ct = default) => Task.FromResult(0);
        public Task SendUpdateDescriptorAsync(UpdateEntityDescriptorCommand cmd, CancellationToken ct = default) => Task.CompletedTask;
        public Task<MissionCommitResult> SendMissionControlRequestAsync(MissionControlCommand cmd, CancellationToken ct = default)
        {
            MissionCalls.Add(cmd);
            return Task.FromResult(new MissionCommitResult { Success = true });
        }
        public void Dispose() { }
    }

    private readonly StubCommandGateway _gateway = new();
    public ICommandGateway CommandGateway => _gateway;
    public List<MissionControlCommand> MissionCalls => _gateway.MissionCalls;

    public Task<int> CreateRouteEntityAsync(long tkbRouteType, IReadOnlyList<(double, double, double)> waypoints,
        double anchorLat, double anchorLon, double anchorAlt, int commanderEntityId, CancellationToken ct = default)
    {
        CreateRouteCallCount++;
        LastTkbType = tkbRouteType;
        LastWaypoints = waypoints;
        LastCommanderEntityId = commanderEntityId;
        return Task.FromResult(CreateRouteReturnId);
    }

    // All other methods: no-ops
    public void WriteMapClick(MapClickEventDto dto) { }
    public void WriteSelectionChanged(SelectionChangedEventDto dto) { }
    public void WriteMapCommandAck(MapCommandAckDto dto) { }
    public void WriteContextMenuRequest(Guid requestId, int mapId, IReadOnlyList<int> forSelection) { }
    public void PublishCapabilities(int mapId, string layerTreeJson, string configSchemasJson) { }
    public MapConfigDto? PollMapConfig() => null;
    public MapCommandDto? PollMapCommand() => null;
    public EntityLifecycleAckDto? PollEntityLifecycleAck() => null;
    public void Dispose() { }
}
```

Inject via a new `TestHook_SetNetworkAdapter(IIgNetworkAdapter adapter)` on IgApplication (see Task 5).

Update test assertions to check `MockNetworkAdapter.CreateRouteCallCount`, `LastWaypoints.Count`,
and `MissionCalls[0].Plan.Tasks[0].BehaviorId == "FollowRoute"`.

### Task 5: Add TestHook_SetNetworkAdapter to IgApplication

**File:** `Hrot.IG/IgApplication.cs` (UPDATE)

Add:
```csharp
/// <summary>
/// Test hook: injects a mock network adapter so unit tests can verify
/// IgApplication behaviour without a live DDS participant.
/// Also enables network-dependent code paths (sets _networkEnabled = true).
/// Must be called after InitializeEmbedded.
/// </summary>
internal void TestHook_SetNetworkAdapter(Hrot.Core.Network.IIgNetworkAdapter adapter)
{
    _networkAdapter = adapter;
    _commandGateway = adapter.CommandGateway as NedCommandGateway;
    _commandGatewayInterface = _commandGateway;
    _networkEnabled = true;
}
```

**Note:** `_commandGateway` and `_commandGatewayInterface` can stay as-is since the mock provides
a `CommandGateway` property. The `SendGeoSpatialUpdate` method still uses `_commandGatewayInterface`
for the NED-specific path — that's OK for now (Task 6 below will fix it).

### Task 6: Fix SendGeoSpatialUpdate to use neutral path — DEBT-008

**File:** `Hrot.IG/IgApplication.cs` (UPDATE) + `Hrot.Network.NED/ExCon/NedTranslationHelper.cs` (UPDATE)

**Step 1:** Change `SendGeoSpatialUpdate` to use the neutral `_commandGateway` path:

OLD:
```csharp
var request = new UpdateEntityDescriptorRequest { ... WorldPos payload ... };
_commandGatewayInterface.SendUpdateDescriptor(request);
```

NEW:
```csharp
if (_commandGateway == null) return;
var descJson = System.Text.Json.JsonSerializer.Serialize(new
{
    type     = "WorldPos",
    entityId = (int)netId,
    lat,
    lon,
    alt,
    time     = DateTime.UtcNow.Ticks,
});
var cmd = new Hrot.Core.Network.UpdateEntityDescriptorCommand
{
    EntityId       = (int)netId,
    DescriptorJson = descJson,
    BaseVersion    = 0,
};
_ = _commandGateway.SendUpdateDescriptorAsync(cmd);
```

**Step 2:** In `NedTranslationHelper.ToUpdateDescriptorRequest`, parse the JSON to build the `WorldPos` payload:

```csharp
public static UpdateEntityDescriptorRequest ToUpdateDescriptorRequest(
    UpdateEntityDescriptorCommand cmd)
{
    var payload = new EntityDescriptorUnion();

    if (!string.IsNullOrEmpty(cmd.DescriptorJson))
    {
        try
        {
            using var doc  = System.Text.Json.JsonDocument.Parse(cmd.DescriptorJson);
            var       root = doc.RootElement;

            if (root.TryGetProperty("type", out var typeProp) &&
                typeProp.GetString() == "WorldPos")
            {
                double lat = root.TryGetProperty("lat", out var lp) ? lp.GetDouble() : 0;
                double lon = root.TryGetProperty("lon", out var lo) ? lo.GetDouble() : 0;
                double alt = root.TryGetProperty("alt", out var ap) ? ap.GetDouble() : 0;

                payload = new EntityDescriptorUnion
                {
                    _d       = EDescriptorType.dtWorldPos,
                    WorldPos = new WorldPos
                    {
                        EntityId = cmd.EntityId,
                        Time     = DateTime.UtcNow,
                        Pos      = new GeoPoint
                        {
                            Latitude  = lat,
                            Longitude = lon,
                            Altitude  = alt,
                        },
                        Ori = new EulerOri(),
                    },
                };
            }
        }
        catch
        {
            // Malformed JSON — send without payload (best-effort)
        }
    }

    return new UpdateEntityDescriptorRequest
    {
        RequestId      = Guid.NewGuid(),
        EntityId       = cmd.EntityId,
        DescriptorType = EDescriptorType.dtWorldPos,
        CurrentVersion = (int)cmd.BaseVersion,
        Payload        = payload,
    };
}
```

After this change, also remove `_commandGatewayInterface` field and all `INedCommandGateway` usages
from IgApplication.cs (it was only needed for `SendGeoSpatialUpdate`).

Update `TestHook_SetCommandGateway` to accept `ICommandGateway` instead of `INedCommandGateway`.

**ContinuousDragTests:** Update `TallyGateway` to implement `ICommandGateway` instead of `INedCommandGateway`:
- Remove `SendUpdateDescriptor(UpdateEntityDescriptorRequest)` — add `Task SendUpdateDescriptorAsync(UpdateEntityDescriptorCommand, CancellationToken)`
- Update call count to track `SendUpdateDescriptorAsync`

### Task 7: Remove NED Reference from Hrot.IG.csproj — Task 19 Completion

After all NED types are removed from IgApplication.cs:

```
dotnet remove Hrot.IG\Hrot.IG.csproj reference ..\Hrot.Network.NED\Hrot.Network.NED.csproj
dotnet build Hrot.IG\Hrot.IG.csproj  --> must be 0 errors
dotnet test Hrot.IG.Tests\Hrot.IG.Tests.csproj --> must be 0 failures
```

---

## Priority 2 — DEBT-012: Remove Unused DDS Writers from IgApplication

### Task 8: Remove _mapCommandAckWriter and _contextMenuRequestWriter

**File:** `Hrot.IG/IgApplication.cs` (UPDATE)

1. Remove field declarations:
   - `private DdsWriter<MapCommandAck>? _mapCommandAckWriter;`
   - `private DdsWriter<ContextMenuRequest>? _contextMenuRequestWriter;`

2. Remove initialization in `InitializeNetwork`:
   - `_mapCommandAckWriter    = new DdsWriter<MapCommandAck>(participant, "MapCommandAck");`
   - `_contextMenuRequestWriter = new DdsWriter<ContextMenuRequest>(participant, "ContextMenuRequest");`

3. Remove disposal in `Shutdown`:
   - `_mapCommandAckWriter?.Dispose();`
   - `_contextMenuRequestWriter?.Dispose();`
   - (Note: `_networkAdapter.Dispose()` already handles these resources through NedIgNetworkAdapter)

4. Check for any remaining references. If `ParseCommandAndActivatePersonalRoute` still
   writes to `_mapCommandAckWriter` for the cancellation path, update to use the
   `_mapCommandController` callback pattern (see Task 3 note about cancel acks).

---

## Priority 3 — TASK-P6-001: Integration Test Harness Update

### Task 9: Verify CgfHarness Uses MockNetworkFactory

**File:** `Hrot.ClusterRunner.Integration.Tests/CgfHarness.cs` (VERIFY/UPDATE)

Check if `CgfHarness` still passes `NedNetworkFactory` directly to CGF. After BATCH-16,
CGF no longer needs `Hrot.Network.NED` — verify the harness doesn't reference it.

If any integration tests in `Hrot.ClusterRunner.Integration.Tests` can use `MockNetworkFactory`
(pure domain tests without DDS), update them to do so. E2E DDS loopback tests may keep NED.

---

## Testing Requirements

After all tasks:

1. `dotnet build IOS-IG-SimHost.sln -v quiet` → 0 errors
2. `dotnet list Hrot.IG\Hrot.IG.csproj reference` → no `Hrot.Network.NED` entry
3. `dotnet test Hrot.IG.Tests\Hrot.IG.Tests.csproj` → 0 failures
4. `dotnet test Hrot.IG.Tests\Hrot.IG.Tests.csproj --filter "ContinuousDragTests"` → 0 failures
5. `dotnet test Hrot.IG.Tests\Hrot.IG.Tests.csproj --filter "DrawPersonalRouteCommandTests"` → 0 failures
6. `dotnet test Hrot.SimHost.Tests\Hrot.SimHost.Tests.csproj` → 0 failures
7. `dotnet test Hrot.Network.NED.Tests\Hrot.Network.NED.Tests.csproj` → 0 failures

---

## Report Requirements

Write report to `.dev/modular-2/reports/BATCH-17-REPORT.md`. Include:

1. Tasks completed and status
2. **Developer Insights:**
   - What issues were encountered?
   - What weak points were spotted in the codebase?
   - What design decisions were made beyond the spec?
3. Test outcomes (pass counts)
4. Any deferred items or new debt identified
