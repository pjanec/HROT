using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.AI.Behaviors.Brains;
using Hrot.Core.Tkb;
using Hrot.Map.Common;
using Hrot.MuscleCharacter.Animation.Components;
using Xunit;

namespace Hrot.SimHost.Tests;

/// <summary>
/// ⭐ CE-1238 (People Display U7a, R-1023; 🔒 User, `2026-10-21`: "Maybe the guy operating a mortar is crouching next to it usually (for
/// smaller mortars)" — "Best if you do both changes yourself") — a man firing a mortar of his OWN crouches beside it: the brain asks for the
/// stance, so the sim sees and hits him crouched and the 3-D figure draws him so. One rule decides "a mortar"
/// (<see cref="MunitionTkbCatalog.IsMortar"/>), read by the figure and the brain alike: the MortarTeam's 81 mm (no category, a bomb round)
/// and the crewed tubes (category Mortar) are; a rifle, a grenade and a pistol are not. A crewed tube's gunner is not crouched by it — he
/// kneels in the tube's seat.
/// </summary>
public sealed class CrouchBesideOwnMortarTests
{
    private static TkbDatabase BuiltIn()
    {
        var db = new TkbDatabase();
        HrotEnvironment.RegisterBuiltInTkb(db);
        return db;
    }

    [Fact]
    public void CE1238_OneRuleSaysMortar_TheMortarTeamAndTheTubes_NotARifleAGrenadeOrAPistol()
    {
        var db = BuiltIn();
        WeaponMountDto Mount(long type, int i) => db.GetByType(type)!.GetDescriptor<WeaponSuiteDto>()!.Mounts[i];
        Assert.True(MunitionTkbCatalog.IsMortar(db, Mount(UrbanCombatTkbCatalog.TkbMortarTeam, 0)), "the MortarTeam's 81 mm (a bomb, no category)");
        Assert.True(MunitionTkbCatalog.IsMortar(db, Mount(TkbEntityTypes.WeaponSystem_Mortar81, 0)), "the crewed 81 mm tube (category Mortar)");
        Assert.False(MunitionTkbCatalog.IsMortar(db, Mount(UrbanCombatTkbCatalog.TkbInfantrySoldier, 0)), "a rifle");
        Assert.False(MunitionTkbCatalog.IsMortar(db, Mount(UrbanCombatTkbCatalog.TkbInfantrySoldier, 1)), "a pistol");
        Assert.False(MunitionTkbCatalog.IsMortar(db, Mount(UrbanCombatTkbCatalog.TkbGrenadier, 1)), "a hand grenade");
        Assert.False(MunitionTkbCatalog.IsMortar(db, null));
    }

    [Fact]
    public void CE1238_AManFiringHisOwnMortar_Crouches_ARiflemanAndATubesGunnerDoNot()
    {
        using var world = new EntityRepository();
        SimHostComponentRegistry.RegisterAll(world);
        world.SetSingletonManaged<ITkbDatabase>(BuiltIn());
        Entity Man(long type)
        {
            var e = world.CreateEntity();
            world.AddComponent(e, new TkbIdentity { TkbType = type });
            world.AddComponent(e, new StanceIntent { TargetStance = StanceId.Standing });
            return e;
        }
        StanceId Fire(Entity man, byte mount)
        {
            var p = new FireAtPointNodeParams { Mount = mount };
            Assert.Equal(Fbt.NodeStatus.Success, FireAtPointBehavior.CrouchBesideOwnMortar(ref p, man, world));
            return world.GetComponentRO<StanceIntent>(man).TargetStance;
        }

        Assert.Equal(StanceId.Crouched, Fire(Man(UrbanCombatTkbCatalog.TkbMortarTeam), 0));
        Assert.Equal(StanceId.Standing, Fire(Man(UrbanCombatTkbCatalog.TkbInfantrySoldier), 0));   // his rifle
        Assert.Equal(StanceId.Standing, Fire(Man(UrbanCombatTkbCatalog.TkbInfantrySoldier),
                                             (byte)WeaponSystems.ControlledMountBase));             // a crewed tube: he kneels in its seat
    }
}
