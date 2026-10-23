using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Diagnostics
{
    /// <summary>⭐ <c>CE-1249</c> — what changed in a brain unit's mind. 📄 docs/DESIGN_Entity_Brain_View.md §6 BV-10.</summary>
    public enum BrainJournalKind : byte
    {
        None = 0,
        /// <summary>A task started. A = the behaviour, B = the one it replaced (0 = none); Origin, Urgency, Cause (the sense).</summary>
        TaskStarted,
        /// <summary>A reaction paused the running task. A = the paused behaviour, B = the reaction.</summary>
        TaskPaused,
        /// <summary>The paused task came back. A = the behaviour.</summary>
        TaskResumed,
        /// <summary>The task finished by itself. A = the behaviour, Urgency = the result (2 success, 3 failure), B = the fault code.</summary>
        TaskEnded,
        /// <summary>An order cleared the task. A = the behaviour, Origin = who cleared it.</summary>
        TaskCleared,
        /// <summary>The gate refused an assignment. A = the refused behaviour, Origin / Urgency of the refused one, Cause = <see cref="BrainRefusal"/>.</summary>
        TaskRefused,
        /// <summary>The SOP slot started a behaviour. A = the behaviour.</summary>
        SopStarted,
        /// <summary>The SOP slot ended. A = the behaviour.</summary>
        SopEnded,
        /// <summary>A utility option decision picked a different option. A = the decision, B = the new option, Cause = the old option, X = the winner's score.</summary>
        DecisionSwitched,
        /// <summary>A new locomotion action started. A = the action id, Point = its destination (zero when it has none), Cause = the move source.</summary>
        MoveStarted,
        /// <summary>A squad / platoon play started. A = the play.</summary>
        PlayStarted,
        /// <summary>A play changed phase. A = the play, B = the phase, Cause = why.</summary>
        PlayPhase,
        /// <summary>A play ended. A = the play.</summary>
        PlayEnded,
        /// <summary>A commander ordered a member. A = the member's network id, B = the behaviour.</summary>
        OrderIssued,
        /// <summary>An HSM region's active state changed. Origin = the region, A = from state, B = to state (state indices).</summary>
        StateChanged,
    }

    /// <summary>⭐ <c>CE-1249</c> (BV-13) — why the gate refused an assignment.</summary>
    public enum BrainRefusal : byte
    {
        /// <summary>No reason recorded (⚠ not <c>None</c>: the DDS IDL generator puts this file's enums in one scope).</summary>
        Unspecified = 0,
        /// <summary>The running task's origin outranks it.</summary>
        LowerRank,
        /// <summary>A running reaction is at least as urgent.</summary>
        LowerUrgency,
        /// <summary>The ROE says stay on task: no reaction pauses it.</summary>
        StayOnTask,
        /// <summary>The unit is possessed: no reaction interrupts the operator.</summary>
        Possessed,
        /// <summary>The SOP's idle choice waits while a reaction runs.</summary>
        ReactionRunning,
        /// <summary>The reaction is the task it would pause.</summary>
        SameAsTask,
        /// <summary>The task is the unit's SOP.</summary>
        IsTheSop,
        /// <summary>Its parameters did not parse.</summary>
        ParseFailed,
    }

    /// <summary>⭐ <c>CE-1250</c> (BV-12) — why a unit moves, carried by its <see cref="BrainJournalKind.MoveStarted"/> entry's
    /// <c>Cause</c>. ⚠ Not on <c>MoveToParams</c> as BV-12 first said: that struct is at its 32-byte limit and every Flags bit is taken —
    /// the issuer notes it with <see cref="BrainJournalOf.NoteMoveSource"/> instead.</summary>
    public enum MoveSource : byte
    {
        /// <summary>Nobody said (⚠ not <c>None</c>: this file's enums share one IDL scope).</summary>
        SourceUnknown = 0,
        /// <summary>An order (an operator's or a commander's move order).</summary>
        Order,
        /// <summary>A cover search (EQS) picked the point.</summary>
        CoverSearch,
        /// <summary>The unit's post in its squad's formation or play.</summary>
        SquadPost,
        /// <summary>Following its leader.</summary>
        FollowLeader,
        /// <summary>Getting away from a threat.</summary>
        Flee,
        /// <summary>Through or to a door.</summary>
        Door,
        /// <summary>The next point of a route.</summary>
        Route,
        /// <summary>An idle wander.</summary>
        Wander,
        /// <summary>The operator drives the unit (possession).</summary>
        Operator,
    }

    /// <summary>One change, numbers only (32 bytes) — names are resolved when it is shown.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct BrainJournalEntry
    {
        /// <summary>Sim seconds.</summary>
        public float At;
        /// <summary>The unit's running count when this was written (<see cref="BrainJournal.Seq"/>).</summary>
        public uint Seq;
        public BrainJournalKind Kind;
        public byte Origin;
        public byte Urgency;
        public byte Cause;
        public int A;
        public int B;
        public Vector3 Point;
    }

    [InlineArray(BrainJournal.Capacity)]
    public struct BrainJournalEntries
    {
        private BrainJournalEntry _element;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-1249</c> (R-1026, R-1027; BV-9, BV-15, BV-27) — a brain unit's last <see cref="Capacity"/> changes WITH their causes:
    /// always on (attached beside <see cref="ActionStatus"/>), recorded, never in a scenario, never on the wire; unmanaged, so a
    /// write copies 32 bytes and allocates nothing. <see cref="Seq"/> counts every entry ever written (callers ask <c>?since=</c>;
    /// a breakpoint on it stops when the unit changes its mind). 📄 docs/DESIGN_Entity_Brain_View.md §6, §10.
    /// </summary>
    [ComponentId(GlobalComponentIds.BrainJournal)]
    [DataPolicy(DataPolicy.NoScenario)]
    [StructLayout(LayoutKind.Sequential)]
    public struct BrainJournal
    {
        public const int Capacity = 24;

        /// <summary>Every entry ever written to this unit (never reset).</summary>
        public uint Seq;
        /// <summary>The slot the next entry goes to.</summary>
        public byte Head;
        /// <summary>How many slots hold an entry (≤ <see cref="Capacity"/>).</summary>
        public byte Count;
        /// <summary>The newest entry's kind (a breakpoint condition can filter on it).</summary>
        public BrainJournalKind LastKind;
        /// <summary>⭐ <c>CE-1250</c> — the source noted for the move command <see cref="PendingMoveInstance"/> (one shot: the
        /// dispatcher's <see cref="BrainJournalKind.MoveStarted"/> entry reads it when that instance starts).</summary>
        public MoveSource PendingMoveSource;
        /// <summary>The locomotion channel's action instance <see cref="PendingMoveSource"/> belongs to.</summary>
        public uint PendingMoveInstance;
        public BrainJournalEntries Entries;

        /// <summary>The entries oldest first — <paramref name="i"/> = 0 is the oldest kept.</summary>
        public readonly BrainJournalEntry At(int i)
        {
            int start = (Head - Count + Capacity) % Capacity;
            return Entries[(start + i) % Capacity];
        }
    }

    /// <summary>⭐ <c>CE-1249</c> (BV-16) — the switch, read when the brain components are attached: off ⇒ never attached, every writer
    /// is one presence check. Default on (the engine is in development). ⛔ No runtime toggle (R-1027).</summary>
    public static class BrainJournalSettings
    {
        public static bool Enabled { get; set; } = true;
    }

    /// <summary>The one write path into <see cref="BrainJournal"/> — a no-op where the unit or the world has none.</summary>
    public static class BrainJournalOf
    {
        /// <summary>Appends one entry. ⭐ A refusal identical to the newest entry only refreshes its time, so an SOP or a sense retrying
        /// every tick does not wash the ring out.</summary>
        public static void Write(EntityRepository world, Entity unit, BrainJournalKind kind, int a = 0, int b = 0,
            byte origin = 0, byte urgency = 0, byte cause = 0, Vector3 point = default)
        {
            if (!world.IsComponentTypeRegistered<BrainJournal>() || !world.HasComponent<BrainJournal>(unit)) return;
            ref var j = ref world.GetComponentRW<BrainJournal>(unit);
            float now = world.HasSingleton<GlobalTime>() ? (float)world.GetSingleton<GlobalTime>().TotalTime : 0f;

            if (kind == BrainJournalKind.TaskRefused && j.Count > 0)
            {
                ref var last = ref j.Entries[(j.Head + BrainJournal.Capacity - 1) % BrainJournal.Capacity];
                if (last.Kind == kind && last.A == a && last.Cause == cause && last.Origin == origin)
                {
                    last.At = now;
                    return;
                }
            }

            unchecked { j.Seq++; }
            ref var e = ref j.Entries[j.Head];
            e.At = now;
            e.Seq = j.Seq;
            e.Kind = kind;
            e.Origin = origin;
            e.Urgency = urgency;
            e.Cause = cause;
            e.A = a;
            e.B = b;
            e.Point = point;
            j.Head = (byte)((j.Head + 1) % BrainJournal.Capacity);
            if (j.Count < BrainJournal.Capacity) j.Count++;
            j.LastKind = kind;
        }

        /// <summary>⭐ <c>CE-1250</c> — notes why the move command just issued as <paramref name="instance"/> (the locomotion channel's new
        /// <c>ActionInstanceId</c>) was issued; its <see cref="BrainJournalKind.MoveStarted"/> entry carries it. A no-op without a journal.</summary>
        public static void NoteMoveSource(EntityRepository world, Entity unit, uint instance, MoveSource source)
        {
            if (!world.IsComponentTypeRegistered<BrainJournal>() || !world.HasComponent<BrainJournal>(unit)) return;
            ref var j = ref world.GetComponentRW<BrainJournal>(unit);
            j.PendingMoveSource = source;
            j.PendingMoveInstance = instance;
        }

        /// <summary>The source noted for <paramref name="instance"/>, else <see cref="MoveSource.SourceUnknown"/>.</summary>
        public static MoveSource MoveSourceOf(EntityRepository world, Entity unit, uint instance)
        {
            if (!world.IsComponentTypeRegistered<BrainJournal>() || !world.HasComponent<BrainJournal>(unit)) return MoveSource.SourceUnknown;
            ref readonly var j = ref world.GetComponentRO<BrainJournal>(unit);
            return j.PendingMoveInstance == instance ? j.PendingMoveSource : MoveSource.SourceUnknown;
        }

        /// <summary>Attaches an empty journal when the switch is on and the world registers it (the brain's attach point).</summary>
        public static void Attach(EntityRepository world, Entity unit)
        {
            if (!BrainJournalSettings.Enabled) return;
            if (!world.IsComponentTypeRegistered<BrainJournal>() || world.HasComponent<BrainJournal>(unit)) return;
            world.AddComponent(unit, new BrainJournal());
        }
    }
}
