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

        /// <summary>⭐ <c>CE-3167</c> — <c>TacticalIntentKind</c> as a byte: 0 = assign, 1 = clear the running behaviour.
        /// ⭐ <c>CE-3251</c> (BR-13) — or a DIRECTIVE to the brain's owner (<see cref="TacticalIntentWireKind"/>): 2 = set the ROE,
        /// its fields as JSON in <see cref="JsonParams"/>; <see cref="IntentId"/> is empty then.</summary>
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

    /// <summary>⭐ <c>CE-3251</c> (BR-13) — the values of <see cref="TacticalIntentRequest.Kind"/>. 0 and 1 are
    /// <c>TacticalIntentKind</c> (an order); from 2 up a directive that changes what the unit's brain obeys, sent on the order's
    /// own road so it reaches the node that owns the brain (an order and a directive written in one frame stay in order on the
    /// one writer). 📄 <c>docs/DESIGN_Entity_Control.md</c> §2.3a.</summary>
    public static class TacticalIntentWireKind
    {
        /// <summary>An order: start a behaviour (<c>TacticalIntentKind.Assign</c>).</summary>
        public const byte Assign = 0;
        /// <summary>An order: clear the running behaviour (<c>TacticalIntentKind.Clear</c>).</summary>
        public const byte Clear = 1;
        /// <summary>A <c>SetRoeEvent</c> for a unit whose brain runs elsewhere — <c>RoeDirectiveJson</c> in the JSON.</summary>
        public const byte SetRoe = 2;
        /// <summary>A <c>SetReportingEvent</c> (Command Chain CC-2) for a unit whose brain runs elsewhere — <c>ReportingDirectiveJson</c> in
        /// the JSON. ⭐ <c>CE-2299</c> (behaviors, 2026-10-19).</summary>
        public const byte SetReporting = 3;
        /// <summary>⭐ <c>CE-3261</c> — a <c>SetEngagementAreaEvent</c> (behaviors' CE-2282 sector, the ROE twin) for a unit whose brain runs
        /// elsewhere — <c>EngagementAreaDirectiveJson</c> in the JSON.</summary>
        public const byte SetEngagementArea = 4;
    }
}
