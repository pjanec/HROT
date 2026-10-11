using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Time;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Core.Tkb;
using Hrot.Map.Common;
using Hrot.Map.Common.Components;
using Hrot.UI.Common.Effects;
using Xunit;

namespace Hrot.Presentation.Tests.Effects;

/// <summary>
/// ⭐ CE-1042 E1–E3 — realism effects (<c>docs/DESIGN_Visual_Effects.md</c>): effect types in the TKB with their look (VE-C), the ammo
/// → effect mapping with a calibre-class default (VE-D), the spawn and lifetime systems on simulation time (VE-B, VE-G, VE-H,
/// VE-J, VE-M), every map host scheduling them through the pack (VE-A, R-254), and the layer drawing them from the TKB (VE-F).
/// </summary>
public sealed class EffectTests
{
    private static (EntityRepository World, ITkbDatabase Tkb) World()
    {
        var world = new EntityRepository();
        var tkb = Hrot.Map.Common.HrotEnvironment.CreateTkb();
        world.SetSingletonManaged<ITkbDatabase>(tkb);
        PresentationComponentRegistry.RegisterEffects(world);
        world.RegisterEvent<DetonationNotification>();
        world.RegisterEvent<WeaponFireNotification>();
        return (world, tkb);
    }

    private static void Tick(EntityRepository world, Fdp.ModuleHost.Abstractions.IEcsModuleSystem system, float dt)
    {
        world.Bus.SwapBuffers();
        system.Execute(world, dt);
        ((EntityCommandBuffer)((ISimulationView)world).GetCommandBuffer()).Playback(world);
    }

    /// <summary>Sets the world's sim time — what <see cref="SimClock"/> reads (the kernel pushes it every frame).</summary>
    private static void At(EntityRepository world, double simSeconds)
        => world.SetSingletonUnmanaged(new GlobalTime { TotalTime = simSeconds });

    private static List<(long Type, Entity Entity)> Effects(EntityRepository world)
    {
        var list = new List<(long, Entity)>();
        foreach (var e in world.Query().With<Lifespan>().With<TkbIdentity>().Build())
            list.Add((world.GetComponentRO<TkbIdentity>(e).TkbType, e));
        return list;
    }

    private static Entity Unit(EntityRepository world, long type, Vector3 at)
    {
        var e = world.CreateEntity();
        world.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
        world.AddComponent(e, new TkbIdentity { TkbType = type });
        return e;
    }

    [Fact]
    public void E1_EveryEffectTypeIsInTheSharedTkb_WithItsLook_AndTheMunitionsNameTheirs()
    {
        var tkb = Hrot.Map.Common.HrotEnvironment.CreateTkb();
        Assert.Equal(12, EffectTkbCatalog.BuiltIn.Count);   // + W3's wreck fire and smoke
        foreach (var (type, (_, look)) in EffectTkbCatalog.BuiltIn)
        {
            Assert.True(tkb.TryGetByType(type, out var t) && t != null, $"effect type {type} missing from CreateTkb");
            Assert.Equal(look.Kind, t!.GetDescriptor<EffectVisualDto>()!.Kind);
            Assert.True(t.GetDescriptor<TkbMasterDto>()!.HideFromPalette, "an effect is not something anybody places");
        }
        Assert.Equal(EffectTkbCatalog.ExplosionLarge, EffectTkbCatalog.EffectsFor(tkb, MunitionTkbCatalog.Tkb81mmMortarHe, 0f).Explosion);
        Assert.Equal(0, EffectTkbCatalog.EffectsFor(tkb, MunitionTkbCatalog.TkbM67Grenade, 0f).MuzzleFlash);   // a thrown grenade: no flash
    }

    [Fact]
    public void E1_AnAmmoWithNoSet_GetsItsCalibreClassDefault_FromTheRoundsDamage()
    {
        // the built-in mounts: rifle 25 · 25 mm cannon 60 · RPG 400 · 125 mm gun 1100 · ATGM 2000
        Assert.Equal(EffectTkbCatalog.MuzzleFlashSmall, EffectTkbCatalog.EffectsFor(null, 0, 25f).MuzzleFlash);
        Assert.Equal(EffectTkbCatalog.ExplosionMedium, EffectTkbCatalog.EffectsFor(null, 0, 60f).Explosion);
        Assert.Equal(EffectTkbCatalog.DecalMedium, EffectTkbCatalog.EffectsFor(null, 0, 400f).Decal);
        Assert.Equal(EffectTkbCatalog.ExplosionLarge, EffectTkbCatalog.EffectsFor(null, 0, 1100f).Explosion);
        Assert.Equal(0, EffectTkbCatalog.EffectsFor(null, 0, 25f).Tracer);   // rifles: no tracer by default
        // a host without a TKB (the replay browser) still knows how every built-in effect looks
        Assert.Equal(EffectKind.Decal, EffectTkbCatalog.VisualOf(null, EffectTkbCatalog.DecalLarge)!.Kind);
    }

    [Fact]
    public void E2_AShot_MakesAFlashAttachedToTheWeapon_AndATracerTowardTheTarget()
    {
        var (world, _) = World();
        var tank = Unit(world, Hrot.Map.Common.TkbEntityTypes.Tank_T72, new Vector3(0, 0, 3));
        var target = Unit(world, Hrot.Map.Common.TkbEntityTypes.Tank_M1Abrams, new Vector3(800, 0, 5));
        world.Bus.Publish(new WeaponFireNotification { Shooter = tank, Target = target, WeaponIndex = 0 });
        var spawn = new EffectSpawnSystem();
        Tick(world, spawn, 0.016f);

        var fx = Effects(world);
        Assert.Contains(fx, f => f.Type == EffectTkbCatalog.MuzzleFlashLarge);   // the 125 mm gun is large calibre
        Assert.Contains(fx, f => f.Type == EffectTkbCatalog.Tracer);
        var flash = fx.Single(f => f.Type == EffectTkbCatalog.MuzzleFlashLarge).Entity;
        var anchor = world.GetComponentRO<EffectAnchor>(flash);
        Assert.Equal(tank, anchor.Shooter);                                      // VE-J: attached, no position of its own
        Assert.Equal(new Vector3(800, 0, 5), anchor.Toward);
        Assert.True(world.HasComponent<Fdp.Toolkit.Scenario.ScenarioIgnoreTag>(flash), "VE-R: an effect is never saved with the scenario");
        // and no map layer draws it as a body: an effect type has none (the body layer would show an unknown box)
        Assert.Equal(Hrot.UI.Common.Map3D.VisualFamily.Unit,
                     Hrot.UI.Common.Map3D.EntityBodyLayer3D.Resolve(Hrot.Map.Common.HrotEnvironment.CreateTkb().GetByType(EffectTkbCatalog.MuzzleFlashLarge)).Family);
        Assert.Equal(Hrot.UI.Common.Map3D.VisualFamily.Unit,
                     new Hrot.UI.Common.Map3D.EntityBodyLayer3D(() => null, () => null).LookOf(EffectTkbCatalog.DecalSmall, null).Family);
    }

    [Fact]
    public void E2_AHitOnTheGround_MakesAnExplosionAndADecalOnTheGround_AHitOnAVehicle_NoDecal()
    {
        var (world, _) = World();
        var tank = Unit(world, Hrot.Map.Common.TkbEntityTypes.Tank_T72, new Vector3(0, 0, 0));
        world.Bus.Publish(new DetonationNotification { Target = Entity.Null, HitX = 50, HitY = 20, HitZ = 0.4f, Damage = 1100f });
        world.Bus.Publish(new DetonationNotification { Target = tank, HitX = 0, HitY = 0, HitZ = 1.5f, Damage = 25f });
        Tick(world, new EffectSpawnSystem(), 0.016f);

        var fx = Effects(world);
        Assert.Contains(fx, f => f.Type == EffectTkbCatalog.ExplosionLarge);
        Assert.Contains(fx, f => f.Type == EffectTkbCatalog.ExplosionSmall);
        var decal = Assert.Single(fx, f => f.Type == EffectTkbCatalog.DecalLarge);   // VE-M: none for the hit on the tank
        Assert.DoesNotContain(fx, f => f.Type == EffectTkbCatalog.DecalSmall);
        Assert.Equal(0f, world.GetComponentRO<SimTransform>(decal.Entity).Position.Z); // on the ground under the hit (VE-I step 1)
    }

    [Fact]
    public void E2_EffectsAgeOnSimulationTime_ExpireAtTheirDuration_AndAPausedSimFreezesThem()
    {
        var (world, _) = World();
        At(world, 100.0);
        world.Bus.Publish(new DetonationNotification { Target = Entity.Null, HitX = 0, HitY = 0, Damage = 1100f });
        Tick(world, new EffectSpawnSystem(), 0.016f);
        foreach (var (_, e) in Effects(world))
            Assert.Equal(100.0, world.GetComponentRO<Lifespan>(e).BornAtSimSeconds);   // W1: made at sim now, no age stored
        var life = new LifespanSystem();
        int before = Effects(world).Count;
        for (int i = 0; i < 100; i++) Tick(world, life, 0.016f);   // paused: sim time does not move, whatever the frame delta
        Assert.Equal(before, Effects(world).Count);
        At(world, 102.0);                                            // past the explosion's 1.6 s, well inside the decal's 300 s
        Tick(world, life, 0f);
        Assert.Equal(new[] { EffectTkbCatalog.DecalLarge }, Effects(world).Select(f => f.Type).ToArray());
        Assert.Equal(1, life.Removed);
    }

    [Fact]
    public void E2_AFlashWhoseShooterIsGone_Ends_AndDecalsAreCappedOldestFirst()
    {
        var (world, _) = World();
        var tank = Unit(world, Hrot.Map.Common.TkbEntityTypes.Tank_T72, Vector3.Zero);
        world.Bus.Publish(new WeaponFireNotification { Shooter = tank, Target = Entity.Null, WeaponIndex = 0 });
        var spawn = new EffectSpawnSystem();
        Tick(world, spawn, 0.016f);
        Assert.Single(Effects(world));
        world.DestroyEntity(tank);
        Tick(world, spawn, 0.001f);   // the spawner keeps its own population honest (VE-J)
        Assert.Empty(Effects(world));

        At(world, 10_000.0);
        for (int i = 0; i < EffectSpawnSystem.DecalCap + 10; i++)
        {
            var d = world.CreateEntity();
            world.AddComponent(d, new SimTransform());
            world.AddComponent(d, new TkbIdentity { TkbType = EffectTkbCatalog.DecalSmall });
            world.AddComponent(d, Lifespan.Starting(i, 100_000f));   // decal i was made at sim second i
        }
        Tick(world, spawn, 0.001f);
        var born = Effects(world).Select(f => world.GetComponentRO<Lifespan>(f.Entity).BornAtSimSeconds).ToList();
        Assert.Equal(EffectSpawnSystem.DecalCap, born.Count);
        Assert.True(born.Min() >= 10, "the OLDEST ten went");
    }

    [Fact]
    public void E2_EveryMapHostSchedulesTheEffects_ThroughThePacksInteractionSystems()
    {
        var mi = Hrot.ScenarioEditor.Map.MapInteractionPack.Build(new Hrot.ScenarioEditor.Map.MapInteractionContext { World = new EntityRepository() });
        Assert.Contains(mi.EffectSpawn, mi.InteractionSystems);
        Assert.Contains(mi.Lifespan, mi.InteractionSystems);
        Assert.Contains(typeof(EffectSpawnSystem), mi.RequiredSystems);
        Assert.Contains(typeof(LifespanSystem), mi.RequiredSystems);
        Assert.Empty(mi.Unserviceable(mi.InteractionSystems));
        // and a host that drives the systems itself but forgets the effects is REPORTED
        Assert.Contains(mi.Unserviceable(new object[] { mi.GlobalManager, mi.DataDrivenSystem, mi.StatelessSystem, mi.ActionDispatch }),
                        m => m.Contains(nameof(EffectSpawnSystem)));
    }

    /// <summary>⭐ W2 (VE-N, VE-O) — ONE "destroyed" test (Health ≤ 0 on an entity that still exists); a wreck draws near-black on
    /// every part role; the 2-D X spans the shape's footprint corner to corner, turned with the unit.</summary>
    [Fact]
    public void W2_DestroyedIsHealthAtZero_TheWreckIsNearBlack_AndTheXCrossesTheFootprint()
    {
        var (world, _) = World();
        world.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
        var tank = Unit(world, Hrot.Map.Common.TkbEntityTypes.Tank_T72, Vector3.Zero);
        var marker = Unit(world, Hrot.Map.Common.TkbEntityTypes.Tank_T72, Vector3.Zero);   // no Health: never destroyed
        world.AddComponent(tank, new Fdp.Toolkit.Combat.Components.Health { Current = 10f, Max = 100f });
        Assert.False(Fdp.Toolkit.Combat.CombatLife.IsDestroyed(world, tank));
        world.SetComponent(tank, new Fdp.Toolkit.Combat.Components.Health { Current = 0f, Max = 100f });
        Assert.True(Fdp.Toolkit.Combat.CombatLife.IsDestroyed(world, tank));
        Assert.False(Fdp.Toolkit.Combat.CombatLife.IsDestroyed(world, marker));
        world.DestroyEntity(tank);
        Assert.False(Fdp.Toolkit.Combat.CombatLife.IsDestroyed(world, tank));   // deleted is gone, not a wreck

        var olive = new Raylib_cs.Color(112, 120, 82, 255);
        foreach (var role in System.Enum.GetValues<Hrot.UI.Common.Map3D.ColourRole>())
        {
            var c = Hrot.UI.Common.Map3D.EntityBodyLayer3D.PartColour(role, olive, wreck: true);
            Assert.True(c.R < 40 && c.G < 40 && c.B < 40, $"{role} on a wreck: {c.R},{c.G},{c.B}");
        }
        Assert.Equal(olive, Hrot.UI.Common.Map3D.EntityBodyLayer3D.PartColour(Hrot.UI.Common.Map3D.ColourRole.Body, olive, wreck: false));

        // a 10 m × 4 m hull turned 90°: the X's strokes run corner to corner along the turned footprint
        var (a, b, c2, d) = GizmoMap.Presentation.Shapes.PerspectiveShapeRenderer.DestroyedCross(
            new System.Numerics.Vector2(100, 50), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, System.MathF.PI / 2f), 10f, 4f);
        Assert.Equal(100f + 2f, a.X, 3); Assert.Equal(50f - 5f, a.Y, 3);
        Assert.Equal(100f - 2f, b.X, 3); Assert.Equal(50f + 5f, b.Y, 3);
        Assert.Equal(new System.Numerics.Vector2(100, 50), (a + b) / 2f);
        Assert.Equal(new System.Numerics.Vector2(100, 50), (c2 + d) / 2f);
    }

    /// <summary>⭐ W3 (VE-P) — only a CROSSING to zero burns: a vehicle killed now gets flames (60 s) and smoke (180 s) attached to its
    /// body; a soldier does not burn; a wreck first seen already dead does not; in a replay nothing is made (the recording has it).</summary>
    [Fact]
    public void W3_AVehicleKilledNowBurnsAndSmokes_ASoldierOrAWreckLoadedDeadDoesNot_AndAReplayMakesNothing()
    {
        var (world, _) = World();
        world.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
        At(world, 20.0);
        Entity Living(long type, float hp)
        {
            var e = Unit(world, type, new Vector3(10, 10, 0));
            world.AddComponent(e, new Fdp.Toolkit.Combat.Components.Health { Current = hp, Max = 100f });
            return e;
        }
        var tank = Living(Hrot.Map.Common.TkbEntityTypes.Tank_T72, 100f);
        var soldier = Living(Hrot.Map.Common.TkbEntityTypes.Infantry_Rifleman, 100f);
        var loadedWreck = Living(Hrot.Map.Common.TkbEntityTypes.Tank_M1Abrams, 0f);
        var wrecks = new WreckSpawnSystem();
        Tick(world, wrecks, 0.016f);
        Assert.Empty(Effects(world));                                   // the loaded wreck is already dead: no crossing

        world.SetComponent(tank, new Fdp.Toolkit.Combat.Components.Health { Current = 0f, Max = 100f });
        world.SetComponent(soldier, new Fdp.Toolkit.Combat.Components.Health { Current = 0f, Max = 100f });
        Tick(world, wrecks, 0.016f);
        var fx = Effects(world);
        Assert.Equal(new[] { EffectTkbCatalog.Fire, EffectTkbCatalog.Smoke }, fx.Select(f => f.Type).OrderBy(t => t).ToArray());
        foreach (var (type, e) in fx)
        {
            var anchor = world.GetComponentRO<EffectAnchor>(e);
            Assert.Equal(tank, anchor.Shooter);
            Assert.Equal(EffectAnchorKind.Body, anchor.Kind);
            Assert.Equal(type == EffectTkbCatalog.Fire ? 60f : 180f, world.GetComponentRO<Lifespan>(e).Seconds);
            Assert.Equal(20.0, world.GetComponentRO<Lifespan>(e).BornAtSimSeconds);
        }
        Tick(world, wrecks, 0.016f);
        Assert.Equal(2, Effects(world).Count);                         // a wreck burns ONCE

        var mi = Hrot.ScenarioEditor.Map.MapInteractionPack.Build(new Hrot.ScenarioEditor.Map.MapInteractionContext
        {
            World = world, IsReplayActive = static () => true,
        });
        var other = Living(Hrot.Map.Common.TkbEntityTypes.Tank_T72, 100f);
        Tick(world, mi.WreckSpawn, 0.016f);
        world.SetComponent(other, new Fdp.Toolkit.Combat.Components.Health { Current = 0f, Max = 100f });
        Tick(world, mi.WreckSpawn, 0.016f);
        Assert.Equal(2, Effects(world).Count);                         // a replayed kill makes nothing
        Assert.Contains(mi.WreckSpawn, mi.InteractionSystems);
        Assert.Contains(typeof(WreckSpawnSystem), mi.RequiredSystems);
    }

    /// <summary>⭐ W3 (VE-P) — the flames sit on the wreck's TOP and scale with it; the particles are STATELESS (the same seed and age
    /// give the same puffs, so a jump or another frame cannot disagree); the fire dies down at the end; the smoke rises and fades.</summary>
    [Fact]
    public void W3_FlamesSitOnTheWreck_AndTheirPuffsComeFromSeedAndAgeAlone()
    {
        var (world, tkb) = World();
        world.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
        At(world, 0.0);
        var tank = Unit(world, Hrot.Map.Common.TkbEntityTypes.Tank_T72, new Vector3(50, 0, 0));
        world.AddComponent(tank, new Fdp.Toolkit.Combat.Components.Health { Current = 100f, Max = 100f });
        var wrecks = new WreckSpawnSystem();
        Tick(world, wrecks, 0f);
        world.SetComponent(tank, new Fdp.Toolkit.Combat.Components.Health { Current = 0f, Max = 100f });
        Tick(world, wrecks, 0f);
        var bodies = new Hrot.UI.Common.Map3D.EntityBodyLayer3D(() => world, () => tkb);
        var layer = new EffectLayer(() => world, () => tkb, bodies);
        Assert.True(bodies.TryGetBox(world, tank, tkb, out var centre, out _, out var half));

        At(world, 10.0);
        var fire = Assert.Single(layer.Resolve(), r => r.Look.Kind == EffectKind.Fire);
        Assert.Equal(centre.Z + half.Z, fire.At.Z, 3);                 // on top of the drawn hull
        Assert.Equal(50f, fire.At.X, 3);
        Assert.Equal(10f, fire.Age, 3);
        var puffs = EffectLayer.Puffs(fire);
        Assert.Equal(EffectLayer.FirePuffs, puffs.Count);
        Assert.Equal(puffs, EffectLayer.Puffs(fire));                  // stateless: same input, same puffs
        Assert.NotEqual(puffs, EffectLayer.Puffs(fire with { Age = 10.3f }));
        Assert.All(puffs, p => Assert.True(p.At.Z >= fire.At.Z - 0.01f, "flames rise off the top"));

        var dying = EffectLayer.Puffs(fire with { Phase = 0.99f });
        Assert.True(dying.Sum(p => (float)p.Colour.A) < puffs.Sum(p => (float)p.Colour.A) * 0.2f, "the fire dies down at the end");

        var smoke = Assert.Single(layer.Resolve(), r => r.Look.Kind == EffectKind.Smoke);
        var column = EffectLayer.Puffs(smoke);
        Assert.Equal(EffectLayer.SmokePuffs, column.Count);
        Assert.True(column.Max(p => p.At.Z) > smoke.At.Z + smoke.Look.Size * 2f, "the column rises well above the wreck");
        Assert.Contains(layer.Resolve(), r => r.Look.Kind == EffectKind.Fire);   // still burning at 10 s

        At(world, 61.0);                                               // the fire's 60 s are up; the smoke's 180 s are not
        Tick(world, new LifespanSystem(), 0f);
        Assert.DoesNotContain(layer.Resolve(), r => r.Look.Kind == EffectKind.Fire);
        Assert.Contains(layer.Resolve(), r => r.Look.Kind == EffectKind.Smoke);
    }

    private static Entity Round(EntityRepository world, long ammo, Vector3 at, Vector3 velocity, byte warhead = 0, float fuze = 0f,
                                Vector3 muzzle = default)
    {
        var e = world.CreateEntity();
        world.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
        world.AddComponent(e, new SimVelocity { Linear = velocity });
        world.AddComponent(e, new Fdp.Toolkit.Combat.Components.BallisticProjectile
        {
            Ammo = ammo, Warhead = warhead, FuzeRemaining = fuze, PreviousPosition = at - velocity * 0.016f, Muzzle = muzzle,
        });
        return e;
    }

    private static EntityRepository RoundsWorld(ITkbDatabase tkb)
    {
        var world = new EntityRepository();
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<SimVelocity>();
        world.RegisterComponent<Fdp.Toolkit.Combat.Components.BallisticProjectile>();
        world.SetSingletonManaged<ITkbDatabase>(tkb);
        return world;
    }

    /// <summary>⭐ P1 (docs/DESIGN_Visual_Effects.md §8 PR-A/PR-B) — a round's body comes from its MUNITION type: a thrown grenade
    /// flies (tumbling) and then LIES flat on the ground with its fuze counting; a mortar bomb points its nose down its arc; a bullet
    /// (no munition look) and a burst round draw no body.</summary>
    [Fact]
    public void P1_AGrenadeFliesThenLiesOnTheGround_ABombPointsDownItsArc_ABulletDrawsNoBody()
    {
        var tkb = Hrot.Map.Common.HrotEnvironment.CreateTkb();
        var world = RoundsWorld(tkb);
        var W = Fdp.Toolkit.Combat.Components.WarheadRound.Arc | Fdp.Toolkit.Combat.Components.WarheadRound.TimeFuze
              | Fdp.Toolkit.Combat.Components.WarheadRound.Area;
        Round(world, MunitionTkbCatalog.TkbM67Grenade, new Vector3(0, 0, 3), new Vector3(8, 0, 2), (byte)W, 3.5f);
        Round(world, MunitionTkbCatalog.TkbM67Grenade, new Vector3(20, 5, 0.4f), Vector3.Zero,
              (byte)(W | Fdp.Toolkit.Combat.Components.WarheadRound.Landed), 2.7f);
        Round(world, MunitionTkbCatalog.Tkb81mmMortarHe, new Vector3(50, 0, 80), new Vector3(30, 0, -40));
        Round(world, 0, new Vector3(5, 5, 1), new Vector3(800, 0, 0));                                       // a bullet
        Round(world, MunitionTkbCatalog.TkbM67Grenade, new Vector3(9, 9, 0), Vector3.Zero, (byte)(W | Fdp.Toolkit.Combat.Components.WarheadRound.Spent));
        var layer = new ProjectileLayer(() => world, () => tkb) { GroundHeight = (_, _) => 0.5f };

        var rounds = layer.Resolve();
        Assert.Equal(3, rounds.Count);
        var flying = Assert.Single(rounds, r => r.Look.Shape == MunitionShape.Grenade && !r.Lying);
        var lying = Assert.Single(rounds, r => r.Lying);
        var bomb = Assert.Single(rounds, r => r.Look.Shape == MunitionShape.Bomb);
        Assert.Equal(2.7f, lying.FuzeLeft);
        Assert.Equal(0.5f, lying.Centre.Z);                                     // up on the ground, not under it
        Assert.Equal(0f, lying.Nose.Z, 3);                                      // flat
        Assert.True(ProjectileLayer.SolidsOf(lying).Min(s => s.From.Z - s.RadiusFrom) >= 0.5f - 1e-3f, "it rests ON the ground");
        Assert.Equal(1f, flying.Nose.Length(), 3);
        Assert.Equal(Vector3.Normalize(new Vector3(30, 0, -40)), bomb.Nose);     // nose down its arc
        var noseCone = ProjectileLayer.SolidsOf(bomb)[0];
        Assert.True(Vector3.Dot(noseCone.To - bomb.Centre, bomb.Nose) > 0f, "the bomb's nose leads");

        // the grenade tumbles: its axis turns with sim time (stateless)
        world.SetSingletonUnmanaged(new GlobalTime { TotalTime = 0.1 });
        var a = layer.Resolve().Single(r => r.Look.Shape == MunitionShape.Grenade && !r.Lying).Nose;
        world.SetSingletonUnmanaged(new GlobalTime { TotalTime = 0.2 });
        var b = layer.Resolve().Single(r => r.Look.Shape == MunitionShape.Grenade && !r.Lying).Nose;
        Assert.True(Vector3.Dot(a, b) < 0.99f, "the grenade tumbles");
        world.SetSingletonUnmanaged(new GlobalTime { TotalTime = 0.1 });
        Assert.Equal(a, layer.Resolve().Single(r => r.Look.Shape == MunitionShape.Grenade && !r.Lying).Nose);   // same time, same pose
    }

    /// <summary>⭐ P1 (§8 PR-B) — a munition with a smoke trail (a rocket, a missile) trails puffs back toward its muzzle, thinning;
    /// a grenade does not.</summary>
    [Fact]
    public void P1_ARocketTrailsSmokeBackToItsMuzzle()
    {
        var look = new MunitionVisualDto { Shape = MunitionShape.Missile, Length = 1.2f, Diameter = 0.12f, SmokeTrail = true };
        var r = new ProjectileLayer.Round(look, new Vector3(40, 0, 2), Vector3.UnitX, false, 0f, new Vector3(20, 0, 2), 1);
        var trail = ProjectileLayer.TrailOf(r);
        Assert.Equal(22, trail.Count);                                          // 20 m back, one puff each 0.9 m
        Assert.All(trail, p => Assert.InRange(p.At.X, 20f, 40f));             // between the missile and its muzzle
        Assert.True(trail[^1].Colour.A < trail[0].Colour.A, "older puffs are thinner");
        Assert.Empty(ProjectileLayer.TrailOf(r with { Look = look with { SmokeTrail = false } }));
        Assert.Equal(4, ProjectileLayer.SolidsOf(r).Count);                     // nose, body, mid wings, tail fins
    }

    /// <summary>⭐ P1 (§8 PR-C) — the 2-D projector: a bullet keeps its streak only; a grenade adds a marker; a LYING grenade adds its
    /// fuze seconds — before, a lying grenade's streak had zero length and it was invisible.</summary>
    [Fact]
    public void P1_The2DMark_ALyingGrenadeShowsItsFuzeSeconds_ABulletOnlyItsStreak()
    {
        var tkb = Hrot.Map.Common.HrotEnvironment.CreateTkb();
        var world = RoundsWorld(tkb);
        var W = Fdp.Toolkit.Combat.Components.WarheadRound.Arc | Fdp.Toolkit.Combat.Components.WarheadRound.TimeFuze;
        var lying = Round(world, MunitionTkbCatalog.TkbM67Grenade, new Vector3(20, 5, 0), Vector3.Zero,
                          (byte)(W | Fdp.Toolkit.Combat.Components.WarheadRound.Landed), 2.74f);
        var bullet = Round(world, 0, new Vector3(5, 5, 1), new Vector3(800, 0, 0));
        var gizmo = new Hrot.ScenarioEditor.Gizmos.ProjectilePresentationGizmo();

        var buffer = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer();
        gizmo.Draw(world, bullet, buffer);
        Assert.Single(buffer.GetFrame().ToArray());                              // the streak

        buffer = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer();
        gizmo.Draw(world, lying, buffer);
        var frame = buffer.GetFrame().ToArray();
        Assert.Equal(3, frame.Length);                                           // streak + marker + fuze text
        Assert.Contains(frame, p => p.Shape == Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveShape.Sphere);
        Assert.Contains(frame, p => p.Shape == Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveShape.Text);
        Assert.Equal("3 s", Hrot.ScenarioEditor.Gizmos.ProjectilePresentationGizmo.FuzeLabel(2.74f));   // a countdown rounds up
    }

    /// <summary>⭐ W1 (VE-Q) — ONE generic timer: <see cref="Lifespan"/> on sim time; a jump BACK to before an entity was made removes
    /// it too (it did not exist then); the layer's phase and fade come from the sim clock.</summary>
    [Fact]
    public void W1_Lifespan_IsSimTime_AJumpBackRemovesWhatDidNotExistYet_AndTheLayerPhaseFollowsTheClock()
    {
        var span = Lifespan.Starting(50.0, 4f);
        Assert.Equal(0.5f, span.Phase(52.0));
        Assert.Equal(1f, span.Remaining(53.0));
        Assert.False(span.IsOver(53.9));
        Assert.True(span.IsOver(54.0));
        Assert.True(span.IsOver(49.0), "a jump back to before it was made");

        var (world, tkb) = World();
        At(world, 50.0);
        world.Bus.Publish(new DetonationNotification { Target = Entity.Null, HitX = 0, HitY = 0, Damage = 1100f });
        Tick(world, new EffectSpawnSystem(), 0.016f);
        var layer = new EffectLayer(() => world, () => tkb, new Hrot.UI.Common.Map3D.EntityBodyLayer3D(() => world, () => tkb));
        At(world, 50.8);   // half the large explosion's 1.6 s
        var burst = Assert.Single(layer.Resolve(), x => x.Look.Kind == EffectKind.Explosion);
        Assert.Equal(0.5f, burst.Phase, 3);

        At(world, 10.0);   // a rewind to before the shot
        Tick(world, new LifespanSystem(), 0f);
        Assert.Empty(Effects(world));
    }

    /// <summary>⭐ W1 (VE-R, U8) — effects are RECORDED and never saved: neither component is <c>NoReplay</c>, both are
    /// <c>NoScenario</c>; and in a replay the spawner and the collector stand down — the recording holds the effects.</summary>
    [Fact]
    public void W1_EffectsAreRecordedNeverSaved_AndInAReplayTheSpawnerAndTheCollectorStandDown()
    {
        foreach (var type in new[] { typeof(Lifespan), typeof(EffectAnchor) })
        {
            var policy = ((DataPolicyAttribute)System.Attribute.GetCustomAttribute(type, typeof(DataPolicyAttribute))!).Policy;
            Assert.False(policy.HasFlag(DataPolicy.NoReplay), $"{type.Name} must be recorded");
            Assert.True(policy.HasFlag(DataPolicy.NoScenario), $"{type.Name} is never saved");
        }

        var (world, _) = World();
        bool replaying = true;
        var mi = Hrot.ScenarioEditor.Map.MapInteractionPack.Build(new Hrot.ScenarioEditor.Map.MapInteractionContext
        {
            World = world, IsReplayActive = () => replaying,
        });
        At(world, 5.0);
        world.Bus.Publish(new DetonationNotification { Target = Entity.Null, HitX = 0, HitY = 0, Damage = 1100f });
        Tick(world, mi.EffectSpawn, 0.016f);
        Assert.Empty(Effects(world));                       // a replayed hit makes nothing: the recording already has its effects

        var recorded = world.CreateEntity();                // an effect the replay restored, its time long past
        world.AddComponent(recorded, new SimTransform());
        world.AddComponent(recorded, new TkbIdentity { TkbType = EffectTkbCatalog.ExplosionLarge });
        world.AddComponent(recorded, Lifespan.Starting(1.0, 1f));
        Tick(world, mi.Lifespan, 0.016f);
        Assert.Single(Effects(world));                      // the replay removes it when the recording does, not the collector

        replaying = false;                                  // back to live: the collector runs again
        Tick(world, mi.Lifespan, 0.016f);
        Assert.Empty(Effects(world));
    }

    [Fact]
    public void E3_TheLayerResolvesEachEffectFromItsTkbType_AFlashAtTheMuzzle_ATracerStreakingAlongItsPath()
    {
        var (world, tkb) = World();
        var tank = Unit(world, Hrot.Map.Common.TkbEntityTypes.Tank_T72, new Vector3(0, 0, 0));
        var target = Unit(world, Hrot.Map.Common.TkbEntityTypes.Tank_M1Abrams, new Vector3(300, 0, 0));
        world.Bus.Publish(new WeaponFireNotification { Shooter = tank, Target = target, WeaponIndex = 0 });
        Tick(world, new EffectSpawnSystem(), 0.016f);
        var bodies = new Hrot.UI.Common.Map3D.EntityBodyLayer3D(() => world, () => tkb);
        var layer = new EffectLayer(() => world, () => tkb, bodies);

        var r = layer.Resolve();
        var flash = Assert.Single(r, x => x.Look.Kind == EffectKind.MuzzleFlash);
        Assert.True(flash.At.X > 2f && flash.At.Z > 0.5f, $"the flash is at the hull's front, up: {flash.At}");
        Assert.Equal(EffectTkbCatalog.BuiltIn[EffectTkbCatalog.MuzzleFlashLarge].Visual.Size, flash.Look.Size);
        var tracer = Assert.Single(r, x => x.Look.Kind == EffectKind.Tracer);
        Assert.True(EffectLayer.TracerSpan(tracer with { Phase = 0.5f }, out var a, out var b));
        Assert.True(a.X > flash.At.X && b.X < 300f && b.X > a.X, $"mid-flight streak {a} → {b}");
    }

    [Fact]
    public void E3_AttachMapLayersAttachesTheEffectLayer_OnEveryHost()
    {
        string pack = File.ReadAllText(Path.Combine(RepoRoot(), "Hrot/Engine/Hrot.Presentation/ScenarioEditor/Map/MapInteractionPack.cs"));
        Assert.Contains("canvas.AddLayer(effects);", pack);
        Assert.False(File.Exists(Path.Combine(RepoRoot(), "Hrot/Engine/Hrot.Presentation/Gizmos/EffectPresentationGizmo.cs")),
                     "VE-F: the old effect gizmo is retired — the layer draws the effects");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "HROT.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
