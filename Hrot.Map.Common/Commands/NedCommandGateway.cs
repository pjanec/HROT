using Hrot.NED.Messages;
using FDP.Toolkit.Commands;
using CycloneDDS.Core;
using CycloneDDS.Runtime;
using FDP.Kernel.Logging;
using System;
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
    public class NedCommandGateway : INedCommandGateway, IDisposable
    {
        private readonly DdsCommandClient<CreateEntityRequest, CreateUpdateDeleteEntityAck>    _createEntityClient;
        private readonly DdsCommandClient<MissionControlRequest, MissionControlAck>            _missionControlClient;
        private readonly DdsWriter<UpdateEntityDescriptorRequest>                              _updateWriter;

        /// <summary>
        /// creates a new gateway instance.
        /// </summary>
        /// <param name="participant">The DDS participant to use for communication.</param>
        public NedCommandGateway(DdsParticipant participant)
        {
            if (participant == null) throw new ArgumentNullException(nameof(participant));

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

            FdpLog<NedCommandGateway>.Debug("[TRACE-GW] Sending CreateEntityRequest ID={0}", request.RequestId);

            var ack = await _createEntityClient.SendAsync(request, timeoutMs);
            var ackDetails = string.Concat("Entity=", ack.EntityId, " Status=", ack.StatusCode);
            FdpLog<NedCommandGateway>.Debug(
                "[TRACE-GW] CreateUpdateDeleteEntityAck ID={0} {1}", ack.RequestId, ackDetails);
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
                "[TRACE-GW] Sending MissionControlRequest ID={0} Entity={1}",
                request.RequestId, request.TargetEntityId);

            var ack = await _missionControlClient.SendAsync(request, timeoutMs);
            FdpLog<NedCommandGateway>.Debug(
                "[TRACE-GW] MissionControlAck ID={0} Error={1}", ack.RequestId, ack.ErrorCode);
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

        public void Dispose()
        {
            _createEntityClient?.Dispose();
            _missionControlClient?.Dispose();
            _updateWriter?.Dispose();
        }
    }
}