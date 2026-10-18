using System;
using System.Numerics;
using Fdp.Core;

namespace Fdp.Toolkit.Squad.Reports
{
    // ⚠ The enums of this file share one IDL scope when a wire type names them, so every member name is unique within the file.

    /// <summary>⭐ <c>CE-3250</c> (BR-3, CB-43) — the CLOSED set of report kinds a commander sends up. A new kind is a code change.
    /// 📄 docs/designs/group-maneuvers/DESIGN_Building_Clearing.md §5d.</summary>
    public enum ReportKindId : ushort
    {
        ReportKindUnset = 0,
        /// <summary>CONTACT — "contact, north, 200 m, two riflemen in the window".</summary>
        Contact = 1,
        /// <summary>SPOT (SALUTE) — size, activity, location, equipment.</summary>
        Spot = 2,
        /// <summary>SITREP — where we are, what we do, our colours (strength, ammo), phase.</summary>
        Sitrep = 3,
        /// <summary>LACE — ammo, casualties (WIA / KIA), key equipment lost.</summary>
        Lace = 4,
        /// <summary>PROGRESS — the play reached a point (<see cref="ProgressSubtype"/>).</summary>
        Progress = 5,
        /// <summary>REQUEST — CASEVAC, fire support, … (<see cref="RequestSubtype"/>); answered by an order citing <c>RequestId</c>.</summary>
        Request = 6,
    }

    /// <summary>PROGRESS's subtypes (CB-43; <see cref="FireLifted"/> PC-4 / PC-7).</summary>
    public enum ProgressSubtype : byte
    {
        ProgressUnset = 0, Set = 1, PhaseLine = 2, Foothold = 3, RoomClear = 4, RoomBlocked = 5, BuildingClear = 6,
        ObjectiveSecure = 7, Detained = 8, Displacing = 9, FallingBack = 10,
        /// <summary>The support squad lifted or shifted its fire (the lift hand-off).</summary>
        FireLifted = 11,
    }

    /// <summary>REQUEST's subtypes (CB-43).</summary>
    public enum RequestSubtype : byte { RequestUnset = 0, Casevac = 1, FireSupport = 2, Resupply = 3, Reinforce = 4, Permission = 5 }

    /// <summary>A SITREP / LACE colour (CB-37): combat power or ammo.</summary>
    public enum ReportColour : byte { ColourUnset = 0, Green = 1, Amber = 2, Red = 3, Black = 4 }

    /// <summary>SPOT size class.</summary>
    public enum SpotSize : byte { SizeUnset = 0, Individual = 1, Team = 2, SquadSize = 3, PlatoonSize = 4 }

    /// <summary>SPOT activity.</summary>
    public enum SpotActivity : byte { ActivityUnset = 0, Firing = 1, Moving = 2, Static = 3, Withdrawing = 4, Surrendering = 5 }

    /// <summary>SPOT equipment / LACE equipment lost.</summary>
    [Flags]
    public enum ReportEquipment : byte { EquipmentNone = 0, MachineGun = 1, Sniper = 2, AntiTank = 4, Vehicle = 8 }

    /// <summary>
    /// ⭐ <c>CE-3250</c> (BR-3, R-2031, CB-15, CB-31…CB-34, CB-43) — ONE report up the chain of command, as the local FDP event every node
    /// reads. The sender's staff publishes it when the report is DUE (its delay is the sender's, never the wire's); the
    /// <c>SquadReport</c> topic carries it to every other node, whose ingress raises the same event with <see cref="OriginNodeId"/> set.
    /// 📄 docs/designs/group-maneuvers/DESIGN_Building_Clearing.md §3 (the SquadReport box), §5b (a report's life), §5d (the closed set).
    /// </summary>
    public sealed class SquadReportEvent
    {
        /// <summary>The unit that reports (its commander entity).</summary>
        public Entity FromUnit = Entity.Null;
        /// <summary>The unit it reports to (its direct superior).</summary>
        public Entity ToUnit = Entity.Null;
        public ReportKindId KindId;
        /// <summary>The kind's subtype (<see cref="ProgressSubtype"/>, <see cref="RequestSubtype"/>; 0 for the others).</summary>
        public byte Subtype;
        /// <summary>What it is about — the contact, the casualty, the detainee (null = none).</summary>
        public Entity Subject = Entity.Null;
        /// <summary>The building (its index in the terrain's building list; −1 = none). A room is named by <see cref="Position"/> inside
        /// it — rooms are derived per answer and have no id (R-2038).</summary>
        public int Building = -1;
        /// <summary>Where (the contact, the room's floor centre, the phase line point).</summary>
        public Vector3 Position;
        /// <summary>A count: men seen, men down, the phase line's index (PROGRESS PhaseLine).</summary>
        public int Count;
        /// <summary>SPOT: the error radius of <see cref="Position"/> (m).</summary>
        public float Radius;
        public SpotSize Size;
        public SpotActivity Activity;
        /// <summary>SPOT equipment seen; LACE equipment lost.</summary>
        public ReportEquipment Equipment;
        /// <summary>SITREP / LACE: combat power.</summary>
        public ReportColour Strength;
        /// <summary>SITREP / LACE: ammo.</summary>
        public ReportColour Ammo;
        /// <summary>SITREP: the play's phase (its own numbering).</summary>
        public byte Phase;
        /// <summary>LACE: wounded / killed.</summary>
        public byte Wounded, Killed;
        /// <summary>CB-31 — when the sender SAW what it reports (sim s); a report's age is measured from it.</summary>
        public double ObservedAt;
        /// <summary>When the sender published it (sim s).</summary>
        public double SentAt;
        /// <summary>CB-32 — per sender, increasing: the newest wins, a late older one is dropped.</summary>
        public uint Seq;
        /// <summary>A REQUEST's id, cited by the order that answers it (0 = none).</summary>
        public uint RequestId;
        /// <summary>CC-14 (R-2045) — injected over the debug HTTP, not made by a staff.</summary>
        public bool Synthetic;
        /// <summary>The node it was raised on: 0 = THIS node (the egress sends it); otherwise it arrived from that node.</summary>
        public long OriginNodeId;
    }
}
