# BATCH-05 Instructions

**Assigned to:** Claude Sonnet 4.6
**Working directory:** `d:\Work\IOS-IG-SimHost-FDP-2`
**Scope:** Tasks CS020, CS021, CS024, CS025
**Depends on:** BATCH-04 complete (all BATCH-04 changes are already committed)

---

## Reference Documents

- **Task specs:** `.dev/commander-subordinates/TASK-DETAIL.md` — §CS020, §CS021, §CS024, §CS025
- **Design:** `.dev/commander-subordinates/DESIGN.md`
- **Dev workflow:** `.github/skills/dev-lead/SKILL.md`

---

## Current State

All previous batches (01–04) are committed. The following stubs exist and must be replaced:

- `EditorOrbatAdapter.RequestAssignSubordinate` / `RequestRemoveSubordinate` — log warn, do nothing
- `ExConOrbatAdapter.RequestAssignSubordinate` / `RequestRemoveSubordinate` — log warn, do nothing
- `ICommandGateway` — does NOT yet have `SendUpdateAttributeAsync`
- `UpdateEntityAttributeRequestSystem.ProcessRequest` — does NOT yet intercept `"CommanderId"` key

---

## Task CS020 — EditorOrbatAdapter Full Implementation

**File:** `Hrot/Subsystems/Hrot.Editor/Adapters/EditorOrbatAdapter.cs`

### Changes required

1. **`GetVisibleNodes`** — update the `CanAcceptSubordinates` field for each node:
   ```csharp
   CanAcceptSubordinates: _world.HasComponent<UnitRoster>(entity)
   ```
   (The `UnitRoster` component is in `Fdp.Core.CommandHierarchy` namespace, already imported.)

2. **`RequestAssignSubordinate(int subordinateEntityId, int commanderEntityId)`** — replace the stub:
   ```csharp
   public void RequestAssignSubordinate(int subordinateEntityId, int commanderEntityId)
   {
       if (!_indexCache.TryGetValue(subordinateEntityId, out var sub) ||
           !_indexCache.TryGetValue(commanderEntityId,   out var cmd))
       {
           FdpLog<EditorOrbatAdapter>.Warn(
               "[EditorOrbatAdapter] RequestAssignSubordinate: entity not in cache " +
               "(subordinate={0}, commander={1}).", subordinateEntityId, commanderEntityId);
           return;
       }

       _bus.Publish(new CmdAssignSubordinate
       {
           Subordinate = sub,
           Commander   = cmd,
           Designation = TacticalDesignation.Undefined,
       });
   }
   ```
   Add `using Fdp.Core.CommandHierarchy;` if not already present.

3. **`RequestRemoveSubordinate(int subordinateEntityId)`** — replace the stub:
   ```csharp
   public void RequestRemoveSubordinate(int subordinateEntityId)
   {
       if (!_indexCache.TryGetValue(subordinateEntityId, out var sub))
       {
           FdpLog<EditorOrbatAdapter>.Warn(
               "[EditorOrbatAdapter] RequestRemoveSubordinate: entity not in cache " +
               "(subordinate={0}).", subordinateEntityId);
           return;
       }

       _bus.Publish(new CmdRemoveSubordinate { Subordinate = sub });
   }
   ```

### Tests (add to `Hrot/Subsystems/Hrot.Editor.Tests/Adapters/AdapterTests.cs`)

Add 4 new test methods inside the existing `EditorOrbatAdapterTests` class:

**CS020-T01: Tree built from UnitSubordinate (CanAcceptSubordinates reflects UnitRoster)**
```csharp
[Fact]
public void GetVisibleNodes_CommanderWithRoster_CanAcceptSubordinatesTrue()
{
    _world.RegisterComponent<UnitRoster>();
    _world.CreateEntity(); // burn index 0
    var commander = _world.CreateEntity();
    _world.AddComponent(commander, new EntityInfo { Name = new Fdp.Core.FixedString64("CMD") });
    _world.AddComponent(commander, new UnitRoster());  // has roster → CanAcceptSubordinates = true

    var subordinate = _world.CreateEntity();
    _world.AddComponent(subordinate, new EntityInfo { Name = new Fdp.Core.FixedString64("SUB") });
    _world.AddComponent(subordinate, new UnitSubordinate { Commander = commander });

    var adapter = CreateAdapter();
    var nodes   = adapter.GetVisibleNodes("", new HashSet<int>());

    var cmdNode = nodes.Single(n => n.EntityId == commander.Index);
    var subNode = nodes.Single(n => n.EntityId == subordinate.Index);

    Assert.True(cmdNode.CanAcceptSubordinates);
    Assert.False(subNode.CanAcceptSubordinates);
    Assert.Equal(1, subNode.Depth);
}
```

**CS020-T02: RequestAssignSubordinate publishes CmdAssignSubordinate**
```csharp
[Fact]
public void RequestAssignSubordinate_ValidEntities_PublishesCmdAssignSubordinate()
{
    _world.RegisterEvent<CmdAssignSubordinate>();
    _world.CreateEntity(); // burn index 0
    var commander  = _world.CreateEntity();
    var subordinate = _world.CreateEntity();
    _world.AddComponent(commander,   new EntityInfo { Name = new Fdp.Core.FixedString64("CMD") });
    _world.AddComponent(subordinate, new EntityInfo { Name = new Fdp.Core.FixedString64("SUB") });

    var adapter = CreateAdapter();
    adapter.GetVisibleNodes("", new HashSet<int>()); // populate cache

    adapter.RequestAssignSubordinate(subordinate.Index, commander.Index);

    _bus.SwapBuffers();
    var events = _bus.Read<CmdAssignSubordinate>().ToArray();
    Assert.Single(events);
    Assert.Equal(subordinate, events[0].Subordinate);
    Assert.Equal(commander,   events[0].Commander);
}
```

**CS020-T03: RequestAssignSubordinate — unknown entity logs warning, no event**
```csharp
[Fact]
public void RequestAssignSubordinate_UnknownEntity_DoesNotThrow()
{
    _world.RegisterEvent<CmdAssignSubordinate>();
    var adapter = CreateAdapter();

    // No exception; no event.
    adapter.RequestAssignSubordinate(999, 888);

    _bus.SwapBuffers();
    Assert.Empty(_bus.Read<CmdAssignSubordinate>().ToArray());
}
```

**CS020-T04: RequestRemoveSubordinate publishes CmdRemoveSubordinate**
```csharp
[Fact]
public void RequestRemoveSubordinate_ValidEntity_PublishesCmdRemoveSubordinate()
{
    _world.RegisterEvent<CmdRemoveSubordinate>();
    _world.CreateEntity(); // burn index 0
    var entity = _world.CreateEntity();
    _world.AddComponent(entity, new EntityInfo { Name = new Fdp.Core.FixedString64("SUB") });

    var adapter = CreateAdapter();
    adapter.GetVisibleNodes("", new HashSet<int>()); // populate cache

    adapter.RequestRemoveSubordinate(entity.Index);

    _bus.SwapBuffers();
    var events = _bus.Read<CmdRemoveSubordinate>().ToArray();
    Assert.Single(events);
    Assert.Equal(entity, events[0].Subordinate);
}
```

**Required usings in AdapterTests.cs** (add if not already present):
```csharp
using Fdp.Core.CommandHierarchy;
```

---

## Task CS021 — ExConOrbatAdapter Full Implementation

CS021 requires THREE changes: extending `ICommandGateway`, implementing it in `NedCommandGateway`, and wiring `ExConOrbatAdapter`.

### Step 1 — Extend `ICommandGateway` interface

**File:** `Hrot/Engine/Hrot.Core/Network/ICommandGateway.cs`

Add after `SendUpdateDescriptorAsync`:
```csharp
/// <summary>
/// Sends an attribute-level JSON patch request for a specific entity.
/// Used by the ExCon to push <c>CommanderId</c> hierarchy changes over DDS.
/// </summary>
Task SendUpdateAttributeAsync(UpdateEntityAttributeCommand cmd, CancellationToken ct = default);
```

Add `using Fdp.Toolkit.Replication.Events;` at the top.

### Step 2 — Implement in `NedCommandGateway`

**File:** `Hrot/Network/Hrot.Network.NED/Commands/NedCommandGateway.cs`

1. Add a `DdsWriter<UpdateEntityAttributeRequest> _attributeWriter` field.
2. In the constructor, after `_updateWriter = new DdsWriter<...>("UpdateEntityDescriptorRequest")`:
   ```csharp
   _attributeWriter = new DdsWriter<UpdateEntityAttributeRequest>(participant, "UpdateEntityAttributeRequest");
   ```
3. Add the implementation of `ICommandGateway.SendUpdateAttributeAsync`:
   ```csharp
   Task ICommandGateway.SendUpdateAttributeAsync(UpdateEntityAttributeCommand cmd, CancellationToken ct)
   {
       _attributeWriter.Write(new UpdateEntityAttributeRequest
       {
           RequestId          = Guid.NewGuid(),
           EntityId           = (int)cmd.NetworkId,
           AttributePatchJson = cmd.AttributePatchJson,
           RequireAck         = false,
       });
       return Task.CompletedTask;
   }
   ```
4. Dispose `_attributeWriter` in `Dispose()`.

### Step 3 — Add stub to `NullCommandGateway` in `NedNetworkFactory.cs`

**File:** `Hrot/Network/Hrot.Network.NED/Factory/NedNetworkFactory.cs`

In `internal sealed class NullCommandGateway : ICommandGateway` (at bottom of file), add:
```csharp
public Task SendUpdateAttributeAsync(UpdateEntityAttributeCommand cmd, CancellationToken ct = default)
    => Task.CompletedTask;
```

### Step 4 — Add stub to `NullCommandGateway` in `ExConSubsystem.cs`

**File:** `Hrot/Subsystems/Hrot.ExCon/ExConSubsystem.cs`

Find `internal sealed class NullCommandGateway : ICommandGateway` and add:
```csharp
public Task SendUpdateAttributeAsync(UpdateEntityAttributeCommand cmd, CancellationToken ct = default)
    => Task.CompletedTask;
```
Add `using Fdp.Toolkit.Replication.Events;` if needed.

### Step 5 — Add stub to `BdcNullCommandGateway` in `BdcNetworkFactory.cs`

**File:** `Hrot/Network/Hrot.Network.BDC/Factory/BdcNetworkFactory.cs`

Find `internal sealed class BdcNullCommandGateway : ICommandGateway` and add:
```csharp
public Task SendUpdateAttributeAsync(UpdateEntityAttributeCommand cmd, CancellationToken ct = default)
    => Task.CompletedTask;
```

### Step 6 — Update `ExConOrbatAdapter`

**File:** `Hrot/Subsystems/Hrot.ExCon/Adapters/ExConOrbatAdapter.cs`

1. Add `ICommandGateway _gateway` field and inject via constructor:
   ```csharp
   private readonly ICommandGateway _gateway;

   public ExConOrbatAdapter(IDerRepo repo, IExConLogic logic, ICommandGateway gateway)
   {
       _repo    = repo     ?? throw new ArgumentNullException(nameof(repo));
       _logic   = logic    ?? throw new ArgumentNullException(nameof(logic));
       _gateway = gateway  ?? throw new ArgumentNullException(nameof(gateway));
   }
   ```
   Add `using Hrot.Core.Network;` and `using Fdp.Toolkit.Replication.Events;`.

2. **`GetVisibleNodes`** — update `CanAcceptSubordinates` per entity:
   Replace:
   ```csharp
   CanAcceptSubordinates: false
   ```
   With:
   ```csharp
   CanAcceptSubordinates: IsCompositeType(entity.GetDescriptor<EntityInfoDescriptor>().TkbType)
   ```
   Add the helper method:
   ```csharp
   private static bool IsCompositeType(long tkbType)
   {
       // 0 = unknown/not yet resolved; treat as non-composite.
       return tkbType != 0;
   }
   ```
   *(The simplest correct implementation; `TkbType != 0` means a typed entity which can be a
   commander. Refine later with a catalog lookup when the TKB catalog is available.)*

3. **`RequestAssignSubordinate`** — replace stub:
   ```csharp
   public void RequestAssignSubordinate(int subordinateEntityId, int commanderEntityId)
   {
       _ = _gateway.SendUpdateAttributeAsync(new UpdateEntityAttributeCommand
       {
           NetworkId          = subordinateEntityId,
           AttributePatchJson = $"{{\"CommanderId\":{commanderEntityId}}}",
       });
   }
   ```

4. **`RequestRemoveSubordinate`** — replace stub:
   ```csharp
   public void RequestRemoveSubordinate(int subordinateEntityId)
   {
       _ = _gateway.SendUpdateAttributeAsync(new UpdateEntityAttributeCommand
       {
           NetworkId          = subordinateEntityId,
           AttributePatchJson = "{\"CommanderId\":0}",
       });
   }
   ```

### Step 7 — Wire `ICommandGateway` into `ExConOrbatAdapter` construction sites

Search for all `new ExConOrbatAdapter(` usages and pass the gateway. Likely in
`ExConSubsystem.cs`. Check with:
```
grep_search "new ExConOrbatAdapter"
```

### Tests (in `Hrot/Subsystems/Hrot.ExCon.Tests/SharedOrbatPanelTests.cs` or a new file)

Create new file `Hrot/Subsystems/Hrot.ExCon.Tests/ExConOrbatAdapterTests.cs`:

**CS021-T01: RequestAssignSubordinate calls SendUpdateAttributeAsync (not SendUpdateDescriptorAsync)**
**CS021-T02: RequestRemoveSubordinate sends CommanderId=0 via attribute channel**
**CS021-T03: CanAcceptSubordinates = true when TkbType != 0**

Use Moq to mock `ICommandGateway` and `IDerRepo`. The `ExConOrbatAdapter` tests project
already has Moq — check existing `SharedOrbatPanelTests.cs` for the pattern.

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Toolkit.DER;
using Fdp.Toolkit.Replication.Events;
using Hrot.Core.Network;
using Hrot.ExCon.Adapters;
using Moq;
using Xunit;

namespace Hrot.ExCon.Tests;

public sealed class ExConOrbatAdapterTests
{
    private Mock<IDerRepo>        _repo    = new();
    private Mock<IExConLogic>     _logic   = new();
    private Mock<ICommandGateway> _gateway = new();

    private ExConOrbatAdapter CreateAdapter()
        => new ExConOrbatAdapter(_repo.Object, _logic.Object, _gateway.Object);

    // CS021-T01
    [Fact]
    public void RequestAssignSubordinate_CallsSendUpdateAttributeAsync_WithCommanderIdPatch()
    {
        UpdateEntityAttributeCommand? captured = null;
        _gateway
            .Setup(g => g.SendUpdateAttributeAsync(It.IsAny<UpdateEntityAttributeCommand>(), It.IsAny<CancellationToken>()))
            .Callback<UpdateEntityAttributeCommand, CancellationToken>((cmd, _) => captured = cmd)
            .Returns(Task.CompletedTask);

        var adapter = CreateAdapter();
        adapter.RequestAssignSubordinate(subordinateEntityId: 10, commanderEntityId: 5);

        _gateway.Verify(g => g.SendUpdateAttributeAsync(It.IsAny<UpdateEntityAttributeCommand>(), It.IsAny<CancellationToken>()), Times.Once);
        _gateway.Verify(g => g.SendUpdateDescriptorAsync(It.IsAny<UpdateEntityDescriptorCommand>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.NotNull(captured);
        Assert.Equal(10, (int)captured!.NetworkId);
        Assert.Contains("\"CommanderId\":5", captured.AttributePatchJson);
    }

    // CS021-T02
    [Fact]
    public void RequestRemoveSubordinate_CallsSendUpdateAttributeAsync_WithCommanderIdZero()
    {
        UpdateEntityAttributeCommand? captured = null;
        _gateway
            .Setup(g => g.SendUpdateAttributeAsync(It.IsAny<UpdateEntityAttributeCommand>(), It.IsAny<CancellationToken>()))
            .Callback<UpdateEntityAttributeCommand, CancellationToken>((cmd, _) => captured = cmd)
            .Returns(Task.CompletedTask);

        var adapter = CreateAdapter();
        adapter.RequestRemoveSubordinate(subordinateEntityId: 10);

        Assert.NotNull(captured);
        Assert.Equal(10, (int)captured!.NetworkId);
        Assert.Contains("\"CommanderId\":0", captured.AttributePatchJson);
    }
}
```

---

## Task CS024 — UpdateEntityAttributeRequestSystem: CommanderId Pre-Intercept

**File:** `Hrot/Network/Hrot.Network.NED/Systems/UpdateEntityAttributeRequestSystem.cs`

This system has the `ProcessRequest` private method. CS024 modifies the JSON path (step 4 in
the method: after resolving the entity and checking for binary records, before compiling JSON).

### Changes to `ProcessRequest`

Inject `_entityMap` is already a field. The system also has access to `repo.Bus` after
`(EntityRepository)view` is cast. The bus is accessed as `repo.Bus`.

**Add `using System.Text.Json;` at the top of the file** (if not already present).

The `ProcessRequest` method currently at step 4 builds `context = _jsonCompiler.CreatePatchContext(repo, entity)`.

Modify to intercept `"CommanderId"` **before** step 4:

```csharp
// 3a. Pre-intercept "CommanderId" from the JSON patch.
//     This key was removed from EntityInfo (CS009) so the reflection compiler
//     would silently drop it. We must handle it explicitly.
bool commanderIntercepted = false;
if (!string.IsNullOrEmpty(req.AttributePatchJson))
{
    req.AttributePatchJson = InterceptCommanderId(
        req.AttributePatchJson, req.EntityId, entity, repo,
        out commanderIntercepted);
}
```

Then after step 7 (SILENT BYSTANDER RULE):
```csharp
// If we intercepted CommanderId but the JSON had no other keys, still send ACK.
if (!context.HasAppliedAny && !commanderIntercepted)
    return;

if (!context.HasAppliedAny && commanderIntercepted)
{
    if (req.RequireAck)
        _ackSink.WriteAck(req.RequestId, (int)NedStatusCode.Success, _localNodeId, ReadOnlySpan<byte>.Empty);
    return;
}
```

Add the helper method `InterceptCommanderId`:

```csharp
/// <summary>
/// Scans <paramref name="json"/> for a "CommanderId" property.
/// If found: resolves the entity, checks authority, publishes
/// <see cref="CmdAssignSubordinate"/> or <see cref="CmdRemoveSubordinate"/>, then returns
/// a sanitized JSON string without the "CommanderId" key.
/// </summary>
private string InterceptCommanderId(
    string json, int entityNetId, Entity entity, EntityRepository repo,
    out bool intercepted)
{
    intercepted = false;
    try
    {
        using var doc  = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("CommanderId", out var cmdIdProp))
            return json;

        intercepted = true;
        long commanderNetId = cmdIdProp.GetInt64();

        if (commanderNetId != 0)
        {
            if (_entityMap.TryGetEntity(commanderNetId, out var commander))
            {
                repo.Bus.Publish(new CmdAssignSubordinate
                {
                    Subordinate = entity,
                    Commander   = commander,
                    Designation = TacticalDesignation.Undefined,
                });
            }
            else
            {
                FdpLog<UpdateEntityAttributeRequestSystem>.Warn(
                    "[UpdAttrReq] CommanderId {0} not found in entity map for entity {1}.",
                    commanderNetId, entityNetId);
            }
        }
        else
        {
            // Zero = remove subordination.
            if (repo.HasComponent<UnitSubordinate>(entity))
                repo.Bus.Publish(new CmdRemoveSubordinate { Subordinate = entity });
        }

        // Rebuild JSON without "CommanderId".
        return RebuildJsonWithout(root, "CommanderId");
    }
    catch (JsonException ex)
    {
        FdpLog<UpdateEntityAttributeRequestSystem>.Warn(
            "[UpdAttrReq] Failed to parse AttributePatchJson for CommanderId intercept: {0}", ex.Message);
        return json;
    }
}

private static string RebuildJsonWithout(JsonElement root, string excludeProperty)
{
    using var ms = new System.IO.MemoryStream();
    using (var writer = new Utf8JsonWriter(ms))
    {
        writer.WriteStartObject();
        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Name == excludeProperty) continue;
            prop.WriteTo(writer);
        }
        writer.WriteEndObject();
    }
    return System.Text.Encoding.UTF8.GetString(ms.ToArray());
}
```

**Add usings at top of file:**
```csharp
using System.Text.Json;
using System.IO;
using System.Text;
using Fdp.Core.CommandHierarchy;
```

**IMPORTANT:** The authority check is already done implicitly — `ProcessRequest` only reaches this
code after `TryGetEntity` succeeds for the target entity. The commander assignment itself does
not require an authority check beyond that because `CmdAssignSubordinate` is consumed by
`UnitHierarchySystem` which checks component ownership before writing.

Also note: the `req` parameter is a struct passed by value in the callback lambda — to mutate
`req.AttributePatchJson` you need to keep it as a local variable in `ProcessRequest`. The current
code already does this via the callback `req => ProcessRequest(req, ...)` so `req` is a copy.

**Fix the bystander rule integration** — the current code returns at step 7 if `!context.HasAppliedAny`.
The commanderIntercepted variable must be declared BEFORE context is created. The final
`ProcessRequest` flow for the JSON path should be:

```
[step 3 - null compiler check]
[step 3a - CommanderId intercept → sets commanderIntercepted, sanitizes json]
[step 4 - create context]
[step 5 - compile sanitized json]
[step 6 - flush dirty marks]
[step 7 - bystander rule: if !HasAppliedAny && !commanderIntercepted → return]
[step 7a - if !HasAppliedAny && commanderIntercepted → send ACK with empty mask, return]
[step 8 - opt-in ACK with mutation bitmask]
```

### Tests (new file: `Hrot/Network/Hrot.Network.NED.Tests/UpdateEntityAttributeCommanderIdTests.cs`)

```csharp
using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Hrot.Map.Common.Systems;
using Hrot.NED.Messages;
using Xunit;

namespace Hrot.Network.NED.Tests;

public sealed class UpdateEntityAttributeCommanderIdTests
{
    // ... test fixture with EntityRepository, NetworkEntityMap stubs, etc.
}
```

**CS024-T01: Assign patch routes to CmdAssignSubordinate event**
Setup:
- `EntityRepository` with `CmdAssignSubordinate` registered
- Target entity in repo; commander entity in repo and in `NetworkEntityMap` with net ID 42
- Create `UpdateEntityAttributeRequestSystem` with stub `IUpdateEntityAttributeRequestSource`,
  `IUpdateEntityAttributeAckSink`, the `NetworkEntityMap`, and NO `JsonAttributeCompiler` (null)
- Invoke `ProcessRequest` via `Execute(view, 0f)` after queuing `req` in the stub source with
  `AttributePatchJson = "{\"CommanderId\":42}"`, `RequireAck = false`

Assert:
- `repo.Bus.SwapBuffers()`
- `repo.Bus.Read<CmdAssignSubordinate>()` contains one event with `Subordinate = target` and
  `Commander = commander`

**CS024-T02: Remove patch routes to CmdRemoveSubordinate**
Setup: target entity with `UnitSubordinate` component; `AttributePatchJson = "{\"CommanderId\":0}"`
Assert: `CmdRemoveSubordinate` published for the target entity.

**CS024-T03: Remove patch on entity without UnitSubordinate — no event**
Setup: target entity without `UnitSubordinate`; `AttributePatchJson = "{\"CommanderId\":0}"`
Assert: no `CmdRemoveSubordinate` published.

**CS024-T04: Other keys unaffected by intercept**
Setup: JSON compiler that tracks calls; `AttributePatchJson = "{\"Name\":\"Bravo\",\"CommanderId\":0}"`
Assert: the JSON forwarded to the compiler does NOT contain `"CommanderId"`, but does contain
`"Name"`.

**CS024-T05: ACK sent when only CommanderId was in the patch**
Setup: `RequireAck = true`; `AttributePatchJson = "{\"CommanderId\":42}"` (no other keys).
Assert: `IUpdateEntityAttributeAckSink.WriteAck` is called (not `WriteErrorAck`).

Use the existing test infrastructure in `Hrot.Network.NED.Tests` for stubs. Look at
`AttributeRecordTests.cs` and `GenericMessageFieldTests.cs` for patterns.

The `IUpdateEntityAttributeRequestSource` and `IUpdateEntityAttributeAckSink` interfaces are
in `Hrot.Network.NED`. Use simple in-memory stubs.

---

## Task CS025 — Integration Tests: Distributed Boundary Validation

**IMPORTANT:** CS025 covers `HrotRunnerHarness`-based integration tests. These are in
`Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/`. Before writing these tests:

1. Read `TASK-DETAIL.md §CS025` carefully for the 6 success conditions.
2. Look at existing integration tests in `Hrot.ClusterRunner.Integration.Tests/` for the harness API
   pattern (`CgfSubsystemHeadlessTests.cs`, `AllSubsystemsClusterTransitionTests.cs`).
3. Note that tests in this assembly are often marked `[Trait("Category","Integration")]` and
   some are `[Skip]`-ed for DDS requirements. Follow the existing patterns.

**For BATCH-05, only implement CS025 success conditions 2 and 6** (the simpler EditorHarness-based
ones that don't require live DDS):

**CS025-T02: Atomic capacity validation (EditorHarness)**
Create test file: `Hrot/Subsystems/Hrot.SimHost.Tests/Integration/HierarchyCapacityIntegrationTests.cs`

```
Setup: EntityRepository; register UnitRoster, UnitSubordinate, CmdAssignSubordinate events.
Create 1 commander with UnitRoster.
Create 17 subordinate entities.
Publish 17 CmdAssignSubordinate events.
Run UnitHierarchySystem.Execute (from Hrot.Common.Systems).
Assert: commander UnitRoster.Count == 16.
Assert: the 17th subordinate has no UnitSubordinate.
Assert: CmdAssignSubordinateRejected event published for the 17th entity.
```

**CS025-T06: Genesis scenario serialization (EditorHarness)**
Create test file: `Hrot/Subsystems/Hrot.SimHost.Tests/Integration/HierarchySerializationIntegrationTests.cs`

```
Setup: EntityRepository with UnitRoster, UnitSubordinate, UnitSubordinateTranslator registered.
Create commander + 1 subordinate with UnitSubordinate.
Serialize via HrotScenarioSerializerFactory.
Create new EntityRepository (simulating reload).
Deserialize — subordinate should have InitialUnitSubordinateIntent.
Register commander in NetworkEntityMap.
Run GenesisMaterializationSystem.Execute.
Assert: UnitSubordinate is reconstituted with correct Commander entity.
Assert: InitialUnitSubordinateIntent is removed.
```

---

## Build and Test Verification

After implementing all changes, run:

```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build IOS-IG-SimHost.sln --no-restore -v quiet
```

Verify: `Build succeeded. 0 Error(s)`

Then run targeted tests:

```powershell
dotnet test "Hrot\Subsystems\Hrot.Editor.Tests\Hrot.Editor.Tests.csproj" --no-build --nologo
dotnet test "Hrot\Subsystems\Hrot.ExCon.Tests\Hrot.ExCon.Tests.csproj" --no-build --nologo
dotnet test "Hrot\Network\Hrot.Network.NED.Tests\Hrot.Network.NED.Tests.csproj" --no-build --nologo
dotnet test "Hrot\Subsystems\Hrot.SimHost.Tests\Hrot.SimHost.Tests.csproj" --no-build --nologo
```

All new tests must pass (0 failures in new test methods).

---

## Post-Implementation Workflow

After all tests pass:

1. Create `.dev/commander-subordinates/reports/BATCH-05-REPORT.md`
2. Create `.dev/commander-subordinates/reviews/BATCH-05-REVIEW.md`
3. Update `.dev/commander-subordinates/TASK-TRACKER.md` marking CS020, CS021, CS024, CS025 as `[x]`
4. Run `git add -A` then `git commit -m "BATCH-05: CS020/CS021 ORBAT adapters, CS024 CommanderId intercept, CS025 integration tests"`

---

## Technical Context (Do Not Duplicate in Code)

- `TacticalDesignation` enum values: `Undefined=0, Commander=1, SquadLeader=2, Wingman=3, Support=4`
- `Bus.Read<T>()` is NON-DRAINING — call `Bus.SwapBuffers()` first in tests
- `RegisterManagedComponent<T>()` MUST be called before `SetManagedComponent<T>()`
- `EntityRepository.CreateEntity()` defaults to `EntityLifecycle.Active`
- `NetworkEntityMap` is in `Fdp.Toolkit.Replication.Services` namespace
- `UnitRoster.Capacity = 16`; `UnitSubordinate.Commander` is an `Entity`
- The ExCon has NO ECS — never import `Fdp.Core` into `Hrot.ExCon` project
