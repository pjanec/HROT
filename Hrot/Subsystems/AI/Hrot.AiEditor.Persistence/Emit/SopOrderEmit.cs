using Hrot.AiEditor.Persistence.BTree;

namespace Hrot.AiEditor.Persistence.Emit;

/// <summary>
/// ⭐ <c>CE-2083</c> — the ONE spelling of the generated call an SOP order makes, shared by every host that emits one: the
/// BTree bridge (an action node's order) and the HSM bridge (a state's order). Each host supplies only how it reaches the
/// world, the unit and the params bytes. 📄 <c>docs/DESIGN_Decision_Layer.md</c> §4.10 (D2), §4.6.
/// </summary>
public static class SopOrderEmit
{
    /// <summary>
    /// <c>global::Fdp.Toolkit.Behavior.SopActions.{DoWhenIdle|React}(world, self, "name"[, urgency], params)</c>.
    /// <paramref name="paramsArg"/> is a C# argument (e.g. <c>in dto</c>); <c>null</c> passes <c>"{}"</c> — the behaviour's
    /// authored defaults.
    /// </summary>
    public static string Call(SopOrderPayloadDto order, string world, string self, string? paramsArg)
    {
        string name = order.BehaviorName.Replace("\\", "\\\\").Replace("\"", "\\\"");
        string method = order.Kind == SopOrderKindDto.React ? "React" : "DoWhenIdle";
        string urgency = order.Kind == SopOrderKindDto.React
            ? $", global::Fdp.Toolkit.Behavior.Components.ReactionUrgency.{order.Urgency}"
            : "";
        return $"global::Fdp.Toolkit.Behavior.SopActions.{method}({world}, {self}, \"{name}\"{urgency}, {paramsArg ?? "\"{}\""})";
    }

    /// <summary>
    /// ⭐ The baked HOST offset of an order's params variable, or <c>-1</c> when it binds none (authored defaults) — the ONE
    /// rule both hosts key on (<see cref="SopOrderPayloadDto.ActionKey"/>). ⛔ A named variable with no packed offset fails
    /// loud: the order would silently drop its params. <paramref name="where"/> names the node / state in the message.
    /// </summary>
    public static long Offset(SopOrderPayloadDto order, System.Collections.Generic.IReadOnlyDictionary<string, int>? variableOffsets,
        string where)
    {
        if (string.IsNullOrEmpty(order.BehaviorName))
            throw new System.InvalidOperationException($"SOP order {where} names no behaviour — pick one in the editor.");
        if (string.IsNullOrEmpty(order.ParamsVariable)) return -1;
        if (variableOffsets != null && variableOffsets.TryGetValue(order.ParamsVariable!, out int offset)) return offset;
        throw new System.InvalidOperationException(
            $"SOP order {where} binds params variable '{order.ParamsVariable}', which has no packed offset " +
            "(a non-managed blackboard, or no such variable).");
    }
}
