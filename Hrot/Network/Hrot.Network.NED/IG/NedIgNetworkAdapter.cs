using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hrot.Core.Network;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.NED.Common;
using Hrot.Map.Common.Commands;
using CycloneDDS.Runtime;
using Fdp.Core.Logging;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;
using DebugPrimitivesBatch = GizmoMap.Network.DebugPrimitivesBatch;
using GizmoInteractionBatch = GizmoMap.Network.GizmoInteractionBatch;

namespace Hrot.Network.NED.IG
{
    /// <summary>
    /// NED/DDS implementation of <see cref="IIgNetworkAdapter"/>.
    /// Owns all DDS writers and readers used by the IG application.
    /// Created by <see cref="Hrot.Network.NED.NedNetworkFactory.CreateIgNetworkAdapter"/>.
    /// </summary>
    public sealed class NedIgNetworkAdapter : IIgNetworkAdapter
    {
        private readonly ICommandGateway                       _commandGateway;
        private readonly DdsWriterGizmoAdapter<GizmoInteractionBatch>  _gizmoInteractionWriter;
        private readonly DdsReaderGizmoAdapter<DebugPrimitivesBatch>   _debugPrimitivesReader;
        private bool _disposed;

        /// <inheritdoc/>
        public ICommandGateway CommandGateway => _commandGateway;

        /// <inheritdoc/>
        public IDdsWriter<GizmoInteractionBatch>? GizmoInteractionWriter => _gizmoInteractionWriter;

        /// <inheritdoc/>
        public IDdsReader<DebugPrimitivesBatch>? DebugPrimitivesReader => _debugPrimitivesReader;

        /// <summary>
        /// Creates all DDS writers and readers for the IG.
        /// </summary>
        /// <param name="participant">Active DDS participant; must not be null.</param>
        /// <param name="nodeId">Node ID of this IG instance (used as MapId).</param>
        public NedIgNetworkAdapter(DdsParticipant participant, long nodeId = 0)
        {
            if (participant == null) throw new ArgumentNullException(nameof(participant));

            _commandGateway    = new NedCommandGateway(participant, nodeId);
            _gizmoInteractionWriter = new DdsWriterGizmoAdapter<GizmoInteractionBatch>(participant);
            _debugPrimitivesReader  = new DdsReaderGizmoAdapter<DebugPrimitivesBatch>(participant);
        }










        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _commandGateway.Dispose();
            _gizmoInteractionWriter.Dispose();
            _debugPrimitivesReader.Dispose();
        }
    }
}
