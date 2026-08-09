using System.Text;
using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler;

/// <summary>
/// Produces deterministic human-readable text from an <see cref="IrAsset"/>.
/// Used for snapshot testing and debugging.
/// </summary>
internal static class IrPrinter
{
    public static string PrettyPrint(IrAsset asset)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"IrAsset: {asset.Name} (0x{asset.BlueprintId:X8}) {asset.Dispatch}");
        foreach (var g in asset.Graphs)
        {
            var entryVal = g.Blocks.Count > 0
                ? g.Entry.Value.ToString()
                : "-";
            sb.AppendLine($"  Graph: {g.Name} [{g.Kind}] Entry={entryVal}");
            foreach (var b in g.Blocks)
            {
                sb.AppendLine($"    Block {b.Id.Value} ({b.Label}):");
                foreach (var s in b.Statements)
                {
                    string lhs = s.ResultValue.HasValue
                        ? $"t{s.ResultValue.Value.Index} = "
                        : "";
                    sb.AppendLine($"      {lhs}{PrintOperation(s.Operation)}");
                }
                sb.AppendLine($"      {PrintTerminator(b.Terminator)}");
            }
        }
        return sb.ToString();
    }

    private static string PrintOperation(IrOperation op) => op switch
    {
        IrOp_Const c          => $"const {c.Type.FullName} {c.CSharpLiteral}",
        IrOp_ReadVariable r   => $"read_var[{r.VariableIndex}]",
        IrOp_WriteVariable w  => $"write_var[{w.VariableIndex}] <- t{w.Value.Index}",
        IrOp_ReadParam r      => $"read_param[{r.ParamIndex}]",
        IrOp_PureCall p       => $"pure_call {p.MethodFqn}({FormatArgs(p.Args)})",
        IrOp_LibraryCall l    => $"lib_call {l.MethodName}({FormatArgs(l.Args)})",
        IrOp_PeerCall p       => $"peer_call 0x{p.PeerBlueprintId:X8}.{p.MethodName}({FormatArgs(p.Args)})",
        IrOp_RaiseCustomEvent e => $"raise_event[{e.CustomEventIndex}]({FormatArgs(e.Args)})",
        IrOp_LatentDelay d    => $"latent_delay t{d.Seconds.Index}",
        IrOp_WaitForChannel w => $"wait_for_channel {w.ChannelComponentTypeFqn}",
        IrOp_WaitForEvent w   => $"wait_for_event {w.EventTypeFqn}",
        IrOp_ChannelCommand c    => $"channel_cmd {c.ChannelComponentTypeFqn}.{c.ActionIdConstantName}",
        IrOp_InlineActionCall a  => $"inline_action_call {a.ActionFqn}",
        IrOp_ReadShared r        => r.TargetEntity is { } tgt
            ? $"read_shared[{r.VariableId}:{r.SharedTypeFqn}] @t{tgt.Index} (found=t{r.FoundValue.Index})"
            : $"read_shared[{r.VariableId}:{r.SharedTypeFqn}] (found=t{r.FoundValue.Index})",
        IrOp_WriteShared w       => $"write_shared[{w.VariableId}:{w.SharedTypeFqn}] <- t{w.Value.Index}",
        _                        => op.GetType().Name,
    };

    private static string PrintTerminator(IrTerminator term) => term switch
    {
        IrTerm_Return r      => r.Value.HasValue ? $"return t{r.Value.Value.Index}"
                                : r.ReturnsDefault ? "return default"   // BP-117
                                : "return",
        IrTerm_ReturnStatus s => $"return_status {s.Status}",
        IrTerm_Goto g        => $"goto block_{g.Target.Value}",
        IrTerm_Branch b      => $"branch t{b.Condition.Index} ? block_{b.IfTrue.Value} : block_{b.IfFalse.Value}",
        IrTerm_Suspend s     => $"suspend resume_pt=t{s.ResumePoint.Index} resume=block_{s.ResumeBlock.Value}",
        IrTerm_FallThrough   => "fall_through",
        _                     => term.GetType().Name,
    };

    private static string FormatArgs(IReadOnlyList<IrValue> args)
    {
        if (args.Count == 0) return "";
        return string.Join(", ", args.Select(v => $"t{v.Index}"));
    }
}
