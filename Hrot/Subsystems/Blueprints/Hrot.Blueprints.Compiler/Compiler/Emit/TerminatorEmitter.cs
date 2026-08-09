using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler.Emit;

internal static class TerminatorEmitter
{
    public static void Emit(CSharpEmitter e, IrTerminator term, EmissionContext ctx)
    {
        switch (term)
        {
            case IrTerm_Goto t:
                e.WriteLine($"goto __block_{ctx.LabelForBlock(t.Target)};");
                break;

            case IrTerm_Branch t:
                e.WriteLine($"if (__t{t.Condition.Index})");
                e.WriteLine($"    goto __block_{ctx.LabelForBlock(t.IfTrue)};");
                e.WriteLine("else");
                e.WriteLine($"    goto __block_{ctx.LabelForBlock(t.IfFalse)};");
                break;

            case IrTerm_Return t:
                if (t.Value.HasValue)
                    e.WriteLine($"return __t{t.Value.Value.Index};");
                // BP-117: a value-returning method (Library with declared outputs) whose exec chain
                // fell off the end. `return;` here is CS0126; `return default;` is valid for both a
                // scalar and a ValueTuple, so no return-type string is needed at this point.
                else if (t.ReturnsDefault)
                    e.WriteLine("return default;");
                else
                    e.WriteLine("return;");
                break;

            case IrTerm_ReturnStatus t:
                e.WriteLine($"return global::Fbt.NodeStatus.{t.Status};");
                break;

            case IrTerm_Suspend:
                throw new InvalidOperationException(
                    "IrTerm_Suspend reached Emit stage; should have been lowered in Stage 6.");

            case IrTerm_FallThrough:
                // nothing; next block emitted sequentially
                break;

            default:
                throw new NotSupportedException(
                    $"Unsupported IrTerminator in Emit: {term.GetType().Name}");
        }
    }
}
