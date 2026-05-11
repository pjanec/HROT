using System;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using CycloneDDS.Runtime;
using Fdp.Toolkit.Diagnostics.Gizmos;
using GizmoMap.Network;
using GizmoMap.Presentation;

namespace GizmoMap.Viewer
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            uint domainId = 0;
            byte targetNodeId = 1;
            byte viewerNodeId = 250;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--domain" && i + 1 < args.Length)
                {
                    domainId = uint.Parse(args[++i], CultureInfo.InvariantCulture);
                }
                else if (args[i] == "--node-id" && i + 1 < args.Length)
                {
                    targetNodeId = byte.Parse(args[++i], CultureInfo.InvariantCulture);
                }
                else if (args[i] == "--viewer-node-id" && i + 1 < args.Length)
                {
                    viewerNodeId = byte.Parse(args[++i], CultureInfo.InvariantCulture);
                }
                else if (args[i] == "--help" || args[i] == "-h")
                {
                    Console.WriteLine("Usage: GizmoMap.Viewer [--domain <id>] [--node-id <id>] [--viewer-node-id <id>]");
                    return 0;
                }
            }

            Console.WriteLine($"GizmoMap Viewer -- domain={domainId} targetNode={targetNodeId} viewerNode={viewerNodeId}");

            using var participant = new DdsParticipant(domainId);
            using var primitivesReader = new DdsReader<DebugPrimitivesBatch>(participant);
            using var stringsReader = new DdsReader<StringInternEntry>(participant);
            using var interactionWriter = new DdsWriter<GizmoInteractionBatch>(participant);

            var renderBuffer = new GizmoPrimitiveBuffer();
            var schemaRegistry = new GizmoSchemaRegistry();
            uint sequenceNumber = 0;

            GizmoViewerFrontend.Run(
                $"GizmoMap Viewer - Node {targetNodeId}",
                renderBuffer,
                schemaRegistry,
                onUpdateTick: _ =>
                {
                    renderBuffer.Clear();

                    using var stringLoan = stringsReader.Take();
                    foreach (var sample in stringLoan)
                    {
                        if (sample.IsValid && sample.Data.NodeId == targetNodeId)
                            renderBuffer.InternMap.Intern(sample.Data.Hash, sample.Data.Text);
                    }

                    DebugPrimitivesBatch? latestBatch = null;
                    using var primitiveLoan = primitivesReader.Take();
                    foreach (var sample in primitiveLoan)
                    {
                        if (sample.IsValid && sample.Data.NodeId == targetNodeId)
                            latestBatch = sample.Data;
                    }

                    if (!latestBatch.HasValue || latestBatch.Value.PrimitivesData == null)
                        return;

                    var primitives = MemoryMarshal.Cast<byte, DebugPrimitive>(latestBatch.Value.PrimitivesData.AsSpan());
                    foreach (ref readonly var primitive in primitives)
                        renderBuffer.AppendRaw(in primitive);
                },
                onInteraction: (token, kind, pos, actionId, stateFlags, payloadJson) =>
                {
                    interactionWriter.Write(new GizmoInteractionBatch
                    {
                        SourceNodeId = viewerNodeId,
                        SequenceNumber = ++sequenceNumber,
                        Kind = kind,
                        PickAnchorId = token.AnchorId,
                        PickSubElementId = token.SubElementId,
                        PickStreamId = token.StreamId,
                        WorldX = pos.X,
                        WorldY = pos.Y,
                        WorldZ = pos.Z,
                        Space = stateFlags,
                        ActionId = actionId,
                        PayloadJson = payloadJson,
                    });
                },
                onMenuAction: (token, actionId) =>
                {
                    interactionWriter.Write(new GizmoInteractionBatch
                    {
                        SourceNodeId = viewerNodeId,
                        SequenceNumber = ++sequenceNumber,
                        Kind = GizmoInteractionEventKind.MenuAction,
                        PickAnchorId = token.AnchorId,
                        PickSubElementId = token.SubElementId,
                        PickStreamId = token.StreamId,
                        WorldX = 0f,
                        WorldY = 0f,
                        WorldZ = 0f,
                        Space = 0,
                        ActionId = actionId,
                    });
                });

            return 0;
        }
    }
}
