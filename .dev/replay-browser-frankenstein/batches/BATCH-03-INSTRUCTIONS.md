# BATCH-03 Instructions — Replay Browser Frankenstein

**Feature:** Replay Browser Frankenstein (RBF)
**Batch:** BATCH-03
**Tasks:** D01 fix, D02 fix, D03 close, RBF-P3T5, RBF-P3T7

Read `.github/skills/developer/SKILL.md` before starting. This document IS the batch
instruction; follow the MANDATORY WORKFLOW from that skill.

---

## Context

Read these files before starting. DO NOT duplicate their content here — reference them.

- DESIGN: `.dev/replay-browser-frankenstein/DESIGN.md` (§7.3, §7.4, §7.5, §7.8 are critical)
- TASK-DETAILS: `.dev/replay-browser-frankenstein/TASK-DETAILS.md` (RBF-P3T5, RBF-P3T7)
- DEBT-TRACKER: `.dev/replay-browser-frankenstein/DEBT-TRACKER.md` (D01, D02, D03)
- Existing federation code: `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Federation/`
- Existing tests for context: `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Federation/FederatedReplayManagerTests.cs`

---

## Corrective Task C0 — Close D03 (inline-array Entity field constraint)

**Source:** DEBT-TRACKER D03

Check `FDP/Toolkits/Fdp.Toolkits/Scenario/FdpAutoSerializer.cs` around lines 95-115.

`FdpAutoSerializer` EXPLICITLY throws at component-registration time when an
`[InlineArray]`-decorated field has element type `Entity`. This means:

- `RBF_P3T3_DeserializeWith_InlineArrayHandleResolves` cannot be written — no
  component with an `[InlineArray] Entity` element type can even be registered.
- The test is not feasible; the feature is intentionally unsupported.

**Action required:**
1. Confirm this behavior by reading the relevant lines.
2. Resolve D03 in DEBT-TRACKER as RESOLVED with note: "FdpAutoSerializer throws at
   registration for [InlineArray] Entity element type. Test not feasible; constraint is
   intentional (documented in FdpAutoSerializer source)."
3. Mark D03 as RESOLVED in the DEBT-TRACKER (do not delete the row, change status to
   RESOLVED and add a resolution note).
4. No test file to write. No production code to change.

---

## Corrective Task C1 — Fix D01 (SeekAll per-node offset test)

**Source:** DEBT-TRACKER D01
**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Federation/FederatedReplayManagerTests.cs`

The existing `RBF_P2T1_SeekAll_*` tests do NOT verify that a node with a non-zero
offset lands on a different playback position than a node with zero offset.

**Add test:**
```
RBF_P2T1_SeekAll_WithNodeOffset_NodeLandsOnDifferentState
```

**What it must prove:**
- Two recordings, node 1 (offset=0) and node 2 (offset=+delta).
- Record two distinct frames in EACH recording with different `HarnessPosition.X` values.
  Example: frame 0 at wallTick=1_000_000 has X=1.0, frame 1 at wallTick=2_000_000 has X=2.0.
- After `SetNodeOffset(2, offset_matching_frame_1)` and `SeekAll(baseWallTick_at_frame_0)`:
  - Node 1's `SandboxRepo` has X=1.0 (frame 0 state)
  - Node 2's `SandboxRepo` has X=2.0 (frame 1 state, because of the positive offset)
- Use `Assert.NotEqual` to confirm the two nodes have different `HarnessPosition.X` values.

**Notes:**
- You can use the existing `MakeRecording` helper but add two frames with distinct wall ticks.
- `HarnessPosition` is already registered in `FdpRecordingHarness`.
- After `SeekAll`, check `SandboxRepo.GetComponent<HarnessPosition>(entity).X`.
- To find which entity to check, iterate the repo or use the harness's `LastSpawned` index.
  Since the entity index resets after each fresh seek, use `repo.MaxEntityIndex` and
  iterate active entities.
- Mark D01 as RESOLVED in DEBT-TRACKER.

---

## Corrective Task C2 — Fix D02 (SetNodeOffset unknown NodeId)

**Source:** DEBT-TRACKER D02
**File:** `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Federation/FederatedReplayManager.cs`

**Current behavior:** `SetNodeOffset(unknownNodeId, offset)` silently writes to the
backing `_nodeOffsets` dictionary for a node that is not loaded (not in `_contexts`).
This is inconsistent with `SetLocalEntitiesProvider` which throws.

**Required change:**
```csharp
public void SetNodeOffset(int nodeId, long offset)
{
    if (!_contexts.ContainsKey(nodeId))
        throw new ArgumentOutOfRangeException(nameof(nodeId),
            $"Node {nodeId} is not loaded in this FederatedReplayManager.");
    _nodeOffsets[nodeId] = offset;
}
```

**Add test:**
```
RBF_P2T1_SetNodeOffset_UnknownNodeId_Throws
```
Verifies `Assert.Throws<ArgumentOutOfRangeException>(() => manager.SetNodeOffset(999, 0))`.
Add to `FederatedReplayManagerTests.cs`.

Mark D02 as RESOLVED in DEBT-TRACKER.

---

## Task RBF-P3T5 — TransientMasterBuilder.Build

**Full task spec:** `.dev/replay-browser-frankenstein/TASK-DETAILS.md#rbf-p3t5`
**Design:** DESIGN.md §7.3, §7.4, §7.5

### New file
`FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Federation/TransientMasterBuilder.cs`

### Class design

```csharp
namespace Fdp.Toolkit.ReplayBrowser.Federation
{
    /// <summary>
    /// Builds a transient <see cref="EntityRepository"/> from a
    /// <see cref="FederatedReplayManager"/> snapshot. The returned repo is
    /// owned and disposed by the caller.
    /// </summary>
    public sealed class TransientMasterBuilder
    {
        private readonly Fdp.Toolkit.Scenario.ScenarioSerializer _serializer;

        public TransientMasterBuilder(Fdp.Toolkit.Scenario.ScenarioSerializer serializer)
        {
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

        /// <summary>
        /// Constructs and returns a new transient <see cref="EntityRepository"/>
        /// populated from the current seeked state of all nodes in
        /// <paramref name="manager"/>. Follows DESIGN §7.3, §7.4, §7.5.
        /// The caller is responsible for disposing the returned repository.
        /// </summary>
        public EntityRepository Build(FederatedReplayManager manager) { ... }
    }
}
```

### Implementation algorithm (follow DESIGN §7.3, §7.4, §7.5 exactly)

**Step 1 — Allocate and prime transient repo**
```csharp
var transientRepo = new EntityRepository();
RepositoryPriming.RegisterDiscoveredComponents(transientRepo);
```

**Step 2 — Correlate by NetworkIdentity**

Walk every alive entity in every `manager.Contexts[nodeId].SandboxRepo`. For entities
that carry `NetworkIdentity`, group by `NetworkIdentity.Value`:
```csharp
var correlation = new Dictionary<long, List<(int nodeId, Entity entity)>>();
foreach (var (nodeId, ctx) in manager.Contexts)
{
    var repo = ctx.SandboxRepo;
    int netIdTypeId = ComponentTypeRegistry.GetId(typeof(NetworkIdentity));
    if (netIdTypeId < 0) continue; // not registered in this run
    for (int i = 0; i <= repo.MaxEntityIndex; i++)
    {
        var e = new Entity(i, repo.GetMetadata(i).Generation);
        if (!repo.IsAlive(e)) continue;
        if (!repo.GetComponentMask(i).IsSet(netIdTypeId)) continue;
        long netVal = repo.GetComponent<NetworkIdentity>(e).Value;
        if (!correlation.TryGetValue(netVal, out var list))
            correlation[netVal] = list = new List<(int, Entity)>();
        list.Add((nodeId, e));
    }
}
```

**Step 3 — Pre-allocate transient entities and build resolver load-map**

For each global ID in the correlation map, create one transient entity. Use
`NetworkIdGuid.From(globalId).ToString("N")` as the JSON entity key:
```csharp
var resolver   = new FederatedGuidResolver();
var preAllocated = new Dictionary<string, Entity>(StringComparer.Ordinal);
var loadMap    = new Dictionary<string, Entity>(StringComparer.Ordinal);

foreach (var (netVal, _) in correlation)
{
    var key = NetworkIdGuid.From(netVal).ToString("N");
    var transientEntity = transientRepo.CreateEntity();
    preAllocated[key] = transientEntity;
    loadMap[key]      = transientEntity;
}
resolver.SetLoadMap(loadMap);
```

**Step 4 — Build master DOM envelope**

```csharp
var entitiesNode = new JsonObject();
var masterDom    = new JsonObject
{
    ["Header"]   = new JsonObject
    {
        ["SubsystemType"]   = _serializer.SubsystemType,
        ["SchemaVersion"]   = 1
    },
    ["Entities"] = entitiesNode
};
```

Note: `ScenarioSerializer.SubsystemType` must be accessible. If it is not currently a
public property, add `public string SubsystemType { get; }` to `ScenarioSerializer`
(set from the constructor's subsystem-type parameter, already stored as `_subsystemType`).

**Step 5 — For each global ID: order nodes, apply §7.3 consensus extraction**

```csharp
int netAuthTypeId = ComponentTypeRegistry.GetId(typeof(NetworkAuthority));

foreach (var (netVal, nodeEntities) in correlation)
{
    string entityKey = NetworkIdGuid.From(netVal).ToString("N");
    var mergedEntityNode = new JsonObject();
    var alreadyClaimed   = new BitMask512();

    // Determine primary owner
    int primaryOwner = -1;
    if (netAuthTypeId >= 0)
    {
        foreach (var (nid, ent) in nodeEntities)
        {
            var repo = manager.Contexts[nid].SandboxRepo;
            if (!repo.GetComponentMask(ent.Index).IsSet(netAuthTypeId)) continue;
            primaryOwner = repo.GetComponent<NetworkAuthority>(ent).PrimaryOwnerId;
            break; // All nodes agree on PrimaryOwnerId for the same entity
        }
    }

    // Order: primary-owner node first, then ascending NodeId
    var ordered = new List<(int nodeId, Entity entity)>(nodeEntities);
    ordered.Sort((a, b) =>
    {
        if (a.nodeId == primaryOwner && b.nodeId != primaryOwner) return -1;
        if (b.nodeId == primaryOwner && a.nodeId != primaryOwner) return  1;
        return a.nodeId.CompareTo(b.nodeId);
    });

    // Build per-node save maps and extract fragments
    foreach (var (nid, localEntity) in ordered)
    {
        var localRepo = manager.Contexts[nid].SandboxRepo;

        // Build save-map for this node (local entity → transient key)
        var saveMap = BuildSaveMapForNode(nid, manager, loadMap, correlation);
        resolver.SetSaveMap(saveMap);

        // §7.3 consensus mask
        var presenceMask  = localRepo.GetComponentMask(localEntity.Index);
        var authorityMask = localRepo.GetMetadata(localEntity.Index).AuthorityMask;
        var candidate     = presenceMask;
        candidate.BitwiseAnd(authorityMask);
        var extract = candidate;
        extract.BitwiseAndNot(alreadyClaimed);
        alreadyClaimed.BitwiseOr(extract);

        if (extract.IsEmpty()) continue;

        // Extract fragment
        var fragment = _serializer.SerializeEntity(localRepo, localEntity, resolver, extract);

        // Merge fragment into merged entity node (duplicate keys impossible by design)
        foreach (var kv in fragment)
            mergedEntityNode.Add(kv.Key, kv.Value?.DeepClone());
    }

    entitiesNode[entityKey] = mergedEntityNode;
}
```

**Step 6 — Deserialize into transient repo**
```csharp
_serializer.DeserializeWith(transientRepo, masterDom, resolver, preAllocated);
return transientRepo;
```

**Helper method `BuildSaveMapForNode`:**

Builds a `Dictionary<Entity, string>` mapping every alive entity in the given node's
repo to its master-DOM key. Global (NetworkIdentity) entities use
`NetworkIdGuid.From(netVal).ToString("N")`. Local-only entities are NOT included here
(that is P3T7's job — keep them null/absent for now). Pass `loadMap` and `correlation`
to derive the mapping:
```csharp
private static Dictionary<Entity, string> BuildSaveMapForNode(
    int nodeId,
    FederatedReplayManager manager,
    Dictionary<string, Entity> loadMap, // masterKey -> transientEntity
    Dictionary<long, List<(int nodeId, Entity entity)>> correlation)
{
    // Invert loadMap for lookup: transientEntity -> masterKey
    var invertedLoad = new Dictionary<Entity, string>();
    foreach (var kvp in loadMap)
        invertedLoad[kvp.Value] = kvp.Key;

    // Build save map: localEntity (on this node) -> masterKey
    var saveMap = new Dictionary<Entity, string>();
    var localRepo = manager.Contexts[nodeId].SandboxRepo;
    int netIdTypeId = ComponentTypeRegistry.GetId(typeof(NetworkIdentity));
    if (netIdTypeId < 0) return saveMap;

    for (int i = 0; i <= localRepo.MaxEntityIndex; i++)
    {
        var e = new Entity(i, localRepo.GetMetadata(i).Generation);
        if (!localRepo.IsAlive(e)) continue;
        if (!localRepo.GetComponentMask(i).IsSet(netIdTypeId)) continue;
        long netVal    = localRepo.GetComponent<NetworkIdentity>(e).Value;
        string key     = NetworkIdGuid.From(netVal).ToString("N");
        saveMap[e]     = key;
    }
    return saveMap;
}
```

### Required namespaces

```csharp
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components; // NetworkIdentity, NetworkAuthority
using Fdp.Toolkit.Scenario;
```

### ScenarioSerializer.SubsystemType

If `ScenarioSerializer` does not currently expose `public string SubsystemType { get; }`,
add it. It is already stored as `private readonly string _subsystemType` (from the
`ScenarioSerializerBuilder` call). Just add a public getter. This is a minimal additive
change.

### Test file
`FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Federation/TransientMasterBuilderTests.cs`

**Test setup pattern:**

```csharp
public sealed class TransientMasterBuilderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ScenarioSerializer _serializer;
    private readonly Guid _exerciseId = Guid.NewGuid();

    public TransientMasterBuilderTests()
    {
        ComponentTypeRegistry.Clear();
        _tempDir = Path.Combine(Path.GetTempPath(), $"TMBTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _serializer = new ScenarioSerializerBuilder("TestSubsystem").Build();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    /// <summary>
    /// Creates a .fdp recording + .meta.json for a node.
    /// The <paramref name="setup"/> callback configures the EntityRepository
    /// (components, authority) before the keyframe is captured.
    /// Components registered here are available to playback via RepositoryPriming.
    /// </summary>
    private string MakeNetworkRecording(int nodeId, Action<EntityRepository> setup)
    {
        var path = Path.Combine(_tempDir, $"node{nodeId}_{Guid.NewGuid():N}.fdp");
        var meta = new RecordingMetadata { ExerciseId = _exerciseId, NodeId = nodeId };

        using var repo = new EntityRepository();
        repo.RegisterComponent<NetworkIdentity>();
        repo.RegisterComponent<NetworkAuthority>();
        repo.RegisterComponent<DummyPosition>();
        repo.RegisterComponent<GuidedTarget>();
        setup(repo);

        using (var recorder = new AsyncRecorder(path, meta))
            recorder.CaptureKeyframe(repo, 1_000_000L, blocking: true, eventBus: repo.Bus);

        // Ensure the .meta.json carries the federation metadata.
        // AsyncRecorder writes it on Dispose; overwrite to be safe.
        File.WriteAllText(path + ".meta.json", MetadataSerializer.Serialize(meta));
        return path;
    }
}
```

**Use `DummyPosition` and `GuidedTarget`** from `Fdp.Toolkit.Scenario.Tests` namespace
(already defined in `FDP/Toolkits/Fdp.Toolkits.Tests/Scenario/TestComponents.cs`).

### Mandatory tests

#### RBF_P3T5_Build_TwoNodes_SplitAuthority

Two nodes with `NetworkIdentity.Value = 42L`:
- Node 1 setup: entity has `NetworkIdentity(42)`, authoritative `DummyPosition {X=1}`.
  `SetAuthority<DummyPosition>(entity, true)`.
- Node 2 setup: entity has `NetworkIdentity(42)`, authoritative `GuidedTarget {TargetId=Entity.Null}`.
  `SetAuthority<GuidedTarget>(entity, true)`.

After `FederatedReplayManager.LoadGroup([path1, path2])` + `SeekAll(0)` + `Build`:
```csharp
Assert.Equal(1, master.EntityCount);
Entity masterEntity = GetSingleAliveEntity(master); // helper: iterate EntityCount == 1
Assert.True(master.HasComponent<DummyPosition>(masterEntity));
Assert.True(master.HasComponent<GuidedTarget>(masterEntity));
Assert.Equal(1f, master.GetComponent<DummyPosition>(masterEntity).X);
```

Note: `SeekAll(0)` for a manager loaded with 2 recordings; the keyframes are at
wallTick 1_000_000. Call `SeekAll(1_000_000L)` or call `SetBaseWallTicks(1_000_000L)`.
Then call `Build`. The manager must have seeked both contexts.

#### RBF_P3T5_Build_GhostExcluded

- Node 1: entity has `NetworkIdentity(99)`, authoritative `DummyPosition {X=10}`.
  `SetAuthority<DummyPosition>(entity, true)`.
- Node 2: entity has `NetworkIdentity(99)`, `DummyPosition {X=99}` PRESENT but NOT
  authoritative. `SetAuthority<DummyPosition>(entity, false)`.

After Build:
```csharp
Assert.Equal(1, master.EntityCount);
var masterEntity = GetSingleAliveEntity(master);
Assert.True(master.HasComponent<DummyPosition>(masterEntity));
// Node 1's data wins; node 2's ghost is excluded
Assert.Equal(10f, master.GetComponent<DummyPosition>(masterEntity).X);
Assert.NotEqual(99f, master.GetComponent<DummyPosition>(masterEntity).X);
```

#### RBF_P3T5_Build_RelationalHandleRemapped

- Node 1 setup (both entities on the same node):
  - Entity A: `NetworkIdentity(100)`, authoritative `GuidedTarget` with `TargetId` = entity B.
    `SetAuthority<GuidedTarget>(entityA, true)`.
  - Entity B: `NetworkIdentity(101)`, authoritative `DummyPosition {X=5}`.
    `SetAuthority<DummyPosition>(entityB, true)`.

After Build:
```csharp
// Find entity with NetworkIdentity=100 in master
Entity masterA = FindEntityWithNetId(master, 100L);
Entity masterB = FindEntityWithNetId(master, 101L);

Assert.True(master.HasComponent<GuidedTarget>(masterA));
var gt = master.GetComponent<GuidedTarget>(masterA);
Assert.Equal(masterB, gt.TargetId);  // handle was remapped to transient master entity
Assert.NotEqual(Entity.Null, gt.TargetId);
```

Helper `FindEntityWithNetId`: iterate alive entities, check `HasComponent<NetworkIdentity>`
and compare `GetComponent<NetworkIdentity>(e).Value`. BUT WAIT — after `Build`, the
transient repo does NOT have `NetworkIdentity` as a component. The `NetworkIdentity`
data was merged into the DOM and then loaded, but only components with authoritative
bits were extracted. If you set `SetAuthority<NetworkIdentity>(entity, true)` in both
setups, the `NetworkIdentity` will be present in the master. Otherwise, use the
`preAllocated` key ordering (entities are created in the order the global IDs are
processed in `Build`).

**Alternative approach for this test:** Since the transient repo should carry
`NetworkIdentity` as a component (it was in the authority mask on node 1), query it:
```csharp
Entity masterA = FindEntityWithNetId(master, 100L); // by querying NetworkIdentity
Entity masterB = FindEntityWithNetId(master, 101L);
```
Add `SetAuthority<NetworkIdentity>(entityA, true)` and `SetAuthority<NetworkIdentity>(entityB, true)`
so `NetworkIdentity` is extracted.

#### RBF_P3T5_Build_MissingTargetResolvesToEntityNull

- Node 1: entity A has `NetworkIdentity(200)`, `GuidedTarget {TargetId = entityX}` authoritative.
  `SetAuthority<GuidedTarget>(entityA, true)`, `SetAuthority<NetworkIdentity>(entityA, true)`.
- `entityX` is a local entity that has NO `NetworkIdentity` — it will not be correlated.
  It will NOT appear in the transient master's `preAllocated` map.

After Build:
```csharp
Entity masterA = FindEntityWithNetId(master, 200L);
Assert.True(master.HasComponent<GuidedTarget>(masterA));
Assert.Equal(Entity.Null, master.GetComponent<GuidedTarget>(masterA).TargetId);
// No throw occurred
```

#### RBF_P3T5_Build_SplitBrainConflict_PrimaryOwnerWins

Both nodes claim the same component for the same entity:
- Node 1 (primary owner): entity has `NetworkIdentity(300)`, authoritative `DummyPosition {X=1}`.
  `SetAuthority<DummyPosition>(e1, true)`, `SetAuthority<NetworkIdentity>(e1, true)`.
  Add `NetworkAuthority {PrimaryOwnerId=1, LocalNodeId=1}` with authority.
- Node 2 (non-primary): entity has same `NetworkIdentity(300)`, authoritative `DummyPosition {X=99}`.
  `SetAuthority<DummyPosition>(e2, true)`, `SetAuthority<NetworkIdentity>(e2, true)`.
  Add `NetworkAuthority {PrimaryOwnerId=1, LocalNodeId=2}` with authority.

Both nodes set `AuthorityMask` for `DummyPosition`. The §7.3 algorithm should process
node 1 first (primary owner) and claim `DummyPosition`. When node 2 is processed,
`DummyPosition` is already in `alreadyClaimed`, so node 2's data is skipped.

After Build:
```csharp
Entity masterE = FindEntityWithNetId(master, 300L);
Assert.Equal(1f, master.GetComponent<DummyPosition>(masterE).X);
Assert.NotEqual(99f, master.GetComponent<DummyPosition>(masterE).X);
```

**Note:** For this test, add `SetAuthority<NetworkAuthority>(entity, true)` so the
`NetworkAuthority` component is also extracted and appears in the DOM.

#### RBF_P3T5_Build_RebuildableCheaply

Load two nodes with `NetworkIdentity(400)`, each with distinct authoritative components.
Call `Build` twice. Both calls should return repos with the same entity count and the
same component values:
```csharp
using var master1 = builder.Build(manager);
using var master2 = builder.Build(manager);
Assert.Equal(master1.EntityCount, master2.EntityCount);
// Spot-check one value
Entity e1 = GetSingleAliveEntity(master1);
Entity e2 = GetSingleAliveEntity(master2);
Assert.Equal(
    master1.GetComponent<DummyPosition>(e1).X,
    master2.GetComponent<DummyPosition>(e2).X);
```

### Helper methods in the test class

Add these private helpers:
```csharp
private static Entity GetSingleAliveEntity(EntityRepository repo)
{
    for (int i = 0; i <= repo.MaxEntityIndex; i++)
    {
        var e = new Entity(i, repo.GetMetadata(i).Generation);
        if (repo.IsAlive(e)) return e;
    }
    throw new InvalidOperationException("No alive entity found.");
}

private static Entity FindEntityWithNetId(EntityRepository repo, long netVal)
{
    int typeId = ComponentTypeRegistry.GetId(typeof(NetworkIdentity));
    if (typeId < 0) throw new InvalidOperationException("NetworkIdentity not registered.");
    for (int i = 0; i <= repo.MaxEntityIndex; i++)
    {
        var e = new Entity(i, repo.GetMetadata(i).Generation);
        if (!repo.IsAlive(e)) continue;
        if (!repo.GetComponentMask(i).IsSet(typeId)) continue;
        if (repo.GetComponent<NetworkIdentity>(e).Value == netVal) return e;
    }
    throw new InvalidOperationException($"No entity with NetworkIdentity.Value={netVal}.");
}
```

### Build verification

After implementing `TransientMasterBuilder`:
```
dotnet build IOS-IG-SimHost.sln
```
Must produce 0 errors.

---

## Task RBF-P3T7 — Local-Entities Provider injection in TransientMasterBuilder

**Full task spec:** `.dev/replay-browser-frankenstein/TASK-DETAILS.md#rbf-p3t7`
**Design:** DESIGN.md §7.8

Extend `TransientMasterBuilder.Build` to inject entities from the
`LocalEntitiesProviderNodeId` node that have NO `NetworkIdentity`.

### Extension to `Build` (AFTER the global-ID correlation pass)

In DESIGN §7.8:
1. Get `providerNodeId = manager.LocalEntitiesProviderNodeId`.
2. Walk the provider's `SandboxRepo` for entities WITHOUT `NetworkIdentity`.
3. For each, generate a synthetic Guid from `(providerNodeId, entity.Index, entity.Generation)`.
4. Pre-allocate transient entities and add to `loadMap` / `preAllocated`.
5. Seed the provider's `_saveMap` to include local entities.
6. Extract with FULL presence mask (NOT AuthorityMask).
7. Add to master DOM under the synthetic key.

### Synthetic Guid generation

Use a deterministic hash. Recommended:
```csharp
private static string MakeSyntheticKey(int providerNodeId, int entityIndex, ushort generation)
{
    // Stable human-readable prefix for debug dumps; hash into a Guid.
    var src = $"LOCAL_NODE_{providerNodeId}_ENT_{entityIndex}_G_{generation}";
    byte[] hash = System.Security.Cryptography.MD5.HashData(
        System.Text.Encoding.UTF8.GetBytes(src));
    return new Guid(hash).ToString("N");
}
```

The string key is stored in `loadMap` and `preAllocated`. `Guid.TryParse` on the
result must return true.

### Extended save-map seeding for the provider

When building the save-map for the provider node (in `BuildSaveMapForNode`), also include
the local-only entities' entries. You can either extend `BuildSaveMapForNode` with a
parameter or build the provider's save-map inline in `Build`.

The requirement: a global entity on the provider node that has a `GuidedTarget.TargetId`
pointing to a LOCAL entity in the same provider node must resolve to the transient
entity for that local entity (not `Entity.Null`).

### Extraction for local entities (FULL presence mask)

```csharp
var fullMask = localProviderRepo.GetComponentMask(localEntity.Index);
// No BitwiseAnd with AuthorityMask — use full presence mask directly
var fragment = _serializer.SerializeEntity(localProviderRepo, localEntity, resolver, fullMask);
entitiesNode[syntheticKey] = fragment;
```

### Mandatory tests

Test file: same `TransientMasterBuilderTests.cs` (add tests to existing class).

#### RBF_P3T7_LocalEntities_ProviderEntitiesAppearInMaster

- Provider = node 1 (default lowest NodeId).
- Node 1: entity WITHOUT `NetworkIdentity`, authoritative `DummyPosition {X=7}`.
  No `NetworkIdentity` component at all.
- After Build:
  ```csharp
  // Master must contain an entity with DummyPosition.X == 7
  bool found = false;
  for (int i = 0; i <= master.MaxEntityIndex; i++)
  {
      var e = new Entity(i, master.GetMetadata(i).Generation);
      if (!master.IsAlive(e)) continue;
      if (!master.HasComponent<DummyPosition>(e)) continue;
      if (master.GetComponent<DummyPosition>(e).X == 7f) { found = true; break; }
  }
  Assert.True(found, "Local entity from provider must appear in master.");
  ```

#### RBF_P3T7_LocalEntities_NonProviderLocalsExcluded

Two nodes loaded. Provider = node 1.
- Node 1: NO local entities (or only global/networked entities).
- Node 2: entity WITHOUT `NetworkIdentity`, `DummyPosition {X=99}`.
  This local entity should NOT appear in master because node 2 is not the provider.

After Build, verify no entity in master has `DummyPosition.X == 99`.

#### RBF_P3T7_LocalEntities_UseFullPresenceMask_NotAuthorityMask

- Provider = node 1.
- Node 1: local entity (no `NetworkIdentity`), `DummyPosition {X=3}`.
  `SetAuthority<DummyPosition>(entity, false)` — NOT authoritative (ghost-like).
  The component IS present in `SandboxRepo` but NOT in `AuthorityMask`.

After Build: entity appears in master WITH `DummyPosition.X == 3`.
(Proves full presence mask is used, not AuthorityMask.)

#### RBF_P3T7_LocalEntities_GlobalHandleToLocalResolves

- Provider = node 1.
- Node 1 setup:
  - Global entity: `NetworkIdentity(500)`, `GuidedTarget {TargetId = localEntity}` authoritative.
    `SetAuthority<GuidedTarget>(globalE, true)`, `SetAuthority<NetworkIdentity>(globalE, true)`.
  - Local entity (no `NetworkIdentity`): `DummyPosition {X=8}`.
    The `GuidedTarget.TargetId` on `globalE` = `localEntity`.

After Build:
```csharp
Entity masterGlobal = FindEntityWithNetId(master, 500L);
var gt = master.GetComponent<GuidedTarget>(masterGlobal);
Assert.NotEqual(Entity.Null, gt.TargetId);
// Resolve by finding the entity with DummyPosition.X == 8
Assert.True(master.HasComponent<DummyPosition>(gt.TargetId));
Assert.Equal(8f, master.GetComponent<DummyPosition>(gt.TargetId).X);
```

#### RBF_P3T7_LocalEntities_SwitchProviderRebuilds

Two nodes. Each has a local entity with distinct `DummyPosition.X` (X=7 for node 1, X=9 for node 2).

1. Default provider = node 1 → Build → find entity with X=7, verify no entity with X=9.
2. `manager.SetLocalEntitiesProvider(2)` → Build again → find entity with X=9, verify no entity with X=7.

#### RBF_P3T7_SyntheticGuid_ParseableAndDeterministic

```csharp
// Access the synthetic key generation method indirectly:
// Build twice from same state, compare the preAllocated keys.
// OR expose MakeSyntheticKey as internal and test it directly.
```

Expose `internal static string MakeSyntheticKey(int, int, ushort)` so it can be tested.

```csharp
string key1 = TransientMasterBuilder.MakeSyntheticKey(1, 5, 2);
string key2 = TransientMasterBuilder.MakeSyntheticKey(1, 5, 2);
Assert.Equal(key1, key2);
Assert.True(Guid.TryParse(key1, out _));
// Different inputs produce different keys
string key3 = TransientMasterBuilder.MakeSyntheticKey(2, 5, 2);
Assert.NotEqual(key1, key3);
```

---

## Required using statements for `TransientMasterBuilder.cs`

```csharp
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
```

---

## Build & Test Checklist

After completing all corrective tasks and RBF-P3T5:
```
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj ^
  --filter "FullyQualifiedName~RBF_P2T1|FullyQualifiedName~RBF_P3T5"
```

After completing RBF-P3T7:
```
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj ^
  --filter "FullyQualifiedName~RBF_P3T5|FullyQualifiedName~RBF_P3T7"
```

Full solution build before finalizing:
```
dotnet build IOS-IG-SimHost.sln
```

---

## Success Criteria

| Criterion | Verify |
|-----------|--------|
| D01 resolved — offset displacement test exists and passes | test `RBF_P2T1_SeekAll_WithNodeOffset_NodeLandsOnDifferentState` passes |
| D02 resolved — `SetNodeOffset` throws for unknown NodeId | test `RBF_P2T1_SetNodeOffset_UnknownNodeId_Throws` passes |
| D03 closed — inline array entity constraint documented | DEBT-TRACKER D03 marked RESOLVED |
| `TransientMasterBuilder.Build` produces correct merged repo | 6 P3T5 tests pass |
| Local-entities injection works | 6 P3T7 tests pass |
| Solution builds | 0 errors |

---

## Developer Insight Questions

Answer these in the batch report (BATCH-03-REPORT.md):

**Q1:** Did the `BitMask512.BitwiseAnd` (not `AndNot`) used in the consensus extraction work
correctly as a mutation (modifies `this` in place)? Or did you need to create a copy before
calling it? Show the signature you relied on.

**Q2:** How did you handle the case where `NetworkIdentity` is NOT registered in
`ComponentTypeRegistry` when `Build` is called (e.g., in a test that calls
`ComponentTypeRegistry.Clear()` followed by minimal `RegisterComponent` calls, without
running `RepositoryPriming`)? Is there a guard?

**Q3:** The `_serializer.SerializeEntity(...)` in step 5 calls `GetConsumedComponentsMask()`
on each translator and skips translators whose consumed mask doesn't intersect the
extraction mask. If a translator consumes BOTH component A and B, but the extraction mask
only grants component A — what happens to component B in the fragment? Describe the actual
behavior from reading the code.

**Q4:** Did you encounter any issues with the `GuidedTarget.TargetId` being set to a
local-entity handle in node-1 and then resolving in the master DOM, specifically with
the order of operations: save-map build BEFORE vs AFTER calling `SetSaveMap` on the
resolver? Explain what ordering was required.

**Q5:** `MD5.HashData` in `MakeSyntheticKey` uses `System.Security.Cryptography`. Is MD5
available in .NET 8 without additional packages, and is it appropriate here (not for
cryptographic security, only for deterministic hashing of a debug string)? Confirm.

---

## Report

Write the completion report to:
`.dev/replay-browser-frankenstein/reports/BATCH-03-REPORT.md`

Include:
- Summary table of tasks and test counts
- Description of production files created/modified
- Description of test files created/modified
- Build & test results
- Answers to Q1-Q5
- Any design deviations with justification
