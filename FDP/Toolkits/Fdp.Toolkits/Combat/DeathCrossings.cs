using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐ <c>CE-4660</c> — THE "this system saw it alive, then dead" rule (VE-P, the W3 wreck rule): a death is a CROSSING, so a body first seen already
    /// dead (a scenario saved with a wreck, a late joiner, a Health claim that moved here) makes none — a cleared wreck is never rebuilt, a burnt-out
    /// one never re-lit, a reload never doubles. Lifted out of <c>WreckObstacleSystem</c> (CE-6005) so the fire's ignition (<c>BurnSystem</c>) is the same
    /// rule, not a second copy. Forgets on a reload (a new world epoch) and during a replay (the recording holds what a death made).
    /// ⭐ Its only state is a per-entity "last state" map, grown on first sight — 0 B per steady frame; one instance per consuming system, and ONE form per
    /// instance: <see cref="Crossed"/> (alive → dead) or <see cref="Changed"/> (any state → another, ⭐ <c>CE-4661</c>: the injury state's time,
    /// <c>HealthStateSinceSystem</c>) — the same rule (a change is something SEEN to change; first sight is none).
    /// </summary>
    [Fdp.Core.WorldDerived("which bodies this system saw alive; forgets on a new world epoch and in a replay - a body first seen dead makes no crossing")]
    public sealed class DeathCrossings
    {
        private readonly Dictionary<Entity, byte> _last = new();
        private EntityRepository? _for;
        private int _epoch;

        /// <summary>Call once per frame first: true when the world is new (a reload, another repository) — the caller rebuilds its queries and caches;
        /// the map is cleared. During a replay the map is cleared and <paramref name="replaying"/> is true: the caller does nothing.</summary>
        public bool Begin(EntityRepository repo, out bool replaying)
        {
            replaying = Fdp.Toolkit.Replay.ReplayState.IsActive(repo);
            if (replaying) { _last.Clear(); return false; }
            if (Fdp.Toolkit.Replication.Services.WorldEpoch.Moved(repo, ref _epoch) | !ReferenceEquals(repo, _for))
            {
                _for = repo;
                _last.Clear();
                return true;
            }
            return false;
        }

        /// <summary>Records <paramref name="e"/> as <paramref name="alive"/> now; true exactly once — the frame it is first seen dead after being seen alive.</summary>
        public bool Crossed(Entity e, bool alive)
        {
            bool crossed = !alive && _last.TryGetValue(e, out byte was) && was == 1;
            _last[e] = alive ? (byte)1 : (byte)0;
            return crossed;
        }

        /// <summary>⭐ <c>CE-4661</c> — records <paramref name="e"/> in <paramref name="state"/> now; true when it was seen before in ANOTHER state (first sight: false).</summary>
        public bool Changed(Entity e, byte state)
        {
            bool changed = _last.TryGetValue(e, out byte was) && was != state;
            _last[e] = state;
            return changed;
        }
    }
}
