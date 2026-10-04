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
            if (endingInstanceId == 0) return 0;
            int count = 0;
            Entity[]? doomed = null;
            // ⚠ Collect first, destroy after: never mutate structure under a live query.
            foreach (var part in repo.Query().With<BehaviorOwnedPart>().With<PartMetadata>().Build())
            {
                ref readonly var meta  = ref repo.GetComponentRO<PartMetadata>(part);
                ref readonly var owned = ref repo.GetComponentRO<BehaviorOwnedPart>(part);
                if (!meta.ParentEntity.Equals(parent) || owned.OwnerInstanceId != endingInstanceId) continue;
                doomed ??= new Entity[4];
                if (count == doomed.Length) System.Array.Resize(ref doomed, count * 2);
                doomed[count++] = part;
            }
            for (int i = 0; i < count; i++) repo.DestroyEntity(doomed![i]);
            return count;
        }
    }
}
