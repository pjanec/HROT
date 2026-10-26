using System;
using System.Collections.Generic;
using System.Linq;

namespace Fdp.Toolkit.ReplayBrowser.Timeline
{
    /// <summary>
    /// ⭐ <c>R-4078</c> / <c>R-4079</c> (docs/DESIGN_Director_Timeline_Editor.md §9c) — every lane a timeline can show, as REGISTERED: the
    /// providers of the modules that own the data, plus lanes a user built from a predicate (§9c ⑤). ⛔ Nothing here, in the index or in the
    /// panel names a lane — a host composes its catalogue and the timeline draws what is in it.
    /// </summary>
    public sealed class TimelineLaneCatalog
    {
        private readonly List<ITimelineLaneProvider> _providers = new();

        /// <summary>Bumped on every registration, so a host can re-open its index when the lanes change.</summary>
        public int Version { get; private set; }

        public IReadOnlyList<ITimelineLaneProvider> Providers => _providers;

        public TimelineLaneCatalog Register(ITimelineLaneProvider provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            _providers.Add(provider);
            Version++;
            return this;
        }

        /// <summary>A user's lane (§9c ⑤): a predicate from the existing editor, indexed on the next open.</summary>
        public TimelineLaneCatalog Register(IndexedLaneDefinition lane) => Register(new SingleLane(lane));

        /// <summary>The lanes that belong where the timeline is shown, in registration order.</summary>
        public IReadOnlyList<TimelineLaneDescriptor> Lanes(TimelineLaneContext context) =>
            _providers.Where(p => p.Applies(context)).SelectMany(p => p.Lanes).ToArray();

        /// <summary>The lanes the recording's index must build for this context.</summary>
        public IReadOnlyList<IndexedLaneDefinition> Indexed(TimelineLaneContext context) =>
            _providers.Where(p => p.Applies(context)).SelectMany(p => p.Indexed).ToArray();

        /// <summary>The registered descriptor of a lane id, or null.</summary>
        public TimelineLaneDescriptor? Find(string laneId) =>
            _providers.SelectMany(p => p.Lanes).FirstOrDefault(l => l.Id == laneId);

        /// <summary>⭐ <c>CE-3330</c> (R-4087 addendum 4) — the menu items the provider that OWNS <paramref name="context"/>'s lane contributes.</summary>
        public IReadOnlyList<TimelineMenuItem> MenuItems(TimelineMenuContext context)
        {
            if (context.LaneId == null) return Array.Empty<TimelineMenuItem>();
            foreach (var p in _providers)
                if (p.Lanes.Any(l => l.Id == context.LaneId)) return p.MenuItems(context).ToList();
            return Array.Empty<TimelineMenuItem>();
        }

        private sealed class SingleLane : ITimelineLaneProvider
        {
            private readonly IndexedLaneDefinition _lane;
            public SingleLane(IndexedLaneDefinition lane) => _lane = lane ?? throw new ArgumentNullException(nameof(lane));
            public IReadOnlyList<TimelineLaneDescriptor> Lanes => new[] { _lane.Lane };
            public bool Applies(TimelineLaneContext context) => context.Recording;
            public IReadOnlyList<IndexedLaneDefinition> Indexed => new[] { _lane };
        }
    }
}
