using Hrot.Core.Mission;

namespace Hrot.UI.Common.Facades;

/// <summary>
/// Port interface for asynchronous map-interaction picking operations.
/// Provides a clean async/await API over user-driven location, entity, and area picks,
/// independent of the underlying transport or IG communication mechanism.
/// </summary>
public interface IMapPickService
{
    /// <summary>
    /// Requests a map-side location pick from the operator.
    /// Returns a <see cref="Task{GeoPoint}"/> that resolves when the operator clicks the map.
    /// </summary>
    /// <param name="ct">
    /// Optional cancellation token. When cancelled the returned task transitions
    /// to a <see cref="TaskCanceledException"/> fault.
    /// </param>
    Task<Hrot.Core.Mission.GeoPoint> PickLocationAsync(CancellationToken ct = default);

    /// <summary>
    /// Requests a single entity pick from the operator, optionally filtered by layer preset names.
    /// Returns a <see cref="Task{Int32}"/> that resolves with the network entity ID when the
    /// operator clicks a valid entity on the map.
    /// </summary>
    /// <param name="filterPresets">
    /// Domain-specific layer preset names (e.g. <c>["road_graphs"]</c>).
    /// <c>null</c> is treated as empty (match all visible entities).
    /// </param>
    /// <param name="ct">Optional cancellation token.</param>
    Task<int> PickEntityAsync(string[]? filterPresets = null, CancellationToken ct = default);

    /// <summary>
    /// Requests an area entity pick from the operator, returning all entities within the
    /// operator-drawn selection rectangle, optionally filtered by layer preset names.
    /// </summary>
    /// <param name="filterPresets">
    /// Domain-specific layer preset names. <c>null</c> matches all visible entities.
    /// </param>
    /// <param name="ct">Optional cancellation token.</param>
    Task<IReadOnlyList<int>> PickAreaEntitiesAsync(string[]? filterPresets = null, CancellationToken ct = default);

    /// <summary>
    /// ⭐ <c>CE-3171</c> (DESIGN_Entity_Control §8a) — the network id of the topmost entity drawn at world point
    /// <paramref name="worldXY"/> NOW, with no pick session: the hit test a click-driven tool (the RTS command tool) needs.
    /// -1 when nothing is there, or when this service has no map to test against (the default).
    /// </summary>
    int EntityAt(System.Numerics.Vector2 worldXY) => -1;
}
