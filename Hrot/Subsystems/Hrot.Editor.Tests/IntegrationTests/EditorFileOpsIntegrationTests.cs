using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Fdp.Core;
using Fdp.Core.Serialization.Migrations;
using Fdp.Toolkit.Scenario;
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
        return new EditorApplication(fileService, bus, new FdpEventBus(), repo);
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

        var app = CreateApp(repo);
        app.NewScenario();

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

        // $meta.docType is "Hrot.Scenario" (not "Hrot.Editor" or anything else).
        var meta = doc.RootElement.GetProperty("$meta");
        Assert.Equal("Hrot.Scenario",
            meta.GetProperty("docType").GetString());

        // Entity count in the Entities object equals repo entity count.
        var entities = doc.RootElement.GetProperty("Entities");
        Assert.Equal(5, entities.EnumerateObject().Count());

        repo.Dispose();
    }

    // ── PACK2-F004: Load Scenario (round-trip) ───────────────────────────────

    /// <summary>
    /// F004-1: Load round-trip preserves entity count and TestVector3 component values.
    /// </summary>
    [Fact]
    public void A_save_load_round_trip_preserves_entity_count_and_components()
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
        // ⭐ HN-037 Part B: EditorApplication.LoadScenario (the direct file→repo facade) is gone; the
        //   round-trip it was standing in front of is the SERIALIZER's, so the test asks it directly.
        var loadRepo = CreateRepo();
        EditorBootstrap.CreateFileService();   // keeps the serializer registration path exercised
        new ScenarioSerializerBuilder("Hrot.Scenario").Build()
            .Deserialize(loadRepo, File.ReadAllText(_tempFile));

        Assert.Equal(5, loadRepo.EntityCount);

        // Collect all TestVector3 values and sort by X.
        var actual = new System.Collections.Generic.List<TestVector3>();
        for (int i = 0; i <= loadRepo.MaxEntityIndex; i++)
        {
            var e = new Entity(i, loadRepo.GetEntityIndex().GetMetadata(i).Generation);
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
    /// F004-2: the world boundary resets <c>GlobalTime.TotalTime</c> to zero.
    /// <para>⭐ HN-037 Part B: this used to be asserted through <c>LoadScenario</c>. With that facade gone,
    /// <c>NewScenario</c> is the world boundary that owns the reset — and it is the SAME line of code
    /// (<c>SetSingletonUnmanaged(default(GlobalTime))</c>) the removed method ran. ⛔ Not weakened: it is
    /// also the boundary the real load goes through, since <c>EditorApplication.Update</c> calls
    /// <c>NewScenario()</c> before dispatching the load transition.</para>
    /// </summary>
    [Fact]
    public void The_world_boundary_resets_global_time()
    {
        var repo = CreateRepo();
        repo.CreateEntity();
        repo.SetSingletonUnmanaged(new GlobalTime { TotalTime = 99.0, TimeScale = 1.0f });

        var app = CreateApp(repo);
        app.NewScenario();

        Assert.Equal(0.0, repo.GetSingletonUnmanaged<GlobalTime>().TotalTime, precision: 6);

        repo.Dispose();
    }

    /// <summary>
    /// F004-3: Loading a file with an unrecognised docType throws MigrationException;
    /// repo is left empty (migration throws before SoftClear is reached).
    /// </summary>
    [Fact]
    public void An_unrecognised_doc_type_is_refused_before_anything_is_mutated()
    {
        // Write a JSON file with an unknown $meta docType.
        var badJson = """
            {
              "$meta": { "docType": "SomeOtherApp", "schemaVersion": 1 },
              "Entities": {}
            }
            """;
        File.WriteAllText(_tempFile, badJson);

        var repo = CreateRepo();

        // ⭐ HN-037 Part B: the rejection is the MIGRATION ADAPTER's, and the removed LoadScenario was only
        //   the caller that reached it. ⛔ Retargeted rather than deleted — this is the only rail on
        //   "an unknown docType is refused before anything is mutated".
        // ⚠ Worth recording: the migration-aware ScenarioFileService is TEST-ONLY. 📐 Measured `2026-08-24`,
        //   EditorBootstrap.CreateFileService has zero production callers and EditorSubsystem builds the
        //   service with migrationServices: null — so this adapter never ran in the shipped editor, before
        //   or after Part B. Wiring migration into the genesis load path is filed as an open finding.
        var migrations = EditorBootstrap.CreateMigrationServices();

        Assert.Throws<MigrationException>(() =>
            migrations.Persistent.LoadAndMigrateAsync(_tempFile).GetAwaiter().GetResult());

        // Nothing was mutated — the refusal happens before any world touch.
        Assert.Equal(0, repo.EntityCount);

        repo.Dispose();
    }

    /// <summary>
    /// F004-4: Loading a file saved with "Hrot.SimHost" SubsystemType succeeds —
    /// cross-app compatibility.
    /// </summary>
    [Fact]
    public void A_HrotSimHost_labelled_file_is_accepted_and_deserializes()
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

        // Load with the Editor's accepted-types check plus the serializer.
        // ⭐ Two claims, and Part B splits them onto the two components that own them: the HEADER is accepted
        //   (ScenarioFileService.ValidateSubsystemType lists Hrot.SimHost), and the CONTENT deserializes.
        var loadRepo = CreateRepo();
        ScenarioFileService.ValidateSubsystemType(File.ReadAllText(_tempFile));
        new ScenarioSerializerBuilder("Hrot.Scenario").Build()
            .Deserialize(loadRepo, File.ReadAllText(_tempFile));

        Assert.Equal(2, loadRepo.EntityCount);

        loadRepo.Dispose();
    }
}
