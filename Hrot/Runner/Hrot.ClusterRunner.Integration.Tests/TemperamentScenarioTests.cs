using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ⭐⭐ The temperament &amp; stress demos (📄 docs/DESIGN_Temperament_And_Stress.md §6), each the in-process twin of its live check
/// (<c>scripts/utility-demo-check.py --launch ts-…</c>): the shipped scenario on the four hosts, the people read from the CGF world.
/// </summary>
[Collection("HeavyE2ETests")]
public sealed class TemperamentScenarioTests : IDisposable
{
    private const int DomainBase = 209;   // CycloneDDS accepts 0–232; 209–210 used by no other rail (grep of fixed ids, DomainBase and counters, 2026-10-24)
    private static int _domainSeq = DomainBase - 1;
    private static int NextDomainId() => Interlocked.Increment(ref _domainSeq);

    private readonly List<string> _staged = new();
    private readonly ITestOutputHelper _out;

    public TemperamentScenarioTests(ITestOutputHelper output) => _out = output;

    public void Dispose()
    {
        foreach (var id in _staged) NasScenarioStaging.Remove(id);
    }

    /// <summary>
    /// ⭐ <c>CE-2271</c> (TS-C, the damping modifiers, R-2075) — <c>ts-aggression</c>: two identical riflemen ordered to attack the same
    /// way, one bold (risk acceptance 0.9) and one cautious (0.1), obedience 0.3 so character shows. ⭐ The bold man closes on his enemy;
    /// the cautious one stops at his first cover and suppresses. 📐 Measured live: 4.7 m vs 88.6 m after 90 s.
    /// </summary>
    [Fact(Timeout = 300_000)]
    public async Task CE2271_TsAggression_TheBoldManCloses_TheCautiousManHolds()
    {
        using var harness = await ScenarioTwin.Live("ts-aggression", _staged, NextDomainId());
        var cgf = harness.Cgf!.World!;
        string[] names = { "Bold", "Bold Enemy", "Cautious", "Cautious Enemy" };
        Assert.True(harness.PumpUntil(() => names.All(n => !ScenarioTwin.ByName(cgf, n).IsNull), timeoutFrames: 2000),
            "both attackers and both enemies must spawn");

        float Gap(string who)
        {
            Entity a = ScenarioTwin.ByName(cgf, who), b = ScenarioTwin.ByName(cgf, who + " Enemy");
            if (a.IsNull || b.IsNull || !cgf.HasComponent<SimTransform>(a) || !cgf.HasComponent<SimTransform>(b)) return float.NaN;
            Vector3 pa = cgf.GetComponent<SimTransform>(a).Position, pb = cgf.GetComponent<SimTransform>(b).Position;
            return Vector2.Distance(new Vector2(pa.X, pa.Y), new Vector2(pb.X, pb.Y));
        }
        float bold0 = Gap("Bold"), cautious0 = Gap("Cautious");
        double T() => cgf.GetSingleton<GlobalTime>().TotalTime;
        double start = T();
        for (int f = 0; f < 12_000 && T() - start < 90.0; f += 300)
        {
            harness.PumpFrames(300);
            _out.WriteLine($"  t {T() - start,5:F1}  Bold {Gap("Bold"),6:F1} m  Cautious {Gap("Cautious"),6:F1} m");
        }
        float bold = Gap("Bold"), cautious = Gap("Cautious");
        _out.WriteLine($"start Bold {bold0:F1} / Cautious {cautious0:F1} m → end Bold {bold:F1} / Cautious {cautious:F1} m after {T() - start:F1} s");

        Assert.True(bold < 15f, $"the bold man closes on his enemy (ends {bold:F1} m away, started {bold0:F1})");
        Assert.True(cautious > 50f, $"the cautious man holds back (ends {cautious:F1} m away, started {cautious0:F1})");
    }

    /// <summary>
    /// ⭐ <c>CE-2272</c> (TS-D, the break, R-2034 TS-6) — <c>ts-three-defenders</c>: three insurgents under the same over-the-head fire. ⭐ The
    /// Coward (effective nerve 0.30) breaks and runs — his resolve (0.53) keeps him from surrendering; the Waverer (effective resolve
    /// 0.18) breaks with a rifle on him and surrenders; the Fanatic (effective nerve 0.86) never breaks.
    /// </summary>
    [Fact(Timeout = 300_000)]
    public async Task CE2272_TsThreeDefenders_TheCowardRuns_TheWavererSurrenders_TheFanaticHolds()
    {
        using var harness = await ScenarioTwin.Live("ts-three-defenders", _staged, NextDomainId());
        var cgf = harness.Cgf!.World!;
        var registry = harness.Cgf!.TestHook_BehaviorRegistry!;
        string[] men = { "Fanatic", "Coward", "Waverer" };
        Assert.True(harness.PumpUntil(() => men.Concat(new[] { "Rifleman Waverer", "Gunner Fanatic" }).All(n => !ScenarioTwin.ByName(cgf, n).IsNull),
            timeoutFrames: 2000), "the three insurgents, their gunners and riflemen must spawn");

        var ran = men.ToDictionary(n => n, _ => new HashSet<string>());
        var broke = men.ToDictionary(n => n, _ => false);
        var peak = men.ToDictionary(n => n, _ => 0f);
        double T() => cgf.GetSingleton<GlobalTime>().TotalTime;
        double start = T();
        for (int f = 0; f < 12_000 && T() - start < 70.0; f += 30)
        {
            harness.PumpFrames(30);
            foreach (var n in men)
            {
                var e = ScenarioTwin.ByName(cgf, n);
                if (e.IsNull) continue;
                if (cgf.HasComponent<BehaviorState>(e) && registry.TryGetName(cgf.GetComponent<BehaviorState>(e).ActiveBehaviorHash, out var task))
                    ran[n].Add(task);
                if (cgf.HasComponent<Stress>(e))
                {
                    var s = cgf.GetComponent<Stress>(e);
                    broke[n] |= s.Broken != 0;
                    peak[n] = MathF.Max(peak[n], s.Level);
                }
            }
        }
        foreach (var n in men)
            _out.WriteLine($"  {n,-8} peak {peak[n]:F2} broke {broke[n]} ran [{string.Join(", ", ran[n])}]");

        Assert.True(broke["Coward"] && ran["Coward"].Contains("Break") && !ran["Coward"].Contains("Surrender"),
            $"the Coward breaks and runs, he does not surrender (ran [{string.Join(", ", ran["Coward"])}])");
        Assert.True(broke["Waverer"] && ran["Waverer"].Contains("Surrender"),
            $"the Waverer breaks and, covered, surrenders (ran [{string.Join(", ", ran["Waverer"])}])");
        Assert.True(!broke["Fanatic"] && !ran["Fanatic"].Contains("Surrender") && peak["Fanatic"] > 0f,
            $"the Fanatic is under fire (peak {peak["Fanatic"]:F2}) and never breaks (ran [{string.Join(", ", ran["Fanatic"])}])");
    }
}
