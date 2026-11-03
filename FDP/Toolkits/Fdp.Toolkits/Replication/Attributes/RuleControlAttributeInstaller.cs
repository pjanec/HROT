using Fdp.Toolkit.Replication.Patching;
using Fdp.Toolkit.ScenarioRules;

namespace Fdp.Toolkit.Replication.Attributes;

/// <summary>
/// ⭐ rules V-4 (📄 docs/DESIGN_Scenario_Rules.md §00.10) — the binary arm of a rule entity's control: <c>Rule.Request</c> / <c>Rule.Seq</c> /
/// <c>Rule.Origin</c>, the twins of the JSON paths in <see cref="AttributeCompilerFactory.Build"/>. The typed overload gates on
/// <c>CanWrite&lt;RuleControl&gt;()</c> — only the node that owns the rule entity applies it (and holds the component); a write anywhere else is the
/// router's change-request. ⭐ One <c>Write(entity, changes)</c> carries all three, so the owner never sees a Seq without its Request.
/// </summary>
public sealed class RuleControlAttributeInstaller : IBinaryAttributeInstaller<EntityAttributeChange>
{
    public void Install(BinaryInterpreterBuilder<EntityAttributeChange> builder)
    {
        builder.RegisterHandler<RuleControl>(AttributeIds.RuleRequest, (ctx, r) => ctx.PatchContext.GetUnmanagedComponent<RuleControl>().Request = (byte)Int(r));
        builder.RegisterHandler<RuleControl>(AttributeIds.RuleSeq, (ctx, r) =>
        {
            ctx.PatchContext.GetUnmanagedComponent<RuleControl>().Seq = Int(r);
            // ⭐ CE-2380 part 2 — the request is judged later (ObjectiveHost.Enter / TriggerBehavior.Control): remember its id for the judge, and tell the owner to report Pending.
            //   RuleControl is a recorded unmanaged struct, so the id rides in a per-world side map (RuleRequestIds), not in a new field.
            if (ctx.Repo is { } repo && ctx.RequestId != System.Guid.Empty) RuleRequestIds.Set(repo, ctx.Entity, ctx.RequestId);
            ctx.Deferred = true;
        });
        builder.RegisterHandler<RuleControl>(AttributeIds.RuleOrigin, (ctx, r) => ctx.PatchContext.GetUnmanagedComponent<RuleControl>().Origin = (byte)Int(r));
    }

    private static int Int(EntityAttributeChange r) => r.Value.Kind switch
    {
        AttributeValueKind.CsInt64 => (int)r.Value.LongValue,
        AttributeValueKind.CsFloat64 => (int)r.Value.DoubleValue,
        _ => r.Value.IntValue,
    };
}
