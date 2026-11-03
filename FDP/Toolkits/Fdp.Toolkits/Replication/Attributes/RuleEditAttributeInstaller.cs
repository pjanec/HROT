using Fdp.Toolkit.Replication.Patching;
using Fdp.Toolkit.ScenarioRules;

namespace Fdp.Toolkit.Replication.Attributes;

/// <summary>
/// ⭐ rules ② (📄 docs/DESIGN_Scenario_Rules.md §00.11a, R1) — the binary arm of a rule's LIVE EDIT: <c>Rule.Params</c> / <c>Rule.EditSeq</c> / <c>Rule.EditOrigin</c>, the twins of the JSON
/// paths in <see cref="AttributeCompilerFactory.Build"/>. <see cref="RuleEdit"/> is a MANAGED immutable record, so each handler REPLACES it (<c>old with { … }</c> through
/// <c>SetManagedComponent</c>); the gate is <c>CanWriteManaged&lt;RuleEdit&gt;</c> (only the owner holds the component and its authority bit — a write elsewhere is the router's
/// change-request). ⭐ One <c>Write(entity, changes)</c> carries all three, applied in order, so the owner never sees a Seq without its Json.
/// </summary>
public sealed class RuleEditAttributeInstaller : IBinaryAttributeInstaller<EntityAttributeChange>
{
    public void Install(BinaryInterpreterBuilder<EntityAttributeChange> builder)
    {
        builder.RegisterManagedHandler<RuleEdit>(AttributeIds.RuleParams, (ctx, r) => Replace(ctx, e => e with { Json = r.Value.StringValue ?? "" }));
        builder.RegisterManagedHandler<RuleEdit>(AttributeIds.RuleEditSeq, (ctx, r) => Replace(ctx, e => e with { Seq = Int(r) }));
        builder.RegisterManagedHandler<RuleEdit>(AttributeIds.RuleEditOrigin, (ctx, r) => Replace(ctx, e => e with { Origin = Int(r) }));
    }

    private static void Replace(BinaryPatchContext ctx, System.Func<RuleEdit, RuleEdit> with)
        => ctx.PatchContext.SetManagedComponent(with(ctx.PatchContext.GetManagedComponent<RuleEdit>()));

    private static int Int(EntityAttributeChange r) => r.Value.Kind switch
    {
        AttributeValueKind.CsInt64 => (int)r.Value.LongValue,
        AttributeValueKind.CsFloat64 => (int)r.Value.DoubleValue,
        _ => r.Value.IntValue,
    };
}
