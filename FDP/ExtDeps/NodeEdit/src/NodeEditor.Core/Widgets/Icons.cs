using System.Numerics;

namespace NodeEditor.Core.Widgets;

/// <summary>
/// ⭐ U-2 (docs/UX/DESIGN_Colour_Icons.md §2–§3.1) — THE semantic key → <see cref="IconGlyph"/> map: the colour belongs to the icon's KEY, not to the call site (R-174), so every place that draws
/// "by command" gets the same glyph in the same colour. A static <c>switch</c> on the key (the C# compiler hashes it): no dictionary, no allocation on a lookup.
/// <para>The glyph strings are FontAwesome 6 Solid code points — the same ones as <c>Fdp.Presentation.Fonts.IconsFontAwesome6</c> (NodeEditor.Core cannot reference it; a rail in Hrot.Presentation.Tests
/// pins every entry to that file's constant). The mode keys fold in <c>MainToolbarTimeControlSection.ModeGlyph</c> (DAR §9a, R-3060).</para>
/// </summary>
public static class Icons
{
    /// <summary>What an unknown key draws: a question mark in the muted grey — visible, never a crash.</summary>
    public static readonly IconGlyph Unknown = new("\uf128", IconPalette.Muted);   // FA question

    /// <summary>The glyph and colour of <paramref name="key"/>; false (and <see cref="Unknown"/>) when no such key.</summary>
    public static bool TryGet(string key, out IconGlyph icon)
    {
        switch (key)
        {
            case "mode/edit": icon = new("\uf304", IconPalette.Yellow); return true;   // Pen
            case "mode/preview": icon = new("\uf06e", IconPalette.Violet); return true;   // Eye — ⭐ CE-3394 / presets D9 (user 2026-11-07): the eye replaces ◐ (R-3061 SUPERSEDED)
            case "mode/live": icon = new("\uf111", IconPalette.Green); return true;   // Circle
            case "mode/review": icon = new("\uf1da", IconPalette.Amber); return true;   // ClockRotateLeft
            case "mode/replay": icon = new("\uf04b", IconPalette.Blue); return true;   // Play
            case "orbat/command": icon = new("\uf0e8", IconPalette.Teal); return true;   // Sitemap
            case "orbat/structure": icon = new("\uf1b3", IconPalette.Orange); return true;   // Cubes
            case "orbat/scenario": icon = new("\uf12e", IconPalette.Violet); return true;   // PuzzlePiece
            // ⭐ U-4 (CE-4334, UI Conventions §2) — the ICON of an ORBAT row, by what the row IS (a unit with subordinates · a part · a Scenario-view group; a macro instance reuses orbat/scenario, any other entity thing/entity)
            case "orbat/unit": icon = new("\uf0c0", IconPalette.Teal); return true;   // Users — a unit that takes subordinates
            case "orbat/part": icon = new("\uf013", IconPalette.Grey); return true;   // Gear — a part of a vehicle (sensor, weapon mount)
            case "orbat/group": icon = new("\uf07c", IconPalette.Amber); return true;   // FolderOpen — Saved / Transient
            case "details/layout-stacked": icon = new("\uf5fd", IconPalette.Violet); return true;   // LayerGroup
            case "details/layout-tabs": icon = new("\uf0db", IconPalette.Blue); return true;   // TableColumns
            case "details/float": icon = new("\uf35d", IconPalette.Blue); return true;   // UpRightFromSquare
            case "details/pin": icon = new("\uf08d", IconPalette.Amber); return true;   // Thumbtack
            case "doc/blueprint": icon = new("\uf0e7", IconPalette.Amber); return true;   // Bolt
            case "doc/btree": icon = new("\uf0e8", IconPalette.Teal); return true;   // Sitemap
            case "doc/hsm": icon = new("\ue4e2", IconPalette.Violet); return true;   // CircleNodes
            case "doc/blackboard": icon = new("\uf5fd", IconPalette.Blue); return true;   // LayerGroup
            case "doc/utility": icon = new("\uf013", IconPalette.Grey); return true;   // Gear
            case "doc/scenario": icon = new("\uf542", IconPalette.Green); return true;   // DiagramProject
            case "doc/condition": icon = new("\uf059", IconPalette.Blue); return true;   // CircleQuestion
            case "doc/other": icon = new("\uf15b", IconPalette.Grey); return true;   // File
            case "bp/eqs-template": icon = new("\uf7c0", IconPalette.Teal); return true;   // SatelliteDish
            case "bp/cross-asset": icon = new("\uf0c1", IconPalette.Blue); return true;   // Link
            case "bp/condition": icon = new("\uf0e7", IconPalette.Amber); return true;   // Bolt
            case "bp/eqs-result": icon = new("\uf080", IconPalette.Orange); return true;   // ChartBar
            case "task/running": icon = new("\uf04b", IconPalette.Blue); return true;   // Play
            case "task/done": icon = new("\uf00c", IconPalette.Green); return true;   // Check
            case "task/failed": icon = new("\uf00d", IconPalette.Red); return true;   // Xmark
            case "task/skipped": icon = new("\uf051", IconPalette.Grey); return true;   // ForwardStep
            case "task/stopped": icon = new("\uf04d", IconPalette.Grey); return true;   // Stop
            case "form/error": icon = new("\uf057", IconPalette.Error); return true;   // CircleXmark
            case "form/pending": icon = new("\uf252", IconPalette.Pending); return true;   // HourglassHalf
            case "form/locked": icon = new("\uf023", IconPalette.Muted); return true;   // Lock
            case "form/reset": icon = new("\uf2ea", IconPalette.Info); return true;   // RotateLeft
            case "form/warning": icon = new("\uf071", IconPalette.Pending); return true;   // TriangleExclamation
            case "form/info": icon = new("\uf05a", IconPalette.Info); return true;   // CircleInfo
            case "form/pick": icon = new("\uf05b", IconPalette.Blue); return true;   // Crosshairs
            // ⭐ U-7 (Condition editor §8.2 ①, condition-expert-visual.svg) — the condition kinds, as the mockup draws them
            case "kind/all": icon = new("\uf0b0", IconPalette.Teal); return true;   // Filter — All of these
            case "kind/on-entity": icon = new("\uf05b", IconPalette.Blue); return true;   // Crosshairs — On one thing
            case "kind/property": icon = new("\uf0ae", IconPalette.Green); return true;   // ListCheck — A property
            case "kind/time": icon = new("\uf017", IconPalette.Amber); return true;   // Clock — Time
            case "kind/count": icon = new("\uf0c0", IconPalette.Orange); return true;   // Users — At least N of
            // ⭐ U-8 (Condition editor §8.2 ④) — a THING in an entity-link chip, by its kind; and the chip's map pick
            case "thing/objective": icon = new("\uf024", IconPalette.Amber); return true;   // Flag
            case "thing/trigger": icon = new("\uf0e7", IconPalette.Violet); return true;   // Bolt
            case "rule/phase": icon = new("\uf024", IconPalette.Violet); return true;   // Flag — ⭐ W-1 (P-8): an operation phase (a trigger marked as one), mission-plan-detail-visual.svg B
            case "thing/entity": icon = new("\uf1b2", IconPalette.Blue); return true;   // Cube
            case "form/map-pick": icon = new("\uf3c5", IconPalette.Teal); return true;   // LocationDot
            case "tab/unit": icon = new("\uf007", IconPalette.Blue); return true;   // User
            case "tab/crew": icon = new("\uf5b6", IconPalette.Orange); return true;   // VanShuttle
            case "tab/orders": icon = new("\uf4d7", IconPalette.Green); return true;   // Route
            case "tab/brain": icon = new("\uf5dc", IconPalette.Pink); return true;   // Brain
            case "tab/rules": icon = new("\uf24e", IconPalette.Amber); return true;   // ScaleBalanced
            case "tab/composition": icon = new("\uf12e", IconPalette.Violet); return true;   // PuzzlePiece
            case "tab/shape": icon = new("\uf5ee", IconPalette.Teal); return true;   // DrawPolygon
            case "tab/event": icon = new("\uf0e7", IconPalette.Amber); return true;   // Bolt
            case "tab/components": icon = new("\uf121", IconPalette.Grey); return true;   // Code
            case "tab/scenario": icon = new("\uf7a2", IconPalette.Blue); return true;   // EarthEurope
            case "tab/when": icon = new("\uf0b0", IconPalette.Blue); return true;   // Filter
            case "tab/do": icon = new("\uf04b", IconPalette.Green); return true;   // Play
            case "tab/settings": icon = new("\uf013", IconPalette.Grey); return true;   // Gear
            case "tab/results": icon = new("\uf11e", IconPalette.Amber); return true;   // FlagCheckered
            case "tab/overview": icon = new("\uf05a", IconPalette.Blue); return true;   // CircleInfo
            case "tab/status": icon = new("\uf05a", IconPalette.Blue); return true;   // CircleInfo
            case "tab/bindings": icon = new("\uf0c1", IconPalette.Blue); return true;   // Link
            case "tab/history": icon = new("\uf1da", IconPalette.Grey); return true;   // ClockRotateLeft
            case "section/health": icon = new("\uf0f1", IconPalette.Green); return true;   // Stethoscope
            case "section/time": icon = new("\uf017", IconPalette.Amber); return true;   // Clock
            case "section/terrain": icon = new("\uf6fc", IconPalette.Teal); return true;   // Mountain
            case "section/assets": icon = new("\uf468", IconPalette.Orange); return true;   // BoxesStacked
            case "section/checkpoint": icon = new("\uf024", IconPalette.Amber); return true;   // Flag
            case "section/episodes": icon = new("\uf008", IconPalette.Pink); return true;   // Film
            case "section/archive": icon = new("\uf187", IconPalette.Grey); return true;   // BoxArchive
            case "section/spawn": icon = new("\uf067", IconPalette.Green); return true;   // Plus
            case "section/params": icon = new("\uf1de", IconPalette.Blue); return true;   // Sliders
            case "section/allocations": icon = new("\uf538", IconPalette.Violet); return true;   // Memory
            case "section/render": icon = new("\uf03e", IconPalette.Pink); return true;   // Image
            case "section/list": icon = new("\uf03a", IconPalette.Teal); return true;   // List
            case "section/search": icon = new("\uf002", IconPalette.Blue); return true;   // MagnifyingGlass
            case "section/selection": icon = new("\uf0ae", IconPalette.Blue); return true;   // ListCheck
            default: icon = Unknown; return false;
        }
    }

    /// <summary>The glyph and colour of <paramref name="key"/>, or <see cref="Unknown"/>.</summary>
    public static IconGlyph Get(string key) { TryGet(key, out var icon); return icon; }

    /// <summary>Every key the map answers (for rails and for sheets).</summary>
    public static readonly string[] Keys =
    {
        "mode/edit",
        "mode/preview",
        "mode/live",
        "mode/review",
        "mode/replay",
        "orbat/command",
        "orbat/structure",
        "orbat/scenario",
        "orbat/unit",
        "orbat/part",
        "orbat/group",
        "details/layout-stacked",
        "details/layout-tabs",
        "details/float",
        "details/pin",
        "doc/blueprint",
        "doc/btree",
        "doc/hsm",
        "doc/blackboard",
        "doc/utility",
        "doc/scenario",
        "doc/condition",
        "doc/other",
        "bp/eqs-template",
        "bp/cross-asset",
        "bp/condition",
        "bp/eqs-result",
        "task/running",
        "task/done",
        "task/failed",
        "task/skipped",
        "task/stopped",
        "form/error",
        "form/pending",
        "form/locked",
        "form/reset",
        "form/warning",
        "form/info",
        "form/pick",
        "kind/all",
        "kind/on-entity",
        "kind/property",
        "kind/time",
        "kind/count",
        "thing/objective",
        "thing/trigger",
        "thing/entity",
        "form/map-pick",
        "tab/unit",
        "tab/crew",
        "tab/orders",
        "tab/brain",
        "tab/rules",
        "tab/composition",
        "tab/shape",
        "tab/event",
        "tab/components",
        "tab/scenario",
        "tab/when",
        "tab/do",
        "tab/settings",
        "tab/results",
        "tab/overview",
        "tab/status",
        "tab/bindings",
        "tab/history",
        "section/health",
        "section/time",
        "section/terrain",
        "section/assets",
        "section/checkpoint",
        "section/episodes",
        "section/archive",
        "section/spawn",
        "section/params",
        "section/allocations",
        "section/render",
        "section/list",
        "section/search",
        "section/selection"
    };
}
