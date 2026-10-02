using System;
using System.Runtime.InteropServices;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Systems
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-215</c> — the CONSUMER half of <see cref="DebugPrimitivesBatchPublisherSystem"/>.</b>
    /// Reads <c>DebugPrimitivesBatch</c> from DDS and fills a local <see cref="DebugPrimitiveBuffer"/>,
    /// so a node can RENDER gizmos produced elsewhere without running that simulation.
    /// </summary>
    /// <remarks>
    /// <para><b>📌 Why this had to be written rather than wired.</b> The publisher has existed since the
    /// gizmo network work, and <c>NedNetworkFactory.CreateGizmoPublisherSystem</c> builds it — but
    /// measured <c>2026-09-09</c>, <b>no production consumer existed anywhere</b>. The only reader-side
    /// code in the tree was <c>GizmoMap.Example</c>'s <c>DdsGizmoTransport</c>, an EXAMPLE project.
    /// ⇒ the dangling <c>// _gizmoIngress?.PollAndApply(); // wire in SM-006</c> in
    /// <c>StrideNodeBootstrapper.Tick</c> pointed at that example's API, which is why it was never
    /// "just wired".</para>
    ///
    /// <para><b>⭐ It is a strict mirror of the publisher, deliberately.</b> The publisher encodes with
    /// <c>MemoryMarshal.AsBytes(frame).ToArray()</c> — a raw blob of <see cref="DebugPrimitive"/>
    /// structs — so this decodes with <c>MemoryMarshal.Cast</c> and appends. ⛔ No schema, no version
    /// field: if the struct layout changes, both halves change together because they live in the same
    /// folder and share the type. ⚠ That is a real constraint, stated rather than hidden — a mismatched
    /// build on either side would reinterpret bytes.</para>
    ///
    /// <para><b>⚠ Ragged payloads are DROPPED, not partially decoded.</b> A byte count that is not a
    /// whole multiple of the struct size means the sender and receiver disagree about the layout; taking
    /// the first N whole structs from such a payload would render plausible-looking garbage. ⭐ The
    /// count is exposed as <see cref="DroppedRaggedCount"/> so the condition is visible instead of
    /// silent — the drop-with-no-signal shape this codebase keeps paying for.</para>
    ///
    /// <para>⭐ <b>Own-node samples are skipped.</b> A node that both publishes and subscribes would
    /// otherwise draw its own gizmos twice, once locally and once round-tripped. ⚠ Skipping on
    /// <c>NodeId</c> costs nothing and makes the system safe to register on a publisher.</para>
    ///
    /// <para>⛔ <b>The buffer is NOT cleared here.</b> Clearing is the frame owner's job — the host
    /// clears its consumer buffer at the top of its own frame — because only the host knows where the
    /// frame boundary is. ⚠ Registering this without clearing anywhere would grow the buffer until it
    /// saturates.</para>
    ///
    /// <para>Runs in <see cref="SystemPhase.Input"/>: the frame's remote primitives must be present
    /// before anything renders them, and the publisher's counterpart runs at <c>Export</c>.</para>
    /// </remarks>
    [UpdateInPhase(SystemPhase.Input)]
    public sealed class DebugPrimitivesBatchSubscriberSystem : IEcsModuleSystem
    {
        private readonly DebugPrimitiveBuffer _buffer;
        private readonly IDdsReader<DebugPrimitivesBatch> _reader;
        private readonly byte _localNodeId;

        /// <summary>Batches accepted and appended. ⭐ A flat zero here is the "nothing arrived" signal.</summary>
        public long ReceivedSampleCount { get; private set; }

        /// <summary>Primitives appended to the buffer across all batches.</summary>
        public long AppendedPrimitiveCount { get; private set; }

        /// <summary>
        /// ⚠ Batches dropped because the payload was not a whole number of <see cref="DebugPrimitive"/>
        /// structs. ⛔ Non-zero means the two sides disagree about the struct layout — a build mismatch,
        /// not a transient.
        /// </summary>
        public long DroppedRaggedCount { get; private set; }

        /// <summary>Batches skipped because they came from this node (see the class remarks).</summary>
        public long SkippedOwnNodeCount { get; private set; }

        public DebugPrimitivesBatchSubscriberSystem(
            DebugPrimitiveBuffer buffer,
            IDdsReader<DebugPrimitivesBatch> reader,
            byte localNodeId)
        {
            _buffer      = buffer ?? throw new ArgumentNullException(nameof(buffer));
            _reader      = reader ?? throw new ArgumentNullException(nameof(reader));
            _localNodeId = localNodeId;
        }

        public unsafe void Execute(ISimulationView view, float deltaTime)
        {
            int structSize = sizeof(DebugPrimitive);

            // ⭐ Drain: DDS keeps the latest samples, and one frame may carry several publishers.
            while (_reader.TryRead(out DebugPrimitivesBatch batch))
            {
                if (batch.NodeId == _localNodeId) { SkippedOwnNodeCount++; continue; }

                byte[]? data = batch.PrimitivesData;
                if (data == null || data.Length == 0) continue;

                if (data.Length % structSize != 0) { DroppedRaggedCount++; continue; }

                ReadOnlySpan<DebugPrimitive> prims =
                    MemoryMarshal.Cast<byte, DebugPrimitive>(new ReadOnlySpan<byte>(data));

                foreach (ref readonly var p in prims)
                {
                    _buffer.AppendRaw(in p);
                    AppendedPrimitiveCount++;
                }

                ReceivedSampleCount++;
            }
        }
    }
}
