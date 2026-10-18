using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;

namespace Fdp.Toolkit.Squad.Command
{
    // ⭐⭐ CE-2291 CC-A (R-2043 CC-1, R-2050 CC-16) — a commander's knowledge as a UNIT: three components, written by the staff systems,
    //   read by any behaviour through the picture guards. ⭐ Every buffer is a FIXED-LIST WRAPPER (exactly `int Count` + one [InlineArray]
    //   field, FixedListShape) so GET /entities/{id} prints it as a plain array (FixedListJsonConverter) — no special reader (CC-16).
    //   Brain-local and never saved (NoScenario): rebuilt from reports. 📄 docs/designs/group-maneuvers/DESIGN_Command_Chain_Example.md §6, §8.

    /// <summary>One element a subordinate REPORTED (SPOT / CONTACT, CB-33 as revised by CC-1).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ReportedElement
    {
        public Vector3 At;
        public float Radius;
        public byte Count;
        public ElementSize Size;
        public ElementActivity Activity;
        public ElementEquipment Equipment;
        /// <summary>The reporting subordinate's network id.</summary>
        public long FromNetId;
        /// <summary>CB-31 — when it was observed (sim s); the element ages from it.</summary>
        public float ObservedAt;
        /// <summary>When the report arrived here (sim s) — CC-12 shows both.</summary>
        public float ReceivedAt;
        public uint Seq;
        public byte Synthetic;
    }

    [InlineArray(CommandPicture.MaxElements)]
    public struct ReportedElementSlots { private ReportedElement _e0; }

    /// <summary>The picture's elements (a fixed-list wrapper).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ReportedElementList
    {
        public int Count;
        public ReportedElementSlots Items;
    }

    /// <summary>What a commander last heard from ONE direct subordinate.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SubordinateRow
    {
        /// <summary>The subordinate's network id.</summary>
        public long UnitNetId;
        /// <summary>The LATEST PROGRESS it reported for its current order (<see cref="ProgressKind"/>, not ordinal: FallingBack after
        /// Foothold is news, not a regression); reset when the commander orders it again (CC-B).</summary>
        public ProgressKind Progress;
        public StatusColour Strength;
        public StatusColour Ammo;
        public ushort Phase;
        /// <summary>When its last report of any kind arrived (sim s) — "overdue" is measured from it (CC-12).</summary>
        public float LastReportAt;
        public float LastSitrepAt;
        /// <summary>The newest sequence taken in from it (CB-32: an older one arriving late is dropped).</summary>
        public uint LastSeq;
    }

    [InlineArray(CommandPicture.MaxSubordinates)]
    public struct SubordinateRowSlots { private SubordinateRow _e0; }

    /// <summary>The picture's subordinates (a fixed-list wrapper).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SubordinateRowList
    {
        public int Count;
        public SubordinateRowSlots Items;
    }

    /// <summary>A REQUEST a subordinate sent, not yet answered.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PendingRequest
    {
        public long FromNetId;
        public RequestKind Kind;
        public Vector3 At;
        public float ReceivedAt;
        public uint Seq;
    }

    [InlineArray(CommandPicture.MaxRequests)]
    public struct PendingRequestSlots { private PendingRequest _e0; }

    /// <summary>The picture's open requests (a fixed-list wrapper).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PendingRequestList
    {
        public int Count;
        public PendingRequestSlots Items;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2291</c> CC-A — on a commander that RECEIVES reports: what its subordinates told it. Written ONLY by the staff's inbox
    /// (CC-C, <c>StaffInboxSystem</c>) and a play's own <c>ProgressReached</c> reset (CC-B); read by the picture guards. ⭐ One per entity,
    /// whichever behaviour runs — a reaction that pauses the task reads the same picture (R-2042). Sizes: PC-8.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.CommandPicture)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct CommandPicture
    {
        public const int MaxElements = 8;
        /// <summary>PC-8 — 3 squads + the weapons squad + two attachments.</summary>
        public const int MaxSubordinates = 6;
        public const int MaxRequests = 4;

        public ReportedElementList Elements;
        public SubordinateRowList Subordinates;
        public PendingRequestList Requests;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2291</c> CC-A (CC-2, CC-8) — on a commander that SENDS reports: the EXTRA requirements its superior set (the standing
    /// minimum needs none). Written ONLY by <see cref="ReportingRequirementsSystem"/> from a <see cref="SetReportingEvent"/> — the ROE twin:
    /// origin-gated, persists across tasks. ⛔ A behaviour never reads it (CC-8); the staff's composer does. The addressee is always the
    /// direct superior (<c>UnitSubordinate.Commander</c>, R-2040) — not stored here.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.ReportingRequirements)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct ReportingRequirements
    {
        public PirFlags Pir;
        public FfirFlags Ffir;
        /// <summary>A routine SITREP every this many seconds; 0 = none beyond the standing minimum.</summary>
        public float SitrepEverySeconds;
        /// <summary>PC-3 — the network id of the area entity <see cref="PirFlags.InArea"/> / <see cref="PirFlags.NearArea"/> refer to (0 = none).</summary>
        public long PirArea;
        /// <summary>Who set it last — a lower origin cannot change it (<see cref="BehaviorOriginRank"/>).</summary>
        public BehaviorOrigin SetBy;
    }

    [InlineArray(ReportOutbox.MaxQueued)]
    public struct QueuedReportSlots { private QueuedReport _e0; }

    /// <summary>A report waiting to be sent.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct QueuedReport
    {
        public CommandReport Report;
        /// <summary>When it may go (sim s) — the send delay (R-2031: 3 s by default).</summary>
        public float DueAt;
    }

    /// <summary>The outbox's queue (a fixed-list wrapper).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct QueuedReportList
    {
        public int Count;
        public QueuedReportSlots Items;
    }

    /// <summary>⭐ <c>CE-2291</c> — how a behaviour shapes its unit's reporting (CC-8).</summary>
    public enum ReportingPosture : byte
    {
        /// <summary>Everything goes when due.</summary>
        Normal = 0,
        /// <summary>Sneaking: everything is queued except a CONTACT under fire.</summary>
        Silent = 1,
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2291</c> CC-A — on a commander that SENDS reports: what is queued to go up, written by the staff's composer (CC-C) and a
    /// play's <c>SendReport</c> (CC-B), flushed when due. <see cref="NextSeq"/> numbers this sender's reports (CB-32).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.ReportOutbox)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct ReportOutbox
    {
        public const int MaxQueued = 8;

        public QueuedReportList Queued;
        /// <summary>The next report's sequence number (1-based; 0 = nothing sent yet).</summary>
        public uint NextSeq;
        public ReportingPosture Posture;
        /// <summary>CC-15 — lost comms: nothing is sent before this sim time.</summary>
        public float MutedUntil;
    }
}
