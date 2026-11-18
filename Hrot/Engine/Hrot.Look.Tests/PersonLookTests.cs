using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Replication.Components;
using Hrot.Look.People;

namespace Hrot.Look.Tests;

/// <summary>
/// ⭐⭐ CE-4671 LK-S2 (DESIGN_Vehicle_Appearance_State.md §7 LK-4 / LK-5, §7c) — a person's look called the way a GATEWAY calls it, from a project with no presentation:
/// a soldier and a civilian are dressed (plain colours, no renderer type), the same on every call (LK-5), a vehicle is no person. That it equals what HROT's map
/// draws is the map suite's rail (<c>PersonOutfitsRail.CE4671_LKS2_*</c>).
/// </summary>
public sealed class PersonLookTests
{
    /// <summary>🔒 Red-proved: <c>FactsOf</c>'s family test inverted (a person gets no facts) ⇒ red.</summary>
    [Fact]
    public void LKS2_ASoldierAndACivilian_AreDressed_TheSameOnEveryCall_AVehicleIsNoPerson()
    {
        var tkb = Hrot.Map.Common.HrotEnvironment.CreateTkb();
        using var world = new EntityRepository();
        world.RegisterComponent<TkbIdentity>();
        world.RegisterComponent<NetworkIdentity>();
        world.RegisterComponent<EntityInfo>();
        Entity Man(long type, int net, ForceId side)
        {
            var e = world.CreateEntity();
            world.AddComponent(e, new TkbIdentity { TkbType = (TkbTypeId)type });
            world.AddComponent(e, new NetworkIdentity(net));
            world.AddComponent(e, new EntityInfo { ForceId = side });
            return e;
        }
        var soldier = Man(Hrot.Map.Common.TkbEntityTypes.Infantry_Rifleman, 1201, ForceId.Hostile);
        var civilianType = tkb.GetAll().First(t => PersonLook.FactsOf(t) is { Soldier: false });
        var civilian = Man((long)civilianType.TkbType, 1202, ForceId.Neutral);
        var tank = Man(Hrot.Map.Common.TkbEntityTypes.Tank_T72, 1203, ForceId.Friend);

        var s = PersonLook.Of(world, soldier, tkb);
        Assert.NotNull(s);
        Assert.True(s!.Value.Outfit.Soldier);
        Assert.Equal((1201L, ForceId.Hostile), (s.Value.Seed, s.Value.Side));
        Assert.Equal(s, PersonLook.Of(world, soldier, tkb));                // LK-5: a pure function — the same look on every call
        var c = PersonLook.Of(world, civilian, tkb);
        Assert.NotNull(c);
        Assert.False(c!.Value.Outfit.Soldier);
        Assert.Equal(255, c.Value.Outfit.Skin.A);                            // a plain, opaque colour
        Assert.Null(PersonLook.Of(world, tank, tkb));                        // a vehicle is no person
    }
}
