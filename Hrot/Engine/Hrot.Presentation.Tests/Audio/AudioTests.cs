using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Time;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Core.Tkb;
using Hrot.Map.Common;
using Hrot.Map.Common.Components;
using Hrot.UI.Common.Audio;
using Hrot.UI.Common.Effects;
using Xunit;

namespace Hrot.Presentation.Tests.Audio;

/// <summary>
/// ⭐ CE-1120 (docs/designs/audio/DESIGN_FPS_Audio.md) — FPS audio: the generated sounds, the spatial rules, the voice budget, which sound a
/// shot / burst / mover makes, and the layer — silent until a unit is held, a sound device optional — all on the recording null output.
/// </summary>
public sealed class AudioTests
{
    private static readonly Ear North = new(new Vector3(0f, 0f, 1.7f), Vector3.UnitY);

    // ── the generated placeholders (AU-8) ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CE1120_EverySoundTypeHasAGeneratedPlaceholder_Normalised_Repeatable_AndItsLoopsJoin()
    {
        int peak = (int)(SoundSynthesizer.Peak * short.MaxValue);
        foreach (var (type, (name, sound)) in SoundTkbCatalog.BuiltIn)
        {
            Assert.NotEqual(SoundSynth.None, sound.Synth);
            var a = SoundSynthesizer.Make(sound.Synth);
            Assert.True(a.Length > SoundSynthesizer.SampleRate / 20, $"{name}: {a.Length} samples");
            int max = a.Max(s => Math.Abs((int)s));
            Assert.InRange(max, peak - 2, peak + 1);                       // normalised, never clipping
            Assert.Equal(a, SoundSynthesizer.Make(sound.Synth));           // the same every time
            if (sound.Loop)
            {
                // the seam (end → start) is no bigger a step than the clip's own steps: a cut without the cross-fade jumps between two
                // unrelated points, which in a smooth bed (wind, town, rumble) is many times its largest step
                int step = 0;
                for (int i = 1; i < a.Length; i++) step = Math.Max(step, Math.Abs(a[i] - a[i - 1]));
                Assert.True(Math.Abs(a[0] - a[^1]) <= 2 * step + 64, $"{name}: the loop's end meets its start ({a[^1]} → {a[0]}, largest step {step})");
            }
        }
        var wav = SoundSynthesizer.Wav(new short[] { 1, -1, 2 });
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal(44 + 6, wav.Length);
    }

    // ── the spatial rules (AU-2, §4) ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CE1120_GainFallsToSilenceAtTheRange_PanBySide_DopplerBySense_DelayBySpeedOfSound()
    {
        Assert.Equal(1f, Spatializer.Gain(5f, 10f, 800f), 3);
        Assert.True(Spatializer.Gain(100f, 10f, 800f) < Spatializer.Gain(50f, 10f, 800f));
        Assert.Equal(0f, Spatializer.Gain(800f, 10f, 800f));
        Assert.Equal(0f, Spatializer.Gain(900f, 10f, 800f));
        Assert.Equal(0f, Spatializer.Gain(1f, 10f, 0f));
        Assert.True(Spatializer.Gain(790f, 10f, 800f) < 0.01f * Spatializer.Gain(400f, 10f, 800f) + 1e-4f);

        Assert.True(Spatializer.Pan(North, new Vector3(50f, 0f, 0f)) > 0.8f, "east of a man looking north is on his right");
        Assert.True(Spatializer.Pan(North, new Vector3(-50f, 0f, 0f)) < -0.8f);
        Assert.Equal(0f, Spatializer.Pan(North, new Vector3(0f, 50f, 0f)), 3);
        Assert.Equal(0f, Spatializer.Pan(North, North.Position), 3);

        var at = new Vector3(0f, 200f, 0f);
        Assert.True(Spatializer.Doppler(North, at, new Vector3(0f, -30f, 0f)) > 1f, "coming: higher");
        Assert.True(Spatializer.Doppler(North, at, new Vector3(0f, 30f, 0f)) < 1f, "going: lower");
        Assert.Equal(1f + Spatializer.MaxDoppler, Spatializer.Doppler(North, at, new Vector3(0f, -1000f, 0f)), 3);

        Assert.Equal(1.0, Spatializer.Delay(343f), 3);
        Assert.Equal(1f, Spatializer.NearShare(50f));
        Assert.Equal(0f, Spatializer.NearShare(300f));
        var (point, d) = Spatializer.Closest(new Vector3(-10f, 2f, 0f), new Vector3(10f, 2f, 0f), Vector3.Zero);
        Assert.Equal(2f, d, 3);
        Assert.Equal(0f, point.X, 3);
    }

    // ── the voice budget and the delay (AU-7) ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CE1120_TheBudget_KeepsTheStrongest_ADelayedSoundWaits_AnUnheldLoopStops()
    {
        var output = Opened();
        var mixer = new SoundMixer();
        for (int i = 0; i < SoundMixer.MaxVoices; i++) mixer.Request(1, Vector3.Zero, false, 0.5f, 1f, 1, 0.0, 0.0);
        mixer.Update(output, 0.0, North);
        Assert.Equal(SoundMixer.MaxVoices, mixer.Voices);

        mixer.Request(1, Vector3.Zero, false, 0.5f, 1f, 6, 0.0, 0.0);   // a near shot takes a footstep's voice
        mixer.Request(1, Vector3.Zero, false, 0.5f, 1f, 0, 0.0, 0.0);   // a weaker one is dropped
        int dropped = mixer.Dropped;
        mixer.Update(output, 0.0, North);
        Assert.Equal(SoundMixer.MaxVoices + 1, mixer.Started);
        Assert.Equal(dropped + 1, mixer.Dropped);
        Assert.Equal(SoundMixer.MaxVoices, mixer.Voices);

        mixer.Clear(output);
        mixer.Request(1, new Vector3(-30f, 0f, 0f), true, 0.5f, 1f, 5, 10.0, 1.0);
        mixer.Update(output, 10.5, North);
        Assert.Equal(1, mixer.Waiting);
        mixer.Update(output, 11.0, North);
        Assert.Equal(0, mixer.Waiting);
        Assert.True(output.Played[^1].Pan < 0f, "panned from where it is when it plays");

        mixer.Hold(output, 42, 1, 0.5f, 0f, 1f, 2);
        mixer.Update(output, 11.1, North);
        Assert.Single(output.Looping);
        mixer.Update(output, 11.2, North);   // nobody held it this frame
        Assert.Empty(output.Looping);
    }

    // ── which sound (AU-5) ────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>⭐ CE-1141 — footsteps sit well under a rifle shot: at least 15 dB below it in the catalogue, and the operator's own
    /// movement at half. 🔒 User, 2026-10-16: <i>"the footstep sound should be way less loud now too close to the rifle shot."</i></summary>
    [Fact]
    public void CE1141_FootstepsAreWellUnderARifleShot()
    {
        var steps = SoundTkbCatalog.SoundOf(null, SoundTkbCatalog.Footsteps)!;
        var shot = SoundTkbCatalog.SoundOf(null, SoundTkbCatalog.ShotSmall)!;
        Assert.True(shot.VolumeDb - steps.VolumeDb >= 15f, $"footsteps {steps.VolumeDb} dB vs the shot {shot.VolumeDb} dB");
        Assert.True(SoundSources.OwnMoverShare <= 0.5f);
    }

    [Fact]
    public void CE1120_AShotsSoundFollowsItsFlash_ABurstsItsFireball_AMoversItsClass()
    {
        var tkb = HrotEnvironment.CreateTkb();
        Assert.Equal((SoundTkbCatalog.ShotSmall, SoundTkbCatalog.ShotFarSmall, SoundTkbCatalog.Burst), SoundTkbCatalog.ShotFor(tkb, 0, 25f));
        Assert.Equal(SoundTkbCatalog.ShotLarge, SoundTkbCatalog.ShotFor(tkb, 0, 1200f).Near);
        Assert.Equal(SoundTkbCatalog.ExplosionLarge, SoundTkbCatalog.DetonationFor(tkb, MunitionTkbCatalog.Tkb81mmMortarHe, 0f));
        Assert.Equal(SoundTkbCatalog.ExplosionMedium, SoundTkbCatalog.DetonationFor(tkb, MunitionTkbCatalog.TkbM67Grenade, 0f));
        Assert.Equal(SoundTkbCatalog.TrackedEngine, SoundTkbCatalog.MovingFor(tkb, 0, SoundSourceClass.TrackedEngine, fixedWing: false));
        Assert.Equal(SoundTkbCatalog.Jet, SoundTkbCatalog.MovingFor(tkb, 0, SoundSourceClass.Rotor, fixedWing: true));
        Assert.Null(SoundSources.MovingDefault(LocomotionClass.Unknown));
        Assert.Equal(SoundSourceClass.Footsteps, SoundSources.MovingDefault(LocomotionClass.Pedestrian)!.MovingClass);
        foreach (var type in SoundTkbCatalog.BuiltIn.Keys)
        {
            Assert.True(tkb.TryGetByType(type, out var t) && t!.GetDescriptor<SoundDto>() != null, $"{type} registered");
            Assert.True(t!.GetDescriptor<TkbMasterDto>()!.HideFromPalette);
        }
    }

    // ── the layer (AU-3, AU-6) ────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CE1120_TheLayerSleepsUntilAUnitIsHeld_GoesSilentWithNoDevice_AndStopsWhenTheUnitIsLost()
    {
        var (world, tkb) = World();
        var me = Unit(world, TkbEntityTypes.Infantry_Rifleman, Vector3.Zero);
        var held = Entity.Null;
        int made = 0;
        var layer = new AudioLayer(() => world, () => tkb, () => held, () => North, () => { made++; return new NullAudioOutput(); });
        layer.Update(0.016f);
        Assert.Equal((AudioState.Idle, 0), (layer.State, made));
        Assert.Null(SoundEvents.Of(world));

        held = me;
        layer.Update(0.016f);
        Assert.Equal(AudioState.Active, layer.State);
        var output = (NullAudioOutput)layer.Output!;
        Assert.All(SoundTkbCatalog.BuiltIn.Keys, k => Assert.True(output.Has(k)));
        Assert.NotNull(SoundEvents.Of(world));
        Assert.Contains(output.Looping, v => v.Clip == SoundTkbCatalog.Wind);

        held = Entity.Null;
        layer.Update(0.016f);
        Assert.Equal(AudioState.Idle, layer.State);
        Assert.Null(SoundEvents.Of(world));
        Assert.Empty(output.Looping);

        var mute = new AudioLayer(() => world, () => tkb, () => me, () => North, () => new NullAudioOutput { Opens = false });
        mute.Update(0.016f);
        Assert.Equal(AudioState.Silent, mute.State);
    }

    /// <summary>⭐ CE-1137 — with no unit held the scene is silent (AU-3) unless the operator turned on the camera sound: then the layer
    /// listens from the camera, with no "own" unit. 🔒 User, 2026-10-16: <i>"a menu settings for enabling sounds in 3d map non-FPS mode
    /// heard from the camera position"</i>.</summary>
    [Fact]
    public void CE1137_WithNoUnitHeld_TheCameraListens_OnlyWhenTheOperatorAsks()
    {
        var (world, tkb) = World();
        bool camera = false;
        var layer = new AudioLayer(() => world, () => tkb, () => Entity.Null, () => North, () => new NullAudioOutput(), cameraSound: () => camera);
        layer.Update(0.016f);
        Assert.Equal(AudioState.Idle, layer.State);
        camera = true;
        layer.Update(0.016f);
        Assert.Equal(AudioState.Active, layer.State);
        Assert.NotNull(SoundEvents.Of(world));
        camera = false;
        layer.Update(0.016f);
        Assert.Equal(AudioState.Idle, layer.State);
    }

    [Fact]
    public void CE1120_AShotIsHeardAfterItsSoundTravels_FromItsSide_HisOwnAtOnce()
    {
        var (world, tkb) = World();
        var me = Unit(world, TkbEntityTypes.Infantry_Rifleman, new Vector3(4f, 0f, 0f));   // his gun 4 m off the ear (a station gun, a muzzle)
        var far = Unit(world, TkbEntityTypes.Infantry_Rifleman, new Vector3(343f, 0f, 0f));
        var near = Unit(world, TkbEntityTypes.Infantry_Rifleman, new Vector3(-50f, 0f, 0f));
        var (layer, output) = Listening(world, tkb, me);

        Fire(world, far);
        Fire(world, near);
        layer.Update(0.016f);
        Assert.DoesNotContain(output.Played, v => v.Clip is SoundTkbCatalog.ShotFarSmall);
        layer.Update(0.2f);   // 0.15 s: the near one arrives
        var heard = Assert.Single(output.Played, v => v.Clip == SoundTkbCatalog.ShotSmall);
        Assert.True(heard.Pan < -0.5f, "50 m to his left");
        layer.Update(1.0f);   // 1 s: the far one
        var distant = Assert.Single(output.Played, v => v.Clip == SoundTkbCatalog.ShotFarSmall);
        Assert.True(distant.Pan > 0.5f && distant.Gain < heard.Gain, "343 m to his right, quieter");

        int before = output.Played.Count;
        Fire(world, me);
        layer.Update(0.016f);
        var own = output.Played.Skip(before).Single(v => v.Clip == SoundTkbCatalog.ShotSmall);
        Assert.Equal(0f, own.Pan);   // his own: centred, though it is 4 m to his right
        Assert.Equal(3, layer.Sources.ShotsHeard);
    }

    /// <summary>⭐ CE-1216 (docs/DESIGN_Entity_Control.md §8k S-2) — the pass rule the crack and the first-person edge darkening share:
    /// a round passing within 5 m counts ONCE; one passing 10 m off, his own round and a round born beside him never do.</summary>
    [Fact]
    public void CE1216_ARoundPassingTheEar_CountsOnce_NotHisOwn_NotFarOff_NotBornBesideHim()
    {
        var (world, _) = World();
        if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.BallisticProjectile>()) world.RegisterComponent<Fdp.Toolkit.Combat.Components.BallisticProjectile>();
        var me = Unit(world, TkbEntityTypes.Infantry_Rifleman, Vector3.Zero);
        var them = Unit(world, TkbEntityTypes.Infantry_Rifleman, new Vector3(-300f, 0f, 0f));
        Entity Round(Entity shooter, Vector3 at)
        {
            var r = world.CreateEntity();
            world.AddComponent(r, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            world.AddComponent(r, new Fdp.Toolkit.Combat.Components.BallisticProjectile { Shooter = shooter });
            return r;
        }
        var near = Round(them, new Vector3(-30f, 2f, 0f));
        var far = Round(them, new Vector3(-30f, 10f, 0f));
        var mine = Round(me, new Vector3(-30f, 1f, 0f));
        var beside = Round(them, new Vector3(0.5f, 0f, 0f));
        var passes = new RoundPasses();
        var heard = new List<float>();
        Assert.Equal(0, passes.Scan(world, me, Vector3.Zero, (_, d) => heard.Add(d)));   // first sight: no segment yet
        foreach (var r in new[] { near, far, mine, beside }) Move(world, r, new Vector3(60f, 0f, 0f));
        Assert.Equal(1, passes.Scan(world, me, Vector3.Zero, (_, d) => heard.Add(d)));
        Assert.Equal(2f, Assert.Single(heard), 3);                                         // the near one, 2 m off
        foreach (var r in new[] { near, far, mine, beside }) Move(world, r, new Vector3(-60f, 0f, 0f));   // back past him
        Assert.Equal(0, passes.Scan(world, me, Vector3.Zero));                             // once per round
    }

    [Fact]
    public void CE1120_ATankComingIsHigherThanATankGoing_ARoundPastTheEarCracksOnce_AFarBattleRumbles()
    {
        var (world, tkb) = World();
        if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Motion.Locomotion>()) world.RegisterComponent<Fdp.Toolkit.Motion.Locomotion>();
        if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.BallisticProjectile>()) world.RegisterComponent<Fdp.Toolkit.Combat.Components.BallisticProjectile>();
        var me = Unit(world, TkbEntityTypes.Infantry_Rifleman, Vector3.Zero);
        var coming = Unit(world, TkbEntityTypes.Tank_T72, new Vector3(100f, 0f, 0f));
        var going = Unit(world, TkbEntityTypes.Tank_T72, new Vector3(-100f, 0f, 0f));
        world.AddComponent(coming, new Fdp.Toolkit.Motion.Locomotion { Class = LocomotionClass.Tracked });
        world.AddComponent(going, new Fdp.Toolkit.Motion.Locomotion { Class = LocomotionClass.Tracked });
        var (layer, output) = Listening(world, tkb, me);
        for (int i = 0; i < 3; i++)
        {
            Move(world, coming, new Vector3(-0.16f, 0f, 0f));   // 10 m/s toward him
            Move(world, going, new Vector3(-0.16f, 0f, 0f));    // 10 m/s away
            layer.Update(0.016f);
        }
        var engines = output.Looping.Where(v => v.Clip == SoundTkbCatalog.TrackedEngine).ToList();
        Assert.Equal(2, engines.Count);
        Assert.True(engines.Max(v => v.Pitch) > engines.Min(v => v.Pitch), "Doppler: the one coming is higher");
        Assert.True(engines.Single(v => v.Pan > 0f).Pitch > engines.Single(v => v.Pan < 0f).Pitch);

        var shooter = Unit(world, TkbEntityTypes.Infantry_Rifleman, new Vector3(-300f, 2f, 1.7f));
        var round = world.CreateEntity();
        world.AddComponent(round, new SimTransform { Position = new Vector3(-30f, 2f, 1.7f), Rotation = Quaternion.Identity });
        world.AddComponent(round, new Fdp.Toolkit.Combat.Components.BallisticProjectile { Shooter = shooter });
        layer.Update(0.016f);
        Move(world, round, new Vector3(60f, 0f, 0f));
        layer.Update(0.016f);
        Move(world, round, new Vector3(60f, 0f, 0f));
        layer.Update(0.016f);
        Assert.Single(output.Played, v => v.Clip == SoundTkbCatalog.BulletPass);

        var distant = Unit(world, TkbEntityTypes.Infantry_Rifleman, new Vector3(5000f, 0f, 0f));
        int heard = layer.Sources.ShotsHeard;
        for (int i = 0; i < 10; i++) Fire(world, distant);
        layer.Update(0.016f);
        Assert.Equal(heard, layer.Sources.ShotsHeard);   // out of earshot: not heard as shots …
        Assert.True(layer.Sources.RumbleLevel > 0.2f);   // … but as the distant battle
        Assert.Contains(output.Looping, v => v.Clip == SoundTkbCatalog.BattleRumble);
    }

    /// <summary>⭐ CE-1120 A2/A3 — a door opening is heard where it is (§2 "doors, boarding"); a body boarding or leaving a vehicle is
    /// heard as a vehicle door; the town bed follows how built-up it is around the ear.</summary>
    [Fact]
    public void CE1120_ADoorIsHeardWhereItIs_ABoardingAsAVehicleDoor_TheTownHumByItsBuildings()
    {
        var terrain = Hrot.Presentation.Tests.Map3D.Map3DTests.BtRange();
        var (world, tkb) = World();
        world.SetSingletonManaged(terrain);
        world.RegisterComponent<Fdp.Toolkit.Terrain.DoorState>();
        world.RegisterManagedComponent<Fdp.Toolkit.Terrain.TerrainObjectKey>();
        if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>()) world.RegisterComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>();
        var def = terrain.Doors[0];
        var ear = new Ear(new Vector3(def.Center.X + 5f, def.Center.Y, def.SillZ + 1.7f), Vector3.UnitY);   // 5 m east of the door
        var me = Unit(world, TkbEntityTypes.Infantry_Rifleman, ear.Position);
        var door = world.CreateEntity();
        world.AddComponent(door, new Fdp.Toolkit.Terrain.DoorState { State = Fdp.Toolkit.Terrain.TerrainDoorState.Closed });
        world.AddComponent(door, new Fdp.Toolkit.Terrain.TerrainObjectKey { Key = def.Key });
        var rider = Unit(world, TkbEntityTypes.Infantry_Rifleman, ear.Position + new Vector3(10f, 0f, 0f));
        var layer = new AudioLayer(() => world, () => tkb, () => me, () => ear, () => new NullAudioOutput { OneShotTicks = 1000 });
        layer.Update(0.016f);   // the first frame learns the doors and who is aboard
        var output = (NullAudioOutput)layer.Output!;
        Assert.DoesNotContain(output.Played, v => v.Clip == SoundTkbCatalog.Door);

        world.SetComponent(door, new Fdp.Toolkit.Terrain.DoorState { State = Fdp.Toolkit.Terrain.TerrainDoorState.Open });
        layer.Update(0.016f);
        layer.Update(0.1f);     // 5 m: 15 ms of sound travel
        var opened = Assert.Single(output.Played, v => v.Clip == SoundTkbCatalog.Door);
        Assert.True(opened.Pan < -0.5f, "the door is west of a man looking north: on his left");
        Assert.Equal(1, layer.Sources.DoorsHeard);

        world.AddComponent(rider, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag());
        layer.Update(0.016f);
        layer.Update(0.1f);
        Assert.Equal(1, layer.Sources.BoardingsHeard);
        Assert.True(output.Played.Last(v => v.Clip == SoundTkbCatalog.Door).Pan > 0.5f, "he boarded 10 m to the east");

        Assert.True(layer.Sources.TownLevel > 0f, "bt-range's buildings are around its doors");
        Assert.Contains(output.Looping, v => v.Clip == SoundTkbCatalog.Town);
        Assert.Equal(0f, SoundSources.Built(terrain, def.Center + new Vector2(5000f, 0f)));
    }

    /// <summary>⭐ CE-1120 AU-9 — the operator's volumes: F6 / F7 / F8 and the F10 panel change them, the mixer and the output take them,
    /// they are saved in HIS file and the next session starts from them; a broken file gives the defaults.</summary>
    [Fact]
    public void CE1120_TheOperatorsVolumes_ReachTheMixer_AndPersistInHisOwnFile()
    {
        var (world, tkb) = World();
        var me = Unit(world, TkbEntityTypes.Infantry_Rifleman, Vector3.Zero);
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"hrot-audio-{Guid.NewGuid():N}.json");
        try
        {
            var pressed = new HashSet<Raylib_cs.KeyboardKey>();
            AudioLayer Make() => new(() => world, () => tkb, () => me, () => North, () => new NullAudioOutput(), k => pressed.Contains(k), () => path);
            void Press(AudioLayer l, params Raylib_cs.KeyboardKey[] keys) { pressed.Clear(); pressed.UnionWith(keys); l.Update(0.016f); pressed.Clear(); }

            var layer = Make();
            layer.Update(0.016f);
            Assert.Equal(0.8f, layer.Volume, 3);
            Press(layer, Raylib_cs.KeyboardKey.F10);
            Assert.True(layer.PanelOpen);
            Press(layer, Raylib_cs.KeyboardKey.Down);               // master → weapons
            Press(layer, Raylib_cs.KeyboardKey.Left);               // weapons 100 → 90 %
            Press(layer, Raylib_cs.KeyboardKey.F6);                 // master 80 → 70 %
            Assert.Equal(0.9f, layer.Mixer.CategoryVolume[(int)SoundCategory.Weapons], 3);
            Assert.Equal(0.7f, layer.Output!.MasterVolume, 3);
            Assert.Equal("Weapons  90 %", layer.PanelRows()[1]);
            Press(layer, Raylib_cs.KeyboardKey.F8);
            Assert.Equal(0f, layer.Output!.MasterVolume);
            Press(layer, Raylib_cs.KeyboardKey.F8);

            var next = Make();
            next.Update(0.016f);
            Assert.Equal((0.7f, false), (next.Volume, next.Muted));
            Assert.Equal(0.7f, next.Output!.MasterVolume, 3);
            Assert.Equal(0.9f, next.Mixer.CategoryVolume[(int)SoundCategory.Weapons], 3);

            System.IO.File.WriteAllText(path, "{ not json");
            Assert.Equal(0.8f, AudioSettings.Load(path).Master, 3);
            Assert.Equal(AudioSettings.CategoryCount, AudioSettings.Load(null).Categories.Length);
        }
        finally { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
    }

    /// <summary>⭐ CE-1120 AU-8 — listen to the placeholders: <c>HROT_EXPORT_SOUNDS=&lt;folder&gt;</c> writes every generated sound type as a
    /// WAV (named by id and name) — what a CC0 recording would replace (assets/audio/LICENSES.md).</summary>
    [SkippableFact]
    public void CE1120_ExportThePlaceholders_ToListen()
    {
        string? dir = Environment.GetEnvironmentVariable("HROT_EXPORT_SOUNDS");
        Skip.If(string.IsNullOrWhiteSpace(dir), "opt-in: HROT_EXPORT_SOUNDS=<folder>");
        System.IO.Directory.CreateDirectory(dir!);
        foreach (var (type, (name, sound)) in SoundTkbCatalog.BuiltIn)
        {
            string file = System.IO.Path.Combine(dir!, $"{type}-{name.Replace("Sound: ", "").Replace(' ', '-')}.wav");
            System.IO.File.WriteAllBytes(file, SoundSynthesizer.Wav(SoundSynthesizer.Make(sound.Synth)));
            Assert.True(new System.IO.FileInfo(file).Length > 44);
        }
    }

    [Fact]
    public void CE1120_EveryMapHostGetsTheLayer_BesideThePossessionStation()
    {
        string pack = System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot(), "Hrot/Engine/Hrot.Presentation/ScenarioEditor/Map/MapInteractionPack.cs"));
        Assert.Contains("var audio = Hrot.UI.Common.Audio.AudioLayer.For(possession, viewSwitch, worldProvider, tkb);", pack);
        Assert.Contains("canvas.AddLayer(audio);", pack);
        Assert.Contains("possession, audio);", pack);
        string spawner = System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot(), "Hrot/Engine/Hrot.Presentation/Effects/EffectSystems.cs"));
        Assert.Contains("Hrot.UI.Common.Audio.SoundEvents.Of(world)", spawner);
    }

    /// <summary>
    /// ⭐ CE-1120 A0 — the REAL raylib output, opt-in (<c>HROT_AUDIO_SMOKE=1</c>; a CI box has no sound card): it opens or declines
    /// without throwing; when it opens (a card, or miniaudio's null backend) every clip loads from its generated samples, a one-shot
    /// plays and ends, a loop streams from native memory, and it closes cleanly.
    /// </summary>
    [SkippableFact]
    public void CE1120_TheRaylibOutput_OpensOrDeclines_AndDrivesEveryClip()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("HROT_AUDIO_SMOKE") == "1", "opt-in: HROT_AUDIO_SMOKE=1");
        using var output = new RaylibAudioOutput();
        bool open = output.Open();
        Skip.IfNot(open, "no audio device here (declined cleanly)");
        foreach (var (type, (_, sound)) in SoundTkbCatalog.BuiltIn)
            output.Define(type, SoundSynthesizer.Make(sound.Synth), SoundSynthesizer.SampleRate);
        Assert.All(SoundTkbCatalog.BuiltIn.Keys, k => Assert.True(output.Has(k)));
        int shot = output.Play(SoundTkbCatalog.EmptyClick, 0.1f, -0.5f, 1f);
        int loop = output.Loop(SoundTkbCatalog.Wind, 0.1f, 0.5f, 1f);
        Assert.True(shot > 0 && loop > 0);
        var until = DateTime.UtcNow.AddSeconds(1.5);
        while (DateTime.UtcNow < until) { output.Tick(); System.Threading.Thread.Sleep(20); }
        Assert.False(output.IsPlaying(shot), "the 80 ms click has ended");
        Assert.True(output.IsPlaying(loop), "the loop keeps going");
        output.StopAll();
        Assert.False(output.IsPlaying(loop));
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────────────────────────────────────────

    private static NullAudioOutput Opened()
    {
        var o = new NullAudioOutput();
        o.Open();
        o.Define(1, new short[100], SoundSynthesizer.SampleRate);
        return o;
    }

    private static (EntityRepository World, ITkbDatabase Tkb) World()
    {
        var world = new EntityRepository();
        var tkb = HrotEnvironment.CreateTkb();
        world.SetSingletonManaged<ITkbDatabase>(tkb);
        PresentationComponentRegistry.RegisterEffects(world);
        world.RegisterEvent<DetonationNotification>();
        world.RegisterEvent<WeaponFireNotification>();
        world.SetSingletonUnmanaged(new GlobalTime { TotalTime = 10.0 });
        return (world, tkb);
    }

    private static Entity Unit(EntityRepository world, long type, Vector3 at)
    {
        var e = world.CreateEntity();
        world.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
        world.AddComponent(e, new TkbIdentity { TkbType = type });
        return e;
    }

    private static void Move(EntityRepository world, Entity e, Vector3 by)
    {
        var tf = world.GetComponentRO<SimTransform>(e);
        world.SetComponent(e, tf with { Position = tf.Position + by });
    }

    private static (AudioLayer, NullAudioOutput) Listening(EntityRepository world, ITkbDatabase tkb, Entity me)
    {
        var layer = new AudioLayer(() => world, () => tkb, () => me, () => North, () => new NullAudioOutput { OneShotTicks = 1000 });
        layer.Update(0.016f);
        Assert.Equal(AudioState.Active, layer.State);
        return (layer, (NullAudioOutput)layer.Output!);
    }

    /// <summary>A shot as the bus brings it, read by the map host's spawner (which queues its sound).</summary>
    private static void Fire(EntityRepository world, Entity shooter)
    {
        world.Bus.Publish(new WeaponFireNotification { Shooter = shooter, Target = Entity.Null, WeaponIndex = 0 });
        world.Bus.SwapBuffers();
        new EffectSpawnSystem().Execute(world, 0.016f);
        ((EntityCommandBuffer)((Fdp.ModuleHost.Abstractions.ISimulationView)world).GetCommandBuffer()).Playback(world);
    }

    private static string RepoRoot()
    {
        var d = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !System.IO.File.Exists(System.IO.Path.Combine(d.FullName, "HROT.sln"))) d = d.Parent;
        return d!.FullName;
    }
}
