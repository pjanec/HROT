using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Hrot.Editor.Gizmos;
using Hrot.IG.Systems;
using Hrot.ScenarioEditor.Gizmos;
using Hrot.UI.Common.Facades;

namespace Hrot.Editor.Adapters
{
    /// <summary>
    /// Implements <see cref="IMapPickService"/> for the offline editor by pushing
    /// modal picker tools onto the <see cref="MapCanvas"/> and bridging their callbacks
    /// into <see cref="Task{T}"/> via <see cref="TaskCompletionSource{T}"/>.
    ///
    /// <para>
    /// All three pick operations follow the same pattern:
    /// push tool → wire callbacks → return <c>tcs.Task</c>.
    /// The registered cancellation handler calls <see cref="MapCanvas.PopTool"/> and
    /// cancels the <see cref="TaskCompletionSource{T}"/> if the active tool is the one
    /// we pushed.
    /// </para>
    ///
    /// No DDS or CycloneDDS references.
    /// </summary>
    public sealed class EditorMapPickAdapter : IMapPickService
    {
        private readonly MapCanvas _canvas;
        private readonly IGeographicTransform _geoTransform;
        private readonly EntityRepository? _repo;
        private readonly IEntityFilterFactory? _filterFactory;
        private readonly GlobalGizmoManager? _globalGizmoManager;

        /// <param name="canvas">The map canvas that hosts the tool stack.</param>
        /// <param name="geoTransform">Used to convert flat-map Cartesian picks to WGS-84 geodetic coordinates.</param>
        /// <param name="repo">
        /// ECS entity repository used to translate picked ECS entity handles to
        /// <see cref="NetworkIdentity.Value"/> (Network ID) before completing pick tasks.
        /// </param>
        /// <param name="globalGizmoManager">The global gizmo manager for placement gizmos.</param>
        public EditorMapPickAdapter(MapCanvas canvas, IGeographicTransform geoTransform, EntityRepository? repo = null, GlobalGizmoManager? globalGizmoManager = null)
        {
            _canvas        = canvas;
            _geoTransform  = geoTransform ?? throw new ArgumentNullException(nameof(geoTransform));
            _repo          = repo;
            _filterFactory = repo != null ? new HrotEntityFilterFactory(repo) : null;
            _globalGizmoManager = globalGizmoManager;
        }

        /// <inheritdoc/>
        public Task<Hrot.Core.Mission.GeoPoint> PickLocationAsync(CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<Hrot.Core.Mission.GeoPoint>(TaskCreationOptions.RunContinuationsAsynchronously);
            var id = GlobalGizmoManager.NewId();
            var gizmo = new LocationPickerGizmo(
                _geoTransform,
                geo => tcs.TrySetResult(new Hrot.Core.Mission.GeoPoint(geo.Latitude, geo.Longitude, geo.Altitude)),
                onRemove: () => _globalGizmoManager!.Unregister(id));

            ct.Register(() =>
            {
                _globalGizmoManager!.Unregister(id);
                tcs.TrySetCanceled(ct);
            });

            _globalGizmoManager!.Register(id, gizmo);
            return tcs.Task;
        }

        /// <inheritdoc/>
        public Task<int> PickEntityAsync(
            string[]? filterPresets = null,
            CancellationToken ct    = default)
        {
            if (_filterFactory is null)
                throw new InvalidOperationException(
                    "PickEntityAsync requires an EntityRepository. Pass a non-null repo to the EditorMapPickAdapter constructor.");

            var tcs    = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var id     = GlobalGizmoManager.NewId();
            var filter = _filterFactory.CreateFilter(filterPresets ?? Array.Empty<string>());
            var gizmo  = new Fdp.Toolkit.Vis2D.Gizmos.EntityPickerGizmo(
                hitTest:     pos => _canvas.PickTopmostEntity(pos) ?? Fdp.Core.Entity.Null,
                filter:      filter,
                onPicked:    entity =>
                {
                    // Translate ECS entity handle to Network ID via NetworkIdentity component.
                    int netId = -1;
                    if (_repo != null && _repo.IsAlive(entity) && _repo.HasComponent<NetworkIdentity>(entity))
                        netId = (int)_repo.GetComponent<NetworkIdentity>(entity).Value;
                    tcs.TrySetResult(netId);
                },
                onCancelled: () => tcs.TrySetCanceled(),
                onRemove:    () => _globalGizmoManager!.Unregister(id));

            ct.Register(() =>
            {
                _globalGizmoManager!.Unregister(id);
                tcs.TrySetCanceled(ct);
            });

            _globalGizmoManager!.Register(id, gizmo);
            return tcs.Task;
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<int>> PickAreaEntitiesAsync(
            string[]? filterPresets = null,
            CancellationToken ct    = default)
        {
            var tcs = new TaskCompletionSource<IReadOnlyList<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var id = GlobalGizmoManager.NewId();
            var gizmo = new ModalBoxSelectionGizmo(
                list => tcs.TrySetResult(list),
                onRemove: () => _globalGizmoManager!.Unregister(id));

            ct.Register(() =>
            {
                _globalGizmoManager!.Unregister(id);
                tcs.TrySetCanceled(ct);
            });

            _globalGizmoManager!.Register(id, gizmo);
            return tcs.Task;
        }
    }
}
