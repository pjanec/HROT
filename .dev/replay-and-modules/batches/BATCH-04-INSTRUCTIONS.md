# BATCH-04 Implementation Instructions
**Tasks**: T-RMF-20, T-RMF-21, T-RMF-22, T-RMF-23  
**Phase**: Phase 4 — Deep replay architecture fixes  
**Agent**: Claude Sonnet 4.6

---

## Overview

This batch fixes the replay architecture so that:
- NetworkLifecycleSystemGroup gates ALL network lifecycle systems (T-RMF-20)
- GlobalTime is not pushed by the kernel during replay (T-RMF-21)
- After a seek, all egress dirty state is flushed (T-RMF-22)
- After a seek, CycloneNetworkCleanupSystem tracking is reset (T-RMF-23)

**FDP changes must be committed to the FDP submodule first**, then the main repo commit.

---

## T-RMF-20: Move GhostDestructionSystem + DeferredTakeoverSystem into NetworkLifecycleSystemGroup

### File: `Hrot/Network/Hrot.Network.NED/Replication/NedReplicationModule.cs`

#### Change 1 — Constructor: build lifecycle inner-systems list conditionally

Read the constructor. Find the two lines:
```csharp
GhostCreationSystem   = new GhostCreationSystem(entityMap);
NetworkLifecycleGroup = new NetworkLifecycleSystemGroup(GhostCreationSystem);
```

Replace those two lines with:
```csharp
GhostCreationSystem = new GhostCreationSystem(entityMap);

var lifecycleInnerSystems = new List<IEcsModuleSystem> { GhostCreationSystem };
if (_roleHasBrain && !_roleHasMuscle && !_roleHasIG)
    lifecycleInnerSystems.Add(new GhostDestructionSystem(_entityMap));
if (_roleHasMuscle)
    lifecycleInnerSystems.Add(new DeferredTakeoverSystem(_entityMap, _localNodeId, _descriptorOwnershipMap, _tkbDb));
NetworkLifecycleGroup = new NetworkLifecycleSystemGroup(lifecycleInnerSystems.ToArray());
```

Add `using System.Collections.Generic;` at the top if not already present (it should be since the file already uses `List<T>`).

#### Change 2 — `RegisterSystems`: remove standalone registrations + add field + property

At the top of the class (in the fields section), add this private field:
```csharp
private CycloneNetworkCleanupSystem? _cleanupSystem;
```

In `RegisterSystems`, find the block for `pureBrainRole` that contains:
```csharp
registry.RegisterSystem(new GhostDestructionSystem(_entityMap));
```
**Remove that line** (do NOT remove the surrounding `if (pureBrainRole)` block — only the `GhostDestructionSystem` registration line inside it).

In `RegisterSystems`, find the block for `_roleHasMuscle` that contains:
```csharp
registry.RegisterSystem(new DeferredTakeoverSystem(_entityMap, _localNodeId, _descriptorOwnershipMap, _tkbDb));
```
**Remove that line** (do NOT remove the surrounding `if (_roleHasMuscle)` block — only the `DeferredTakeoverSystem` registration line inside it).

Still in `RegisterSystems`, find the line that registers `CycloneNetworkCleanupSystem`:
```csharp
registry.RegisterSystem(new CycloneNetworkCleanupSystem(allCleanupTranslators));
```
Replace it with:
```csharp
_cleanupSystem = new CycloneNetworkCleanupSystem(allCleanupTranslators);
registry.RegisterSystem(_cleanupSystem);
```

After all fields/properties, add a public property:
```csharp
/// <summary>Exposes the <see cref="CycloneNetworkCleanupSystem"/> for composition-root afterSeek wiring.</summary>
public CycloneNetworkCleanupSystem? CleanupSystem => _cleanupSystem;
```

> **Important**: Do NOT remove the standalone `registry.RegisterSystem(new OwnershipIngressSystem(...))` in the `pureBrainRole` block. Only remove the two system-move lines.

---

## T-RMF-21: Fix GlobalTime Tug-of-War

### File 1: `FDP/Engine/Fdp.ModuleHost/ModuleHostKernel.cs`

Read the file to understand it fully. Then make three changes:

#### Change 1 — Add suppression field

In the private fields section (near other private backing fields), add:
```csharp
private volatile bool _globalTimePushSuspended;
```

#### Change 2 — Add public suspend/resume methods

Add the following two public methods in the public API section (near `SetTimeController` or similar kernel-control methods):
```csharp
/// <summary>
/// Prevents <see cref="UpdateInternal"/> from overwriting the ECS world's
/// simulation time while replay is active (the playback system owns time during replay).
/// </summary>
public void SuspendGlobalTimePush() => _globalTimePushSuspended = true;

/// <summary>Resumes normal simulation-time propagation after replay ends.</summary>
public void ResumeGlobalTimePush() => _globalTimePushSuspended = false;
```

#### Change 3 — Wrap time push in `UpdateInternal`

In `UpdateInternal`, find the block that calls `SetSimulationTime` and `SetSingletonUnmanaged`. It looks like:
```csharp
_liveWorld.Tick();
_liveWorld.SetSimulationTime((float)globalTime.TotalTime);
_liveWorld.SetSingletonUnmanaged(globalTime);
```
Replace it with:
```csharp
_liveWorld.Tick();
if (!_globalTimePushSuspended)
{
    _liveWorld.SetSimulationTime((float)globalTime.TotalTime);
    _liveWorld.SetSingletonUnmanaged(globalTime);
}
```

> `_liveWorld.Tick()` must remain OUTSIDE the guard — it always runs.
> `CurrentTime = globalTime;` and `_currentFrame = ...` assignments (if present immediately after) also remain outside the guard.

---

### File 2: `FDP/Toolkits/Fdp.Toolkits/Orchestration/Handlers/ReferenceReplayLoadHandler.cs`

Read the file fully to understand the constructor signature and `Commit` method structure.

#### Change 1 — Add private fields

In the private fields section, add:
```csharp
private readonly Action? _suspendGlobalTimePush;
private readonly Action? _resumeGlobalTimePush;
```

#### Change 2 — Update constructor signature

The current constructor ends with a parameter like `string storageDirectory`. Add two optional parameters at the end:
```csharp
Action? suspendGlobalTimePush = null,
Action? resumeGlobalTimePush = null
```

In the constructor body, assign them:
```csharp
_suspendGlobalTimePush = suspendGlobalTimePush;
_resumeGlobalTimePush  = resumeGlobalTimePush;
```

#### Change 3 — Invoke in `Commit(PrepareReplay ...)`

In the `Commit` handler for `PrepareReplay` (where `SetSystemsEnabled(false)` is called), add after that call:
```csharp
_suspendGlobalTimePush?.Invoke();
```

#### Change 4 — Invoke in `Commit(FinalizeReplay ...)` and `Commit(PrepareLive ...)`

In the `Commit` handler for `FinalizeReplay` (where `SetSystemsEnabled(true)` is called), add after that call:
```csharp
_resumeGlobalTimePush?.Invoke();
```

In the `Commit` handler for `PrepareLive` (where `SetSystemsEnabled(true)` is called), add after that call:
```csharp
_resumeGlobalTimePush?.Invoke();
```

---

### T-RMF-21 Composition root wiring

#### File 3: `Hrot/Subsystems/Hrot.SimHost/NodeBootstrapper.cs`

Read `BuildOrchestration`. Find where `new ReferenceReplayLoadHandler(...)` is constructed. The current call will have parameters ending with `storageDirectory: localTempRoot` or similar.

Add two named arguments at the end:
```csharp
suspendGlobalTimePush: kernel.SuspendGlobalTimePush,
resumeGlobalTimePush:  kernel.ResumeGlobalTimePush
```

#### File 4: `Hrot/Subsystems/Hrot.CGF/CgfApplication.cs`

Find where `new ReferenceReplayLoadHandler(...)` is constructed (around line 165). Add the same two named arguments at the end:
```csharp
suspendGlobalTimePush: _kernel.SuspendGlobalTimePush,
resumeGlobalTimePush:  _kernel.ResumeGlobalTimePush
```

#### File 5: `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs`

Find where `new ReferenceReplayLoadHandler(...)` is constructed (around line 323). Add the same two named arguments at the end:
```csharp
suspendGlobalTimePush: _context.Kernel.SuspendGlobalTimePush,
resumeGlobalTimePush:  _context.Kernel.ResumeGlobalTimePush
```

---

## T-RMF-22: SmartEgressSystem seek lag fix

### File: `FDP/Toolkits/Fdp.Toolkits/Replication/Utilities/SmartEgressUtil.cs`

Read the file to understand the existing `ShouldPublish`, `MarkPublished`, and `MarkDirty` methods and the `EgressPublicationState` class structure.

Add the following new static method to `SmartEgressUtil`:
```csharp
/// <summary>
/// After a seek, clears all published-tick records so every descriptor appears
/// dirty and will be republished on the next egress frame.
/// </summary>
public static void ForceMarkAllDirty(EntityRepository repo)
{
    var view = (ISimulationView)repo;
    var query = view.Query()
        .WithManagedComponent<EgressPublicationState>()
        .Build();
    foreach (var entity in query)
    {
        var state = view.GetManagedComponentRO<EgressPublicationState>(entity);
        state.LastPublishedTickMap.Clear();
    }
}
```

> Match the code style of adjacent methods (using pattern). Check whether `ISimulationView` is already in scope from the existing usings. Add any missing using directives.

---

## T-RMF-22 + T-RMF-23: PlaybackTickSystem changes

### File: `FDP/Toolkits/Fdp.Toolkits/Replay/PlaybackTickSystem.cs`

Read the full file to understand the Strategy A / Strategy B logic.

#### Change 1 — Add `_afterSeek` field

In the private fields section add:
```csharp
private readonly Action? _afterSeek;
```

#### Change 2 — Update constructor signature

Change the constructor from:
```csharp
public PlaybackTickSystem(PlaybackController playback)
```
to:
```csharp
public PlaybackTickSystem(PlaybackController playback, Action? afterSeek = null)
```

In the constructor body, add:
```csharp
_afterSeek = afterSeek;
```

#### Change 3 — Call after seek in Strategy B

In `Execute`, find the Strategy B block where `_playback.SeekToFrame(repo, targetFrame)` is called. After that call, add:
```csharp
SmartEgressUtil.ForceMarkAllDirty(repo);
_afterSeek?.Invoke();
```

> Make sure `SmartEgressUtil` is reachable (they're in the same project `Fdp.Toolkits`). Add `using Fdp.Toolkit.Replication.Utilities;` at the top if not already present.

---

## T-RMF-23: CycloneNetworkCleanupSystem seek flood fix

### File 1: `FDP/Network/Fdp.Network.Cyclone/Systems/CycloneNetworkCleanupSystem.cs`

Read the file. Find the `_trackedEntities` field (type `Dictionary<long, Entity>`).

Add the following public method:
```csharp
/// <summary>
/// Discards all tracked entity state. Called after a replay seek so the system
/// does not emit stale DISPOSE packets for pre-seek ghost entities.
/// </summary>
public void ResetTracking() => _trackedEntities.Clear();
```

---

### File 2: `FDP/Toolkits/Fdp.Toolkits/Replay/ReplayModule.cs`

Read the full file.

#### Change 1 — Add `_afterSeek` field

In the private fields section, add:
```csharp
private readonly Action? _afterSeek;
```

#### Change 2 — Update constructor

Change the constructor signature from:
```csharp
public ReplayModule(string filePath, EntityRepository repo)
```
to:
```csharp
public ReplayModule(string filePath, EntityRepository repo, Action? afterSeek = null)
```

In the constructor body, add:
```csharp
_afterSeek = afterSeek;
```

#### Change 3 — Pass to PlaybackTickSystem in RegisterSystems

In `RegisterSystems`, find:
```csharp
_tickSystem = new PlaybackTickSystem(_playback);
```
Change to:
```csharp
_tickSystem = new PlaybackTickSystem(_playback, _afterSeek);
```

---

### File 3: `Hrot/Subsystems/Hrot.SimHost/Modules/Orchestration/EcsRecordReplayController.cs`

Read the full file.

#### Change 1 — Add `_afterSeek` field

Add:
```csharp
private readonly Action? _afterSeek;
```

#### Change 2 — Update constructor

Change the constructor from:
```csharp
public EcsRecordReplayController(ModuleHostKernel kernel, int nodeId, EntityRepository repo)
```
to:
```csharp
public EcsRecordReplayController(ModuleHostKernel kernel, int nodeId, EntityRepository repo, Action? afterSeek = null)
```

In the constructor body, add:
```csharp
_afterSeek = afterSeek;
```

#### Change 3 — Pass to ReplayModule in PrepareReplayAsync

In `PrepareReplayAsync`, find:
```csharp
new ReplayModule(filePath, _repo)
```
(or similar). Change to:
```csharp
new ReplayModule(filePath, _repo, _afterSeek)
```

---

### T-RMF-23 Composition root wiring

#### File 4: `Hrot/Subsystems/Hrot.SimHost/NodeBootstrapper.cs`

In `BuildOrchestration`, find where `new EcsRecordReplayController(kernel, nodeId, world)` (or equivalent) is created. 

Add an optional parameter to the `BuildOrchestration` method:
```csharp
Fdp.Network.Cyclone.Systems.CycloneNetworkCleanupSystem? cleanupSystem = null
```

Then when creating the `EcsRecordReplayController`, pass:
```csharp
afterSeek: cleanupSystem != null ? (Action?)(() => cleanupSystem.ResetTracking()) : null
```

#### File 5: `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs`

Find where `bootstrapper.BuildOrchestration(...)` is called (around line 395). Before that call, the `replicationModule` variable is available.

After the `replicationModule` variable is referenced (it's used to extract `ghostCreationSystem` and `networkLifecycleGroup`), add:
```csharp
var nedModule = replicationModule as Hrot.Network.Replication.NedReplicationModule;
```

Then in the `BuildOrchestration(...)` call, add:
```csharp
cleanupSystem: nedModule?.CleanupSystem
```

#### File 6: `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs`

In the section where `EcsRecordReplayController` is created (around line 321), `replicationModule` is already in scope. Before creating `rrController`, add:
```csharp
var nedModuleForAfterSeek = replicationModule as Hrot.Network.Replication.NedReplicationModule;
Action? afterSeekAction = nedModuleForAfterSeek?.CleanupSystem != null
    ? () => nedModuleForAfterSeek.CleanupSystem!.ResetTracking()
    : null;
```

Then change:
```csharp
var rrController = new EcsRecordReplayController(_context.Kernel, _context.NodeId, _context.World);
```
to:
```csharp
var rrController = new EcsRecordReplayController(_context.Kernel, _context.NodeId, _context.World, afterSeek: afterSeekAction);
```

#### File 7: `Hrot/Subsystems/Hrot.CGF/CgfApplication.cs`

The `CgfApplication` does not wire a `NedReplicationModule` directly, so `afterSeek` remains `null` (the default). **No changes needed for T-RMF-23 wiring in this file.**

---

## Build and Test

After all changes:

```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2

# 1. Build entire solution
dotnet build IOS-IG-SimHost.sln --no-restore -v quiet 2>&1 | Select-String "error CS|Build succeeded|FAILED"

# 2. SimHost tests
dotnet test Hrot/Subsystems/Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj --no-build 2>&1 | Select-String "Passed!|Failed!" | Select-Object -Last 2

# 3. ClusterRunner tests
dotnet test Hrot/Runner/Hrot.ClusterRunner.Tests/Hrot.ClusterRunner.Tests.csproj --no-build 2>&1 | Select-String "Passed!|Failed!" | Select-Object -Last 2

# 4. NED tests
dotnet test Hrot/Network/Hrot.Network.NED.Tests/Hrot.Network.NED.Tests.csproj --no-build 2>&1 | Select-String "Passed!|Failed!" | Select-Object -Last 2
```

**Expected results**:
- Build: 0 errors
- SimHost.Tests: ≥ 458/461 (1 pre-existing failure: `EntityMission_MovesEntity`)
- ClusterRunner.Tests: 219/219
- NED.Tests: all pass

> The pre-existing `EntityMission_MovesEntity` failure exists since SHA 0ce69f5 and is NOT a regression from any BATCH changes.

---

## What NOT to change

- Do NOT modify `NetworkLifecycleSystemGroup.cs` — it is correct as-is
- Do NOT remove the `registry.RegisterSystem(new OwnershipIngressSystem(...))` in the `pureBrainRole` block
- Do NOT add `GhostCreationSystem` to the lifecycle inner-systems list again — it is already the first element
- Do NOT change `ISystemGroup` or `IEcsModuleSystem` interfaces
- Do NOT alter test projects unless fixing a genuine compilation error caused by your changes

---

## FDP Submodule Commit

After build succeeds, commit the FDP submodule changes (6 files changed in `FDP/` directory):

```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2\FDP
git add -A
git commit -m "T-RMF-21/22/23: GlobalTimePush suspend, ForceMarkAllDirty, afterSeek cascade"
```

Then commit the outer repo:

```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2
git add -A
git commit -m "T-RMF-20/21/22/23: Phase 4 - replay isolation (lifecycle group, global time, seek dirty flush)"
```

---

## Summary of files to change

**FDP submodule (6 files):**
1. `FDP/Engine/Fdp.ModuleHost/ModuleHostKernel.cs` — T-RMF-21: suspend/resume + conditional push
2. `FDP/Toolkits/Fdp.Toolkits/Orchestration/Handlers/ReferenceReplayLoadHandler.cs` — T-RMF-21: delegate params + invoke
3. `FDP/Toolkits/Fdp.Toolkits/Replication/Utilities/SmartEgressUtil.cs` — T-RMF-22: ForceMarkAllDirty
4. `FDP/Toolkits/Fdp.Toolkits/Replay/PlaybackTickSystem.cs` — T-RMF-22+23: ForceMarkAllDirty call + afterSeek
5. `FDP/Toolkits/Fdp.Toolkits/Replay/ReplayModule.cs` — T-RMF-23: afterSeek pass-through
6. `FDP/Network/Fdp.Network.Cyclone/Systems/CycloneNetworkCleanupSystem.cs` — T-RMF-23: ResetTracking

**Hrot (7 files):**
7. `Hrot/Network/Hrot.Network.NED/Replication/NedReplicationModule.cs` — T-RMF-20: move systems, CleanupSystem property
8. `Hrot/Subsystems/Hrot.SimHost/Modules/Orchestration/EcsRecordReplayController.cs` — T-RMF-23: afterSeek pass-through
9. `Hrot/Subsystems/Hrot.SimHost/NodeBootstrapper.cs` — T-RMF-21+23: pass suspend/resume + cleanupSystem param
10. `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs` — T-RMF-23: pass cleanupSystem to BuildOrchestration
11. `Hrot/Subsystems/Hrot.CGF/CgfApplication.cs` — T-RMF-21: pass suspend/resume to ReferenceReplayLoadHandler
12. `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs` — T-RMF-21+23: pass suspend/resume + afterSeek to EcsRecordReplayController
13. (no changes) `Hrot/Subsystems/Hrot.CGF/CgfApplication.cs` T-RMF-23 — afterSeek remains null by default
