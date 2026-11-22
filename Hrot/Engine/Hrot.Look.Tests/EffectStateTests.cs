using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Replication.Components;
using Hrot.Core.Tkb;
using Hrot.Look.Effects;
using Hrot.Map.Common.Components;

namespace Hrot.Look.Tests;

/// <summary>
/// ⭐ CE-11015 / CE-11016 (docs/designs/vehicle-appearance/DESIGN_Look_Rules_For_Renderers.md §3.3) — effects read from STATE, as a renderer does live, in a
/// replay and right after a seek: the effect entities the map spawns (and a recording keeps) rebuild the SAME <see cref="EffectSpec"/> the rule made, and an
/// automatic weapon's recorded flash rebuilds its burst stream — no events.
/// </summary>
public sealed class EffectStateTests
{
    private static (EntityRepository World, ITkbDatabase Tkb) World()
    {
        var world = new EntityRepository();
        var tkb = Hrot.Map.Common.HrotEnvironment.CreateTkb();
        world.SetSingletonManaged<ITkbDatabase>(tkb);
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<TkbIdentity>();
        world.RegisterComponent<Fdp.Toolkit.Time.Lifespan>();
        world.RegisterComponent<EffectAnchor>();
        world.RegisterComponent<EffectCause>();
        return (world, tkb);
    }

    private static Entity Unit(EntityRepository world, long type, Vector3 at)
    {
        var e = world.CreateEntity();
        world.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
        world.AddComponent(e, new TkbIdentity { TkbType = (TkbTypeId)type });
        return e;
    }

    /// <summary>The map's spawner, as it makes an effect entity from a spec (<c>EffectSpawnSystem.SpawnAll</c>).</summary>
    private static Entity Spawn(EntityRepository world, in EffectSpec spec, double now)
    {
        var e = world.CreateEntity();
        world.AddComponent(e, new SimTransform { Position = spec.At, Rotation = spec.Rotation });
        world.AddComponent(e, new TkbIdentity { TkbType = (TkbTypeId)spec.EffectType });
        world.AddComponent(e, Fdp.Toolkit.Time.Lifespan.Starting(now, spec.Lifetime));
        if (spec.Anchor is { } a) world.AddComponent(e, a);
        if (spec.Cause is { } c) world.AddComponent(e, c);
        return e;
    }

    /// <summary>
    /// CE-11015 — every effect a tank's burst and a ground burst make, spawned as entities, rebuilds the spec the rule made: the type, kind, size class,
    /// surface, munition DIS type, place, lifetime and what it struck — and when it began. 🔒 Red-proved: <c>SpecOf</c> ignoring <c>EffectCause</c> ⇒ red.
    /// </summary>
    [Fact]
    public void CE11015_AnEffectEntity_RebuildsTheSpecTheRuleMade()
    {
        var (world, tkb) = World();
        var tank = Unit(world, Hrot.Map.Common.TkbEntityTypes.Tank_T72, new Vector3(0, 0, 3));
        var live = new List<EffectSpec>();
        EffectRules.ForDetonation(new DetonationNotification { Target = tank, HitX = 0, HitY = 0, HitZ = 1.5f, Damage = 25f }, world, tkb, live);
        EffectRules.ForDetonation(new DetonationNotification { Target = Entity.Null, HitX = 50, HitY = 20, HitZ = 0.4f, Damage = 1100f }, world, tkb, live);
        Assert.Equal(3, live.Count);   // the tank burst; the ground burst and its mark

        foreach (var made in live)
        {
            var e = Spawn(world, made, 12.0);
            Assert.True(EffectRules.SpecOf(world, e, tkb, out var back));
            Assert.Equal((made.EffectType, made.Kind, made.SizeClass, made.Surface, made.MunitionDis), (back.EffectType, back.Kind, back.SizeClass, back.Surface, back.MunitionDis));
            Assert.Equal((made.At, made.Lifetime, made.Cause), (back.At, back.Lifetime, back.Cause));
            Assert.Equal(12.0, back.StartedAt);
        }
        Assert.False(EffectRules.SpecOf(world, tank, tkb, out _));   // a unit is no effect
    }

    /// <summary>
    /// CE-11016 — an Apache's chain-gun flash, spawned as the map spawns it, rebuilds the burst stream: the same gun, aimed where the LIVE stream points
    /// (the flash records <c>FireStreams.TowardOf</c>), fired when the flash began; its Hellfire's flash (one round) makes no stream.
    /// 🔒 Red-proved: the automatic-weapon test dropped ⇒ red (the Hellfire streams).
    /// </summary>
    [Fact]
    public void CE11016_AnAutomaticWeaponsFlash_RebuildsItsStream_AimedAsTheLiveOne()
    {
        var (world, tkb) = World();
        var heli = Unit(world, Hrot.Map.Common.TkbEntityTypes.Heli_AH64, new Vector3(0, 0, 30));
        var tank = Unit(world, Hrot.Map.Common.TkbEntityTypes.Tank_T72, new Vector3(400, 0, 0));
        var gun = new WeaponFireNotification { Shooter = heli, Target = tank, WeaponIndex = 0 };
        Assert.True(FireStreams.StreamOf(in gun, world, out var live));

        var specs = new List<EffectSpec>();
        EffectRules.ForShot(in gun, world, tkb, specs);
        var flash = Spawn(world, specs.First(s => s.Anchor != null), 7.25);
        Assert.True(FireStreams.StreamOfFlash(world, flash, out var rebuilt, out double firedAt));
        Assert.Equal(7.25, firedAt);
        Assert.Equal((live.Shooter, live.WeaponIndex, live.Toward), (rebuilt.Shooter, rebuilt.WeaponIndex, rebuilt.Toward));

        specs.Clear();
        var hellfire = gun with { WeaponIndex = 1 };
        EffectRules.ForShot(in hellfire, world, tkb, specs);
        foreach (var s in specs)
            if (s.Anchor != null) Assert.False(FireStreams.StreamOfFlash(world, Spawn(world, s, 8.0), out _, out _));
    }

    /// <summary>
    /// ⭐ CE-11024 — an impact's effect entity says what the round DID there (entry / exit / stop) and WHO fired it, live and after a recording is
    /// played back and SEEKED (the recorder keeps the unmanaged component as bytes); a detonation's effect carries its shooter and no impact kind.
    /// </summary>
    [Fact]
    public void CE11024_AnImpactsEffect_CarriesItsKindAndShooter_LiveAndAfterASeek()
    {
        var (world, tkb) = World();
        var rifleman = Unit(world, Hrot.Map.Common.TkbEntityTypes.Tank_T72, new Vector3(-30, 0, 1));
        var specs = new List<EffectSpec>();
        var hit = new RoundImpactNotification { Shooter = rifleman, X = 5, Y = 5, Z = 0, NormalZ = 1, TravelX = 1, Kind = (byte)Fdp.Toolkit.Combat.ImpactKind.Exit };
        Assert.True(EffectRules.ForImpact(in hit, world, tkb, specs) > 0);
        Assert.All(specs, sp => Assert.Equal(rifleman, sp.Cause!.Value.Shooter));
        Assert.Equal((byte)2, Assert.Single(specs, sp => sp.Cause!.Value.Impact != 0).Cause!.Value.Impact);   // Exit + 1, on ONE effect (CE-11030)
        var e = Spawn(world, specs.Single(sp => sp.Cause!.Value.Impact != 0), 3.0);

        var burst = new List<EffectSpec>();
        EffectRules.ForDetonation(new DetonationNotification { Shooter = rifleman, Target = Entity.Null, HitX = 50, HitY = 20, HitZ = 0.4f, Damage = 1100f }, world, tkb, burst);
        Assert.All(burst, sp => Assert.Equal(((byte)0, rifleman), (sp.Cause!.Value.Impact, sp.Cause!.Value.Shooter)));   // 0 = not a round's impact

        string path = System.IO.Path.GetTempFileName() + ".fdp";
        try
        {
            using (var recorder = new Fdp.Core.FlightRecorder.AsyncRecorder(path))
            {
                world.Tick();
                recorder.CaptureKeyframe(world, 100_000L, blocking: true, eventBus: world.Bus);
                uint prev = world.GlobalVersion - 1;
                world.Bus.SwapBuffers();
                world.Tick();
                recorder.CaptureFrame(world, prev, 200_000L, blocking: true, eventBus: world.Bus);
            }
            var (sandbox, _) = World();
            using var playback = new Fdp.Core.FlightRecorder.PlaybackController(path);
            Assert.True(playback.StepForward(sandbox));
            Assert.True(playback.StepForward(sandbox));
            playback.SeekToFrame(sandbox, 0);
            Assert.True(sandbox.IsAlive(e) && sandbox.HasComponent<EffectCause>(e));
            var cause = sandbox.GetComponentRO<EffectCause>(e);
            Assert.Equal(((byte)2, rifleman), (cause.Impact, cause.Shooter));
        }
        finally
        {
            try { System.IO.File.Delete(path); System.IO.File.Delete(path + ".meta.json"); } catch { }
        }
    }

    /// <summary>
    /// ⭐ CE-11030 — a round's impact makes a puff AND a mark and exactly ONE of them is the impact (<c>EffectCause.Impact</c> != 0): a renderer that makes one impact per such entity sees one. 🔴 Mutation: every effect stamped.
    /// </summary>
    [Fact]
    public void CE11030_OneImpact_GivesExactlyOneEntityWithAnImpactKind()
    {
        var (world, tkb) = World();
        var hit = new RoundImpactNotification { X = 5, Y = 5, Z = 0, NormalZ = 1, TravelX = 1, Kind = (byte)Fdp.Toolkit.Combat.ImpactKind.Stop, Ammo = (long)tkb.GetAll().First(t => t.DisType.Kind == 2).TkbType };
        var specs = new List<EffectSpec>();
        Assert.True(EffectRules.ForImpact(in hit, world, tkb, specs) >= 2, "a puff and a mark");
        var entities = specs.Select(sp => Spawn(world, sp, 1.0)).ToList();
        var impacts = entities.Where(e => world.GetComponentRO<EffectCause>(e).Impact != 0).ToList();
        Assert.Single(impacts);
        Assert.Equal((byte)3, world.GetComponentRO<EffectCause>(impacts[0]).Impact);   // Stop + 1
    }

    /// <summary>⭐ CE-11030 — the effect says the face's normal as the notification did (every effect of the impact; all 0 for a detonation's). 🔴 Mutation: Normal dropped.</summary>
    [Fact]
    public void CE11030_AnImpactsEffects_CarryTheNotificationsNormal_AndADetonationsDoNot()
    {
        var (world, tkb) = World();
        var hit = new RoundImpactNotification { X = 5, Y = 5, Z = 0, NormalX = 0.6f, NormalY = -0.0f, NormalZ = 0.8f, Kind = (byte)Fdp.Toolkit.Combat.ImpactKind.Entry };
        var specs = new List<EffectSpec>();
        Assert.True(EffectRules.ForImpact(in hit, world, tkb, specs) > 0);
        Assert.All(specs, sp => Assert.Equal(new Vector3(0.6f, 0f, 0.8f), sp.Cause!.Value.Normal));
        var burst = new List<EffectSpec>();
        EffectRules.ForDetonation(new DetonationNotification { Target = Entity.Null, HitX = 50, HitY = 20, HitZ = 0.4f, Damage = 1100f }, world, tkb, burst);
        Assert.NotEmpty(burst);
        Assert.All(burst, sp => Assert.Equal((byte)0, sp.Cause!.Value.Impact));
        Assert.All(burst, sp => Assert.Equal(Vector3.Zero, sp.Cause!.Value.Normal));
    }

    /// <summary>
    /// ⭐ CE-11030 — <c>EffectCause</c> is a COMPONENT (a recorded one), so appending <c>Normal</c> changes its layout hash and size: a recording made at the old layout (hash 0x99F49FE46F4AA46E, 40 bytes, hard-coded from the CE-11024
    /// base) only WARNS and has this component's data skipped (SchemaValidator: components warn, events throw) — it still validates and plays.
    /// </summary>
    [Fact]
    public void CE11030_AManifestRecordedAtTheOldEffectCauseLayout_StillValidates_ItsCauseDataSkipped()
    {
        Assert.True(typeof(EffectCause).IsValueType);   // a component struct, not a [EventId] event
        Assert.Empty(typeof(EffectCause).GetCustomAttributes(typeof(Fdp.Core.EventIdAttribute), false));
        var meta = new Fdp.Core.FlightRecorder.Metadata.RecordingMetadata
        {
            SchemaManifest = new Dictionary<int, Fdp.Core.FlightRecorder.ComponentSchemaInfo>
            {
                [Fdp.Core.GlobalComponentIds.EffectCause] = new() { Name = typeof(EffectCause).FullName!, Size = 40, LayoutHash = 0x99F49FE46F4AA46EUL, IsManaged = false },
            },
        };
        var changed = Fdp.Core.FlightRecorder.SchemaValidator.Validate(meta);   // does not throw
        Assert.Contains(Fdp.Core.GlobalComponentIds.EffectCause, changed);
        Assert.NotEqual(0x99F49FE46F4AA46EUL, Fdp.Core.FlightRecorder.ComponentLayoutHasher.ComputeHash(typeof(EffectCause)));
    }
}
