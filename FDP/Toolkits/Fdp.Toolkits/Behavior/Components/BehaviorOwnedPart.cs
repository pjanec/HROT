using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Behavior.Components
{
    /// <summary>
    /// ⭐ <b><c>CE-485</c> — a child part (an EQS sensor) belongs to the behaviour RUN that created it.</b>
    /// 📄 <c>docs/blueprints/DESIGN_Behaviour_Fault_And_Teardown.md</c> §1 D4/D5.
    ///
    /// <para>Brain-local — never on the wire. The part's network identity is its <see cref="PartMetadata.InstanceId"/> (an
    /// allocated, reused part id); this stamp is what tells the behaviour WHICH of its parent's parts is its own:
    /// <see cref="SiteId"/> (the creating call site — a blueprint spawn node, a C# constant) and <see cref="Key"/> (one
    /// sensor per key from a single site), for the run <see cref="OwnerInstanceId"/>.</para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(BehaviorApplicationComponentIds.BehaviorOwnedPart)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct BehaviorOwnedPart
    {
        /// <summary>The parent's <see cref="BehaviorState.InstanceId"/> when the part was created; 0 = not behaviour-owned.</summary>
        public uint OwnerInstanceId;
        /// <summary>The creating call site.</summary>
        public int SiteId;
        /// <summary>Distinguishes several parts from one site (0 = the default single part).</summary>
        public long Key;
    }

    /// <summary>
    /// ⭐ <b><c>CE-485</c> — the behaviour-wide teardown of owned parts.</b> 📄 <c>DESIGN_Behaviour_Fault_And_Teardown.md</c> §1 D4.
    ///
    /// <para>⭐ <c>CE-2141</c>: the run's orders to its members (<see cref="SubordinateOrders"/>) are released in the same step.</para>
    /// <para>Called by <c>BehaviorIngressSystem</c> at EVERY site that ends a behaviour run — the sites that bump
    /// <see cref="BehaviorState.InstanceId"/> (clear / finish / fault, every (re)start, the unhosted assign) — just before the
    /// bump. Destroys immediately: a restart in the same frame must not find the ending run's part.</para>
    /// </summary>
    public static class BehaviorOwnedParts
    {
        /// <summary>The run that owns parts created under <paramref name="parent"/> right now (0 when it runs no behaviour).</summary>
        public static uint OwnerOf(ISimulationView view, Entity parent)
            => BrainSlotScope.TryGetInstanceId(parent, out uint slotRun) ? slotRun   // ⭐ CE-3035 — a part the SOP spawns is the SOP run's
            : view.IsAlive(parent) && view.HasComponent<BehaviorState>(parent)
                ? view.GetComponentRO<BehaviorState>(parent).InstanceId
                : 0u;

        /// <summary>Destroy every part of <paramref name="parent"/> owned by the run <paramref name="endingInstanceId"/>.
        /// Returns how many were destroyed.</summary>
        public static int Release(EntityRepository repo, Entity parent, uint endingInstanceId)

        {

            // ⭐ CE-4395 B-1 — timed (QueryTimings "brain.OwnedParts.Release"; measurement only, the behaviour is the Core's)

            long t0 = Fdp.Toolkit.Diagnostics.QueryTimings.Begin();

            try { return ReleaseCore(repo, parent, endingInstanceId); }

            finally { Fdp.Toolkit.Diagnostics.QueryTimings.End(S_Release, t0); }

        }


        private static readonly int S_Release = Fdp.Toolkit.Diagnostics.QueryTimings.Slot("brain.OwnedParts.Release");


        private static int ReleaseCore(EntityRepository repo, Entity parent, uint endingInstanceId)
        {
            if (endingInstanceId == 0) return 0;
            SubordinateOrders.Release(repo, parent, endingInstanceId);   // ⭐ CE-2141 — the orders the run gave its members end with it too
            // ⭐ CE-4395 — the parent's parts from the ONE parts index (PartIndex, CE-2412), not a walk over every entity per call: 📐 279 µs a
            //   call at 3 000 men. Destroyed in ENTITY order, as the walk met them, so the freed indices recycle exactly as before.
            if (!repo.IsComponentTypeRegistered<BehaviorOwnedPart>()) return 0;   // no owned part can exist (the walk found none)
            System.Span<Entity> parts = stackalloc Entity[32];
            int total = Fdp.Toolkit.Replication.Utilities.PartIndex.PartsOf(repo, parent, parts);
            if (total > parts.Length) { parts = new Entity[total]; total = Fdp.Toolkit.Replication.Utilities.PartIndex.PartsOf(repo, parent, parts); }
            int count = 0;
            for (int i = 0; i < total; i++)
            {
                var part = parts[i];
                if (!repo.HasComponent<BehaviorOwnedPart>(part) || repo.GetComponentRO<BehaviorOwnedPart>(part).OwnerInstanceId != endingInstanceId) continue;
                int at = count++;                                   // keep the doomed sorted by entity index (a handful: insertion)
                while (at > 0 && parts[at - 1].Index > part.Index) { parts[at] = parts[at - 1]; at--; }
                parts[at] = part;
            }
            // ⚠ Collect first, destroy after: the index is read above, never mutated under a read.
            for (int i = 0; i < count; i++) repo.DestroyEntity(parts[i]);
            return count;
        }
    }
}
