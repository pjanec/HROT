using CycloneDDS.Schema;

namespace Hrot.NED.Messages
{
    /// <summary>
    /// ⭐ <b><c>CE-3250</c> (behaviors' BR-3, R-2031) — a report up the chain of command, on the wire.</b> 📄
    /// <c>docs/designs/group-maneuvers/DESIGN_Building_Clearing.md</c> §3, §5b, §5d.
    ///
    /// <para>⭐ Its fields are behaviors' <c>CommandReport</c> one for one (CE-2291's closed set) plus the envelope — one vocabulary.</para>
    ///
    /// <para>Event-shaped, on the <c>BehaviorFaultReport</c> precedent: published once by the node that raised the local
    /// <c>SquadReportNotification</c> (<c>SquadReportEgressTranslator</c>) and raised again as that event on every other node
    /// (<c>SquadReportIngressTranslator</c>). Topic <c>Hrot_NED_Messages_SquadReport</c> (derived from the type), ordinal <see cref="Hrot.NED.Descriptors.EDescriptorType.dtSquadReport"/> = 146.
    /// ⛔ No delay on the wire: the sender's staff holds a report its <c>ReportDelaySeconds</c> and publishes when due.</para>
    /// </summary>
    [DdsTopic]   // ⭐ AQ107 A — was [DdsStruct] + a name in the translators; now a topic whose name is derived from the type
    [DdsIdlFile("hrot-squad-report")]
    [DdsManaged]
    public partial struct SquadReport
    {
        /// <summary>Network id of the reporting unit.</summary>
        public long FromUnit;
        /// <summary>Network id of the unit reported to.</summary>
        public long ToUnit;
        /// <summary>The node that raised it (a node skips its own sample).</summary>
        public long OriginNodeId;
        /// <summary><c>ReportKind</c> (behaviors' <c>CommandReport.Kind</c>, the closed set); an unknown value still crosses and is logged by the receiver.</summary>
        public byte Kind;
        /// <summary>The kind's subtype.</summary>
        public byte Subtype;
        /// <summary>Network id of what it is about (0 = none).</summary>
        public long Subject;
        /// <summary>The building's index (−1 = none).</summary>
        public int Building;
        /// <summary><c>CommandReport.At</c>.</summary>
        public float AtX;
        public float AtY;
        public float AtZ;
        public float Radius;
        public byte Count;
        public byte Size;
        public byte Activity;
        public byte Equipment;
        public byte Strength;
        public byte Ammo;
        public byte Wia;
        public byte Kia;
        public ushort Phase;
        /// <summary>When the sender saw it (sim s) — the report ages from it (CB-31).</summary>
        public float ObservedAt;
        /// <summary>When the sender published it (sim s).</summary>
        public double SentAt;
        /// <summary>Per sender, increasing (CB-32).</summary>
        public uint Seq;
        /// <summary>1 = injected over the debug HTTP (CC-14).</summary>
        public byte Synthetic;
    }
}
