using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Tools;
using Hrot.Editor.Tools;
using Hrot.IG.Systems;
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

        /// <param name="canvas">The map canvas that hosts the tool stack.</param>
        /// <param name="geoTransform">Used to convert flat-map Cartesian picks to WGS-84 geodetic coordinates.</param>
        /// <param name="repo">
        /// ECS entity repository used to translate picked ECS entity handles to
        /// <see cref="NetworkIdentity.Value"/> (Network ID) before completing pick tasks.
        /// </param>
        public EditorMapPickAdapter(MapCanvas canvas, IGeographicTransform geoTransform, EntityRepository? repo = null)
        {
            _canvas        = canvas;
            _geoTransform  = geoTransform ?? throw new ArgumentNullException(nameof(geoTransform));
            _repo          = repo;
            _filterFactory = repo != null ? new HrotEntityFilterFactory(repo) : null;
        }

        /// <inheritdoc/>
        public Task<Hrot.Core.Mission.GeoPoint> PickLocationAsync(CancellationToken ct = default)
        {
            var tcs  = new TaskCompletionSource<Hrot.Core.Mission.GeoPoint>(TaskCreationOptions.RunContinuationsAsynchronously);
            var tool = new Hrot.Editor.Tools.LocationPickerTool(_geoTransform);

            tool.OnLocationPicked += geo =>
            {
                tcs.TrySetResult(new Hrot.Core.Mission.GeoPoint(geo.Latitude, geo.Longitude, geo.Altitude));
            };

            tool.OnCancelled += () =>
            {
                tcs.TrySetCanceled();
            };

            CancellationTokenRegistration reg = default;
            reg = ct.Register(() =>
            {
                if (_canvas.ActiveTool == tool)
                    _canvas.PopTool();
                tcs.TrySetCanceled(ct);
                reg.Dispose();
            });

            _canvas.PushTool(tool);
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

            var tcs  = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var tool = new Fdp.Toolkit.Vis2D.Tools.EntityPickerTool(_filterFactory, filterPresets);

            tool.OnEntityPicked += entity =>
            {
                // Translate ECS entity handle to Network ID via NetworkIdentity component.
                if (_repo.IsAlive(entity) && _repo.HasComponent<NetworkIdentity>(entity))
                {
                    long netId = _repo.GetComponent<NetworkIdentity>(entity).Value;
                    tcs.TrySetResult((int)netId);
                }
                else
                {
                    tcs.TrySetResult(-1);
                }
            };

            tool.OnCancelled += () =>
            {
                tcs.TrySetCanceled();
            };

            CancellationTokenRegistration reg = default;
            reg = ct.Register(() =>
            {
                if (_canvas.ActiveTool == tool)
                    _canvas.PopTool();
                tcs.TrySetCanceled(ct);
                reg.Dispose();
            });

            _canvas.PushTool(tool);
            return tcs.Task;
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<int>> PickAreaEntitiesAsync(
            string[]? filterPresets = null,
            CancellationToken ct    = default)
        {
            var tcs  = new TaskCompletionSource<IReadOnlyList<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var tool = new ModalBoxSelectionTool();

            tool.OnSelectionComplete += list =>
            {
                tcs.TrySetResult(list);
            };

            tool.OnCancelled += () =>
            {
                tcs.TrySetCanceled();
            };

            CancellationTokenRegistration reg = default;
            reg = ct.Register(() =>
            {
                if (_canvas.ActiveTool == tool)
                    _canvas.PopTool();
                tcs.TrySetCanceled(ct);
                reg.Dispose();
            });

            _canvas.PushTool(tool);
            return tcs.Task;
        }
    }
}
