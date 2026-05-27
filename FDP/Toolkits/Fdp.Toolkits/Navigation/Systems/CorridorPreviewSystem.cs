using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Navigation.Systems
{
    /// <summary>
    /// Maintains the opt-in <see cref="NavigationCorridorPreview"/> sliding window (N=8).
    /// Present on an entity only when <see cref="NavigationConstants.FlagBitStreamCorridorPreview"/>
    /// is set in <see cref="NavigationIntent.Flags"/>; absent otherwise.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public sealed class CorridorPreviewSystem : IEcsModuleSystem
    {
        private readonly IPathRegistry _registry;

        // Scratch buffer reused each tick to avoid per-frame heap allocation.
        private readonly NavWaypoint[] _waypointScratch = new NavWaypoint[8];

        public CorridorPreviewSystem(IPathRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(CorridorPreviewSystem)} requires direct EntityRepository access.");

            const byte previewBit = 1 << NavigationConstants.FlagBitStreamCorridorPreview;

            var query = repo.Query()
                .With<NavigationIntent>()
                .With<NavigationCorridorMuscle>()
                .Build();

            foreach (var entity in query)
            {
                var intent   = repo.GetComponent<NavigationIntent>(entity);
                var corridor = repo.GetComponent<NavigationCorridorMuscle>(entity);

                bool wantsPreview = (intent.Flags & previewBit) != 0;

                if (!wantsPreview)
                {
                    // Remove component if present.
                    if (repo.HasComponent<NavigationCorridorPreview>(entity))
                        repo.RemoveComponent<NavigationCorridorPreview>(entity);
                    continue;
                }

                if (corridor.RouteHandle == 0)
                {
                    // No active path — clear the preview.
                    if (repo.HasComponent<NavigationCorridorPreview>(entity))
                        repo.RemoveComponent<NavigationCorridorPreview>(entity);
                    continue;
                }

                int startIdx = Math.Max(0, corridor.CurrentSegmentIndex);

                // Read up to 8 waypoints from the path registry starting at startIdx.
                if (!_registry.TryGetWaypointsSlice(
                        corridor.RouteHandle,
                        startIdx,
                        8,
                        _waypointScratch.AsSpan(),
                        out int count) || count <= 0)
                {
                    if (repo.HasComponent<NavigationCorridorPreview>(entity))
                        repo.RemoveComponent<NavigationCorridorPreview>(entity);
                    continue;
                }

                // Build the new preview value.
                var newPreview = BuildPreview(_waypointScratch, startIdx, count);

                if (repo.HasComponent<NavigationCorridorPreview>(entity))
                {
                    var existing = repo.GetComponent<NavigationCorridorPreview>(entity);
                    // Bump PreviewVersion only if the window changed.
                    if (existing.GlobalSegmentStart != startIdx || existing.WaypointCount != count)
                    {
                        newPreview.PreviewVersion = existing.PreviewVersion + 1;
                        repo.SetComponent(entity, newPreview);
                    }
                }
                else
                {
                    newPreview.PreviewVersion = 1;
                    repo.AddComponent(entity, newPreview);
                }
            }
        }

        private static NavigationCorridorPreview BuildPreview(NavWaypoint[] all, int startIdx, int count)
        {
            var p = new NavigationCorridorPreview
            {
                GlobalSegmentStart = startIdx,
                WaypointCount      = count,
            };

            // Inline assignment of up to 8 waypoints (all[] is indexed from 0 = startIdx).
            if (count > 0) p.W0 = ToPreview(all[0]);
            if (count > 1) p.W1 = ToPreview(all[1]);
            if (count > 2) p.W2 = ToPreview(all[2]);
            if (count > 3) p.W3 = ToPreview(all[3]);
            if (count > 4) p.W4 = ToPreview(all[4]);
            if (count > 5) p.W5 = ToPreview(all[5]);
            if (count > 6) p.W6 = ToPreview(all[6]);
            if (count > 7) p.W7 = ToPreview(all[7]);

            return p;
        }

        private static PreviewWaypoint ToPreview(in NavWaypoint wp) => new PreviewWaypoint
        {
            Position  = wp.Position,
            Traversal = wp.Traversal,
            Surface   = wp.Surface,
        };
    }
}
