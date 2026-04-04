# BATCH-10: Feature Switch Implementation + Offline Spawn/Edit/Delete Tests

**Batch Number:** BATCH-10  
**Tasks:** PACK2-C002, PACK2-C003, PACK2-R004  
**Phase:** Phase 5 (Feature Switch) + Phase 6 (Integration Tests)  
**Estimated Effort:** 4–5 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-09 (C001, R003 — composition root + harnesses complete)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch implements the Feature Switch in the HROT Editor — the capability to hot-swap between "Internal FDP SimHost" mode and "External HROT SimHost over DDS" mode using the kernel's RCU (Read-Copy-Update) module lifecycle. It also extends `EditorHarness` with a working `NetworkSpawningSystem` and adds the first real offline integration tests.

### Required Reading (IN ORDER)

1. **Task Definitions:** `.dev/packs-2/TASK-DETAIL.md` — See PACK2-C002, PACK2-C003, PACK2-R004 details  
2. **Design Document:** `.dev/packs-2/DESIGN.md` — §5.D and §5.E (Feature Switch)  
3. **Previous Batch Instructions:** `.dev/packs-2/batches/BATCH-09-INSTRUCTIONS.md`  
4. **Previous Review:** `.dev/packs-2/reviews/BATCH-09-REVIEW.md`

### Source Code Location

- **Feature Switch code:** `Hrot.Editor/`
- **Feature Switch tests:** `Hrot.Editor.Tests/`
- **Harness + integration tests:** `Hrot.ClusterRunner.Integration.Tests/`

### Report Submission

**When done, submit your report to:**  
`.dev/packs-2/reports/BATCH-10-REPORT.md`

**If you have questions, create:**  
`.dev/packs-2/questions/BATCH-10-QUESTIONS.md`

---

## Context

BATCH-09 implemented the all-in-one offline composition root (`Hrot.Editor` EXE, `EditorHarness`). The editor can currently tick frames offline, but has no way to hot-swap to an external HROT SimHost. BATCH-10:

1. **C002+C003:** Adds `SwitchToExternalAsync()` / `SwitchToInternalAsync()` to `EditorApplication`, wires a toolbar toggle, and extends `Program.cs` to pass kernel + pack references so they can be RCU'd at runtime.

2. **R004:** Extends `EditorHarness` with a working `NetworkSpawningSystem` (via `SimHostModule` + `EntityLifecycleModule` + `TkbDatabase`), then adds three offline integration tests that exercise spawn → edit → delete command routing through the kernel.

**Critical architecture fact (do NOT skip):**  
`SimHostCoreLogicPack.RegisterSystems()` is a **NO-OP** — it does NOT include `NetworkSpawningSystem`. The spawn system lives in `SimHostModule(NetworkSpawningSystem)`, which must be explicitly installed as its own kernel module. `EditorHarness` currently does NOT have spawn support — this batch adds it.

---

## 🎯 Batch Objectives

- Implement `SwitchToExternalAsync` / `SwitchToInternalAsync` on `EditorApplication`
- Add `SimHostMode { Internal, External }` enum and `CurrentMode` property
- Wire the feature switch toggle to `EditorToolbarPanel`
- Extend `Program.cs` to pass kernel + logic pack instances to `EditorApplication`
- Extend `EditorHarness` with `SimHostModule` + spawn support + `Editor` property
- Write 3 offline integration tests (spawn, edit, delete) in `OfflineEditorIntegrationTests.cs`
- Write minimal unit tests for C002/C003 feature switch mode tracking

---

## ✅ Tasks

---

### Task 1: Add `SimHostMode` Enum (C002)

**File:** `Hrot.Editor/SimHostMode.cs` (NEW FILE)

```csharp
namespace Hrot.Editor;

/// <summary>
/// Tracks whether the HROT Editor is running with its internal FDP SimHost
/// or connected to an external HROT SimHost over DDS.
/// </summary>
public enum SimHostMode
{
    /// <summary>Local FDP SimHost logic packs are installed and active.</summary>
    Internal = 0,

    /// <summary>Local logic packs are ejected; ACL translator packs are active.</summary>
    External = 1,
}
```

---

### Task 2: Extend `IEditorLogic` Interface (C002 + C003)

**File:** `Hrot.Editor/IEditorLogic.cs` (UPDATE)

Add three members to the interface:

```csharp
/// <summary>
/// Ejects the local FDP SimHost logic packs and (if translator packs are configured)
/// installs the ACL translator packs. No-op when kernel is not configured.
/// </summary>
Task SwitchToExternalAsync();

/// <summary>
/// Uninstals translator packs (if any) and reinstalls the local FDP SimHost logic packs.
/// No-op when kernel is not configured or already in Internal mode.
/// </summary>
Task SwitchToInternalAsync();

/// <summary>Current operational mode of the editor.</summary>
SimHostMode CurrentMode { get; }
```

---

### Task 3: Implement Feature Switch in `EditorApplication` (C002 + C003)

**File:** `Hrot.Editor/EditorApplication.cs` (UPDATE)

#### 3a. Add fields

```csharp
private readonly ModuleHostKernel?          _kernel;
private readonly IReadOnlyList<IEcsModule>? _logicPacks;       // uninstalled on SwitchToExternal
private readonly IReadOnlyList<IEcsModule>? _translatorPacks;  // installed on SwitchToExternal
private SimHostMode _currentMode = SimHostMode.Internal;
```

#### 3b. Extend constructor (add optional params — existing 3-arg signature remains valid)

```csharp
public EditorApplication(
    ScenarioFileService fileService,
    FdpEventBus bus,
    EntityRepository world,
    ModuleHostKernel?          kernel          = null,
    IReadOnlyList<IEcsModule>? logicPacks      = null,
    IReadOnlyList<IEcsModule>? translatorPacks = null)
{
    _fileService      = fileService ?? throw new ArgumentNullException(nameof(fileService));
    _bus              = bus         ?? throw new ArgumentNullException(nameof(bus));
    _world            = world       ?? throw new ArgumentNullException(nameof(world));
    _kernel           = kernel;
    _logicPacks       = logicPacks;
    _translatorPacks  = translatorPacks;
}
```

#### 3c. Implement `CurrentMode` property

```csharp
public SimHostMode CurrentMode => _currentMode;
```

#### 3d. Implement `SwitchToExternalAsync` (C002: uninstall logic packs + C003: install translator packs)

```csharp
public async Task SwitchToExternalAsync()
{
    if (_kernel == null || _logicPacks == null) return;
    if (_currentMode == SimHostMode.External) return;

    await _kernel.UninstallModulesAsync(_logicPacks);

    if (_translatorPacks != null)
        await _kernel.InstallModulesAsync(_translatorPacks);

    _currentMode = SimHostMode.External;
}
```

#### 3e. Implement `SwitchToInternalAsync` (C003: uninstall translator packs + reinstall logic packs)

```csharp
public async Task SwitchToInternalAsync()
{
    if (_kernel == null || _logicPacks == null) return;
    if (_currentMode == SimHostMode.Internal) return;

    if (_translatorPacks != null)
        await _kernel.UninstallModulesAsync(_translatorPacks);

    await _kernel.InstallModulesAsync(_logicPacks);

    _currentMode = SimHostMode.Internal;
}
```

**Important — API note:**  
`UninstallModulesAsync` and `InstallModulesAsync` take `IReadOnlyList<IEcsModule>` of the actual **instances** (not types). The `_logicPacks` list must contain the exact same instances that were registered with `RegisterModule(...)` at startup. Task completion requires `kernel.Update()` calls (the RCU drain completes during the next frame). This is handled by the game loop in `Program.cs` and by `PumpUntil` in tests.

**Required usings to add:**

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using ModuleHost.Core;
using ModuleHost.Core.Abstractions;
```

---

### Task 4: Add Toggle Button to `EditorToolbarPanel` (C003)

**File:** `Hrot.Editor/UI/EditorToolbarPanel.cs` (UPDATE)

Add one testable handler and one ImGui button to `DrawContent`:

```csharp
// ── New handler ───────────────────────────────────────────────────────────────
public void HandleToggleModeClick(IEditorLogic logic)
{
    if (logic.CurrentMode == SimHostMode.Internal)
        _ = logic.SwitchToExternalAsync();   // fire-and-forget; kernel drains during game loop
    else
        _ = logic.SwitchToInternalAsync();
}
```

In `DrawContent`, add after the Route button:

```csharp
ImGui.SameLine();
string modeLabel = logic.CurrentMode == SimHostMode.Internal ? "Go External" : "Go Internal";
if (ImGui.Button(modeLabel)) HandleToggleModeClick(logic);
```

---

### Task 5: Update `Program.cs` to Pass Kernel + Packs (C002)

**File:** `Hrot.Editor/Program.cs` (UPDATE)

Currently, pack instances are created and passed anonymously to `RegisterModule`. Change the module registration section (step 4) to retain named references, and pass them to `EditorApplication`:

**Before (current, lines ~31–36):**
```csharp
kernel.RegisterModule(new SimHostCoreLogicPack(entityMap));
kernel.RegisterModule(new CgfLogicPack(doctrineRegistry, entityMap));
kernel.RegisterModule(new OrchestrationLogicPack(clusterSlave));
kernel.RegisterModule(new ScenarioEditorModule(fileService));
```

**After:**
```csharp
// ── 4a. Named pack instances for feature-switch RCU ────────────────────────
var simHostCorePack = new SimHostCoreLogicPack(entityMap);
var cgfLogicPackInst = new CgfLogicPack(doctrineRegistry, entityMap);
var orchPack        = new OrchestrationLogicPack(clusterSlave);
var scenarioMod     = new ScenarioEditorModule(fileService);

kernel.RegisterModule(simHostCorePack);
kernel.RegisterModule(cgfLogicPackInst);
kernel.RegisterModule(orchPack);
kernel.RegisterModule(scenarioMod);

// ── 4b. Logic-pack list used by EditorApplication.SwitchToExternalAsync ───
var logicPacks = new List<IEcsModule> { simHostCorePack, cgfLogicPackInst };
```

**And update the EditorApplication construction (step 6):**

**Before:**
```csharp
var app   = new EditorApplication(fileService, world.Bus, world);
```

**After:**
```csharp
var app = new EditorApplication(fileService, world.Bus, world, kernel, logicPacks);
```

Add `using System.Collections.Generic;` and `using ModuleHost.Core.Abstractions;` at the top if not already present.

---

### Task 6: Feature Switch Unit Tests (C002 + C003)

**File:** `Hrot.Editor.Tests/FeatureSwitchTests.cs` (NEW FILE)

Write 3 unit tests that cover the mode-tracking logic **without** requiring a full running kernel. The DDS-based behavioural tests (spawn reaching DDS, external mode full picture) belong to R005.

```csharp
using System.Threading.Tasks;
using Hrot.Editor;
using Hrot.ScenarioEditor.Services;
using Fdp.Kernel;
using Xunit;

namespace Hrot.Editor.Tests;

public class FeatureSwitchTests
{
    private static EditorApplication BuildMinimalApp()
    {
        var world       = new EntityRepository();
        var fileService = EditorBootstrap.CreateFileService();
        // Minimal 3-arg constructor — no kernel, no packs (no-op mode)
        return new EditorApplication(fileService, world.Bus, world);
    }

    [Fact]
    public void InitialMode_IsInternal()
    {
        var app = BuildMinimalApp();
        Assert.Equal(SimHostMode.Internal, app.CurrentMode);
    }

    [Fact]
    public async Task SwitchToExternal_NullKernel_IsNoOp_AndDoesNotThrow()
    {
        var app = BuildMinimalApp();
        // Should complete synchronously without throwing
        await app.SwitchToExternalAsync();
        // Mode stays Internal because kernel is null (guard returns early)
        Assert.Equal(SimHostMode.Internal, app.CurrentMode);
    }

    [Fact]
    public async Task SwitchToInternal_NullKernel_IsNoOp_AndDoesNotThrow()
    {
        var app = BuildMinimalApp();
        await app.SwitchToInternalAsync();
        Assert.Equal(SimHostMode.Internal, app.CurrentMode);
    }
}
```

**Note:** Tests for `EditorToolbarPanel.HandleToggleModeClick` may be added to the existing `EditorToolbarPanelTests.cs` if desired, using a mock `IEditorLogic` (Moq is available).

---

### Task 7: Extend `EditorHarness` with Spawn Support (R004)

**File:** `Hrot.ClusterRunner.Integration.Tests/EditorHarness.cs` (UPDATE)

This is the most significant change in the batch. Add a working `NetworkSpawningSystem` + `SimHostModule` + `EntityLifecycleModule` to the harness, plus expose the `Editor` (IEditorLogic) and `EntityMap` properties.

#### 7a. Add a project reference to `Hrot.Editor`

In `Hrot.ClusterRunner.Integration.Tests/Hrot.ClusterRunner.Integration.Tests.csproj`, add:

```xml
<ProjectReference Include="..\Hrot.Editor\Hrot.Editor.csproj" />
```

(This allows `EditorHarness` to instantiate `EditorApplication` for test-harness use.)

#### 7b. Add a nested `SequentialIdAllocator` stub

Inside the `EditorHarness` class (or as a file-scoped private class), add:

```csharp
private sealed class SequentialIdAllocator : INetworkIdAllocator
{
    private long _next = 1000;
    public long AllocateId()           => _next++;
    public void Reset(long startId = 0) => _next = startId;
    public void Dispose() { }
}
```

#### 7c. Add new fields and properties

```csharp
private readonly SequentialIdAllocator _idAllocator;

public NetworkEntityMap EntityMap { get; }
public IEditorLogic     Editor    { get; }
```

(`NetworkEntityMap` is already imported. `IEditorLogic` requires `using Hrot.Editor;`.)

#### 7d. Extend the constructor

The full revised constructor (keep the same class outline, just replace the constructor body and field list):

```csharp
public EditorHarness()
{
    Repo   = new EntityRepository();
    Bus    = Repo.Bus;

    var accumulator = new EventAccumulator();
    Kernel = new ModuleHostKernel(Repo, accumulator);

    // Stepping time controller — offline, no DDS sync
    var stepping = new SteppingTimeController(new GlobalTime { TimeScale = 1.0f });
    _stepping = stepping;
    Kernel.SetTimeController(stepping);

    EntityMap = new NetworkEntityMap();

    var doctrineRegistry = new DoctrineRegistry();
    var clusterSlave     = new ClusterSlave(0, "EditorHarness");
    var fileService      = EditorBootstrap.CreateFileService();

    // ── TKB + ELM + spawn system ──────────────────────────────────────────
    var tkbDb   = new TkbDatabase();
    tkbDb.Register(new TkbTemplate("TestUnit", tkbType: 1L));   // offline test entity type

    var elm        = new EntityLifecycleModule(tkbDb, Array.Empty<int>());
    _idAllocator   = new SequentialIdAllocator();
    var spawnSys   = new NetworkSpawningSystem(tkbDb, elm, EntityMap, _idAllocator, localNodeId: 0);

    // ── Module registration (offline — no translator packs) ───────────────
    var simHostCorePack = new SimHostCoreLogicPack(EntityMap);
    var cgfLogicPackInst = new CgfLogicPack(doctrineRegistry, EntityMap);
    var scenarioMod     = new ScenarioEditorModule(fileService);

    Kernel.RegisterModule(simHostCorePack);
    Kernel.RegisterModule(cgfLogicPackInst);
    Kernel.RegisterModule(scenarioMod);
    Kernel.RegisterModule(elm);
    Kernel.RegisterModule(new SimHostModule(spawnSys));

    Kernel.Initialize();

    // ── Editor application facade ─────────────────────────────────────────
    var logicPacks = new List<IEcsModule> { simHostCorePack, cgfLogicPackInst };
    Editor = new EditorApplication(fileService, Bus, Repo, Kernel, logicPacks);
}
```

**Required new using directives:**

```csharp
using System.Collections.Generic;
using Fdp.Interfaces;                              // TkbTemplate
using FDP.Toolkit.Lifecycle;                       // EntityLifecycleModule
using FDP.Toolkit.NetworkSpawning.Systems;         // NetworkSpawningSystem
using Fdp.Toolkit.Tkb;                             // TkbDatabase
using Hrot.Editor;                                 // EditorApplication, IEditorLogic
using Hrot.SimHost.Modules;                        // SimHostModule
using ModuleHost.Core.Network.Interfaces;          // INetworkIdAllocator
```

**Also add to `Dispose`:**

```csharp
_idAllocator.Dispose();
```

#### 7e. Notes on spawn behavior

- **Spawn:** `Bus.PublishManaged(new SpawnEntityCommand { TkbType = 1L, OwnerNodeId = 0, InitType = ReliableInitType.None, NetworkId = <nonzero> })` — entity appears in `Repo` within 1 pump frame. Use `NetworkId = 42L` (deterministic) so tests can look up the entity via `EntityMap.TryGetEntity(42L, out var entity)`.
- **Delete:** After `DestroyEntityCommand`, entity is removed from `Repo` within 2–3 pump frames (ELM drains asynchronously). Use `PumpUntil(() => Repo.EntityCount == 0)` with a 5 s timeout.
- **Update:** After `UpdateEntityCommand`, component values are applied within 1 pump frame. Use `PumpUntil(() => { if (!EntityMap.TryGetEntity(id, out var e)) return false; ref readonly var t = ref Repo.GetComponentRO<SimTransform>(e); return MathF.Abs(t.Position.Y - expected) < 0.01f; })`.

---

### Task 8: Create `OfflineEditorIntegrationTests` (R004)

**File:** `Hrot.ClusterRunner.Integration.Tests/OfflineEditorIntegrationTests.cs` (NEW FILE)

Create three independent tests. Each instantiates its own `EditorHarness` (no shared state).

#### Full file listing:

```csharp
using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Kernel;
using FDP.Toolkit.NetworkSpawning.Events;
using Hrot.Map.Common.Dds;
using Hrot.NED.Messages;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// PACK2-R004 — IT-1: Offline Editor integration tests.
/// Exercises spawn / edit / delete command routing via EditorHarness
/// without any DDS participant. Asserts that no DDS writer is ever called.
/// </summary>
public sealed class OfflineEditorIntegrationTests
{
    // ── Test double: counts DDS write calls ──────────────────────────────────

    private sealed class RecordingDdsWriter : IDdsWriter<CreateEntityRequest>
    {
        public int CallCount { get; private set; }
        public void Write(CreateEntityRequest sample) => CallCount++;
        public void DisposeInstance(CreateEntityRequest key) { }
    }

    // ── Test constants ────────────────────────────────────────────────────────

    private const long  TestTkbType    = 1L;   // matches TkbTemplate registered in EditorHarness
    private const long  TestNetworkId  = 42L;  // deterministic; non-zero = no allocator call
    private const int   PumpTimeoutMs  = 5_000;

    // =========================================================================
    // IT-1a: Spawn
    // =========================================================================

    [Fact]
    public void SpawnCommand_LocalRepo_NoNetworkTraffic()
    {
        var writer = new RecordingDdsWriter();

        using var harness = new EditorHarness();

        harness.Bus.PublishManaged(new SpawnEntityCommand
        {
            TkbType     = TestTkbType,
            NetworkId   = TestNetworkId,
            OwnerNodeId = 0,
            InitType    = FDP.Toolkit.Replication.Components.ReliableInitType.None,
        });

        bool appeared = harness.PumpUntil(() => harness.Repo.EntityCount == 1, PumpTimeoutMs);

        Assert.True(appeared, "Entity should appear in the local repo within 5 s");
        Assert.Equal(1, harness.Repo.EntityCount);
        Assert.Equal(0, writer.CallCount); // no DDS translator packs installed → zero writes
    }

    // =========================================================================
    // IT-1b: Edit
    // =========================================================================

    [Fact]
    public void EditCommand_UpdatesRepoInPlace()
    {
        var writer = new RecordingDdsWriter();
        const float NorthOffsetMetres = 100f;

        using var harness = new EditorHarness();

        // 1. Spawn
        harness.Bus.PublishManaged(new SpawnEntityCommand
        {
            TkbType     = TestTkbType,
            NetworkId   = TestNetworkId,
            OwnerNodeId = 0,
            InitType    = FDP.Toolkit.Replication.Components.ReliableInitType.None,
            InitialTransform = new SimTransform { Position = Vector3.Zero },
        });
        Assert.True(harness.PumpUntil(() => harness.Repo.EntityCount == 1, PumpTimeoutMs));

        // 2. Edit — shift 100 m north (Y axis)
        harness.Bus.PublishManaged(new UpdateEntityCommand
        {
            NetworkId          = TestNetworkId,
            ComponentsToUpdate = new List<object>
            {
                new SimTransform { Position = new Vector3(0f, NorthOffsetMetres, 0f) }
            },
        });

        // 3. Assert position updated
        bool updated = harness.PumpUntil(() =>
        {
            if (!harness.EntityMap.TryGetEntity(TestNetworkId, out var e)) return false;
            ref readonly var t = ref harness.Repo.GetComponentRO<SimTransform>(e);
            return MathF.Abs(t.Position.Y - NorthOffsetMetres) < 0.01f;
        }, PumpTimeoutMs);

        Assert.True(updated, "SimTransform.Position.Y should reflect the 100 m north offset");
        Assert.Equal(0, writer.CallCount);
    }

    // =========================================================================
    // IT-1c: Delete
    // =========================================================================

    [Fact]
    public void DeleteCommand_RemovesEntityFromRepo()
    {
        var writer = new RecordingDdsWriter();

        using var harness = new EditorHarness();

        // 1. Spawn
        harness.Bus.PublishManaged(new SpawnEntityCommand
        {
            TkbType     = TestTkbType,
            NetworkId   = TestNetworkId,
            OwnerNodeId = 0,
            InitType    = FDP.Toolkit.Replication.Components.ReliableInitType.None,
        });
        Assert.True(harness.PumpUntil(() => harness.Repo.EntityCount == 1, PumpTimeoutMs));

        // 2. Delete
        harness.Bus.PublishManaged(new DestroyEntityCommand
        {
            NetworkId = TestNetworkId,
            Reason    = "test-delete",
        });

        // 3. Assert removal (ELM takes 2–3 frames — PumpUntil handles this)
        bool removed = harness.PumpUntil(() => harness.Repo.EntityCount == 0, PumpTimeoutMs);

        Assert.True(removed, "Entity should be removed from repo within 5 s");
        Assert.Equal(0, harness.Repo.EntityCount);
        Assert.Equal(0, writer.CallCount);
    }
}
```

**Namespace notes:**
- `ReliableInitType` is in `FDP.Toolkit.Replication.Components` — use the fully qualified name or add `using FDP.Toolkit.Replication.Components;`
- `CreateEntityRequest` is in `Hrot.NED.Messages` — use `using Hrot.NED.Messages;` (project already references `Hrot.NED`)
- `IDdsWriter<T>` is in `Hrot.Map.Common.Dds` — already referenced via `Hrot.Map.Common`
- `SimTransform` is in `Fdp.Kernel` — already in scope

---

## 🔎 File Change Summary

| File | Change |
|------|--------|
| `Hrot.Editor/SimHostMode.cs` | NEW — `SimHostMode { Internal, External }` enum |
| `Hrot.Editor/IEditorLogic.cs` | UPDATE — add `SwitchToExternalAsync()`, `SwitchToInternalAsync()`, `CurrentMode` |
| `Hrot.Editor/EditorApplication.cs` | UPDATE — new fields, extended constructor, two async switch methods |
| `Hrot.Editor/UI/EditorToolbarPanel.cs` | UPDATE — `HandleToggleModeClick` + toggle ImGui button |
| `Hrot.Editor/Program.cs` | UPDATE — named pack instances + pass kernel+logicPacks to EditorApplication |
| `Hrot.Editor.Tests/FeatureSwitchTests.cs` | NEW — 3 unit tests for mode tracking |
| `Hrot.ClusterRunner.Integration.Tests/EditorHarness.cs` | UPDATE — add SimHostModule + spawn support + Editor + EntityMap |
| `Hrot.ClusterRunner.Integration.Tests/Hrot.ClusterRunner.Integration.Tests.csproj` | UPDATE — add reference to `Hrot.Editor` |
| `Hrot.ClusterRunner.Integration.Tests/OfflineEditorIntegrationTests.cs` | NEW — 3 integration tests (spawn/edit/delete) |

---

## ⚠️ Known Pitfalls

1. **`UninstallModulesAsync` takes instances, not types.** The task spec's reference to `typeof(SimHostCoreLogicPack)` in the C002 description is conceptual only. The actual API (`IReadOnlyList<IEcsModule>`) requires the same object instances that were passed to `RegisterModule`.

2. **`UninstallModulesAsync` needs pump frames to drain.** The Task returned by `SwitchToExternalAsync` / `SwitchToInternalAsync` won't complete until `kernel.Update()` is called at least once (the kernel drains during Update). In `Program.cs`'s Raylib game loop this is automatic. In tests, use `harness.PumpUntil(() => switchTask.IsCompleted, timeoutMs: 5000)`.

3. **`TkbType = 0` in `TkbTemplate` throws** an `ArgumentException`. Use `tkbType: 1L` for tests.

4. **Spawn is silently skipped if TkbType is not registered.** If you use `TkbType` values other than `1L` in tests, register a matching template first.

5. **`SimTransform.Position.Y` = north (not latitude).** The assertion `Position.Y ≈ 100` means 100 metres north in flat-Earth Cartesian space — not degrees.

6. **ELM destruction takes 2–3 frames offline.** After publishing `DestroyEntityCommand`, the ELM lifecycle completes in the next frame (no peer ACKs needed since `participatingModuleIds = Array.Empty<int>()`). Always use `PumpUntil` for post-destroy assertions.

7. **`NetworkSpawningSystem` is in `Hrot.SimHost` transitively** via `Hrot.ClusterRunner`. No extra csproj reference should be needed, but if compilation fails for `SimHostModule` or `EntityLifecycleModule`, add explicit project references:
   - `Hrot.SimHost` → `<ProjectReference Include="..\Hrot.SimHost\Hrot.SimHost.csproj" />`
   - `FDP.Toolkit.Lifecycle` → `<ProjectReference Include="..\FDP\Toolkits\FDP.Toolkit.Lifecycle\FDP.Toolkit.Lifecycle.csproj" />`

---

## 🧪 Testing Requirements

**Minimum test counts:**

| Project | Tests Before | Tests Added | Expected |
|---------|-------------|------------|---------|
| `Hrot.Editor.Tests` | 17 | +3 (FeatureSwitchTests) | 20 |
| `Hrot.ClusterRunner.Integration.Tests` | ~7 | +3 (OfflineEditorIntegrationTests) | ~10 |

**Quality:** Each integration test must use `PumpUntil` with explicit `timeoutMs`. No `Thread.Sleep` or `PumpFrames(n)` with hardcoded frame counts.

**Isolation:** All 3 offline integration tests must pass in any order with no shared state (each creates its own `EditorHarness`).

---

## 📊 Report Requirements

Submit `.dev/packs-2/reports/BATCH-10-REPORT.md` with:

**Q1:** Did `UninstallModulesAsync` / `InstallModulesAsync` behave as expected in tests? Did you need to pump frames to make the Task complete?

**Q2:** Did `EntityLifecycleModule` drain destruction within 2–3 pump frames, or did you need to adjust the `PumpUntil` timeout?

**Q3:** Were there any missing project references that needed to be added to the csproj files?

**Q4:** Any issues with transitive namespace resolution for `TkbDatabase`, `EntityLifecycleModule`, or `SimHostModule`?

**Q5:** What design decisions did you make beyond the instructions? Suggested commit message for this batch?

---

## 🎯 Success Criteria

This batch is DONE when:

- [ ] `SimHostMode` enum exists in `Hrot.Editor`
- [ ] `IEditorLogic` has `SwitchToExternalAsync()`, `SwitchToInternalAsync()`, `CurrentMode`
- [ ] `EditorApplication.SwitchToExternalAsync()` calls `kernel.UninstallModulesAsync(logicPacks)`
- [ ] `EditorApplication.SwitchToExternalAsync()` calls `kernel.InstallModulesAsync(translatorPacks)` when non-null
- [ ] `EditorApplication.SwitchToInternalAsync()` reverses the switch
- [ ] `EditorToolbarPanel` has a mode-toggle button that calls `HandleToggleModeClick`
- [ ] `Program.cs` passes named kernel + logicPacks to `EditorApplication`
- [ ] `EditorHarness` exposes `Editor` (IEditorLogic) and `EntityMap` (NetworkEntityMap)
- [ ] `EditorHarness` has working `NetworkSpawningSystem` (via `SimHostModule` + ELM + TKB)
- [ ] `OfflineEditorIntegrationTests` has 3 passing tests (spawn/edit/delete)
- [ ] `FeatureSwitchTests` has 3 passing unit tests
- [ ] `dotnet test Hrot.Editor.Tests` passes (≥ 20 tests)
- [ ] `dotnet test Hrot.ClusterRunner.Integration.Tests` passes (≥ 10 tests, DDS tests may skip)
- [ ] `dotnet build Hrot.Editor` passes with zero errors

---
