using Hrot.Map.Definitions.Behavior;
using Hrot.Map.Definitions.Behavior.Orders;

namespace Hrot.Map.Common.Tests;

/// <summary>
/// ⭐⭐ <c>CE-2134</c> (B2, <c>docs/DESIGN_Orders_Params_Entity_Classes.md</c> §5b) — the ONE scan of the params contract: a
/// behaviour declares its DTO, several behaviours may share one DTO, an order DTO carries its order name, and the load check
/// refuses a nameless contract or a second declaration of one behaviour.
/// </summary>
public class ParamsDtoRegistryTests
{
    [Order("TestMove")]
    private struct TestMoveOrder { public float X; }

    private static (BehaviorAttribute, string) Decl(string name, System.Type? dto, BehaviorCategory cats = BehaviorCategory.None)
        => (new BehaviorAttribute(name) { Params = dto, Categories = cats }, "test");

    [Fact]
    public void SeveralBehaviours_ShareOneDto_AndAnOrderDtoCarriesItsName()
    {
        var r = ParamsDtoRegistry.FromDeclarations(
            new[] { Decl("TankMove", typeof(TestMoveOrder)), Decl("SquadMove", typeof(TestMoveOrder)), Decl("Idle", typeof(NoParams)) },
            new[] { typeof(TestMoveOrder) });

        var move = Assert.Single(r.Dtos, d => d.Type == typeof(TestMoveOrder));
        Assert.Equal("TestMove", move.OrderName);
        Assert.Equal(new[] { "SquadMove", "TankMove" }, move.BehaviorNames);
        Assert.Null(Assert.Single(r.Dtos, d => d.Type == typeof(NoParams)).OrderName);
        Assert.True(r.TryGetBehavior("TankMove", out var tank) && tank.ParamsType == typeof(TestMoveOrder));
    }

    [Fact]
    public void AnOrderDto_NoBehaviourTakesYet_IsStillListed()
    {
        var r = ParamsDtoRegistry.FromDeclarations(System.Array.Empty<(BehaviorAttribute, string)>(), new[] { typeof(TestMoveOrder) });
        var only = Assert.Single(r.Dtos);
        Assert.Equal("TestMove", only.OrderName);
        Assert.Empty(only.BehaviorNames);
    }

    [Fact]
    public void TheLoadCheck_RefusesADeclarationWithNoParams_AndASecondDeclaration()
    {
        var none = Assert.Throws<System.InvalidOperationException>(() =>
            ParamsDtoRegistry.FromDeclarations(new[] { Decl("Lazy", null) }, System.Type.EmptyTypes));
        Assert.Contains("NoParams", none.Message);

        var twice = Assert.Throws<System.InvalidOperationException>(() =>
            ParamsDtoRegistry.FromDeclarations(new[] { Decl("Dup", typeof(NoParams)), Decl("Dup", typeof(TestMoveOrder)) }, System.Type.EmptyTypes));
        Assert.Contains("declared twice", twice.Message);
    }

    [Fact]
    public void NamesWithCategory_FiltersByTheDeclaredCategories()
    {
        var r = ParamsDtoRegistry.FromDeclarations(new[]
        {
            Decl("A", typeof(NoParams), BehaviorCategory.Infantry | BehaviorCategory.Insurgent),
            Decl("B", typeof(NoParams), BehaviorCategory.Commander),
        }, System.Type.EmptyTypes);
        Assert.Equal(new[] { "A" }, r.NamesWithCategory(BehaviorCategory.Insurgent));
        Assert.Equal(new[] { "B" }, r.NamesWithCategory(BehaviorCategory.Commander));
        Assert.Empty(r.NamesWithCategory(BehaviorCategory.None));
    }

    /// <summary>⭐ The reflection scan finds an <c>[assembly: Behavior]</c> (this test assembly declares three, for the catalogue).</summary>
    [Fact]
    public void TheScan_FindsAssemblyLevelDeclarations()
    {
        var r = ParamsDtoRegistry.Scan(new[] { typeof(ParamsDtoRegistryTests).Assembly });
        Assert.True(r.TryGetBehavior(BehaviorNames.Ambush, out var ambush));
        Assert.Equal(typeof(AmbushParamsJsonDto), ambush.ParamsType);
        Assert.Equal(BehaviorCategory.Insurgent, ambush.Categories);
    }
}
