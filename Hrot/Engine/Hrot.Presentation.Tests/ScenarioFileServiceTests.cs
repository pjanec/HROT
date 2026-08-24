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

        // ⭐ HN-037 Part B: the round-trip is the SERIALIZER's job, and always was. ScenarioFileService's
        //   direct LoadScenario was removed with the facade it served; this test keeps its full coverage by
        //   asking the component that actually does the work.
        var freshRepo = CreateRepoWithPosition();
        serializer.Deserialize(freshRepo, File.ReadAllText(_tempFile));

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

    // ⛔ Test 3 — `LoadScenario_FiresResetBeforePopulate` was DELETED `2026-08-24` (HN-037 Part B).
    //    It asserted that LoadScenario fires the reset observer before repopulating; with that method gone,
    //    the observer contract is exercised in full by Test 2 (NewScenario), which is the only remaining
    //    caller. ⛔ Deleted rather than retargeted: a copy of Test 2 under a load-shaped name would look
    //    like coverage of something that no longer exists.
    //    ⭐ The behaviour it cared about — "the world is wiped before the new one arrives" — is now asserted
    //    where it can actually break: DeterminismRails.A_reload_in_one_process_repeats_the_authored_ids.

    /// <summary>
    /// Test 4 — Subsystem type mismatch is refused (ScenarioFileService.ValidateSubsystemType).
    /// </summary>
    [Fact]
    public void An_unrecognized_subsystem_type_is_refused()
    {
        var json = """{"Header":{"SubsystemType":"Hrot.OtherApp","SchemaVersion":1},"Entities":{}}""";
        File.WriteAllText(_tempFile, json);

        // ⭐ Targets the header check DIRECTLY. It was a private step of the removed LoadScenario and is
        //   public now precisely so this coverage survives — the genesis loader answers the same question
        //   by SKIPPING a non-matching file, so this is the only throwing form.
        Assert.Throws<InvalidOperationException>(
            () => ScenarioFileService.ValidateSubsystemType(File.ReadAllText(_tempFile)));
    }

    /// <summary>
    /// Test 5 — SubsystemType "Hrot.SimHost" is accepted (cross-app compatibility).
    /// </summary>
    [Fact]
    public void A_SimHost_subsystem_type_is_accepted()
    {
        var json = """{"Header":{"SubsystemType":"Hrot.SimHost","SchemaVersion":1},"Entities":{}}""";
        File.WriteAllText(_tempFile, json);

        var ex = Record.Exception(
            () => ScenarioFileService.ValidateSubsystemType(File.ReadAllText(_tempFile)));

        Assert.Null(ex);
    }
}
