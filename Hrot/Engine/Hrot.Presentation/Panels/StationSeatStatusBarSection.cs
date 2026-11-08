using System;
using Fdp.Core;
using Fdp.Presentation.WindowManager;
using Hrot.Common.Editing;
using Hrot.Common.Infrastructure;
using Hrot.ScenarioEditor.Map;
using ImGuiNET;

namespace Hrot.UI.Common.Panels;

/// <summary>
/// ⭐⭐ <c>CE-4329</c> (R-4170, 📄 docs/UX/DESIGN_Seats_And_Hats.md §R-4170) — <b>the station's seat · hat, at the status bar's RIGHT end</b>: <i>"Author · Observe"</i>, and a click opens the seat and hat
/// menu. 🔒 User, <c>2026-11-08</c>: <i>"…putting some text in between the armed mode and the play button on the main toolbar, which I don't like, and I think the text which makes clear what
/// role and what seat is active needs to go to the status bar"</i> — ⛔ SUPERSEDED: H3's text beside the run-mode indicator. The toolbar's indicator is ONE glyph button next to ▶ again (AQ108 D29).
/// <para>⭐ Composed ONCE per process by <c>LocalWindowController.ComposeRunControls</c>, over the run-controls owner's world — so every host that has a seat shows it, by construction.
/// ⛔ The bar's LEFT end stays free (the Review source segment, CE-3419). ⭐ Hot path: the line and the menu rows are rebuilt only when the seat or the hat changes (0 B per steady frame).</para>
/// </summary>
public sealed class StationSeatStatusBarSection
{
    /// <summary>The status-bar section id (a duplicate registration replaces, never doubles).</summary>
    public const string SectionId = "station_seat";

    /// <summary>After everything else (the message-log icon is 90): the right end.</summary>
    public const int SortOrder = 200;

    /// <summary>The popup's ImGui id.</summary>
    public const string PopupId = "##station_seat_menu";

    /// <summary>The tooltip: what the text is and that it opens the menu.</summary>
    public const string Tooltip = "This station's seat · what you are doing now (the hat). Click for the seat and hat menu.";

    private readonly Func<EntityRepository?> _world;
    private Seat _seat;
    private Hat _hat;
    private string? _line;
    private Row[] _rows = Array.Empty<Row>();
    private bool _openRequested;

    /// <summary>One row of the menu: a section header, or a hat / seat — checked when it is the current one, greyed with <see cref="Why"/> when it cannot be chosen.</summary>
    public readonly record struct Row(string Label, bool Header, bool Checked, string? Why);

    /// <param name="world">The station's world (the run-controls owner's); null or one with no map ⇒ no seat · hat to show.</param>
    public StationSeatStatusBarSection(Func<EntityRepository?> world) => _world = world ?? throw new ArgumentNullException(nameof(world));

    /// <summary>Registers the section on <paramref name="bar"/> for EVERY perspective (the seat is the station's, not a perspective's).</summary>
    public static StationSeatStatusBarSection Register(StatusBarManager bar, Func<EntityRepository?> world)
    {
        var section = new StationSeatStatusBarSection(world);
        bar.RegisterSection(SectionId, SortOrder, section.Render);
        return section;
    }

    /// <summary>⭐ A seat as the status bar words it.</summary>
    public static string SeatWord(Seat seat) => seat switch
    {
        Seat.SideController => "Side controller",
        _ => seat.ToString(),
    };

    /// <summary>The seat · hat line of the station's world, or null (no world / no map); rebuilt only when either changes (the same string otherwise).</summary>
    public string? Line()
    {
        if (_world() is not { } w || HatArbiter.Of(w) is not { } hats) return null;
        var seat = StationMode.SeatOf(w);
        var hat = hats.Active;
        if (_line == null || seat != _seat || hat != _hat)
        {
            _seat = seat; _hat = hat;
            _line = SeatWord(seat) + " · " + HatArbiter.WordOf(hat);
            _rows = MenuRows(seat, hat);
        }
        return _line;
    }

    /// <summary>The menu's rows now (as of the last <see cref="Line"/>).</summary>
    public Row[] Rows => _rows;

    /// <summary>Whether the last click asked for the menu (a rail reads it; <see cref="Render"/> consumes it).</summary>
    public bool OpenRequested => _openRequested;

    /// <summary>Asks for the menu (the click's consequence, railable without a mouse).</summary>
    public void Open() => _openRequested = true;

    /// <summary>The words a hat is listed by.</summary>
    public static string HatLabel(Hat hat) => hat == Hat.Embodied ? "Embodied — possess a unit" : HatArbiter.WordOf(hat);

    private const string DerivedWhy = "the hat follows what you do (the run mode, the Command tool, possessing) — it is not picked by hand";
    private const string SeatWhy = "the seat is chosen at launch: --feature station.seat";

    /// <summary>
    /// ⭐ The menu: <i>Hat — one at a time</i> (the five hats; the current one checked; a hat the SEAT does not allow is greyed WITH the reason, <see cref="SeatHats.Refusal"/>, never hidden — R-4119),
    /// then <i>Seat</i> (the six seats; the current one checked). ⚠ A hat is DERIVED (R-4164) and a seat is chosen at launch (<c>--feature station.seat</c>), so every row but the current ones is greyed
    /// with that reason — the mockup's one-click switching is not built (Seats and hats §3.3 "⏭ not here").
    /// </summary>
    public static Row[] MenuRows(Seat seat, Hat hat)
    {
        var rows = new System.Collections.Generic.List<Row> { new("Hat — one at a time", true, false, null) };
        foreach (var h in new[] { Hat.Edit, Hat.Command, Hat.Embodied, Hat.Observe, Hat.Direct })
            rows.Add(new Row(HatLabel(h), false, h == hat, h == hat ? null : SeatHats.Refusal(seat, h) ?? DerivedWhy));
        rows.Add(new Row("Seat", true, false, null));
        foreach (var s in new[] { Seat.Author, Seat.Instructor, Seat.SideController, Seat.Player, Seat.Observer, Seat.Reviewer })
            rows.Add(new Row(SeatWord(s), false, s == seat, s == seat ? null : SeatWhy));
        return rows.ToArray();
    }

    /// <summary>Whether the menu was drawn on the last frame (a rail's observation of the click's consequence).</summary>
    internal bool MenuDrawn { get; private set; }

    /// <summary>The screen rectangle of the text as last drawn (a rail's measurement).</summary>
    internal (System.Numerics.Vector2 Min, System.Numerics.Vector2 Max) TextRect;

    /// <summary>Called each frame by the <see cref="StatusBarManager"/>, inside an active ImGui frame and the status-bar window. Right-aligned; 0 B steady.</summary>
    public void Render()
    {
        if (Line() is not { } line) return;
        var style = ImGui.GetStyle();
        float width = ImGui.CalcTextSize(line).X;
        float x = ImGui.GetWindowWidth() - width - style.WindowPadding.X;
        if (x > ImGui.GetCursorPosX()) ImGui.SetCursorPosX(x);   // the right end; never over the sections before it
        ImGui.TextDisabled(line);
        TextRect = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip(Tooltip);
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left)) _openRequested = true;
        if (_openRequested) { ImGui.OpenPopup(PopupId); _openRequested = false; }
        MenuDrawn = false;
        if (!ImGui.BeginPopup(PopupId)) return;
        MenuDrawn = true;
        for (int i = 0; i < _rows.Length; i++)
        {
            ref readonly var r = ref _rows[i];
            if (r.Header) { ImGui.TextDisabled(r.Label); continue; }
            ImGui.BeginDisabled(r.Why != null);
            ImGui.MenuItem(r.Label, null, r.Checked);
            ImGui.EndDisabled();
            if (r.Why != null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(r.Why);
        }
        ImGui.EndPopup();
    }
}
