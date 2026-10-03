using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.Replication;

namespace Hrot.Presentation.Facades;

/// <summary>
/// Adapts the async <see cref="Hrot.UI.Common.Facades.IMapPickService"/> to the
/// synchronous per-frame polling contract required by <see cref="IComponentPickerContext"/>.
///
/// <para>Instantiate once per subsystem and assign to
/// <c>ComponentReflector.EditPickerContext</c> after registering windows.</para>
/// </summary>
public sealed class MapPickServiceBridge : IComponentPickerContext
{
    private readonly Hrot.UI.Common.Facades.IMapPickService _pickService;

    private string? _pendingPath;
    private Task<int>? _entityPickTask;
    private Task<Hrot.Core.Mission.GeoPoint>? _locationPickTask;

    /// <summary>Creates a <see cref="MapPickServiceBridge"/> over <paramref name="pickService"/>.</summary>
    /// <remarks>⭐ <c>DESIGN_Entity_Reference.md</c> D5 — a pick yields the picked entity's NETWORK id as an
    /// <see cref="EntityRef"/>, the form an authored field stores, so the bridge needs no repository: the old reverse
    /// lookup to a local <see cref="Entity"/> fed only the deleted Fdp.Presentation attribute, which had no production
    /// user.</remarks>
    public MapPickServiceBridge(Hrot.UI.Common.Facades.IMapPickService pickService)
    {
        _pickService = pickService ?? throw new ArgumentNullException(nameof(pickService));
    }

    /// <inheritdoc/>
    public bool IsPickPendingFor(string jsonPath)
        => _pendingPath == jsonPath
           && (_entityPickTask   is { IsCompleted: false }
               || _locationPickTask is { IsCompleted: false });

    /// <inheritdoc/>
    public void RequestEntityPick(string jsonPath, string[]? filterPresets)
    {
        _pendingPath      = jsonPath;
        _entityPickTask   = _pickService.PickEntityAsync(filterPresets);
        _locationPickTask = null;
    }

    /// <inheritdoc/>
    public void RequestLocationPick(string jsonPath)
    {
        _pendingPath      = jsonPath;
        _locationPickTask = _pickService.PickLocationAsync();
        _entityPickTask   = null;
    }

    /// <inheritdoc/>
    public bool TryConsumeEntityPick(string jsonPath, out EntityRef picked)
    {
        if (_pendingPath == jsonPath && _entityPickTask != null)
        {
            if (_entityPickTask.IsCompletedSuccessfully)
            {
                int networkId   = _entityPickTask.Result;
                _pendingPath    = null;
                _entityPickTask = null;
                picked          = networkId > 0 ? new EntityRef(networkId) : EntityRef.None;   // 0 / negative = no identity
                return !picked.IsNone;
            }

            // Task cancelled or faulted — clear pending state silently.
            if (_entityPickTask.IsCompleted)
            {
                _pendingPath    = null;
                _entityPickTask = null;
            }
        }

        picked = EntityRef.None;
        return false;
    }

    /// <inheritdoc/>
    public bool TryConsumeLocationPick(string jsonPath, out Vector3 location)
    {
        if (_pendingPath == jsonPath && _locationPickTask != null)
        {
            if (_locationPickTask.IsCompletedSuccessfully)
            {
                var gp            = _locationPickTask.Result;
                _pendingPath      = null;
                _locationPickTask = null;
                location = new Vector3((float)gp.Latitude, (float)gp.Longitude, (float)gp.Altitude);
                return true;
            }

            // Task cancelled or faulted — clear pending state silently.
            if (_locationPickTask.IsCompleted)
            {
                _pendingPath      = null;
                _locationPickTask = null;
            }
        }

        location = default;
        return false;
    }
}
