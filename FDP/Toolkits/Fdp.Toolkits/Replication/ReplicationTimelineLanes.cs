using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.ReplayBrowser.Timeline;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Replication
{
    /// <summary>
    /// ⭐ <c>R-4079</c> (docs/DESIGN_Director_Timeline_Editor.md §9c ②) — the Life lanes, as DATA, registered by the toolkit that owns
    /// <see cref="NetworkIdentity"/>: a unit APPEARS when an entity gains a network id and LEAVES when it loses it or is destroyed (structural
    /// predicates). ⚠ Only networked entities — rounds and other local helpers have none and would drown the lane. X / Y are where it
    /// appeared, or was last seen.
    /// </summary>
    public sealed class ReplicationTimelineLanes : ITimelineLaneProvider
    {
        public const string Group = "Life";
        public static readonly TimelineLaneDescriptor Spawned = new("life.spawned", "Spawned", Group: Group, Priority: 0, Color: 0xFF66CC66);
        public static readonly TimelineLaneDescriptor Left = new("life.left", "Left", Group: Group, Priority: 6, Color: 0xFF3030C0);

        private static ResultProjectionDto Where() => new() { SourceComponent = typeof(SimTransform), XPath = "Position.X", YPath = "Position.Y" };

        private static readonly IndexedLaneDefinition[] Definitions =
        {
            new(Spawned, new StructuralPredicateDto { ComponentType = typeof(NetworkIdentity), ModificationType = StructuralModification.Added }, Where()),
            new(Left, new StructuralPredicateDto { ComponentType = typeof(NetworkIdentity), ModificationType = StructuralModification.Removed }, Where()),
        };

        public IReadOnlyList<TimelineLaneDescriptor> Lanes { get; } = new[] { Spawned, Left };
        public IReadOnlyList<IndexedLaneDefinition> Indexed => Definitions;
        public bool Applies(TimelineLaneContext context) => context.Indexing;   // ⭐ CE-3330 — a recording's index or the live one
    }
}
