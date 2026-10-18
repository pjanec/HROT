using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Fdp.Toolkit.Squad.Command
{
    // ⭐⭐ CE-2291 CC-A (R-2043…R-2046, R-2050) — the vocabulary of a report: the CLOSED set (Building Clearing CB-43) — six kinds, closed
    //   subtype enums, extended only in code. ⚠ Member names are unique across these enums (the DDS IDL puts every enum member of a module
    //   in ONE scope, the Roe precedent) because BR-3's SquadReport topic will carry them.
    //   📄 docs/designs/group-maneuvers/DESIGN_Command_Chain_Example.md §6 · DESIGN_Building_Clearing.md §5d.

    /// <summary>⭐ <c>CE-2291</c> — the six doctrinal report kinds (CB-37, CB-43).</summary>
    public enum ReportKind : byte
    {
        /// <summary>No report (an empty slot).</summary>
        NoReport = 0,
        /// <summary>CONTACT — "contact, direction, distance, what": the first word, at once.</summary>
        Contact = 1,
        /// <summary>SPOT (SALUTE) — an element: size, activity, location, unit, time, equipment.</summary>
        Spot = 2,
        /// <summary>SITREP — where we are, what we do, our colours.</summary>
        Sitrep = 3,
        /// <summary>LACE — ammunition, casualties, equipment, after contact.</summary>
        Lace = 4,
        /// <summary>PROGRESS — a phase report (<see cref="ProgressKind"/>).</summary>
        Progress = 5,
        /// <summary>REQUEST — something only the superior can give (<see cref="RequestKind"/>).</summary>
        Request = 6,
    }

    /// <summary>⭐ <c>CE-2291</c> — PROGRESS subtypes (CB-43 + PC-4 <c>FireLifted</c>), ordered so a guard can ask "at least Set".</summary>
    public enum ProgressKind : byte
    {
        NoProgress = 0,
        /// <summary>In position (the task ended where it was sent).</summary>
        Set = 1,
        /// <summary>A phase line crossed; its index rides in <see cref="CommandReport.Count"/> (PC-4).</summary>
        PhaseLine = 2,
        Foothold = 3,
        RoomClear = 4,
        RoomBlocked = 5,
        BuildingClear = 6,
        ObjectiveSecure = 7,
        Detained = 8,
        Displacing = 9,
        FallingBack = 10,
        /// <summary>PC-4 — the support has stopped firing on the entry face (the lift hand-off, PC-7).</summary>
        FireLifted = 11,
        /// <summary>CC-5 — the task FAILED (the staff's PROGRESS on a task that did not succeed).</summary>
        TaskFailed = 12,
    }

    /// <summary>⭐ <c>CE-2291</c> — REQUEST subtypes (CB-43).</summary>
    public enum RequestKind : byte
    {
        NoRequest = 0,
        Casevac = 1,
        FireSupport = 2,
        Resupply = 3,
        Reinforce = 4,
        Permission = 5,
    }

    /// <summary>⭐ <c>CE-2291</c> — SALUTE's S: the size of an element.</summary>
    public enum ElementSize : byte
    {
        SizeUnknown = 0,
        Individual = 1,
        Team = 2,
        SquadSize = 3,
        PlatoonSize = 4,
    }

    /// <summary>⭐ <c>CE-2291</c> — SALUTE's A.</summary>
    public enum ElementActivity : byte
    {
        ActivityUnknown = 0,
        Firing = 1,
        Moving = 2,
        Static = 3,
        Withdrawing = 4,
        Surrendering = 5,
    }

    /// <summary>⭐ <c>CE-2291</c> — SALUTE's E, as flags.</summary>
    [Flags]
    public enum ElementEquipment : byte
    {
        NoEquipment = 0,
        MachineGun = 1,
        Sniper = 2,
        AntiTank = 4,
        Vehicle = 8,
    }

    /// <summary>⭐ <c>CE-2291</c> — a status colour (SITREP strength / ammo, CB-37): GREEN full … BLACK combat ineffective.</summary>
    public enum StatusColour : byte
    {
        ColourUnknown = 0,
        Green = 1,
        Amber = 2,
        Red = 3,
        Black = 4,
    }

    /// <summary>⭐ <c>CE-2291</c> — the superior's priority intelligence requirements (CC-8: EXTRAS over the standing minimum), as flags.</summary>
    [Flags]
    public enum PirFlags : uint
    {
        NoPir = 0,
        /// <summary>Any new element at once (not only at team size or larger).</summary>
        AnyElement = 1,
        /// <summary>An element with a machine gun.</summary>
        PirMachineGun = 2,
        PirSniper = 4,
        PirAntiTank = 8,
        PirVehicle = 16,
        /// <summary>PC-3 — an element INSIDE <see cref="ReportingRequirements.PirArea"/> (the EA, the squad's sector).</summary>
        InArea = 32,
        /// <summary>PC-3 — an element outside the area but within 150 m of it.</summary>
        NearArea = 64,
    }

    /// <summary>⭐ <c>CE-2291</c> — the superior's friendly-force requirements (CC-8 extras), as flags.</summary>
    [Flags]
    public enum FfirFlags : uint
    {
        NoFfir = 0,
        /// <summary>Every phase line, not only Set (the standing minimum).</summary>
        EveryPhase = 1,
        /// <summary>A SITREP whenever a colour changes (the minimum sends one only on RED / BLACK).</summary>
        EveryColourChange = 2,
        /// <summary>A LACE after every contact.</summary>
        LaceAfterContact = 4,
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2291</c> CC-A — ONE report: the kind, its subtype, and the closed set's fixed fields (CB-43: no free payload). The same
    /// struct sits in a sender's <see cref="ReportOutbox"/>, in the local notification, and (BR-3) on the wire. Times are sim seconds.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CommandReport
    {
        public ReportKind Kind;
        /// <summary>The kind's subtype: a <see cref="ProgressKind"/> or a <see cref="RequestKind"/> (else 0).</summary>
        public byte Subtype;
        /// <summary>SPOT / CONTACT: where (+ <see cref="Radius"/>); PROGRESS / SITREP: where the unit is.</summary>
        public Vector3 At;
        public float Radius;
        /// <summary>SPOT: how many; PROGRESS PhaseLine: the line's index (PC-4).</summary>
        public byte Count;
        public ElementSize Size;
        public ElementActivity Activity;
        public ElementEquipment Equipment;
        /// <summary>SITREP / LACE.</summary>
        public StatusColour Strength;
        public StatusColour Ammo;
        /// <summary>LACE: wounded and killed.</summary>
        public byte Wia;
        public byte Kia;
        /// <summary>SITREP: the sender's phase (its play's own numbering).</summary>
        public ushort Phase;
        /// <summary>CB-31 — when the reported fact was OBSERVED; a report ages from it, not from its arrival.</summary>
        public float ObservedAt;
        /// <summary>CB-32 — per-sender sequence: the newest wins, a late older one is dropped.</summary>
        public uint Seq;
        /// <summary>CC-14 — faked over the debug HTTP (<c>ReportInjection</c>): it bypasses the newest-per-sender drop, and the sender's next
        /// real report of the same subject replaces it. 1 = synthetic.</summary>
        public byte Synthetic;
    }
}
