using System;
using System.Runtime.InteropServices;
using Fdp.Kernel;
using Hrot.Core.Mission;

namespace Hrot.Common.Events
{
    /// <summary>
    /// Cross-boundary intent published by <c>MissionControlIngressTranslator</c>
    /// when a mission command must traverse the bus.
    /// This is a managed class (not a value type) because <see cref="MissionCommandPayload"/>
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

        /// <summary>Strongly-typed neutral mission command payload.</summary>
        public MissionCommandPayload Payload = new();
    }

    /// <summary>
    /// Outcome event published after processing a <see cref="MissionControlIntent"/>.
    /// Unmanaged struct routed via FdpEventBus.Publish / Consume.
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
