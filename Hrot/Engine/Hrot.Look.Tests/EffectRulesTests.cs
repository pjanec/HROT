using System.Numerics;
using System.Reflection;
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
/// ⭐⭐ CE-4671 LK-S1 (DESIGN_Vehicle_Appearance_State.md §7 LK-1 / LK-2, §7b) — the effect rules called the way a GATEWAY calls them: from a project that
/// references <c>Hrot.Look</c> and no presentation assembly, on a node's world and its events. The same cases as the map's own rails
/// (<c>EffectTests.E2_*</c>), so the map and a gateway agree by construction.
/// </summary>
public sealed class EffectRulesTests
{
    private static (EntityRepository World, ITkbDatabase Tkb) World()
    {
        var world = new EntityRepository();
        var tkb = Hrot.Map.Common.HrotEnvironment.CreateTkb();
        world.SetSingletonManaged<ITkbDatabase>(tkb);
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<TkbIdentity>();
        world.RegisterComponent<EffectAnchor>();
        world.RegisterEvent<DetonationNotification>();
        world.RegisterEvent<WeaponFireNotification>();
        world.RegisterEvent<RoundImpactNotification>();
        world.RegisterEvent<Fdp.Toolkit.Terrain.ContactBreachEvent>();
        return (world, tkb);
    }

    private static Entity Unit(EntityRepository world, long type, Vector3 at)
    {
        var e = world.CreateEntity();
        world.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
        world.AddComponent(e, new TkbIdentity { TkbType = (TkbTypeId)type });
        return e;
    }

    /// <summary>LK-1 — the library and everything it pulls in carry NO presentation: no Raylib, Stride, ImGui, <c>Hrot.UI</c> or <c>Hrot.Presentation</c>.
    /// 🔒 Red-proved: a Raylib package reference added to <c>Hrot.Look</c> (and a type of it used) ⇒ red.</summary>
    [Fact]
    public void LK1_TheLookLibrary_PullsInNoPresentationAssembly()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var todo = new Stack<AssemblyName>();
        todo.Push(typeof(EffectRules).Assembly.GetName());
        while (todo.Count > 0)
        {
            var name = todo.Pop();
            if (!seen.Add(name.Name!)) continue;
            Assembly a;
            try { a = Assembly.Load(name); } catch (FileNotFoundException) { continue; }   // a framework facade not in the output
            foreach (var r in a.GetReferencedAssemblies())
                if (!r.Name!.StartsWith("System", StringComparison.Ordinal) && !r.Name.StartsWith("Microsoft", StringComparison.Ordinal)
                    && r.Name != "netstandard" && r.Name != "mscorlib")
                    todo.Push(r);
        }
        string[] banned = ["Raylib", "Stride", "ImGui", "Hrot.UI", "Hrot.Presentation"];
        var bad = seen.Where(n => banned.Any(b => n.StartsWith(b, StringComparison.OrdinalIgnoreCase))).ToList();
        Assert.True(bad.Count == 0, "the look library pulls in presentation: " + string.Join(", ", bad));
        Assert.Contains("Hrot.Look", seen);
    }

    /// <summary>
    /// LK-2 — a shot: a large muzzle flash ATTACHED to the shooter's weapon toward its target (VE-J), our category (a muzzle flash, large, in the air) and the
    /// round's DIS identity; a detonation on the ground: an explosion and a mark ON the ground under it; one on a vehicle: an explosion and no mark (VE-M).
    /// The lifetimes and sizes are the TKB effect types' own. 🔒 Red-proved: the flash spec built with no anchor ⇒ red.
    /// </summary>
    [Fact]
    public void LKS1_AShot_AnAttachedFlash_AHitOnTheGround_AnExplosionAndAMark_AHitOnAVehicle_NoMark()
    {
        var (world, tkb) = World();
        var tank = Unit(world, Hrot.Map.Common.TkbEntityTypes.Tank_T72, new Vector3(0, 0, 3));
        var target = Unit(world, Hrot.Map.Common.TkbEntityTypes.Tank_M1Abrams, new Vector3(800, 0, 5));
        world.Bus.Publish(new WeaponFireNotification { Shooter = tank, Target = target, WeaponIndex = 0 });
        world.Bus.Publish(new DetonationNotification { Target = Entity.Null, HitX = 50, HitY = 20, HitZ = 0.4f, Damage = 1100f });
        world.Bus.Publish(new DetonationNotification { Target = tank, HitX = 0, HitY = 0, HitZ = 1.5f, Damage = 25f });
        world.Bus.SwapBuffers();

        var specs = new List<EffectSpec>();
        EffectRules.ForFrame(world, tkb, specs);

        var flash = Assert.Single(specs, s => s.EffectType == EffectTkbCatalog.MuzzleFlashLarge);
        Assert.NotNull(flash.Anchor);
        Assert.Equal(tank, flash.Anchor!.Value.Shooter);
        Assert.Equal(new Vector3(800, 0, 5), (Vector3)flash.Anchor.Value.Toward);
        Assert.Equal((Fdp.Toolkit.Tkb.Domain.EffectKind.MuzzleFlash, EffectSurface.Air), (flash.Kind, flash.Surface));
        Assert.Equal(EffectTkbCatalog.VisualOf(tkb, EffectTkbCatalog.MuzzleFlashLarge)!.Duration, flash.Lifetime);
        Assert.DoesNotContain(specs, s => s.EffectType == EffectTkbCatalog.Tracer);   // ⛔ LR-4

        var big = Assert.Single(specs, s => s.EffectType == EffectTkbCatalog.ExplosionLarge);
        Assert.Equal((EffectTkbCatalog.CalibreClass.Large, EffectSurface.Ground), (big.SizeClass, big.Surface));
        var mark = Assert.Single(specs, s => s.EffectType == EffectTkbCatalog.DecalLarge);
        Assert.Equal(new Vector3(50, 20, 0), mark.At);                                 // on the ground under the burst (no terrain here: z 0)
        var onTank = Assert.Single(specs, s => s.EffectType == EffectTkbCatalog.ExplosionSmall);
        Assert.Equal(EffectSurface.Vehicle, onTank.Surface);
        Assert.DoesNotContain(specs, s => s.EffectType == EffectTkbCatalog.DecalSmall);   // VE-M: no mark on a body
        // ⭐ CE-11010 (RR-10) — each effect a HIT made says what it struck (the explosion on the tank names the tank; the ground burst names none); a shot's flash says nothing
        Assert.Equal((tank, (byte)EffectSurface.Vehicle, 25f), (onTank.Cause!.Value.Target, onTank.Cause.Value.Surface, onTank.Cause.Value.Damage));
        Assert.Equal((Entity.Null, (byte)EffectSurface.Ground, 1100f), (big.Cause!.Value.Target, big.Cause.Value.Surface, big.Cause.Value.Damage));
        Assert.Equal(Entity.Null, mark.Cause!.Value.Target);
        Assert.Null(flash.Cause);
        // ⭐ the order the map makes them: the shot's flash, then each detonation's explosion and its mark
        Assert.Equal([EffectTkbCatalog.MuzzleFlashLarge, EffectTkbCatalog.ExplosionLarge, EffectTkbCatalog.DecalLarge, EffectTkbCatalog.ExplosionSmall],
                     specs.Select(s => s.EffectType).ToArray());
    }

    /// <summary>⭐ CE-4635 (flame weapons §8 U-5, the <c>EffectRules</c> row) — a fuel shot from a FLAMETHROWER mount makes no muzzle flash and no blast (the jet is its look; it fires
    /// 10×/s), while a rifle's shot from the same kind of soldier still flashes. 🔒 Red-proved: the Flamethrower early-out removed ⇒ the flamethrower's shot flashes.</summary>
    [Fact]
    public void CE4635_AFlamethrowersFuelShot_MakesNoFlash_ARiflesShotStillDoes()
    {
        var (world, tkb) = World();
        const long Flamer = 0x6C11, Fuel = 0x6C12, Rifle = 0x6C13, Bullet = 0x6C14;
        var db = (Fdp.Toolkit.Tkb.TkbDatabase)tkb;
        var f = new TkbTemplate("test flamer", Flamer);
        f.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts = { new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.Flamethrower, AmmoGuid = Fuel, InitialAmmunition = 100 } } });
        db.Register(f); db.Register(new TkbTemplate("test fuel", Fuel));
        var r = new TkbTemplate("test rifleman", Rifle);
        r.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts = { new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.SmallArms, AmmoGuid = Bullet, InitialAmmunition = 30, DamagePerHit = 25f } } });
        db.Register(r); db.Register(new TkbTemplate("test bullet", Bullet));
        var flamer = Unit(world, Flamer, new Vector3(0, 0, 0));
        var rifle = Unit(world, Rifle, new Vector3(10, 0, 0));
        world.Bus.Publish(new WeaponFireNotification { Shooter = flamer, WeaponIndex = 0 });
        world.Bus.SwapBuffers();
        var specs = new List<EffectSpec>();
        EffectRules.ForFrame(world, tkb, specs);
        Assert.Empty(specs);                                            // no flash, no blast, nothing

        world.Bus.Publish(new WeaponFireNotification { Shooter = rifle, WeaponIndex = 0 });
        world.Bus.SwapBuffers();
        specs.Clear();
        EffectRules.ForFrame(world, tkb, specs);
        Assert.Contains(specs, s => s.Kind == Fdp.Toolkit.Tkb.Domain.EffectKind.MuzzleFlash);
    }

    /// <summary>LK-2 — 0 B on the steady path: the rules on a frame with no events allocate nothing (R-2084). 🔒 Red-proved: a list made per call in
    /// <c>ForFrame</c> ⇒ red.</summary>
    [Fact]
    public void LKS1_AFrameWithNoEvents_AllocatesNothing()
    {
        var (world, tkb) = World();
        var specs = new List<EffectSpec>(16);
        world.Bus.SwapBuffers();
        EffectRules.ForFrame(world, tkb, specs);   // warm
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) EffectRules.ForFrame(world, tkb, specs);
        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Empty(specs);
    }

    /// <summary>⭐ CE-11009 (RR-9) — a terrain material's name folds to the impact's material through its thermal class (the shared library), unknown names to
    /// Unknown; an impact's material rides on every effect it makes (RR-10). 🔒 Red-proved: the impact's Cause without the material ⇒ red.</summary>
    [Fact]
    public void RR9_AnImpactsMaterial_IsTheTerrainMaterialsThermalClass_AndRidesOnItsEffects()
    {
        Assert.Equal(Fdp.Toolkit.Combat.ImpactMaterial.Concrete, Fdp.Toolkit.Combat.ImpactMaterials.Of("concrete"));
        Assert.Equal(Fdp.Toolkit.Combat.ImpactMaterial.Unknown, Fdp.Toolkit.Combat.ImpactMaterials.Of("no-such-material"));
        Assert.Equal(Fdp.Toolkit.Combat.ImpactMaterial.Vegetation, Fdp.Toolkit.Combat.ImpactMaterials.OfThermalClass("dense-vegetation"));

        var (world, tkb) = World();
        var hit = new RoundImpactNotification { X = 5, Y = 5, Z = 0, NormalZ = 1, TravelX = 1, Kind = 2, Material = (byte)Fdp.Toolkit.Combat.ImpactMaterial.Brick };
        var specs = new List<EffectSpec>();
        Assert.True(EffectRules.ForImpact(in hit, world, tkb, specs) > 0);
        Assert.All(specs, sp => Assert.Equal((byte)Fdp.Toolkit.Combat.ImpactMaterial.Brick, sp.Cause!.Value.Material));
    }
}
