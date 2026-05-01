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
    }
}
