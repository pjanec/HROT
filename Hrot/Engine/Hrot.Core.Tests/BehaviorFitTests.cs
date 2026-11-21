using Fdp.Core;
using Hrot.Core.Tkb;
using Hrot.Map.Definitions.Behavior;
using Hrot.Map.Definitions.Behavior.Orders;

namespace Hrot.Map.Common.Tests;

/// <summary>
/// ⭐⭐ <c>CE-4350</c> (docs/DESIGN_Orders_Params_Entity_Classes.md §14) — <see cref="BehaviorFit"/>: the classes of a behaviour (declared, else its order
/// name's class prefix, else its categories) against an entity's DIS class chain, the component AND, and "no class ⇒ an authored asset ⇒ fits everyone".
/// The shipped behaviours × the shipped TKB types are pinned by the golden (<c>Hrot.Editor.Tests</c>, <c>TheOfferedBehavioursFitTheEntityTests</c>).
/// </summary>
public class BehaviorFitTests
{
    [Order("Frob")] private struct FrobOrder { public float X; }
    private struct Gun { public int Rounds; }
    private struct Hull { public int Armour; }

    private static DISEntityType Dis(string text) { Assert.True(DISEntityType.TryParse(text, out var t)); return t; }

    private static readonly DISEntityType Tank = Dis("1.1.225.1.1.0.0");     // Platform · Land · Tank
    private static readonly DISEntityType Man = Dis("3.1.225.1.1.0.0");      // Life form · Land · Infantry
    private static readonly DISEntityType Squad = Dis("11.1.225.1.2.1.0");   // Unit · Land · InfantrySquad

    private static (BehaviorAttribute, string) Decl(string name, System.Type dto, BehaviorCategory cats = BehaviorCategory.None, string[]? classes = null, System.Type[]? requires = null)
        => (new BehaviorAttribute(name) { Params = dto, Categories = cats, Classes = classes, Requires = requires }, "test");

    private static ParamsDtoRegistry Registry(params (BehaviorAttribute, string)[] d) => ParamsDtoRegistry.FromDeclarations(d, new[] { typeof(FrobOrder) });

    private static readonly EntityClassTable Table = EntityClassTable.Default;
    private static readonly System.Func<Entity, System.Type, bool> None = (_, _) => false;

    private static bool Fits(ParamsDtoRegistry r, DISEntityType dis, string name, System.Func<Entity, System.Type, bool>? probe = null, long tkb = 0)
        => BehaviorFit.Fits(r, Table, dis, tkb, name, probe, default);

    [Fact]
    public void ADeclaredClassList_IsAnyOf_AgainstTheDisChain()
    {
        var r = Registry(Decl("Drill", typeof(NoParams), classes: new[] { "Tank", "Infantry" }));
        Assert.True(Fits(r, Tank, "Drill"));
        Assert.True(Fits(r, Man, "Drill"));
        Assert.False(Fits(r, Squad, "Drill"));
    }

    [Fact]
    public void AnOrderBehaviour_TakesItsClassFromTheNamePrefix_TheSplitTheOrderResolverIsTheInverseOf()
    {
        var r = Registry(Decl("PersonFrob", typeof(FrobOrder)), Decl("PlatformFrob", typeof(FrobOrder)), Decl("NopeFrob", typeof(FrobOrder), BehaviorCategory.Civilian));
        Assert.Equal(new[] { "Person" }, BehaviorFit.ClassesOf(r, Table, "PersonFrob"));
        Assert.True(Fits(r, Man, "PersonFrob"));
        Assert.False(Fits(r, Man, "PlatformFrob"));
        Assert.True(Fits(r, Tank, "PlatformFrob"));
        // a prefix that is no class falls through to the categories — never to a class nobody has
        Assert.Equal(new[] { "Civilian" }, BehaviorFit.ClassesOf(r, Table, "NopeFrob"));
        Assert.Equal("PersonFrob", OrderResolver.BehaviourName("Person", "Frob"));
        Assert.Equal("Person", OrderResolver.ClassOfBehaviour(Table, "PersonFrob", "Frob"));
        Assert.Null(OrderResolver.ClassOfBehaviour(Table, "Frob", "Frob"));
    }

    [Fact]
    public void TheCategories_AreTranslatedOnce_ToClasses()
    {
        var r = Registry(
            Decl("Foot", typeof(NoParams), BehaviorCategory.Infantry | BehaviorCategory.Insurgent),
            Decl("Wheels", typeof(NoParams), BehaviorCategory.MilitaryApc),
            Decl("Boss", typeof(NoParams), BehaviorCategory.Commander),
            Decl("Civvy", typeof(NoParams), BehaviorCategory.Civilian));
        Assert.True(Fits(r, Man, "Foot")); Assert.False(Fits(r, Tank, "Foot")); Assert.False(Fits(r, Squad, "Foot"));
        Assert.True(Fits(r, Tank, "Wheels")); Assert.False(Fits(r, Man, "Wheels"));
        Assert.False(Fits(r, Dis("1.1.0.81.0.0.0"), "Wheels"));   // a civilian car (Car, 1.1.*.81) is no military vehicle
        Assert.True(Fits(r, Squad, "Boss")); Assert.False(Fits(r, Tank, "Boss"));
        Assert.False(Fits(r, Man, "Civvy"));
        Assert.True(Fits(r, Dis("3.1.0.2.0.0.0"), "Civvy"));
    }

    [Fact]
    public void ABehaviourWithNoClassAtAll_IsAnAuthoredAsset_AndFitsEveryone()
    {
        var r = Registry(Decl("Plain", typeof(NoParams)));
        Assert.Empty(BehaviorFit.ClassesOf(r, Table, "Plain"));
        Assert.Empty(BehaviorFit.ClassesOf(r, Table, "NotDeclaredAtAll"));
        foreach (var dis in new[] { Tank, Man, Squad, default })
        {
            Assert.True(Fits(r, dis, "Plain"));
            Assert.True(Fits(r, dis, "NotDeclaredAtAll"));
        }
    }

    [Fact]
    public void ObjectiveAndTrigger_HaveNoDis_SoTheyMatchTheirTkbType()
    {
        var r = Registry(Decl("Reach", typeof(NoParams), BehaviorCategory.Objective), Decl("Fire", typeof(NoParams), BehaviorCategory.Trigger));
        Assert.True(Fits(r, default, "Reach", tkb: TkbEntityTypes.Objective));
        Assert.False(Fits(r, default, "Reach", tkb: TkbEntityTypes.Trigger));
        Assert.True(Fits(r, default, "Fire", tkb: TkbEntityTypes.Trigger));
        Assert.False(Fits(r, Man, "Reach"));
    }

    [Fact]
    public void RequiredComponents_AreTheOneAnd_AndNoProbeMeansNotMet()
    {
        var r = Registry(Decl("Shoot", typeof(NoParams), classes: new[] { "Infantry" }, requires: new[] { typeof(Gun), typeof(Hull) }));
        Assert.True(Fits(r, Man, "Shoot", (_, _) => true));
        Assert.False(Fits(r, Man, "Shoot", (_, t) => t == typeof(Gun)));   // one of two missing
        Assert.False(Fits(r, Man, "Shoot", None));
        Assert.False(Fits(r, Man, "Shoot", probe: null));
        Assert.False(Fits(r, Tank, "Shoot", (_, _) => true));              // the class still decides
        Assert.True(ComponentRequirements.Met((_, _) => false, default, System.Array.Empty<System.Type>()));
    }
}
