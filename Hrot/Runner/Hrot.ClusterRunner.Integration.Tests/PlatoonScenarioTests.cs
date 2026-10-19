using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Command;
using Fdp.Toolkit.Squad.Maneuvers;
using Fdp.Toolkit.Behavior.Systems;
using Hrot.NED.Descriptors.Orchestration;
using Xunit;
using Xunit.Abstractions;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ⭐⭐ <c>CE-2296</c> — the platoon demos' in-process twins (📄 <c>docs/designs/group-maneuvers/DESIGN_Platoon_Attack_Defend_Clear.md</c>
/// §12): each shipped <c>scenarios/</c> demo run on the real cluster, asserting what its <c>run_*</c> live check in
/// <c>scripts/utility-demo-check.py</c> asserts.
/// </summary>
[Collection("HeavyE2ETests")]
public sealed class PlatoonScenarioTests : IDisposable
{
    private const int DomainBase = 120;   // CycloneDDS accepts 0–232; 120 used by no other rail (grep of domain ids, 2026-10-19)
    private static int _domainSeq = DomainBase - 1;
    private static int NextDomainId() => Interlocked.Increment(ref _domainSeq);

    private readonly List<string> _staged = new();
    private readonly ITestOutputHelper _out;

    public PlatoonScenarioTests(ITestOutputHelper output) => _out = output;

    public void Dispose()
    {
        foreach (var id in _staged) NasScenarioStaging.Remove(id);
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "Hrot", "Subsystems"))) return dir.FullName;
        throw new DirectoryNotFoundException("repo root not found above " + AppContext.BaseDirectory);
    }

    private static Entity ByName(EntityRepository world, string name)
    {
        for (int i = 0; i <= world.MaxEntityIndex; i++)
        {
            var e = world.GetEntityByIndex(i);
            if (!world.IsAlive(e) || !world.HasComponent<EntityInfo>(e)) continue;
            if (world.GetComponent<EntityInfo>(e).Name.ToString() == name) return e;
        }
        return Entity.Null;
    }

    /// <summary>Stages <c>scenarios/&lt;name&gt;</c>, boots the four hosts and takes the cluster live.</summary>
    private async Task<HrotRunnerHarness> Live(string scenario)
    {
        var id = scenario.Replace('-', '_') + "_" + Guid.NewGuid().ToString("N");
        _staged.Add(id);
        Directory.CreateDirectory(NasScenarioStaging.DirectoryOf(id));
        File.Copy(Path.Combine(RepoRoot(), "scenarios", scenario, "scenario.json"),
            Path.Combine(NasScenarioStaging.DirectoryOf(id), "scenario.json"), overwrite: true);

        var harness = new HrotRunnerHarness("simhost,ig,excon,cgf", NextDomainId());
        var master = harness.OrchestratorSvc.TestHook_ClusterMaster!;
        var rosterDeadline = DateTime.UtcNow.AddSeconds(10.0);
        while (master.NodeRoster.ActiveNodes.Count == 0 && DateTime.UtcNow < rosterDeadline)
        {
            harness.PumpFrames(1);
            Thread.Sleep(10);
        }
        await master.HandleClusterOpRequestAsync(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = JsonSerializer.Serialize(new { TargetState = nameof(ClusterState.OperatingLive), ScenarioId = id }),
        }).ConfigureAwait(false);
        Assert.True(harness.PumpUntil(() => (int)master.CurrentClusterState == 31, timeoutFrames: 4000),
            $"cluster must reach OperatingLive; at {(int)master.CurrentClusterState}");
        return harness;
    }

    // ── sq-defend-house (§12.2 stage B; DESIGN_Defend_And_Attack_Position §8c, CE-2275) ──────────────────────────────────

    private static readonly Vector3 DhOrigin = new(120.9f, 157f, 0f);
    private const float DhLeft = 150f, DhRight = 210f, DhRange = 90f;

    /// <summary>
    /// ⭐⭐ <c>CE-2275</c> — the twin of <c>run_sq_defend_house</c>: a Red squad holding fire (operator) defends OBJ on
    /// <c>cqb-village</c> with its sector south; a Blue fire team walks up from the south. The squad posts · ③ no Red round before the
    /// first Blue is inside the sector (DA-35), then Red fires. ⚠ PROGRESS Set is its own test below (blocked by <c>CE-2304</c>).
    /// </summary>
    [Fact(Timeout = 420_000)]
    public Task CE2275_SqDefendHouse_TheSquadMansOBJ_HoldsFireUntilBlueIsInItsSector_ThenFights() => RunSqDefendHouse(requireSet: false);

    /// <summary>⭐ <c>CE-2275</c> — the same run, and every man reaches his post: PROGRESS Set (D-2d, D-2e built). ⛔ Skipped on
    /// <c>CE-2304</c>: measured live, four of seven men stall 4–6 m from their posts inside OBJ with their moves <c>InProgress</c> for
    /// good (backend, docs/blueprints/batches/NOTE_Behaviors_To_Backend_Interior_Move_Stall.md). Unskip when it lands.</summary>
    [Fact(Timeout = 420_000, Skip = "CE-2304: men stall inside OBJ, their moves never end (backend)")]
    public Task CE2275_SqDefendHouse_EveryManReachesHisPost_ProgressSet() => RunSqDefendHouse(requireSet: true);

    private async Task RunSqDefendHouse(bool requireSet)
    {
        using var harness = await Live("sq-defend-house");
        var cgf = harness.Cgf!.World!;
        var sim = harness.SimHost.World!;
        string[] reds = new[] { "Red Leader" }.Concat(Enumerable.Range(1, 6).Select(i => $"Red Rifleman {i}")).ToArray();
        string[] blues = Enumerable.Range(1, 4).Select(i => $"Blue {i}").ToArray();
        Assert.True(harness.PumpUntil(() => !ByName(cgf, "Red Squad").IsNull && reds.Concat(blues).All(n => !ByName(sim, n).IsNull), timeoutFrames: 2000),
            "the Red squad and the Blue fire team must spawn");
        var squad = ByName(cgf, "Red Squad");

        // the operator gives the squad its sector south (what POST /entities/{id}/sector does)
        Assert.True(EngagementAreaSystem.Apply(cgf, new SetEngagementAreaEvent
        {
            Entity = squad, Origin = BehaviorOrigin.Operator, ArcSet = 1, ArcOrigin = DhOrigin,
            LeftBearing = DhLeft, RightBearing = DhRight, MaxRange = DhRange,
        }), "the sector is applied");

        SquadCognitiveState Squad() => cgf.GetComponent<SquadCognitiveState>(squad);
        Assert.True(harness.PumpUntil(() => cgf.HasComponent<SquadCognitiveState>(squad) && Squad().ManeuverKind == SquadDefendManeuver.ManeuverKind
                                         && Squad().PhaseId == SquadDefendManeuver.PhasePosting, timeoutFrames: 6000),
            "the squad reaches OBJ and starts posting");

        bool Progress(ProgressKind kind)
        {
            if (!cgf.HasComponent<ReportOutbox>(squad)) return false;
            var q = cgf.GetComponentRO<ReportOutbox>(squad).Queued;
            for (int i = 0; i < q.Count; i++)
                if (q.Items[i].Report.Kind == ReportKind.Progress && q.Items[i].Report.Subtype == (byte)kind) return true;
            return false;
        }
        int Ammo(EntityRepository w, string n) => w.HasComponent<WeaponState>(ByName(w, n)) ? w.GetComponent<WeaponState>(ByName(w, n)).Ammo : -1;
        bool InSector(Vector3 p)
        {
            var d = new Vector2(p.X - DhOrigin.X, p.Y - DhOrigin.Y);
            if (d.Length() > DhRange) return false;
            float b = (MathF.Atan2(d.X, d.Y) * 180f / MathF.PI + 360f) % 360f;
            return b >= DhLeft && b <= DhRight;
        }

        var ammo0 = reds.ToDictionary(n => n, n => Ammo(cgf, n));   // the brain spends the rounds (FireDistributionScenarioTests reads them there)
        bool set = false, blueIn = false, earlyRound = false;
        var shooters = new HashSet<string>();
        for (int frame = 0; frame < 9000 && !((set || !requireSet) && blueIn && shooters.Count >= 2); frame++)
        {
            harness.PumpFrames(1);
            set |= Progress(ProgressKind.Set);
            bool inNow = blues.Any(n => InSector(sim.GetComponent<SimTransform>(ByName(sim, n)).Position));
            foreach (var n in reds)
                if (Ammo(cgf, n) < ammo0[n])
                {
                    if (!blueIn && !inNow) earlyRound = true;
                    shooters.Add(n);
                }
            blueIn |= inNow;
        }
        _out.WriteLine($"set={set} blueIn={blueIn} early={earlyRound} shooters={string.Join(",", shooters)}");

        if (requireSet) Assert.True(set, "PROGRESS Set — every man on his post (D-2d, D-2e)");
        Assert.True(blueIn, "the Blue fire team walks into the sector");
        Assert.False(earlyRound, "③ no Red round before the first Blue enters the sector (DA-35)");
        Assert.True(shooters.Count >= 2, $"③ …then Red fires: {shooters.Count} shooters");
    }
}
