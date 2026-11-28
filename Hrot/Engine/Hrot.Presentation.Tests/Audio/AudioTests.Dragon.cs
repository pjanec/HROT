using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Motion;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Core.Tkb;
using Hrot.Map.Common;
using Hrot.UI.Common.Audio;
using Hrot.UI.Common.Map3D;
using Xunit;

namespace Hrot.Presentation.Tests.Audio;

/// <summary>
/// ⭐⭐ CE-4634 (DESIGN_Dragon §8.2 S; NOTE_Backend_To_Ui_Dragon_Visuals "the dragon ROARS") — the dragon's sounds, every one from sim state: a WHOOSH on each down-stroke while it beats its wings (none
/// gliding, none landed), a ROAR on take-off, on a hit, on a new breath and now and then in flight (never two inside 3 s), a DYING ROAR once. It is no longer silent / a helicopter. 🔴 Red-proofs: see each test.
/// </summary>
public sealed partial class AudioTests
{
    private static Entity Dragon(EntityRepository world, Vector3 at, byte airborne)
    {
        if (!world.IsComponentTypeRegistered<FlightState>()) world.RegisterComponent<FlightState>();
        if (!world.IsComponentTypeRegistered<Health>()) world.RegisterComponent<Health>();
        var e = Unit(world, TkbEntityTypes.Dragon, at);
        world.AddComponent(e, new FlightState { Airborne = airborne });
        world.AddComponent(e, new Health { Current = 1000f, Max = 1000f });
        return e;
    }

    /// <summary>Steps the sim by <paramref name="dt"/> with the dragon moving at <paramref name="velocity"/>; returns the sim time.</summary>
    private static double Step(EntityRepository world, AudioLayer layer, Entity dragon, ref double sim, Vector3 velocity, float dt = 0.02f)
    {
        sim += dt;
        world.SetSingletonUnmanaged(new GlobalTime { TotalTime = sim });
        Move(world, dragon, velocity * dt);
        var fs = world.GetComponentRO<FlightState>(dragon);
        world.SetComponent(dragon, fs with { VerticalSpeed = velocity.Z });
        layer.Update(dt);
        return sim;
    }

    /// <summary>⭐ the WINGBEAT — beating (slow or climbing): one whoosh per stroke (≈ 1 a second), each at the start of a DOWN-stroke of the beat the look draws (<see cref="DragonPoses"/>);
    /// gliding fast and level: none; landed: none. 🔴 Red-proof: the <c>BeatTarget</c> gate dropped ⇒ the glide whooshes.</summary>
    [Fact]
    public void CE4634_TheDragonWhooshesOncePerDownStrokeWhileBeating_NoneGliding_NoneLanded()
    {
        var (world, tkb) = World();
        var me = Unit(world, TkbEntityTypes.Infantry_Rifleman, Vector3.Zero);
        var dragon = Dragon(world, new Vector3(30f, 0f, 25f), airborne: 1);
        var (layer, output) = Listening(world, tkb, me);
        double sim = 10.0;
        for (int i = 0; i < 10; i++) Step(world, layer, dragon, ref sim, new Vector3(4f, 0f, 2f));   // learn
        int w0 = layer.Sources.DragonWhooshes;
        for (int i = 0; i < 250; i++)   // 5 s climbing slowly
        {
            int before = layer.Sources.DragonWhooshes;
            double t = Step(world, layer, dragon, ref sim, new Vector3(4f, 0f, 2f));
            if (layer.Sources.DragonWhooshes > before) Assert.True(DragonPoses.DownStroke(t), $"a whoosh at sim {t} is on a down-stroke");
        }
        Assert.InRange(layer.Sources.DragonWhooshes - w0, 4, 6);
        Assert.Contains(output.Played, v => v.Clip == SoundTkbCatalog.DragonWhoosh);
        // fast and level: a glide — silent wings
        int w1 = layer.Sources.DragonWhooshes;
        for (int i = 0; i < 250; i++) Step(world, layer, dragon, ref sim, new Vector3(40f, 0f, 0f));   // 200 m on: still well inside the whoosh's 400 m
        Assert.Equal(w1, layer.Sources.DragonWhooshes);
        // landed: none
        world.SetComponent(dragon, new FlightState { Airborne = 0 });
        for (int i = 0; i < 150; i++) Step(world, layer, dragon, ref sim, Vector3.Zero);
        Assert.Equal(w1, layer.Sources.DragonWhooshes);
        // and it is not heard as a helicopter (no rotor loop) — its own class names no moving loop
        Assert.DoesNotContain(output.Looping, v => v.Clip == SoundTkbCatalog.Rotor);
    }

    /// <summary>⭐ the ROAR — on take-off, on a hit (not inside 3 s of the last), on a new breath; a DYING roar once; nothing after death. 🔴 Red-proof: the quiet window dropped ⇒ the hit at +1 s roars.</summary>
    [Fact]
    public void CE4634_TheDragonRoarsOnTakeOff_AHit_ANewBreath_NotTwiceIn3s_AndDiesWithALongRoarOnce()
    {
        var (world, tkb) = World();
        var me = Unit(world, TkbEntityTypes.Infantry_Rifleman, Vector3.Zero);
        var dragon = Dragon(world, new Vector3(40f, 0f, 3f), airborne: 0);
        var (layer, output) = Listening(world, tkb, me);
        double sim = 10.0;
        for (int i = 0; i < 10; i++) Step(world, layer, dragon, ref sim, Vector3.Zero);
        Assert.Equal(0, layer.Sources.DragonRoars);
        // take-off
        world.SetComponent(dragon, new FlightState { Airborne = 1 });
        Step(world, layer, dragon, ref sim, Vector3.Zero);
        Assert.Equal(1, layer.Sources.DragonRoars);
        world.SetComponent(dragon, new FlightState { Airborne = 0 });   // back on its legs: no now-and-then roar can stand in the events' way below
        // hit 1 s later: inside the quiet window
        for (int i = 0; i < 50; i++) Step(world, layer, dragon, ref sim, Vector3.Zero);
        world.SetComponent(dragon, new Health { Current = 900f, Max = 1000f });
        Step(world, layer, dragon, ref sim, Vector3.Zero);
        Assert.Equal(1, layer.Sources.DragonRoars);
        // hit again 3 s later: roars
        for (int i = 0; i < 150; i++) Step(world, layer, dragon, ref sim, Vector3.Zero);
        int r = layer.Sources.DragonRoars;
        world.SetComponent(dragon, new Health { Current = 800f, Max = 1000f });
        Step(world, layer, dragon, ref sim, Vector3.Zero);
        Assert.Equal(r + 1, layer.Sources.DragonRoars);
        // a new breath, 3 s later: roars (beside the flame's own roar loop)
        for (int i = 0; i < 160; i++) Step(world, layer, dragon, ref sim, Vector3.Zero);
        if (!world.IsComponentTypeRegistered<WeaponPosture>()) world.RegisterComponent<WeaponPosture>();
        if (!world.IsComponentTypeRegistered<WeaponAmmo>()) world.RegisterComponent<WeaponAmmo>();
        world.AddComponent(dragon, new WeaponPosture { Kind = WeaponPostureKind.Aimed, Target = Entity.Null, Point = new Vector3(40f, 20f, 0f), ActiveMount = 0 });
        world.AddComponent(dragon, new WeaponAmmo { Count = 1, Load0 = MunitionTkbCatalog.TkbDragonFire, LastShotAt0 = sim + 0.02, BurstStartedAt0 = sim + 0.02 });
        r = layer.Sources.DragonRoars;
        for (int k = 0; k < 3 && layer.Sources.DragonRoars == r; k++) Step(world, layer, dragon, ref sim, Vector3.Zero);   // the audio collects at its own rate: within 60 ms
        Assert.Equal(r + 1, layer.Sources.DragonRoars);
        // death: one long roar, then silence
        for (int i = 0; i < 20; i++) Step(world, layer, dragon, ref sim, Vector3.Zero);
        world.SetComponent(dragon, new Health { Current = 0f, Max = 1000f });
        r = layer.Sources.DragonRoars;
        for (int i = 0; i < 300; i++) Step(world, layer, dragon, ref sim, Vector3.Zero);
        Assert.Equal(1, layer.Sources.DragonDeathRoars);
        Assert.Equal(r, layer.Sources.DragonRoars);
        Assert.Contains(output.Played, v => v.Clip == SoundTkbCatalog.DragonDeathRoar);
        Assert.Contains(output.Played, v => v.Clip == SoundTkbCatalog.DragonRoar);
    }

    /// <summary>⭐ now and then in flight: about once in <see cref="SoundSources.RoarEverySeconds"/>, the same on every host (a closed-form count of sim time and its id); never on the ground.</summary>
    [Fact]
    public void CE4634_ADragonRoarsNowAndThenInFlight_AboutEvery20s_NeverOnTheGround()
    {
        var (world, tkb) = World();
        var me = Unit(world, TkbEntityTypes.Infantry_Rifleman, Vector3.Zero);
        var dragon = Dragon(world, new Vector3(40f, 0f, 25f), airborne: 1);
        var (layer, _) = Listening(world, tkb, me);
        double sim = 10.0;
        for (int i = 0; i < 10; i++) Step(world, layer, dragon, ref sim, Vector3.Zero, 0.1f);
        int r0 = layer.Sources.DragonRoars;
        for (int i = 0; i < 1000; i++) Step(world, layer, dragon, ref sim, Vector3.Zero, 0.1f);   // 100 s hovering
        Assert.InRange(layer.Sources.DragonRoars - r0, 4, 6);
        world.SetComponent(dragon, new FlightState { Airborne = 0 });
        Step(world, layer, dragon, ref sim, Vector3.Zero, 0.1f);
        int r1 = layer.Sources.DragonRoars;
        for (int i = 0; i < 1000; i++) Step(world, layer, dragon, ref sim, Vector3.Zero, 0.1f);
        Assert.Equal(r1, layer.Sources.DragonRoars);
    }

    /// <summary>⭐ the three placeholder clips are sounds (not silence), a whoosh short, the death roar the longest; the catalogue knows them with the TKB's 1.5 km roar reach.</summary>
    [Fact]
    public void CE4634_TheDragonsClips_AreAudible_AWhooshShort_TheDeathRoarLongest()
    {
        short Peak(short[] s) => s.Length == 0 ? (short)0 : s.Max(x => (short)Math.Abs((int)x));
        var whoosh = SoundSynthesizer.Make(SoundSynth.DragonWhoosh);
        var roar = SoundSynthesizer.Make(SoundSynth.DragonRoar);
        var death = SoundSynthesizer.Make(SoundSynth.DragonDeathRoar);
        Assert.True(Peak(whoosh) > 3000 && Peak(roar) > 3000 && Peak(death) > 3000);
        Assert.InRange(whoosh.Length / (float)SoundSynthesizer.SampleRate, 0.5f, 1f);
        Assert.True(death.Length > roar.Length && roar.Length > whoosh.Length);
        Assert.Equal(1500f, SoundTkbCatalog.SoundOf(null, SoundTkbCatalog.DragonRoar)!.RangeM);
        Assert.False(SoundTkbCatalog.SoundOf(null, SoundTkbCatalog.DragonWhoosh)!.Loop);
    }
}
