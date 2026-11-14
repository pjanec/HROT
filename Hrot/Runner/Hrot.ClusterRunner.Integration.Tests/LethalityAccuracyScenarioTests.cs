using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Testing;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ⭐⭐ <c>CE-6008</c> (city battle L-1, lethality as scenario settings; 📄 docs/designs/group-maneuvers/DESIGN_City_Battle_Showcase.md §4b) —
/// <c>scenarios/showcases/city-battle/features/lethality-accuracy</c> in-process on the four hosts: weapon ACCURACY is a scenario setting through the
/// EXISTING side skill amplifier (a 0 s trigger: <c>SetSkillAmplifier</c> Friend +2, Hostile −2 ⇒ <c>ProficiencyFillSystem</c> →
/// <c>CombatProficiency.SideTiers</c> → <c>HitModel.Sigma</c> × <c>ProficiencyOf.SpreadFactor</c>). Two IDENTICAL lanes — a rifleman firing 30 rounds
/// (<c>FireAtPoint</c>) at an immortal man 100 m away — differ ONLY by side ⇒ the better-amplified side hits more.
/// 🔴 Red-proof: set both tiers to 0 in the staged trigger ⇒ the two targets lose about the same ⇒ red.
/// </summary>
[Collection("HeavyE2ETests")]
public sealed class LethalityAccuracyScenarioTests : IDisposable
{
    private const int DomainId = 179;   // CycloneDDS accepts 0–232; 179 used by no other rail (ClusterOpE2e owns 170–173, 176–178 the showcase rails, the AI debug surface 181–189; grep 2026-11-13)
    private readonly List<string> _staged = new();
    private readonly ITestOutputHelper _out;

    public LethalityAccuracyScenarioTests(ITestOutputHelper output) => _out = output;
    public void Dispose() { foreach (var id in _staged) NasScenarioStaging.Remove(id); }

    private static float Lost(EntityRepository w, string name)
    {
        var e = ScenarioTwin.ByName(w, name);
        if (e.IsNull || !w.HasComponent<Health>(e)) return -1f;
        var h = w.GetComponentRO<Health>(e);
        return h.Max - h.Current;
    }

    private static int Tiers(EntityRepository w, string name)
    {
        var e = ScenarioTwin.ByName(w, name);
        return !e.IsNull && w.IsComponentTypeRegistered<CombatProficiency>() && w.HasComponent<CombatProficiency>(e)
            ? w.GetComponentRO<CombatProficiency>(e).SideTiers : 0;
    }

    private static int Fired(EntityRepository w, string name)
    {
        var e = ScenarioTwin.ByName(w, name);
        if (e.IsNull || !w.IsComponentTypeRegistered<WeaponAmmo>() || !w.HasComponent<WeaponAmmo>(e)) return -1;
        var a = w.GetComponentRO<WeaponAmmo>(e);
        return a.Max0 - a.Ammo0;
    }

    [Fact(Timeout = 600_000)]
    public async Task CE6008_LethalityAccuracy_TheSidesAmplifier_ChangesHowOftenItHits()
    {
        using var harness = await ScenarioTwin.Live("lethality-accuracy", _staged, DomainId);
        var cgf = harness.Cgf!.World!;
        var sim = harness.SimHost.World!;

        harness.PumpUntil(() => cgf.SimulationTime > 40.0, timeoutFrames: 20000);
        float red = Lost(sim, "Red Target"), blue = Lost(sim, "Blue Target");
        string state = $"sim {cgf.SimulationTime:0.0}: Red Target lost {red:0} (hit by the Friend +2 gunner), Blue Target lost {blue:0} (hit by the Hostile −2 gunner); "
                     + $"tiers on SimHost: Blue Gunner {Tiers(sim, "Blue Gunner"):+0;-0;0}, Red Gunner {Tiers(sim, "Red Gunner"):+0;-0;0}";
        _out.WriteLine(state);
        int blueFired = Fired(cgf, "Blue Gunner"), redFired = Fired(cgf, "Red Gunner");
        _out.WriteLine($"rounds fired (CGF WeaponAmmo): Blue Gunner {blueFired}, Red Gunner {redFired}");

        // ⓪ both lanes fired the same rounds (the outcome differs by accuracy alone)
        Assert.True(blueFired >= 25 && redFired >= 25, $"both gunners fire their rounds: Blue {blueFired}, Red {redFired}");
        // ① the amplifier reached the node that resolves the shot (the muscle)
        Assert.True(Tiers(sim, "Blue Gunner") == 2 && Tiers(sim, "Red Gunner") == -2, state);
        // ② the setting changes the OUTCOME: the Elite lane hits, and hits far more than the Untrained lane
        Assert.True(red > 0f && red >= 2f * blue, state);
    }
}
