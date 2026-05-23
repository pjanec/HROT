using System;
using System.IO;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Scenario;
using Hrot.ScenarioEditor.Services;
using Xunit;

namespace Hrot.ScenarioEditor.Tests;

/// <summary>
/// Unit tests for <see cref="ScenarioFileService"/> (PACK2-E004).
/// </summary>
public sealed class ScenarioFileServiceTests : IDisposable
{
    // ── Test component ────────────────────────────────────────────────────────

    /// <summary>Simple saveable position component for round-trip tests.</summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(220)]
    private struct SaveablePosition
    {
        public float X;
        public float Y;
        public float Z;
    }

    // ── Setup / Teardown ──────────────────────────────────────────────────────

    private readonly string _tempFile;

    public ScenarioFileServiceTests()
    {
        // Isolate the component registry state from other tests in the suite.
        ComponentTypeRegistry.Clear();
        _tempFile = Path.GetTempFileName();
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
        ComponentTypeRegistry.Clear();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ScenarioSerializer BuildSerializer(string subsystemType = "Hrot.Scenario")
    {
        var builder = new ScenarioSerializerBuilder(subsystemType);
        return builder.Build();
    }

    private static EntityRepository CreateRepoWithPosition()
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<SaveablePosition>();
        return repo;
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Test 1 — Save/Load round-trip: two entities with distinct positions are
    /// serialized and then reloaded into a fresh repo with matching positions.
    /// </summary>
    [Fact]
    public void SaveLoad_RoundTrip_PreservesEntitiesAndComponents()
    {
        var repo = CreateRepoWithPosition();
        var e1   = repo.CreateEntity(); repo.SetComponent(e1, new SaveablePosition { X = 1f, Y = 2f, Z = 3f });
        var e2   = repo.CreateEntity(); repo.SetComponent(e2, new SaveablePosition { X = 4f, Y = 5f, Z = 6f });

        var serializer  = BuildSerializer();
        var fileService = new ScenarioFileService(serializer);

        fileService.SaveScenario(repo, _tempFile);

        var freshRepo = CreateRepoWithPosition();
        fileService.LoadScenario(freshRepo, _tempFile);

        Assert.Equal(2, freshRepo.EntityCount);

        var positions = new System.Collections.Generic.List<SaveablePosition>();
        for (int i = 0; i <= freshRepo.MaxEntityIndex; i++)
        {
            var e = new Entity(i, freshRepo.GetEntityIndex().GetMetadata(i).Generation);
            if (!freshRepo.IsAlive(e)) continue;
            if (freshRepo.HasComponent<SaveablePosition>(e))
                positions.Add(freshRepo.GetComponent<SaveablePosition>(e));
        }
        positions.Sort((a, b) => a.X.CompareTo(b.X));

        Assert.Equal(2, positions.Count);
        Assert.Equal(1f, positions[0].X); Assert.Equal(2f, positions[0].Y);
        Assert.Equal(4f, positions[1].X); Assert.Equal(5f, positions[1].Y);

        freshRepo.Dispose();
        repo.Dispose();
    }

    /// <summary>
    /// Test 2 — NewScenario clears repo and fires the registered observer.
    /// </summary>
    [Fact]
    public void NewScenario_ClearsRepo_AndFiresObserver()
    {
        var repo        = CreateRepoWithPosition();
        repo.CreateEntity();
        repo.CreateEntity();
        repo.CreateEntity();

        var serializer  = BuildSerializer();
        var fileService = new ScenarioFileService(serializer);

        bool observerCalled = false;
        fileService.RegisterWorldResetObserver(() => observerCalled = true);

        fileService.NewScenario(repo);

        Assert.True(observerCalled);
        Assert.Equal(0, repo.EntityCount);

        repo.Dispose();
    }

    /// <summary>
    /// Test 3 — LoadScenario fires reset observer before populating the repo.
    /// </summary>
    [Fact]
    public void LoadScenario_FiresResetBeforePopulate()
    {
        // Save a 2-entity scenario to file
        var sourceRepo = CreateRepoWithPosition();
        sourceRepo.CreateEntity();
        sourceRepo.CreateEntity();

        var serializer  = BuildSerializer();
        var fileService = new ScenarioFileService(serializer);
        fileService.SaveScenario(sourceRepo, _tempFile);

        // Target repo starts with 5 entities
        var targetRepo = CreateRepoWithPosition();
        for (int i = 0; i < 5; i++) targetRepo.CreateEntity();

        bool observerFired = false;
        fileService.RegisterWorldResetObserver(() => observerFired = true);

        fileService.LoadScenario(targetRepo, _tempFile);

        Assert.True(observerFired);
        Assert.Equal(2, targetRepo.EntityCount);

        sourceRepo.Dispose();
        targetRepo.Dispose();
    }

    /// <summary>
    /// Test 4 — Subsystem type mismatch throws InvalidOperationException.
    /// </summary>
    [Fact]
    public void LoadScenario_UnrecognizedSubsystemType_Throws()
    {
        var json = """{"Header":{"SubsystemType":"Hrot.OtherApp","SchemaVersion":1},"Entities":{}}""";
        File.WriteAllText(_tempFile, json);

        var serializer  = BuildSerializer();
        var fileService = new ScenarioFileService(serializer);
        var repo        = CreateRepoWithPosition();

        Assert.Throws<InvalidOperationException>(() => fileService.LoadScenario(repo, _tempFile));

        repo.Dispose();
    }

    /// <summary>
    /// Test 5 — A file with SubsystemType "Hrot.SimHost" is accepted (cross-app compatibility).
    /// </summary>
    [Fact]
    public void LoadScenario_SimHostSubsystemType_DoesNotThrow()
    {
        var json = """{"Header":{"SubsystemType":"Hrot.SimHost","SchemaVersion":1},"Entities":{}}""";
        File.WriteAllText(_tempFile, json);

        var serializer  = BuildSerializer();
        var fileService = new ScenarioFileService(serializer);
        var repo        = CreateRepoWithPosition();

        var ex = Record.Exception(() => fileService.LoadScenario(repo, _tempFile));

        Assert.Null(ex);

        repo.Dispose();
    }
}
