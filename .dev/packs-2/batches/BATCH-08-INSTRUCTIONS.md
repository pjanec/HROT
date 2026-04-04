# BATCH-08 Instructions

**Batch:** BATCH-08  
**Developer:** GitHub Copilot  
**Tasks:** PACK2-F002 · PACK2-F003 · PACK2-F004  
**Branch:** main (append directly)

---

## Context

- `ScenarioFileService` (in `Hrot.ScenarioEditor/Services/`) is the file-layer service implementing `NewScenario`, `SaveScenario`, and `LoadScenario`.
- `EditorApplication` is the `IEditorLogic` implementation in `Hrot.Editor/`; it delegates all file ops to `ScenarioFileService`.
- `EditorBootstrap.CreateFileService()` builds a `ScenarioFileService` with a `ScenarioSerializerBuilder("Hrot.Scenario")`.
- F002–F004 are integration tests (not unit tests). They call the real `EditorApplication` → `ScenarioFileService` → `ScenarioSerializer` chain, hitting the actual file system with temp files.
- Tests go in **`Hrot.Editor.Tests/`** (already exists; already referenced in `IOS-IG-SimHost.sln`).
- Use `ComponentTypeRegistry.Clear()` in test constructor and `Dispose()` to isolate the static component registry between tests. This is already the pattern in `Hrot.ScenarioEditor.Tests/ScenarioFileServiceTests.cs`.
- `SoftClear()` on `EntityRepository` does NOT reset singletons. The `_singletons` array persists. Therefore `ScenarioFileService.NewScenario` must be extended to explicitly zero out the `GlobalTime` singleton.
- `GlobalTime` is a singleton struct in `Fdp.Kernel`. The task spec's "`GlobalTime.T == 0.0f`" refers to `TotalTime` (the total elapsed simulation time field of type `double`).
- Use blittable test-only structs with `[ComponentId(222)]` and `[ComponentId(223)]` for round-trip tests — do NOT try to use production components like `SimTransform` or `RoutePlan` which may not be accessible without extra project references.

---

## Task A: PACK2-F002 — Extend `NewScenario` to Reset `GlobalTime`

### A.1 — Modify `Hrot.ScenarioEditor/Services/ScenarioFileService.cs`

In `NewScenario(EntityRepository repo)`, after `repo.SoftClear()`, add a `GlobalTime` singleton reset:

```csharp
public void NewScenario(EntityRepository repo)
{
    if (repo == null) throw new ArgumentNullException(nameof(repo));
    FireWorldReset();
    repo.SoftClear();
    // Reset simulation time: SoftClear() does not touch singletons.
    if (repo.HasSingletonUnmanaged<GlobalTime>())
        repo.SetSingletonUnmanaged(default(GlobalTime));
}
```

`GlobalTime` is already in scope because `Hrot.ScenarioEditor` imports `Fdp.Kernel` (it uses `EntityRepository`). No new `using` is needed beyond what is already present.

Also apply the same GlobalTime reset in `LoadScenario` (after `repo.SoftClear()`), using the same pattern:

```csharp
FireWorldReset();
repo.SoftClear();
if (repo.HasSingletonUnmanaged<GlobalTime>())
    repo.SetSingletonUnmanaged(default(GlobalTime));
_serializer.Deserialize(repo, jsonText);
```

### A.2 — Add F002 integration test to `Hrot.Editor.Tests/`

Create `Hrot.Editor.Tests/IntegrationTests/EditorFileOpsIntegrationTests.cs`.

> The test component IDs must not collide with `SaveablePosition` (220) used in `Hrot.ScenarioEditor.Tests/ScenarioFileServiceTests.cs`. Since `ComponentTypeRegistry.Clear()` is called in each test's setup/teardown, IDs 222 and 223 are safe.

Full file:

```csharp
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Fdp.Kernel;
using FDP.Toolkit.Scenario;
using Hrot.Editor;
using Hrot.ScenarioEditor.Services;
using Xunit;

namespace Hrot.Editor.Tests.IntegrationTests;

/// <summary>
/// Integration tests for the Editor file operations via <see cref="IEditorLogic"/>:
/// PACK2-F002 (New), PACK2-F003 (Save), PACK2-F004 (Load round-trip).
/// </summary>
public sealed class EditorFileOpsIntegrationTests : IDisposable
{
    // ── Test component types ─────────────────────────────────────────────────

    /// <summary>Round-trip payload component for integration tests.</summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(222)]
    private struct TestVector3
    {
        public float X;
        public float Y;
        public float Z;
    }

    /// <summary>Secondary payload to verify multi-component round-trips.</summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(223)]
    private struct TestTag
    {
        public int Value;
    }

    // ── Setup / Teardown ─────────────────────────────────────────────────────

    private readonly string _tempFile;

    public EditorFileOpsIntegrationTests()
    {
        ComponentTypeRegistry.Clear();
        _tempFile = Path.GetTempFileName();
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
        ComponentTypeRegistry.Clear();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static EntityRepository CreateRepo()
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<TestVector3>();
        repo.RegisterComponent<TestTag>();
        return repo;
    }

    private static EditorApplication CreateApp(EntityRepository repo)
    {
        var fileService = EditorBootstrap.CreateFileService();
        var bus         = new FdpEventBus();
        return new EditorApplication(fileService, bus, repo);
    }

    // ── PACK2-F002: New Scenario ─────────────────────────────────────────────

    /// <summary>
    /// F002-1: HandleNewClick via IEditorLogic facade empties the repo
    /// and resets GlobalTime.TotalTime to zero.
    /// </summary>
    [Fact]
    public void NewScenario_EmptiesRepo_AndResetsGlobalTime()
    {
        var repo = CreateRepo();
        for (int i = 0; i < 10; i++) repo.CreateEntity();
        Assert.Equal(10, repo.EntityCount); // pre-condition

        // Seed a non-zero GlobalTime singleton.
        repo.SetSingletonUnmanaged(new GlobalTime { TotalTime = 42.0, TimeScale = 1.0f, DeltaTime = 0.016f });

        var app   = CreateApp(repo);
        var panel = new ScenarioBrowserPanel();
        panel.HandleNewClick(app);

        Assert.Equal(0, repo.EntityCount);
        Assert.True(repo.HasSingletonUnmanaged<GlobalTime>(),
            "GlobalTime singleton should still be registered after reset.");
        Assert.Equal(0.0, repo.GetSingletonUnmanaged<GlobalTime>().TotalTime, precision: 6);

        repo.Dispose();
    }

    /// <summary>
    /// F002-2: NewScenario when GlobalTime was never set does not throw.
    /// </summary>
    [Fact]
    public void NewScenario_WithoutGlobalTime_DoesNotThrow()
    {
        var repo = CreateRepo();
        repo.CreateEntity();

        var app = CreateApp(repo);
        app.NewScenario(); // called directly — no GlobalTime singleton registered

        Assert.Equal(0, repo.EntityCount);

        repo.Dispose();
    }

    // ── PACK2-F003: Save Scenario ────────────────────────────────────────────

    /// <summary>
    /// F003-1: SaveScenario writes a valid JSON file with the correct SubsystemType header
    /// and an entity array matching the repo entity count.
    /// </summary>
    [Fact]
    public void SaveScenario_WritesValidJson_WithCorrectHeaderAndEntityCount()
    {
        var repo = CreateRepo();
        for (int i = 0; i < 5; i++)
        {
            var e = repo.CreateEntity();
            repo.SetComponent(e, new TestVector3 { X = i * 10f, Y = i * 20f, Z = i * 30f });
            repo.SetComponent(e, new TestTag     { Value = i + 1 });
        }

        var app = CreateApp(repo);
        app.SaveScenario(_tempFile);

        // File exists and is non-empty.
        Assert.True(File.Exists(_tempFile), "Saved file should exist.");
        var json = File.ReadAllText(_tempFile);
        Assert.False(string.IsNullOrWhiteSpace(json), "Saved file should not be empty.");

        // JSON is parseable.
        using var doc = JsonDocument.Parse(json);

        // Header.SubsystemType is "Hrot.Scenario" (not "Hrot.Editor" or anything else).
        var header = doc.RootElement.GetProperty("Header");
        Assert.Equal("Hrot.Scenario",
            header.GetProperty("SubsystemType").GetString());

        // Entity array length equals repo entity count.
        var entities = doc.RootElement.GetProperty("Entities");
        Assert.Equal(5, entities.GetArrayLength());

        repo.Dispose();
    }

    // ── PACK2-F004: Load Scenario (round-trip) ───────────────────────────────

    /// <summary>
    /// F004-1: Load round-trip preserves entity count and TestVector3 component values.
    /// </summary>
    [Fact]
    public void LoadScenario_RoundTrip_PreservesEntityCountAndComponents()
    {
        // — Save phase —
        var saveRepo = CreateRepo();
        var expected = new TestVector3[5];
        for (int i = 0; i < 5; i++)
        {
            var e = saveRepo.CreateEntity();
            expected[i] = new TestVector3 { X = i + 1.5f, Y = i + 2.5f, Z = i + 3.5f };
            saveRepo.SetComponent(e, expected[i]);
        }

        var saveApp = CreateApp(saveRepo);
        saveApp.SaveScenario(_tempFile);
        saveRepo.Dispose();

        // — Load phase —
        var loadRepo = CreateRepo();
        var loadApp  = CreateApp(loadRepo);
        loadApp.LoadScenario(_tempFile);

        Assert.Equal(5, loadRepo.EntityCount);

        // Collect all TestVector3 values and sort by X.
        var actual = new System.Collections.Generic.List<TestVector3>();
        for (int i = 0; i <= loadRepo.MaxEntityIndex; i++)
        {
            var e = new Entity(i, loadRepo.GetHeader(i).Generation);
            if (!loadRepo.IsAlive(e)) continue;
            if (loadRepo.HasComponent<TestVector3>(e))
                actual.Add(loadRepo.GetComponent<TestVector3>(e));
        }
        actual.Sort((a, b) => a.X.CompareTo(b.X));

        Assert.Equal(5, actual.Count);
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(i + 1.5f, actual[i].X, precision: 4);
            Assert.Equal(i + 2.5f, actual[i].Y, precision: 4);
            Assert.Equal(i + 3.5f, actual[i].Z, precision: 4);
        }

        loadRepo.Dispose();
    }

    /// <summary>
    /// F004-2: LoadScenario also resets GlobalTime.TotalTime to zero.
    /// </summary>
    [Fact]
    public void LoadScenario_ResetsGlobalTime()
    {
        // Prepare a valid saved file.
        var saveRepo = CreateRepo();
        saveRepo.CreateEntity();
        var saveApp = CreateApp(saveRepo);
        saveApp.SaveScenario(_tempFile);
        saveRepo.Dispose();

        // Load into a repo with a pre-seeded non-zero GlobalTime.
        var loadRepo = CreateRepo();
        loadRepo.SetSingletonUnmanaged(new GlobalTime { TotalTime = 99.0, TimeScale = 1.0f });

        var loadApp = CreateApp(loadRepo);
        loadApp.LoadScenario(_tempFile);

        Assert.Equal(0.0, loadRepo.GetSingletonUnmanaged<GlobalTime>().TotalTime, precision: 6);

        loadRepo.Dispose();
    }

    /// <summary>
    /// F004-3: Loading a file with an unrecognised SubsystemType throws;
    /// repo is left empty (validation happens before SoftClear).
    /// </summary>
    [Fact]
    public void LoadScenario_UnrecognisedSubsystemType_Throws_AndLeavesRepoEmpty()
    {
        // Write a JSON file with an unknown SubsystemType.
        var badJson = """
            {
              "Header": { "SubsystemType": "SomeOtherApp", "Version": 1 },
              "Entities": []
            }
            """;
        File.WriteAllText(_tempFile, badJson);

        var repo = CreateRepo();
        var app  = CreateApp(repo);

        // Should throw InvalidOperationException.
        Assert.Throws<InvalidOperationException>(() => app.LoadScenario(_tempFile));

        // Repo is still empty — SoftClear was not reached.
        Assert.Equal(0, repo.EntityCount);

        repo.Dispose();
    }

    /// <summary>
    /// F004-4: Loading a file saved with "Hrot.SimHost" SubsystemType succeeds —
    /// cross-app compatibility.
    /// </summary>
    [Fact]
    public void LoadScenario_HrotSimHostSubsystemType_Succeeds()
    {
        // Build a valid 2-entity file but label it "Hrot.SimHost".
        var saveRepo = CreateRepo();
        saveRepo.CreateEntity();
        saveRepo.CreateEntity();

        // Use a serializer that stamps "Hrot.SimHost".
        var simHostSerializer = new ScenarioSerializerBuilder("Hrot.SimHost").Build();
        var simHostFileService = new ScenarioFileService(simHostSerializer);
        simHostFileService.SaveScenario(saveRepo, _tempFile);
        saveRepo.Dispose();

        // Load with the Editor's file service (accepts "Hrot.SimHost").
        var loadRepo = CreateRepo();
        var loadApp  = CreateApp(loadRepo);
        loadApp.LoadScenario(_tempFile);

        Assert.Equal(2, loadRepo.EntityCount);

        loadRepo.Dispose();
    }
}
```

---

## Testing Summary

| Suite | Expected delta | Details |
|-------|---------------|---------|
| `Hrot.Editor.Tests` | +6 tests (was 8, now 14) | 6 new integration tests in `IntegrationTests/` sub-namespace |
| `Hrot.ScenarioEditor.Tests` | 0 delta (14 still) | No new tests; `NewScenario` behavior change is covered by existing `NewScenario_ClearsRepo_AndFiresObserver` test which will still pass (it doesn't assert GlobalTime) |

> **Zero regressions required.** Run `dotnet test Hrot.Editor.Tests` and `dotnet test Hrot.ScenarioEditor.Tests` — both must be green.

---

## Build & Verify Commands

```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2

# Build
dotnet build Hrot.ScenarioEditor/Hrot.ScenarioEditor.csproj --no-restore -warnaserror
dotnet build Hrot.Editor/Hrot.Editor.csproj --no-restore -warnaserror
dotnet build Hrot.Editor.Tests/Hrot.Editor.Tests.csproj --no-restore -warnaserror

# Test
dotnet test Hrot.ScenarioEditor.Tests/Hrot.ScenarioEditor.Tests.csproj --no-build
dotnet test Hrot.Editor.Tests/Hrot.Editor.Tests.csproj --no-build
```

---

## Report

Submit your report to `.dev/packs-2/reports/BATCH-08-REPORT.md`.

Include:
1. Final test counts for `Hrot.Editor.Tests` and `Hrot.ScenarioEditor.Tests`.
2. Any deviations from the instructions (e.g. if `Assert.Equal(..., precision: 4)` overload didn't compile — some xunit versions use `tolerance` or `delta`).
3. Confirmation that `GlobalTime.TotalTime` is correctly zero after `NewScenario` and `LoadScenario`.
