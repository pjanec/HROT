using Hrot.NED.Messages;
using Hrot.Core.Network;
using Hrot.Network.NED.ExCon;
using Fdp.Toolkit.Commands;
using CycloneDDS.Core;
using CycloneDDS.Runtime;
using Fdp.Kernel.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Hrot.Map.Common.Commands
{
    /// <summary>
    /// Abstraction over the NED command gateway that enables injecting a test stub
    /// without requiring a live DDS participant.
    /// </summary>
    public interface INedCommandGateway
    {
        /// <summary>
        /// Sends a fire-and-forget <see cref="UpdateEntityDescriptorRequest"/> over DDS.
        /// </summary>
        void SendUpdateDescriptor(UpdateEntityDescriptorRequest request);

        /// <summary>
        /// Sends a <see cref="CreateEntityRequest"/> and awaits the acknowledgment.
        /// </summary>
        Task<CreateUpdateDeleteEntityAck> CreateEntityAsync(CreateEntityRequest request, int timeoutMs = 5000);

        /// <summary>
        /// Sends a <see cref="MissionControlRequest"/> and awaits the acknowledgment.
        /// </summary>
        Task<MissionControlAck> SendMissionControlRequestAsync(MissionControlRequest request, int timeoutMs = 5000);
    }

    /// <summary>
    /// A gateway for executing NED-specific commands over DDS.
    /// This wraps generic DdsCommandClient instances for specific request types.
    /// </summary>
    public class NedCommandGateway : INedCommandGateway, ICommandGateway
    {
        private readonly DdsCommandClient<CreateEntityRequest, CreateUpdateDeleteEntityAck>    _createEntityClient;
        private readonly DdsCommandClient<MissionControlRequest, MissionControlAck>            _missionControlClient;
        private readonly DdsWriter<UpdateEntityDescriptorRequest>                              _updateWriter;
        private readonly long _localNodeId;

        /// <summary>
        /// creates a new gateway instance.
        /// </summary>
        /// <param name="participant">The DDS participant to use for communication.</param>
        /// <param name="localNodeId">Local node identifier; embedded in FdpLog messages.</param>
        public NedCommandGateway(DdsParticipant participant, long localNodeId = 0)
        {
            if (participant == null) throw new ArgumentNullException(nameof(participant));
            _localNodeId = localNodeId;

            // Initialize the client for CreateEntityRequest
            _createEntityClient = new DdsCommandClient<CreateEntityRequest, CreateUpdateDeleteEntityAck>(
                participant,
                "CreateEntityRequest",          // Must match [DdsTopic] attribute
                "CreateUpdateDeleteEntityAck",  // Must match [DdsTopic] attribute
                req => req.RequestId,
                ack => ack.RequestId
            );

            // Initialize the client for MissionControlRequest
            _missionControlClient = new DdsCommandClient<MissionControlRequest, MissionControlAck>(
                participant,
                "MissionControlRequest",
                "MissionControlAck",
                req => req.RequestId,
                ack => ack.RequestId
            );

            // Fire-and-forget writer for UpdateEntityDescriptorRequest (drag-end position updates).
            _updateWriter = new DdsWriter<UpdateEntityDescriptorRequest>(participant, "UpdateEntityDescriptorRequest");
        }

        /// <summary>
        /// Sends a CreateEntityRequest and awaits the acknowledgment.
        /// If RequestId is empty, a new Guid is generated.
        /// </summary>
        /// <param name="request">The request data.</param>
        /// <param name="timeoutMs">Timeout in milliseconds.</param>
        /// <returns>The acknowledgment containing the entity ID or status code.</returns>
        public async Task<CreateUpdateDeleteEntityAck> CreateEntityAsync(CreateEntityRequest request, int timeoutMs = 5000)
        {
            // If the caller didn't provide an ID, generate one.
            // Since CreateEntityRequest is a struct, we must ensure we pass the modified version.
            if (request.RequestId == Guid.Empty)
            {
                request.RequestId = Guid.NewGuid();
            }

            FdpLog<NedCommandGateway>.Debug("[Node-{0}] Sending CreateEntityRequest ID={1}", _localNodeId, request.RequestId);

            var ack = await _createEntityClient.SendAsync(request, timeoutMs);
            var ackDetails = string.Concat("Entity=", ack.EntityId, " Status=", ack.StatusCode);
            FdpLog<NedCommandGateway>.Debug(
                "[Node-{0}] CreateUpdateDeleteEntityAck ID={1} {2}", _localNodeId, ack.RequestId, ackDetails);
            return ack;
        }

        /// <summary>
        /// Sends a MissionControlRequest and awaits the acknowledgment.
        /// If RequestId is empty, a new Guid is generated.
        /// </summary>
        /// <param name="request">The request data.</param>
        /// <param name="timeoutMs">Timeout in milliseconds.</param>
        /// <returns>The acknowledgment with error code (0 = success).</returns>
        public async Task<MissionControlAck> SendMissionControlRequestAsync(
            MissionControlRequest request, int timeoutMs = 5000)
        {
            if (request.RequestId == Guid.Empty)
                request.RequestId = Guid.NewGuid();

            FdpLog<NedCommandGateway>.Debug(
                "[Node-{0}] Sending MissionControlRequest ID={1} Entity={2}",
                _localNodeId, request.RequestId, request.TargetEntityId);

            var ack = await _missionControlClient.SendAsync(request, timeoutMs);
            FdpLog<NedCommandGateway>.Debug(
                "[Node-{0}] MissionControlAck ID={1} Error={2}", _localNodeId, ack.RequestId, ack.ErrorCode);
            return ack;
        }

        /// <summary>
        /// Sends a fire-and-forget <see cref="UpdateEntityDescriptorRequest"/> over DDS.
        /// Used by the IG to broadcast entity position changes after a drag-drop operation.
        /// A new <see cref="Guid"/> is generated automatically when <see cref="UpdateEntityDescriptorRequest.RequestId"/> is empty.
        /// </summary>
        /// <param name="request">The request to publish.</param>
        public void SendUpdateDescriptor(UpdateEntityDescriptorRequest request)
        {
            if (request.RequestId == Guid.Empty)
                request.RequestId = Guid.NewGuid();

            _updateWriter.Write(request);

            FdpLog<NedCommandGateway>.Info(
                "[GW] Sent UpdateEntityDescriptorRequest for Entity {0} ({1})",
                request.EntityId,
                request.DescriptorType);
        }

        // ── ICommandGateway ───────────────────────────────────────────────────

        /// <inheritdoc/>
        async Task<int> ICommandGateway.CreateEntityAsync(CreateEntityCommand cmd, CancellationToken ct)
        {
            var request = NedTranslationHelper.ToCreateEntityRequest(cmd);
            var ack     = await CreateEntityAsync(request).ConfigureAwait(false);
            return ack.EntityId;
        }

        /// <inheritdoc/>
        Task ICommandGateway.SendUpdateDescriptorAsync(UpdateEntityDescriptorCommand cmd, CancellationToken ct)
        {
            var request = NedTranslationHelper.ToUpdateDescriptorRequest(cmd);
            SendUpdateDescriptor(request);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        async Task<MissionCommitResult> ICommandGateway.SendMissionControlRequestAsync(
            MissionControlCommand cmd, CancellationToken ct)
        {
            var request = NedTranslationHelper.ToMissionControlRequest(cmd);
            var ack     = await SendMissionControlRequestAsync(request).ConfigureAwait(false);
            return new MissionCommitResult
            {
                Success      = ack.ErrorCode == 0,
                ErrorMessage = ack.ErrorCode == 0 ? string.Empty : $"Error {ack.ErrorCode}",
                NewVersion   = ack.NewVersion,
                ErrorCode    = ack.ErrorCode,
            };
        }

        public void Dispose()
        {
            _createEntityClient?.Dispose();
            _missionControlClient?.Dispose();
            _updateWriter?.Dispose();
        }
    }
}