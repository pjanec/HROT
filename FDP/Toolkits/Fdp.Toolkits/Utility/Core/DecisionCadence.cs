using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Perception.Events;

namespace Fdp.Toolkit.Utility
{
    /// <summary>
    /// ⭐⭐ <c>CE-4395</c> (docs/DESIGN_Evaluation_Cost_And_Cadence.md §11g-2; user, 2026-11-10: "0.2 seconds is very much acceptable") — a
    /// decision that declares <see cref="UtilityDecisionDef.CadenceSeconds"/> is re-scored for a unit at most once per period of SIM time,
    /// on the unit's own phase (<see cref="CadenceGate.NextOnGrid"/>), and AT ONCE when the unit's senses changed this frame
    /// (<see cref="SensorChangedEvent"/> — a new contact, a hit, a near miss: the SOP slot's wake, R-195). Between, the scorer returns the
    /// last result and writes the unit's result buffer and decision log back exactly as that scoring wrote them, so every reader sees what it
    /// saw before — only computed less often.
    /// <para>Re-scored also when: the caller's remembered winner is not the stored one (a new run starts from 0), the unit's slot was reused,
    /// sim time went BACK (a restore or a seek — the store is not recorded), or the world has no <c>GlobalTime</c> (a bare world: every call,
    /// as before).</para>
    /// <para>📐 Why: at 1 000 fighting men three decisions re-scored by every man on every tick were ~13 of the brain's 20 ms.</para>
    /// </summary>
    public sealed class DecisionCadence
    {
        /// <summary>One unit's stored result for one decision.</summary>
        internal sealed class Entry
        {
            public ushort Generation;
            public double ComputedAt, NextDue;
            public byte Winner;
            public Entity Top;
            public float TopScore;
            public bool HasTop;
            public UtilityResultBuffer Output;
        }

        private static readonly ConditionalWeakTable<EntityRepository, DecisionCadence> s_of = new();
        // ⭐ N-11 (CE-4473) — concurrent: parallel brains store results for DIFFERENT units at once; each key (unit, decision) is touched only
        //   by its unit's own thread, so what it holds stays deterministic
        private readonly System.Collections.Concurrent.ConcurrentDictionary<long, Entry> _entries = new();
        private int[] _wokenAt = System.Array.Empty<int>();   // [entity index] = the wake build (stamp) it was woken in
        private uint _wakeBuiltAt;
        private double _wakeBuiltNow;
        private int _wakeStamp;
        private bool _wakeBuilt;

        /// <summary>How many scorings the cadence let through, and how many it answered from the store (a rail reads them).</summary>
        public long Scored => System.Threading.Interlocked.Read(ref _scored);
        /// <summary>See <see cref="Scored"/>.</summary>
        public long Reused => System.Threading.Interlocked.Read(ref _reused);
        private long _scored, _reused;

        /// <summary>The cadence state of <paramref name="world"/>.</summary>
        public static DecisionCadence Of(EntityRepository world) => s_of.GetValue(world, static _ => new DecisionCadence());

        /// <summary>
        /// The stored entry when <paramref name="def"/> is cadenced and its result for <paramref name="self"/> may be reused now; else null with
        /// <paramref name="now"/> set (NaN when the decision is not cadenced here) — the caller scores and then calls <see cref="Store"/>.
        /// </summary>
        internal Entry? Reusable(EntityRepository world, Entity self, UtilityDecisionDef def, int decisionId, out double now)
        {
            now = double.NaN;
            if (def.CadenceSeconds <= 0 || !world.HasSingleton<GlobalTime>()) return null;
            now = world.GetSingleton<GlobalTime>().TotalTime;
            if (!_entries.TryGetValue(Key(self, decisionId), out var e)) return null;
            if (e.Generation != self.Generation || now < e.ComputedAt || now >= e.NextDue) return null;
            if (e.ComputedAt < now && Woken(world, self, now)) return null;   // woken: once, by the first call this frame
            return e;
        }

        /// <summary>Records a fresh scoring (no-op when <paramref name="now"/> is NaN — the decision is not cadenced here).</summary>
        internal void Store(Entity self, UtilityDecisionDef def, int decisionId, double now, byte winner, bool hasTop, Entity top, float topScore,
                            in UtilityResultBuffer output)
        {
            System.Threading.Interlocked.Increment(ref _scored);
            if (double.IsNaN(now)) return;
            long key = Key(self, decisionId);
            if (!_entries.TryGetValue(key, out var e)) _entries[key] = e = new Entry();   // allocation once per unit and decision (only its own thread adds it)
            e.Generation = self.Generation;
            e.ComputedAt = now;
            e.NextDue = CadenceGate.NextOnGrid(now, def.CadenceSeconds, self.Index);
            e.Winner = winner;
            e.HasTop = hasTop; e.Top = top; e.TopScore = topScore;
            e.Output = output;
        }

        internal void CountReuse() => System.Threading.Interlocked.Increment(ref _reused);

        /// <summary>⭐ N-11 — builds this frame's wake table from the frame's events, SERIALLY, before the brains' per-entity phase (inside the
        /// phase it is only read). The serial walk builds it on its first call, as before.</summary>
        public void PrepareFrame(EntityRepository world)
        {
            if (!world.HasSingleton<GlobalTime>()) return;
            BuildWake(world, world.GetSingleton<GlobalTime>().TotalTime);
        }

        private static readonly int[] s_wakeSlots = new int[256];
        private static int WakeSlot(SensorChange what)
        {
            int k = (byte)what;
            if (s_wakeSlots[k] == 0) s_wakeSlots[k] = Fdp.Toolkit.Diagnostics.QueryTimings.Slot("cadence.wake:" + what) + 1;
            return s_wakeSlots[k] - 1;
        }

        private static long Key(Entity self, int decisionId) => ((long)decisionId << 32) | (uint)self.Index;

        /// <summary>Did <paramref name="self"/>'s senses change this frame? Built once per frame from the bus (no allocation once grown).</summary>
        private bool Woken(EntityRepository world, Entity self, double now)
        {
            if (!ParallelReadCheck.Defers(world)) BuildWake(world, now);   // in a phase: built by PrepareFrame, read only
            return self.Index < _wokenAt.Length && _wokenAt[self.Index] == _wakeStamp;
        }

        private void BuildWake(EntityRepository world, double now)
        {
            uint v = world.GlobalVersion;   // a frame = the version AND the sim clock (a host that never ticks the repository still advances time)
            if (!_wakeBuilt || _wakeBuiltAt != v || _wakeBuiltNow != now)
            {
                _wakeBuilt = true; _wakeBuiltAt = v; _wakeBuiltNow = now; _wakeStamp++;
                if (world.Bus.IsRegistered<SensorChangedEvent>())
                    foreach (var evt in world.ReadEvents<SensorChangedEvent>())
                    {
                        int i = evt.Unit.Index;
                        if (i < 0) continue;
                        if (Fdp.Toolkit.Diagnostics.QueryTimings.Enabled) Fdp.Toolkit.Diagnostics.QueryTimings.Add(WakeSlot(evt.What), 1);   // measurement: which changes wake
                        if (i >= _wokenAt.Length) System.Array.Resize(ref _wokenAt, System.Math.Max(256, (i + 1) * 2));
                        _wokenAt[i] = _wakeStamp;
                    }
            }
        }
    }
}
