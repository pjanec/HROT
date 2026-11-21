using System;
using System.Diagnostics;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Replication.Services
{
    /// <summary>
    /// ⭐⭐ <b>CE-3329 PW — §12c W2: no sample leaves this node between a world swap and the master's commit.</b>
    /// 📄 docs/DESIGN_Replay_Time_Axis_And_Review.md §12a, §12c.
    /// </summary>
    /// <remarks>
    /// <para>⭐ The node that swaps its world (a replay seek, a checkpoint restore) holds; the cleanup's DISPOSES still go out during
    /// the hold (<c>CycloneNetworkCleanupSystem</c> does not read it), the egress PUBLICATION does not
    /// (<c>CycloneEgressSystem</c> skips its scan). The hold is released when the master's commit reaches the node (the next
    /// cluster-state broadcast — <c>IClusterStateUpdateObserver</c>) ⇒ one node's retraction never lands after another node's
    /// publication of the same id.</para>
    /// <para>⚠ Bounded: a hold older than <see cref="MaxHold"/> lapses with a warning, so a lost broadcast never silences a node.</para>
    /// <para>⭐ A Transient world singleton, the <see cref="WorldEpoch"/> pattern: no host has to wire it, a keyframe never carries it.</para>
    /// </remarks>
    [ComponentId(GlobalComponentIds.EgressHold)]
    [DataPolicy(DataPolicy.Transient)]
    public sealed class EgressHold
    {
        /// <summary>The longest a hold lasts without its release (a lost broadcast, a dead master).</summary>
        public static readonly TimeSpan MaxHold = TimeSpan.FromSeconds(3);

        /// <summary>Why the egress is held (<see langword="null"/> = not held).</summary>
        public string? Reason { get; private set; }

        private long _heldAtTimestamp;

        /// <summary>How many holds lapsed on <see cref="MaxHold"/> (diagnostics, rails).</summary>
        public int Lapsed { get; private set; }

        /// <summary>Holds the egress of <paramref name="world"/> until <see cref="Release"/> (or <see cref="MaxHold"/>).</summary>
        public static void Hold(EntityRepository world, string reason)
        {
            var h = Get(world);
            h.Reason = reason ?? "held";
            h._heldAtTimestamp = Stopwatch.GetTimestamp();
        }

        /// <summary>Releases the hold (no-op when not held).</summary>
        public static void Release(EntityRepository world)
        {
            if (!world.HasSingletonManaged<EgressHold>()) return;
            var h = world.GetSingletonManaged<EgressHold>();
            if (h != null) h.Reason = null;
        }

        /// <summary>⭐ Is the egress of this world held now? A hold past <see cref="MaxHold"/> lapses here.</summary>
        public static bool IsHeld(ISimulationView view)
        {
            if (view is not EntityRepository repo || !repo.HasSingletonManaged<EgressHold>()) return false;
            var h = repo.GetSingletonManaged<EgressHold>();
            if (h?.Reason == null) return false;
            if (Stopwatch.GetElapsedTime(h._heldAtTimestamp) <= MaxHold) return true;
            FdpLog<EgressHold>.Warn("Egress hold '{0}' lapsed after {1} s without the master's commit.", h.Reason, MaxHold.TotalSeconds);
            h.Reason = null;
            h.Lapsed++;
            return false;
        }

        private static EgressHold Get(EntityRepository world)
        {
            var h = world.HasSingletonManaged<EgressHold>() ? world.GetSingletonManaged<EgressHold>() : null;
            if (h == null) { h = new EgressHold(); world.SetSingletonManaged(h); }
            return h;
        }
    }
}
