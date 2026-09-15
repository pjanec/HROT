using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;
using Hrot.Core.Mission;

namespace Hrot.UI.Common.Facades;

/// <summary>
/// Implements <see cref="IMapPickService"/> using a local <see cref="MapCanvas"/> and
/// gizmo-based pickers.  Suitable for Editor, CGF, SimHost, and IG subsystems that
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
    private readonly GlobalGizmoManager? _globalGizmoManager;

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
    /// <param name="globalGizmoManager">
    /// Optional gizmo manager used to host picker gizmos.  When <see langword="null"/>
    /// the pick operations are not available.
    /// </param>
    /// <param name="tools">
    /// 🔒 <b><c>UXI-07</c> step 4b — the host's arbiter.</b> ⛔ Optional so an unconverted host still
    /// picks, but a production caller that HAS it must PASS it: without it a pick arms beside the active
    /// tool instead of suspending it (§4.8's bypass — and 🔴 <c>CE-254</c> is this adapter).
    /// </param>
    public CanvasMapPickAdapter(
        MapCanvas canvas,
        EntityRepository? repo = null,
        IEntityFilterFactory? filterFactory = null,
        GlobalGizmoManager? globalGizmoManager = null,
        Func<Hrot.ScenarioEditor.Tools.ToolController?>? tools = null)
    {
        _canvas             = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _repo               = repo;
        _filterFactory      = filterFactory ?? DefaultFactory;
        _globalGizmoManager = globalGizmoManager;
        _pickers            = new Hrot.ScenarioEditor.Tools.PickerToolHost(
            tools ?? (() => null), () => _globalGizmoManager);
    }

    /// <summary>⭐ The ONE picker-modal protocol — see <c>PickerToolHost</c>.</summary>
    private readonly Hrot.ScenarioEditor.Tools.PickerToolHost _pickers;

    /// <inheritdoc/>
    public Task<GeoPoint> PickLocationAsync(CancellationToken ct = default)
        => _pickers.RunPickAsync<GeoPoint>(
            Hrot.ScenarioEditor.Tools.ScenarioToolIds.PickLocation,
            (tcs, remove) => new Fdp.Toolkit.Vis2D.Gizmos.FdpLocationPickerGizmo(
                onPicked: worldPos => tcs.TrySetResult(new GeoPoint(worldPos.X, worldPos.Y, 0)),
                onRemove: remove),
            ct);

    /// <inheritdoc/>
    public Task<int> PickEntityAsync(string[]? filterPresets = null, CancellationToken ct = default)
    {
        var filter = _filterFactory.CreateFilter(filterPresets ?? Array.Empty<string>());

        return _pickers.RunPickAsync<int>(
            Hrot.ScenarioEditor.Tools.ScenarioToolIds.PickEntity,
            (tcs, remove) => new Fdp.Toolkit.Vis2D.Gizmos.EntityPickerGizmo(
                hitTest:     pos => _canvas.PickTopmostEntity(pos) ?? Fdp.Core.Entity.Null,
                filter:      filter,
                onPicked:    entity =>
                {
                    int networkId = -1;
                    if (_repo != null
                        && _repo.IsAlive(entity)
                        && _repo.HasComponent<NetworkIdentity>(entity))
                    {
                        networkId = (int)_repo.GetComponentRO<NetworkIdentity>(entity).Value;
                    }
                    tcs.TrySetResult(networkId);
                },
                onCancelled: () => tcs.TrySetCanceled(),
                onRemove:    remove),
            ct);
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
