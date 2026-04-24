using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Tools;
using Hrot.Core.Mission;

namespace Hrot.UI.Common.Facades;

/// <summary>
/// Implements <see cref="IMapPickService"/> using a local <see cref="MapCanvas"/> and
/// the Fdp picker tools.  Suitable for Editor, CGF, SimHost, and IG subsystems that
/// own a canvas but do not depend on <c>Hrot.Editor.Tools</c>.
///
/// <para>Location picks return the raw world-space cartesian position encoded as a
/// <see cref="GeoPoint"/>: Latitude = world-X, Longitude = world-Y, Altitude = 0.</para>
/// </summary>
public sealed class CanvasMapPickAdapter : IMapPickService
{
    private readonly MapCanvas _canvas;
    private readonly EntityRepository? _repo;
    private readonly IEntityFilterFactory _filterFactory;

    // Match-all factory used when no domain-specific factory is provided.
    private static readonly IEntityFilterFactory DefaultFactory = new MatchAllFilterFactory();

    /// <summary>
    /// Creates a <see cref="CanvasMapPickAdapter"/>.
    /// </summary>
    /// <param name="canvas">The canvas to push picker tools onto.</param>
    /// <param name="repo">
    /// Optional entity repository used to look up <see cref="NetworkIdentity"/>
    /// on a picked entity.  When <see langword="null"/>, entity picks always return -1.
    /// </param>
    /// <param name="filterFactory">
    /// Optional domain-specific filter factory.  When <see langword="null"/> a
    /// match-all factory is used so every entity qualifies.
    /// </param>
    public CanvasMapPickAdapter(
        MapCanvas canvas,
        EntityRepository? repo = null,
        IEntityFilterFactory? filterFactory = null)
    {
        _canvas        = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _repo          = repo;
        _filterFactory = filterFactory ?? DefaultFactory;
    }

    /// <inheritdoc/>
    public Task<GeoPoint> PickLocationAsync(CancellationToken ct = default)
    {
        var tcs  = new TaskCompletionSource<GeoPoint>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tool = new LocationPickerTool();

        tool.OnLocationPicked += worldPos =>
            tcs.TrySetResult(new GeoPoint(worldPos.X, worldPos.Y, 0));
        tool.OnCancelled += () => tcs.TrySetCanceled();

        ct.Register(() =>
        {
            if (_canvas.ActiveTool == tool) _canvas.PopTool();
            tcs.TrySetCanceled(ct);
        });

        _canvas.PushTool(tool);
        return tcs.Task;
    }

    /// <inheritdoc/>
    public Task<int> PickEntityAsync(string[]? filterPresets = null, CancellationToken ct = default)
    {
        var tcs  = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tool = new EntityPickerTool(_filterFactory, filterPresets);

        tool.OnEntityPicked += entity =>
        {
            int networkId = -1;
            if (_repo != null
                && _repo.IsAlive(entity)
                && _repo.HasComponent<NetworkIdentity>(entity))
            {
                networkId = (int)_repo.GetComponentRO<NetworkIdentity>(entity).Value;
            }
            tcs.TrySetResult(networkId);
        };
        tool.OnCancelled += () => tcs.TrySetCanceled();

        ct.Register(() =>
        {
            if (_canvas.ActiveTool == tool) _canvas.PopTool();
            tcs.TrySetCanceled(ct);
        });

        _canvas.PushTool(tool);
        return tcs.Task;
    }

    /// <inheritdoc/>
    /// <remarks>Area entity pick is not supported by this adapter; always returns an empty list.</remarks>
    public Task<IReadOnlyList<int>> PickAreaEntitiesAsync(
        string[]? filterPresets = null, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<int>>(Array.Empty<int>());

    // ── Internal filter helpers ───────────────────────────────────────────────

    private sealed class MatchAllFilterFactory : IEntityFilterFactory
    {
        private static readonly MatchAllFilter Filter = new();
        public IEntityFilter CreateFilter(string[] filterPresets) => Filter;
    }

    private sealed class MatchAllFilter : IEntityFilter
    {
        public bool IsMatch(Entity entity) => true;
    }
}
