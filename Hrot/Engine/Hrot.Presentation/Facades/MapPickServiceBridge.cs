using Fdp.Presentation.Editing;
using Fdp.Toolkit.Behavior.Params;
using Fdp.Toolkit.Replication;

namespace Hrot.Presentation.Facades;

/// <summary>
/// ⭐⭐ THE per-frame pick broker: adapts the async <see cref="Hrot.UI.Common.Facades.IMapPickService"/> to the
/// synchronous polling contract of <see cref="IComponentPickerContext"/> — one pick in flight, keyed by the field's
/// path, its result handed to whoever asks for that path. 📄 <c>docs/blueprints/DESIGN_Map_Picking_Unification.md</c> P4.
///
/// <para>Used by the component editor (path = the edit node's <c>JsonPath</c>, via
/// <c>ComponentReflector.EditPickerContext</c>) and by the mission panel (path = <c>$.tasks[i].Prop</c>, which owns one
/// over its per-frame service). ⛔ The mission panel's own copy of this state machine — and the
/// <c>IPickInteractionContext</c> it implemented — were deleted.</para>
/// </summary>
public sealed class MapPickServiceBridge : IComponentPickerContext
{
    private readonly Func<Hrot.UI.Common.Facades.IMapPickService?> _pickService;

    private string? _pendingPath;
    private Task<int>? _entityPickTask;
    private Task<Hrot.Core.Mission.GeoPoint>? _locationPickTask;

    /// <summary>A broker over a fixed pick service.</summary>
    public MapPickServiceBridge(Hrot.UI.Common.Facades.IMapPickService pickService)
    {
        if (pickService == null) throw new ArgumentNullException(nameof(pickService));
        _pickService = () => pickService;
    }

    /// <summary>A broker over a service read at REQUEST time — for a host that receives it per frame (the mission
    /// panel's <c>DrawContent(service, pick)</c>). A request with no service available is ignored.</summary>
    public MapPickServiceBridge(Func<Hrot.UI.Common.Facades.IMapPickService?> pickService)
        => _pickService = pickService ?? throw new ArgumentNullException(nameof(pickService));

    /// <summary><c>true</c> while an entity pick is in flight (for any path).</summary>
    public bool IsEntityPickPending => _entityPickTask is { IsCompleted: false };

    /// <summary><c>true</c> while a location pick is in flight (for any path).</summary>
    public bool IsLocationPickPending => _locationPickTask is { IsCompleted: false };

    /// <inheritdoc/>
    public bool IsPickPendingFor(string jsonPath)
        => _pendingPath == jsonPath && (IsEntityPickPending || IsLocationPickPending);

    /// <inheritdoc/>
    public void RequestEntityPick(string jsonPath, string[]? filterPresets)
    {
        var service = _pickService();
        if (service == null) return;
        _pendingPath      = jsonPath;
        _entityPickTask   = service.PickEntityAsync(filterPresets);
        _locationPickTask = null;
    }

    /// <inheritdoc/>
    public void RequestLocationPick(string jsonPath)
    {
        var service = _pickService();
        if (service == null) return;
        _pendingPath      = jsonPath;
        _locationPickTask = service.PickLocationAsync();
        _entityPickTask   = null;
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
    public bool TryConsumeLocationPick(string jsonPath, out PickableGeoPoint location)
    {
        location = default;
        if (_pendingPath != jsonPath || _locationPickTask is not { IsCompleted: true } task) return false;

        _pendingPath      = null;
        _locationPickTask = null;
        if (!task.IsCompletedSuccessfully) return false;
        location = new PickableGeoPoint(task.Result.Latitude, task.Result.Longitude);
        return true;
    }
}
