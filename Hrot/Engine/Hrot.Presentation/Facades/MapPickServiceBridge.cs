using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.Replication.Components;

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
    private readonly EntityRepository? _repo;

    private string? _pendingPath;
    private Task<int>? _entityPickTask;
    private Task<Hrot.Core.Mission.GeoPoint>? _locationPickTask;

    // Cached live query over entities that carry a NetworkIdentity component.
    private EntityQuery? _networkQuery;

    /// <summary>
    /// Creates a <see cref="MapPickServiceBridge"/>.
    /// </summary>
    /// <param name="pickService">The map pick service to delegate to.</param>
    /// <param name="repo">
    /// Optional entity repository used to reverse-look up an entity from a
    /// network ID after an entity pick completes.
    /// When <see langword="null"/>, <see cref="TryConsumeEntityPick"/> always
    /// returns <see langword="false"/>.
    /// </param>
    public MapPickServiceBridge(
        Hrot.UI.Common.Facades.IMapPickService pickService,
        EntityRepository? repo = null)
    {
        _pickService = pickService ?? throw new ArgumentNullException(nameof(pickService));
        _repo        = repo;
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
    public bool TryConsumeEntityPick(string jsonPath, out Entity pickedEntity)
    {
        if (_pendingPath == jsonPath && _entityPickTask != null)
        {
            if (_entityPickTask.IsCompletedSuccessfully)
            {
                int networkId   = _entityPickTask.Result;
                _pendingPath    = null;
                _entityPickTask = null;
                pickedEntity    = FindEntityByNetworkId(networkId);
                return pickedEntity != Entity.Null;
            }

            // Task cancelled or faulted — clear pending state silently.
            if (_entityPickTask.IsCompleted)
            {
                _pendingPath    = null;
                _entityPickTask = null;
            }
        }

        pickedEntity = Entity.Null;
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

    // ── Entity reverse-lookup ─────────────────────────────────────────────────

    private Entity FindEntityByNetworkId(long networkId)
    {
        if (_repo == null || networkId <= 0) return Entity.Null;

        _networkQuery ??= _repo.Query().With<NetworkIdentity>().Build();

        foreach (var e in _networkQuery)
        {
            if (_repo.GetComponentRO<NetworkIdentity>(e).Value == networkId)
                return e;
        }

        return Entity.Null;
    }
}
