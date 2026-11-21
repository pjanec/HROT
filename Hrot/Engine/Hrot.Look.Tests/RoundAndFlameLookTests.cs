using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Perception.Signatures;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Core.Tkb;
using Hrot.Look.Effects;

namespace Hrot.Look.Tests;

/// <summary>
/// ⭐ CE-11004 (RR-4, docs/designs/vehicle-appearance/DESIGN_Look_Rules_For_Renderers.md) — rounds in flight and the flame look, called the way a renderer calls
/// them. The map's <c>ProjectileLayer.Resolve</c> and <c>EffectLayer.ResolveStateInto</c> call the same classes (their P1 / P2 / LR-3 / CE-1976 / CE-4647 and
/// Flame U-1…U-5 rails stay the proof that nothing it draws changed).
/// </summary>
public sealed class RoundAndFlameLookTests
{
    /// <summary>
    /// RR-4 — a replicated tracer round flying east at 800 m/s: its nose points along its velocity, it is not lying, its streak runs back 24 m
    /// (800 × 0.03); at rest it LIES, flat, on the ground the caller gives. 🔒 Red-proved: the lying test dropped ⇒ red.
    /// </summary>
    [Fact]
    public void RR4_ARoundFliesNoseFirst_StreaksBackAlongItsFlight_AndLiesOnTheGroundAtRest()
    {
        var world = new EntityRepository();
        var tkb = Hrot.Map.Common.HrotEnvironment.CreateTkb();
        world.SetSingletonManaged<ITkbDatabase>(tkb);
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<SimVelocity>();
        world.RegisterComponent<TkbIdentity>();
        world.RegisterComponent<LightweightRound>();
        long tracer = 0;
        foreach (var t in tkb.GetAll())
            if (MunitionTkbCatalog.VisualOf(tkb, t.TkbType) is { Tracer: true, Shape: MunitionShape.Bullet }) { tracer = t.TkbType; break; }
        Assert.NotEqual(0, tracer);
        var round = world.CreateEntity();
        world.AddComponent(round, new SimTransform { Position = new Vector3(100, 0, 20), Rotation = Quaternion.Identity });
        world.AddComponent(round, new SimVelocity { Linear = new Vector3(800, 0, 0) });
        world.AddComponent(round, new TkbIdentity { TkbType = (TkbTypeId)tracer });
        world.AddComponent(round, new LightweightRound());

        var look = new RoundLook();
        var list = new List<RoundState>();
        look.Resolve(world, tkb, groundHeight: null, list);
        var r = Assert.Single(list);
        Assert.False(r.Lying);
        Assert.Equal(Vector3.UnitX, r.Nose);
        Assert.True(RoundLook.TracerOf(in r, out var head, out var tail));
        Assert.Equal(800f * RoundLook.TracerSeconds, Vector3.Distance(head, tail), 2);

        world.SetComponent(round, new SimVelocity { Linear = Vector3.Zero });
        look.Resolve(world, tkb, (_, _) => 5f, list);
        r = Assert.Single(list);
        Assert.True(r.Lying);
        Assert.Equal(20f, r.Centre.Z);   // above the ground at 5 m: it rests where it is (max of its height and the ground)
        Assert.Equal(0f, r.Nose.Z);
        Assert.False(RoundLook.TracerOf(in r, out _, out _));
        Assert.Equal(1f, RoundLook.TrailFactor(5f, 4.0));
        Assert.Equal(0f, RoundLook.TrailFactor(5f, 5f + RoundLook.TrailLingerSeconds + 0.1));
    }

    /// <summary>RR-4 — a burning spot on a holder turned 90° sits in the holder's frame; its flames are 0.7 × its radius (0.3…8 m) with a small smoke; a spot
    /// whose burn is over is gone.</summary>
    [Fact]
    public void RR4_ABurningSpot_SitsInItsHoldersFrame_SizedByItsRadius()
    {
        var world = new EntityRepository();
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<FireState>();
        var fs = new FireState();
        Assert.True(fs.TryAdd(new FireSpot(2f, 0f, 1f, 4f, (SimInstant)10.0, 30f, 1f), 0.0));
        var holder = world.CreateEntity();
        world.AddComponent(holder, new SimTransform { Position = new Vector3(50, 50, 0), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f) });
        world.AddComponent(holder, fs);

        var flames = new FlameLook();
        var specs = new List<FlameSpec>();
        flames.Resolve(world, 20.0, specs);
        var f = Assert.Single(specs);
        Assert.Equal(FlameKind.Spot, f.Kind);
        Assert.Equal(50f, f.At.X, 3);
        Assert.Equal(52f, f.At.Y, 3);
        Assert.Equal(FlameLook.SpotSize(4f), f.Size);
        Assert.True(f.Smoke);
        FlameLook.SpotSmoke(in f, out float smoke, out _, out _);
        Assert.Equal(f.Size * FlameLook.SpotSmokeShare, smoke, 4);

        specs.Clear();
        flames.Resolve(world, 45.0, specs);
        Assert.Empty(specs);
    }
}
