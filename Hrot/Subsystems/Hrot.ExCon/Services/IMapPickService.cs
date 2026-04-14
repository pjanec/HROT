using Hrot.Core.Mission;

namespace Hrot.ExCon.Services;

/// <summary>
/// Asynchronous map-interaction picking service.
///
/// <para>
/// Provides a clean async/await API over the DDS <c>MapCommandRequest</c> /
/// <c>MapClickEvent</c> round-trip, allowing any ExCon panel to request a
/// user-supplied location or entity from the IG without blocking the UI thread.
/// </para>
///
/// <para><b>Usage pattern (MissionPanel example):</b>
/// <code>
/// _pendingLocationPick = logic.MapPickService.PickLocationAsync();
/// // ... next frame(s) ...
/// if (_pendingLocationPick.IsCompleted)
/// {
///     var pos = _pendingLocationPick.Result;
///     HandleEditBehaviorParams(index, BuildMoveToJson(pos));
///     _pendingLocationPick = null;
/// }
/// </code>
/// </para>
/// </summary>
public interface IMapPickService
{
    /// <summary>
    /// Requests a map-side location pick from the operator.
    ///
    /// <para>
    /// Sends a <c>CMD_PICK_LOCATION</c> command to the target IG and returns a
    /// <see cref="Task{GeoPoint}"/> that resolves when the operator clicks the map
    /// (or is cancelled via <paramref name="ct"/>).
    /// </para>
    /// </summary>
    /// <param name="ct">
    /// Optional cancellation token. When cancelled the returned task transitions
    /// to a <see cref="TaskCanceledException"/> fault.
    /// </param>
    Task<GeoPoint> PickLocationAsync(CancellationToken ct = default);

    /// <summary>
    /// Requests an entity pick from the operator, filtered to entities whose
    /// <c>MapDisplayComponent.LayerMask</c> matches one or more of
    /// <paramref name="filterPresets"/>.
    ///
    /// <para>
    /// Sends a <c>CMD_PICK_ENTITY</c> command to the target IG and returns a
    /// <see cref="Task{Int32}"/> that resolves with the network entity ID when the
    /// operator clicks a valid entity (or is cancelled via <paramref name="ct"/>).
    /// </para>
    /// </summary>
    /// <param name="filterPresets">
    /// Domain-specific layer preset names forwarded verbatim to the IG
    /// (e.g. <c>["road_graphs"]</c>). Null is treated as empty (match all).
    /// </param>
    Task<int> PickEntityAsync(string[]? filterPresets = null, CancellationToken ct = default);
}