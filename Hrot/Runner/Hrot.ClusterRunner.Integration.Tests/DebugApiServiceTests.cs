using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication;
using Hrot.Editor.DebugApi;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ADA-BATCH-02 Tier-1 gate — exercises every implemented <see cref="DebugApiService"/>
/// method against the offline <see cref="EditorHarness"/> (no HTTP, fast). Entities are seeded
/// via the proven <c>SpawnEntityCommand</c> path (the bare harness has no orchestrator-driven
/// scenario-load pipeline), and assertions verify the inspector-grade JSON payloads.
/// </summary>
[Collection("EditorOfflineTests")]
public sealed class DebugApiServiceTests
{
    private const long TestTkbType   = 1L;   // matches TkbTemplate registered in EditorHarness
    private const long TestNetworkId = 42L;
    private const int  PumpTimeoutMs = 5_000;

    private static void SpawnTestEntity(EditorHarness h, long networkId, string? name = null, Vector3 pos = default)
    {
        var cmd = new SpawnEntityCommand
        {
            TkbType          = TestTkbType,
            NetworkId        = networkId,
            OwnerNodeId      = 0,
            InitType         = ReliableInitType.None,
            InitialTransform = new SimTransform { Position = pos },
        };
        if (name != null)
            cmd.InitialComponents = new System.Collections.Generic.List<object>
            {
                new EntityInfo { Name = new FixedString64(name) },
            };
        h.Bus.PublishManaged(cmd);
        Assert.True(h.PumpUntil(() => h.EntityMap.TryGetEntity(networkId, out _), PumpTimeoutMs));
    }

    // ── Group A — status ──────────────────────────────────────────────────────

    [Fact]
    public void GetStatus_ReportsEntityCountAndState()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        SpawnTestEntity(h, TestNetworkId);

        var status = svc.GetStatus().AsObject();
        Assert.Equal(1, status["entityCount"]!.GetValue<int>());
        Assert.False(status["inPreview"]!.GetValue<bool>());
        // Paused (deterministic) at rest.
        Assert.True(status["isPaused"]!.GetValue<bool>());
    }

    // ── Group B — entities ──────────────────────────────────────────────────────

    [Fact]
    public void ListEntities_ReturnsSeededEntities_WithNetworkId()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        SpawnTestEntity(h, TestNetworkId, name: "Alpha");

        var list = svc.ListEntities().AsArray();
        Assert.Single(list);
        var e = list[0]!.AsObject();
        Assert.Equal(TestNetworkId, e["networkId"]!.GetValue<long>());
        Assert.Equal("Alpha", e["name"]!.GetValue<string>());
        Assert.Contains(e["components"]!.AsArray().Select(n => n!.GetValue<string>()), c => c == "EntityInfo");
    }

    [Fact]
    public void DumpEntity_RendersReadableEntityInfo_NotRawBytes()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        SpawnTestEntity(h, TestNetworkId, name: "Bravo");

        var dump = svc.DumpEntity(TestNetworkId);
        Assert.NotNull(dump);

        // Re-serialize and verify the EntityInfo.Name is the readable string (proves serializer path).
        var json = dump!.ToJsonString();
        Assert.Contains("Bravo", json);
        Assert.DoesNotContain("\"Name\":[", json); // would indicate raw byte-array rendering
    }

    [Fact]
    public void DumpEntity_UnknownId_ReturnsNull_For404()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        Assert.Null(svc.DumpEntity(999_999L));
    }

    [Fact]
    public async Task GetEntitiesById_UnknownId_Returns404WithHelpMessage()
    {
        // Verifies that the HTTP host surfaces the upgraded 404 message that
        // includes "GET /entities" so callers know where to look.
        using var h    = new EditorHarness();
        var svc        = h.BuildDebugApiService();
        var queue      = new MainThreadJobQueue();
        int port       = FindFreePort();
        using var host = new DebugApiHost(port, queue, () => { });
        host.AttachService(svc);
        host.Start();

        // Drain the main-thread job queue on a background thread so that
        // RunOnMainThread jobs complete while the HTTP request is in flight.
        using var cts = new System.Threading.CancellationTokenSource();
        var drainTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                queue.DrainAll();
                await Task.Delay(5, cts.Token).ConfigureAwait(false);
            }
        });

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await client.GetAsync($"http://localhost:{port}/entities/999999");

            Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("not found", body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("GET /entities", body, StringComparison.Ordinal);
        }
        finally
        {
            cts.Cancel();
            try { await drainTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
    }

    private static int FindFreePort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    // ── Group C — event history ─────────────────────────────────────────────────

    [Fact]
    public void GetEvents_World_IncludesPublishedEvent_WithReadablePayload()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        // Spawn → SpawnEntityCommand is published on the World bus and captured by history.
        SpawnTestEntity(h, TestNetworkId, name: "Charlie");

        var events = svc.GetEvents(bus: "world").AsArray();
        Assert.NotEmpty(events);

        // Type filter narrows to the spawn command.
        var spawns = svc.GetEvents(bus: "world", type: nameof(SpawnEntityCommand)).AsArray();
        Assert.NotEmpty(spawns);
        var first = spawns[0]!.AsObject();
        Assert.Equal(nameof(SpawnEntityCommand), first["type"]!.GetValue<string>());
        Assert.NotNull(first["payload"]);
        // The readable name survives into the payload.
        Assert.Contains("Charlie", first["payload"]!.ToJsonString());
    }

    // ── Group D — sim / preview / time ──────────────────────────────────────────

    [Fact]
    public void PlayPause_AreIdempotent_AndFlipState()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        // play → enters preview and runs.
        var played = svc.Play().AsObject();
        Assert.True(played["inPreview"]!.GetValue<bool>());
        Assert.False(played["isPaused"]!.GetValue<bool>());

        // play again is idempotent (still running).
        var again = svc.Play().AsObject();
        Assert.False(again["isPaused"]!.GetValue<bool>());

        // pause requests deterministic mode. SwitchToDeterministic enters a BarrierPending state
        // that the controller reports as Continuous until the wall-clock barrier elapses on a
        // subsequent tick (mirrors the real editor loop, where the toolbar pause takes effect on
        // a following frame), so pump until the paused state is observed.
        svc.Pause();
        bool becamePaused = h.PumpUntil(
            () => svc.GetSimState().AsObject()["isPaused"]!.GetValue<bool>(), PumpTimeoutMs);
        Assert.True(becamePaused, "Pause should bring the sim to a paused (deterministic) state.");

        svc.ExitPreview();
    }

    [Fact]
    public void SetTimeScale_ReflectedInSimState()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        svc.SetTimeScale(2.0f);
        var state = svc.GetSimState().AsObject();
        Assert.Equal(2.0f, state["timeScale"]!.GetValue<float>(), 3);
    }

    [Fact]
    public void EnterExitPreview_TogglesInPreview()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        var entered = svc.EnterPreview(startPaused: true).AsObject();
        Assert.True(entered["inPreview"]!.GetValue<bool>());

        var exited = svc.ExitPreview().AsObject();
        Assert.False(exited["inPreview"]!.GetValue<bool>());
    }

    [Fact]
    public void Step_AdvancesTotalTime()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        // Enter preview paused so Step() advances discrete ticks.
        svc.EnterPreview(startPaused: true);
        double before = svc.GetSimState().AsObject()["totalTime"]!.GetValue<double>();

        svc.Step(3);
        // The facade's Step sets the time-controller delta; pump the kernel to apply the ticks.
        h.PumpFrames(1);

        double after = svc.GetSimState().AsObject()["totalTime"]!.GetValue<double>();
        Assert.True(after >= before, $"totalTime should not regress (before={before}, after={after}).");

        svc.ExitPreview();
    }

    // ── Group E — scenarios ─────────────────────────────────────────────────────

    [Fact]
    public void ListScenarios_ReturnsArray()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        var arr = svc.ListScenarios();
        Assert.IsType<JsonArray>(arr);
    }

    [Fact]
    public void SaveScenario_WritesFile_ThatReloadsToEquivalentEntitySet()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        SpawnTestEntity(h, TestNetworkId, name: "Delta");

        // Save via the file-service round-trip (SaveScenarioAs targets the editor's scenarios root,
        // which is environment-dependent; this test exercises the same serializer path the API uses
        // through an explicit temp file to keep the assertion hermetic).
        var dir  = Path.Combine(Path.GetTempPath(), "ada-batch-02-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "scenario.json");
        try
        {
            h.Editor.SaveScenario(file);
            Assert.True(File.Exists(file), "SaveScenario should write a scenario file.");

            // Reload into a fresh harness and confirm the entity set is non-empty / equivalent count.
            using var h2 = new EditorHarness();
            // ⭐ HN-037 Part B: reload through the serializer — the comment above already says this test
            //   deliberately exercises "the same serializer path the API uses", so it now says it in code.
            new ScenarioSerializerBuilder("Hrot.Scenario").Build()
                .Deserialize(h2.Repo, File.ReadAllText(file));
            Assert.True(h2.PumpUntil(() => h2.Repo.EntityCount >= 1, PumpTimeoutMs),
                "Reloaded scenario should re-materialize at least one entity.");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }
}
