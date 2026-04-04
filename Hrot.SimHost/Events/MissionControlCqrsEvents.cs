using System;
using System.Runtime.InteropServices;
using Fdp.Kernel;
using Hrot.NED.Messages;

namespace Hrot.SimHost.Events
{
    /// <summary>
    /// Cross-boundary intent published by <c>MissionControlIngressTranslator</c>
    /// when a <see cref="MissionControlRequest"/> DDS sample arrives.
    ///
    /// This is a managed class (not a value type) because <see cref="MissionCommandUnion"/>
    /// contains managed reference fields (<see cref="MissionPlan"/>, task lists, etc.).
    /// Use <c>World.Bus.PublishManaged</c> / <c>World.Bus.ConsumeManaged</c> for routing.
    ///
    /// Design note: the events file resides in <c>Hrot.SimHost</c> rather than
    /// <c>FDP.Toolkit.Behavior</c> because <see cref="MissionCommandUnion"/> is a
    /// Hrot.NED DDS-generated type; adding that dependency to the FDP toolkit layer
    /// would create bad coupling.
    /// </summary>
    public sealed class MissionControlIntent
    {
        /// <summary>Unique identifier that links this intent to its ACK.</summary>
        public Guid RequestId;

        /// <summary>Network entity ID of the mission target.</summary>
        public long TargetEntityId;

        /// <summary>Client-side version the request is based on (0 = unconditional).</summary>
        public long BaseVersion;

        /// <summary>
        /// Strongly-typed mission command payload (deserialized from the DDS wire message
        /// by <c>MissionControlIngressTranslator</c> before publishing this intent).
        /// </summary>
        public MissionCommandUnion Payload;
    }

    /// <summary>
    /// Outcome event published by <c>MissionControlExecutionSystem</c> after processing
    /// a <see cref="MissionControlIntent"/>.
    ///
    /// Consumed by <c>MissionControlAckEgressTranslator</c> which writes a
    /// <c>MissionControlAck</c> DDS message back to the requesting client.
    ///
    /// This is an unmanaged struct so it can be routed via the standard
    /// <c>World.Bus.Publish</c> / <c>World.Bus.Consume</c> path.
    /// Error details are passed via <see cref="ErrorCode"/>; the egress translator
    /// maps the code to a canonical error message string in the DDS ACK.
    /// </summary>
    [EventId(6002)]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MissionControlAckEvent
    {
        /// <summary>Matches the <see cref="MissionControlIntent.RequestId"/> that triggered this ACK.</summary>
        public Guid RequestId;

        /// <summary>Zero on success; non-zero NED status code on failure.</summary>
        public int ErrorCode;

        /// <summary>New version of the mission plan on the entity (0 on failure).</summary>
        public long NewVersion;
    }
}
