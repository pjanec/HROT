using System;
using System.Runtime.InteropServices;
using Fdp.Kernel;
using Hrot.NED.Messages;

namespace Hrot.Common.Events
{
    /// <summary>
    /// Cross-boundary intent published by <c>MissionControlIngressTranslator</c>
    /// (SimHost) or <c>MissionEditorService</c> (ExCon) when a mission command
    /// must traverse the bus.
    ///
    /// This is a managed class (not a value type) because <see cref="MissionCommandUnion"/>
    /// contains managed reference fields (<see cref="MissionPlan"/>, task lists, etc.).
    /// Use <c>FdpEventBus.PublishManaged</c> / <c>ConsumeManaged</c> for routing.
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
        /// Strongly-typed mission command payload.
        /// </summary>
        public MissionCommandUnion Payload;
    }

    /// <summary>
    /// Outcome event published after processing a <see cref="MissionControlIntent"/>.
    ///
    /// On SimHost: published by <c>MissionControlExecutionSystem</c>,
    /// consumed by <c>MissionControlAckEgressTranslator</c>.
    ///
    /// On ExCon: published by <c>MissionControlAckIngressTranslator</c>,
    /// consumed by <c>MissionEditorService</c> to resolve pending commits.
    ///
    /// This is an unmanaged struct so it can be routed via the standard
    /// <c>FdpEventBus.Publish</c> / <c>Consume</c> path.
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
