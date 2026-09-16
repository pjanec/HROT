using System;
using System.Runtime.InteropServices;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;
using DebugPrimitivesBatch = GizmoMap.Network.DebugPrimitivesBatch;

namespace Hrot.Network.NED.Gizmos
{
    /// <summary>
    /// Polls the DDS <see cref="DebugPrimitivesBatch"/> topic and applies the most recent
    /// batch to the local <see cref="DebugPrimitiveBuffer"/>, replacing its contents.
    /// Called from the Raylib render-loop thread (not the ECS thread).
    /// </summary>
    public sealed class DebugPrimitivesIngressTranslator
    {
        private readonly DebugPrimitiveBuffer _buffer;
        private readonly IDdsReader<DebugPrimitivesBatch>? _reader;
        private readonly byte? _filterNodeId;

        /// <param name="buffer">Target buffer to populate.</param>
        /// <param name="reader">DDS reader; null disables network ingress (local-only mode).</param>
        /// <param name="filterNodeId">When set, only batches with matching NodeId are applied.</param>
        public DebugPrimitivesIngressTranslator(
            DebugPrimitiveBuffer buffer,
            IDdsReader<DebugPrimitivesBatch>? reader = null,
            byte? filterNodeId = null)
        {
            _buffer       = buffer ?? throw new ArgumentNullException(nameof(buffer));
            _reader       = reader;
            _filterNodeId = filterNodeId;
        }

        /// <summary>
        /// Drains all pending DDS samples, selects the latest matching one, and replaces the
        /// buffer contents with its primitives. Called every render tick.
        /// </summary>
        public void PollAndApply()
        {
            if (_reader == null) return;

            DebugPrimitivesBatch? latest = null;
            while (_reader.TryRead(out var batch))
            {
                if (_filterNodeId.HasValue && batch.NodeId != _filterNodeId.Value)
                    continue;
                latest = batch;
            }

            if (!latest.HasValue) return;

            _buffer.Clear();
            var data = latest.Value.PrimitivesData;
            if (data == null) return;

            var primitives = MemoryMarshal.Cast<byte, DebugPrimitive>(data.AsSpan());
            for (int i = 0; i < primitives.Length; i++)
            {
                var prim = primitives[i];

                // ⭐⭐⭐ CE-259af — STRIP THE SENDER'S ECS HANDLE. This is the one place foreign
                //   primitives enter a local buffer, so it is the one place the invariant can be
                //   ENFORCED rather than asserted in a comment.
                //
                //   📐 Offsets 8/12 carry an IN-PROCESS PAYLOAD: the emitting process's ECS index and
                //     generation, which the local terminal's adapter turns straight back into
                //     `new Entity(AnchorIndex, StreamId)` (Fdp.Presentation DebugGizmoLayer.ToPickToken).
                //     ⛔ For a primitive that arrived over DDS those are ANOTHER PROCESS'S handle ⇒
                //     rebuilding an Entity from them yields a WRONG LOCAL ENTITY, and
                //     SelectionInteractionSystem would select it. That is defect D2 of
                //     DESIGN_Gizmo_Anchor_Identity.md relocated from the wire to the receiver's own
                //     boundary — silently, because a plausible entity usually exists.
                //
                //   ⭐ Zeroing them makes the payload STRUCTURALLY process-local: ToPickToken sees
                //     StreamId == 0, returns an invalid token, and the interaction is published over DDS
                //     to the OWNING node, where GizmoInteractionIngressTranslator resolves the network id
                //     through its NetworkEntityMap. ⇒ it degrades onto the DESIGNED remote path rather
                //     than into a wrong selection. A dumb terminal does not own entities.
                //
                //   ⭐⭐ The IDENTITY is untouched: BoxAnchorId / StructNetworkId are network ids and
                //     remain fully usable here — hit-testing, the exclusive-capture filter and the
                //     context-menu lookup all work on a received primitive exactly as before.
                //
                //   ⛔⛔ AND IT MUST BE SHAPE-DISCRIMINATED — a blanket zeroing is WRONG, which is the
                //     mistake this comment exists to prevent. Offsets 8/12 hold THREE different things
                //     (DebugPrimitive.cs, S7):
                //       (a) an interactive Box2D/Sphere → the ECS payload  ← strip this one
                //       (b) any EntityLocal primitive   → the SpatialAnchor cache KEY, a NETWORK id
                //           ← MUST SURVIVE, or remote EntityLocal geometry stops resolving entirely,
                //             which is the whole point of the dumb-terminal two-pass renderer
                //       (c) Text / EntityBadge          → StringHash at 8 and LineOffsetPx at 12
                //           ← MUST SURVIVE, or remote text loses its content and its line stacking
                //
                //   ⚠ Not a live bug fix: this translator is currently instantiated only in tests
                //     (measured 2026-09-11 — no production call site). It is a TRAP being closed before
                //     anyone wires primitive streaming, which IG already imports this type for.
                //
                //   ⭐⭐⭐ §6.7, LATER THE SAME DAY — WHAT THIS NOW GUARDS, restated honestly. The local
                //     producers of arm (a) are GONE: no factory and no gizmo stamps an ECS handle into
                //     offsets 8/12 any more, and ToPickToken no longer reads one (it copies the network
                //     id). ⇒ a primitive from a CURRENT peer arrives with those bytes already zero and
                //     this strip is a no-op.
                //   ⭐ It is kept because it is now a WIRE-COMPATIBILITY guard: a peer running older
                //     code still sends its handle, and offsets 8/12 are read for arms (b) and (c) — so
                //     an EntityLocal primitive whose 32-bit anchor key happened to equal a foreign ECS
                //     index is exactly the confusion this keeps out. ⛔ Do not delete it as dead: its
                //     inputs come from another process, not from this build.
                bool carriesEcsPayload =
                    prim.Space != CoordinateSpace.EntityLocal
                    && (prim.Shape == DebugPrimitiveShape.Box2D
                        || prim.Shape == DebugPrimitiveShape.Sphere);

                if (carriesEcsPayload)
                {
                    prim.AnchorIndex      = 0;
                    prim.AnchorGeneration = 0;
                }

                _buffer.AppendRaw(in prim);
            }
        }
    }
}
