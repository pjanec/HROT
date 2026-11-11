using System.Numerics;
using Fdp.Presentation.Fonts;
using Hrot.Terrain.Osm;
using Hrot.UI.Common.TerrainOsm;
using ImGuiNET;
using static Hrot.UI.Common.TerrainOsm.OsmWizardStyle;

namespace Hrot.UI.Common.TerrainRandom;

/// <summary>
/// ⭐⭐ Terrain authoring U-8 (docs/DESIGN_Terrain_Authoring.md §12–§13, §18f; the APPROVED mockup <c>docs/UX/terrain-authoring-1-wizard.svg</c>, R-4203) — <b>the modal "New random terrain"</b>: ① the six presets + Name · Size · Relief · Vegetation · Water · Seed,
/// ② the 2-D preview of what was generated (the OSM wizard's own preview records, drawn in its colours) with the counts, the budget verdict and <i>Shuffle layout</i> / <i>Shuffle ground</i>, ③ <i>Open in editor</i>. It DRAWS <see cref="RandomTerrainWizardModel"/> (pure) in the
/// frame and colours of the OSM wizard (<see cref="OsmWizardStyle"/>); every element's rectangle and glyph is recorded for the rail. ⚠ Deviations from the mockup, said in the status file: the preset tiles carry a name and a line, not a picture; the preview draws roads,
/// woods, water, scrub, block outlines and houses from the records — no relief shading and no per-recipe icon rows.
/// </summary>
public sealed class RandomTerrainWizardWindow
{
    public const string PopupId = "New random terrain##random_terrain_wizard";
    private readonly Func<IRandomTerrainService?> _service;
    private bool _wantOpen;

    public RandomTerrainWizardWindow(Func<IRandomTerrainService?> service) => _service = service ?? throw new ArgumentNullException(nameof(service));

    public RandomTerrainWizardModel? Model { get; private set; }
    public bool IsOpen { get; private set; }

    /// <summary>Raised once the scenario is written: the host opens it.</summary>
    public event Action<string>? Opened;

    public readonly Dictionary<string, (Vector2 Min, Vector2 Max)> Rects = new();
    public readonly Dictionary<string, (string Glyph, Vector4 Colour)> Glyphs = new();

    public string? DisabledReason => _service() == null ? "this host cannot generate terrains" : null;

    public void Open() { if (_service() is { } sv) { Model = new RandomTerrainWizardModel(sv); _wantOpen = true; } }
    public void OpenWith(RandomTerrainWizardModel model) { Model = model; _wantOpen = true; }

    private static Vector2 W(float x, float y, float s) => ImGui.GetWindowPos() + new Vector2(x, y) * s;
    private static void At(float x, float y, float s) => ImGui.SetCursorScreenPos(W(x, y, s));
    private static float Ascent() { unsafe { return ImGui.GetFont().NativePtr->Ascent * ImGui.GetFontSize() / ImGui.GetFont().FontSize; } }
    private void TextAt(float x, float y, float s, Vector4 c, string t) => ImGui.GetWindowDrawList().AddText(W(x, y, s) - new Vector2(0, Ascent()), ImGui.GetColorU32(c), t);

    private void Glyph(string key, string glyph, Vector4 colour, float x, float y, float s)
    {
        var at = W(x, y, s);
        ImGui.GetWindowDrawList().AddText(at, ImGui.GetColorU32(colour), glyph);
        Glyphs[key] = (glyph, colour);
        Rects["glyph:" + key] = (at, at + ImGui.CalcTextSize(glyph));
    }

    private void Section(float x, float y, float x1, string label, float s)
    {
        var p = W(x, y + 12, s) - new Vector2(0, Ascent());
        ImGui.GetWindowDrawList().AddText(p, ImGui.GetColorU32(SEPTEXT), label);
        var ts = ImGui.CalcTextSize(label);
        float ly = W(0, y + 8, s).Y;
        ImGui.GetWindowDrawList().AddLine(new Vector2(p.X + ts.X + 8 * s, ly), new Vector2(W(x1, 0, s).X, ly), ImGui.GetColorU32(SEP));
        Rects["sep:" + label] = (p, new Vector2(W(x1, 0, s).X, p.Y + ts.Y));
    }

    private bool Chip(string key, float x, float y, float w, string label, bool selected, float s)
    {
        At(x, y, s);
        ImGui.PushStyleColor(ImGuiCol.Button, selected ? BTNSEL : BTN);
        bool clicked = ImGui.Button(label + "##chip_" + key, new Vector2(w, 22) * s);
        ImGui.PopStyleColor();
        Rects["chip:" + key] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        return clicked;
    }

    private bool Button(string key, float x, float y, float w, string label, string? glyph, Vector4? glyphColour, Vector4? fill, bool disabled, float s)
    {
        At(x, y, s);
        if (disabled) ImGui.BeginDisabled();
        var f = fill ?? BTN;
        ImGui.PushStyleColor(ImGuiCol.Button, f); ImGui.PushStyleColor(ImGuiCol.ButtonHovered, (f * 1.12f) with { W = 1 });
        bool clicked = ImGui.Button((glyph == null ? label : "    " + label) + "##btn_" + key, new Vector2(w, 26) * s);
        ImGui.PopStyleColor(2);
        var min = ImGui.GetItemRectMin(); var max = ImGui.GetItemRectMax();
        if (disabled) ImGui.EndDisabled();
        if (glyph != null)
        {
            var g = new Vector2(min.X + 10 * s, min.Y + (max.Y - min.Y - ImGui.GetFontSize()) * 0.5f);
            ImGui.GetWindowDrawList().AddText(g, ImGui.GetColorU32(disabled ? DIS : glyphColour ?? SUB), glyph);
            Glyphs["btn:" + key] = (glyph, disabled ? DIS : glyphColour ?? SUB);
        }
        Rects["btn:" + key] = (min, max);
        return clicked && !disabled;
    }

    /// <summary>The frame overlay: lands finished work and draws the modal while open.</summary>
    public void Draw()
    {
        var m = Model;
        if (m == null) return;
        m.Pump();
        if (m.OpenedScenario is { } opened) { var sc = opened; Model = null; IsOpen = false; Opened?.Invoke(sc); return; }
        if (_wantOpen) { ImGui.OpenPopup(PopupId); _wantOpen = false; }
        float s = ImGui.GetFontSize() / 14f;
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowSize(new Vector2(1000, 560) * s, ImGuiCond.Always);
        ImGui.SetNextWindowPos(vp.WorkPos + vp.WorkSize * 0.5f, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        bool open = true;
        IsOpen = ImGui.BeginPopupModal(PopupId, ref open, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoSavedSettings);
        if (!IsOpen) return;
        Rects.Clear(); Glyphs.Clear();
        Rects["window"] = (ImGui.GetWindowPos(), ImGui.GetWindowPos() + ImGui.GetWindowSize());
        StepBar(m, s);
        bool cancel;
        switch (m.Step)
        {
            case RandomTerrainStep.Preset: PresetStep(m, s); cancel = FooterPreset(m, s); break;
            case RandomTerrainStep.Preview: PreviewStep(m, s); cancel = FooterPreview(m, s); break;
            default: TextAt(30, 150, s, TXT, m.Activity); TextAt(30, 176, s, NOTE, "the ground is imported and the navmesh baked once — a few seconds for a small terrain"); cancel = Button("cancel", 1000 - 110, 560 - 40, 90, "Cancel", null, null, null, false, s); if (cancel) m.Cancel(); cancel = false; break;
        }
        if (cancel || !open) { m.Cancel(); ImGui.CloseCurrentPopup(); IsOpen = false; Model = null; }
        ImGui.EndPopup();
    }

    private void StepBar(RandomTerrainWizardModel m, float s)
    {
        string[] names = { "Preset", "Preview", "Open" };
        for (int i = 0; i < 3; i++)
        {
            bool cur = (int)m.Step == i, done = (int)m.Step > i;
            At(14 + i * 130, 30, s);
            ImGui.PushStyleColor(ImGuiCol.Button, cur ? BTNSEL : BTN);
            ImGui.Button($"{i + 1}  {names[i]}##step_{i}", new Vector2(116, 22) * s);
            ImGui.PopStyleColor();
            Rects["step:" + i] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            if (done) Glyph("step:" + i, CHECK, OK, 22 + i * 130, 31, s);
        }
    }

    private void PresetStep(RandomTerrainWizardModel m, float s)
    {
        Section(16, 62, 600, "What kind of place", s);
        for (int i = 0; i < RandomTerrainChoices.Presets.Length; i++)
        {
            float x = 16 + (i % 3) * 196, y = 92 + (i / 3) * 150;
            At(x, y, s);
            bool sel = m.Preset == i;
            ImGui.PushStyleColor(ImGuiCol.Button, sel ? NEUTRALSEL : NEUTRAL);
            if (sel) ImGui.PushStyleColor(ImGuiCol.Border, ACC);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, sel ? 2f : 1f);
            if (ImGui.Button($"##preset_{i}", new Vector2(186, 140) * s)) m.Preset = i;
            ImGui.PopStyleVar();
            if (sel) ImGui.PopStyleColor();
            ImGui.PopStyleColor();
            var (min, max) = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            Rects["preset:" + i] = (min, max);
            // the tile's picture: the preset's character as coloured blobs on the ground green (a stand-in for the mockup's rendered plan)
            var dl = ImGui.GetWindowDrawList();
            dl.AddRectFilled(min + new Vector2(5, 5) * s, new Vector2(max.X - 5 * s, min.Y + 84 * s), ImGui.GetColorU32(new Vector4(0.2f, 0.29f, 0.17f, 1f)));
            int houses = i switch { 0 => 0, 1 => 3, 2 => 7, 3 => 20, 4 => 24, _ => 14 };
            for (int h = 0; h < houses; h++) dl.AddRectFilled(min + new Vector2(14 + (h * 37 % 150), 14 + (h * 23 % 56)) * s, min + new Vector2(22 + (h * 37 % 150), 20 + (h * 23 % 56)) * s, ImGui.GetColorU32(new Vector4(0.92f, 0.8f, 0.6f, 1f)));
            if (i >= 1) dl.AddRectFilled(min + new Vector2(120, 50) * s, min + new Vector2(168, 78) * s, ImGui.GetColorU32(new Vector4(0.12f, 0.36f, 0.2f, 1f)));
            dl.AddText(min + new Vector2(10, 92) * s, ImGui.GetColorU32(TXT), RandomTerrainChoices.Presets[i].Name);
            dl.AddText(min + new Vector2(10, 114) * s, ImGui.GetColorU32(NOTE), RandomTerrainChoices.Presets[i].Line);
            if (sel) Glyph("preset.sel", CIRCLE_CHECK, BLUE, (max.X - ImGui.GetWindowPos().X) / s - 22, (min.Y - ImGui.GetWindowPos().Y) / s + 112, s);
        }

        // the right column — Terrain: the mockup's rows at x 620…
        Section(620, 62, 984, "Terrain", s);
        At(620, 92, s); ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted("Name");
        At(690, 92, s); ImGui.SetNextItemWidth(280 * s);
        string name = m.Name;
        if (ImGui.InputText("##rt_name", ref name, 64)) m.Name = name;
        Rects["name"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        TextAt(690, 128, s, !m.NameOk ? ERR : m.NameExists ? PEND : NOTE, !m.NameOk ? "use letters, digits, - and _" : m.NameExists ? "a terrain or scenario of this name exists — choose another" : $"Saved as assets/terrain/{m.Name}/ + a scenario");
        Row("size", 150, "Size", RandomTerrainChoices.Sizes.Select(x => x.Label).ToArray(), m.Size, v => m.Size = v, s);
        Row("relief", 184, "Relief", RandomTerrainChoices.Reliefs, m.Relief, v => m.Relief = v, s);
        Row("vegetation", 218, "Vegetation", RandomTerrainChoices.Vegetations, m.Vegetation, v => m.Vegetation = v, s);
        Row("water", 252, "Water", RandomTerrainChoices.Waters, m.Water, v => m.Water = v, s);
        At(620, 286, s); ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted("Seed");
        At(690, 286, s); ImGui.SetNextItemWidth(150 * s);
        int seed = (int)(m.Seed % 1_000_000);
        if (ImGui.InputInt("##rt_seed", ref seed, 0, 0) && seed > 0) m.Seed = (ulong)seed;
        Rects["seed"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        if (Button("dice", 850, 286, 40, "", IconsFontAwesome6.ArrowsRotate, AMBER, null, false, s)) m.Seed = (ulong)Random.Shared.Next(10000, 99999);
        Glyph("info", INFO_G, INFO, 630, 336, s);
        TextAt(656, 352, s, SUB, "Everything the generator places — towns, villages,");
        TextAt(656, 370, s, SUB, "farms, woods — is an ordinary block in a scenario:");
        TextAt(656, 388, s, SUB, "move, re-roll or delete any of it later in the editor.");
        if (m.Error != null) TextAt(16, 520, s, ERR, m.Error);
    }

    private void Row(string key, float y, string label, string[] options, int selected, Action<int> set, float s)
    {
        At(620, y, s); ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted(label);
        for (int i = 0; i < options.Length; i++)
            if (Chip($"{key}:{i}", 690 + i * 70, y, 66, options[i], selected == i, s)) set(i);
    }

    private bool FooterPreset(RandomTerrainWizardModel m, float s)
    {
        if (Button("generate", 1000 - 250, 560 - 40, 130, "Generate", IconsFontAwesome6.ArrowsRotate, AMBER, BTNSEL, !m.NameOk || m.NameExists || m.Busy, s)) m.Generate();
        return Button("cancel", 1000 - 110, 560 - 40, 90, "Cancel", null, null, null, false, s);
    }

    private void PreviewStep(RandomTerrainWizardModel m, float s)
    {
        // the 2-D plan, left: the records in the OSM wizard's colours
        var canvasMin = W(14, 62, s); var canvasMax = W(654, 500, s);
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(canvasMin, canvasMax, ImGui.GetColorU32(new Vector4(0.2f, 0.27f, 0.17f, 1f)));
        Rects["map"] = (canvasMin, canvasMax);
        if (m.View is { } v) DrawPlan(dl, v.Preview, canvasMin, canvasMax, s);
        if (m.Busy) { TextAt(30, 90, s, AMBER, m.Activity); }

        // what was generated, right
        Section(676, 62, 984, "What was generated", s);
        if (m.View is { } view)
        {
            var c = view.Preview.Counts;
            int y = 92;
            Line("blocks", ref y, IconsFontAwesome6.City, ORANGE, $"{view.Blocks} blocks", $"{c.Buildings} houses", s);
            foreach (var (recipe, blocks, houses) in view.ByRecipe.Take(4)) Line("recipe:" + recipe, ref y, IconsFontAwesome6.House, ORANGE, $"{Hrot.UI.Common.AddEntity.TerrainPickCatalog.Title(recipe)} x{blocks}", houses > 0 ? $"{houses} houses" : "", s);
            Line("woods", ref y, IconsFontAwesome6.Tree, GREEN, "Woods", $"{c.WoodsAreaM2 / 10000.0:0} ha", s);
            Line("roads", ref y, IconsFontAwesome6.Route, AMBER, "Roads", $"{c.RoadKm:0.#} km", s);
            Line("water", ref y, IconsFontAwesome6.Water, BLUE, "Water", c.WaterAreas > 0 ? $"{c.WaterAreas} areas" : "none", s);
            var (fill, glyph, colour) = view.Preview.Verdict switch { BudgetVerdict.Ok => (OKBOX, CIRCLE_CHECK, OK), BudgetVerdict.Heavy => (PENDROW, WARNING, PEND), _ => (PENDROW, WARNING, ERR) };
            var box = (W(676, y + 6, s), W(984, y + 34, s));
            dl.AddRectFilled(box.Item1, box.Item2, ImGui.GetColorU32(fill));
            Rects["verdict"] = box;
            Glyph("verdict", glyph, colour, 686, y + 12, s);
            TextAt(710, y + 28, s, TXT, view.Preview.Verdict == BudgetVerdict.Ok ? $"Within budget — {c.Buildings} houses" : view.Preview.VerdictReason ?? view.Preview.Verdict.ToString());
        }
        Section(676, 340, 984, "Not happy?", s);
        if (Button("shuffle-layout", 676, 372, 308, "Shuffle layout", IconsFontAwesome6.Dice, AMBER, null, m.Busy, s)) m.ShuffleLayout();
        TextAt(940, 392, s, NOTE, "≈ 2 s");
        TextAt(676, 414, s, NOTE, "new places for the blocks, same ground");
        if (Button("shuffle-ground", 676, 424, 308, "Shuffle ground", IconsFontAwesome6.Mountain, GREEN, null, m.Busy, s)) m.ShuffleGround();
        TextAt(940, 444, s, NOTE, "≈ 40 s");
        TextAt(676, 466, s, NOTE, "new hills, roads and river, then a layout");
        if (m.Error != null) TextAt(16, 520, s, ERR, m.Error);
    }

    private void Line(string key, ref int y, string glyph, Vector4 colour, string label, string value, float s)
    {
        Glyph(key, glyph, colour, 680, y, s);
        TextAt(704, y + 12, s, TXT, label);
        TextAt(900, y + 12, s, NOTE, value);
        y += 22;
    }

    private bool FooterPreview(RandomTerrainWizardModel m, float s)
    {
        if (Button("back", 1000 - 360, 560 - 40, 90, "Back", null, null, null, m.Busy, s)) m.Back();
        if (Button("open", 1000 - 260, 560 - 40, 140, "Open in editor", IconsFontAwesome6.Mountain, OK, BTNSEL, m.Busy || m.View is null || !m.NameOk || m.NameExists, s)) m.OpenInEditor();
        return Button("cancel", 1000 - 110, 560 - 40, 90, "Cancel", null, null, null, false, s);
    }

    /// <summary>The records drawn as a plan: water, woods, scrub, then roads (by class), block outlines, houses.</summary>
    private static void DrawPlan(ImDrawListPtr dl, OsmPreview p, Vector2 min, Vector2 max, float s)
    {
        double minX = p.Bounds[0], minY = p.Bounds[1], maxX = p.Bounds[2], maxY = p.Bounds[3];
        float sc = (float)Math.Min((max.X - min.X) / (maxX - minX), (max.Y - min.Y) / (maxY - minY));
        var origin = new Vector2(min.X + ((max.X - min.X) - (float)(maxX - minX) * sc) * 0.5f, max.Y - ((max.Y - min.Y) - (float)(maxY - minY) * sc) * 0.5f);
        Vector2 P(Vector2 w) => new(origin.X + (w.X - (float)minX) * sc, origin.Y - (w.Y - (float)minY) * sc);
        dl.PushClipRect(min, max, true);
        foreach (var a in p.Areas)
        {
            if (a.Kind == "block" || a.Ring.Length < 3) continue;
            var col = a.Kind == "water" ? Water : a.Kind == "woods" ? Forest : Scrub;
            var pts = a.Ring.Select(P).ToArray();
            dl.AddConvexPolyFilled(ref pts[0], pts.Length, ImGui.GetColorU32(col));
        }
        foreach (var r in p.Roads) { var (c, w) = Road(r.Class); for (int i = 0; i + 1 < r.Points.Length; i++) dl.AddLine(P(r.Points[i]), P(r.Points[i + 1]), ImGui.GetColorU32(c), Math.Max(1f, w * 0.5f)); }
        foreach (var b in p.Buildings)
        {
            if (b.Footprint.Length < 3) continue;
            var pts = b.Footprint.Select(P).ToArray();
            dl.AddConvexPolyFilled(ref pts[0], pts.Length, ImGui.GetColorU32(Building(b.Storeys)));
        }
        foreach (var a in p.Areas)
            if (a.Kind == "block" && a.Ring.Length >= 3)
            {
                var pts = a.Ring.Select(P).ToArray();
                dl.AddPolyline(ref pts[0], pts.Length, ImGui.GetColorU32(new Vector4(1, 1, 1, 0.85f)), ImDrawFlags.Closed, 1.2f * s);
            }
        dl.PopClipRect();
    }
}
