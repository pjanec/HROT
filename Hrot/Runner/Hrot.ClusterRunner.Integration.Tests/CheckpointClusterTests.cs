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

    // ── ⭐⭐ CE-3299 B4 (§1h) — RESTORE: every node goes back to its keyframe and records on as a new exercise ──

    private static IEnumerable<(string Path, RecordingMetadata Meta)> Recordings(Guid exercise)
    {
        // every node records under its OWN staging root (nodes/node-N/exercises/<id>/), so search the whole staging root
        var root = OrchestrationConstants.ResolveStagingRoot();
        if (!Directory.Exists(root)) yield break;
        var metas = Directory.EnumerateDirectories(root, exercise.ToString(), SearchOption.AllDirectories)
            .Where(d => Path.GetFileName(Path.GetDirectoryName(d)) == OrchestrationConstants.ExercisesDirectoryName)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.fdp.meta.json")).ToList();
        foreach (var meta in metas)
        {
            RecordingMetadata? m = null;
            try { m = MetadataSerializer.Deserialize(File.ReadAllText(meta)); } catch (IOException) { } catch (JsonException) { }
            if (m != null) yield return (meta[..^".meta.json".Length], m);
        }
    }

    private static Dictionary<int, System.Numerics.Vector3> Positions(Fdp.Core.EntityRepository world)
    {
        var at = new Dictionary<int, System.Numerics.Vector3>();
        foreach (var e in world.Query().With<Fdp.Core.SimTransform>().Build()) at[e.Index] = world.GetComponentRO<Fdp.Core.SimTransform>(e).Position;
        return at;
    }

    private static double Farthest(Dictionary<int, System.Numerics.Vector3> a, Dictionary<int, System.Numerics.Vector3> b)
        => a.Where(kv => b.ContainsKey(kv.Key)).Select(kv => (double)System.Numerics.Vector3.Distance(kv.Value, b[kv.Key])).DefaultIfEmpty(0).Max();

    [Fact]
    public async Task CE3299_B4_ARestoreOnALiveCluster_PutsEveryNodeBackToTheCheckpoint_AndRecordsOnAsANewExercise()
    {
        var recipe = Path.Combine(RepoRoot(), "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Scenarios", "tt-take-cover", "scenario.json");
        Directory.CreateDirectory(NasScenarioStaging.DirectoryOf(_scenarioId));
        File.Copy(recipe, Path.Combine(NasScenarioStaging.DirectoryOf(_scenarioId), "scenario.json"), overwrite: true);

        using var harness = new HrotRunnerHarness("simhost,ig,excon,cgf", DomainId);
        var master = harness.OrchestratorSvc.TestHook_ClusterMaster!;
        var deadline = DateTime.UtcNow.AddSeconds(10.0);
        while (master.NodeRoster.ActiveNodes.Count == 0 && DateTime.UtcNow < deadline) { harness.PumpFrames(1); Thread.Sleep(10); }

        var first = Guid.NewGuid();
        await master.HandleClusterOpRequestAsync(new ClusterOpRequest
        {
            RequestId = Guid.NewGuid(), OperationType = ClusterOpType.TransitionState,
            PayloadJson = JsonSerializer.Serialize(new { TargetState = ClusterState.OperatingLive.ToString(), ScenarioId = _scenarioId, ExerciseId = first }),
        });
        Assert.True(harness.PumpUntil(() => (int)master.CurrentClusterState == (int)ClusterState.OperatingLive, timeoutFrames: 4000), "the cluster must go live");
        harness.PumpFrames(120);

        // take X, and catch the world as it was when every node's mark is on disk
        await master.HandleClusterOpRequestAsync(new ClusterOpRequest
        {
            RequestId = Guid.NewGuid(), OperationType = ClusterOpType.TakeCheckpoint, PayloadJson = JsonSerializer.Serialize(new { label = _label }),
        });
        Assert.True(harness.PumpUntil(() => Recordings(first).Count(r => r.Meta.Checkpoints.Any(c => c.Label == _label)) >= 3, timeoutFrames: 600),
            $"simhost, ig and cgf must each mark '{_label}'");
        var world = harness.SimHost.World!;
        var atMark = Positions(world);
        var marks = Recordings(first).Select(r => r.Meta.Checkpoints.Single(c => c.Label == _label)).ToList();

        // the world moves on
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(3)) harness.PumpFrames(1);
        double moved = Farthest(atMark, Positions(world));

        // restore X
        var beforeRestore = world.GetSingletonUnmanaged<Fdp.Core.GlobalTime>();
        var restore = Guid.NewGuid();
        await master.HandleClusterOpRequestAsync(new ClusterOpRequest
        {
            RequestId = restore, OperationType = ClusterOpType.RestoreCheckpoint, PayloadJson = JsonSerializer.Serialize(new { label = _label }),
        });
        Assert.True(harness.PumpUntil(() => master.ActiveExerciseId != first && Recordings(master.ActiveExerciseId).Count() >= 3, timeoutFrames: 1200),
            $"every recording node must record on as the new exercise (master exercise {(master.ActiveExerciseId == first ? "UNCHANGED" : master.ActiveExerciseId.ToString())}, "
            + $"state {master.CurrentClusterState}, recordings of it: {Recordings(master.ActiveExerciseId).Count()})");
        harness.PumpFrames(30);
        Assert.Equal((int)ClusterState.OperatingLive, (int)master.CurrentClusterState);

        // ① the world is back at X (within the nodes' PLL skew and a frame), not where it had moved to
        double back = Farthest(atMark, Positions(world));
        _out.WriteLine($"[B4] after the checkpoint the units moved up to {moved:F2} m; after the restore they are within {back:F2} m of the checkpoint");
        Assert.True(back < 0.75, $"the restored world is {back:F2} m from the checkpoint");
        Assert.True(moved > back, "the run must have moved on before the restore, or the rail proves nothing");

        // ② the clock is back at X, and paused: SIM time stands still at a mark's (wall ticks flow on while paused, by design).
        //   The snap follows the round's completion on the master, then reaches the nodes through the time sync.
        Assert.True(harness.PumpUntil(() => world.GetSingletonUnmanaged<Fdp.Core.GlobalTime>().TotalTime < beforeRestore.TotalTime, timeoutFrames: 600),
            $"the sim clock must go back to the checkpoint (it stays at {world.GetSingletonUnmanaged<Fdp.Core.GlobalTime>().TotalTime:F3} s)");
        harness.PumpFrames(5);
        var restored = world.GetSingletonUnmanaged<Fdp.Core.GlobalTime>();
        harness.PumpFrames(30);
        Assert.Equal(restored.TotalTime, world.GetSingletonUnmanaged<Fdp.Core.GlobalTime>().TotalTime);
        Assert.InRange(restored.TotalTime, marks.Min(m => m.TotalTime) - 1e-6, marks.Max(m => m.TotalTime) + 1e-6);
        Assert.True(restored.TotalTime < beforeRestore.TotalTime, "the sim clock went back to the checkpoint");
        // ⚠ the WALL clock is not: a snap moves the master's, and each node's wall ticks keep physical time (the replay seek's
        //   behaviour too) — so a branch's own frames resume after a wall-time GAP, never before its copied history (③ below)

        // ③ D18 — each node's new exercise opens with its old recording up to X, names its parent, and the old recording keeps its frames after X
        foreach (var (path, meta) in Recordings(master.ActiveExerciseId))
        {
            Assert.Equal(first, meta.ParentExerciseId);
            var parentPath = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(path))!, first.ToString(), Path.GetFileName(path));
            var mark = meta.Checkpoints.Single(c => c.Label == _label);
            using var parent = new PlaybackController(parentPath);
            using var branch = new PlaybackController(path);
            Assert.True(parent.TotalFrames > mark.Frame + 1, "the old recording keeps the frames after the checkpoint");
            Assert.Equal(parent.GetFrameMetadata(mark.Frame).WallClockTicks, branch.GetFrameMetadata(mark.Frame).WallClockTicks);
            Assert.Equal(FrameType.Keyframe, branch.GetFrameMetadata(mark.Frame + 1).FrameType);
            Assert.True(branch.GetFrameMetadata(mark.Frame + 1).WallClockTicks >= mark.WallTicks, "the branch's own frames come after its history");
        }
    }

    /// <summary>
    /// ⭐⭐ CE-3299 D17 (R-3045) — a live session started in CHECKPOINTING mode records, on EVERY recording node, a keyframe every N s of
    /// sim time and nothing between: the request's one number crosses the master, the wire and the load chain to each node's recorder.
    /// </summary>
    [Fact]
    public async Task CE3299_D17_ALiveSessionInCheckpointingMode_RecordsKeyframesOnly_OnEveryNode()
    {
        var recipe = Path.Combine(RepoRoot(), "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Scenarios", "tt-take-cover", "scenario.json");
        Directory.CreateDirectory(NasScenarioStaging.DirectoryOf(_scenarioId));
        File.Copy(recipe, Path.Combine(NasScenarioStaging.DirectoryOf(_scenarioId), "scenario.json"), overwrite: true);

        using var harness = new HrotRunnerHarness("simhost,ig,excon,cgf", DomainId);
        var master = harness.OrchestratorSvc.TestHook_ClusterMaster!;
        var deadline = DateTime.UtcNow.AddSeconds(10.0);
        while (master.NodeRoster.ActiveNodes.Count == 0 && DateTime.UtcNow < deadline) { harness.PumpFrames(1); Thread.Sleep(10); }

        var exercise = Guid.NewGuid();
        await master.HandleClusterOpRequestAsync(new ClusterOpRequest
        {
            RequestId = Guid.NewGuid(), OperationType = ClusterOpType.TransitionState,
            PayloadJson = JsonSerializer.Serialize(new
            {
                TargetState = ClusterState.OperatingLive.ToString(), ScenarioId = _scenarioId, ExerciseId = exercise, CheckpointingSeconds = 1.0,
            }),
        });
        Assert.True(harness.PumpUntil(() => (int)master.CurrentClusterState == (int)ClusterState.OperatingLive, timeoutFrames: 4000), "the cluster must go live");
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(3.5)) harness.PumpFrames(1);
        // a named checkpoint is still exact in this mode (R-3044) — every node marks it
        await master.HandleClusterOpRequestAsync(new ClusterOpRequest
        {
            RequestId = Guid.NewGuid(), OperationType = ClusterOpType.TakeCheckpoint, PayloadJson = JsonSerializer.Serialize(new { label = _label }),
        });
        Assert.True(harness.PumpUntil(() => Recordings(exercise).Count(r => r.Meta.Checkpoints.Any(c => c.Label == _label)) >= 3, timeoutFrames: 600),
            "simhost, ig and cgf must each record, and mark the checkpoint");
        var simSeconds = harness.SimHost.World!.GetSingletonUnmanaged<Fdp.Core.GlobalTime>().TotalTime;
        harness.Dispose();   // ends the recordings: every frame is on disk

        var recordings = Recordings(exercise).ToList();
        foreach (var (path, meta) in recordings)
        {
            Assert.Equal(1.0, meta.CheckpointingSeconds);
            using var playback = new PlaybackController(path);
            for (int f = 0; f < playback.TotalFrames; f++) Assert.Equal(FrameType.Keyframe, playback.GetFrameMetadata(f).FrameType);
            // one keyframe per started second of sim time (+ the first), not one per frame
            Assert.InRange(playback.TotalFrames, 2, (int)Math.Ceiling(simSeconds) + 3);   // + the checkpoint's own keyframe
            _out.WriteLine($"[D17] {Path.GetFileName(path)}: {playback.TotalFrames} keyframes over {simSeconds:F1} s of sim time");
        }
    }
}
