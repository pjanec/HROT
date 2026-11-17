using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Aggregate
{
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // ⭐⭐⭐ CE-4616 / CE-8000 — THE AGGREGATE-UNIT CONTRACT (docs/designs/aggregate-units/DESIGN_Stochastic_Units_And_Resolution.md §23.2).
    // Declared by exp/agg-backend; exp/agg-behaviors and exp/agg-ui build against these names and layouts
    // (docs/blueprints/batches/NOTE_AggBackend_To_Agg_Contract.md). ⛔ SV-12: a recorded layout change refuses old recordings
    // (FlightRecorder/SchemaValidator) — EVERY recorded field of §14–§19 is declared HERE, now; a later addition is a deliberate, versioned break.
    // Every struct is unmanaged and ≤ 1024 B (RV-11, EntityCommandBuffer's limit) — pinned by AggregateContractRails.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>The resolution a unit is simulated at (X-1, SV-2). ⭐ The wire / recording values are FIXED (§23.2): 1 / 2 / 3.</summary>
    public enum AggregateResolution : byte
    {
        /// <summary>Not set — read as <see cref="Physical"/> (a unit with no <see cref="AggregateUnit"/> is physical).</summary>
        Unset = 0,
        /// <summary>Real members — every man and vehicle is an entity.</summary>
        Physical = 1,
        /// <summary>FusionSquads — one soldier each, vehicles real (R-4247).</summary>
        Fusion = 2,
        /// <summary>No members exist: the unit's slot table and holdings ARE the unit (§10, S-1).</summary>
        Stochastic = 3,
    }

    /// <summary>The formation the unit moves and stands in (M-3).</summary>
    public enum UnitFormation : byte
    {
        /// <summary>Not set (a stopped unit with no order) — drawn as <see cref="Line"/>.</summary>
        NoFormation = 0,
        /// <summary>A road column: the unit is a SEGMENT of its path, the tail <see cref="AggregateUnit.Depth"/> behind the head.</summary>
        Column = 1,
        /// <summary>A deployed line: frontage <see cref="AggregateUnit.Width"/> × <see cref="AggregateUnit.Depth"/>.</summary>
        Line = 2,
        /// <summary>The assault: a line inside the assault distance (B-4) — the assault modifiers apply.</summary>
        Assault = 3,
    }

    /// <summary>Mounted / dismounted (S-9): speed, exposure, which systems fire, which navmesh layer plans the route (M-1).</summary>
    public enum UnitPosture : byte
    {
        Dismounted = 0,
        Mounted = 1,
    }

    /// <summary>What the unit is doing that raises its signature (N-4).</summary>
    [Flags]
    public enum UnitActivity : byte
    {
        Quiet = 0,
        Moving = 1 << 0,
        Firing = 1 << 1,
    }

    /// <summary>The de-aggregation process stage (D-P2, §16a) — recorded, so a seek mid-split restores it.</summary>
    public enum SplitStage : byte
    {
        NotSplitting = 0,
        /// <summary>Each level's real planner, top-down, children in parallel.</summary>
        Planning = 1,
        /// <summary>Leaf posts known; entities created bottom-up and held (SV-6).</summary>
        Materialising = 2,
        /// <summary>The one-tick handover (D-P6).</summary>
        Handover = 3,
        /// <summary>Re-aggregation in progress (D-P7: sum, then remove).</summary>
        Merging = 4,
    }

    /// <summary>Time in position (P-5): sets the cover factor in combat (§18) and the men's cover and stance on a split.</summary>
    public enum PreparedLevel : byte
    {
        Unprepared = 0,
        Hasty = 1,
        Prepared = 2,
        Fortified = 3,
    }

    /// <summary>The instructor's pin (§16b, SV-14) — ranks above the author's policy and the automatic manager.</summary>
    public enum ResolutionPin : byte
    {
        NoPin = 0,
        /// <summary>Never change the current resolution automatically.</summary>
        PinKeep = 1,
        /// <summary>Keep the unit physical (split now if it is not).</summary>
        PinPhysical = 2,
        /// <summary>Keep the unit stochastic (merge now if it is split).</summary>
        PinStochastic = 3,
    }

    /// <summary>The unit's own movement state (B-2, M-4) — the path follower's.</summary>
    public enum UnitMoveState : byte
    {
        Stopped = 0,
        /// <summary>A route was requested and has not arrived (the solver runs at 10 Hz, ≤ 256 per tick — RV-17).</summary>
        AwaitingRoute = 1,
        OnRoute = 2,
        Arrived = 3,
        /// <summary>The route was refused / dropped — re-requested by the play (B-2).</summary>
        RouteFailed = 4,
    }

    /// <summary>The incoming fire per 1 s step over the last <see cref="AggregateUnit.SuppressionWindowSteps"/> steps (C-4).</summary>
    [InlineArray(AggregateUnit.SuppressionWindowSteps)]
    public struct IncomingFireWindow
    {
#pragma warning disable CS0169
        private float _rounds;
#pragma warning restore CS0169
    }

    /// <summary>
    /// ⭐⭐⭐ <c>AggregateUnit</c> (281, <c>dtAggregateUnit</c> 220) — the unit entity's resolution and its stochastic state. ⭐ The UNIT entity
    /// persists across every resolution (X-1); only <see cref="Resolution"/> changes. Written ONLY by the unit's own per-entity steps (the
    /// path follower, the attrition phase B, the sensor step) and the serial resolution manager. Present strength is DERIVED from the
    /// <see cref="UnitSlotTable"/>, never stored here (SV-11). The current ORDER is derived from <c>BehaviorState</c> /
    /// <c>BehaviorStartRecord</c>, never stored (RV-17, X-2).
    /// </summary>
    [ComponentId(GlobalComponentIds.AggregateUnit)]
    [StructLayout(LayoutKind.Sequential)]
    public struct AggregateUnit
    {
        /// <summary>C-4: incoming fire is summed over this many 1 s steps.</summary>
        public const int SuppressionWindowSteps = 5;

        // ── resolution (X-1, SV-2, §16a, §16b) ──
        /// <summary>1 Physical · 2 Fusion · 3 Stochastic (§23.2). ⭐ An INPUT to the routing table (SV-2), never part of the type.</summary>
        public AggregateResolution Resolution;
        /// <summary><c>Fdp.Toolkit.Squad.Echelon</c>: 0 squad, 1 platoon, 2 company … — sets the slot capacity (<see cref="UnitSlotTable.CapacityFor"/>).</summary>
        public byte Echelon;
        /// <summary>The de-aggregation stage (D-P2).</summary>
        public SplitStage Splitting;
        /// <summary>The instructor's pin (SV-14).</summary>
        public ResolutionPin Pin;

        // ── shape (M-3) ──
        public UnitFormation Formation;
        /// <summary>Mounted / dismounted (S-9).</summary>
        public UnitPosture Posture;
        /// <summary>Moving / firing — the signature (N-4).</summary>
        public UnitActivity Activity;
        /// <summary>Time in position (P-5).</summary>
        public PreparedLevel Prepared;
        /// <summary>Frontage (m).</summary>
        public float Width;
        /// <summary>Depth (m): a column's length — the tail is this far behind the head along the path (M-3).</summary>
        public float Depth;
        /// <summary>The direction the unit faces / moves.</summary>
        public Bearing Heading;

        // ── movement (M-1 … M-4, B-2) ──
        public UnitMoveState Move;
        private byte _pad0, _pad1, _pad2;
        /// <summary>Where the current move ends.</summary>
        public WorldPoint Destination;
        /// <summary>The head's distance along the current route (m) — the path follower's state (M-4).</summary>
        public float PathDistanceM;
        /// <summary>The current speed (m/s) — M-2's model, written by the follower.</summary>
        public float SpeedMps;
        /// <summary>When the current route was requested (re-requested when dropped, B-2).</summary>
        public SimInstant RouteRequestedAt;

        // ── combat state (C-4, C-5, §16b) ──
        /// <summary>The share of the unit's slots under fire (0..1) — incoming rounds per exposed man over the last 5 s (C-4).</summary>
        public float SuppressedShare;
        /// <summary>Incoming rounds per 1 s step, a ring over <see cref="SuppressionWindowSteps"/> steps (C-4).</summary>
        public IncomingFireWindow IncomingRounds;
        /// <summary>The ring's next write index.</summary>
        public byte IncomingHead;
        private byte _pad3, _pad4, _pad5;
        /// <summary>The last time the unit fired (§16b quiet time; the signature N-4).</summary>
        public SimInstant LastFiredAt;
        /// <summary>The last time the unit was fired at or detected an enemy (§16b "out of contact for T"; the stress calm-down after 30 s quiet).</summary>
        public SimInstant LastContactAt;

        // ── time stamps of the processes (P-5, D-P5) ──
        /// <summary>When the unit settled in its current position — <see cref="Prepared"/> grows from it (P-5).</summary>
        public SimInstant PreparedSince;
        /// <summary>When the current split / merge began — the deadline (D-P5) counts from it.</summary>
        public SimInstant SplittingSince;
        /// <summary>The last step the attrition / sensor steps ran for this unit (the 1 s fixed-step accumulator, S-3).</summary>
        public SimInstant LastStepAt;

        /// <summary>The resolution, with <see cref="AggregateResolution.Unset"/> read as physical.</summary>
        public readonly AggregateResolution Effective => Resolution == AggregateResolution.Unset ? AggregateResolution.Physical : Resolution;
    }

    /// <summary>The role of a slot (X-4: who lives longest; C-1: which weapon the slot fires).</summary>
    public enum SlotRole : byte
    {
        Empty = 0,
        Rifleman = 1,
        Leader = 2,
        MachineGunner = 3,
        AntiTank = 4,
        Grenadier = 5,
        Marksman = 6,
        Medic = 7,
        Crew = 8,
        Radio = 9,
    }

    /// <summary>
    /// One man of a stochastic unit (SV-1) — 8 B so a company fits one table (<see cref="UnitSlotTable.Capacity"/>). ⭐ Health and stress are
    /// <see cref="Half"/> (health 25 is exact to 1/64; stress 0..1 to ~1/2000), deterministic everywhere. A slot is DOWN when
    /// <see cref="Health"/> is below the incapacitation threshold (C-3); its wounds stay (they survive a split, SV-1).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UnitSlot
    {
        /// <summary>Health points (the same scale as <c>Health</c> on a soldier: a man is 25).</summary>
        public Half Health;
        /// <summary>Stress 0..1 with the <c>StressModel</c> constants (C-5); broken ⇒ not standing.</summary>
        public Half Stress;
        /// <summary>Rounds left for the slot's personal weapon.</summary>
        public ushort Ammo;
        /// <summary>The slot's role (<see cref="SlotRole.Empty"/> = no man in this slot).</summary>
        public SlotRole Role;
        /// <summary>The index of the TKB composite slot this man is (X-3: the child key <c>child:&lt;slot&gt;</c>).</summary>
        public byte CompositeSlot;
    }

    [InlineArray(UnitSlotTable.Capacity)]
    public struct UnitSlotArray
    {
#pragma warning disable CS0169
        private UnitSlot _slot;
#pragma warning restore CS0169
    }

    /// <summary>
    /// ⭐⭐⭐ <c>UnitSlotTable</c> (282, <c>dtUnitSlotTable</c> 221) — the per-slot truth of a stochastic unit (SV-1): one row per man, alive or
    /// dead. Present strength, standing and ammunition are DERIVED from it (RV-5 — the readers' branch is exp/agg-behaviors'). ⭐ One table holds
    /// a whole company (<see cref="Capacity"/> = 120 × 8 B); the header's <see cref="Chunk"/> is 0 — a second chunk is a versioned extension only
    /// if a leaf larger than a company is ever ruled (R-8001).
    /// </summary>
    [ComponentId(GlobalComponentIds.UnitSlotTable)]
    [StructLayout(LayoutKind.Sequential)]
    public struct UnitSlotTable
    {
        /// <summary>The slots one table holds (a company leaf, S-6).</summary>
        public const int Capacity = 120;
        /// <summary>A man is down below this many health points (the physical soldier's incapacitation: <c>Health</c> 0).</summary>
        public const float DownBelow = 0.5f;

        /// <summary>Slots in use (the TKB composite's men).</summary>
        public byte Count;
        /// <summary>The chunk index (0; RV-11).</summary>
        public byte Chunk;
        private ushort _pad0;
        private uint _pad1;
        public UnitSlotArray Slots;

        /// <summary>The slot capacity a unit of <paramref name="echelon"/> may use (S-6: squad, platoon, company leaves).</summary>
        public static int CapacityFor(byte echelon) => echelon switch { 0 => 13, 1 => 48, _ => Capacity };

        /// <summary>Is slot <paramref name="i"/> a man still in the fight (not empty, not down)?</summary>
        public readonly bool IsUp(int i) => Slots[i].Role != SlotRole.Empty && (float)Slots[i].Health >= DownBelow;

        /// <summary>The men present (up) — the derived Present (SV-11). 0 B.</summary>
        public readonly int Present()
        {
            int n = 0;
            for (int i = 0; i < Count; i++) if (IsUp(i)) n++;
            return n;
        }

        /// <summary>The men the table was authored with (every non-empty slot, up or down).</summary>
        public readonly int Authorized()
        {
            int n = 0;
            for (int i = 0; i < Count; i++) if (Slots[i].Role != SlotRole.Empty) n++;
            return n;
        }
    }

    /// <summary>What a holding is (S-7, N-4).</summary>
    public enum HoldingKind : byte
    {
        NoKind = 0,
        /// <summary>A crew-served or support weapon (MG, ATGM, mortar …) — the personal weapons are the slots'.</summary>
        Weapon = 1,
        /// <summary>A vehicle (IFV, tank, truck) — mounted speed and trafficability (M-1, M-2).</summary>
        Vehicle = 2,
        /// <summary>A sensor (optics, thermal, radar — <see cref="UnitHolding.Sensor"/>), N-4.</summary>
        Sensor = 3,
    }

    /// <summary>How a vehicle holding moves (M-1: any wheeled vehicle mounted ⇒ woods forbidden).</summary>
    public enum HoldingMobility : byte
    {
        NoMobility = 0,
        Wheeled = 1,
        Tracked = 2,
    }

    /// <summary>A sensor holding's kind (N-1 observer quality).</summary>
    public enum HoldingSensor : byte
    {
        NoSensor = 0,
        Optics = 1,
        Thermal = 2,
        Radar = 3,
    }

    /// <summary>One system type the unit holds (S-7): count, readiness and ammunition — 24 B (20 of fields, aligned to 8).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UnitHolding
    {
        /// <summary>The TKB platform / weapon type.</summary>
        public TkbTypeId Type;
        /// <summary>How many the unit has (authored).</summary>
        public ushort Count;
        /// <summary>How many still work (K-kills take them out, C-3).</summary>
        public ushort Operational;
        /// <summary>Rounds left across the holding (a vehicle's main gun, a launcher's missiles).</summary>
        public uint Ammo;
        public HoldingKind Kind;
        public HoldingMobility Mobility;
        public HoldingSensor Sensor;
        /// <summary>Mobility-killed (M-kill) count among the operational ones.</summary>
        public byte Immobile;
    }

    [InlineArray(UnitHoldings.Capacity)]
    public struct UnitHoldingArray
    {
#pragma warning disable CS0169
        private UnitHolding _holding;
#pragma warning restore CS0169
    }

    /// <summary>
    /// ⭐⭐⭐ <c>UnitHoldings</c> (283, <c>dtUnitHoldings</c> 222) — the systems a unit holds by TKB type (S-7): vehicles, crew-served weapons,
    /// sensors — each with count, operational, ammunition. Filled from the TKB composite on load; written by the unit's own attrition (phase B).
    /// </summary>
    [ComponentId(GlobalComponentIds.UnitHoldings)]
    [StructLayout(LayoutKind.Sequential)]
    public struct UnitHoldings
    {
        public const int Capacity = 24;

        public byte Count;
        private byte _pad0;
        private ushort _pad1;
        private uint _pad2;
        public UnitHoldingArray Items;

        /// <summary>Does the unit hold a working wheeled vehicle (M-1: woods forbidden when mounted)?</summary>
        public readonly bool HasWheeled()
        {
            for (int i = 0; i < Count; i++)
                if (Items[i].Kind == HoldingKind.Vehicle && Items[i].Mobility == HoldingMobility.Wheeled && Items[i].Operational > 0) return true;
            return false;
        }

        /// <summary>Does the unit hold a working vehicle (B-6: Embark / Dismount refused without one)?</summary>
        public readonly bool HasVehicles()
        {
            for (int i = 0; i < Count; i++)
                if (Items[i].Kind == HoldingKind.Vehicle && Items[i].Operational > 0) return true;
            return false;
        }
    }

    /// <summary>One line of a firer's allocation (C-2): what it threw at one target class of one target this step — 24 B.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FireLine
    {
        /// <summary>The target unit.</summary>
        public Entity Target;
        /// <summary>The expected hits (λ) on the class — ⭐ already × the FIRER's suppression (A owns that factor; B adds the victim's cover).</summary>
        public float Hits;
        /// <summary>The expected damage per hit (C-1: <c>ArmorModel</c> × the mean zone factor, or the vehicle outcome's).</summary>
        public float DamagePerHit;
        /// <summary>The rounds that arrive (C-4 — the victim's suppression input).</summary>
        public float Rounds;
        /// <summary><see cref="FireAllocation.PersonnelClass"/> = the men (slots); else 1 + the victim's holding index.</summary>
        public byte VictimClass;
        /// <summary>⭐ CE-8007 — what fired it: <see cref="FireAllocation.PersonalSource"/> = the slots' personal weapons; else 1 + the FIRER's
        /// holding index (phase B spends the firer's own ammunition from its own lines). The former padding byte: the layout is unchanged.</summary>
        public byte Source;
        private ushort _pad1;
    }

    [InlineArray(FireAllocation.Capacity)]
    public struct FireLineArray
    {
#pragma warning disable CS0169
        private FireLine _line;
#pragma warning restore CS0169
    }

    /// <summary>
    /// ⭐⭐⭐ <c>FireAllocation</c> (284, node-local, recorded) — phase A's output (C-2): the firer writes ONLY its own; phase B on each victim
    /// reads every enemy allocation naming it, from the tick-start view (a declared stable read — RV-4). ⛔ Not replicated: the attrition runs
    /// on the node that runs the unit.
    /// </summary>
    [ComponentId(GlobalComponentIds.FireAllocation)]
    [DataPolicy(DataPolicy.NoScenario)]
    [StructLayout(LayoutKind.Sequential)]
    public struct FireAllocation
    {
        public const int Capacity = 16;
        /// <summary><see cref="FireLine.VictimClass"/> of the men.</summary>
        public const byte PersonnelClass = 0;
        /// <summary><see cref="FireLine.Source"/> of the slots' personal weapons.</summary>
        public const byte PersonalSource = 0;

        /// <summary>The step this allocation belongs to (a stale one is ignored by phase B).</summary>
        public SimInstant Step;
        public byte Count;
        private byte _pad0;
        private ushort _pad1;
        private uint _pad2;
        public FireLineArray Lines;
    }

    /// <summary>One observer–target pair's accumulated detection (N-5) — 24 B.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DetectionPair
    {
        public Entity Target;
        /// <summary>The accumulated level 0..1 (each step adds the step's chance); a contact at <see cref="UnitDetections.ContactAt"/>.</summary>
        public float Level;
        /// <summary>Was the contact reported (crossed the threshold and not yet faded)?</summary>
        public byte Reported;
        private byte _pad0;
        private ushort _pad1;
        /// <summary>The last step the target was in line of sight — the contact fades after <see cref="UnitDetections.FadeAfterSeconds"/>.</summary>
        public SimInstant LastSeenAt;
    }

    [InlineArray(UnitDetections.Capacity)]
    public struct DetectionPairArray
    {
#pragma warning disable CS0169
        private DetectionPair _pair;
#pragma warning restore CS0169
    }

    /// <summary>
    /// ⭐⭐ <c>UnitDetections</c> (285, agg-backend's own; node-local, recorded) — the observer's accumulating detection levels per target (N-5,
    /// RV-11: the per-pair levels declared before the first recording). Written only by the observer's own sensor step.
    /// </summary>
    [ComponentId(GlobalComponentIds.UnitDetections)]
    [DataPolicy(DataPolicy.NoScenario)]
    [StructLayout(LayoutKind.Sequential)]
    public struct UnitDetections
    {
        public const int Capacity = 32;
        /// <summary>The level at which a contact is reported (N-5).</summary>
        public const float ContactAt = 1f;
        /// <summary>A contact fades when the target was out of sight this long (s).</summary>
        public const float FadeAfterSeconds = 30f;

        public byte Count;
        private byte _pad0;
        private ushort _pad1;
        private uint _pad2;
        public DetectionPairArray Pairs;
    }

    /// <summary>
    /// ⭐⭐ <c>RunSeed</c> (286, agg-backend's own; a recorded SINGLETON) — C-8: the run's seed, mixed into every <c>SimRng</c> draw so two
    /// runs of one scenario differ by seed (both resolutions), and a recording replays its own run. 0 = the historical behaviour (every run the
    /// same run).
    /// </summary>
    [ComponentId(GlobalComponentIds.RunSeed)]
    [StructLayout(LayoutKind.Sequential)]
    public struct RunSeed
    {
        public uint Value;
    }
}
