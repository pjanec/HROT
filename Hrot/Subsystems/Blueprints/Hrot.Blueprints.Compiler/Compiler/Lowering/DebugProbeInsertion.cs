using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler.Lowering;

internal static class DebugProbeInsertion
{
    public static IrAsset Apply(IrAsset asset, CompilerMode mode)
    {
        if (mode == CompilerMode.Release) return asset;

        var newGraphs = asset.Graphs.Select(g => g with
        {
            Blocks = g.Blocks.Select(b => InsertProbes(b, mode)).ToList(),
        }).ToList();

        return asset with { Graphs = newGraphs };
    }

    private static IrBlock InsertProbes(IrBlock block, CompilerMode mode)
    {
        if (block.Statements.Count == 0) return block;

        var firstStmt = block.Statements[0];
        if (firstStmt.Debug?.NodeId is null) return block;

        var probeOp = new IrOp_DebugProbe_NodeEnter(
            firstStmt.Debug.NodeId.Value,
            firstStmt.Debug.NodeId.Value.ToString());
        var probe = new IrStatement
        {
            Operation = probeOp,
            Debug = firstStmt.Debug with { NodeKind = probeOp.NodeKind },
        };

        var newStatements = new List<IrStatement> { probe };
        newStatements.AddRange(block.Statements);

        if (mode == CompilerMode.Trace)
        {
            // In Trace mode, also add IrOp_DebugProbe_PinValue after
            // each value-producing statement that has an associated pin.
            var withPinProbes = new List<IrStatement>(newStatements.Count * 2);
            foreach (var stmt in newStatements)
            {
                withPinProbes.Add(stmt);
                if (stmt.ResultValue.HasValue && stmt.Debug?.PinId.HasValue == true)
                {
                    withPinProbes.Add(new IrStatement
                    {
                        Operation = new IrOp_DebugProbe_PinValue(
                            stmt.Debug.PinId!.Value,
                            stmt.ResultValue.Value,
                            stmt.Debug.PinId.Value.ToString()),
                        Debug = stmt.Debug,
                    });
                }
            }
            newStatements = withPinProbes;
        }

        return block with { Statements = newStatements };
    }
}
