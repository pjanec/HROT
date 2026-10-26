using System.Linq;
using System.Collections.Generic;
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
        world.RegisterEvent<Fdp.Toolkit.Combat.StrikeNotification>();   // ⭐ CE-1171
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

    /// <summary>
    /// ⭐ CE-1165 (civilian U-4, docs/DESIGN_Civilians_And_Crowd_Control.md §8.8 U4-a′, U4-e) — a gas cloud (backend's replicated
    /// <c>AreaEffectCloud</c>, no <c>Lifespan</c>) is drawn where it IS now (it drifts), as dense as it looks now (dense for two thirds of
    /// its life, then thinning), and not once its time is up; its 3-D puffs all lie inside its cylinder and are the same on every call.
    /// </summary>
    [Fact]
    public void CE1165_ACloud_IsDrawnWhereItIs_AsDenseAsItLooks_UntilItsTimeIsUp()
    {
        var (world, tkb) = World();
        if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.AreaEffectCloud>()) world.RegisterComponent<Fdp.Toolkit.Combat.AreaEffectCloud>();
        var gas = Unit(world, AreaEffectCloudTkb.Type, new Vector3(10f, 20f, 2f));
        world.AddComponent(gas, new Fdp.Toolkit.Combat.AreaEffectCloud
        {
            Kind = Fdp.Toolkit.Combat.CloudKind.TearGas, Radius = 6f, Height = 3f, SightTransmittance = 0.4f, BornAtSimSeconds = 100.0, Seconds = 60f,
        });
        var layer = new EffectLayer(() => world, () => tkb);

        At(world, 100.0);
        var c = Assert.Single(layer.ResolveClouds());
        Assert.Equal((new Vector3(10f, 20f, 2f), 6f, 3f, Fdp.Toolkit.Combat.CloudKind.TearGas), (c.Centre, c.Radius, c.Height, c.Kind));
        Assert.Equal(0.6f, c.Density, 3);
        Assert.Empty(layer.Resolve());   // not a Lifespan effect

        world.GetComponentRW<SimTransform>(gas).Position = new Vector3(13f, 21f, 2f);   // its owner drifted it on the wind
        At(world, 130.0);
        c = Assert.Single(layer.ResolveClouds());
        Assert.Equal((new Vector3(13f, 21f, 2f), 0.6f), (c.Centre, MathF.Round(c.Density, 3)));

        At(world, 155.0);   // in its last third: thinning
        float thin = Assert.Single(layer.ResolveClouds()).Density;
        Assert.InRange(thin, 0.05f, 0.3f);

        var puffs = EffectLayer.CloudPuffs(c);
        Assert.InRange(puffs.Count, 8, EffectLayer.MaxCloudPuffs);
        Assert.Equal(puffs, EffectLayer.CloudPuffs(c));
        foreach (var p in puffs)
        {
            Assert.True(Vector2.Distance(new Vector2(p.At.X, p.At.Y), new Vector2(c.Centre.X, c.Centre.Y)) + p.Radius <= c.Radius + 1e-3f);
            Assert.True(p.At.Z - p.Radius >= c.Centre.Z - 1e-3f && p.At.Z + p.Radius <= c.Centre.Z + c.Height + 1e-3f);
        }
        Assert.NotEqual(EffectLayer.CloudColour(Fdp.Toolkit.Combat.CloudKind.TearGas), EffectLayer.CloudColour(Fdp.Toolkit.Combat.CloudKind.Smoke));

        At(world, 160.5);
        Assert.Empty(layer.ResolveClouds());
    }

    [Fact]
    public void E1_EveryEffectTypeIsInTheSharedTkb_WithItsLook_AndTheMunitionsNameTheirs()
    {
        var tkb = Hrot.Map.Common.HrotEnvironment.CreateTkb();
        Assert.Equal(17, EffectTkbCatalog.BuiltIn.Count);   // + W3's wreck fire and smoke + ⭐ CE-1090's backblast and gun blast + ⭐ CE-1115's flare and chaff cloud + ⭐ CE-1231's huge burst
        foreach (var (type, (_, look)) in EffectTkbCatalog.BuiltIn)
        {
            Assert.True(tkb.TryGetByType(type, out var t) && t != null, $"effect type {type} missing from CreateTkb");
            Assert.Equal(look.Kind, t!.GetDescriptor<EffectVisualDto>()!.Kind);
            Assert.True(t.GetDescriptor<TkbMasterDto>()!.HideFromPalette, "an effect is not something anybody places");
        }
        Assert.Equal(EffectTkbCatalog.ExplosionLarge, EffectTkbCatalog.EffectsFor(tkb, MunitionTkbCatalog.Tkb81mmMortarHe, 0f).Explosion);
        Assert.Equal(0, EffectTkbCatalog.EffectsFor(tkb, MunitionTkbCatalog.TkbM67Grenade, 0f).MuzzleFlash);   // a thrown grenade: no flash
    }

    /// <summary>
    /// ⭐ CE-1231 (docs/DESIGN_Visual_Effects.md §11 CH-1, R-1016) — a burst that comes with NO damage (the charge; every off-target burst and
    /// every burst from another node carry none) is sized by its warhead's explosive mass: the IED / vest (8 kg), a 155 mm shell (6.6 kg) and
    /// the Mk-82 (94 kg) burst HUGE and leave the crater; a 120 mm mortar bomb (3 kg) large, a 60 mm one (0.4 kg) medium. Its flash stays its
    /// damage class's; a round with damage, or with no warhead, is unchanged; a munition that names its set keeps it. 🔒 User: <i>"do we have
    /// any vsualization of ICD explosion? I ahevnt seen any. Also nothing for suicide bomber explosion."</i> 🔴 Red-proof: all of these drew the
    /// 0.8 m dust puff and a bullet mark.
    /// </summary>
    [Fact]
    public void CE1231_ABurstWithNoDamage_IsSizedByItsExplosive()
    {
        var tkb = Hrot.Map.Common.HrotEnvironment.CreateTkb();
        var charge = EffectTkbCatalog.EffectsFor(tkb, MunitionTkbCatalog.TkbExplosiveCharge, 0f);
        Assert.Equal(EffectTkbCatalog.ExplosionHuge, charge.Explosion);
        Assert.Equal(EffectTkbCatalog.DecalLarge, charge.Decal);   // the crater
        Assert.Equal(EffectTkbCatalog.MuzzleFlashSmall, charge.MuzzleFlash);   // its flash is untouched (its damage class)
        Assert.Equal(EffectTkbCatalog.ExplosionHuge, EffectTkbCatalog.EffectsFor(tkb, MunitionTkbCatalog.Tkb155mmHowitzerHe, 0f).Explosion);
        Assert.Equal(EffectTkbCatalog.ExplosionHuge, EffectTkbCatalog.EffectsFor(tkb, MunitionTkbCatalog.TkbMk82Bomb, 0f).Explosion);
        Assert.Equal(EffectTkbCatalog.ExplosionLarge, EffectTkbCatalog.EffectsFor(tkb, MunitionTkbCatalog.Tkb120mmMortarHe, 0f).Explosion);
        Assert.Equal(EffectTkbCatalog.ExplosionMedium, EffectTkbCatalog.EffectsFor(tkb, MunitionTkbCatalog.Tkb60mmMortarHe, 0f).Explosion);
        Assert.Equal(EffectTkbCatalog.ExplosionMedium, EffectTkbCatalog.EffectsFor(tkb, MunitionTkbCatalog.TkbM67Grenade, 0f).Explosion);   // its own set
        Assert.Equal(EffectTkbCatalog.ExplosionSmall, EffectTkbCatalog.EffectsFor(tkb, MunitionTkbCatalog.TkbExplosiveCharge, 25f).Explosion);   // a damage decides
        Assert.Equal(EffectTkbCatalog.ExplosionSmall, EffectTkbCatalog.EffectsFor(tkb, 0, 0f).Explosion);   // nothing known: the puff

        // the classes agree with the damage classes for a round that hits and the same round that misses
        Assert.Equal(EffectTkbCatalog.CalibreClass.Medium, EffectTkbCatalog.ClassOfExplosive(0.03f));   // 25 mm HEI — damage 60, medium
        Assert.Equal(EffectTkbCatalog.CalibreClass.Medium, EffectTkbCatalog.ClassOfExplosive(0.73f));   // PG-7V — damage 400, medium
        Assert.Equal(EffectTkbCatalog.CalibreClass.Large, EffectTkbCatalog.ClassOfExplosive(1.6f));     // 120 mm HEAT — damage 1200, large
        Assert.Equal(EffectTkbCatalog.CalibreClass.Huge, EffectTkbCatalog.ClassOfExplosive(8f));

        // how it looks: bigger and longer than the large burst
        var huge = EffectTkbCatalog.VisualOf(tkb, EffectTkbCatalog.ExplosionHuge)!;
        var large = EffectTkbCatalog.VisualOf(tkb, EffectTkbCatalog.ExplosionLarge)!;
        Assert.Equal(EffectKind.Explosion, huge.Kind);
        Assert.True(huge.Size > large.Size && huge.Duration > large.Duration, $"huge {huge.Size} m {huge.Duration} s");
        // an orange core inside a grey-brown cloud (the lean): only the huge burst raises the dust, which outlasts its core's first half
        Assert.True(EffectLayer.RaisesDust(huge));
        Assert.False(EffectLayer.RaisesDust(large));
        Assert.True(EffectLayer.Dust(huge.Size, 0.4f).Alpha > 0f && EffectLayer.Dust(huge.Size, 1f).Alpha == 0f);
        Assert.True(EffectLayer.Dust(huge.Size, 0.5f).Radius > huge.Size / 2f * EffectLayer.CoreShare, "the cloud is wider than the core");
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
    public void CE1171_ABlowStruck_IsKeptForTheBodies_WithWhatItWasStruck_AndMakesNoEffect()
    {
        var (world, _) = World();
        At(world, 10.0);
        var man = Unit(world, Hrot.Map.Common.TkbEntityTypes.Infantry_Rifleman, new Vector3(0, 0, 0));
        world.Bus.Publish(new Fdp.Toolkit.Combat.StrikeNotification { Striker = man, Weapon = Fdp.Toolkit.Combat.MeleeWeapon.MeleeKnife, IsRemote = true });
        Tick(world, new EffectSpawnSystem(), 0.016f);
        Assert.True(StrikeTracker.Of(world)!.StruckWithin(man, 10.0, 0.1, out var with, out double at), "the spawner keeps the blow (a remote one too)");
        Assert.Equal(Fdp.Toolkit.Combat.MeleeWeapon.MeleeKnife, with);
        Assert.Equal(10.0, at);
        Assert.Empty(Effects(world));   // a blow is no shot: no flash, no blast
    }

    /// <summary>⭐ CE-1215 (docs/DESIGN_Entity_Control.md §8j H-1, H-2) — the spawner keeps every hit that names a target (a round's,
    /// with its travel and damage; a blow's, at the one struck) and not a burst on the ground; the arc's place on the screen is the bearing
    /// toward where the hit came from, relative to the camera (ahead −90, right 0, behind 90 — raylib's ring degrees).</summary>
    [Fact]
    public void CE1215_AHitOnSomeone_IsKeptForTheHud_AndItsArcPointsWhereItCameFrom()
    {
        var (world, _) = World();
        At(world, 10.0);
        var shooter = Unit(world, Hrot.Map.Common.TkbEntityTypes.Infantry_Rifleman, new Vector3(0, 0, 0));
        var victim = Unit(world, Hrot.Map.Common.TkbEntityTypes.Infantry_Rifleman, new Vector3(30, 0, 0));
        world.Bus.Publish(new DetonationNotification { Shooter = shooter, Target = victim, HitX = 30, HitY = 0, HitZ = 1.2f,
                                                       TravelX = 1f, Damage = 30f });
        world.Bus.Publish(new DetonationNotification { Shooter = shooter, HitX = 50, HitY = 5, HitZ = 0, Damage = 30f });   // the ground
        world.Bus.Publish(new Fdp.Toolkit.Combat.StrikeNotification { Striker = victim, Target = shooter, Weapon = Fdp.Toolkit.Combat.MeleeWeapon.MeleeFist });
        Tick(world, new EffectSpawnSystem(), 0.016f);
        var hits = HitFeedback.Of(world)!.Recent(10.0, HitFeedback.MarkerSeconds).ToArray();
        Assert.Equal(2, hits.Length);
        var round = Assert.Single(hits, h => h.Target == victim);
        Assert.Equal((shooter, new Vector3(1f, 0f, 0f), 30f), (round.Shooter, round.Travel, round.Damage));
        var blow = Assert.Single(hits, h => h.Target == shooter);
        Assert.Equal((victim, Vector3.Zero, Vector3.Zero), (blow.Shooter, blow.At, blow.Travel));   // at the one struck; no travel

        Assert.Equal(new Vector3(-1f, 0f, 0f), HitFeedback.FromWhere(round, null));                // back along the travel
        Assert.Equal(new Vector3(30f, 0f, 0f), HitFeedback.FromWhere(blow, new Vector3(30f, 0f, 0f)));   // toward the striker
        Assert.Null(HitFeedback.FromWhere(blow, null));                                            // nothing known: no arc
        var east = new Vector3(1f, 0f, 0f);
        Assert.Equal(-90f, HitFeedback.ScreenAngle(east, new Vector3(5f, 0f, 0f))!.Value, 3);      // ahead: top
        Assert.Equal(0f, HitFeedback.ScreenAngle(east, new Vector3(0f, -5f, 0f))!.Value, 3);       // his right (south): right
        Assert.Equal(90f, HitFeedback.ScreenAngle(east, new Vector3(-5f, 0f, 2f))!.Value, 3);      // behind: bottom
        Assert.Null(HitFeedback.ScreenAngle(east, new Vector3(0f, 0f, 5f)));                       // straight above: no bearing
        Assert.Equal((6f, 12f, 18f), (Hrot.UI.Common.Map3D.PossessionStation.HitArcThickness(0f),
            Hrot.UI.Common.Map3D.PossessionStation.HitArcThickness(30f), Hrot.UI.Common.Map3D.PossessionStation.HitArcThickness(1000f)));
    }

    [Fact]
    public void E2_AShot_MakesAFlashAttachedToTheWeapon_AndNoDecorativeTracer()
    {
        var (world, _) = World();
        var tank = Unit(world, Hrot.Map.Common.TkbEntityTypes.Tank_T72, new Vector3(0, 0, 3));
        var target = Unit(world, Hrot.Map.Common.TkbEntityTypes.Tank_M1Abrams, new Vector3(800, 0, 5));
        world.Bus.Publish(new WeaponFireNotification { Shooter = tank, Target = target, WeaponIndex = 0 });
        var spawn = new EffectSpawnSystem();
        Tick(world, spawn, 0.016f);

        var fx = Effects(world);
        Assert.Contains(fx, f => f.Type == EffectTkbCatalog.MuzzleFlashLarge);   // the 125 mm gun is large calibre
        Assert.DoesNotContain(fx, f => f.Type == EffectTkbCatalog.Tracer);         // ⛔ LR-4: a tracer is a real, replicated round now
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

    /// <summary>
    /// ⭐ CE-1194 — a thrown STONE (or a thrown blade) lands as a little dust: no burst, no scorch on the ground, no bang; a hand GRENADE
    /// still bursts and scorches. 🔒 User, live, 2026-10-19: <i>"stone falling on the ground create explosion effect and decal"</i>.
    /// </summary>
    [Fact]
    public void CE1194_AThrownStoneLandsAsDust_AGrenadeStillBursts()
    {
        var (world, tkb) = World();
        long stone = Hrot.Core.Tkb.MunitionTkbCatalog.TkbStone, hatchet = Hrot.Core.Tkb.MunitionTkbCatalog.TkbThrowingHatchet;
        long grenade = Hrot.Core.Tkb.MunitionTkbCatalog.TkbM67Grenade, ball = Hrot.Core.Tkb.MunitionTkbCatalog.Tkb556Ball;
        Assert.True(EffectSpawnSystem.IsInertHandItem(tkb, stone));
        Assert.True(EffectSpawnSystem.IsInertHandItem(tkb, hatchet));
        Assert.False(EffectSpawnSystem.IsInertHandItem(tkb, grenade));     // a hand item, but its warhead bursts
        Assert.False(EffectSpawnSystem.IsInertHandItem(tkb, ball));        // not thrown: a rifle round keeps its impact
        world.Bus.Publish(new DetonationNotification { HitX = 10, HitY = 10, HitZ = 0, Ammo = stone, Damage = 3f });
        world.Bus.Publish(new DetonationNotification { HitX = 30, HitY = 10, HitZ = 0, Ammo = grenade, Damage = 400f });
        var spawn = new EffectSpawnSystem();
        Tick(world, spawn, 0.016f);

        Assert.Equal(1, spawn.InertLandings);
        var fx = Effects(world);
        Vector3 At(Entity e) => world.GetComponentRO<SimTransform>(e).Position;
        var atStone = fx.Where(f => At(f.Entity).X == 10f).Select(f => f.Type).ToList();
        Assert.Equal(new[] { EffectTkbCatalog.ExplosionSmall }, atStone);  // the dust puff only — no decal
        var atGrenade = fx.Where(f => At(f.Entity).X == 30f).Select(f => f.Type).ToList();
        Assert.Contains(atGrenade, t => t == EffectTkbCatalog.DefaultFor(EffectTkbCatalog.ClassOf(400f)).Decal
                                        || t == EffectTkbCatalog.EffectsFor(tkb, grenade, 400f).Decal);   // the grenade scorches
        Assert.Equal(2, atGrenade.Count);                                     // its burst and its scorch
    }

    /// <summary>
    /// ⭐ CE-1199 (VE-D) — a mark is the size of the round that made it: a tank's main-gun round marks a wall as a LARGE crater, a rifle
    /// ball as a small hole — read through the shooter's gun, since the tank's round carries no damage of its own. 🔒 User, live,
    /// 2026-10-19: <i>"decals left by ammo are unified size same for bullets and same for tank cannon shells"</i>.
    /// </summary>
    [Fact]
    public void CE1199_AnImpactMark_IsTheSizeOfItsRound_ATankShellsIsLarge_ABulletsSmall()
    {
        var (world, tkb) = World();
        world.RegisterEvent<RoundImpactNotification>();
        if (!world.IsComponentTypeRegistered<TkbIdentity>()) world.RegisterComponent<TkbIdentity>();
        Entity Shooter(long type)
        {
            var e = world.CreateEntity();
            world.AddComponent(e, new TkbIdentity { TkbType = type });
            return e;
        }
        var tank = Shooter(Hrot.Map.Common.TkbEntityTypes.Tank_M1Abrams);
        var rifleman = Shooter(Hrot.Map.Common.TkbEntityTypes.Infantry_Rifleman);
        long gunRound = unchecked((long)Fdp.Toolkit.Combat.CombatTkb.MountOf(world, tank, 0)!.AmmoGuid);
        long ball = unchecked((long)Fdp.Toolkit.Combat.CombatTkb.MountOf(world, rifleman, 0)!.AmmoGuid);
        Assert.True(EffectSpawnSystem.ImpactDamage(world, tkb, new RoundImpactNotification { Shooter = tank, Ammo = gunRound })
                    >= EffectTkbCatalog.LargeDamageFrom, "the tank's round reads its gun's damage");
        world.Bus.Publish(new RoundImpactNotification { Shooter = tank, X = 10, Y = 19.9f, Z = 1.5f, NormalY = -1f, Ammo = gunRound });
        world.Bus.Publish(new RoundImpactNotification { Shooter = rifleman, X = 30, Y = 19.9f, Z = 1.5f, NormalY = -1f, Ammo = ball });
        var spawn = new EffectSpawnSystem();
        Tick(world, spawn, 0.016f);

        var fx = Effects(world);
        Vector3 At(Entity e) => world.GetComponentRO<SimTransform>(e).Position;
        Assert.Equal(10f, At(fx.Single(f => f.Type == EffectTkbCatalog.DecalLarge).Entity).X);
        Assert.Equal(30f, At(fx.Single(f => f.Type == EffectTkbCatalog.DecalSmall).Entity).X);
    }

    /// <summary>
    /// ⭐⭐ CE-1129 (docs/DESIGN_Visual_Effects.md §10 IM-5, IM-6) — every surface a round marked gets the round's puff and its mark, the
    /// mark turned onto the face: on a WALL it stands in the wall's plane (its +Z = the wall's normal), on the GROUND it is draped, on a ROOF
    /// it lies on the roof — never dropped to the ground below. 🔒 User, 2026-10-16: <i>"Every surface pierced should get a decal and
    /// particle effect on the entry side"</i> · <i>"the shot goes to that point and leaves a decal there"</i>.
    /// </summary>
    [Fact]
    public void CE1129_EveryMarkedSurface_GetsThePuffAndTheMark_OnTheWallInItsPlane_OnTheGroundDraped_OnTheRoofOnTheRoof()
    {
        var (world, tkb) = World();
        world.RegisterEvent<RoundImpactNotification>();
        long ball = Hrot.Core.Tkb.MunitionTkbCatalog.Tkb556Ball;
        world.Bus.Publish(new RoundImpactNotification { X = 10, Y = 19.9f, Z = 1.5f, NormalY = -1f, TravelY = 1f, Ammo = ball, Kind = (byte)Fdp.Toolkit.Combat.ImpactKind.Entry });
        world.Bus.Publish(new RoundImpactNotification { X = 30, Y = 5, Z = 0f, NormalZ = 1f, Ammo = ball, Kind = (byte)Fdp.Toolkit.Combat.ImpactKind.Stop });
        world.Bus.Publish(new RoundImpactNotification { X = 50, Y = 5, Z = 6f, NormalZ = 1f, Ammo = ball, Kind = (byte)Fdp.Toolkit.Combat.ImpactKind.Stop });
        var spawn = new EffectSpawnSystem();
        Tick(world, spawn, 0.016f);

        Assert.Equal(3, spawn.ImpactsDrawn);
        var fx = Effects(world);
        Assert.Equal(3, fx.Count(f => f.Type == EffectTkbCatalog.ExplosionSmall));   // the small-arms impact puff
        var marks = fx.Where(f => f.Type == EffectTkbCatalog.DecalSmall).Select(f => f.Entity).ToList();
        Assert.Equal(3, marks.Count);
        Vector3 Up(Entity e) => Vector3.Transform(Vector3.UnitZ, world.GetComponentRO<SimTransform>(e).Rotation);
        var wall = marks.Single(e => world.GetComponentRO<SimTransform>(e).Position.X == 10f);
        Assert.True(Vector3.Distance(Up(wall), new Vector3(0, -1, 0)) < 1e-4f, $"the wall mark faces {Up(wall)}");

        var layer = new EffectLayer(() => world, () => tkb) { GroundHeight = (_, _) => 0f };
        var drawn = layer.Resolve().Where(r => r.Look.Kind == EffectKind.Decal).ToList();
        Assert.False(layer.IsOnGround(drawn.Single(r => r.At.X == 10f)));    // in the wall's plane
        Assert.True(layer.IsOnGround(drawn.Single(r => r.At.X == 30f)));     // draped on the ground
        Assert.False(layer.IsOnGround(drawn.Single(r => r.At.X == 50f)));    // on the roof, 6 m up — not dropped to the ground
        var (centre, u, v) = EffectLayer.SurfaceDiscFrame(new Vector3(10, 19.9f, 1.5f), new Vector3(0, -1, 0));
        Assert.True(centre.Y < 19.9f, "lifted off the wall toward the shooter");
        Assert.Equal(0f, u.Y, 4);
        Assert.Equal(0f, v.Y, 4);                                            // the disc lies in the wall's plane
    }

    /// <summary>
    /// ⭐⭐ CE-1115 (DESIGN_Visual_Effects.md §9) — a flare release (backend's fire, BS-1 §5.4c: an expendable <c>AmmoGuid</c>, origin, the
    /// aircraft's velocity, <c>FiredAt</c>) draws a FLARE that is born at the release, moves along <c>DecoyFlight.At</c> — the point the
    /// seeker chases — and draws no muzzle flash; chaff draws a cloud; a release already burnt out (a late viewer) draws nothing.
    /// </summary>
    [Fact]
    public void CE1115_AFlareRelease_DrawsAFlareMovingAlongTheDecoyPath_NoFlash()
    {
        var (world, tkb) = World();
        At(world, 100.0);
        var heli = Unit(world, Hrot.Map.Common.TkbEntityTypes.Heli_UH60, new Vector3(0, 0, 300));
        var origin = new Vector3(0, 0, 300);
        var velocity = new Vector3(60, 0, 0);
        world.Bus.Publish(new WeaponFireNotification { Shooter = heli, WeaponIndex = 1, AmmoGuid = MunitionTkbCatalog.TkbFlare,
                                                       Origin = origin, Velocity = velocity, Quantity = 1, FiredAt = 99.5 });
        world.Bus.Publish(new WeaponFireNotification { Shooter = heli, WeaponIndex = 1, AmmoGuid = MunitionTkbCatalog.TkbChaff,
                                                       Origin = origin, Velocity = velocity, Quantity = 1, FiredAt = 99.5 });
        world.Bus.Publish(new WeaponFireNotification { Shooter = heli, WeaponIndex = 1, AmmoGuid = MunitionTkbCatalog.TkbFlare,
                                                       Origin = origin, Velocity = velocity, Quantity = 1, FiredAt = 90.0 });   // burnt out
        var spawn = new EffectSpawnSystem();
        Tick(world, spawn, 0.016f);

        var fx = Effects(world);
        Assert.DoesNotContain(fx, f => EffectTkbCatalog.VisualOf(tkb, f.Type)!.Kind == EffectKind.MuzzleFlash);   // a dispenser is no gun
        var flare = Assert.Single(fx, f => f.Type == EffectTkbCatalog.Flare).Entity;                            // the late one: nothing
        Assert.Single(fx, f => f.Type == EffectTkbCatalog.ChaffCloud);
        var exp = Fdp.Toolkit.Combat.Munitions.ExpendableOf(tkb, MunitionTkbCatalog.TkbFlare)!;
        Assert.Equal(99.5, world.GetComponentRO<Lifespan>(flare).BornAtSimSeconds);                            // DV-2: born at the release
        Assert.Equal(exp.LifeSeconds, world.GetComponentRO<Lifespan>(flare).Seconds);
        Assert.Equal(Fdp.Toolkit.Combat.DecoyFlight.At(origin, velocity, exp.SinkMps, 0.5f), world.GetComponentRO<SimTransform>(flare).Position);

        At(world, 102.0);                                                                                       // DV-3: it moves
        Tick(world, spawn, 0.016f);
        Assert.Equal(Fdp.Toolkit.Combat.DecoyFlight.At(origin, velocity, exp.SinkMps, 2.5f), world.GetComponentRO<SimTransform>(flare).Position);

        var layer = new EffectLayer(() => world, () => tkb);
        var drawn = Assert.Single(layer.Resolve(), r => r.Look.Kind == EffectKind.Flare);
        Assert.NotNull(drawn.Flight);
        Assert.NotEmpty(EffectLayer.Puffs(drawn));                                                              // its smoke trail
    }

    /// <summary>⭐⭐ CE-3248 (Civilians §7.17a TC-6) — a stone THROWN from a man's hands draws no muzzle flash (📐 before: a stone has no
    /// effects of its own, so it drew the default small flash), yet is recorded as his last shot (his pose reads it); a rifle shot still
    /// flashes. ✅ Red-proof: <c>Threw</c> dropped from <c>EffectSpawnSystem</c> ⇒ a flash at his hand ⇒ red.</summary>
    [Fact]
    public void CE3248_AThrow_DrawsNoMuzzleFlash_ButIsRecorded()
    {
        var (world, _) = World();
        At(world, 5.0);
        var man = Unit(world, CivilianTkbCatalog.TkbCivilianAdultMale, new Vector3(0, 0, 0));
        int hands = Fdp.Toolkit.Combat.PersonHands.MountIndex(world, man);
        Assert.True(hands >= 0, "a person type ends with hands");
        world.Bus.Publish(new WeaponFireNotification { Shooter = man, WeaponIndex = hands, AmmoGuid = MunitionTkbCatalog.TkbStone, Quantity = 1 });
        var spawn = new EffectSpawnSystem();
        Tick(world, spawn, 0.016f);
        Assert.Empty(Effects(world));
        Assert.True(RecentShots.Of(world)!.FiredWithin(man, 5.0, 1.0, out int mount) && mount == hands);

        var tank = Unit(world, Hrot.Map.Common.TkbEntityTypes.Tank_T72, new Vector3(50, 0, 3));
        world.Bus.Publish(new WeaponFireNotification { Shooter = tank, WeaponIndex = 0, AmmoGuid = MunitionTkbCatalog.Tkb81mmMortarHe, Quantity = 1 });
        Tick(world, spawn, 0.016f);
        Assert.Contains(Effects(world), f => f.Type == EffectTkbCatalog.MuzzleFlashMedium);
    }

    /// <summary>⭐ CE-1115 (§9 DV-5, BS-1 §5.4b F3) — a shot draws the flash of the round actually FIRED (the fire carries it), not the
    /// mount's default: a T-72 firing an 81 mm mortar bomb (its set: a medium flash) shows a medium flash, not its large 125 mm one.</summary>
    [Fact]
    public void CE1115_AShot_DrawsTheFiredRoundsFlash_NotTheMountsDefault()
    {
        var (world, _) = World();
        var tank = Unit(world, Hrot.Map.Common.TkbEntityTypes.Tank_T72, new Vector3(0, 0, 3));
        world.Bus.Publish(new WeaponFireNotification { Shooter = tank, WeaponIndex = 0, AmmoGuid = MunitionTkbCatalog.Tkb81mmMortarHe, Quantity = 1 });
        Tick(world, new EffectSpawnSystem(), 0.016f);
        var fx = Effects(world);
        Assert.Contains(fx, f => f.Type == EffectTkbCatalog.MuzzleFlashMedium);
        Assert.DoesNotContain(fx, f => f.Type == EffectTkbCatalog.MuzzleFlashLarge);
    }

    /// <summary>⭐ CE-1113 (DESIGN_Map_3D_Mode.md §6v AD-V6) — a burst in the AIR (a SAM's proximity fuze 400 m up, no target) draws its
    /// explosion where it burst and scorches nothing below; one just above the ground still does.</summary>
    [Fact]
    public void CE1113_ABurstInTheAir_ExplodesThere_AndLeavesNoDecalOnTheGround()
    {
        var (world, _) = World();
        world.Bus.Publish(new DetonationNotification { Target = Entity.Null, HitX = 50, HitY = 20, HitZ = 400f, Damage = 1100f });
        world.Bus.Publish(new DetonationNotification { Target = Entity.Null, HitX = 90, HitY = 20, HitZ = 1.5f, Damage = 1100f });
        Tick(world, new EffectSpawnSystem(), 0.016f);

        var fx = Effects(world);
        var explosions = fx.Where(f => f.Type == EffectTkbCatalog.ExplosionLarge).ToArray();
        Assert.Equal(2, explosions.Length);
        Assert.Contains(explosions, f => world.GetComponentRO<SimTransform>(f.Entity).Position.Z == 400f);   // drawn where it burst
        var decal = Assert.Single(fx, f => f.Type == EffectTkbCatalog.DecalLarge);                         // only the low one scorches
        Assert.Equal(90f, world.GetComponentRO<SimTransform>(decal.Entity).Position.X);
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

    /// <summary>
    /// ⭐⭐ CE-1094 / CE-1090 (docs/DESIGN_Map_3D_Mode.md §6n N-10, N-4) — a BOMB leaving the A-10's rack makes no muzzle flash (it made a
    /// 4 m one at the nose); a Javelin fired from the shoulder throws a BACKBLAST behind the gunner on the line of fire, besides its flash;
    /// every shot is remembered for the bodies (the launcher on the shoulder).
    /// </summary>
    [Fact]
    public void CE1094_ABombMakesNoFlash_ALauncherThrowsABackblastBehindHim()
    {
        var (world, tkb) = World();
        At(world, 5.0);
        var a10 = Unit(world, Hrot.Map.Common.TkbEntityTypes.Jet_AttackA10, new Vector3(0, 0, 300));
        world.Bus.Publish(new WeaponFireNotification { Shooter = a10, Target = Entity.Null, WeaponIndex = 0 });
        var spawn = new EffectSpawnSystem();
        Tick(world, spawn, 0.016f);
        Assert.DoesNotContain(Effects(world), f => EffectTkbCatalog.VisualOf(tkb, f.Type)?.Kind == EffectKind.MuzzleFlash);

        var gunner = Unit(world, Hrot.Core.Tkb.UrbanCombatTkbCatalog.TkbJavelinTeam, new Vector3(100, 0, 0));
        var tank = Unit(world, Hrot.Map.Common.TkbEntityTypes.Tank_T72, new Vector3(100, 400, 2));
        var suite = tkb.GetByType(Hrot.Core.Tkb.UrbanCombatTkbCatalog.TkbJavelinTeam)!.GetDescriptor<WeaponSuiteDto>()!;
        int launcher = suite.Mounts.FindIndex(m => m.Category == WeaponCategory.Atgm);
        world.Bus.Publish(new WeaponFireNotification { Shooter = gunner, Target = tank, WeaponIndex = launcher });
        Tick(world, spawn, 0.016f);
        var blast = Effects(world).Single(f => f.Type == EffectTkbCatalog.Backblast).Entity;
        var at = world.GetComponentRO<SimTransform>(blast).Position;
        Assert.True(at.Y < -1.5f && MathF.Abs(at.X - 100f) < 0.1f, $"behind him, away from the tank ({at})");
        Assert.True(Hrot.UI.Common.Effects.RecentShots.Of(world)!.FiredWithin(gunner, 5.0, 1.0, out int mount) && mount == launcher);
    }

    /// <summary>
    /// ⭐ CE-1105 (docs/DESIGN_Map_3D_Mode.md §6u B-1) — which weapons stream: the AH-64's chin M134 and the A-10's GAU-8 (machine gun,
    /// autocannon) do; a rifle, a tank gun and the AH-64's rocket pod are one round each.
    /// </summary>
    [Fact]
    public void CE1105_MachineGunsAndAutocannonsStream_RiflesAndBigGunsDoNot()
    {
        var (_, tkb) = World();
        List<WeaponMountDto> Mounts(long type) => tkb.GetByType(type)!.GetDescriptor<WeaponSuiteDto>()!.Mounts;
        var ah64 = Mounts(Hrot.Map.Common.TkbEntityTypes.Heli_AH64);
        Assert.True(FireStreams.IsAutomatic(ah64[0]));
        Assert.False(FireStreams.IsAutomatic(ah64[1]));
        Assert.Contains(Mounts(Hrot.Map.Common.TkbEntityTypes.Jet_AttackA10), FireStreams.IsAutomatic);
        Assert.DoesNotContain(Mounts(Hrot.Map.Common.TkbEntityTypes.Infantry_Rifleman), FireStreams.IsAutomatic);
        Assert.False(FireStreams.IsAutomatic(Mounts(Hrot.Map.Common.TkbEntityTypes.Tank_T72)[0]));
        Assert.False(FireStreams.IsAutomatic(null));
    }

    /// <summary>
    /// ⭐ CE-1105 (§6u B-2) — the stream's geometry: 0.1 s after the shot its head has flown 90 m (+ one spacing), so dashes of 1.8 m start
    /// every 5 m from the phase (90 % 5 = 0) up to 95 m, toward the aim; it is 1 − 0.1/0.35 strong; past the window nothing; an unknown aim
    /// streams along the weapon's forward; a near target cuts it short.
    /// </summary>
    [Fact]
    public void CE1105_TheDashesRunOutFromTheMuzzle_TowardTheAim_AndFade()
    {
        var into = new List<(Vector3 From, Vector3 To)>();
        var muzzle = new Vector3(10, 0, 5);
        float s = FireStreams.Dashes(muzzle, muzzle + new Vector3(300, 0, 0), Vector3.UnitY, 0.1, into);
        Assert.Equal(1f - 0.1f / 0.35f, s, 3);
        Assert.Equal(19, into.Count);   // 0, 5, … 90
        for (int i = 0; i < into.Count; i++)
        {
            Assert.Equal(muzzle.X + i * 5f, into[i].From.X, 2);
            Assert.Equal(1.8f, Vector3.Distance(into[i].From, into[i].To), 2);
            Assert.Equal(0f, into[i].From.Y, 3);   // toward the aim, not the forward
        }
        Assert.Equal(0f, FireStreams.Dashes(muzzle, muzzle + new Vector3(300, 0, 0), Vector3.UnitY, 0.36, into));
        Assert.Empty(into);

        FireStreams.Dashes(muzzle, Vector3.Zero + muzzle, Vector3.UnitY, 0.1, into);   // no aim known
        Assert.All(into, d => Assert.True(MathF.Abs(d.From.X - muzzle.X) < 1e-3f && d.To.Y > d.From.Y));

        FireStreams.Dashes(muzzle, muzzle + new Vector3(12, 0, 0), Vector3.UnitY, 0.3, into);   // a target 12 m away
        Assert.All(into, d => Assert.True(d.To.X <= muzzle.X + 12f + 1e-3f));
    }

    /// <summary>⭐ CE-1105 (§6u) — the spawner remembers where each shot was AIMED: at its target when it has one.</summary>
    [Fact]
    public void CE1105_EveryShotIsRememberedWithItsAim()
    {
        var (world, _) = World();
        At(world, 7.0);
        var heli = Unit(world, Hrot.Map.Common.TkbEntityTypes.Heli_AH64, new Vector3(0, 0, 30));
        var tank = Unit(world, Hrot.Map.Common.TkbEntityTypes.Tank_T72, new Vector3(400, 0, 0));
        world.Bus.Publish(new WeaponFireNotification { Shooter = heli, Target = tank, WeaponIndex = 0 });
        Tick(world, new EffectSpawnSystem(), 0.016f);
        var shot = Assert.Single(RecentShots.Of(world)!.Recent(7.1, FireStreams.Window));
        Assert.Equal((heli, 0, 7.0, new Vector3(400, 0, 0)), shot);
        Assert.Empty(RecentShots.Of(world)!.Recent(7.5, FireStreams.Window));
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
    /// body; a soldier does not burn; ⭐ CE-1198 nor does a weapon system (it only goes dark, 🔒 user 2026-10-19: "weapons are usually
    /// non-flammable"); a wreck first seen already dead does not; in a replay nothing is made (the recording has it).</summary>
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
        var hmg = Living(Hrot.Map.Common.TkbEntityTypes.WeaponSystem_M2Hmg, 100f);   // ⭐ CE-1198 — a weapon does not burn
        var wrecks = new WreckSpawnSystem();
        Tick(world, wrecks, 0.016f);
        Assert.Empty(Effects(world));                                   // the loaded wreck is already dead: no crossing

        world.SetComponent(tank, new Fdp.Toolkit.Combat.Components.Health { Current = 0f, Max = 100f });
        world.SetComponent(soldier, new Fdp.Toolkit.Combat.Components.Health { Current = 0f, Max = 100f });
        world.SetComponent(hmg, new Fdp.Toolkit.Combat.Components.Health { Current = 0f, Max = 100f });
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
            World = world,
        });
        Fdp.Toolkit.Replay.ReplayState.Enter(world);                  // ⭐ CE-3329 P2 — the world says it is a replay
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

    /// <summary>
    /// ⭐ CE-1146 PR-2 (docs/DESIGN_Map_3D_Mode.md §6aa.next SW) — a wreck's smoke drifts on the WORLD'S wind: through <c>Resolve</c> (the
    /// production path) a World Environment blowing 8 m/s toward the west leans the column west and not north-east (the old fixed breeze);
    /// calm rises straight; a world with no weather keeps the old breeze. ✅ Red-proof: <c>Resolve</c> not passing the wind ⇒ the column
    /// leans north-east.
    /// </summary>
    [Fact]
    public void CE1146_PR2_TheSmokeColumn_DriftsOnTheWorldsWind()
    {
        var (world, tkb) = World();
        world.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
        world.RegisterComponent<Fdp.Toolkit.Weather.ScenarioClock>();
        world.RegisterComponent<Fdp.Toolkit.Weather.WeatherConditions>();
        var env = world.CreateEntity();
        world.AddComponent(env, Fdp.Toolkit.Weather.ScenarioClock.Default);
        world.AddComponent(env, Fdp.Toolkit.Weather.WeatherConditions.SteadyWind(new Vector2(-8f, 0f)));
        At(world, 0.0);
        var tank = Unit(world, Hrot.Map.Common.TkbEntityTypes.Tank_T72, new Vector3(50, 0, 0));
        world.AddComponent(tank, new Fdp.Toolkit.Combat.Components.Health { Current = 100f, Max = 100f });
        var wrecks = new WreckSpawnSystem();
        Tick(world, wrecks, 0f);
        world.SetComponent(tank, new Fdp.Toolkit.Combat.Components.Health { Current = 0f, Max = 100f });
        Tick(world, wrecks, 0f);
        var layer = new EffectLayer(() => world, () => tkb, new Hrot.UI.Common.Map3D.EntityBodyLayer3D(() => world, () => tkb));
        At(world, 10.0);

        var smoke = Assert.Single(layer.Resolve(), r => r.Look.Kind == EffectKind.Smoke);
        Assert.True(smoke.Wind is { X: < -7f } w && MathF.Abs(w.Y) < 0.5f, $"the world's wind reaches the column: {smoke.Wind}");
        Vector2 Lean(EffectLayer.Resolved r)
        {
            var puffs = EffectLayer.Puffs(r);
            var top = puffs.OrderByDescending(p => p.At.Z).Take(4).ToList();
            return new Vector2(top.Average(p => p.At.X), top.Average(p => p.At.Y)) - new Vector2(r.At.X, r.At.Y);
        }
        var lean = Lean(smoke);
        Assert.True(lean.X < -smoke.Look.Size, $"leans west, downwind: {lean}");

        var calm = Lean(smoke with { Wind = Vector2.Zero });
        Assert.True(calm.Length() < smoke.Look.Size * 0.5f, $"calm rises straight: {calm}");
        var none = Lean(smoke with { Wind = null });
        Assert.True(none.X > 0f && none.Y > 0f, $"no weather modelled: the old north-east breeze: {none}");
        Assert.Equal(new Vector3(3f, 0f, 0f), EffectLayer.SmokeDrift(new Vector2(40f, 0f)));   // a gale is capped
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

    /// <summary>⭐ CE-1190 (docs/blueprints/batches/NOTE_Backend_To_Ui_Thrown_Blade_Shapes.md) — NO <see cref="MunitionShape"/> is
    /// invisible: every value draws at least one solid (a bullet's body is by design its tracer streak, so it is railed through that),
    /// and a shape appended later that has no case of its own still draws the default lump.</summary>
    [Fact]
    public void CE1190_EveryMunitionShapeDrawsSomething_NoneInvisible()
    {
        foreach (var shape in Enum.GetValues<MunitionShape>())
        {
            var look = new MunitionVisualDto { Shape = shape, Length = 0.4f, Diameter = 0.1f, Tracer = shape == MunitionShape.Bullet };
            var r = new ProjectileLayer.Round(look, new Vector3(10, 0, 3), Vector3.UnitX, false, 0f, new Vector3(0, 0, 3), 1, Speed: 20f);
            if (shape == MunitionShape.Bullet) Assert.True(ProjectileLayer.TracerOf(r, out _, out _), "a tracer bullet draws its streak");
            else Assert.NotEmpty(ProjectileLayer.SolidsOf(r));
        }
        var future = new ProjectileLayer.Round(new MunitionVisualDto { Shape = (MunitionShape)200, Length = 0.3f, Diameter = 0.08f },
                                               new Vector3(10, 0, 3), Vector3.UnitX, false, 0f, Vector3.Zero, 1);
        Assert.NotEmpty(ProjectileLayer.SolidsOf(future));                        // an unknown, future shape: a lump, not nothing
    }

    /// <summary>⭐ CE-1190 — the thrown blades (the built-in TKB 6044 / 6045 / 6046): the hatchet TUMBLES end over end, the spear points
    /// along its velocity (no tumble), the star SPINS flat; and a blade lying on the ground is flat on it (its top within 0.15 m).</summary>
    [Fact]
    public void CE1190_ThrownBlades_AxeTumbles_SpearPointsAlongFlight_StarSpins_LyingOnesAreFlat()
    {
        var tkb = Hrot.Map.Common.HrotEnvironment.CreateTkb();
        var world = RoundsWorld(tkb);
        var v = new Vector3(12, 4, 1);
        Round(world, MunitionTkbCatalog.TkbThrowingHatchet, new Vector3(0, 0, 3), v);
        Round(world, MunitionTkbCatalog.TkbThrowingSpear, new Vector3(0, 5, 3), v);
        Round(world, MunitionTkbCatalog.TkbThrowingStar, new Vector3(0, 10, 3), v);
        const byte Landed = (byte)Fdp.Toolkit.Combat.Components.WarheadRound.Landed;
        Round(world, MunitionTkbCatalog.TkbThrowingHatchet, new Vector3(20, 0, 0.1f), Vector3.Zero, Landed);
        Round(world, MunitionTkbCatalog.TkbThrowingSpear, new Vector3(20, 5, 0.1f), Vector3.Zero, Landed);
        Round(world, MunitionTkbCatalog.TkbThrowingStar, new Vector3(20, 10, 0.1f), Vector3.Zero, Landed);
        var layer = new ProjectileLayer(() => world, () => tkb) { GroundHeight = (_, _) => 0.5f };

        List<ProjectileLayer.Round> At(double t)
        {
            world.SetSingletonUnmanaged(new GlobalTime { TotalTime = t });
            return layer.Resolve();
        }
        ProjectileLayer.Round Of(List<ProjectileLayer.Round> rs, MunitionShape s, bool lying) => rs.Single(r => r.Look.Shape == s && r.Lying == lying);

        var t1 = At(0.1);
        var t2 = At(0.2);
        Assert.Equal(6, t1.Count);                                                // every blade is drawn: none invisible
        Assert.True(Vector3.Dot(Of(t1, MunitionShape.Axe, false).Nose, Of(t2, MunitionShape.Axe, false).Nose) < 0.99f, "the axe tumbles");
        Assert.True(Vector3.Dot(Of(t1, MunitionShape.Star, false).Nose, Of(t2, MunitionShape.Star, false).Nose) < 0.99f, "the star spins");
        Assert.Equal(0f, Of(t1, MunitionShape.Star, false).Nose.Z, 3);            // ... flat, about the vertical
        var vn = Vector3.Normalize(v);
        Assert.Equal(1f, Vector3.Dot(Of(t1, MunitionShape.Spear, false).Nose, vn), 3);   // the spear points along its velocity
        Assert.Equal(1f, Vector3.Dot(Of(t2, MunitionShape.Spear, false).Nose, vn), 3);   // ... at every time: no tumble
        var spear = Of(t1, MunitionShape.Spear, false);
        var point = ProjectileLayer.SolidsOf(spear).Last();
        Assert.True(Vector3.Dot(point.To - spear.Centre, vn) > 0f, "the point leads");

        foreach (var shape in new[] { MunitionShape.Axe, MunitionShape.Spear, MunitionShape.Star })
        {
            var lying = Of(t1, shape, true);
            Assert.Equal(0f, lying.Nose.Z, 3);                                    // flat, not tumbling
            Assert.Equal(lying.Nose, Of(t2, shape, true).Nose);                   // ... and not turning
            var solids = ProjectileLayer.SolidsOf(lying);
            float top = solids.Max(s => MathF.Max(s.From.Z, s.IsSphere ? s.From.Z : s.To.Z) + MathF.Max(s.RadiusFrom, s.RadiusTo));
            float bottom = solids.Min(s => MathF.Min(s.From.Z, s.IsSphere ? s.From.Z : s.To.Z) - MathF.Max(s.RadiusFrom, s.RadiusTo));
            Assert.InRange(top, 0.5f, 0.5f + 0.15f);                              // its top within 0.15 m of the ground
            Assert.True(bottom >= 0.5f - 1e-3f, $"{shape} rests ON the ground");
        }
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

    /// <summary>⭐ P2 (§8 PR-F) — a TRACER munition (the TKB switch) draws a streak along its REAL flight — the last 0.03 s, ≤ 25 m,
    /// never past its muzzle — and no body; in 2-D a wider streak in its glow and no marker. An ordinary bullet draws nothing.</summary>
    [Fact]
    public void P2_ATracerBulletBurnsAStreakAlongItsRealFlight_AnOrdinaryBulletDrawsNothing()
    {
        var tracerLook = new MunitionVisualDto { Shape = MunitionShape.Bullet, Tracer = true };
        var r = new ProjectileLayer.Round(tracerLook, new Vector3(300, 40, 2), Vector3.Normalize(new Vector3(1, 0.1f, 0)), false, 0f,
                                          Vector3.Zero, 1, Speed: 850f);
        Assert.True(ProjectileLayer.TracerOf(r, out var head, out var tail));
        Assert.Equal(r.Centre, head);
        Assert.Equal(25f, Vector3.Distance(head, tail), 3);                     // 850 m/s × 0.03 s = 25.5 ⇒ capped at 25 m
        Assert.True(Vector3.Dot(head - tail, r.Nose) > 0f, "the streak trails BEHIND the round");
        Assert.Empty(ProjectileLayer.SolidsOf(r));                               // a bullet has no body
        var nearMuzzle = r with { Centre = new Vector3(10, 1, 2) };
        Assert.True(ProjectileLayer.TracerOf(nearMuzzle, out var h2, out var t2));
        Assert.True(Vector3.Distance(h2, t2) <= Vector3.Distance(nearMuzzle.Centre, nearMuzzle.Muzzle) + 1e-3f, "never past the muzzle");
        Assert.False(ProjectileLayer.TracerOf(r with { Look = tracerLook with { Tracer = false } }, out _, out _));

        const long TracerAmmo = 6998;
        var tkb = Hrot.Map.Common.HrotEnvironment.CreateTkb();
        var t = new TkbTemplate("Test 5.56 tracer", TracerAmmo);
        t.AddDescriptor(tracerLook);
        tkb.Register(t);
        var world = RoundsWorld(tkb);
        var tracer = Round(world, TracerAmmo, new Vector3(300, 40, 2), new Vector3(850, 85, 0));
        var plain = Round(world, 0, new Vector3(300, 50, 2), new Vector3(850, 85, 0));
        var layer = new ProjectileLayer(() => world, () => tkb);
        var drawn = Assert.Single(layer.Resolve());                              // the tracer only
        Assert.Equal(System.MathF.Sqrt(850 * 850 + 85 * 85), drawn.Speed, 1);

        var gizmo = new Hrot.ScenarioEditor.Gizmos.ProjectilePresentationGizmo();
        var buffer = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer();
        gizmo.Draw(world, tracer, buffer);
        var frame = buffer.GetFrame().ToArray();
        Assert.Equal(2, frame.Length);                                           // the streak + its glow, no marker
        Assert.All(frame, p => Assert.Equal(Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveShape.Line, p.Shape));
        buffer = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer();
        gizmo.Draw(world, plain, buffer);
        Assert.Single(buffer.GetFrame().ToArray());
    }

    /// <summary>⭐⭐ LR-1 (docs/DESIGN_Visual_Effects.md §8.5, R-271: "Every ammo needs its tkb type, always") — every built-in mount
    /// loads a registered munition type, and giving it one CHANGED NO COMBAT NUMBER: no new type picks up an explosive warhead from
    /// the reference library by name (only the M67 and the 81 mm, which had them), and every mount's penetration is still its own.
    /// The visible ones carry a look (tracers, the big gun rounds, the rocket, the missile); 5.56 ball does not.
    /// <para>⭐ M3 (DESIGN_Armour_Hit_Model §2) — RE-PINNED ON PURPOSE: the RPG and the TOW now carry their own shaped-charge
    /// warhead (a decided combat change), so they join the explosive set; every other "no number moved" claim stands.</para></summary>
    [Fact]
    public void LR1_EveryBuiltInMountLoadsAMunitionType_AndNoCombatNumberMoved()
    {
        var tkb = Hrot.Map.Common.HrotEnvironment.CreateTkb();
        var explosive = new HashSet<long> { MunitionTkbCatalog.TkbM67Grenade, MunitionTkbCatalog.Tkb81mmMortarHe,
                                            MunitionTkbCatalog.TkbRpg7Rocket, MunitionTkbCatalog.TkbTowMissile,   // ⭐ M3
                                            MunitionTkbCatalog.TkbJavelinMissile, MunitionTkbCatalog.TkbNlawMissile,   // ⭐ M5 — the new AT teams' own launchers
                                            MunitionTkbCatalog.TkbKornetMissile,     // ⭐ M9 — the ATGM technical's
                                            MunitionTkbCatalog.TkbFpvWarhead,        // ⭐ M7 — the drone's own shaped charge
                                            MunitionTkbCatalog.TkbMk82Bomb,          // ⭐ Air Ops slice 4 — the new bomb's area warhead (new type, no number moved)
                                            MunitionTkbCatalog.TkbLawRocket,         // ⭐ WS-4 — the M72 LAW weapon system (605) is the first mount to load the existing LAW rocket (no number moved)
                                            MunitionTkbCatalog.TkbGau8Hei, MunitionTkbCatalog.TkbHydra70Rocket,   // ⭐ Air Ops 6a — new types on new mounts (the A-10's gun, the AH-64's pod): no number moved
                                            MunitionTkbCatalog.Tkb60mmMortarHe, MunitionTkbCatalog.Tkb120mmMortarHe,   // ⭐ IF-1 (I7) — the new guns' own rounds (new types on new mounts): no number moved
                                            MunitionTkbCatalog.Tkb105mmHowitzerHe, MunitionTkbCatalog.Tkb155mmHowitzerHe,
                                            MunitionTkbCatalog.TkbStingerMissile, MunitionTkbCatalog.TkbIglaMissile,   // ⭐ CE-2187 — the MANPADS' proximity-fused warheads (new types on new mounts): no number moved
                                            MunitionTkbCatalog.Tkb23mmHei,           // ⭐ CE-2190 — the ZU-23-2's HE-I (a new type on a new mount)
                                            MunitionTkbCatalog.TkbBatonRound, MunitionTkbCatalog.TkbStunGrenade,     // ⭐ CE-3223
                                            MunitionTkbCatalog.TkbTearGasGrenade,
                                            MunitionTkbCatalog.TkbExplosiveCharge };   // ⭐ CE-3233 B-11b — the charge 618's 8 kg round (a new type on a new mount)   // ⭐ CE-3219 part 3 — the launcher man's gas grenades (a new type on a new mount); ⭐ CE-3223 — the security forces' non-lethal rounds (new types on new mounts)
        int mounts = 0, tracerMixed = 0;
        foreach (var t in tkb.GetAll())
        {
            var suite = t.GetDescriptor<WeaponSuiteDto>();
            if (suite == null) continue;
            foreach (var m in suite.Mounts)
            {
                if (m.Hands) continue;   // ⭐ CE-3230 (H-2) — a person's HANDS name no round of their own: they hold what he picked up
                mounts++;
                Assert.True(m.AmmoGuid != 0, $"{t.Name}: a mount with no munition type");
                Assert.True(tkb.TryGetByType(unchecked((long)m.AmmoGuid), out var ammo) && ammo != null, $"{t.Name}: munition {m.AmmoGuid} not registered");
                // a round is DIS kind 2 (munition); ⭐ CE-2188 / CE-1115 — a dispenser's flare / chaff is kind 8 (expendable, BS-1 §5.4c)
                Assert.True(ammo!.DisType.Kind == 2 || (ammo.DisType.Kind == 8 && m.Category == WeaponCategory.Countermeasure),
                            $"{t.Name} / {ammo.Name}: DIS kind {ammo.DisType.Kind}");
                var (warhead, source, _) = Fdp.Toolkit.Tkb.Parameters.ParameterResolver.Warhead(tkb, unchecked((long)m.AmmoGuid));
                Assert.True((warhead != null) == explosive.Contains(unchecked((long)m.AmmoGuid)), $"{t.Name} / {ammo.Name}: warhead changed — {source}");
                if (m.Penetration > 0f)
                    Assert.Equal(m.Penetration, Fdp.Toolkit.Tkb.Parameters.ParameterResolver.MountPenetration(tkb, m).Value);
                if (m.TracerAmmoGuid != 0)
                {
                    tracerMixed++;
                    Assert.True(m.TracerEvery > 0, $"{t.Name}: a tracer munition with no mix");
                    Assert.True(MunitionTkbCatalog.VisualOf(tkb, unchecked((long)m.TracerAmmoGuid))?.Tracer == true);
                    Assert.Null(Fdp.Toolkit.Tkb.Parameters.ParameterResolver.Warhead(tkb, unchecked((long)m.TracerAmmoGuid)).Warhead);
                }
            }
        }
        Assert.True(mounts >= 6, $"only {mounts} mounts found");
        Assert.True(tracerMixed >= 3, "the rifles mix tracers (the M4 and both insurgent rifles)");
        Assert.Null(MunitionTkbCatalog.VisualOf(tkb, MunitionTkbCatalog.Tkb556Ball));          // an ordinary bullet: no look, never sent
        foreach (var visible in new[] { MunitionTkbCatalog.Tkb556Tracer, MunitionTkbCatalog.Tkb25mmApdsT, MunitionTkbCatalog.Tkb120mmApfsds,
                                        MunitionTkbCatalog.Tkb125mmApfsds, MunitionTkbCatalog.TkbTowMissile, MunitionTkbCatalog.TkbRpg7Rocket })
            Assert.NotNull(tkb.GetByType(visible).GetDescriptor<MunitionVisualDto>());
    }

    /// <summary>⭐ M3 (DESIGN_Armour_Hit_Model §2) — the RPG and TOW rounds carry a SHAPED-CHARGE warhead of their own (the template
    /// wins over the library by name), with the library's "PG-7V HEAT" / "TOW-2 HEAT" numbers; the names stay.</summary>
    [Fact]
    public void M3_TheRpgAndTowRounds_CarryAShapedChargeWarhead()
    {
        var tkb = Hrot.Map.Common.HrotEnvironment.CreateTkb();
        foreach (var (type, library) in new[] { (MunitionTkbCatalog.TkbRpg7Rocket, MunitionTkbCatalog.Rpg7LibraryName),
                                                 (MunitionTkbCatalog.TkbTowMissile, MunitionTkbCatalog.TowLibraryName) })
        {
            var (w, source, provenance) = Fdp.Toolkit.Tkb.Parameters.ParameterResolver.Warhead(tkb, type);
            Assert.NotNull(w);
            Assert.Equal(WarheadKind.ShapedCharge, w!.Kind);
            Assert.Equal(Fdp.Toolkit.Tkb.Parameters.ParameterProvenance.Explicit, provenance);
            Assert.True(Fdp.Toolkit.Tkb.Reference.ReferenceLibrary.Shared.TryGetAmmo(library, out var r));
            Assert.Equal(r.Warhead!.Dto.ExplosiveKg, w.ExplosiveKg);
            Assert.Equal(r.Warhead.Dto.FragmentRadiusM, w.FragmentRadiusM);
            Assert.NotEqual(library, tkb.GetByType(type).Name);   // the names stay (LR-1)
        }
    }

    /// <summary>⭐⭐ M5 (DESIGN_Missiles_And_Ammo §5) — the new munitions (6010–6021) carry the table's numbers ON THE TEMPLATE: a
    /// generic penetration + damage (what another round of a loadout reads), a warhead of the right kind (shaped charge, tandem, HE —
    /// none for the bullets) and, for a rocket or missile, its flight. The TOW and the PG-7V gain their flight; every one is a round
    /// (never a body) and no name matches a reference-library entry.</summary>
    [Fact]
    public void M5_TheNewMunitions_CarryTheTablesNumbers_OnTheirTemplates()
    {
        var tkb = Hrot.Map.Common.HrotEnvironment.CreateTkb();
        var sc = WarheadKind.ShapedCharge;
        foreach (var (type, pen, dmg, kind, tandem, guidance, speed) in new (long, float, float, WarheadKind?, bool, GuidanceKind?, float)[]
                 {
                     (MunitionTkbCatalog.TkbPg7vlRocket, 500, 450, sc, false, GuidanceKind.Unguided, 300),
                     (MunitionTkbCatalog.TkbPg7vrRocket, 650, 500, sc, true, GuidanceKind.Unguided, 280),
                     (MunitionTkbCatalog.TkbLawRocket, 300, 350, sc, false, GuidanceKind.Unguided, 145),
                     (MunitionTkbCatalog.TkbNlawMissile, 500, 900, sc, false, GuidanceKind.FireAndForget, 200),
                     (MunitionTkbCatalog.TkbJavelinMissile, 760, 1500, sc, true, GuidanceKind.FireAndForget, 140),
                     (MunitionTkbCatalog.TkbKornetMissile, 1100, 2000, sc, true, GuidanceKind.Command, 300),
                     (MunitionTkbCatalog.TkbFpvWarhead, 400, 450, sc, false, null, 0),
                     (MunitionTkbCatalog.TkbTankHeatMp, 600, 1200, sc, false, null, 0),
                     (MunitionTkbCatalog.TkbTankHeFrag, 50, 600, WarheadKind.HighExplosive, false, null, 0),
                     (MunitionTkbCatalog.TkbHmgAp, 25, 60, null, false, null, 0),
                 })
        {
            var t = tkb.GetByType(type);
            Assert.Equal(2, t.DisType.Kind);
            Assert.True(MunitionTkbCatalog.IsBuiltIn(type));
            Assert.False(Fdp.Toolkit.Tkb.Reference.ReferenceLibrary.Shared.TryGetAmmo(t.Name, out _), $"{t.Name} matches a library entry by name");
            var b = t.GetDescriptor<AmmoWeaponBallisticsDto>()!;
            Assert.Equal((0L, pen, dmg), (b.WeaponGuid, b.PenetrationMm, b.Damage));
            var w = Fdp.Toolkit.Tkb.Parameters.ParameterResolver.Warhead(tkb, type).Warhead;
            Assert.Equal(kind, w?.Kind);
            Assert.Equal(tandem, w?.Tandem ?? false);
            var m = t.GetDescriptor<MissileDto>();
            Assert.Equal(guidance, m?.Guidance);
            if (m != null) Assert.Equal(speed, m.SpeedMps);
        }
        var javelin = tkb.GetByType(MunitionTkbCatalog.TkbJavelinMissile).GetDescriptor<MissileDto>()!;
        Assert.True(javelin.TopAttack && javelin.TopAttackMinRangeM == 150f && javelin.MinRangeM == 65f);
        var tow = tkb.GetByType(MunitionTkbCatalog.TkbTowMissile).GetDescriptor<MissileDto>()!;
        Assert.Equal((GuidanceKind.Command, 280f, 65f), (tow.Guidance, tow.SpeedMps, tow.MinRangeM));
        Assert.Equal(GuidanceKind.Unguided, tkb.GetByType(MunitionTkbCatalog.TkbRpg7Rocket).GetDescriptor<MissileDto>()!.Guidance);
        Assert.Null(Fdp.Toolkit.Tkb.Parameters.ParameterResolver.Warhead(tkb, MunitionTkbCatalog.Tkb762Ball).Warhead);
        Assert.True(MunitionTkbCatalog.VisualOf(tkb, MunitionTkbCatalog.Tkb762Tracer)?.Tracer == true);
    }

    /// <summary>⭐ LR-3 (§8.5, R-271) — a VIEWER draws a replicated round from nothing but its master and position: the look from its
    /// munition type, the nose from the replicated velocity, a tracer streak back along the flight; at rest it lies. On the node that
    /// runs combat the same round (BallisticProjectile + LightweightRound) is drawn ONCE. Neither the body layer nor the 2-D entity
    /// marker draws it as a unit; the 2-D round projector does.</summary>
    [Fact]
    public void LR3_AViewerDrawsAReplicatedRound_FromItsTypeAndVelocity_AndTheMuscleDrawsItOnce()
    {
        var tkb = Hrot.Map.Common.HrotEnvironment.CreateTkb();
        var world = RoundsWorld(tkb);
        world.RegisterComponent<LightweightRound>();
        world.RegisterComponent<TkbIdentity>();
        Entity Replicated(long type, Vector3 at, Vector3 v)
        {
            var e = world.CreateEntity();
            world.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            world.AddComponent(e, new SimVelocity { Linear = v });
            world.AddComponent(e, new TkbIdentity { TkbType = type });
            world.AddComponent(e, default(LightweightRound));
            return e;
        }
        var tracer = Replicated(MunitionTkbCatalog.Tkb556Tracer, new Vector3(100, 0, 2), new Vector3(900, 0, 0));
        Replicated(MunitionTkbCatalog.TkbM67Grenade, new Vector3(30, 4, 0), Vector3.Zero);                 // lying, no fuze known
        Replicated(MunitionTkbCatalog.Tkb556Ball, new Vector3(50, 0, 2), new Vector3(900, 0, 0));         // never sent; drawn as nothing
        var muscleRound = Round(world, MunitionTkbCatalog.TkbRpg7Rocket, new Vector3(60, 0, 3), new Vector3(120, 0, 0));
        world.AddComponent(muscleRound, new TkbIdentity { TkbType = MunitionTkbCatalog.TkbRpg7Rocket });
        world.AddComponent(muscleRound, default(LightweightRound));
        var layer = new ProjectileLayer(() => world, () => tkb) { GroundHeight = (_, _) => 0.5f };

        var rounds = layer.Resolve();
        Assert.Equal(3, rounds.Count);                                                         // tracer, grenade, rocket once
        var t = Assert.Single(rounds, r => r.Look.Shape == MunitionShape.Bullet);
        Assert.Equal(Vector3.UnitX, t.Nose);
        Assert.True(ProjectileLayer.TracerOf(t, out var head, out var tail) && tail.X < head.X, "the streak trails behind it");
        var g = Assert.Single(rounds, r => r.Look.Shape == MunitionShape.Grenade);
        Assert.True(g.Lying);
        Assert.Equal(0f, g.FuzeLeft);                                                          // a viewer knows no fuze (LR-C)
        Assert.Equal(0.5f, g.Centre.Z);
        Assert.Single(rounds, r => r.Look.Shape == MunitionShape.Rocket);

        Assert.Equal(Hrot.UI.Common.Map3D.VisualFamily.Unit,
                     Hrot.UI.Common.Map3D.EntityBodyLayer3D.Resolve(tkb.GetByType(MunitionTkbCatalog.Tkb556Tracer)).Family);
        Assert.Equal(Hrot.UI.Common.Map3D.VisualFamily.Unit,                                    // a host whose TKB lacks it (replay browser)
                     new Hrot.UI.Common.Map3D.EntityBodyLayer3D(() => null, () => null).LookOf(MunitionTkbCatalog.Tkb556Ball, null).Family);
        var buffer = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer();
        new Hrot.ScenarioEditor.Gizmos.LightweightRoundGizmo().Draw(world, tracer, buffer);
        Assert.Equal(2, buffer.GetFrame().ToArray().Length);                                   // the streak + its glow
        // ⭐ CE-1203 (§5e FL-3) — the Rounds row
        Fdp.TestShared.GizmoLayerProbe.AssertOnItsOwnLayer(d => new Hrot.ScenarioEditor.Gizmos.LightweightRoundGizmo().Draw(world, tracer, d), Fdp.Toolkit.Diagnostics.Gizmos.DebugTraceLayers.Rounds);
        buffer = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer();
        new Hrot.ScenarioEditor.Gizmos.LightweightRoundGizmo().Draw(world, muscleRound, buffer);
        Assert.Empty(buffer.GetFrame().ToArray());                                             // the combat projector draws that one
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
        Fdp.TestShared.GizmoLayerProbe.AssertOnItsOwnLayer(d => gizmo.Draw(world, lying, d), Fdp.Toolkit.Diagnostics.Gizmos.DebugTraceLayers.Rounds);   // ⭐ CE-1203
        Assert.Contains(frame, p => p.Shape == Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveShape.Sphere);
        Assert.Contains(frame, p => p.Shape == Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveShape.Text);
        Assert.Equal("3 s", Hrot.ScenarioEditor.Gizmos.ProjectilePresentationGizmo.FuzeLabel(2.74f));   // a countdown rounds up
        // ⭐ CE-1203 (§5e FL-7) — the streak and the marker are 2-D symbols of a round the 3-D map draws itself: never in 3-D (the 3-D
        //   triage culls FlatOnly); the countdown is no symbol of anything 3-D draws, so it stays in both.
        //   🔴 Red-proof: before FL-7 the marker targeted All and was draped on the ground under the 3-D grenade.
        foreach (var p in frame)
        {
            bool flat = (p.TargetView & Fdp.Toolkit.Diagnostics.Gizmos.PipelineTarget.FlatOnly) != 0;
            Assert.Equal(p.Shape != Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveShape.Text, flat);
        }
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
        var mi = Hrot.ScenarioEditor.Map.MapInteractionPack.Build(new Hrot.ScenarioEditor.Map.MapInteractionContext
        {
            World = world,
        });
        Fdp.Toolkit.Replay.ReplayState.Enter(world);        // ⭐ CE-3329 P2 — the world says it is a replay
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

        Fdp.Toolkit.Replay.ReplayState.Leave(world);        // back to live: the collector runs again
        Tick(world, mi.Lifespan, 0.016f);
        Assert.Empty(Effects(world));
    }

    [Fact]
    public void E3_TheLayerResolvesEachEffectFromItsTkbType_AFlashAtTheMuzzle_ARecordedTracerStreakingAlongItsPath()
    {
        var (world, tkb) = World();
        var tank = Unit(world, Hrot.Map.Common.TkbEntityTypes.Tank_T72, new Vector3(0, 0, 0));
        var target = Unit(world, Hrot.Map.Common.TkbEntityTypes.Tank_M1Abrams, new Vector3(300, 0, 0));
        world.Bus.Publish(new WeaponFireNotification { Shooter = tank, Target = target, WeaponIndex = 0 });
        Tick(world, new EffectSpawnSystem(), 0.016f);
        // ⭐ LR-4 — shots spawn no tracer effect any more; one RECORDED before it (a replay) still draws, so it is made here directly
        var recorded = world.CreateEntity();
        world.AddComponent(recorded, new SimTransform { Position = new Vector3(0, 0, 0), Rotation = Quaternion.Identity });
        world.AddComponent(recorded, new TkbIdentity { TkbType = EffectTkbCatalog.Tracer });
        world.AddComponent(recorded, Lifespan.Starting(0.0, EffectTkbCatalog.BuiltIn[EffectTkbCatalog.Tracer].Visual.Duration));
        world.AddComponent(recorded, new EffectAnchor { Shooter = tank, WeaponIndex = 0, Toward = new Vector3(300, 0, 0) });
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

    /// <summary>⭐ <c>CE-1357</c> — a weapon system fired at a POINT (a door M134: no target unit, no aim posture of its own) draws its burst
    /// stream along its rounds' launch, and a stream with no aim known runs along the gun — never to the map's origin. 🔒 User, 2026-10-25:
    /// "the majority of the stream of tracers is rendered to the corner of the terrain". ✅ Red-proof: StreamToward's launch fallback
    /// dropped ⇒ (0,0,0); Dashes' zero guard dropped ⇒ dashes toward the origin.</summary>
    [Fact]
    public void CE1357_AStationGunsStream_FollowsItsRounds_NeverTheOrigin()
    {
        var origin = new Vector3(116f, 282f, 26f);
        var velocity = new Vector3(-600f, 650f, -150f);
        var toward = Hrot.UI.Common.Effects.EffectSpawnSystem.StreamToward(Vector3.Zero, Vector3.Zero, origin, velocity, 1000f);
        Assert.True(Vector3.Distance(origin, toward) > 900f, $"out along the launch ({toward})");
        Assert.True(Vector3.Dot(Vector3.Normalize(toward - origin), Vector3.Normalize(velocity)) > 0.999f);
        var target = new Vector3(1f, 2f, 3f);
        Assert.Equal(target, Hrot.UI.Common.Effects.EffectSpawnSystem.StreamToward(target, new Vector3(9f, 9f, 9f), origin, velocity, 1000f));   // a unit wins
        Assert.Equal(Vector3.Zero, Hrot.UI.Common.Effects.EffectSpawnSystem.StreamToward(Vector3.Zero, Vector3.Zero, origin, Vector3.Zero, 0f));   // nothing known

        var dashes = new System.Collections.Generic.List<(Vector3 From, Vector3 To)>();
        Hrot.UI.Common.Effects.FireStreams.Dashes(origin, Vector3.Zero, new Vector3(0f, 1f, 0f), 0.2, dashes);
        Assert.NotEmpty(dashes);
        Assert.All(dashes, d => Assert.True(d.To.Y > origin.Y && MathF.Abs(d.To.X - origin.X) < 1e-3f, $"along the gun, not to the origin ({d.To})"));
    }
}
