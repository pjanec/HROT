# BATCH-06 — TKB Integration: Singleton, Handler, Translator Wiring

## Scope

| Task | Title | Notes |
|------|-------|-------|
| TKB-015 | Register `ITkbDatabase` as ECS world singleton | `SimHostNodeBootstrapper` only; IG already done |
| TKB-019 | Implement `TkbLoadClusterStateHandler` | New file in `Hrot.SimHost.Orchestration.Handlers` |
| TKB-020 | Wire handler in `NodeBootstrapper.BuildOrchestration()` | Add `tkbDb` param; register before scenario block |
| TKB-022 | Wire translator list in composition root | All three systems get the same list |

**Full specs:** `DESIGN.md` Phase 7 §7.1–7.4 and `TASK-DETAIL.md` §TKB-015, §TKB-019, §TKB-020, §TKB-022.

---

## Prerequisites — Read First

Before writing any code, read these files for full context:

- `DESIGN.md` lines 597–760 (Phase 7: `TkbLoadClusterStateHandler` full spec)
- `TASK-DETAIL.md` lines 820–1005 (TKB-015, TKB-019, TKB-020, TKB-022)
- `Hrot/Subsystems/Hrot.SimHost/SimHostNodeBootstrapper.cs` (full file — all hooks)
- `Hrot/Subsystems/Hrot.SimHost/NodeBootstrapper.cs` (full `BuildOrchestration` method)
- `Hrot/Network/Hrot.Network.NED/Replication/NedReplicationModule.cs` lines 1–380
- `Hrot/Network/Hrot.Network.NED/Infrastructure/HrotNodeBuilderReplicationExtensions.cs`
- `FDP/Toolkits/Fdp.Toolkits/Lifecycle/EntityLifecycleModule.cs` (constructor + RegisterSystems)

---

## Architectural Context

### Instance identity (critical)

`HrotNodeBuilder.Build()` creates `tkbDb = HrotEnvironment.CreateTkb()` and stores it in
`HrotNodeContext.TkbDb`. This is the authoritative `TkbDatabase` instance used by:
- `EntityLifecycleModule` (passed at construction inside `HrotNodeBuilder`)
- `NetworkSpawningSystem` (passed explicitly in `RegisterSpawningPipeline` via `context.TkbDb!`)
- `TkbLoadClusterStateHandler` (must receive `context.TkbDb` so `Clear()` affects all systems)

`NedReplicationModule` CURRENTLY creates a SEPARATE `HrotEnvironment.CreateTkb()` instance for
its internal use (a pre-existing design quirk). This means `GhostPromotionSystem` inside NED
uses a different TkbDatabase. This fragmentation is a pre-existing issue; do NOT fix it in this
batch. Focus TKB-015 on registering `context.TkbDb` as the world singleton; TKB-019/020 also
use `context.TkbDb`.

### How `RegisterDomainComponents` receives `context.TkbDb`

`SharedApplicationBootstrapper.BootstrapNode` calls `BuildContext` then `RegisterDomainComponents`.
`BuildContext` returns the `HrotNodeContext` (which has `TkbDb`), but `RegisterDomainComponents`
only receives the `EntityRepository world` parameter. To bridge this gap, store the TkbDb
reference in a private field during `BuildContext`:

```csharp
private ITkbDatabase? _tkbDb;

protected override HrotNodeContext BuildContext(...)
{
    // ... build context as before ...
    var ctx = new HrotNodeBuilder(config)...Build();
    _tkbDb = ctx.TkbDb;   // capture before returning
    return ctx;
}

protected override void RegisterDomainComponents(EntityRepository world)
{
    SimHostComponentRegistry.RegisterAll(world);
    world.SetSingletonManaged<ITkbDatabase>(_tkbDb!);  // TKB-015
}
```

---

## Task A — TKB-015: Register `ITkbDatabase` as ECS singleton in SimHost

**File:** `Hrot/Subsystems/Hrot.SimHost/SimHostNodeBootstrapper.cs`

### Changes

1. Add two private fields near the top of the class (after `_nodeBootstrapper`):
   ```csharp
   private ITkbDatabase? _tkbDb;
   private IReadOnlyList<ITkbEntityTranslator>? _translators;
   ```

2. In `BuildContext`, create the translator list and capture TkbDb BEFORE returning:
   ```csharp
   protected override HrotNodeContext BuildContext(HrotNodeConfig config, NodeRole role, INetworkFactory? networkFactory)
   {
       _translators = new List<ITkbEntityTranslator>
       {
           new VehicleKinematicsTkbTranslator(),
       }.AsReadOnly();

       var ctx = new HrotNodeBuilder(config)
           .WithRole(config.SubsystemName, role)
           .WithNetworkFactory(networkFactory)
           .WithReplication(role)
           .WithBehaviorRegistry(GetBehaviorRegistry())
           .WithTranslators(_translators)   // TKB-022 — threads through to NedReplicationModule
           .Build();

       _tkbDb = ctx.TkbDb;
       return ctx;
   }
   ```

3. In `RegisterDomainComponents`, add singleton registration:
   ```csharp
   protected override void RegisterDomainComponents(EntityRepository world)
   {
       SimHostComponentRegistry.RegisterAll(world);
       world.SetSingletonManaged<ITkbDatabase>(_tkbDb!);  // TKB-015
   }
   ```

4. In `RegisterSpawningPipeline`, wire translators into ELM and NetworkSpawningSystem:
   ```csharp
   var elm = (EntityLifecycleModule)context.BaseModules[0];
   elm.SetTranslators(_translators!);   // TKB-022: set before kernel Initialize

   var spawningSystem = new NetworkSpawningSystem(
       context.TkbDb!,
       elm,
       context.EntityMap,
       context.IdAllocator!,
       context.NodeId,
       translators: _translators,       // TKB-022
       onEntitySpawned: (world, entity, isLocalAuthority) =>
       {
           // existing lambda body unchanged
       });
   ```

5. In `BuildOrchestration` override, pass `_tkbDb` to the inner `_nodeBootstrapper.BuildOrchestration`:
   ```csharp
   var slave = _nodeBootstrapper.BuildOrchestration(
       _role, context.Kernel, context.World, context.NodeId,
       participant:          context.Participant,
       subsystemName:        "SimHost",
       eventBus:             context.EventBus,
       scenarioSerializer:   null,
       localTempRoot:        _localTempRoot,
       tkbDb:                _tkbDb,         // NEW — TKB-020
       checkpointWorker:     CheckpointWorker,
       simGroup:             simGroup,
       lifecycleGroup:       context.NedReplication?.NetworkLifecycleGroup,
       ghostCreationSystem:  context.GhostCreationSystem,
       eventAccumulator:     context.EventAccumulator,
       afterSeek:            (context.NedReplication as Hrot.Common.Abstractions.INedReplicationModule)?.AfterSeekCallback,
       diagnosticsDumpHandler: diagHandler);
   ```

**Required usings to add** (if not already present):
```csharp
using CarKinem.Tkb;           // VehicleKinematicsTkbTranslator
using Fdp.Interfaces;         // ITkbEntityTranslator
using Fdp.Toolkit.Tkb;        // ITkbDatabase
```

---

## Task B — TKB-022: `EntityLifecycleModule.SetTranslators`

**File:** `FDP/Toolkits/Fdp.Toolkits/Lifecycle/EntityLifecycleModule.cs`

The `EntityLifecycleModule` is created inside `HrotNodeBuilder.Build()` BEFORE the caller
knows the translator list. Add a method that can be called before the kernel calls
`RegisterSystems`:

```csharp
/// <summary>
/// Replaces the translator list used by <see cref="Fdp.Toolkit.Lifecycle.Systems.BlueprintApplicationSystem"/>.
/// Must be called before the module host kernel calls <see cref="RegisterSystems"/>.
/// </summary>
public void SetTranslators(IReadOnlyList<ITkbEntityTranslator> translators)
{
    _translators = translators;
}
```

This method sets the `private IReadOnlyList<ITkbEntityTranslator> _translators` field that was
added in BATCH-05.

---

## Task C — TKB-022: `NedReplicationModule` translator threading

**File:** `Hrot/Network/Hrot.Network.NED/Replication/NedReplicationModule.cs`

### Constructor change

Add an optional parameter to the constructor AFTER `lifecycleModule`:

```csharp
public NedReplicationModule(
    DdsParticipant?        participant,
    NodeRole               role,
    NetworkEntityMap       entityMap,
    IGeographicTransform   geoTransform,
    FdpEventBus            eventBus,
    int                    localNodeId,
    int                    domainId,
    BehaviorRegistry?      behaviorRegistry      = null,
    ITkbDatabase?          tkbDb                 = null,
    EntityLifecycleModule? lifecycleModule       = null,
    IReadOnlyList<ITkbEntityTranslator>? tkbEntityTranslators = null)  // NEW
```

### Store the field

Add to the `// ── Ghost lifecycle deps` field group:
```csharp
private readonly IReadOnlyList<ITkbEntityTranslator>? _tkbEntityTranslators;
```

And assign in the constructor body:
```csharp
_tkbEntityTranslators = tkbEntityTranslators;
```

### Wire into GhostPromotionSystem

In `RegisterSystems`, both `GhostPromotionSystem` instantiation sites currently do:
```csharp
registry.RegisterSystem(new GhostPromotionSystem(_tkbDb, _lifecycleModule));
```

Change both to:
```csharp
registry.RegisterSystem(new GhostPromotionSystem(_tkbDb, _lifecycleModule, _tkbEntityTranslators));
```

There are exactly TWO sites in `RegisterSystems` that create `GhostPromotionSystem` — one
inside the `if (pureIgRole)` block and one inside the `if (_roleHasMuscle && ...)` block.
Both must be updated.

### Required using

Add if not already present:
```csharp
using Fdp.Interfaces;   // IReadOnlyList<ITkbEntityTranslator>
```

---

## Task D — TKB-022: `HrotNodeBuilderWithReplication.WithTranslators`

**File:** `Hrot/Network/Hrot.Network.NED/Infrastructure/HrotNodeBuilderReplicationExtensions.cs`

### Add field and method to `HrotNodeBuilderWithReplication`

```csharp
private IReadOnlyList<ITkbEntityTranslator>? _translators;

/// <summary>
/// Specifies the translator list forwarded to <see cref="NedReplicationModule"/> so that
/// <see cref="GhostPromotionSystem"/> receives the same translator instances as
/// <see cref="NetworkSpawningSystem"/> and <see cref="BlueprintApplicationSystem"/>.
/// </summary>
public HrotNodeBuilderWithReplication WithTranslators(IReadOnlyList<ITkbEntityTranslator>? translators)
{
    _translators = translators;
    return this;
}
```

### Thread translators in `Build()`

Inside `HrotNodeBuilderWithReplication.Build()`, the `NedReplicationModule` constructor call
currently ends with:
```csharp
tkbDb:            HrotEnvironment.CreateTkb(),
lifecycleModule:  elm);
```

Add the new parameter:
```csharp
tkbDb:                HrotEnvironment.CreateTkb(),
lifecycleModule:      elm,
tkbEntityTranslators: _translators);
```

There are two places in `HrotNodeBuilderReplicationExtensions.cs` where `NedReplicationModule`
is constructed: in `HrotNodeBuilderWithReplication.Build()` and in
`BindReplicationParticipant()`. Update ONLY the one in `Build()`. The `BindReplicationParticipant`
method is used by IG and does not need translator wiring in this batch.

### Required using

Add if not already present:
```csharp
using Fdp.Interfaces;   // IReadOnlyList<ITkbEntityTranslator>
```

---

## Task E — TKB-019: Implement `TkbLoadClusterStateHandler`

**File to create:**
`Hrot/Subsystems/Hrot.SimHost/Orchestration/Handlers/TkbLoadClusterStateHandler.cs`

The implementation MUST match the DESIGN.md §7.2 spec exactly. Here is the full implementation:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Core.Orchestration;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Tkb;
using Hrot.Map.Common;
using Hrot.Map.Definitions.Tkb;

namespace Hrot.SimHost.Orchestration.Handlers;

/// <summary>
/// Cluster state handler that intercepts <see cref="NodeOpType.PrepareLive"/> and
/// <see cref="NodeOpType.PrepareEdit"/> to load the correct TKB artifact from the
/// node's local staging area before the scenario is deserialized.
///
/// <para>
/// Uses a differential cache keyed on (TkbName, ZIP file timestamp) to avoid
/// unnecessary <see cref="ITkbDatabase.Clear"/> and re-ingestion when the same TKB
/// is loaded for consecutive transitions.
/// </para>
/// <para>
/// If the locally staged scenario header contains no <c>TkbName</c>, the handler
/// falls back to <see cref="NedTkbCatalog.RegisterAll"/> (called only when the
/// database is empty, to preserve any catalog already loaded by a previous
/// successful load).
/// </para>
/// </summary>
public sealed class TkbLoadClusterStateHandler : IClusterStateHandler
{
    private readonly ITkbDatabase _tkbDb;
    private readonly string _localTkbStagingRoot;

    private string? _lastLoadedTkbName;
    private DateTime _lastLoadedTimestamp;

    /// <param name="tkbDb">
    /// The live TKB database shared with <c>NetworkSpawningSystem</c>,
    /// <c>BlueprintApplicationSystem</c>, and <c>GhostPromotionSystem</c>.
    /// </param>
    /// <param name="localStagingRoot">
    /// Root of the node's local staging area (e.g. <c>C:\FDP_Temp</c>).
    /// TKB artifacts are expected under <c>{localStagingRoot}/TKB/</c>.
    /// </param>
    public TkbLoadClusterStateHandler(ITkbDatabase tkbDb, string localStagingRoot)
    {
        _tkbDb = tkbDb ?? throw new ArgumentNullException(nameof(tkbDb));
        _localTkbStagingRoot = Path.Combine(localStagingRoot, "TKB");
    }

    /// <inheritdoc/>
    public bool CanHandle(NodeOpType operation) =>
        operation == NodeOpType.PrepareLive || operation == NodeOpType.PrepareEdit;

    /// <inheritdoc/>
    public Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct)
    {
        // Read TkbName from the node's own locally staged scenario header file.
        string? requestedTkb = ExtractTkbNameFromLocalScenario(_localTkbStagingRoot);

        if (string.IsNullOrWhiteSpace(requestedTkb))
        {
            // No TkbName in local scenario -> use hardcoded fallback catalog.
            // NedTkbCatalog.RegisterAll() is called only if the db is empty to avoid
            // overwriting a previously loaded TKB catalog.
            if (!_tkbDb.GetAll().Any())
                NedTkbCatalog.RegisterAll((TkbDatabase)_tkbDb);
            return Task.FromResult<object?>(null);
        }

        string localPath = Path.Combine(_localTkbStagingRoot, $"{requestedTkb}.zip");

        // Differential cache check using file modification time.
        DateTime currentFileTime = File.Exists(localPath)
            ? File.GetLastWriteTimeUtc(localPath)
            : DateTime.MinValue;

        if (_lastLoadedTkbName == requestedTkb && _lastLoadedTimestamp == currentFileTime)
            return Task.FromResult<object?>(null); // Cache hit — no reload needed.

        if (!File.Exists(localPath))
            throw new FileNotFoundException(
                $"[TkbLoad] TKB artifact not found at '{localPath}'. " +
                "Ensure the TKB file is staged before transitioning to Live/Edit.",
                localPath);

        _tkbDb.Clear();
        using var loader = new TkbUnifiedLoader(localPath);
        var deserializer = new TkbDeserializer();
        foreach (var entityFile in loader.EnumerateEntityFiles())
            deserializer.ParseAndRegister(entityFile, _tkbDb);

        _lastLoadedTkbName = requestedTkb;
        _lastLoadedTimestamp = currentFileTime;
        _tkbDb.ActiveTkbName = requestedTkb;

        FdpLog<TkbLoadClusterStateHandler>.Info(
            "[TkbLoad] Loaded TKB '{0}' ({1} entities).",
            requestedTkb, _tkbDb.GetAll().Count());

        return Task.FromResult<object?>(null);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// No-op: TKB load is fully committed during <see cref="PrepareAsync"/>.
    /// </remarks>
    public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo) { }

    /// <inheritdoc/>
    /// <remarks>
    /// No-op: TKB survives <c>Idle</c> state and is cached across transitions.
    /// A rollback would invalidate the differential cache unnecessarily.
    /// </remarks>
    public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo) { }

    /// <summary>
    /// Peeks the <c>TkbName</c> from the node's locally staged scenario header file
    /// using a forward-only <see cref="Utf8JsonReader"/> — no DOM allocation.
    /// Returns <c>null</c> when the file is absent or does not contain a
    /// <c>TkbName</c> string property.
    /// </summary>
    private static string? ExtractTkbNameFromLocalScenario(string localStagingRoot)
    {
        string headerPath = Path.Combine(localStagingRoot, "ScenarioHeader.json");
        if (!File.Exists(headerPath)) return null;
        var bytes = File.ReadAllBytes(headerPath);
        var reader = new Utf8JsonReader(bytes);
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName &&
                reader.ValueTextEquals("TkbName"))
            {
                reader.Read();
                return reader.TokenType == JsonTokenType.String
                    ? reader.GetString()
                    : null;
            }
        }
        return null;
    }
}
```

### Required using resolution notes

- `Fdp.Toolkit.Tkb` contains `TkbUnifiedLoader`, `TkbDeserializer`, `TkbDatabase`
- `Hrot.Map.Definitions.Tkb` contains `NedTkbCatalog`
- `Fdp.Toolkit.Orchestration` contains `IClusterStateHandler`
- `Fdp.Core.Orchestration` contains `ExecuteNodeOpIntent`, `NodeOpType`
- `Fdp.Core.Logging` contains `FdpLog<T>`

Check the `Hrot.SimHost.csproj` project references — if `Fdp.Toolkit.Tkb` types are not
accessible, trace which assembly they live in from the existing imports in
`HrotScenarioLoadHandler.cs` and add the correct using.

The `NedTkbCatalog` is in namespace `Hrot.Map.Definitions.Tkb` (file: `BdcTkbCatalog.cs`
in `Hrot.Core`). `Hrot.SimHost` references `Hrot.Common` which transitively includes `Hrot.Core`.

---

## Task F — TKB-020: Wire Handler in `NodeBootstrapper.BuildOrchestration`

**File:** `Hrot/Subsystems/Hrot.SimHost/NodeBootstrapper.cs`

### Add parameter

Add `ITkbDatabase? tkbDb = null` as an optional parameter to `BuildOrchestration`. Insert it
AFTER the existing `localTempRoot` parameter:

```csharp
string localTempRoot = @"C:\FDP_Temp",
ITkbDatabase? tkbDb = null,             // NEW — TKB-020: used by TkbLoadClusterStateHandler
CheckpointIOWorker? checkpointWorker = null,
```

Also add the XML doc comment for the new parameter in the method's existing doc block:
```xml
/// <param name="tkbDb">
/// Optional TKB database. When non-null, a <see cref="TkbLoadClusterStateHandler"/>
/// is registered before the scenario handler block so TKB loading precedes scenario
/// deserialization during <c>PrepareLive</c> and <c>PrepareEdit</c>.
/// </param>
```

### Register the handler

In `BuildOrchestration`, register `TkbLoadClusterStateHandler` BEFORE the scenario handler
block. The correct insertion point is AFTER the `clusterSlave.RegisterHandler(new ReferenceArchiveHandler(...))` line and BEFORE the `if (scenarioSerializer != null)` block:

```csharp
// Wire ReferenceArchiveHandler so this node can report .fdp archives to ClusterMaster (CGF1-S0505).
clusterSlave.RegisterHandler(new ReferenceArchiveHandler(localTempRoot, nodeId));

// Wire TkbLoadClusterStateHandler to populate ITkbDatabase before HrotScenarioLoadHandler
// deserializes entities. Must be registered BEFORE the scenario handler block (TKB-020).
if (tkbDb != null)
    clusterSlave.RegisterHandler(new TkbLoadClusterStateHandler(tkbDb, localTempRoot));

// Wire scenario/episode handlers when a serializer is provided.
if (scenarioSerializer != null)
{
    // ... existing code unchanged ...
}
```

### Required usings

Add to `NodeBootstrapper.cs` if not already present:
```csharp
using Fdp.Toolkit.Tkb;                              // ITkbDatabase
using Hrot.SimHost.Orchestration.Handlers;          // TkbLoadClusterStateHandler
```

---

## Tests

### Test file 1: `TkbLoadClusterStateHandlerTests.cs`

**Location:** `Hrot/Subsystems/Hrot.SimHost.Tests/TkbLoadClusterStateHandlerTests.cs`

Write a test class implementing `IDisposable` that creates/tears down a temp directory.
Use `System.IO.Compression.ZipFile` to create minimal test ZIP archives.

```csharp
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core.Orchestration;
using Fdp.Toolkit.Tkb;
using Hrot.SimHost.Orchestration.Handlers;
using Xunit;

namespace Hrot.SimHost.Tests;
```

The test class needs a helper that creates a minimal TKB ZIP. The ZIP need only contain one
JSON file so `TkbUnifiedLoader.EnumerateEntityFiles()` returns something; `TkbDeserializer`
skips unknown descriptor types (it logs a warning but does not throw), so you do NOT need to
register real descriptors:

```csharp
/// Creates a minimal ZIP at <paramref name="path"/> containing one dummy entity file.
private static void CreateMinimalTkbZip(string path, string tkbName)
{
    using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
    var entry = archive.CreateEntry($"{tkbName}/entity.json");
    using var sw = new StreamWriter(entry.Open(), Encoding.UTF8);
    // Unknown type => TkbDeserializer logs warning and skips.
    sw.Write("{\"Name\":\"TestEntity\",\"TkbType\":1,\"UnknownDescriptor\":{}}");
}
```

Write intent helper:
```csharp
private static ExecuteNodeOpIntent MakeIntent(NodeOpType op = NodeOpType.PrepareLive) =>
    new ExecuteNodeOpIntent { Operation = op, TransactionId = Guid.NewGuid() };
```

**Required tests (7 minimum):**

1. `CanHandle_ReturnsTrue_ForPrepareLive`
   ```csharp
   var db = new TkbDatabase();
   var h  = new TkbLoadClusterStateHandler(db, _stagingRoot);
   Assert.True(h.CanHandle(NodeOpType.PrepareLive));
   ```

2. `CanHandle_ReturnsTrue_ForPrepareEdit`
   ```csharp
   Assert.True(h.CanHandle(NodeOpType.PrepareEdit));
   ```

3. `CanHandle_ReturnsFalse_ForOtherOps`
   ```csharp
   Assert.False(h.CanHandle(NodeOpType.FinalizeLive));
   ```

4. `CacheHit_SameTkbAndTimestamp_DoesNotClearDb`
   - Write `ScenarioHeader.json` with `TkbName`
   - Create a real ZIP under `_tkbDir/{TkbName}.zip`
   - Call `PrepareAsync` once (loads TKB)
   - Call `PrepareAsync` again (cache hit)
   - Verify: the db is NOT empty after second call (Clear was not called)
   - To verify Clear was NOT called: count templates after both calls; count should be the
     same as after first call, NOT zero.

5. `CacheMiss_NameChange_ClearsCalled`
   - Create two ZIPs: `Alpha.zip` and `Beta.zip`
   - First PrepareAsync with `Alpha` TkbName
   - Write new `ScenarioHeader.json` with `Beta` TkbName
   - Second PrepareAsync — must call `_tkbDb.Clear()` (db should be non-empty after)
   - Verify `_tkbDb.ActiveTkbName == "Beta"`

6. `AfterSuccessfulLoad_ActiveTkbNameIsSet`
   - Write header with TkbName = "TestTkb"
   - Create `TestTkb.zip`
   - Call `PrepareAsync`
   - Assert: `db.ActiveTkbName == "TestTkb"`

7. `Fallback_NullTkbName_EmptyDb_RegistersNedCatalog`
   - Write `ScenarioHeader.json` WITHOUT a `TkbName` property (or absent file)
   - db is fresh (empty)
   - Call `PrepareAsync`
   - Assert: `db.GetAll().Any()` is true (NedTkbCatalog populated it)

8. `Fallback_NullTkbName_PopulatedDb_DoesNotOverwrite`
   - db already populated via `NedTkbCatalog.RegisterAll(db)`
   - Capture count before
   - Write `ScenarioHeader.json` without TkbName
   - Call `PrepareAsync`
   - Count should be the same (NedTkbCatalog.RegisterAll was NOT called again)
   
   > Implementation note: The current design calls `RegisterAll` only if `!_tkbDb.GetAll().Any()`.
   > To verify it was NOT called again, just verify the count doesn't change (or doesn't double).

9. `MissingZip_ThrowsFileNotFoundException`
   - Write `ScenarioHeader.json` with `TkbName = "MissingFile"`
   - Do NOT create the ZIP
   - `await Assert.ThrowsAsync<FileNotFoundException>(() => h.PrepareAsync(MakeIntent(), CancellationToken.None))`

**Setup/teardown pattern:**
```csharp
private readonly string _stagingRoot;
private readonly string _tkbDir;

public TkbLoadClusterStateHandlerTests()
{
    _stagingRoot = Path.Combine(Path.GetTempPath(), "TkbHandlerTest_" + Guid.NewGuid().ToString("N")[..8]);
    _tkbDir = Path.Combine(_stagingRoot, "TKB");
    Directory.CreateDirectory(_tkbDir);
}

public void Dispose()
{
    if (Directory.Exists(_stagingRoot))
        Directory.Delete(_stagingRoot, recursive: true);
}

private void WriteScenarioHeader(string? tkbName)
{
    string content = tkbName != null
        ? $"{{\"TkbName\":\"{tkbName}\"}}"
        : "{\"SubsystemType\":\"SimHost\"}";
    File.WriteAllText(Path.Combine(_tkbDir, "ScenarioHeader.json"), content, Encoding.UTF8);
}
```

### Test file 2: `TkbDatabaseSingletonTests.cs`

**Location:** `Hrot/Subsystems/Hrot.SimHost.Tests/TkbDatabaseSingletonTests.cs`

Verifies TKB-015: `EntityRepository.SetSingletonManaged<ITkbDatabase>` works correctly and
the correct type is returned. Does NOT test the bootstrapper directly (bootstrapper integration
is covered by existing `SubsystemHeadlessTests`).

```csharp
using Fdp.Core;
using Fdp.Toolkit.Tkb;
using Fdp.Interfaces;
using Xunit;

namespace Hrot.SimHost.Tests;

/// <summary>
/// Verifies TKB-015: <see cref="ITkbDatabase"/> can be registered and retrieved as an
/// ECS world singleton via <see cref="EntityRepository.SetSingletonManaged{T}"/>.
/// </summary>
public class TkbDatabaseSingletonTests
{
    [Fact]
    public void SetSingletonManaged_TkbDatabase_CanBeRetrievedByInterface()
    {
        using var world = new EntityRepository();
        var tkb = new TkbDatabase();
        world.SetSingletonManaged<ITkbDatabase>(tkb);

        var retrieved = world.GetSingletonManaged<ITkbDatabase>();
        Assert.Same(tkb, retrieved);
    }

    [Fact]
    public void SetSingletonManaged_TkbDatabase_SameInstanceAfterRegisterAll()
    {
        using var world = new EntityRepository();
        var tkb = new TkbDatabase();
        world.SetSingletonManaged<ITkbDatabase>(tkb);

        // Simulate SimHostComponentRegistry.RegisterAll being called first.
        SimHostComponentRegistry.RegisterAll(world);

        var retrieved = world.GetSingletonManaged<ITkbDatabase>();
        Assert.Same(tkb, retrieved);
    }
}
```

Wait — the second test needs `SimHostComponentRegistry.RegisterAll` called BEFORE
`SetSingletonManaged` to be realistic. Adjust the order if needed based on the actual
bootstrapper call order (RegisterAll first, then SetSingleton).

### Test file 3: `NedReplicationModuleTranslatorTests.cs`

**Location:** `Hrot/Subsystems/Hrot.SimHost.Tests/NedReplicationModuleTranslatorTests.cs`

Verifies TKB-022 partial: `NedReplicationModule` can be constructed with a translator list
in headless mode (no DDS participant).

```csharp
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Tkb;
using Hrot.Network.Replication;
using CarKinem.Tkb;
using Xunit;

namespace Hrot.SimHost.Tests;

public class NedReplicationModuleTranslatorTests
{
    [Fact]
    public void NedReplicationModule_WithTranslators_ConstructsWithoutThrow()
    {
        var map     = new NetworkEntityMap();
        var bus     = new FdpEventBus();
        var tkb     = new TkbDatabase();
        var translators = new List<ITkbEntityTranslator>
        {
            new VehicleKinematicsTkbTranslator(),
        }.AsReadOnly();

        var ex = Record.Exception(() =>
            new NedReplicationModule(
                participant:          null,
                role:                 NodeRole.MuscleGround,
                entityMap:            map,
                geoTransform:         Hrot.Common.HrotEnvironment.CreateGeoTransform(),
                eventBus:             bus,
                localNodeId:          1,
                domainId:             0,
                tkbEntityTranslators: translators));

        Assert.Null(ex);
    }
}
```

---

## Namespace and using verification

Before compiling, verify these are accessible in their respective projects:

| Symbol | Namespace | Project |
|--------|-----------|---------|
| `VehicleKinematicsTkbTranslator` | `CarKinem.Tkb` | `Fdp.Toolkits` |
| `ITkbEntityTranslator` | `Fdp.Interfaces` | `Fdp.Core` |
| `TkbUnifiedLoader` | `Fdp.Toolkit.Tkb.Vfs` | `Fdp.Toolkits` |
| `TkbDeserializer` | `Fdp.Toolkit.Tkb` | `Fdp.Toolkits` |
| `NedTkbCatalog` | `Hrot.Map.Definitions.Tkb` | `Hrot.Core` |
| `IClusterStateHandler` | `Fdp.Toolkit.Orchestration` | `Fdp.Toolkits` |
| `ExecuteNodeOpIntent` | `Fdp.Core.Orchestration` | `Fdp.Toolkits` |
| `NodeOpType` | `Fdp.Core` (or `Fdp.Core.Orchestration`) | `Fdp.Core` |
| `FdpLog<T>` | `Fdp.Core.Logging` | `Fdp.Core` |

---

## Build & Test Verification

After all changes, run:

```powershell
# Build all solutions (run from workspace root)
cd d:\Work\IOS-IG-SimHost-FDP-2\FDP ; dotnet build FDP.sln -v m 2>&1 | Select-String "error|Build succeeded|Build FAILED" | Select-Object -Last 10
cd d:\Work\IOS-IG-SimHost-FDP-2 ; dotnet build IOS-IG-SimHost.sln -v m 2>&1 | Select-String "error|Build succeeded|Build FAILED" | Select-Object -Last 10
```

```powershell
# Run TKB-scoped tests in FDP
cd d:\Work\IOS-IG-SimHost-FDP-2 ; dotnet test FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj --filter "FullyQualifiedName~Tkb" -v n 2>&1 | Select-String "Passed|Failed|Test Run" | Select-Object -Last 15

# Run Hrot.SimHost.Tests (covers TkbLoad handler tests + singleton tests)
cd d:\Work\IOS-IG-SimHost-FDP-2 ; dotnet test Hrot\Subsystems\Hrot.SimHost.Tests\Hrot.SimHost.Tests.csproj --filter "FullyQualifiedName~Tkb" -v n 2>&1 | Select-String "Passed|Failed|Test Run" | Select-Object -Last 15
```

All existing tests must pass. New tests must all pass.

---

## Report

Write the batch report to `.dev/tkb-1/reports/BATCH-06-REPORT.md` with:
- Summary table of files created/modified
- All new test names and their results
- Any deviations from these instructions and the rationale
- Build output confirming zero errors

---

## Key Invariants

1. Do NOT modify `IgNodeBootstrapper.RegisterDomainComponents` — it already registers ITkbDatabase.
2. Do NOT modify `BindReplicationParticipant` in `HrotNodeBuilderReplicationExtensions.cs`.
3. The translator list `_translators` must be the SAME instance passed to all three systems.
4. `TkbLoadClusterStateHandler._localTkbStagingRoot` = `Path.Combine(localStagingRoot, "TKB")`.
5. `ExtractTkbNameFromLocalScenario` reads `ScenarioHeader.json` from `_localTkbStagingRoot`.
6. Cache key is `(TkbName string equality, DateTime == equality)` — not reference equality.
7. `TkbLoadClusterStateHandler` is registered BEFORE the scenario handler block in `BuildOrchestration`.
8. `Commit()` and `Abort()` are no-ops — no ECS state mutation.
9. Preserve ALL existing comments exactly as-is.
