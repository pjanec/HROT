using System.Linq;
using Fdp.Toolkit.Behavior;
using Hrot.AI.Behaviors.Brains.Objectives;
using Hrot.CGF.Configuration;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.SimHost.Tests;

/// <summary>⭐ <c>CE-2377</c> step 1 — <see cref="BehaviorParamsValidation"/>: unknown behaviour, strict unknown key, the Trigger's validator, a good params. ⛔ No world anywhere.</summary>
public sealed class BehaviorParamsValidationTests
{
    private static BehaviorRegistry Production()
    {
        var r = new BehaviorRegistry();
        CgfBehaviorSetup.LoadFromAiAssembly(r);
        return r;
    }

    private const string GoodWhen = "{\"$type\":\"ScenarioTime\",\"fromSeconds\":1}";

    [Fact]
    public void CE2377_AnUnknownBehaviourNameIsAProblem()
    {
        var p = BehaviorParamsValidation.Validate(Production(), "NoSuchBehaviour_CE2377", "{}");
        Assert.Contains(p, s => s.Contains("unknown behaviour 'NoSuchBehaviour_CE2377'"));
    }

    [Fact]
    public void CE2377_AnEmptyOrNullParamsIsFine()
    {
        var r = Production();
        Assert.Empty(BehaviorParamsValidation.Validate(r, "MoveToLocation", null));
        Assert.Empty(BehaviorParamsValidation.Validate(r, "MoveToLocation", ""));
    }

    [Fact]
    public void CE2377_GoodParamsPass_AndAnUnknownKeyIsNamed()
    {
        var r = Production();
        Assert.Empty(BehaviorParamsValidation.Validate(r, "MoveToLocation", "{\"speed\":2,\"arrivalRadius\":1,\"target\":[102,104,0]}"));
        var p = BehaviorParamsValidation.Validate(r, "MoveToLocation", "{\"speed\":2,\"noSuchKeyCE2377\":1}");
        Assert.Single(p);
        Assert.Contains("noSuchKeyCE2377", p[0]);
    }

    [Fact]
    public void CE2377_TheTriggerValidator_AcceptsAGoodTrigger()
    {
        var json = "{\"when\":" + GoodWhen + ",\"do\":[]}";
        Assert.Empty(BehaviorParamsValidation.Validate(Production(), BehaviorNames.Trigger, json));
    }

    [Fact]
    public void CE2377_TheTriggerValidator_RefusesABadDo_AndAMissingWhen()
    {
        var r = Production();
        var bad = BehaviorParamsValidation.Validate(r, BehaviorNames.Trigger, "{\"when\":" + GoodWhen + ",\"do\":[{\"act\":\"NoSuchAct_CE2377\"}]}");
        Assert.Contains(bad, s => s.Contains("'do' is invalid"));
        var noWhen = BehaviorParamsValidation.Validate(r, BehaviorNames.Trigger, "{\"do\":[]}");
        Assert.Contains(noWhen, s => s.Contains("'when' is required"));
    }

    /// <summary>
    /// ⭐⭐ agg-behaviors <c>R-8300</c> (DESIGN_Stochastic_Units_And_Resolution.md §23.5) — THE CONTRACT RAIL: for EVERY registered play whose order kind
    /// has a neutral <c>[Order(K)]</c> DTO (its <c>Fulfils</c>, else its params DTO's <c>[Order]</c> — <c>ParamsDtoRegistry.OrderNameOf</c>), a FULLY
    /// populated neutral DTO (every float / bool / int / enum / bearing / point field non-default; entity-link fields stay default — an entity ref has no
    /// literal) validates STRICTLY against that play's params ⇒ no problems. So the receiver resolving <c>Order.K</c> to ANY fulfilling play reads every
    /// neutral field. Measured green over all 7 kinds / 26 plays; not vacuous (Defend 3 plays, BreakContact 1, Move ≥ 4).
    /// ✅ Red-proof: a neutral field removed from one play's DTO (<c>CompanyDefendParamsJsonDto.FallBackTo</c>) ⇒ red.
    /// </summary>
    [Fact]
    public void R8300_EveryPlayThatFulfilsAnOrderKind_AcceptsTheKindsNeutralDto_Strictly()
    {
        var registry = Production();
        var defaults = ParamsDtoRegistry.Default;
        var perKind = new System.Collections.Generic.Dictionary<string, int>();
        foreach (var neutral in typeof(Hrot.AI.Behaviors.Orders.OrderKind).Assembly.GetTypes())
        {
            var order = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<Hrot.Map.Definitions.Behavior.Orders.OrderAttribute>(neutral);
            if (order == null) continue;
            var dto = System.Activator.CreateInstance(neutral)!;
            foreach (var f in neutral.GetFields())
            {
                object? v = f.FieldType == typeof(float) ? 7f : f.FieldType == typeof(int) ? 3 : f.FieldType == typeof(bool) ? true
                    : f.FieldType == typeof(Fdp.Core.Bearing) ? new Fdp.Core.Bearing(45f)
                    : f.FieldType == typeof(Fdp.Toolkit.Replication.PointLink) ? (Fdp.Toolkit.Replication.PointLink)new System.Numerics.Vector3(11f, 22f, 3f)
                    : f.FieldType.IsEnum ? System.Enum.GetValues(f.FieldType).GetValue(System.Enum.GetValues(f.FieldType).Length - 1)
                    : f.FieldType == typeof(Fdp.Toolkit.Replication.EntityLink) ? null
                    : throw new Xunit.Sdk.XunitException($"{neutral.Name}.{f.Name}: the rail has no non-default value for {f.FieldType.Name}");
                if (v != null) f.SetValue(dto, v);
            }
            string json = System.Text.Json.JsonSerializer.Serialize(dto, neutral, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed);
            foreach (var play in defaults.Behaviors.Where(b => defaults.OrderNameOf(b.Name) == order.Name).Select(b => b.Name))
            {
                perKind[order.Name] = perKind.GetValueOrDefault(order.Name) + 1;
                var problems = BehaviorParamsValidation.Validate(registry, play, json);
                Assert.True(problems.Count == 0, $"{play} (fulfils {order.Name}) refuses the neutral {neutral.Name}: " + string.Join("; ", problems) + "  JSON=" + json);
            }
        }
        Assert.Equal(4, perKind["Defend"]);          // SquadDefend, PlatoonDefend, CompanyDefend + the stochastic unit's StochasticUnitDefend
        Assert.Equal(3, perKind["BreakContact"]);   // BreakContact + the stochastic unit's StochasticUnitBreakContact (ST-2) + ⭐ CE-8307 the relay (UnitRelayBreakContact)
        Assert.True(perKind["Move"] >= 4 && perKind["Attack"] >= 4 && perKind["Hold"] >= 4, "the walk finds the plays (not vacuous)");
    }

    /// <summary>⭐⭐ <c>X-2</c> X2-C2 (R-8356, R-8302 "built from Fulfils + the echelon class") — every PHYSICAL fallback row of the routing table names a
    /// production play that FULFILS the row's order, and is needed: the <c>&lt;Class&gt;&lt;Order&gt;</c> name rule finds nothing for that class and order
    /// (a row the name rule resolves would be dead — a data error). ✅ Red-proof: a row <c>(InfantrySquad, Physical, Hold) → SquadHold</c> added ⇒ the
    /// name rule resolves <c>InfantrySquadHold</c> ⇒ red.</summary>
    [Fact]
    public void X2C2_EveryPhysicalFallbackRow_FulfilsItsOrder_AndTheNameRuleFindsNothing()
    {
        var registry = Production();
        var defaults = ParamsDtoRegistry.Default;
        int n = 0;
        foreach (var row in Hrot.Core.Tkb.ResolutionRoutingTable.Default.Rows.Where(r => r.Resolution == Fdp.Toolkit.Aggregate.AggregateResolution.Physical))
        {
            n++;
            Assert.True(registry.TryGetId(row.Behaviour, out _), row.Behaviour + " is a production play");
            Assert.True(defaults.TryGetBehavior(row.Behaviour, out var decl), row.Behaviour);
            Assert.Equal(row.Order, decl.Fulfils);
            var cls = Hrot.Core.Tkb.EntityClassTable.Default.Find(row.Class)!;
            // ⭐ CE-8307 — the row's echelon class: squad 2, platoon 4, company 5 (UnitEchelon)
            byte echelon = row.Class switch { "InfantryPlatoon" => 4, "InfantryCompany" => 5, _ => 2 };
            var dis = new Fdp.Core.DISEntityType { Kind = 11, Domain = 1, Country = 225, Category = 1, Subcategory = echelon };
            Assert.True(Hrot.Core.Tkb.EntityClassTable.Default.Chain(dis).Any(c => c.Name == cls.Name), "the rail's DIS is the row's class");
            var named = Hrot.Core.Tkb.OrderResolver.Resolve(Hrot.Core.Tkb.EntityClassTable.Default, dis, row.Order, x => registry.TryGetId(x, out _));
            Assert.Equal(Hrot.Core.Tkb.OrderResolution.NoBehaviour, named.Outcome);
        }
        Assert.Equal(13, n);   // the squad's Defend, BreakContact · ⭐ CE-8307: the platoon's 7 (Defend, Attack, Hold, Bound, SupportByFire, Move / BreakContact relays), the company's 4
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4686</c> ④ (the orders design, R-8300; a user-authorised cross-lane edit) — ONE neutral Bound order (<c>BoundOrder</c>) means the same
    /// to every subordinate that can get it: a physical platoon (<c>PlatoonBound</c>: the assault ends at the place, the cover sets at the support
    /// point), a squad (<c>BoundingOverwatch</c>: the place) and a stochastic unit (the table routes Bound to the generic attack: the place); and a
    /// platoon's cover with NO support point sets at the objective, never at (0, 0).
    /// 🔴 Red-proof: <c>PlatoonBound</c> not reading <c>Position</c> ⇒ its assault goes to (0, 0).
    /// </summary>
    [Fact]
    public void CE4686_OneBoundOrder_MeansTheSameToAPhysicalAndAStochasticSubordinate()
    {
        var opts = Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed;
        var place = new System.Numerics.Vector3(300f, 200f, 0f);
        var support = new System.Numerics.Vector3(220f, 200f, 0f);
        const string json = "{\"Position\":[300,200,0],\"SupportPoint\":[220,200,0]}";
        var bound = System.Text.Json.JsonSerializer.Deserialize<Hrot.AI.Behaviors.Orders.BoundOrder>(json, opts);
        Assert.Equal(place, (System.Numerics.Vector3)bound.Position);

        var repo = new Fdp.Core.EntityRepository();
        var self = repo.CreateEntity();
        var pb = System.Text.Json.JsonSerializer.Deserialize<PlatoonBoundParamsJsonDto>(json, opts);
        var platoon = default(Hrot.AI.Behaviors.Brains.PlatoonBoundBehavior.PlatoonBoundBlackboard);
        Hrot.AI.Behaviors.Brains.PlatoonBoundBehavior.Resolve(pb, ref platoon, repo, self);
        Assert.Equal(place, platoon.Assault.Point);
        Assert.Equal(support, platoon.Support.Point);

        var bo = System.Text.Json.JsonSerializer.Deserialize<BoundingOverwatchParamsJsonDto>(json, opts);
        var squad = default(Hrot.AI.Behaviors.Brains.BoundingOverwatchBehavior.BoundingOverwatchBlackboard);
        Hrot.AI.Behaviors.Brains.BoundingOverwatchBehavior.Resolve(bo, ref squad, repo, self);
        Assert.Equal(place, squad.Order.Objective);

        Assert.Equal(Hrot.Core.Tkb.ResolutionRoutingTable.Plays.Attack,
                     Hrot.Core.Tkb.ResolutionRoutingTable.Default.Find("Unit", Fdp.Toolkit.Aggregate.AggregateResolution.Stochastic, "Bound"));
        var projected = Hrot.AI.Behaviors.Orders.OrderReissue.Project(Hrot.Core.Tkb.ResolutionRoutingTable.Plays.Attack, json);
        var ao = System.Text.Json.JsonSerializer.Deserialize<Hrot.AI.Behaviors.Orders.AttackOrder>(projected, opts);
        var stochastic = default(Hrot.AI.Behaviors.Brains.StochasticUnitAttackBehavior.Blackboard);
        Hrot.AI.Behaviors.Brains.StochasticUnitAttackBehavior.Resolve(ao, ref stochastic, repo, self);
        Assert.Equal(place, stochastic.Attack.Objective);

        // no support point: the platoon's cover sets AT the objective (what the commanders sent before), never (0, 0)
        var bare = System.Text.Json.JsonSerializer.Deserialize<PlatoonBoundParamsJsonDto>("{\"Position\":[300,200,0]}", opts);
        var p2 = default(Hrot.AI.Behaviors.Brains.PlatoonBoundBehavior.PlatoonBoundBlackboard);
        Hrot.AI.Behaviors.Brains.PlatoonBoundBehavior.Resolve(bare, ref p2, repo, self);
        Assert.Equal(place, p2.Support.Point);
    }
}
