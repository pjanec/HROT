using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Replication;

namespace Fdp.Toolkit.ReplayBrowser.Timeline
{
    /// <summary>
    /// ⭐ <c>CE-3330</c> — the ONE composition of the lanes the toolkits ship (Replication, Combat, Behaviour, in that order). The editor, the
    /// Replay Browser and every ECS node's live timeline (<c>HrotNodeContext</c>) take their catalogue from here, so the three cannot drift.
    /// A host adds a user's lane to the catalogue it gets; it never builds a second list of providers.
    /// </summary>
    public static class ShippedTimelineLanes
    {
        /// <summary>A NEW catalogue (the caller registers user lanes into it) holding the shipped providers.</summary>
        public static TimelineLaneCatalog Catalog() => new TimelineLaneCatalog()
            .Register(new ReplicationTimelineLanes())
            .Register(new CombatTimelineLanes())
            .Register(new BehaviourTimelineLanes());
    }
}
