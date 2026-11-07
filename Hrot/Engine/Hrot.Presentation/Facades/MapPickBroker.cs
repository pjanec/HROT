using Fdp.Presentation.Editing;
using Fdp.Toolkit.Replication;

namespace Hrot.Presentation.Facades;

/// <summary>
/// ⭐⭐ THE per-frame pick broker: adapts the async <see cref="Hrot.UI.Common.Facades.IMapPickService"/> to the
/// synchronous polling contract of <see cref="IMapPickContext"/> — one pick in flight, keyed by the field's
/// path, its result handed to whoever asks for that path. 📄 <c>docs/blueprints/DESIGN_Map_Picking_Unification.md</c> P4.
///
/// <para>Used by the component editor (path = the edit node's <c>JsonPath</c>, via
/// <c>ComponentReflector.EditPickerContext</c>) and by the mission panel (path = <c>$.tasks[i].Prop</c>, which owns one
/// over its per-frame service). ⛔ The mission panel's own copy of this state machine — and the
/// <c>IPickInteractionContext</c> it implemented — were deleted.</para>
/// </summary>
public sealed class MapPickBroker : IMapPickContext
{
    private readonly Func<Hrot.UI.Common.Facades.IMapPickService?> _pickService;

    private string? _pendingPath;
    private Task<int>? _entityPickTask;
    private Task<Fdp.Core.WorldPoint>? _locationPickTask;
    private Task<AreaLink>? _areaEditTask;   // ⭐ CE-4206 R2

    /// <summary>A broker over a fixed pick service.</summary>
    public MapPickBroker(Hrot.UI.Common.Facades.IMapPickService pickService)
    {
        if (pickService == null) throw new ArgumentNullException(nameof(pickService));
        _pickService = () => pickService;
    }

    /// <summary>A broker over a service read at REQUEST time — for a host that receives it per frame (the mission
    /// panel's <c>DrawContent(service, pick)</c>). A request with no service available is ignored.</summary>
    public MapPickBroker(Func<Hrot.UI.Common.Facades.IMapPickService?> pickService)
        => _pickService = pickService ?? throw new ArgumentNullException(nameof(pickService));

    /// <summary>⭐ <c>CE-4206</c> R1 (F2) — the live reference scope the reference leaves read: set explicitly, else the pick service's
    /// (<see cref="Hrot.UI.Common.Facades.IMapPickService.References"/> — the canvas adapter builds one over the repository it holds).</summary>
    public Fdp.Toolkit.Scenario.LiveRefScope? References { get => _references ?? _pickService()?.References; set => _references = value; }
    private Fdp.Toolkit.Scenario.LiveRefScope? _references;

    /// <summary><c>true</c> while an entity pick is in flight (for any path).</summary>
    public bool IsEntityPickPending => _entityPickTask is { IsCompleted: false };

    /// <summary><c>true</c> while a location pick is in flight (for any path).</summary>
    public bool IsLocationPickPending => _locationPickTask is { IsCompleted: false };

    /// <summary>
    /// ⭐ <c>CE-1059</c> — the optional entity LIST: opens a searchable list narrowed by the presets and calls back with the
    /// chosen entity's network id (<see cref="Hrot.Presentation.Orders.EntityListSource.Opener"/>); answers <c>false</c> when it
    /// cannot open now. Null ⇒ no <c>List…</c> button. 📄 <c>docs/DESIGN_Orders_Params_Entity_Classes.md</c> §13.12 O3.
    /// </summary>
    public Func<string[]?, Action<int>, bool>? EntityList { get; set; }

    /// <inheritdoc/>
    public bool CanListEntities => EntityList != null;

    /// <inheritdoc/>
    /// <remarks>Nothing is pending while the list is open (the list has no cancel callback, so a pending state could hang);
    /// the choice lands as a COMPLETED entity pick for <paramref name="jsonPath"/>, consumed like a map pick.</remarks>
    public void RequestEntityListPick(string jsonPath, string[]? filterPresets)
    {
        var open = EntityList;
        if (open == null) return;
        open(filterPresets, networkId =>
        {
            _pendingPath      = jsonPath;
            _entityPickTask   = Task.FromResult(networkId);
            _locationPickTask = null;
        });
    }

    /// <inheritdoc/>
    public bool IsPickPendingFor(string jsonPath)
        => _pendingPath == jsonPath && (IsEntityPickPending || IsLocationPickPending || _areaEditTask is { IsCompleted: false });

    /// <inheritdoc/>
    public bool CanShowOnMap => _pickService()?.CanShowOnMap ?? false;

    private Fdp.Presentation.Editing.MapPreview? _preview;
    /// <summary>⭐ U-10 (DESIGN_Show_On_Map §2) — the map's preview service: the pick service's (its canvas'), or one set here (a rail).</summary>
    public Fdp.Presentation.Editing.MapPreview? Preview { get => _preview ?? _pickService()?.Preview; set => _preview = value; }

    /// <inheritdoc/>
    public void ShowOnMap(EntityRef entity) => _pickService()?.ShowOnMap(entity);

    /// <inheritdoc/>
    public void ShowOnMap(Fdp.Core.WorldPoint point) => _pickService()?.ShowOnMap(point);

    /// <inheritdoc/>
    public void RequestAreaEdit(string jsonPath, AreaLink start)
    {
        var service = _pickService();
        if (service == null) return;
        _pendingPath      = jsonPath;
        _areaEditTask     = service.EditAreaAsync(start);
        _entityPickTask   = null;
        _locationPickTask = null;
    }

    /// <inheritdoc/>
    public bool TryConsumeAreaEdit(string jsonPath, out AreaLink area)
    {
        area = default;
        if (_pendingPath != jsonPath || _areaEditTask is not { IsCompleted: true } task) return false;

        _pendingPath  = null;
        _areaEditTask = null;
        if (!task.IsCompletedSuccessfully) return false;   // cancelled (Esc) or faulted
        area = task.Result;
        return true;
    }

    /// <inheritdoc/>
    public void RequestEntityPick(string jsonPath, string[]? filterPresets)
    {
        var service = _pickService();
        if (service == null) return;
        _pendingPath      = jsonPath;
        _entityPickTask   = service.PickEntityAsync(filterPresets);
        _locationPickTask = null;
        _areaEditTask     = null;
    }

    /// <inheritdoc/>
    public void RequestLocationPick(string jsonPath)
    {
        var service = _pickService();
        if (service == null) return;
        _pendingPath      = jsonPath;
        _locationPickTask = service.PickLocalAsync();   // ⭐ S0 item 5: local metres, written as a PointLink
        _entityPickTask   = null;
        _areaEditTask     = null;
    }

    /// <inheritdoc/>
    /// <remarks>⭐ The picked entity's NETWORK id as an <see cref="EntityRef"/> — the form an authored field stores
    /// (<c>DESIGN_Entity_Reference.md</c> D5); a pick of an entity with no identity yields nothing.</remarks>
    public bool TryConsumeEntityPick(string jsonPath, out EntityRef picked)
    {
        picked = EntityRef.None;
        if (_pendingPath != jsonPath || _entityPickTask is not { IsCompleted: true } task) return false;

        _pendingPath    = null;
        _entityPickTask = null;
        if (!task.IsCompletedSuccessfully || task.Result <= 0) return false;   // cancelled, faulted, or no identity
        picked = new EntityRef(task.Result);
        return true;
    }

    /// <inheritdoc/>
    /// <summary>⭐ U-25 / S4 — a location pick that lands on a named Point stores the POINT (its position and its GUID, the chip's exact form — resolved once,
    /// SR-130); anywhere else, the coordinates as before. ⭐ Here, in the one broker every host's pickers go through, so every point picker offers named points.</summary>
    internal PointLink SnapToNamedPoint(Fdp.Core.WorldPoint at)
    {
        if (_pickService() is not { } svc || References is not { } refs) return new PointLink(at);
        int net = svc.NamedPointAt(new System.Numerics.Vector2(at.X, at.Y));
        if (net <= 0) return new PointLink(at);
        var point = new EntityRef(net);
        if (!refs.TryGuidOf(point, out var guid)) return new PointLink(at);
        var where = refs.TryPositionOf(point, out var p) ? p : at;
        return new PointLink(where, Fdp.Core.RefSpec.ByExact(guid));
    }

    public bool TryConsumeLocationPick(string jsonPath, out PointLink location)
    {
        location = default;
        if (_pendingPath != jsonPath || _locationPickTask is not { IsCompleted: true } task) return false;

        _pendingPath      = null;
        _locationPickTask = null;
        if (!task.IsCompletedSuccessfully) return false;
        location = SnapToNamedPoint(task.Result);
        return true;
    }
}
