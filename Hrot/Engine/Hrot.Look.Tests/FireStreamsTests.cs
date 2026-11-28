using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Replication.Components;
using Hrot.Core.Tkb;
using Hrot.Look.Effects;

namespace Hrot.Look.Tests;

/// <summary>
/// ⭐ CE-11001 (RR-1, docs/designs/vehicle-appearance/DESIGN_Look_Rules_For_Renderers.md) — an automatic weapon's tracer stream, called the way an external
/// renderer calls it: <see cref="FireStreams.StreamOf"/> per fire notification, then <see cref="FireStreams.Dashes(EntityRepository, in TracerStream, double, List{ValueTuple{Vector3, Vector3}})"/>
/// per instant — the same two calls the map makes (<c>EffectSpawnSystem</c> records <c>TowardOf</c>, <c>ProjectileLayer.DrawStreams</c> draws <c>Dashes</c>).
/// </summary>
public sealed class FireStreamsTests
{
    private static EntityRepository World()
    {
        var world = new EntityRepository();
        world.SetSingletonManaged<ITkbDatabase>(Hrot.Map.Common.HrotEnvironment.CreateTkb());
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<TkbIdentity>();
        return world;
    }

    private static Entity Unit(EntityRepository world, long type, Vector3 at)
    {
        var e = world.CreateEntity();
        world.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
        world.AddComponent(e, new TkbIdentity { TkbType = (TkbTypeId)type });
        return e;
    }

    /// <summary>
    /// RR-1 — an Apache's chain gun at a tank: a stream toward the TANK, whose dashes start at the gun's muzzle (the engine's <c>Muzzle.TryOf</c>) and run
    /// toward it; its Hellfire (one round) and a refused burst draw none; once the window is past, nothing. 🔒 Red-proved: <c>StreamOf</c> without the
    /// <c>IsAutomatic</c> check ⇒ red (the Hellfire draws a stream).
    /// </summary>
    [Fact]
    public void RR1_AnAutomaticShot_StreamsFromTheMuzzle_TowardItsTarget_OneRoundAndARefusedShotDrawNone()
    {
        var world = World();
        var heli = Unit(world, Hrot.Map.Common.TkbEntityTypes.Heli_AH64, new Vector3(0, 0, 30));
        var tank = Unit(world, Hrot.Map.Common.TkbEntityTypes.Tank_T72, new Vector3(400, 0, 0));

        var gun = new WeaponFireNotification { Shooter = heli, Target = tank, WeaponIndex = 0 };
        Assert.True(FireStreams.StreamOf(in gun, world, out var stream));
        Assert.Equal(new Vector3(400, 0, 0), stream.Toward);
        Assert.Equal((heli, 0), (stream.Shooter, stream.WeaponIndex));

        var dashes = new List<(Vector3 From, Vector3 To)>();
        float strength = FireStreams.Dashes(world, in stream, 0.1, dashes, out var muzzle);
        Assert.True(Fdp.Toolkit.Combat.Muzzle.TryOf(world, heli, 0, out var engineMuzzle));
        Assert.Equal(engineMuzzle, muzzle);
        Assert.Equal(1f - 0.1f / FireStreams.Window, strength, 3);
        Assert.NotEmpty(dashes);
        var along = Vector3.Normalize(new Vector3(400, 0, 0) - muzzle);
        foreach (var (from, to) in dashes)
        {
            Assert.True(Vector3.Dot(Vector3.Normalize(to - from), along) > 0.999f);
            Assert.True(Vector3.Distance(from, to) <= FireStreams.DashLength + 1e-3f);
        }

        Assert.Equal(0f, FireStreams.Dashes(world, in stream, FireStreams.Window + 0.01, dashes));
        Assert.Empty(dashes);

        var hellfire = gun with { WeaponIndex = 1 };
        Assert.False(FireStreams.StreamOf(in hellfire, world, out _));
        var refused = gun with { Refusal = FireRefusal.TooFar };
        Assert.False(FireStreams.StreamOf(in refused, world, out _));
    }

    /// <summary>RR-1 — a door gun fired at a POINT (no target, no posture): the stream runs along the round's own launch for the weapon's range
    /// (<see cref="FireStreams.StreamToward"/>, CE-1357), never to the map's corner; a shooter that is gone draws nothing.</summary>
    [Fact]
    public void RR1_AShotAtAPoint_StreamsAlongItsLaunch_AndAGoneShooterDrawsNothing()
    {
        var world = World();
        var heli = Unit(world, Hrot.Map.Common.TkbEntityTypes.Heli_AH64, new Vector3(0, 0, 30));
        var fire = new WeaponFireNotification { Shooter = heli, WeaponIndex = 0, Origin = new Vector3(2, 0, 29), Velocity = new Vector3(800, 0, -40) };
        Assert.True(FireStreams.StreamOf(in fire, world, out var stream));
        Assert.NotEqual(Vector3.Zero, stream.Toward);
        Assert.True(Vector3.Dot(Vector3.Normalize(stream.Toward - fire.Origin), Vector3.Normalize(fire.Velocity)) > 0.999f);

        world.DestroyEntity(heli);
        var dashes = new List<(Vector3 From, Vector3 To)>();
        Assert.Equal(0f, FireStreams.Dashes(world, in stream, 0.1, dashes));
        Assert.Empty(dashes);
    }

    /// <summary>RR-1 — per instant the stream allocates nothing once the caller's list has grown (it runs every frame for every gun that fired).</summary>
    [Fact]
    public void RR1_TheDashesOfAnInstant_AllocateNothing()
    {
        var world = World();
        var heli = Unit(world, Hrot.Map.Common.TkbEntityTypes.Heli_AH64, new Vector3(0, 0, 30));
        var tank = Unit(world, Hrot.Map.Common.TkbEntityTypes.Tank_T72, new Vector3(400, 0, 0));
        var gun = new WeaponFireNotification { Shooter = heli, Target = tank, WeaponIndex = 0 };
        Assert.True(FireStreams.StreamOf(in gun, world, out var stream));
        var dashes = new List<(Vector3 From, Vector3 To)>(256);
        FireStreams.Dashes(world, in stream, 0.2, dashes);   // warm
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10; i++) FireStreams.Dashes(world, in stream, 0.05 + i * 0.02, dashes);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
