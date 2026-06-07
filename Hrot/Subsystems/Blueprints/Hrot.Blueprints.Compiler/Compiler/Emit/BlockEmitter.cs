using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler.Emit;

internal static class BlockEmitter
{
    /// <summary>
    /// Emits a single IrBlock as C# code.
    /// If isEntry is true, no label is emitted (entry block).
    /// Blocks share the method scope (no per-block braces) so that SSA temp
    /// locals declared in one block remain in scope across goto edges into
    /// other blocks.  This is the correct semantic model for a labelled-block
    /// goto state machine.
    /// </summary>
    public static void Emit(CSharpEmitter e, IrBlock block, bool isEntry)
    {
        if (!isEntry)
            e.WriteLine($"__block_{block.Label}:");
        foreach (var stmt in block.Statements)
            StatementEmitter.Emit(e, stmt);
        TerminatorEmitter.Emit(e, block.Terminator, e.Ctx);
        e.WriteLine();
    }
}
