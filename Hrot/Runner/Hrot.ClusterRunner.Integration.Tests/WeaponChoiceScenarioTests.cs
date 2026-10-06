using System;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Combat;
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
public sealed class WeaponChoiceScenarioTests : IDisposable
{
    private const int DomainBase = 56;    // CycloneDDS accepts 0–232; 56 used by no other rail (52–53 posture, 54–55 heard shot, 60–69 sensor mechanism) (grep of the DomainBase / harness domain ids, 2026-10-06)
    private static int _domainSeq = DomainBase - 1;
    private static int NextDomainId() => Interlocked.Increment(ref _domainSeq);

    private readonly string _scenarioId = "ua_weapon_" + Guid.NewGuid().ToString("N");
    private readonly ITestOutputHelper _out;

    public WeaponChoiceScenarioTests(ITestOutputHelper output) => _out = output;
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

    [Fact(Timeout = 600_000)]
    public async Task CE3089_U5_TheBradley_FiresTheTowAtTheTank_AndThe25mmAtTheInsurgent()
    {
        Directory.CreateDirectory(NasScenarioStaging.DirectoryOf(_scenarioId));
        File.Copy(Path.Combine(RepoRoot(), "scenarios", "ua-weapon-choice", "scenario.json"),
            Path.Combine(NasScenarioStaging.DirectoryOf(_scenarioId), "scenario.json"), overwrite: true);

        using var harness = new HrotRunnerHarness("simhost,ig,excon,cgf", NextDomainId());
        var master = harness.OrchestratorSvc.TestHook_ClusterMaster!;
        var rosterDeadline = DateTime.UtcNow.AddSeconds(10.0);
        while (master.NodeRoster.ActiveNodes.Count == 0 && DateTime.UtcNow < rosterDeadline) { harness.PumpFrames(1); Thread.Sleep(10); }
        await master.HandleClusterOpRequestAsync(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = JsonSerializer.Serialize(new { TargetState = nameof(ClusterState.OperatingLive), ScenarioId = _scenarioId }),
        }).ConfigureAwait(false);
        Assert.True(harness.PumpUntil(() => (int)master.CurrentClusterState == 31, timeoutFrames: 4000), "cluster must reach OperatingLive");

        var cgf = harness.Cgf!.World!;
        Assert.True(harness.PumpUntil(() => !ByName(cgf, "Bradley").IsNull && !ByName(cgf, "T-72").IsNull && !ByName(cgf, "Insurgent").IsNull,
            timeoutFrames: 2000), "the three units must spawn on CGF");
        Entity bradley = ByName(cgf, "Bradley"), tank = ByName(cgf, "T-72"), insurgent = ByName(cgf, "Insurgent");

        // ── the diagnostic: every mount, every input, against the tank ──
        Span<Entity> mounts = stackalloc Entity[8];
        int n = WeaponMountQuery.EnumerateMounts(cgf, bradley, mounts);
        _out.WriteLine($"mounts on CGF: {n} (WeaponMountInfo registered: {cgf.IsComponentTypeRegistered<WeaponMountInfo>()})");
        for (int i = 0; i < n; i++)
        {
            var ctx = new UtilityInputCtx { Repo = cgf, Self = mounts[i], Context = tank };
            int idx = cgf.HasComponent<WeaponMountInfo>(mounts[i]) ? cgf.GetComponentRO<WeaponMountInfo>(mounts[i]).MountIndex : 0;
            _out.WriteLine($"  mount {idx} ({(mounts[i].Equals(bradley) ? "owner" : "child")}): hasAmmo={StandardInputs.WeaponHasAmmo(in ctx)} " +
                           $"rangeFit={StandardInputs.WeaponRangeBandFit(in ctx):F3} effectiveness={StandardInputs.WeaponEffectivenessVsTarget(in ctx):F4} " +
                           $"readiness={StandardInputs.WeaponReadiness(in ctx)} roundsLeft={StandardInputs.RoundsLeft(in ctx):F3}");
        }
        int chosen = WeaponChoice.Choose(cgf, bradley, tank, out _);
        _out.WriteLine($"WeaponChoice at the tank: mount {chosen}");
        Assert.Equal(2, n);
        Assert.Equal(1, chosen);

        // ── the run: a TOW-sized hit on the tank, the insurgent killed ──
        float tank0 = cgf.GetComponent<Health>(tank).Current;
        Assert.True(harness.PumpUntil(() => tank0 - cgf.GetComponent<Health>(tank).Current >= 1000f, timeoutFrames: 20000),
            $"the T-72 must take a TOW hit; health {cgf.GetComponent<Health>(tank).Current}, 25 mm {cgf.GetComponent<WeaponState>(bradley).Ammo}");
        Assert.True(harness.PumpUntil(() => cgf.GetComponent<Health>(insurgent).Current <= 0f, timeoutFrames: 20000), "the insurgent must be killed");
    }
}
