using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler.Emit;

internal static class ChannelCommandLowering
{
    public static void Emit(CSharpEmitter e, IrOp_ChannelCommand op)
    {
        var n = e.Ctx.NextLocalCounter("ch");
        var worldVar = e.Ctx.WorldVar;

        // Guard: only issue the channel command when the entity actually owns the channel
        // component.  This makes the generated code safe to call on entities that were not
        // fully set up (e.g. test scenarios that create minimal entities).  In production
        // all AI entities carry the required channel components.
        e.WriteLine($"if ({worldVar}.HasComponent<global::{op.ChannelComponentTypeFqn}>(self))");
        e.WriteLine("{");
        e.Indent();
        e.WriteLine($"ref var __ch_{n} = ref {worldVar}.GetComponentRW<global::{op.ChannelComponentTypeFqn}>(self);");
        e.WriteLine($"__ch_{n}.ActiveAction = {op.ActionIdConstantName};");
        if (op.ParamFields.Count > 0)
        {
            e.WriteLine("unsafe");
            e.WriteLine("{");
            e.Indent();
            e.WriteLine($"fixed (byte* __paramSlot_{n} = __ch_{n}.Params)");
            e.WriteLine("{");
            e.Indent();
            e.WriteLine($"*(global::{op.ParamsStructTypeFqn}*)__paramSlot_{n} = new global::{op.ParamsStructTypeFqn}");
            e.WriteLine("{");
            e.Indent();
            for (int i = 0; i < op.ParamFields.Count; i++)
            {
                var f = op.ParamFields[i];
                var sep = i == op.ParamFields.Count - 1 ? "" : ",";
                e.WriteLine($"{f.FieldName} = __t{f.Value.Index}{sep}");
            }
            e.Outdent();
            e.WriteLine("};");
            e.Outdent();
            e.WriteLine("}");
            e.Outdent();
            e.WriteLine("}");
        }
        e.WriteLine($"__ch_{n}.ActionInstanceId++;");
        e.Outdent();
        e.WriteLine("}");
    }
}
