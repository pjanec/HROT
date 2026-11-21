using System;
using System.Numerics;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Vis2D;
using Hrot.ScenarioEditor.Map;
using Hrot.ScenarioEditor.Tools;
using ImGuiNET;

namespace Hrot.UI.Common.Panels;

/// <summary>
/// ⭐ B-17 P1 (📄 docs/UX/DESIGN_Map_Interaction.md §2.5 <i>"the status line shows label and hint"</i>; mockup <c>map-interaction-visual.svg</c>, the
/// line under each map) — the armed map tool in the status bar: its icon (in the tool colour), its name, and what the next click does
/// (<see cref="ToolController.ActiveHint"/>). Nothing while the plain Select tool is armed.
/// <para>⭐ Registered by <see cref="MapCursorStatusBarSection.Register"/> on every map host — ⛔ never a per-host call to forget. It reads the
/// canvas's <see cref="MapToolsLink"/> (put there by <c>MapInteractionPack.AttachMapLayers</c>), resolved per frame.</para>
/// </summary>
public sealed class ToolStatusSection
{
    /// <summary>The status-bar section id.</summary>
    public const string SectionId = "map_tool";

    /// <summary>Just left of the cursor reading (50).</summary>
    public const int SortOrder = 45;

    /// <summary>The armed tool's icon colour (the mockup's tool teal).</summary>
    public static readonly Vector4 GlyphColour = new(0.31f, 0.85f, 0.72f, 1f);
    /// <summary>The hint's grey.</summary>
    public static readonly Vector4 HintColour = new(0.66f, 0.66f, 0.72f, 1f);

    private readonly Func<MapCanvas?> _canvas;

    public ToolStatusSection(Func<MapCanvas?> canvas) => _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));

    /// <summary>Registers the section on <paramref name="bar"/> for <paramref name="perspective"/>.</summary>
    public static ToolStatusSection Register(StatusBarManager bar, Func<MapCanvas?> canvas, string perspective)
    {
        var section = new ToolStatusSection(canvas);
        bar.RegisterSection(SectionId, SortOrder, section.Render, perspective);
        return section;
    }

    /// <summary>The armed tool shown now (null: nothing — no controller, no modal tool, or plain Select).</summary>
    public ToolDescriptor? Shown()
        => _canvas()?.Get<MapToolsLink>()?.Tools is { ActiveModal: { } tool } && tool.Id != ScenarioToolIds.Select ? tool : null;

    /// <summary>Called each frame by the <see cref="StatusBarManager"/>, inside the status-bar window.</summary>
    public void Render()
    {
        if (Shown() is not { } tool) return;
        if (tool.Glyph is { Length: > 0 } glyph) { ImGui.TextColored(GlyphColour, glyph); ImGui.SameLine(); }
        ImGui.TextUnformatted(tool.Label);
        if (_canvas()?.Get<MapToolsLink>()?.Tools?.ActiveHint is { Length: > 0 } hint)
        {
            ImGui.SameLine();
            ImGui.TextColored(HintColour, hint);
        }
    }
}
