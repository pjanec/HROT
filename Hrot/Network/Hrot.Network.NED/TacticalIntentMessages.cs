using System.Collections.Generic;
using CycloneDDS.Schema;

namespace Hrot.NED.Messages
{
    /// <summary>
    /// DDS wire message for broadcasting a tactical intent from a Commander Brain node
    /// to a subordinate Brain node.
    ///
    /// <para>
    /// Transported on the <c>"TacticalIntentRequest"</c> DDS topic
    /// (ordinal <see cref="Hrot.NED.Descriptors.EDescriptorType.dtTacticalIntentRequest"/> = 92).
    /// </para>
    ///
    /// <para>
    /// The receiver (<see cref="TacticalIntentIngressTranslator"/>) resolves
    /// <see cref="TargetEntityId"/> to a local ECS <c>Entity</c> via
    /// <c>NetworkEntityMap</c> and publishes an <c>AssignTacticalIntentEvent</c>
    /// on the local bus for <c>TacticalIntentResolutionSystem</c> to process.
    /// </para>
    /// </summary>
    [DdsStruct]
    [DdsIdlFile("hrot-tactical-intent")]
    [DdsManaged]
    public partial struct TacticalIntentRequest
    {
        /// <summary>Network entity ID of the subordinate entity receiving the intent.</summary>
        public long TargetEntityId;

        /// <summary>Generic intent identifier, e.g. <c>"DefendArea"</c>.</summary>
        public string IntentId;

        /// <summary>JSON-serialized intent parameters matching the target DTO.</summary>
        public string JsonParams;

        /// <summary>⭐ <c>CE-3034</c> — who asks (<c>BehaviorOrigin</c> as a byte; 0 = unmarked, ranks as Operator).
        /// The receiving node's gate compares it with what the unit already runs.</summary>
        public byte Origin;

        /// <summary>⭐ <c>CE-3167</c> — <c>TacticalIntentKind</c> as a byte: 0 = assign, 1 = clear the running behaviour.</summary>
        public byte Kind;

        /// <summary>⭐ <c>CE-3167</c> — <see cref="TacticalIntentFlags"/>: bit 0 = <c>ClearsPriorIntent</c>.</summary>
        public byte Flags;
    }

    /// <summary>⭐ <c>CE-3167</c> — the bits of <see cref="TacticalIntentRequest.Flags"/> (outside the DDS struct: its generator
    /// serialises every member, constants included).</summary>
    public static class TacticalIntentFlags
    {
        /// <summary>The owner clears the unit's plans (mission) before the order starts.</summary>
        public const byte ClearsPriorIntent = 1;
    }
}
