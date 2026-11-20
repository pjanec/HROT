using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Animals;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Core.Tkb;
using Hrot.Look.Hud;
using Hrot.UI.Common.Map3D;

namespace Hrot.Look.Tests;

/// <summary>
/// ⭐ CE-11005 (RR-5, docs/designs/vehicle-appearance/DESIGN_Look_Rules_For_Renderers.md) — bodies, gaits, the carriage, the flail and the HUD words, called
/// from a project with NO presentation reference (what a node extension can call). The map calls the same code (moved verbatim) or forwards to it
/// (<c>PossessionStation.Hud</c>), so its own rails (AnimalModelRail, CE-1081 / CE-1085 carriage, MineClearerModelRail, PossessionStationTests) are the
/// proof that nothing it draws or says changed.
/// </summary>
public sealed class BodyRulesTests
{
    /// <summary>RR-5 — the HUD's words: a weapon category's word, a round's TKB name without its trailing " round", a unit's TKB type name.
    /// 🔒 Red-proved: <c>RoundName</c> keeping the suffix ⇒ red.</summary>
    [Fact]
    public void RR5_TheHudWords_AreTheStationsWords()
    {
        Assert.Equal("MG", HudWords.KindOf(WeaponCategory.MachineGun));
        Assert.Equal("CANNON", HudWords.KindOf(WeaponCategory.TankGun));
        Assert.Equal("WEAPON", HudWords.KindOf((WeaponCategory)250));

        var world = new EntityRepository();
        var tkb = Hrot.Map.Common.HrotEnvironment.CreateTkb();
        world.SetSingletonManaged<ITkbDatabase>(tkb);
        world.RegisterComponent<TkbIdentity>();
        long ammo = 0;
        foreach (var t in tkb.GetAll())
            if (t.Name.EndsWith(" round", StringComparison.Ordinal)) { ammo = t.TkbType; break; }
        Assert.NotEqual(0, ammo);
        var name = HudWords.RoundName(world, ammo);
        Assert.False(string.IsNullOrEmpty(name));
        Assert.False(name!.EndsWith(" round", StringComparison.Ordinal));
        Assert.Null(HudWords.RoundName(world, 0));

        var tank = world.CreateEntity();
        world.AddComponent(tank, new TkbIdentity { TkbType = (TkbTypeId)Hrot.Map.Common.TkbEntityTypes.Tank_T72 });
        Assert.False(string.IsNullOrEmpty(HudWords.TypeName(world, tank)));
    }

    /// <summary>RR-5 — a quadruped's gait follows its speed through its species' bands; a flock's birds sit on closed-form offsets (the same at the same
    /// sim time, everywhere); a free man is carried by no one; a mine clearer's flail turns only with the flail active.</summary>
    [Fact]
    public void RR5_GaitsFlocksCarriageAndFlail_AreFunctionsOfReplicatedState()
    {
        var bands = QuadrupedGaits.BandsOf(VisualFamily.Horse);
        Assert.Equal(QuadGait.Stand, QuadrupedGaits.Classify(0f, bands));
        Assert.Equal(QuadGait.Gallop, QuadrupedGaits.Classify(20f, bands));
        Assert.Equal(FlockBirds.Offset(7u, 3, 12.5, FlockMode.Flying, 20), FlockBirds.Offset(7u, 3, 12.5, FlockMode.Flying, 20));

        var world = new EntityRepository();
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<Fdp.Toolkit.Terrain.MineClearing>();
        var man = world.CreateEntity();
        world.AddComponent(man, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
        Assert.Equal(CarriageKind.Free, Carriage.Of(world, man, null).Kind);

        var clearer = world.CreateEntity();
        Assert.False(FlailActivity.IsOn(world, clearer));
        world.AddComponent(clearer, new Fdp.Toolkit.Terrain.MineClearing { Active = 1, Tool = Fdp.Toolkit.Terrain.MineClearingTool.Flail });
        Assert.True(FlailActivity.IsOn(world, clearer));
        world.SetComponent(clearer, new Fdp.Toolkit.Terrain.MineClearing { Active = 1, Tool = Fdp.Toolkit.Terrain.MineClearingTool.Plough });
        Assert.False(FlailActivity.IsOn(world, clearer));
    }
}
