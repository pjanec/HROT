using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler.Emit;

internal static class BlockEmitter
{
    /// <summary>
    /// Emits a single IrBlock as C# code.
    /// If isEntry is true, no label is emitted (entry block).
    /// </summary>
    public static void Emit(CSharpEmitter e, IrBlock block, bool isEntry)
    {
        if (!isEntry)
            e.WriteLine($"__block_{block.Label}:");

        e.WriteLine("{");
        e.Indent();
        foreach (var stmt in block.Statements)
            StatementEmitter.Emit(e, stmt);
        TerminatorEmitter.Emit(e, block.Terminator, e.Ctx);
        e.Outdent();
        e.WriteLine("}");
        e.WriteLine();
    }
}
