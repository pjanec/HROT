using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;

namespace Fdp.Toolkit.Behavior.Systems
{
    /// <summary>
    /// ⭐ <c>CE-2282</c> DA-26 — applies <see cref="SetEngagementAreaEvent"/>: the ONE writer of <see cref="EngagementArea"/>, gated by
    /// origin exactly as <c>RoeSystem</c> and <c>ReportingRequirementsSystem</c> are (a lower origin than the setter's is refused; the
    /// unset state yields to anyone). The event REPLACES the set; all empty withdraws it. A networked replica is left to its brain's owner
    /// (the cross-node road is a tactical-intent kind, backend). 📄 Defend &amp; Attack §13c.
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    [TickMode(TickPolicy.EveryFrame)]
    public sealed class EngagementAreaSystem : IEcsModuleSystem
    {
        /// <summary>Refused changes since this system was built (a test / diagnostics probe).</summary>
        public int RefusedCount { get; private set; }

        /// <summary>Events left to the brain's owner (a replica here) since this system was built.</summary>
        public int ForwardedCount { get; private set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo) return;
            if (!repo.IsComponentTypeRegistered<EngagementArea>() || !repo.Bus.IsRegistered<SetEngagementAreaEvent>()) return;
            bool replicas = repo.IsComponentTypeRegistered<BehaviorState>()
                         && repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
            foreach (var evt in repo.ReadEvents<SetEngagementAreaEvent>())
            {
                if (replicas && repo.HasComponent<BehaviorState>(evt.Entity) && !repo.HasAuthority<BehaviorState>(evt.Entity)
                    && repo.HasComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(evt.Entity))
                {
                    ForwardedCount++;
                    continue;
                }
                if (!Apply(repo, evt)) RefusedCount++;
            }
        }

        /// <summary>What a <see cref="SetEngagementAreaEvent"/> does, applied NOW. <c>false</c> = refused by the gate, or a dead entity.</summary>
        public static bool Apply(EntityRepository repo, in SetEngagementAreaEvent evt)
        {
            if (!repo.IsAlive(evt.Entity) || !repo.IsComponentTypeRegistered<EngagementArea>()) return false;
            if (!repo.HasComponent<EngagementArea>(evt.Entity)) repo.AddComponent(evt.Entity, new EngagementArea());
            ref var a = ref repo.GetComponentRW<EngagementArea>(evt.Entity);
            var origin = evt.Origin == BehaviorOrigin.Self ? a.SetBy : evt.Origin;
            if (a.SetBy != BehaviorOrigin.Unmarked && BehaviorOriginRank.Of(origin) < BehaviorOriginRank.Of(a.SetBy)) return false;

            a.ArcSet = evt.ArcSet;
            a.ArcOrigin = evt.ArcOrigin;
            a.LeftBearing = evt.LeftBearing;
            a.RightBearing = evt.RightBearing;
            a.MinRange = evt.MinRange > 0f ? evt.MinRange : 0f;
            a.MaxRange = evt.MaxRange > 0f ? evt.MaxRange : 0f;
            a.AllowArea = evt.AllowArea;
            a.WatchBearing = evt.WatchBearing;
            a.WatchSet = evt.WatchSet;
            a.NoFire = evt.NoFire;
            if (a.NoFire.Count > EngagementArea.MaxNoFire) a.NoFire.Count = EngagementArea.MaxNoFire;
            bool withdrawn = evt.ArcSet == 0 && evt.AllowArea == 0 && evt.WatchSet == 0 && evt.NoFire.Count == 0;
            a.SetBy = withdrawn ? BehaviorOrigin.Unmarked : origin == BehaviorOrigin.Unmarked ? BehaviorOrigin.Operator : origin;
            return true;
        }
    }
}
