using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Diagnostics;

namespace Hrot.Common.Diagnostics.Gizmos
{
    /// <summary>
    /// ⭐⭐ <c>CE-1317</c> (Brain View §12c BV-48, R-1038) — VIEWING A UNIT'S BRAIN TAB IS DEBUGGING IT: every frame a Brain tab draws a unit
    /// (docked on the selection, or a pinned copy) it calls <see cref="Viewed"/>; the first view arms the unit's traces (BTree / HSM rings,
    /// the utility per-consideration trace), and <see cref="Tick"/> disarms a unit no tab has drawn for <see cref="Linger"/>. 🔒 User:
    /// <i>"when I am watching an entity and its brain view is being shown, this is the condition that should turn this full debug on"</i>.
    /// ⛔ Tracing ONLY — no gizmo is pinned here (BV-49: one family per checkbox, the user's). A unit the debug switch holds
    /// (<see cref="UnitDebugController"/>, the graph-overlay pin) stays armed when its tab closes.
    /// <para>⭐ Composed by the editor (<see cref="Current"/> null elsewhere ⇒ a tab arms nothing there).</para>
    /// </summary>
    public sealed class BrainObservation
    {
        public static BrainObservation? Current { get; set; }

        /// <summary>How long a unit stays armed after the last frame a tab drew it (the selection moving on, a pinned tab closing).</summary>
        public TimeSpan Linger { get; init; } = TimeSpan.FromSeconds(1);

        private readonly Func<long> _nowMs;
        private readonly Dictionary<Entity, long> _seen = new();
        private readonly List<Entity> _expired = new();
        private readonly HashSet<Entity> _armedHere = new();   // only what THIS armed is disarmed (a trace the menu armed stays)

        public BrainObservation(Func<long>? nowMs = null) => _nowMs = nowMs ?? (() => Environment.TickCount64);

        /// <summary>The units armed because a tab is (or was, within <see cref="Linger"/>) drawing them.</summary>
        public IReadOnlyCollection<Entity> Observed => _seen.Keys;

        public bool IsObserved(Entity unit) => _seen.ContainsKey(unit);

        /// <summary>A Brain tab drew <paramref name="unit"/> this frame.</summary>
        public void Viewed(EntityRepository world, Entity unit)
        {
            if (unit == Entity.Null || !world.IsAlive(unit)) return;
            if (!_seen.ContainsKey(unit) && !AiTraceActions.IsOn(world, unit, BehaviorDebugFlags.EnableTraceBuffer))
            {
                AiTraceActions.Set(world, unit, BehaviorDebugFlags.EnableTraceBuffer, true);
                _armedHere.Add(unit);
            }
            _seen[unit] = _nowMs();
        }

        /// <summary>Once per frame (the editor's update): disarms the units no tab drew lately.</summary>
        public void Tick(EntityRepository world)
        {
            if (_seen.Count == 0) return;
            long now = _nowMs(), linger = (long)Linger.TotalMilliseconds;
            _expired.Clear();
            foreach (var (unit, at) in _seen)
                if (now - at > linger) _expired.Add(unit);
            foreach (var unit in _expired)
            {
                _seen.Remove(unit);
                if (!_armedHere.Remove(unit)) continue;                                  // armed by someone else: theirs to disarm
                if (UnitDebugController.Current?.IsDebugged(unit) == true) continue;   // the debug switch still holds it
                if (world.IsAlive(unit)) AiTraceActions.Set(world, unit, BehaviorDebugFlags.EnableTraceBuffer, false);
            }
        }
    }
}
