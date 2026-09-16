using System.IO;
using System.Text.Json;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning.Events;
using Hrot.Common.Events;
using Fdp.Toolkit.Replication;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>PACK2-R005 Part A â€” IT-2: Editor file I/O integration tests.</summary>
[Collection("EditorOfflineTests")]
public sealed class EditorFileIOIntegrationTests
{
    private const int PumpMs = 5_000;

    // â”€â”€ IT-2a â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
        bool eventFired = harness.Bus.Read<WorldResetEvent>().Length > 0;

        Assert.True(eventFired, "WorldResetEvent must fire on NewScenario");
        Assert.Equal(0, harness.Repo.EntityCount);
    }

    // â”€â”€ IT-2b â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

            // ⭐⭐ QA-016 — the subsystem type lives in the `$meta` ENVELOPE now, not under `Header`.
            //
            // ⛔ This used to read `Header.SubsystemType`. 📐 ScenarioSerializer.Serialize:199-200 writes
            //    `Header` with only `TkbName` and then `JsonEnvelope.Write(root, new DocumentMeta(...))`
            //    — and the serializer's own doc-comment calls `Header.SubsystemType` the LEGACY shape
            //    that the LOAD path still accepts for old files. So a freshly SAVED file has never
            //    carried it, and this assertion could not pass.
            //
            // ⚠ The old code also swallowed its own fallback: `if (!TryGetProperty("Header", out h))
            //    TryGetProperty("header", out h);` ignores the second result, so when both missed, the
            //    next call threw "Operation is not valid due to the current state of the object" —
            //    a message that says nothing about a moved field. Read the envelope, then fall back.
            string? subsysType = null;

            if (doc.RootElement.TryGetProperty("$meta", out var meta)
                && meta.TryGetProperty("docType", out var docType))
            {
                subsysType = docType.GetString();
            }
            else if (doc.RootElement.TryGetProperty("Header", out var header)
                  || doc.RootElement.TryGetProperty("header", out header))
            {
                if (header.TryGetProperty("SubsystemType", out var typeElem)
                 || header.TryGetProperty("subsystemType", out typeElem))
                {
                    subsysType = typeElem.GetString();
                }
            }

            Assert.Equal("Hrot.Scenario", subsysType);
        }
        finally { File.Delete(tempPath); }
    }

    // â”€â”€ IT-2c â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

            // ⭐ HN-037 Part B: the direct load path is gone; the claim under test was always the HEADER
            //   check, so it targets that directly. ⛔ Not weakened — ValidateSubsystemType IS the code the
            //   removed method ran, now public for exactly this reason.
            var ex = Record.Exception(
                () => Hrot.ScenarioEditor.Services.ScenarioFileService.ValidateSubsystemType(
                    File.ReadAllText(tempPath)));
            Assert.Null(ex);
        }
        finally { File.Delete(tempPath); }
    }

    // â”€â”€ IT-2d â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

            Assert.Throws<System.InvalidOperationException>(
                () => Hrot.ScenarioEditor.Services.ScenarioFileService.ValidateSubsystemType(
                    File.ReadAllText(tempPath)));
            // ⭐ And the world is untouched — validation is a pure header check, so nothing was cleared.
            Assert.Equal(0, harness.Repo.EntityCount);
        }
        finally { File.Delete(tempPath); }
    }
}

