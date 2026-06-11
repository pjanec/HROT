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
        // ── Determine block probe identity ─────────────────────────────────────
        //
        // Two-tier fallback:
        // 1. block.SourceNodeId      — set by Stage5 for every reachable block
        // 2. OriginNodeId of first statement — set by lowering passes
        // Tier 3 (Statements[0].Debug?.NodeId) intentionally omitted: it
        // mis-attributes probes to data nodes (GetVariable, etc.).
        Guid? blockSourceNodeId = block.SourceNodeId
            ?? (block.Statements.Count > 0 ? block.Statements[0].Debug?.OriginNodeId : null);
        if (blockSourceNodeId is null) return block;

        // Get GraphId from block's first statement or terminator debug info.
        var graphId = (block.Statements.Count > 0
            ? block.Statements[0].Debug?.GraphId
            : null)
            ?? block.Terminator?.Debug?.GraphId
            ?? default;

        // ── Determine whether SourceNodeId needs a block-header probe ──────────
        //
        // DebugProbeInsertion runs AFTER WaitLowering (Stage 6 ordering):
        //
        //   ScheduleLatentNode (Stage5) overwrites bb.SourceNodeId with the
        //   latent node's ID.  The latent op statement (IrOp_LatentDelay etc.)
        //   is tagged ExecEntryNodeId=latentNodeId, but WaitLowering_Instance
        //   strips that statement and replaces it with synthesized WriteCursor*
        //   statements.  Those carry OriginNodeId=bb.SourceNodeId but NO
        //   ExecEntryNodeId.
        //
        // There are therefore two ways a SourceNodeId may be "covered" inline:
        //
        //   (a) ExecEntryNodeId coverage — for exec nodes whose Stage5 effect
        //       statement survives all lowering passes unchanged (SetVariable,
        //       BranchNode, ReturnNode-anchor, …).
        //
        //   (b) OriginNodeId coverage — for the latent node itself: after
        //       WaitLowering the first statement of the WriteCursor group carries
        //       OriginNodeId=SourceNodeId with ExecEntryNodeId absent.  This
        //       serves as the probe insertion point for the latent node.
        //
        // When NEITHER coverage applies (e.g. EventEntryNode, which produces no
        // IR statements at all), emit a block-header probe so that breakpoints set
        // on that exec node can still fire.
        bool coveredByExecEntryId = block.Statements
            .Any(s => s.Debug?.ExecEntryNodeId == blockSourceNodeId);

        bool coveredByOriginId = !coveredByExecEntryId && block.Statements
            .Any(s => s.Debug?.ExecEntryNodeId == null
                   && s.Debug?.OriginNodeId == blockSourceNodeId);

        bool needsHeaderProbe = !coveredByExecEntryId && !coveredByOriginId;

        // ── Pass 1: build the new statement list with per-node probes ───────────
        var newStatements = new List<IrStatement>(block.Statements.Count + 4);

        if (needsHeaderProbe)
        {
            // Exec node produced no IR statements of its own (e.g. EventEntryNode).
            // Emit a block-header probe so breakpoints on that node can fire.
            var headerProbeOp = new IrOp_DebugProbe_NodeEnter(
                blockSourceNodeId.Value,
                blockSourceNodeId.Value.ToString());
            newStatements.Add(new IrStatement
            {
                Operation = headerProbeOp,
                Debug = new IrDebugAnnotation
                {
                    GraphId  = graphId,
                    NodeId   = blockSourceNodeId,
                    NodeKind = headerProbeOp.NodeKind,
                },
            });
        }

        bool originIdProbeEmitted = false; // guard: emit OriginNodeId probe at most once

        foreach (var stmt in block.Statements)
        {
            var entryNodeId = stmt.Debug?.ExecEntryNodeId;
            var originNodeId = stmt.Debug?.OriginNodeId;

            // (a) ExecEntryNodeId path: insert per-node probe before any
            //     statement tagged by Stage5 as an exec-node entry boundary.
            if (entryNodeId.HasValue)
            {
                var perNodeProbeOp = new IrOp_DebugProbe_NodeEnter(
                    entryNodeId.Value,
                    entryNodeId.Value.ToString());
                newStatements.Add(new IrStatement
                {
                    Operation = perNodeProbeOp,
                    Debug = new IrDebugAnnotation
                    {
                        GraphId  = graphId,
                        NodeId   = entryNodeId,
                        NodeKind = perNodeProbeOp.NodeKind,
                    },
                });
            }
            // (b) OriginNodeId path: first synthesized statement whose OriginNodeId
            //     matches SourceNodeId (i.e. WriteCursorResumeAt for a latent node
            //     after WaitLowering) triggers the latent node's per-node probe.
            //     Guard ensures only the first such statement in the group fires it.
            else if (coveredByOriginId
                  && !originIdProbeEmitted
                  && entryNodeId == null
                  && originNodeId == blockSourceNodeId)
            {
                var latentProbeOp = new IrOp_DebugProbe_NodeEnter(
                    blockSourceNodeId.Value,
                    blockSourceNodeId.Value.ToString());
                newStatements.Add(new IrStatement
                {
                    Operation = latentProbeOp,
                    Debug = new IrDebugAnnotation
                    {
                        GraphId  = graphId,
                        NodeId   = blockSourceNodeId,
                        NodeKind = latentProbeOp.NodeKind,
                    },
                });
                originIdProbeEmitted = true;
            }

            newStatements.Add(stmt);
        }

        // ── Pass 2 (Trace mode): insert pin-value probes ────────────────────────
        if (mode == CompilerMode.Trace)
        {
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
