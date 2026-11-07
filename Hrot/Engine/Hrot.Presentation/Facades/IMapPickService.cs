using Fdp.Toolkit.Replication;
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
    /// <summary>⭐ <c>CE-4206</c> R3 (F8) — this service can centre the map (a host with a canvas). Default false.</summary>
    bool CanShowOnMap => false;

    /// <summary>⭐ <c>CE-4206</c> R3 (F8) — centres the map on a network-id entity. Default: nothing.</summary>
    void ShowOnMap(Fdp.Toolkit.Replication.EntityRef entity) { }

    /// <summary>⭐ <c>CE-4206</c> R3 (F8) — centres the map on a local-metres point. Default: nothing.</summary>
    void ShowOnMap(Fdp.Core.WorldPoint point) { }

    /// <summary>
    /// ⭐ <c>CE-4206</c> R2 (DESIGN_Map_Picking_Unification §6.3 F6) — edits an <see cref="Fdp.Toolkit.Replication.AreaLink"/> SHAPE (circle / rectangle) on the map with
    /// handles, starting from <paramref name="start"/> (a non-shape start = place a new circle with the first click). Completes with the edited shape on Enter / right-click;
    /// the task is CANCELLED on Esc. The DEFAULT answers <paramref name="start"/> unchanged at once — a service that cannot edit on a map.
    /// </summary>
    Task<Fdp.Toolkit.Replication.AreaLink> EditAreaAsync(Fdp.Toolkit.Replication.AreaLink start, CancellationToken ct = default)
        => Task.FromResult(start);

    /// <summary>
    /// ⭐ <c>CE-4206</c> R1 — the live reference scope over the repository this service picks in (names, GUIDs, keys for the reference leaves);
    /// <see langword="null"/> for a service with no repository. <c>MapPickBroker.References</c> reads it.
    /// </summary>
    Fdp.Toolkit.Scenario.LiveRefScope? References => null;

    /// <summary>⭐ U-10 (DESIGN_Show_On_Map §2) — the preview service of this service's map (one per map, a canvas resource); null without one.</summary>
    Fdp.Presentation.Editing.MapPreview? Preview => null;

    Task<Hrot.Core.Mission.GeoPoint> PickLocationAsync(CancellationToken ct = default);

    /// <summary>
    /// ⭐ <c>S0</c> item 5 (<c>DESIGN_Scenario_Rules</c> §9.8.0, SR-135) — the same pick answered in LOCAL METRES (the engine's only frame; a
    /// params field's <c>PointLink</c> is written from it, with no geodetic round trip). A map host overrides it with the clicked world point;
    /// the DEFAULT derives it from <see cref="PickLocationAsync"/> exactly as that adapter's no-transform fallback does (X = longitude field,
    /// Y = latitude field), so a service that only knows geodetic picks keeps working.
    /// </summary>
    Task<Fdp.Core.WorldPoint> PickLocalAsync(CancellationToken ct = default)
    {
        // ⛔ not an `async` method: a poller reads IsCompleted the moment the pick completes, and an `await` continuation only runs
        //   inline when no SynchronizationContext is current (a test host installs one) — ExecuteSynchronously always does.
        var result = new TaskCompletionSource<Fdp.Core.WorldPoint>();
        PickLocationAsync(ct).ContinueWith(t =>
        {
            if (t.IsCanceled) result.TrySetCanceled();
            else if (t.IsFaulted) result.TrySetException(t.Exception!.InnerExceptions);
            else result.TrySetResult(new Fdp.Core.WorldPoint((float)t.Result.Longitude, (float)t.Result.Latitude, 0f));
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return result.Task;
    }

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

    /// <summary>⭐ U-25 / S4 (DESIGN_Initial_State §3) — the network id of a named <b>Point</b> (8816) under <paramref name="worldXY"/>, or -1: a location pick
    /// that lands on one stores the point's reference (<c>PointLink</c> with its GUID — resolved once, SR-130), not bare coordinates.</summary>
    int NamedPointAt(System.Numerics.Vector2 worldXY) => -1;
}
