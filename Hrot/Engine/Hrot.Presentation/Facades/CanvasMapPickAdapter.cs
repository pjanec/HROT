using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;
using Hrot.Core.Mission;

namespace Hrot.UI.Common.Facades;

/// <summary>
/// ⭐⭐ THE <see cref="IMapPickService"/> over a local <see cref="MapCanvas"/> — the editor, CGF, SimHost and IG all
/// build this one (<c>CE-063</c>: the editor's twin, <c>EditorMapPickAdapter</c>, and its location gizmo were deleted;
/// 📄 <c>docs/blueprints/DESIGN_Map_Picking_Unification.md</c> P2/P3).
///
/// <para>Location picks are GEODETIC through the host's <see cref="IGeographicTransform"/> — passed, or the world's
/// singleton. ⚠ With neither (a test, a host with no geography) the world X/Y is returned in the Latitude/Longitude
/// fields, Altitude 0.</para>
/// </summary>
public sealed class CanvasMapPickAdapter : IMapPickService
{
    private readonly MapCanvas _canvas;
    private readonly EntityRepository? _repo;
    private readonly IEntityFilterFactory _filterFactory;
    private readonly GlobalGizmoManager? _globalGizmoManager;
    private readonly Fdp.Modules.Geographic.IGeographicTransform? _geoTransform;
    private readonly Func<Action<IReadOnlyList<int>>, Action, Fdp.Toolkit.Diagnostics.Gizmos.IEntityStatefulGizmo>? _areaGizmo;

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
    /// <param name="geoTransform">
    /// ⭐ The host's geographic transform: a location pick converts the clicked world position to WGS-84 through it.
    /// ⛔ A production caller that HAS one must pass it (the silent-default rule) — without it a mission location is
    /// written in metres where degrees belong.
    /// </param>
    /// <param name="areaGizmo">
    /// Optional area-selection gizmo factory <c>(onPicked, onRemove)</c>; without one an area pick answers empty at
    /// once.
    /// </param>
    public CanvasMapPickAdapter(
        MapCanvas canvas,
        EntityRepository? repo = null,
        IEntityFilterFactory? filterFactory = null,
        GlobalGizmoManager? globalGizmoManager = null,
        Func<Hrot.ScenarioEditor.Tools.ToolController?>? tools = null,
        Fdp.Modules.Geographic.IGeographicTransform? geoTransform = null,
        Func<Action<IReadOnlyList<int>>, Action, Fdp.Toolkit.Diagnostics.Gizmos.IEntityStatefulGizmo>? areaGizmo = null)
    {
        _canvas             = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _repo               = repo;
        _filterFactory      = filterFactory ?? DefaultFactory;
        _geoTransform       = geoTransform;
        _areaGizmo          = areaGizmo;
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
                onPicked: worldPos => tcs.TrySetResult(ToGeoPoint(worldPos)),
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

    /// <summary>The clicked world position as a <see cref="GeoPoint"/> — geodetic through the host's transform.
    /// ⭐ The passed transform, else the world's <see cref="IGeographicTransform"/> singleton — the one behaviour resolvers
    /// read (SimHost, CGF and the editor publish it) — resolved at pick time, since a host may publish it after building
    /// this adapter.</summary>
    private GeoPoint ToGeoPoint(System.Numerics.Vector2 world)
    {
        var geo = _geoTransform
                  ?? (_repo != null && _repo.HasSingletonManaged<Fdp.Modules.Geographic.IGeographicTransform>()
                        ? _repo.GetSingletonManaged<Fdp.Modules.Geographic.IGeographicTransform>()
                        : null);
        if (geo is null) return new GeoPoint(world.X, world.Y, 0);
        var (lat, lon, alt) = geo.ToGeodetic(new System.Numerics.Vector3(world.X, world.Y, 0f));
        return new GeoPoint(lat, lon, alt);
    }

    /// <inheritdoc/>
    /// <remarks>Runs the host's area gizmo when it supplied one; otherwise answers empty at once.</remarks>
    public Task<IReadOnlyList<int>> PickAreaEntitiesAsync(
        string[]? filterPresets = null, CancellationToken ct = default)
        => _areaGizmo is null
            ? Task.FromResult<IReadOnlyList<int>>(Array.Empty<int>())
            : _pickers.RunPickAsync<IReadOnlyList<int>>(
                Hrot.ScenarioEditor.Tools.ScenarioToolIds.PickArea,
                (tcs, remove) => _areaGizmo(list => tcs.TrySetResult(list), remove),
                ct);

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
