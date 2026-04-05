using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FDP.Toolkit.Vis2D;
using Hrot.Editor.Tools;
using Hrot.NED.Common;
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

        /// <param name="canvas">The map canvas that hosts the tool stack.</param>
        public EditorMapPickAdapter(MapCanvas canvas)
        {
            _canvas = canvas;
        }

        /// <inheritdoc/>
        public Task<GeoPoint> PickLocationAsync(CancellationToken ct = default)
        {
            var tcs  = new TaskCompletionSource<GeoPoint>(TaskCreationOptions.RunContinuationsAsynchronously);
            var tool = new LocationPickerTool();

            tool.OnLocationPicked += geo =>
            {
                tcs.TrySetResult(geo);
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
            var tcs  = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var tool = new EntityPickerTool();

            tool.OnEntityPicked += idx =>
            {
                tcs.TrySetResult(idx);
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
