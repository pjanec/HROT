using Fdp.Core;
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
        Assert.Equal(typeof(NoParams), ambush.ParamsType);
        Assert.Equal(BehaviorCategory.Insurgent, ambush.Categories);
    }

    // ── CE-3185 (P1, §13.7) — one registry for every authored data struct, with purposes and origin ──────────────────────

    [ParamsDto] private struct CE3185_Speed { public float Value; }
    [Dto] private struct CE3185_Plain { public int A; }
    [UnitMemory] private struct CE3185_Memory { public int Uses; }

    /// <summary>⭐ The one scan gives each DTO its purposes: <c>[Dto]</c> → a variable type only, <c>[UnitMemory]</c> → a unit memory,
    /// <c>[ParamsDto]</c> → a params DTO; only params DTOs are in <see cref="ParamsDtoRegistry.Dtos"/>, every one is in
    /// <see cref="ParamsDtoRegistry.All"/>; a hand-written one's origin is this file at the attribute's line.</summary>
    [Fact]
    public void CE3185_OneScan_RecordsPurposesAndOrigin()
    {
        var r = ParamsDtoRegistry.FromDeclarations(System.Array.Empty<(BehaviorAttribute, string)>(),
            new[] { typeof(CE3185_Speed), typeof(CE3185_Plain), typeof(CE3185_Memory), typeof(TestMoveOrder) });

        Assert.Equal(DtoPurpose.Variable | DtoPurpose.Params, Get(r, typeof(CE3185_Speed)).Purposes);
        Assert.Equal(DtoPurpose.Variable, Get(r, typeof(CE3185_Plain)).Purposes);
        Assert.Equal(DtoPurpose.Variable | DtoPurpose.UnitMemory, Get(r, typeof(CE3185_Memory)).Purposes);
        Assert.Equal(DtoPurpose.Variable | DtoPurpose.Params | DtoPurpose.Order, Get(r, typeof(TestMoveOrder)).Purposes);
        Assert.Equal(new[] { typeof(CE3185_Speed), typeof(TestMoveOrder) }.OrderBy(t => t.FullName), r.Dtos.Select(d => d.Type));
        Assert.Equal(4, r.All.Count);

        var origin = Get(r, typeof(CE3185_Speed)).Origin;
        Assert.False(origin.IsAsset);
        Assert.EndsWith("ParamsDtoRegistryTests.cs", origin.SourceFile!.Replace('\\', '/'));
        Assert.True(origin.SourceLine > 0);
    }

    /// <summary>⭐ Two params DTOs with one SIMPLE name are a load error — menus and order names show the simple name (F1).
    /// A plain <c>[Dto]</c> may share a name; only params DTOs are checked.</summary>
    [Fact]
    public void CE3185_TwoParamsDtos_OneSimpleName_AreRefused()
    {
        // ⚠ The twin lives in a DYNAMIC assembly: a second [ParamsDto] CE3185_Speed in this test assembly would make every scan of
        //   it throw (measured: 9 reds) — the scan skips dynamic assemblies, FromDeclarations does not.
        var clash = Assert.Throws<System.InvalidOperationException>(() => ParamsDtoRegistry.FromDeclarations(
            System.Array.Empty<(BehaviorAttribute, string)>(), new[] { typeof(CE3185_Speed), DynamicParamsDto("Other.CE3185_Speed") }));
        Assert.Contains("CE3185_Speed", clash.Message);
    }

    private static System.Type DynamicParamsDto(string fullName)
    {
        var asm = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
            new System.Reflection.AssemblyName("CE3185_" + System.Guid.NewGuid().ToString("N")), System.Reflection.Emit.AssemblyBuilderAccess.Run);
        var tb = asm.DefineDynamicModule("m").DefineType(fullName,
            System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Sealed | System.Reflection.TypeAttributes.SequentialLayout,
            typeof(System.ValueType));
        tb.SetCustomAttribute(new System.Reflection.Emit.CustomAttributeBuilder(
            typeof(ParamsDtoAttribute).GetConstructor(new[] { typeof(string), typeof(int) })!, new object[] { "", 0 }));
        return tb.CreateType();
    }

    private static ParamsDtoInfo Get(ParamsDtoRegistry r, System.Type t)
    {
        Assert.True(r.TryGetDto(t, out var info), $"{t.Name} is not in the registry");
        return info;
    }
}
