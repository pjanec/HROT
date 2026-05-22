using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Emit;
using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler.Stages;

internal static class Stage7_Emit
{
    public static (string GeneratedSource, DebugMap DebugMap) Run(
        IrAsset asset, CompilerMode mode, DiagnosticSink sink)
    {
        var ctx = new EmissionContext(asset, mode);
        var emitter = new CSharpEmitter(ctx);
        return emitter.Emit(asset);
    }
}
