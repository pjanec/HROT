using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Replication.Components;
using Fdp.Testing;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.ScenarioRules;
using Hrot.NED.Descriptors.Orchestration;
using Xunit;
using Xunit.Abstractions;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ⭐⭐ <c>CE-6010</c> (city battle B-0; 📄 docs/designs/group-maneuvers/DESIGN_City_Battle_Showcase.md §4c) — <c>scenarios/showcases/city-battle</c>, the
/// battle's SKELETON, in-process on the four hosts for 60 s of sim time with the EXISTING brains and recording ON (the cluster default, D-17):
/// ① every side's people, vehicles and aircraft on CGF, SimHost and the Player (the macros expanded), ② all Active on every node, ③ the defenders'
/// position triggers fired and their orders were taken (the Order verb reached a macro-expanded platoon through its DERIVED GUID), ④ the recorder wrote
/// on SimHost and CGF, ⑤ no module fault / unhandled exception in the hosts' output (⚠ the module host keeps a node alive after a system throws —
/// the output is the only witness). Reports the load time and the frame time at this size (Scale Rig R-4192 — reported, not asserted).
/// </summary>
[Collection("HeavyE2ETests")]
public sealed class CityBattleSkeletonTests : IDisposable
{
    private const int DomainId = 175;   // CycloneDDS accepts 0–232; 175 used by no other rail (ClusterOpE2e owns 170–173, 176–179 the showcase rails, the AI debug surface 181–189; grep 2026-11-13)
    private readonly List<string> _staged = new();
    private readonly ITestOutputHelper _out;

    public CityBattleSkeletonTests(ITestOutputHelper output) => _out = output;
    public void Dispose() { foreach (var id in _staged) NasScenarioStaging.Remove(id); }

    // the order of battle as the generator lays it out (scripts/city_battle_scenario.py) — people by side, at the start (the 2nd wave enters at 20 min)
    private const int FriendPeople = 2 * 108 + 36 + 6 + 4 + 6 + 6;            // 2 mech companies · air-assault platoon · mortar crews · drone ops · Javelin · engineers = 274
    private const int HostilePeople = 4 * 108 + 6 + 4 + 4 + 6 + 6 + 4;        // 4 defender companies · hill mortars · AA gunners · drone ops · MG nests · AT · outposts = 462
    private const int Civilians = 200;
    private static readonly HashSet<long> People = new() { 2002, 2003, 2004, 2006, 2009, 202, 203, 1101, 1102, 1103, 1104 };
    private static readonly HashSet<long> Vehicles = new() { 100, 101, 102, 104, 110, 112, 1002 };
    private static readonly HashSet<long> Aircraft = new() { 400, 405, 406, 403 };

    private sealed class Tee : TextWriter
    {
        private readonly TextWriter _inner; private readonly StringBuilder _log;
        public Tee(TextWriter inner, StringBuilder log) { _inner = inner; _log = log; }
        public override Encoding Encoding => _inner.Encoding;
        public override void Write(char value) { lock (_log) _log.Append(value); _inner.Write(value); }
        public override void Write(string? value) { lock (_log) _log.Append(value); _inner.Write(value); }
        public override void WriteLine(string? value) { lock (_log) _log.Append(value).Append('\n'); _inner.WriteLine(value); }
    }

    private static IEnumerable<Entity> All(EntityRepository w)
    {
        for (int i = 0; i <= w.MaxEntityIndex; i++)
        {
            var e = w.GetEntityByIndex(i);
            if (w.IsAlive(e)) yield return e;
        }
    }

    private static long TypeOf(EntityRepository w, Entity e) => w.HasComponent<TkbIdentity>(e) ? (long)w.GetComponentRO<TkbIdentity>(e).TkbType : 0;

    private sealed record Census(int Friend, int Hostile, int Neutral, int Vehicles, int Aircraft, int NotActive)
    {
        public override string ToString() => $"people F{Friend} H{Hostile} civ{Neutral}, vehicles {Vehicles}, aircraft {Aircraft}, not Active {NotActive}";
    }

    private static Census Count(EntityRepository w)
    {
        int f = 0, h = 0, n = 0, v = 0, a = 0, notActive = 0;
        foreach (var e in All(w))
        {
            long t = TypeOf(w, e);
            bool person = People.Contains(t), vehicle = Vehicles.Contains(t), air = Aircraft.Contains(t);
            if (!person && !vehicle && !air) continue;
            if (w.GetLifecycleState(e) != EntityLifecycle.Active) notActive++;
            if (vehicle) v++;
            else if (air) a++;
            else switch (w.GetComponentRO<EntityInfo>(e).ForceId)
            {
                case ForceId.Friend: f++; break;
                case ForceId.Hostile: h++; break;
                default: n++; break;
            }
        }
        return new Census(f, h, n, v, a, notActive);
    }

    /// <summary>Stages the shipped city battle and takes the four-host cluster live on it — with <paramref name="args"/> as the load's scenario args
    /// (<c>{"$variant": N}</c>); none ⇒ variant 0, as authored (this raw transition does not draw a default variant — that is the live-load intent's).</summary>
    private async Task<HrotRunnerHarness> Boot(string? args)
    {
        var id = "city_battle_" + Guid.NewGuid().ToString("N");
        _staged.Add(id);
        Directory.CreateDirectory(NasScenarioStaging.DirectoryOf(id));
        File.Copy(TestRepo.ScenarioFile("city-battle"), Path.Combine(NasScenarioStaging.DirectoryOf(id), "scenario.json"), overwrite: true);
        var harness = new HrotRunnerHarness("simhost,player,cgf", DomainId);
        var master = harness.OrchestratorSvc.TestHook_ClusterMaster!;
        var rosterDeadline = DateTime.UtcNow.AddSeconds(10.0);
        while (master.NodeRoster.ActiveNodes.Count == 0 && DateTime.UtcNow < rosterDeadline) { harness.PumpFrames(1); Thread.Sleep(10); }
        await master.HandleClusterOpRequestAsync(new ClusterOpRequest
        {
            RequestId = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson = JsonSerializer.Serialize(new { TargetState = nameof(Hrot.NED.Descriptors.Orchestration.ClusterState.OperatingLive), ScenarioId = id, ScenarioArgs = args }),
        }).ConfigureAwait(false);
        Assert.True(harness.PumpUntil(() => (int)master.CurrentClusterState == 31, timeoutFrames: 8000), $"cluster must reach OperatingLive; at {(int)master.CurrentClusterState}");
        return harness;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-6011</c> (city battle V-A; 📄 §4c) — a NON-ZERO variant loads complete on every node: the variant chosen is the first (1…) that drops a
    /// roadblock AND moves 1 Coy (predicted by <see cref="ScenarioVariants.Apply"/> on the file — the routine every node's load runs), and every node holds
    /// exactly the people that prediction leaves (a dropped roadblock takes its 2 MG + 2 AT men; the civilians by their Presence), all Active, the
    /// defenders' position triggers taken (their "@Key" orders find the platoons wherever the variant put them).
    /// </summary>
    [Fact(Timeout = 1_200_000)]
    public async Task CE6011_AVariant_LoadsComplete_OnEveryNode_WithWhatItsRollsKept()
    {
        var file = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(TestRepo.ScenarioFile("city-battle")))!.AsObject();
        (int Roadblocks, int Civilians, double D1X) Predict(int v)
        {
            var dom = file.DeepClone().AsObject();
            ScenarioVariants.Apply(dom, v, Guid.Empty, "city-battle");
            var es = dom["entities"]!.AsObject().Select(kv => kv.Value!.AsObject()).ToList();
            string Name(System.Text.Json.Nodes.JsonObject e) => (string?)e["EntityInfo"]?["Name"] ?? "";
            return (es.Count(e => Name(e).StartsWith("Roadblock ")), es.Count(e => Name(e).StartsWith("Civilian ")),
                    (double)es.Single(e => Name(e) == "1 Coy (D)")["SimTransform"]!["Position"]![0]!);
        }
        var authored = Predict(0);
        int variant = Enumerable.Range(1, 200).First(v => { var p = Predict(v); return p.Roadblocks < 3 && p.D1X != authored.D1X; });
        var want = Predict(variant);
        int hostile = HostilePeople - 4 * (3 - want.Roadblocks);
        _out.WriteLine($"variant {variant}: {want.Roadblocks} roadblocks, {want.Civilians} civilians, 1 Coy at x {want.D1X} (authored: {authored.Roadblocks}, {authored.Civilians}, x {authored.D1X})");

        using var harness = await Boot($"{{\"$variant\": {variant}}}");
        var hosts = new[] { ("CGF", harness.Cgf!.World!), ("SimHost", harness.SimHost.World!), ("Player", harness.Player.App.World) };
        var cgf = hosts[0].Item2;
        bool Complete(Census c) => c.Friend == FriendPeople && c.Hostile == hostile && c.Neutral == want.Civilians && c.NotActive == 0;
        harness.PumpUntil(() => hosts.All(h => Complete(Count(h.Item2))) || cgf.SimulationTime > 45.0, timeoutFrames: 20000);
        string State() => string.Join("; ", hosts.Select(h => $"{h.Item1}: {Count(h.Item2)}"));
        _out.WriteLine($"sim {cgf.SimulationTime:0.0}: {State()}");
        foreach (var (name, w) in hosts)
            Assert.True(Complete(Count(w)), $"{name} holds F{FriendPeople} H{hostile} civ{want.Civilians}, all Active: {State()}");
        var d1 = ScenarioTwin.ByName(cgf, "1 Coy (D)");
        _out.WriteLine($"1 Coy (D) instance on CGF at x {(d1.IsNull ? double.NaN : cgf.GetComponentRO<SimTransform>(d1).Position.X):0.0}");
        harness.PumpUntil(() => cgf.SimulationTime > 50.0, timeoutFrames: 20000);
        var triggers = All(cgf).Where(e => cgf.HasComponent<ObjectiveStatus>(e) && (cgf.GetComponentRO<ObjectiveStatus>(e).Title.ToString().EndsWith("takes position")
                                                                                     || cgf.GetComponentRO<ObjectiveStatus>(e).Title.ToString().StartsWith("Reserve Coy"))).ToList();
        Assert.Equal(4, triggers.Count);
        Assert.All(triggers, t => Assert.True(cgf.GetComponentRO<ObjectiveStatus>(t).State == ObjectiveState.Achieved, $"{cgf.GetComponentRO<ObjectiveStatus>(t).Title}: {cgf.GetComponentRO<ObjectiveStatus>(t).State} ({cgf.GetComponentRO<ObjectiveStatus>(t).Reason})"));
    }

    [Fact(Timeout = 1_200_000)]
    public async Task CE6010_CityBattle_Loads_AndRuns60s_EveryNodeComplete_RecorderWriting_NoFault()
    {
        var log = new StringBuilder();
        var (stdout, stderr) = (Console.Out, Console.Error);
        Console.SetOut(new Tee(stdout, log));
        Console.SetError(new Tee(stderr, log));
        try
        {
            var load = Stopwatch.StartNew();
            using var harness = await Boot(args: null);
            var master = harness.OrchestratorSvc.TestHook_ClusterMaster!;
            double liveSeconds = load.Elapsed.TotalSeconds;

            var hosts = new[] { ("CGF", harness.Cgf!.World!), ("SimHost", harness.SimHost.World!), ("Player", harness.Player.App.World) };
            var cgf = hosts[0].Item2;
            bool Complete(Census c) => c.Friend == FriendPeople && c.Hostile == HostilePeople && c.Neutral == Civilians && c.NotActive == 0;
            harness.PumpUntil(() => hosts.All(h => Complete(Count(h.Item2))) || cgf.SimulationTime > 45.0, timeoutFrames: 20000);
            double completeSeconds = load.Elapsed.TotalSeconds;
            double completeSim = cgf.SimulationTime;

            // the steady frame: wall time per pumped frame (all four hosts in this one thread) over the rest of the 60 s
            var frames = 0;
            var steady = Stopwatch.StartNew();
            harness.PumpUntil(() => { frames++; return cgf.SimulationTime >= 60.0; }, timeoutFrames: 40000);
            steady.Stop();

            string State() => string.Join("; ", hosts.Select(h => $"{h.Item1}: {Count(h.Item2)}"));
            _out.WriteLine($"load: OperatingLive after {liveSeconds:0.0} s wall; every node complete after {completeSeconds:0.0} s wall (sim {completeSim:0.0} s)");
            _out.WriteLine($"steady: {frames} frames to sim {cgf.SimulationTime:0.0} s in {steady.Elapsed.TotalSeconds:0.0} s wall = {steady.Elapsed.TotalMilliseconds / Math.Max(1, frames):0.0} ms per frame (four hosts, one thread)");
            _out.WriteLine($"entities alive: CGF {All(cgf).Count()}, SimHost {All(hosts[1].Item2).Count()}, Player {All(hosts[2].Item2).Count()}");
            _out.WriteLine($"census at sim {cgf.SimulationTime:0.0}: {State()}");

            // ①② every node holds the whole order of battle, all Active
            foreach (var (name, w) in hosts)
            {
                var c = Count(w);
                Assert.True(Complete(c), $"①② {name} holds F{FriendPeople} H{HostilePeople} civ{Civilians}, all Active: {State()}");
            }

            // ③ the defenders' position triggers fired and every Order was taken (a failed step leaves the trigger Failed with the reason)
            var triggers = All(cgf).Where(e => cgf.HasComponent<ObjectiveStatus>(e) && (cgf.GetComponentRO<ObjectiveStatus>(e).Title.ToString().EndsWith("takes position")
                                                                                         || cgf.GetComponentRO<ObjectiveStatus>(e).Title.ToString().StartsWith("Reserve Coy"))).ToList();
            string Trig(Entity e) { var s = cgf.GetComponentRO<ObjectiveStatus>(e); return $"'{s.Title}' {s.State} ({s.Reason})"; }
            _out.WriteLine("position triggers: " + string.Join(" · ", triggers.Select(Trig)));
            Assert.Equal(4, triggers.Count);
            Assert.All(triggers, t => Assert.True(cgf.GetComponentRO<ObjectiveStatus>(t).State == ObjectiveState.Achieved, "③ " + Trig(t)));

            // ④ the recorder wrote on SimHost and CGF (recording is ON by default — RecordLiveRuns, D-17)
            var exercise = (Guid)master.ActiveExerciseId;
            var simFile = OrchestrationConstants.GetExerciseRecordingFilePath(OrchestrationConstants.ResolveStagingRoot(), exercise, 1);
            var cgfFile = OrchestrationConstants.GetExerciseRecordingFilePath(OrchestrationConstants.GetNodeStagingRoot(400), exercise, 400);
            long Size(string f) => File.Exists(f) ? new FileInfo(f).Length : -1;
            _out.WriteLine($"recording: SimHost {Size(simFile):N0} B ({simFile}), CGF {Size(cgfFile):N0} B ({cgfFile})");
            Assert.True(master.RecordLiveRuns, "④ recording is ON");
            Assert.True(Size(simFile) > 0 && Size(cgfFile) > 0, $"④ the recorder writes: SimHost {Size(simFile)} B, CGF {Size(cgfFile)} B");
        }
        finally
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
        }

        // ⑤ no module fault, no unhandled exception in what the hosts printed
        string text;
        lock (log) text = log.ToString();
        var faults = text.Split('\n').Where(l => l.Contains("FAULT") || l.Contains("Unhandled") || l.Contains("Exception:")).Distinct().Take(12).ToList();
        _out.WriteLine($"host output: {text.Length:N0} chars, {faults.Count} fault line(s)" + (faults.Count > 0 ? ":\n  " + string.Join("\n  ", faults) : ""));
        Assert.True(faults.Count == 0, "⑤ no fault in the host output:\n" + string.Join("\n", faults));
    }
}
