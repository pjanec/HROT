using System;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using System.Linq;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Utility;
using Hrot.NED.Descriptors.Orchestration;
using Xunit;
using Xunit.Abstractions;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ⭐ <c>CE-3089</c> (G7) — U5 <c>ua-weapon-choice</c> in-process (the live check's twin): a Bradley (25 mm on the owner = mount 0,
/// a TOW child = mount 1) against an insurgent at 300 m and a T-72 at ~523 m. The T-72 must take a TOW-sized hit (only an 800 mm
/// TOW penetrates its 500 mm front) and the insurgent must die. 📄 docs/DESIGN_Utility_AI_Demo_Scenarios.md §12.
/// <para>Before the run it prints every WeaponSelection input for every mount against the T-72, on the REAL Brain world — the
/// layout the unit rails can only imitate (three of G7's defects were invisible to them).</para>
/// </summary>
[Collection("HeavyE2ETests")]
public sealed class FireDistributionScenarioTests : IDisposable
{
    private const int DomainBase = 57;    // CycloneDDS accepts 0–232; 57 used by no other rail (56 weapon choice, 54–55 heard shot, 60–69 sensor mechanism)
    private static int _domainSeq = DomainBase - 1;
    private static int NextDomainId() => Interlocked.Increment(ref _domainSeq);

    private readonly string _scenarioId = "ua_fire_" + Guid.NewGuid().ToString("N");
    private readonly ITestOutputHelper _out;

    public FireDistributionScenarioTests(ITestOutputHelper output) => _out = output;
    public void Dispose() => NasScenarioStaging.Remove(_scenarioId);

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

    /// <summary>
    /// ⭐ <c>CE-3088</c> (G8) — U6 <c>ua-fire-distribution</c> in-process (the live check's twin): a leader and four riflemen against
    /// three spread hostiles — the squad driver assigns every member a target from the MERGED pool, spread, no more than two per
    /// target, and every member fires. 📄 docs/DESIGN_Utility_AI_Demo_Scenarios.md §11.
    /// </summary>
    [Fact(Timeout = 600_000)]
    public async Task CE3088_U6_TheLeaderSpreadsTheSquadsFire_AtMostTwoPerTarget_AndEveryMemberFires()
    {
        Directory.CreateDirectory(NasScenarioStaging.DirectoryOf(_scenarioId));
        File.Copy(Path.Combine(RepoRoot(), "scenarios", "ua-fire-distribution", "scenario.json"),
            Path.Combine(NasScenarioStaging.DirectoryOf(_scenarioId), "scenario.json"), overwrite: true);

        using var harness = new HrotRunnerHarness("simhost,ig,excon,cgf", NextDomainId());
        var master = harness.OrchestratorSvc.TestHook_ClusterMaster!;
        var rosterDeadline = DateTime.UtcNow.AddSeconds(10.0);
        while (master.NodeRoster.ActiveNodes.Count == 0 && DateTime.UtcNow < rosterDeadline) { harness.PumpFrames(1); Thread.Sleep(10); }
        await master.HandleClusterOpRequestAsync(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = JsonSerializer.Serialize(new { TargetState = nameof(Hrot.NED.Descriptors.Orchestration.ClusterState.OperatingLive), ScenarioId = _scenarioId }),
        }).ConfigureAwait(false);
        Assert.True(harness.PumpUntil(() => (int)master.CurrentClusterState == 31, timeoutFrames: 4000), "cluster must reach OperatingLive");

        var cgf = harness.Cgf!.World!;
        string[] names = { "Rifleman 1", "Rifleman 2", "Rifleman 3", "Rifleman 4" };
        Assert.True(harness.PumpUntil(() => !ByName(cgf, "Leader").IsNull && names.All(n => !ByName(cgf, n).IsNull), timeoutFrames: 2000),
            "the leader and the four riflemen must spawn on CGF");
        var leader = ByName(cgf, "Leader");
        var members = names.Select(n => ByName(cgf, n)).ToArray();

        long[] Assigned()
        {
            if (!cgf.HasComponent<UnitRoster>(leader) || !cgf.HasComponent<SquadCognitiveState>(leader)) return Array.Empty<long>();
            var roster = cgf.GetComponentRO<UnitRoster>(leader);
            var state = cgf.GetComponent<SquadCognitiveState>(leader);   // a copy: a ref local cannot be used in the loop below
            var result = new long[members.Length];
            for (int k = 0; k < members.Length; k++)
            {
                int i = UnitRoster.IndexOf(ref roster, members[k]);
                result[k] = i < 0 ? 0L : state.Assignment.GetAssignedTarget(i);
            }
            return result;
        }
        string State() => "assigned=[" + string.Join(",", Assigned()) + "] ammo=[" + string.Join(",", members.Select(m => cgf.GetComponent<WeaponState>(m).Ammo)) + "]";

        Assert.True(harness.PumpUntil(() => Assigned() is { Length: 4 } a && a.All(x => x != 0), timeoutFrames: 6000),
            $"every member is assigned a target (the fire distribution runs); {State()}");
        var assigned = Assigned();
        _out.WriteLine(State());
        Assert.True(assigned.Distinct().Count() >= 2, $"the fire is spread; {State()}");
        Assert.All(assigned.GroupBy(x => x), g => Assert.True(g.Count() <= 2, $"no target over two members; {State()}"));

        var full = members.Select(m => cgf.GetComponent<WeaponState>(m).MaxAmmo).ToArray();
        Assert.True(harness.PumpUntil(() => members.Select((m, i) => cgf.GetComponent<WeaponState>(m).Ammo < full[i]).All(x => x), timeoutFrames: 6000),
            $"every member fires; {State()}");
    }
}
