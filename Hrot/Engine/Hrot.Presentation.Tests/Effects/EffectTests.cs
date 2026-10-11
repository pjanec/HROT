using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Replication.Components;
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

    private static List<(long Type, Entity Entity)> Effects(EntityRepository world)
    {
        var list = new List<(long, Entity)>();
        foreach (var e in world.Query().With<EffectLifetime>().With<TkbIdentity>().Build())
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
        Assert.Equal(10, EffectTkbCatalog.BuiltIn.Count);
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
        Assert.True(world.HasComponent<Fdp.Toolkit.Scenario.ScenarioIgnoreTag>(flash), "VE-B: an effect is never saved with the scenario");
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
        world.Bus.Publish(new DetonationNotification { Target = Entity.Null, HitX = 0, HitY = 0, Damage = 1100f });
        Tick(world, new EffectSpawnSystem(), 0.016f);
        var life = new EffectLifetimeSystem();
        int before = Effects(world).Count;
        for (int i = 0; i < 100; i++) Tick(world, life, 0f);   // paused: dt = 0
        Assert.Equal(before, Effects(world).Count);
        Tick(world, life, 2f);                                  // past the explosion's 1.6 s, well inside the decal's 300 s
        Assert.Equal(new[] { EffectTkbCatalog.DecalLarge }, Effects(world).Select(f => f.Type).ToArray());
    }

    [Fact]
    public void E2_AFlashWhoseShooterIsGone_Ends_AndDecalsAreCappedOldestFirst()
    {
        var (world, _) = World();
        var tank = Unit(world, Hrot.Map.Common.TkbEntityTypes.Tank_T72, Vector3.Zero);
        world.Bus.Publish(new WeaponFireNotification { Shooter = tank, Target = Entity.Null, WeaponIndex = 0 });
        Tick(world, new EffectSpawnSystem(), 0.016f);
        Assert.Single(Effects(world));
        world.DestroyEntity(tank);
        var life = new EffectLifetimeSystem();
        Tick(world, life, 0.001f);
        Assert.Empty(Effects(world));

        for (int i = 0; i < EffectLifetimeSystem.DecalCap + 10; i++)
        {
            var d = world.CreateEntity();
            world.AddComponent(d, new SimTransform());
            world.AddComponent(d, new TkbIdentity { TkbType = EffectTkbCatalog.DecalSmall });
            world.AddComponent(d, new EffectLifetime { Age = i, Duration = 10_000f });   // decal i is i seconds old
        }
        Tick(world, life, 0.001f);
        var ages = Effects(world).Select(f => world.GetComponentRO<EffectLifetime>(f.Entity).Age).ToList();
        Assert.Equal(EffectLifetimeSystem.DecalCap, ages.Count);
        Assert.True(ages.Max() < EffectLifetimeSystem.DecalCap, "the OLDEST ten went");
    }

    [Fact]
    public void E2_EveryMapHostSchedulesTheEffects_ThroughThePacksInteractionSystems()
    {
        var mi = Hrot.ScenarioEditor.Map.MapInteractionPack.Build(new Hrot.ScenarioEditor.Map.MapInteractionContext { World = new EntityRepository() });
        Assert.Contains(mi.EffectSpawn, mi.InteractionSystems);
        Assert.Contains(mi.EffectLifetime, mi.InteractionSystems);
        Assert.Contains(typeof(EffectSpawnSystem), mi.RequiredSystems);
        Assert.Empty(mi.Unserviceable(mi.InteractionSystems));
        // and a host that drives the systems itself but forgets the effects is REPORTED
        Assert.Contains(mi.Unserviceable(new object[] { mi.GlobalManager, mi.DataDrivenSystem, mi.StatelessSystem, mi.ActionDispatch }),
                        m => m.Contains(nameof(EffectSpawnSystem)));
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
