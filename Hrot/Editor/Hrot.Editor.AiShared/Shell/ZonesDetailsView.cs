using System;
using System.Collections.Generic;
using Hrot.Editor.AiShared.Selection;
using Hrot.Presentation.Zones;

namespace Hrot.Editor.AiShared.Shell;

/// <summary>
/// ⭐⭐⭐ <c>E3</c> — the ZONES view: one row per terrain zone, its LOCAL load state, a per-row Load and a
/// "Load all stale" header action.
///
/// <para>⭐⭐ <b>A registered <c>DetailsView</c>, NOT a panel</b> — design §9.5, measured: the details
/// shell already exists and is actively used ("one window, N views, chosen by a predicate"), so a zones
/// PANEL would be a second shell for the same job. ⇒ this contributes to the shell that is there.</para>
///
/// <para>⛔⛔ <b>Progress comes from <see cref="ZoneLoadProgressTracker"/>, never from
/// <c>HasInFlightTransaction</c></b> — design §9.4 measured that property answering <c>false</c> while
/// rounds are genuinely pending, so a spinner sourced from it would read "idle" throughout every load.
/// That defect is pre-existing and explicitly out of scope to fix.</para>
///
/// <para>⭐ <b>Every action here is CLUSTER-WIDE and never gated on local freshness</b> (§9.6, §9.7 ③b) —
/// including "Load all stale", which is computed from local rows but issues one op per zone regardless
/// of what any single node's marker says. ⚠ §9.3's unbounded-fan-out question is answered at the
/// REQUESTER: the cap, if one is ever wanted, belongs here, not in the master.</para>
///
/// <para>⚠ <b>The draw is deliberately thin.</b> The interesting logic — the state per row, including
/// derived staleness — lives in <see cref="ZoneStatusReader"/>, which makes no ImGui calls and is
/// therefore assertable without a render frame. Same split, same reason, as
/// <c>SharedContextMenuPopulator</c>.</para>
///
/// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §9.5, §9.4, §9.6, §9.3, §9.1.
/// </summary>
public sealed class ZonesDetailsView : IDetailsViewInstance
{
    private readonly Func<IReadOnlyList<ZoneStatusRow>> _rows;
    private readonly Action<string>                     _loadZone;
    private readonly IZoneRowRenderer                   _renderer;

    /// <param name="rows">This frame's zone rows — re-read every frame, so the list follows the world.</param>
    /// <param name="loadZone">
    ///   Publishes the CLUSTER-wide zone-load op for one zone id (§9.6). ⛔ Not a local load; there is no
    ///   local-only zone load on any host.
    /// </param>
    /// <param name="renderer">
    ///   ⭐ The ImGui half, behind a seam so the view's BEHAVIOUR (which rows, which actions, what "load
    ///   all stale" means) can be asserted without a render frame. ⚠ Not indirection for its own sake:
    ///   this is the one thing in the view that cannot be tested otherwise.
    /// </param>
    public ZonesDetailsView(
        Func<IReadOnlyList<ZoneStatusRow>> rows,
        Action<string> loadZone,
        IZoneRowRenderer renderer)
    {
        _rows     = rows     ?? throw new ArgumentNullException(nameof(rows));
        _loadZone = loadZone ?? throw new ArgumentNullException(nameof(loadZone));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    }

    /// <inheritdoc/>
    public void Draw(DetailsContext context, string idScope)
    {
        var rows = _rows() ?? Array.Empty<ZoneStatusRow>();

        _renderer.Header(idScope, rows, ZoneStatusReader.AnyStale(rows), LoadAllStale);

        foreach (var row in rows)
            _renderer.Row(idScope, row, () => _loadZone(row.ZoneId));
    }

    /// <summary>
    /// ⭐ "Load all stale" — ONE OP PER ZONE (§9.3), not a batch op.
    /// <para>⛔ A multi-zone payload was rejected by design: independent failure, independent progress
    /// and a natural retry granularity all fall out of one-op-per-zone, and the node side stays free to
    /// serialise the builds as a LOCAL policy the protocol never sees.</para>
    /// </summary>
    private void LoadAllStale()
    {
        foreach (var row in _rows() ?? Array.Empty<ZoneStatusRow>())
        {
            if (row.State is ZoneLoadState.Stale or ZoneLoadState.NotLoaded or ZoneLoadState.Failed)
                _loadZone(row.ZoneId);
        }
    }

    /// <inheritdoc/>
    public void Dispose() { }
}

/// <summary>
/// The ImGui half of <see cref="ZonesDetailsView"/>, behind a seam so the view's behaviour is testable.
/// </summary>
public interface IZoneRowRenderer
{
    /// <summary>Draws the header: counts, plus a "Load all stale" action enabled only when there is any.</summary>
    void Header(string idScope, IReadOnlyList<ZoneStatusRow> rows, bool anyStale, Action loadAllStale);

    /// <summary>Draws one zone row: id, state, and a per-row Load that is ALWAYS enabled (§9.7 ③b).</summary>
    void Row(string idScope, ZoneStatusRow row, Action load);
}

/// <summary>Builds the <see cref="ZonesDetailsView"/> descriptor.</summary>
public static class ZonesDetailsViewDescriptor
{
    /// <summary>Stable id — the layout key and the designer's remembered pick.</summary>
    public const string ViewId = "details.zones";

    /// <summary>
    /// ⚠ Below the entity-shaped views on purpose: when an ENTITY is selected the operator is asking
    /// about that entity, not about the zone list. The zones view's own predicate already restricts it
    /// to the map-background focus, so the rank only decides ties.
    /// </summary>
    public const int Rank = 15;

    public static DetailsViewDescriptor For(
        Func<IReadOnlyList<ZoneStatusRow>> rows,
        Action<string> loadZone,
        IZoneRowRenderer renderer)
        => new DetailsViewDescriptor(
            Id:        ViewId,
            Title:     "Zones",
            Rank:      Rank,
            AppliesTo: Applies,
            Create:    () => new ZonesDetailsView(rows, loadZone, renderer));

    /// <summary>
    /// ⭐⭐⭐ <c>U5</c>'s answer in one line: the view claims the panel when the MAP BACKGROUND is the
    /// focused surface.
    ///
    /// <para>⭐ Extracted so a rail can assert the predicate directly, the same way
    /// <c>VariablesDetailsViewDescriptor.Applies</c> is. 📐 The design recorded <c>U5</c> as needing a
    /// new details CONTEXT; measured, it needed one enum member — the focus latch, the context field and
    /// the predicate helper were all already there.</para>
    /// </summary>
    public static bool Applies(DetailsContext context)
        => DetailsViewPredicates.FocusIs(context, SelectionOrigin.MapBackground);
}
