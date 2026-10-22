using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core.FlightRecorder;
using Fdp.Core.FlightRecorder.Metadata;
using Fdp.Toolkit.Orchestration;
using Hrot.NED.Descriptors.Orchestration;
using Xunit;
using Xunit.Abstractions;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;
using ClusterState  = Hrot.NED.Descriptors.Orchestration.ClusterState;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ⭐⭐ <c>CE-3299</c> B3 (R-3044, D3) — a checkpoint on a LIVE cluster: the master names one wall time, EVERY recording node writes a
/// named keyframe at it (nothing pauses), and a collect exports each as a one-frame recording the replay code reads.
/// 📄 docs/DESIGN_Checkpoint_And_Dry_Run_Restore.md §1g. Also prints M5 — the frame time around the checkpoint.
/// </summary>
[Collection("HeavyE2ETests")]
public sealed class CheckpointClusterTests : IDisposable
{
    private const int DomainId = 216;   // CycloneDDS accepts 0–232; 214–219 used by no other rail (grep of fixed ids, 2026-10-22)

    private readonly string _scenarioId = "tt_checkpoint_" + Guid.NewGuid().ToString("N");
    private readonly string _label = "b3-" + Guid.NewGuid().ToString("N")[..8];
    private readonly ITestOutputHelper _out;

    public CheckpointClusterTests(ITestOutputHelper output) => _out = output;

    public void Dispose()
    {
        NasScenarioStaging.Remove(_scenarioId);
        foreach (var f in Exports()) { try { File.Delete(f); File.Delete(f + ".meta.json"); } catch { } }
    }

    private IEnumerable<string> Exports()
    {
        var root = OrchestrationConstants.ResolveStagingRoot();
        return Directory.Exists(root)
            ? Directory.EnumerateFiles(root, $"*.{_label}.fdp", SearchOption.AllDirectories)
            : Enumerable.Empty<string>();
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "Hrot", "Subsystems"))) return dir.FullName;
        throw new DirectoryNotFoundException("repo root not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public async Task CE3299_B3_ACheckpointOnALiveCluster_IsAKeyframeOnEveryRecordingNode_AtOneWallTime_AndCollects()
    {
        var recipe = Path.Combine(RepoRoot(), "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Scenarios", "tt-take-cover", "scenario.json");
        Directory.CreateDirectory(NasScenarioStaging.DirectoryOf(_scenarioId));
        File.Copy(recipe, Path.Combine(NasScenarioStaging.DirectoryOf(_scenarioId), "scenario.json"), overwrite: true);

        using var harness = new HrotRunnerHarness("simhost,ig,excon,cgf", DomainId);
        var master = harness.OrchestratorSvc.TestHook_ClusterMaster!;
        var deadline = DateTime.UtcNow.AddSeconds(10.0);
        while (master.NodeRoster.ActiveNodes.Count == 0 && DateTime.UtcNow < deadline) { harness.PumpFrames(1); Thread.Sleep(10); }

        await master.HandleClusterOpRequestAsync(new ClusterOpRequest
        {
            RequestId = Guid.NewGuid(), OperationType = ClusterOpType.TransitionState,
            // a live run records under its exercise id (the operator panel mints one), and a checkpoint is a keyframe of that recording
            PayloadJson = JsonSerializer.Serialize(new { TargetState = ClusterState.OperatingLive.ToString(), ScenarioId = _scenarioId, ExerciseId = Guid.NewGuid() }),
        });
        Assert.True(harness.PumpUntil(() => (int)master.CurrentClusterState == (int)ClusterState.OperatingLive, timeoutFrames: 4000), "the cluster must go live");
        harness.PumpFrames(120);   // the recordings run

        // ── take: nothing pauses; the frames go on while the lead passes (M5: their times) ──
        await master.HandleClusterOpRequestAsync(new ClusterOpRequest
        {
            RequestId = Guid.NewGuid(), OperationType = ClusterOpType.TakeCheckpoint,
            PayloadJson = JsonSerializer.Serialize(new { label = _label }),
        });
        var frameMs = new List<double>();
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(1.5))
        {
            var one = Stopwatch.StartNew();
            harness.PumpFrames(1);
            frameMs.Add(one.Elapsed.TotalMilliseconds);
        }
        Assert.Equal((int)ClusterState.OperatingLive, (int)master.CurrentClusterState);   // the save stopped nothing

        // ── collect ──
        await master.HandleClusterOpRequestAsync(new ClusterOpRequest
        {
            RequestId = Guid.NewGuid(), OperationType = ClusterOpType.CollectCheckpoint,
            PayloadJson = JsonSerializer.Serialize(new { label = _label }),
        });
        Assert.True(harness.PumpUntil(() => Exports().Count() >= 3, timeoutFrames: 600),
            $"simhost, ig and cgf must each export '{_label}'; found: {string.Join(", ", Exports().Select(Path.GetFileName))}");

        var ticks = new List<long>();
        foreach (var export in Exports())
        {
            using var playback = new PlaybackController(export);
            Assert.Equal(1, playback.TotalFrames);
            Assert.Equal(FrameType.Keyframe, playback.GetFrameMetadata(0).FrameType);
            var mark = Assert.Single(MetadataSerializer.Deserialize(File.ReadAllText(export + ".meta.json")).Checkpoints);
            Assert.Equal(_label, mark.Label);
            ticks.Add(mark.WallTicks);
        }
        double spreadMs = TimeSpan.FromTicks(ticks.Max() - ticks.Min()).TotalMilliseconds;
        var sorted = frameMs.OrderBy(x => x).ToList();
        _out.WriteLine($"[B3] {ticks.Count} nodes wrote '{_label}' within {spreadMs:F1} ms of each other; " +
                       $"M5 frame times over {frameMs.Count} frames: median {sorted[sorted.Count / 2]:F1} ms, p95 {sorted[(int)(sorted.Count * 0.95)]:F1} ms, max {sorted[^1]:F1} ms");
        Assert.True(spreadMs <= 250, $"the nodes' checkpoint keyframes are {spreadMs:F0} ms apart — one wall time should land within a frame or two");
    }
}
