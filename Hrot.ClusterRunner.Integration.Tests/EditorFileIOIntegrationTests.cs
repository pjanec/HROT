using System.IO;
using System.Text.Json;
using Fdp.Kernel;
using FDP.Toolkit.NetworkSpawning.Events;
using Hrot.ScenarioEditor.Events;
using ModuleHost.Core.Network.Interfaces;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>PACK2-R005 Part A — IT-2: Editor file I/O integration tests.</summary>
[Collection("EditorOfflineTests")]
public sealed class EditorFileIOIntegrationTests
{
    private const int PumpMs = 5_000;

    // ── IT-2a ─────────────────────────────────────────────────────────────────

    [Fact]
    public void NewScenario_FiresWorldResetEventBeforeClear()
    {
        using var harness = new EditorHarness();

        // Spawn an entity so repo is non-empty
        harness.Bus.PublishManaged(new SpawnEntityCommand
        {
            TkbType = 1L, NetworkId = 1L, OwnerNodeId = 0,
            InitType = ReliableInitType.None,
        });
        Assert.True(harness.PumpUntil(() => harness.Repo.EntityCount == 1, PumpMs));

        harness.Editor.NewScenario();

        // Check GlobalTime was reset by NewScenario (before the next kernel update restores controller time)
        if (harness.Repo.HasSingletonUnmanaged<GlobalTime>())
        {
            var gt = harness.Repo.GetSingletonUnmanaged<GlobalTime>();
            Assert.Equal(0.0, gt.TotalTime, precision: 6);
        }

        harness.PumpFrames(1);  // flush any pending command buffer entries; also triggers SwapBuffers

        // WorldResetEvent must be visible in the read buffer after the swap
        bool eventFired = harness.Bus.ConsumeManaged<WorldResetEvent>().Count > 0;

        Assert.True(eventFired, "WorldResetEvent must fire on NewScenario");
        Assert.Equal(0, harness.Repo.EntityCount);
    }

    // ── IT-2b ─────────────────────────────────────────────────────────────────

    [Fact]
    public void SaveScenario_SubsystemTypeIsHrotScenario()
    {
        using var harness = new EditorHarness();
        var tempPath = Path.GetTempFileName();
        try
        {
            harness.Editor.SaveScenario(tempPath);

            var json = File.ReadAllText(tempPath);
            using var doc = JsonDocument.Parse(json);
            var subsysType = doc.RootElement
                               .GetProperty("Header")
                               .GetProperty("SubsystemType")
                               .GetString();

            Assert.Equal("Hrot.Scenario", subsysType);
        }
        finally { File.Delete(tempPath); }
    }

    // ── IT-2c ─────────────────────────────────────────────────────────────────

    [Fact]
    public void LoadScenario_AcceptsHrotSimHostFile()
    {
        using var harness = new EditorHarness();
        var tempPath = Path.GetTempFileName();
        try
        {
            // Construct a minimal valid file with Hrot.SimHost header and no entities
            var minimalJson = """
                {
                  "Header": { "SubsystemType": "Hrot.SimHost", "Version": "1.0" },
                  "Entities": {}
                }
                """;
            File.WriteAllText(tempPath, minimalJson);

            var ex = Record.Exception(() => harness.Editor.LoadScenario(tempPath));
            Assert.Null(ex);
        }
        finally { File.Delete(tempPath); }
    }

    // ── IT-2d ─────────────────────────────────────────────────────────────────

    [Fact]
    public void LoadScenario_RejectsUnknownSubsystemType()
    {
        using var harness = new EditorHarness();
        var tempPath = Path.GetTempFileName();
        try
        {
            var badJson = """
                {
                  "Header": { "SubsystemType": "UnknownApp", "Version": "1.0" },
                  "Entities": {}
                }
                """;
            File.WriteAllText(tempPath, badJson);

            Assert.Throws<System.InvalidOperationException>(() => harness.Editor.LoadScenario(tempPath));
            // Repo should remain empty (validation happens before clear)
            Assert.Equal(0, harness.Repo.EntityCount);
        }
        finally { File.Delete(tempPath); }
    }
}
