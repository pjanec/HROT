using Bagira.BDC.SSTM;
using FDP.Toolkit.Commands;
using CycloneDDS.Core;
using CycloneDDS.Runtime;
using System;
using System.Threading.Tasks;

namespace FDP.Toolkit.Commands.Examples
{
    /// <summary>
    /// A gateway for executing BDC-specific commands over DDS.
    /// This wraps generic DdsCommandClient instances for specific request types.
    /// </summary>
    public class BdcCommandGateway : IDisposable
    {
        private readonly DdsCommandClient<CreateEntityRequest, CreateEntityAck> _createEntityClient;
        
        /// <summary>
        /// creates a new gateway instance.
        /// </summary>
        /// <param name="participant">The DDS participant to use for communication.</param>
        public BdcCommandGateway(DdsParticipant participant)
        {
            if (participant == null) throw new ArgumentNullException(nameof(participant));

            // Initialize the client for CreateEntityRequest
            _createEntityClient = new DdsCommandClient<CreateEntityRequest, CreateEntityAck>(
                participant,
                "CreateEntityRequest", // Must match [DdsTopic] attribute
                "CreateEntityAck",     // Must match [DdsTopic] attribute
                req => req.RequestId,
                ack => ack.RequestId
            );
        }

        /// <summary>
        /// Sends a CreateEntityRequest and awaits the acknowledgment.
        /// If RequestId is empty, a new Guid is generated.
        /// </summary>
        /// <param name="request">The request data.</param>
        /// <param name="timeoutMs">Timeout in milliseconds.</param>
        /// <returns>The acknowledgment containing the new EntityId or error code.</returns>
        public async Task<CreateEntityAck> CreateEntityAsync(CreateEntityRequest request, int timeoutMs = 5000)
        {
            // If the caller didn't provide an ID, generate one.
            // Since CreateEntityRequest is a struct, we must ensure we pass the modified version.
            if (request.RequestId == Guid.Empty)
            {
                request.RequestId = Guid.NewGuid();
            }

            return await _createEntityClient.SendAsync(request, timeoutMs);
        }

        public void Dispose()
        {
            _createEntityClient?.Dispose();
        }
    }
}
