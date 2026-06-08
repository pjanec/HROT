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
        // Three-tier fallback for per-block probe identity:
        // 1. block.SourceNodeId      — set by Stage5 for entry/latent/sequence blocks
        // 2. OriginNodeId            — set by lowering passes to preserve authored id
        // 3. Statements[0].NodeId    — legacy fallback (works for test graphs and
        //    non-AiPrimitive graphs where blocks don't carry SourceNodeId)
        //    Note: tier 3 mis-attributes probes when a data node (GetVariable)
        //    is the first statement. This is mitigated by tier 1+2 catching the
        //    exec nodes that own their own blocks.
        Guid? probeNodeId = block.SourceNodeId
            ?? (block.Statements.Count > 0 ? block.Statements[0].Debug?.OriginNodeId : null)
            ?? (block.Statements.Count > 0 ? block.Statements[0].Debug?.NodeId : null);
        if (probeNodeId is null) return block;

        // Get GraphId from block's first statement or terminator debug info.
        var graphId = (block.Statements.Count > 0
            ? block.Statements[0].Debug?.GraphId
            : null)
            ?? block.Terminator?.Debug?.GraphId
            ?? default;

        var probeOp = new IrOp_DebugProbe_NodeEnter(
            probeNodeId.Value,
            probeNodeId.Value.ToString());
        var probe = new IrStatement
        {
            Operation = probeOp,
            Debug = new IrDebugAnnotation
            {
                GraphId   = graphId,
                NodeId    = probeNodeId,
                NodeKind  = probeOp.NodeKind,
            },
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
