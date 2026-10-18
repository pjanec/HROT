using Fdp.Core;

namespace Fdp.Toolkit.Squad.Command
{
    /// <summary>
    /// ⭐ <c>CE-3250</c> (BR-3, R-2031, CB-15, CB-31…CB-34) — ONE report up the chain of command, as the local FDP event every node reads:
    /// the ENVELOPE (who, to whom, about what, when it was sent) around behaviors' <see cref="CommandReport"/> (the closed set, CE-2291 —
    /// one vocabulary for the outbox, the notification and the wire). The sender's staff publishes it when the report is DUE (its delay
    /// is the sender's, never the wire's); the <c>SquadReport</c> topic carries it to every other node, whose ingress raises the same
    /// event with <see cref="OriginNodeId"/> set. 📄 docs/designs/group-maneuvers/DESIGN_Building_Clearing.md §5d ·
    /// DESIGN_Command_Chain_Example.md §3 (the <c>SquadReportNotification</c> box).
    /// </summary>
    public sealed class SquadReportNotification
    {
        /// <summary>The unit that reports (its commander entity).</summary>
        public Entity FromUnit = Entity.Null;
        /// <summary>The unit it reports to (its direct superior).</summary>
        public Entity ToUnit = Entity.Null;
        /// <summary>What it is about — the contact, the casualty, the detainee (null = none).</summary>
        public Entity Subject = Entity.Null;
        /// <summary>The building (its index in the terrain's building list; −1 = none). A room is named by <see cref="CommandReport.At"/>
        /// inside it — rooms are derived per answer and have no id (R-2038).</summary>
        public int Building = -1;
        /// <summary>When the sender published it (sim s). The report ages from <see cref="CommandReport.ObservedAt"/>, not from this.</summary>
        public double SentAt;
        /// <summary>The report itself (kind, subtype, the kind's fields, <c>ObservedAt</c>, <c>Seq</c>, <c>Synthetic</c>).</summary>
        public CommandReport Report;
        /// <summary>The node it was raised on: 0 = THIS node (the egress sends it); otherwise it arrived from that node.</summary>
        public long OriginNodeId;
    }
}
