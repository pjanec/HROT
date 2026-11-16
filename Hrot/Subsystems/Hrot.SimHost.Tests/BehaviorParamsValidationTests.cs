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
        Assert.Equal(2, perKind["BreakContact"]);   // BreakContact + the stochastic unit's StochasticUnitBreakContact (ST-2)
        Assert.True(perKind["Move"] >= 4 && perKind["Attack"] >= 4 && perKind["Hold"] >= 4, "the walk finds the plays (not vacuous)");
    }
}
