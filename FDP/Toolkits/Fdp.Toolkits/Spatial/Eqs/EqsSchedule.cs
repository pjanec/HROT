using System;
using System.Collections.Generic;
using Fdp.Core;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// The scheduling band of a sensor (<see cref="EqsSensor.Priority"/>). ⭐ <see cref="Normal"/> is 0 so every creator that
    /// leaves <c>Priority</c> unset stays Normal. 📄 docs/DESIGN_Sensors_And_Doctrine.md §5.3–§5.4.
    /// </summary>
    public enum EqsPriorityBand : byte
    {
        Normal   = 0,
        Critical = 1,
        Low      = 2,
    }

    /// <summary>
    /// Optional on an <see cref="IEqsTest"/>: the work units ONE candidate costs it. A test without it costs
    /// <see cref="EqsCost.Cheap"/> in a cheap phase and <see cref="EqsCost.Sight"/> in an expensive one.
    /// </summary>
    public interface IEqsCostWeight
    {
        int CostPerCandidate { get; }
    }

    /// <summary>
    /// ⭐⭐ <b>Counted work, not milliseconds</b> (§5.3). Proportional to what a sensor does AND repeatable — the same scenario
    /// schedules the same sensors on every run. Weights are relative; the budget is in the same units.
    /// </summary>
    public static class EqsCost
    {
        /// <summary>
        /// Per evaluation, whatever it generates — the fixed overhead. 📐 Measured (<c>2026-10-04</c>, debug build, 100
        /// sensors over 50 / 200 / 800 targets): ~32 µs per sensor + ~0.34 µs per counted unit ⇒ the overhead is ~95 units.
        /// </summary>
        public const int Sensor = 100;
        /// <summary>Per generated candidate.</summary>
        public const int Candidate = 1;
        /// <summary>Per candidate through a cheap filter or score.</summary>
        public const int Cheap = 1;
        /// <summary>Per candidate through a sight test.</summary>
        public const int Sight = 4;
        /// <summary>Per candidate through a path / navmesh query.</summary>
        public const int Path = 16;

        /// <summary>The weight of <paramref name="test"/> per candidate.</summary>
        public static int WeightOf(IEqsTest test)
            => test is IEqsCostWeight w ? w.CostPerCandidate
             : test.Phase is EqsTestPhase.FilterCheap or EqsTestPhase.ScoreCheap ? Cheap : Sight;
    }

    /// <summary>
    /// ⭐⭐ <b>Which sensors run this tick, in what order</b> (§5.4): band (Critical, Normal, Low), then the OLDEST result
    /// first (<see cref="SensorEvalState.LastSolvedTick"/>), then entity index — a total order, so the schedule is
    /// deterministic. Each band spends its share (<see cref="CumulativeShare"/>); a sensor whose last cost does not fit
    /// what is left waits — it is then the oldest of its band and runs first.
    /// </summary>
    public sealed class EqsSchedule
    {
        private readonly List<(int Rank, uint LastSolved, int Index, Entity Sensor, int Estimate)> _order = new();

        /// <summary>Clears the tick's list.</summary>
        public void Begin() => _order.Clear();

        /// <summary>Adds a live (not suspended) sensor.</summary>
        public void Add(Entity sensor, byte priority, uint lastSolvedTick, int lastCost)
            => _order.Add((RankOf(priority), lastSolvedTick, sensor.Index, sensor, lastCost));

        /// <summary>The sensors in run order (call after every <see cref="Add"/>).</summary>
        public IReadOnlyList<(int Rank, uint LastSolved, int Index, Entity Sensor, int Estimate)> Ordered()
        {
            _order.Sort(static (a, b) =>
            {
                int c = a.Rank.CompareTo(b.Rank);
                if (c != 0) return c;
                c = a.LastSolved.CompareTo(b.LastSolved);
                return c != 0 ? c : a.Index.CompareTo(b.Index);
            });
            return _order;
        }

        /// <summary>
        /// True when a sensor estimated at <paramref name="estimate"/> may start with <paramref name="spent"/> units of its
        /// band used of <paramref name="budget"/>. The first sensor of a band always starts (a sensor over the whole budget
        /// runs alone).
        /// </summary>
        public static bool ShouldStart(int spent, int estimate, int budget)
            => spent == 0 || (spent < budget && spent + estimate <= budget);

        /// <summary>
        /// The budget bands up to and including <paramref name="rank"/> may use together: Critical 50 %, + Normal 35 %,
        /// + Low the rest (EQS 1.3 §7.5's shares — unused slack rolls down to the next band).
        /// </summary>
        public static int CumulativeShare(int rank, int budget) => rank switch
        {
            0 => budget * 50 / 100,
            1 => budget * 85 / 100,
            _ => budget,
        };

        private static int RankOf(byte priority) => (EqsPriorityBand)priority switch
        {
            EqsPriorityBand.Critical => 0,
            EqsPriorityBand.Low      => 2,
            _                        => 1,
        };
    }
}
