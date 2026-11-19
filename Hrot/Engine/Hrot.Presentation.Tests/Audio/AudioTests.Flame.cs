using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Motion;
using Hrot.Core.Tkb;
using Hrot.Map.Common;
using Hrot.UI.Common.Audio;
using Xunit;

namespace Hrot.Presentation.Tests.Audio;

/// <summary>
/// ⭐⭐ CE-4635 (flame weapons §8; docs/designs/audio/DESIGN_FPS_Audio.md "flamethrower roar") — the AUDIO half of the flame visuals: a flame burst is ONE roar, held as a loop while the jet is lit
/// (<c>FlameJets.TryGet</c>), not a gunshot sample per fuel shot. 🔴 Red-proofs: see each test.
/// </summary>
public sealed partial class AudioTests
{
    private static Entity Flamer(EntityRepository world, Vector3 at, double lastShot)
    {
        foreach (var t in new[] { typeof(WeaponPosture), typeof(WeaponAmmo) })
        {
            if (t == typeof(WeaponPosture) && !world.IsComponentTypeRegistered<WeaponPosture>()) world.RegisterComponent<WeaponPosture>();
            if (t == typeof(WeaponAmmo) && !world.IsComponentTypeRegistered<WeaponAmmo>()) world.RegisterComponent<WeaponAmmo>();
        }
        var e = Unit(world, UrbanCombatTkbCatalog.TkbFlamethrowerSoldier, at);
        world.AddComponent(e, new WeaponPosture { Kind = WeaponPostureKind.Aimed, Target = Entity.Null, Point = at + new Vector3(20f, 0f, 0f), ActiveMount = 0 });
        world.AddComponent(e, new WeaponAmmo { Count = 1, Load0 = MunitionTkbCatalog.TkbFlameFuel, LastShotAt0 = lastShot, BurstStartedAt0 = lastShot });
        return e;
    }

    /// <summary>⭐ (a) — a flamer whose jet is LIT is held as a FlameRoar loop (its own key beside his footsteps'), also standing still and at 150 m (past his steps' 60 m reach); unlit ⇒ not held; the
    /// loop goes the moment the jet goes out. 🔴 Red-proof: the reach not widened (<c>ReachOf</c> without the flame range) ⇒ the 150 m flamer is never heard; the lit check dropped ⇒ the unlit one roars.</summary>
    [Fact]
    public void CE4635_ALitFlamerIsHeldAsARoarLoop_AlsoStandingStillAt150m_AnUnlitOneIsNot()
    {
        var (world, tkb) = World();
        var me = Unit(world, TkbEntityTypes.Infantry_Rifleman, Vector3.Zero);
        var lit = Flamer(world, new Vector3(150f, 0f, 0f), lastShot: 10.0);                 // the sim clock is 10: fuel spent this instant
        var unlit = Flamer(world, new Vector3(-100f, 0f, 0f), lastShot: double.NegativeInfinity);
        var (layer, output) = Listening(world, tkb, me);
        for (int i = 0; i < 6; i++) layer.Update(0.016f);
        var roars = output.Looping.Where(v => v.Clip == SoundTkbCatalog.FlameRoar).ToList();
        var roar = Assert.Single(roars);                                                    // the lit one only, standing still, 150 m off
        Assert.True(layer.Sources.MoversHeard >= 1);
        // between searches it is still held every frame, and it stops when the jet goes out
        world.SetSingletonUnmanaged(new GlobalTime { TotalTime = 10.5 });                   // 0.5 s after the last fuel shot: out
        for (int i = 0; i < 3; i++) layer.Update(0.016f);
        Assert.DoesNotContain(output.Looping, v => v.Clip == SoundTkbCatalog.FlameRoar);
        world.SetComponent(lit, new WeaponAmmo { Count = 1, Load0 = MunitionTkbCatalog.TkbFlameFuel, LastShotAt0 = 10.45, BurstStartedAt0 = 10.0 });   // fires again
        for (int i = 0; i < 6; i++) layer.Update(0.016f);
        Assert.Single(output.Looping, v => v.Clip == SoundTkbCatalog.FlameRoar);
        Assert.NotEqual(0, roar.Id);
        _ = unlit;
    }

    /// <summary>⭐ (b) — a Flamethrower fuel shot queues NO <c>Shot</c> sound (it would play a gunshot sample ten times a second); a rifle's shot still does. 🔴 Red-proof: the
    /// <c>EffectRules.IsFuelShot</c> test removed from the queueing ⇒ the flamer's shot is heard.</summary>
    [Fact]
    public void CE4635_AFlamethrowersFuelShotQueuesNoShotSound_ARiflesStillDoes()
    {
        var (world, tkb) = World();
        var me = Unit(world, TkbEntityTypes.Infantry_Rifleman, Vector3.Zero);
        var flamer = Flamer(world, new Vector3(30f, 0f, 0f), lastShot: 10.0);
        var rifle = Unit(world, TkbEntityTypes.Infantry_Rifleman, new Vector3(-30f, 0f, 0f));
        var (layer, _) = Listening(world, tkb, me);
        int before = layer.Sources.ShotsHeard;
        for (int i = 0; i < 5; i++) Fire(world, flamer);
        layer.Update(0.016f);
        Assert.Equal(before, layer.Sources.ShotsHeard);
        Fire(world, rifle);
        layer.Update(0.016f);
        Assert.Equal(before + 1, layer.Sources.ShotsHeard);
    }

    /// <summary>⭐ (c) — the synthesized FlameRoar clip is a LOOP: not silent, and its last sample runs on into its first (no click at the seam). 🔴 Red-proof: <c>Loop(…)</c> dropped from the recipe (a
    /// plain <c>Finish</c> fades the end to 0, a jump of the clip's amplitude from the start) ⇒ red.</summary>
    [Fact]
    public void CE4635_TheFlameRoarClip_IsALoudLoopWithoutASeam()
    {
        var clip = Hrot.UI.Common.Audio.SoundSynthesizer.Make(SoundSynth.FlameRoar);
        Assert.InRange(clip.Length, 20000, 60000);
        Assert.True(clip.Max(s => Math.Abs((int)s)) > 8000, "not silent");
        double step = 0; for (int i = 1; i < clip.Length; i++) step += Math.Abs(clip[i] - clip[i - 1]);
        step /= clip.Length - 1;
        Assert.True(Math.Abs(clip[^1] - clip[0]) < 8 * step + 200, $"the seam: |{clip[^1]} − {clip[0]}| vs a typical step {step:F0}");
        // a loop does not fade out at its end (a one-shot's last 20 ms do): its last 1.3 ms are as loud as the clip
        double Rms(int from, int to) { double a = 0; for (int i = from; i < to; i++) a += (double)clip[i] * clip[i]; return Math.Sqrt(a / (to - from)); }
        Assert.True(Rms(clip.Length - 30, clip.Length) > 0.4 * Rms(0, clip.Length), "the end does not fade away");
        Assert.True(SoundTkbCatalog.SoundOf(null, SoundTkbCatalog.FlameRoar) is { Loop: true, Synth: SoundSynth.FlameRoar });
    }

    private static long FrameBytes(bool flamer, out bool held)
    {
        var (world, tkb) = World();
        if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Motion.Locomotion>()) world.RegisterComponent<Fdp.Toolkit.Motion.Locomotion>();
        var me = Unit(world, TkbEntityTypes.Infantry_Rifleman, Vector3.Zero);
        Entity tank = default;
        if (flamer) Flamer(world, new Vector3(120f, 0f, 0f), lastShot: 10.0);
        else
        {
            tank = Unit(world, TkbEntityTypes.Tank_T72, new Vector3(100f, 0f, 0f));
            world.AddComponent(tank, new Fdp.Toolkit.Motion.Locomotion { Class = LocomotionClass.Tracked });
        }
        var (layer, output) = Listening(world, tkb, me);
        void Frame() { if (!flamer) Move(world, tank, new Vector3(0.1f, 0f, 0f)); layer.Update(0.016f); }
        for (int i = 0; i < 40; i++) Frame();   // warm: the search, the caches, the lists' capacity
        held = output.Looping.Any(v => v.Clip == (flamer ? SoundTkbCatalog.FlameRoar : SoundTkbCatalog.TrackedEngine));
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 20; i++) Frame();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    /// <summary>⭐ (d, R-2084) — holding a lit flamer costs a steady frame no more than holding an ENGINE (a tank driving): the layer's own per-frame cost (the null output's voice bookkeeping, the beds)
    /// is the baseline both pay; the movers list is reused, <c>FlameJets.TryGet</c> is 0 B and the clock is read without a box. 🔴 Red-proof: a <c>new</c> per held flamer in <c>Movers</c> ⇒ red.</summary>
    [Fact]
    public void CE4635_AHeldLitFlamer_CostsNoMoreAllocationThanAHeldEngine()
    {
        long engine = FrameBytes(flamer: false, out bool engineHeld);
        long flame = FrameBytes(flamer: true, out bool flameHeld);
        Assert.True(engineHeld && flameHeld);
        Assert.True(flame <= engine, $"a held flamer {flame} B vs a held engine {engine} B over 20 frames");
    }
}
