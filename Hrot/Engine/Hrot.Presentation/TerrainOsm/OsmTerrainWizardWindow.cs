using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Hrot.Terrain.Osm;
using ImGuiNET;
using static Hrot.UI.Common.TerrainOsm.OsmWizardStyle;

namespace Hrot.UI.Common.TerrainOsm;

/// <summary>
/// ⭐⭐ CE-4375 (OSM §6 · §6a · §6b; the APPROVED mockups <c>docs/UX/terrain-osm-wizard-visual.svg</c> / <c>terrain-osm-preview-visual.svg</c>, R-4194) — <b>the modal "New terrain from
/// OpenStreetMap"</b>: ONE window through the four steps (C-1) — the step bar (C-2), the map canvas left with the right panel per step (C-3), the footer (C-4) — laid out at the
/// mockup generator's coordinates (<c>scripts/gen-terrain-osm-wizard-mockup.py</c>) scaled by the font. It DRAWS <see cref="OsmWizardModel"/> (the step logic, pure) and
/// <see cref="OsmMapView"/> (the one map canvas). ⭐ Every element it draws is recorded in <see cref="Rects"/> / <see cref="Glyphs"/> — the conformance rails measure them (W-4).
/// Hosted by <c>WindowManager.RegisterFrameOverlay(Draw)</c>, as the existing modals.
/// </summary>
public sealed class OsmTerrainWizardWindow : IDisposable
{
    public const string Title = "New terrain from OpenStreetMap";
    public const string PopupId = Title + "##osm_wizard";
    private static readonly string[] StepNames = { "Area", "Options", "Preview", "Import" };

    private readonly Func<OsmWizardServices> _services;
    private OsmTileTextures? _tiles;
    private ITileSource? _tilesFor;
    private bool _wantOpen;

    public OsmTerrainWizardWindow(Func<OsmWizardServices> services) => _services = services ?? throw new ArgumentNullException(nameof(services));

    /// <summary>The run on show (null = closed and never opened). "Run in background" keeps it while the window is closed.</summary>
    public OsmWizardModel? Model { get; private set; }
    public OsmMapView Map { get; } = new();
    public bool IsOpen { get; private set; }

    /// <summary>Every element's rectangle last frame (screen px), by name — the conformance rails' measurement (C-1 … C-14).</summary>
    public readonly Dictionary<string, (Vector2 Min, Vector2 Max)> Rects = new();
    /// <summary>Every named glyph drawn last frame with its colour, by name (R-4139: the glyph AND its colour are asserted).</summary>
    public readonly Dictionary<string, (string Glyph, Vector4 Colour)> Glyphs = new();
    /// <summary>The step bar's chips last frame: fill and state per step (C-2).</summary>
    public readonly (Vector4 Fill, Vector4 Border, string State)[] Chips = new (Vector4, Vector4, string)[4];

    /// <summary>Why the menu item is greyed (null = it opens): no pipeline on this host yet (W-3 — ⛔ never a stub pipeline in production).</summary>
    public string? DisabledReason => _services().Pipeline == null ? "the pipeline is not built yet" : null;

    /// <summary>File ▸ New ▸ Terrain from OpenStreetMap…: opens the wizard (a running import is shown again, not restarted).</summary>
    public void Open()
    {
        var services = _services();
        if (Model == null || Model.ImportState != OsmRunState.Running) Model = new OsmWizardModel(services);
        if (services.Tiles is { } src && !ReferenceEquals(src, _tilesFor)) { _tiles?.Dispose(); _tiles = new OsmTileTextures(src); _tilesFor = src; }
        _wantOpen = true;
    }

    /// <summary>Opens on a model a rail built (the fakes, a given step).</summary>
    public void OpenWith(OsmWizardModel model)
    {
        Model = model;
        if (model.Services.Tiles is { } src && !ReferenceEquals(src, _tilesFor)) { _tiles?.Dispose(); _tiles = new OsmTileTextures(src); _tilesFor = src; }
        if (model.Box is { } box) Map.Fit(box);
        _wantOpen = true;
    }

    /// <summary>The frame overlay: pumps the run (also while closed — "Run in background") and draws the modal when open.</summary>
    public void Draw()
    {
        Model?.Pump();
        _tiles?.Pump();
        if (Model == null) return;
        if (_wantOpen) { ImGui.OpenPopup(PopupId); _wantOpen = false; }
        float s = ImGui.GetFontSize() / 14f;
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowSize(new Vector2(1200, 600) * s, ImGuiCond.Always);
        ImGui.SetNextWindowPos(vp.WorkPos + vp.WorkSize * 0.5f, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        bool open = true;
        IsOpen = ImGui.BeginPopupModal(PopupId, ref open, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoSavedSettings);
        if (!IsOpen) return;
        Rects.Clear(); Glyphs.Clear();
        var m = Model;
        Rects["window"] = (ImGui.GetWindowPos(), ImGui.GetWindowPos() + ImGui.GetWindowSize());
        StepBar(m, s);
        switch (m.Step)
        {
            case OsmWizardStep.Area: AreaStep(m, s); break;
            case OsmWizardStep.Options: OptionsStep(m, s); break;
            case OsmWizardStep.Preview: PreviewStep(m, s); break;
            default: ImportStep(m, s); break;
        }
        bool close = Footer(m, s) || !open;
        if (close) { if (m.Cancel()) { ImGui.CloseCurrentPopup(); IsOpen = false; } }
        ImGui.EndPopup();
    }

    // ── layout helpers (mockup coordinates are relative to the window's top-left; ×s) ───────

    private static Vector2 W(float x, float y, float s) => ImGui.GetWindowPos() + new Vector2(x, y) * s;
    private static void At(float x, float y, float s) => ImGui.SetCursorScreenPos(W(x, y, s));

    private void Glyph(string key, string glyph, Vector4 colour, Vector2 at)
    {
        ImGui.GetWindowDrawList().AddText(at, ImGui.GetColorU32(colour), glyph);
        var size = ImGui.CalcTextSize(glyph);
        Glyphs[key] = (glyph, colour);
        Rects["glyph:" + key] = (at, at + size);
    }

    /// <summary>The font's ascent (px): the mockup places text by its BASELINE, ImGui by its top.</summary>
    private static float Ascent() { unsafe { return ImGui.GetFont().NativePtr->Ascent * ImGui.GetFontSize() / ImGui.GetFont().FontSize; } }

    /// <summary>Text at a mockup BASELINE (x, y) — the generator's <c>s.text(x, y, …)</c>.</summary>
    private static void TextAt(float x, float y, float s, Vector4 colour, string text, bool bold = false) => Text(W(x, y, s) - new Vector2(0, Ascent()), colour, text, bold);

    private static void Text(Vector2 at, Vector4 colour, string text, bool bold = false)
    {
        var dl = ImGui.GetWindowDrawList();
        dl.AddText(at, ImGui.GetColorU32(colour), text);
        if (bold) dl.AddText(at + new Vector2(0.6f, 0), ImGui.GetColorU32(colour), text);
    }

    /// <summary>The mockup's <c>separator_text(x, y)</c>: the label (baseline y + 12, <see cref="OsmWizardStyle.SEPTEXT"/>), then a line at y + 8 to <paramref name="x1"/>.</summary>
    private void Separator(string key, float x, float y, float x1, string label, float s)
    {
        var p = W(x, y + 12, s) - new Vector2(0, Ascent());
        Text(p, SEPTEXT, label);
        var ts = ImGui.CalcTextSize(label);
        float ly = W(0, y + 8, s).Y;
        ImGui.GetWindowDrawList().AddLine(new Vector2(p.X + ts.X + 8 * s, ly), new Vector2(W(x1, 0, s).X, ly), ImGui.GetColorU32(SEP));
        Rects["sep:" + key] = (p, new Vector2(W(x1, 0, s).X, p.Y + ts.Y));
    }

    /// <summary>The mockup's <c>form_row</c>: a label column, then a field (read-only shows the value disabled).</summary>
    private void FormRowReadOnly(string key, float x, float y, float labelW, float fieldW, string label, string value, float s)
    {
        At(x, y, s); ImGui.AlignTextToFramePadding();
        ImGui.PushStyleColor(ImGuiCol.Text, TXT); ImGui.TextUnformatted(label); ImGui.PopStyleColor();
        At(x + labelW, y, s);
        ImGui.SetNextItemWidth(fieldW * s);
        ImGui.BeginDisabled();
        string v = value;
        ImGui.InputText("##ro_" + key, ref v, 64, ImGuiInputTextFlags.ReadOnly);
        ImGui.EndDisabled();
        Rects["field:" + key] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
    }

    private readonly Dictionary<string, string> _pads = new();
    private float _padScale;

    /// <summary>The leading spaces that leave room for <paramref name="glyph"/> + a gap before the label (glyphs differ in width — a fixed pad overlaps the wide ones). Cached per glyph.</summary>
    private string Pad(string glyph, float s)
    {
        if (s != _padScale) { _pads.Clear(); _padScale = s; }
        if (_pads.TryGetValue(glyph, out var p)) return p;
        float need = ImGui.CalcTextSize(glyph).X + 5 * s;
        p = " ";
        while (ImGui.CalcTextSize(p).X < need) p += " ";
        _pads[glyph] = p;
        return p;
    }

    /// <summary>A button with its glyph drawn in the colour of its meaning (R-4139). Returns clicked.</summary>
    private bool IconButton(string key, float x, float y, float w, string label, string? glyph, Vector4? glyphColour, Vector4? fill, bool disabled, float s, string? tip = null)
    {
        At(x, y, s);
        if (disabled) ImGui.BeginDisabled();
        if (fill is { } f) { ImGui.PushStyleColor(ImGuiCol.Button, f); ImGui.PushStyleColor(ImGuiCol.ButtonHovered, (f * 1.12f) with { W = 1 }); }
        string text = glyph == null ? label : Pad(glyph, s) + label;
        bool clicked = ImGui.Button(text + "##btn_" + key, new Vector2(w, 26) * s);
        if (fill != null) ImGui.PopStyleColor(2);
        var min = ImGui.GetItemRectMin(); var max = ImGui.GetItemRectMax();
        if (disabled) { ImGui.EndDisabled(); if (tip != null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(tip); }
        if (glyph != null)
        {
            var ls = ImGui.CalcTextSize(text);
            var gx = min.X + (max.X - min.X - ls.X) * 0.5f;
            Glyph("btn:" + key, glyph, disabled ? DIS : glyphColour ?? SUB, new Vector2(gx, min.Y + (max.Y - min.Y - ImGui.GetFontSize()) * 0.5f));
        }
        Rects["btn:" + key] = (min, max);
        return clicked && !disabled;
    }

    // ── the step bar (C-2) ─────────────────────────────────────────────────────────────────

    private void StepBar(OsmWizardModel m, float s)
    {
        var dl = ImGui.GetWindowDrawList();
        float x = 12;
        int step = (int)m.Step;
        for (int i = 0; i < 4; i++)
        {
            bool done = i < step, cur = i == step;
            float w = 26 + ImGui.CalcTextSize($"{i + 1}  {StepNames[i]}").X / s + 14;
            var min = W(x, 30, s); var max = W(x + w, 54, s);
            var fill = cur ? BTNSEL : done ? FRAME : NEUTRAL;
            var border = cur ? ACC : BORDER;
            dl.AddRectFilled(min, max, ImGui.GetColorU32(fill), 12 * s);
            dl.AddRect(min, max, ImGui.GetColorU32(border), 12 * s);
            Chips[i] = (fill, border, cur ? "current" : done ? "done" : "ahead");
            Rects["chip:" + i] = (min, max);
            float ty = min.Y + (max.Y - min.Y - ImGui.GetFontSize()) * 0.5f;
            if (done) Glyph("chip:" + i, CHECK, OK, new Vector2(min.X + 9 * s, ty));
            else Text(new Vector2(min.X + 13 * s, ty), cur ? TXT : DIS, (i + 1).ToString(CultureInfo.InvariantCulture), bold: true);
            Text(new Vector2(min.X + 28 * s, ty), cur || done ? TXT : DIS, StepNames[i]);
            x += w;
            if (i < 3) { dl.AddLine(W(x + 4, 42, s), W(x + 20, 42, s), ImGui.GetColorU32(SEP), 1.2f * s); x += 24; }
        }
    }

    // ── the footer (C-4) ───────────────────────────────────────────────────────────────────

    /// <returns>Cancel was pressed.</returns>
    private bool Footer(OsmWizardModel m, float s)
    {
        float y = 600 - 34;
        ImGui.GetWindowDrawList().AddLine(W(0, y - 8, s), W(1200, y - 8, s), ImGui.GetColorU32(SEP));
        Rects["footer.sep"] = (W(0, y - 8, s), W(1200, y - 8, s));
        if (m.Step == OsmWizardStep.Import)
        {
            if (m.ImportState == OsmRunState.Running && IconButton("background", 910, y, 170, "Run in background", BACKGROUND, SUB, null, false, s))
            { ImGui.CloseCurrentPopup(); IsOpen = false; return false; }
            if (m.ImportState == OsmRunState.Failed && IconButton("retry", 790, y, 110, "Retry", null, null, BTNSEL, false, s)) m.Retry();
            bool done = m.ImportState == OsmRunState.Done;
            return IconButton("cancel", 1090, y, 90, done ? "Close" : "Cancel", done ? null : XMARK, RED, null, false, s);
        }
        if (m.CanBack && IconButton("back", 870, y, 90, "Back", BACK, SUB, null, false, s)) m.Back();
        var (label, glyph, colour) = m.Step switch
        {
            OsmWizardStep.Area => ("Next", NEXT, TXT),
            OsmWizardStep.Options => ("Preview", EYE, TEAL),
            _ => ("Import", CHECK, GREEN),
        };
        string? why = m.PrimaryRefusal;
        if (IconButton("primary", 970, y, 110, label, glyph, colour, why == null ? BTNSEL : BTN, why != null, s, why)) m.Primary();
        return IconButton("cancel", 1090, y, 90, "Cancel", null, null, null, false, s);
    }

    // ── ① Area ─────────────────────────────────────────────────────────────────────────────

    private const float CanvasX = 10, CanvasY = 64, CanvasW = 830, CanvasH = 476, RightX = 852, RightW = 338;

    private void AreaStep(OsmWizardModel m, float s)
    {
        At(CanvasX, CanvasY, s);
        Map.Draw(m, _tiles, new Vector2(CanvasW, CanvasH) * s, editBox: true, preview: null, s, () => SearchOverlay(m, s));
        Rects["canvas"] = (Map.CanvasMin, Map.CanvasMax);
        float y = CanvasY;
        Separator("area", RightX, y + 10, RightX + RightW, "The area", s);
        TextAt(RightX, y + 40, s, SUB, "Drag a box on the map, or search a place.");
        var (lat, lon) = m.BoxCentre;
        bool has = m.Box != null;
        FormRowReadOnly("width", RightX, y + 58, 80, RightW - 90, "Width", has ? (m.BoxWidthM / 1000).ToString("0.0", CultureInfo.InvariantCulture) + " km" : "—", s);
        FormRowReadOnly("height", RightX, y + 86, 80, RightW - 90, "Height", has ? (m.BoxHeightM / 1000).ToString("0.0", CultureInfo.InvariantCulture) + " km" : "—", s);
        FormRowReadOnly("centre", RightX, y + 114, 80, RightW - 90, "Centre", has ? CentreText(lat, lon) : "—", s);
        FormRowReadOnly("utm", RightX, y + 142, 80, RightW - 90, "UTM zone", has ? WebMercator.UtmZone(lon) + (lat < 0 ? " S" : " N") : "—", s);
        var dl = ImGui.GetWindowDrawList();
        var iMin = W(RightX, y + 178, s); var iMax = W(RightX + RightW, y + 204, s);
        dl.AddRectFilled(iMin, iMax, ImGui.GetColorU32(NEUTRAL), 3 * s);
        dl.AddRect(iMin, iMax, ImGui.GetColorU32(BORDER), 3 * s);
        Rects["info.largest"] = (iMin, iMax);
        Glyph("largest", INFO_G, INFO, W(RightX + 8, y + 196, s) - new Vector2(0, Ascent()));
        TextAt(RightX + 28, y + 196, s, SUB, $"Largest box: {m.MaxSideMetres / 1000:0} × {m.MaxSideMetres / 1000:0} km");
        Separator("estimate", RightX, y + 232, RightX + RightW, "Estimate (from the map, before download)", s);
        TextAt(RightX, y + 262, s, NOTE, "The content check runs in Preview —");
        TextAt(RightX, y + 276, s, NOTE, "a town packs ≈ 1 000 buildings per km²;");
        TextAt(RightX, y + 290, s, NOTE, "HROT is tuned for ≈ 1 800 in all.");
        if (m.BoxRefusal is { } refusal && m.Box != null) TextAt(RightX, y + 318, s, RED, refusal);
    }

    private static string CentreText(double lat, double lon)
        => Math.Abs(lat).ToString("0.0000", CultureInfo.InvariantCulture) + (lat >= 0 ? " N · " : " S · ") + Math.Abs(lon).ToString("0.0000", CultureInfo.InvariantCulture) + (lon >= 0 ? " E" : " W");

    /// <summary>The search field over the map (C-6) and its results.</summary>
    private void SearchOverlay(OsmWizardModel m, float s)
    {
        var min = Map.CanvasMin + new Vector2(46, 10) * s;
        ImGui.SetCursorScreenPos(min);
        ImGui.SetNextItemWidth(330 * s);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, POPUP);
        ImGui.PushStyleColor(ImGuiCol.Border, ACC);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(26 * s, 4 * s));
        if (ImGui.InputTextWithHint("##osm_search", "Search a place", ref m.SearchText, 128, ImGuiInputTextFlags.EnterReturnsTrue)) m.RunSearch();
        ImGui.PopStyleVar(2); ImGui.PopStyleColor(2);
        var fMin = ImGui.GetItemRectMin(); var fMax = ImGui.GetItemRectMax();
        Rects["search"] = (fMin, fMax);
        Glyph("search", SEARCH, SUB, new Vector2(fMin.X + 8 * s, fMin.Y + (fMax.Y - fMin.Y - ImGui.GetFontSize()) * 0.5f));
        var results = m.SearchResults;
        if (results.Count == 0) return;
        var dl = ImGui.GetForegroundDrawList();
        var lMin = new Vector2(fMin.X, fMax.Y + 2 * s);
        var lMax = new Vector2(fMax.X, lMin.Y + (results.Count * 23 + 3) * s);
        dl.AddRectFilled(lMin, lMax, ImGui.GetColorU32(POPUP), 3 * s);
        dl.AddRect(lMin, lMax, ImGui.GetColorU32(BORDER), 3 * s);
        Rects["search.results"] = (lMin, lMax);
        for (int i = 0; i < results.Count; i++)
        {
            var rMin = lMin + new Vector2(1, 1 + i * 23) * s; var rMax = new Vector2(lMax.X - 1, rMin.Y + 23 * s);
            ImGui.SetCursorScreenPos(rMin);
            if (ImGui.InvisibleButton("##osm_result" + i, rMax - rMin)) { var box = m.PickResult(i); Map.Fit(box); return; }
            if (ImGui.IsItemHovered() || i == 0) dl.AddRectFilled(rMin, rMax, ImGui.GetColorU32(NEUTRALSEL));
            float ty = rMin.Y + (rMax.Y - rMin.Y - ImGui.GetFontSize()) * 0.5f;
            var at = new Vector2(rMin.X + 8 * s, ty);
            dl.AddText(at, ImGui.GetColorU32(RED), LOCATION);
            Glyphs["result:" + i] = (LOCATION, RED);
            var (name, kind) = SplitPlace(results[i].Name);
            dl.AddText(new Vector2(rMin.X + 26 * s, ty), ImGui.GetColorU32(TXT), name);
            dl.AddText(new Vector2(rMin.X + 26 * s + ImGui.CalcTextSize(name).X + 8 * s, ty), ImGui.GetColorU32(NOTE), kind);
            Rects["result:" + i] = (rMin, rMax);
        }
    }

    /// <summary>"Kolín, town, Central Bohemia" → ("Kolín", "town, Central Bohemia") — the name in TXT, the rest (its kind) in NOTE (C-6).</summary>
    public static (string Name, string Kind) SplitPlace(string full)
    {
        int c = full.IndexOf(", ", StringComparison.Ordinal);
        return c < 0 ? (full, "") : (full.Substring(0, c), full.Substring(c + 2));
    }

    // ── ② Options (C-9) ───────────────────────────────────────────────────────────────────

    private static readonly (string Label, string Glyph, string Note)[] IncludeRows =
    {
        ("Buildings", HOUSE, "enterable houses; garages and sheds solid"), ("Trees and woods", TREE, "woods, scrub, lone trees, tree rows"),
        ("Walls and fences", FENCE, "walls, fences, hedges, retaining walls"), ("Water", WATER_G, "lakes, ponds, rivers"),
    };

    private static Vector4 IncludeColour(int i) => i switch { 0 => ORANGE, 1 => GREEN, 2 => GREY, _ => BLUE };

    private void OptionsStep(OsmWizardModel m, float s)
    {
        const float LX = 20, LY = 74;
        Separator("terrain", LX, LY, LX + 560, "Terrain", s);
        At(LX, LY + 20, s); ImGui.AlignTextToFramePadding();
        ImGui.PushStyleColor(ImGuiCol.Text, TXT); ImGui.TextUnformatted("Name"); ImGui.PopStyleColor();
        At(LX + 150, LY + 20, s);
        ImGui.SetNextItemWidth(390 * s);
        ImGui.InputText("##osm_name", ref m.Name, 64);
        Rects["field:name"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        var fieldMax = ImGui.GetItemRectMax();
        if (m.NameRefusal is { } bad) TextAt(LX + 150, LY + 64, s, RED, bad);
        else
        {
            Glyph("name.valid", m.NameExists ? WARNING : CIRCLE_CHECK, m.NameExists ? PEND : OK, new Vector2(W(LX + 552, 0, s).X, fieldMax.Y - ImGui.GetFrameHeight() + (ImGui.GetFrameHeight() - ImGui.GetFontSize()) * 0.5f));
            TextAt(LX + 150, LY + 64, s, NOTE, "Saved as " + m.SavedAs);
        }
        Separator("include", LX, LY + 92, LX + 560, "Include", s);
        bool[] on = { m.IncludeBuildings, m.IncludeTrees, m.IncludeWalls, m.IncludeWater };
        for (int i = 0; i < 4; i++)
        {
            float yy = LY + 112 + i * 28;
            At(LX + 4, yy, s);
            ImGui.Checkbox("##osm_inc" + i, ref on[i]);
            Rects["include:" + i] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            float ty = ImGui.GetItemRectMin().Y + (ImGui.GetItemRectSize().Y - ImGui.GetFontSize()) * 0.5f;
            Glyph("include:" + i, IncludeRows[i].Glyph, IncludeColour(i), new Vector2(W(LX + 30, 0, s).X, ty));
            Text(new Vector2(W(LX + 52, 0, s).X, ty), TXT, IncludeRows[i].Label);
            Text(new Vector2(W(LX + 200, 0, s).X, ty), NOTE, IncludeRows[i].Note);
        }
        m.IncludeBuildings = on[0]; m.IncludeTrees = on[1]; m.IncludeWalls = on[2]; m.IncludeWater = on[3];

        const float RX2 = LX + 600;
        Separator("defaults", RX2, LY, 1180, "Buildings without a height in OSM", s);
        At(RX2, LY + 20, s); ImGui.AlignTextToFramePadding(); ImGui.PushStyleColor(ImGuiCol.Text, TXT); ImGui.TextUnformatted("Storey height"); ImGui.PopStyleColor();
        At(RX2 + 150, LY + 20, s); ImGui.SetNextItemWidth(120 * s);
        ImGui.InputDouble("##osm_storey", ref m.StoreyHeight, 0, 0, "%.1f m");
        m.StoreyHeight = Math.Clamp(m.StoreyHeight, 2.0, 6.0);
        Rects["field:storey"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        At(RX2, LY + 48, s); ImGui.AlignTextToFramePadding(); ImGui.PushStyleColor(ImGuiCol.Text, TXT); ImGui.TextUnformatted("Default storeys"); ImGui.PopStyleColor();
        At(RX2 + 150, LY + 48, s); ImGui.SetNextItemWidth(120 * s);
        ImGui.InputInt("##osm_storeys", ref m.DefaultStoreys, 0);
        m.DefaultStoreys = Math.Clamp(m.DefaultStoreys, 1, 30);
        Rects["field:storeys"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        TextAt(RX2, LY + 92, s, NOTE, "Used only where OSM has no levels, no height");
        TextAt(RX2, LY + 106, s, NOTE, "and no known building type (≈ 1 in 3 buildings).");
        TextAt(RX2, LY + 120, s, NOTE, "Roads always come in (they cannot be switched off).");

        if (m.NameExists)   // the amber exists row (C-9)
        {
            var dl = ImGui.GetWindowDrawList();
            var rMin = W(RX2, LY + 150, s); var rMax = W(RX2 + 540, LY + 196, s);
            dl.AddRectFilled(rMin, rMax, ImGui.GetColorU32(PENDROW), 3 * s);
            dl.AddRect(rMin, rMax, ImGui.GetColorU32(PEND), 3 * s);
            Rects["exists"] = (rMin, rMax);
            Glyph("exists", WARNING, PEND, W(RX2 + 10, LY + 158, s));
            TextAt(RX2 + 32, LY + 168, s, TXT, $"A terrain named \"{m.Name}\" exists — tick Replace to overwrite it.");
            At(RX2 + 30, LY + 174, s);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(2 * s, 1 * s));
            ImGui.Checkbox("##osm_replace", ref m.Replace);
            ImGui.PopStyleVar();
            Rects["replace"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            var used = m.ScenariosUsingName;
            string uses = used.Count == 0 ? "Replace — used by no scenario" : $"Replace — used by {used.Count} scenario{(used.Count == 1 ? "" : "s")}: " + string.Join(", ", used);
            Text(new Vector2(ImGui.GetItemRectMax().X + 6 * s, ImGui.GetItemRectMin().Y + 1 * s), SUB, uses);
        }
    }

    // ── ③ Preview (C-10 … C-13) ───────────────────────────────────────────────────────────

    private OsmPreview? _countsFor;
    private int _enterable, _solid;

    private void PreviewStep(OsmWizardModel m, float s)
    {
        At(CanvasX, CanvasY, s);
        var preview = m.PreviewState == OsmRunState.Done ? m.Preview : null;
        Map.Draw(m, _tiles, new Vector2(CanvasW, CanvasH) * s, editBox: false, preview: preview ?? EmptyPreview, s);
        Rects["canvas"] = (Map.CanvasMin, Map.CanvasMax);
        if (Map.LayersRect is { } lr) Rects["layers"] = lr;
        float y = CanvasY;
        var dl = ImGui.GetWindowDrawList();
        if (m.PreviewState == OsmRunState.Running)
        {
            Separator("progress", RightX, y + 10, RightX + RightW, "Making the preview", s);
            var p = m.PreviewProgress;
            Glyph("progress", HOURGLASS, AMBER, W(RightX, y + 28, s));
            TextAt(RightX + 22, y + 39, s, SUB, string.IsNullOrEmpty(p.Message) ? p.Stage.ToString() : p.Message);
            var bMin = W(RightX, y + 50, s); var bMax = W(RightX + RightW, y + 62, s);
            dl.AddRectFilled(bMin, bMax, ImGui.GetColorU32(NEUTRAL), 2 * s);
            dl.AddRectFilled(bMin, new Vector2(bMin.X + (bMax.X - bMin.X) * (float)Math.Clamp(p.Fraction, 0, 1), bMax.Y), ImGui.GetColorU32(ACC), 2 * s);
            return;
        }
        if (m.PreviewState == OsmRunState.Failed)
        {
            var fMin = W(RightX, y + 10, s); var fMax = W(RightX + RightW, y + 70, s);
            dl.AddRectFilled(fMin, fMax, ImGui.GetColorU32(new Vector4(0.23f, 0.10f, 0.10f, 1)), 3 * s);
            dl.AddRect(fMin, fMax, ImGui.GetColorU32(ERR), 3 * s);
            Rects["failure"] = (fMin, fMax);
            Glyph("failure", WARNING, ERR, fMin + new Vector2(8, 8) * s);
            ImGui.SetCursorScreenPos(fMin + new Vector2(30, 6) * s);
            ImGui.PushTextWrapPos(fMax.X - 8 * s);
            ImGui.TextUnformatted(m.Failure ?? "the preview failed");
            ImGui.PopTextWrapPos();
            if (IconButton("retry", RightX, y + 80, 90, "Retry", null, null, BTNSEL, false, s)) m.Retry();
            return;
        }
        if (preview == null) return;

        Separator("built", RightX, y + 10, RightX + RightW, "What will be built", s);
        if (!ReferenceEquals(preview, _countsFor))
        {
            _countsFor = preview; _enterable = 0; _solid = 0;
            for (int i = 0; i < preview.Buildings.Count; i++) if (preview.Buildings[i].Enterable) _enterable++; else _solid++;
        }
        var c = preview.Counts;
        var rows = _rows ??= new (string, string, string, Vector4)[6];
        rows[0] = ("Enterable buildings", Num(_enterable), HOUSE, ORANGE);
        rows[1] = ("Solid (garages, sheds)", Num(_solid), SQUARE, GREY);
        rows[2] = ("Doors · windows", Num(c.Doors) + " · " + Num(c.Windows), DOOR, AMBER);
        rows[3] = ("Trees (woods + lone)", "≈ " + Num((int)Math.Round(c.EstimatedTrees, MidpointRounding.AwayFromZero)), TREE, GREEN);
        rows[4] = ("Roads", c.RoadKm.ToString("0", CultureInfo.InvariantCulture) + " km", ROUTE, BLUE);
        rows[5] = ("Walls · water areas", Num(c.Walls) + " · " + Num(c.WaterAreas), FENCE, GREY);
        for (int i = 0; i < 6; i++)
        {
            float yy = y + 26 + i * 22;
            Glyph("built:" + i, rows[i].Glyph, rows[i].Colour, W(RightX, yy + 2, s));
            TextAt(RightX + 22, yy + 13, s, SUB, rows[i].Label);
            var vs = ImGui.CalcTextSize(rows[i].Value);
            var vAt = new Vector2(W(RightX + RightW, 0, s).X - vs.X, W(0, yy + 13, s).Y - Ascent());
            Text(vAt, TXT, rows[i].Value, bold: true);
            Rects["built.value:" + i] = (vAt, vAt + vs);
        }

        float by = y + 168;
        Separator("budget", RightX, by, RightX + RightW, "Content budget (vs tuned load)", s);
        var (lb, lo, lt) = m.Load;
        double[] loads = { lb, lo, lt };
        string[] names = { "Buildings", "Openings", "Trees" };
        for (int i = 0; i < 3; i++)
        {
            float yy = by + 20 + i * 30;
            TextAt(RightX, yy + 12, s, SUB, names[i]);
            var bMin = W(RightX + 74, yy, s); var bMax = W(RightX + RightW, yy + 14, s);
            float bw = bMax.X - bMin.X;
            dl.AddRectFilled(bMin, bMax, ImGui.GetColorU32(NEUTRAL), 2 * s);
            dl.AddRectFilled(bMin, new Vector2(bMin.X + bw * 2 / 3f, bMax.Y), ImGui.GetColorU32(OK with { W = 0.18f }));
            dl.AddRectFilled(new Vector2(bMin.X + bw * 2 / 3f, bMin.Y), new Vector2(bMin.X + bw * 5 / 6f, bMax.Y), ImGui.GetColorU32(PEND with { W = 0.22f }));
            dl.AddRectFilled(new Vector2(bMin.X + bw * 5 / 6f, bMin.Y), bMax, ImGui.GetColorU32(ERR with { W = 0.22f }));
            dl.AddRect(bMin, bMax, ImGui.GetColorU32(BORDER), 2 * s);
            float pos = (float)OsmWizardModel.BarPosition(loads[i]);
            var fill = loads[i] > 3 ? ERR : loads[i] > 2 ? PEND : OK;
            dl.AddRectFilled(new Vector2(bMin.X, bMin.Y + 3 * s), new Vector2(bMin.X + bw * pos, bMax.Y - 3 * s), ImGui.GetColorU32(fill), 1 * s);
            Rects["bar:" + i] = (bMin, bMax);
            Rects["bar.fill:" + i] = (new Vector2(bMin.X, bMin.Y + 3 * s), new Vector2(bMin.X + bw * pos, bMax.Y - 3 * s));
            foreach (float f in Marks) dl.AddLine(new Vector2(bMin.X + bw * f, bMin.Y - 2 * s), new Vector2(bMin.X + bw * f, bMax.Y + 2 * s), ImGui.GetColorU32(SEP));
            if (i == 2)
                for (int k = 0; k < 3; k++)
                {
                    string t = MarkLabels[k]; var ts = ImGui.CalcTextSize(t);
                    var tAt = new Vector2(bMin.X + bw * Marks[k] - ts.X * 0.5f, bMax.Y + 3 * s);
                    Text(tAt, NOTE, t);
                    Rects["mark:" + k] = (tAt, tAt + ts);
                }
        }
        // the verdict box
        var verdict = preview.Verdict;
        var vMin = W(RightX, by + 122, s); var vMax = W(RightX + RightW, by + 150, s);
        var (vFill, vBorder, vGlyph, vText) = verdict switch
        {
            BudgetVerdict.Ok => (OKBOX, OK, CIRCLE_CHECK, "Within budget — ready to import"),
            BudgetVerdict.Heavy => (PENDROW, PEND, WARNING, "Heavier than HROT is tuned for" + (preview.VerdictReason is { } hr ? " — " + hr : "")),
            _ => (new Vector4(0.23f, 0.10f, 0.10f, 1), ERR, XMARK, "Too heavy — pick a smaller box" + (preview.VerdictReason is { } rr ? " (" + rr + ")" : "")),
        };
        dl.AddRectFilled(vMin, vMax, ImGui.GetColorU32(vFill), 3 * s);
        dl.AddRect(vMin, vMax, ImGui.GetColorU32(vBorder), 3 * s);
        Rects["verdict"] = (vMin, vMax);
        float vty = vMin.Y + (vMax.Y - vMin.Y - ImGui.GetFontSize()) * 0.5f;
        Glyph("verdict", vGlyph, vBorder, new Vector2(vMin.X + 8 * s, vty));
        dl.PushClipRect(vMin, vMax, true);
        Text(new Vector2(vMin.X + 30 * s, vty), TXT, vText);
        dl.PopClipRect();

        float wy = by + 172;
        var notes = preview.Warnings;
        Separator("notes", RightX, wy, RightX + RightW, $"Notes ({notes.Count})", s);
        int shown = Math.Min(notes.Count, 6);
        for (int i = 0; i < shown; i++)
        {
            float yy = wy + 18 + i * 22;
            bool warn = IsWarning(notes[i]);
            Glyph("note:" + i, warn ? WARNING : INFO_G, warn ? PEND : INFO, W(RightX + 2, yy + 2, s));
            dl.PushClipRect(W(RightX, yy - 2, s), W(RightX + RightW, yy + 20, s), true);
            TextAt(RightX + 22, yy + 13, s, SUB, notes[i]);
            dl.PopClipRect();
            Rects["note:" + i] = (W(RightX, yy - 2, s), W(RightX + RightW, yy + 19, s));
        }
        TextAt(RightX, CanvasY + CanvasH - 4, s, NOTE, "Data: " + preview.Attribution);
    }

    private static readonly float[] Marks = { 1 / 3f, 2 / 3f, 5 / 6f };
    private static readonly string[] MarkLabels = { "1×", "2×", "3×" };
    private (string Label, string Value, string Glyph, Vector4 Colour)[]? _rows;

    /// <summary>A note's severity from its words (⚠ the contract's <c>Warnings</c> carry none): something dropped, refused, failed or skipped is a warning; the rest is information.</summary>
    public static bool IsWarning(string note)
        => note.Contains("dropped", StringComparison.OrdinalIgnoreCase) || note.Contains("refused", StringComparison.OrdinalIgnoreCase)
        || note.Contains("failed", StringComparison.OrdinalIgnoreCase) || note.Contains("skipped", StringComparison.OrdinalIgnoreCase);

    private static readonly OsmPreview EmptyPreview = new()
    {
        Frame = default, Bounds = new double[4], Counts = new OsmCounts(0, 0, 0, 0, 0, 0, 0, 0, 0), Verdict = BudgetVerdict.Ok, Warnings = Array.Empty<string>(),
        Roads = Array.Empty<PreviewRoad>(), Buildings = Array.Empty<PreviewBuilding>(), Areas = Array.Empty<PreviewArea>(), Lines = Array.Empty<PreviewLine>(), Attribution = "",
    };

    /// <summary>"1 412" — thousands with a space, as the mockup.</summary>
    public static string Num(int n) => n.ToString("#,0", CultureInfo.InvariantCulture).Replace(',', ' ');

    // ── ④ Import (C-14) ───────────────────────────────────────────────────────────────────

    private void ImportStep(OsmWizardModel m, float s)
    {
        const float LX = 20, LY = 76;
        var dl = ImGui.GetWindowDrawList();
        Separator("building", LX, LY, LX + 700, $"Building the terrain \"{m.Name}\"", s);
        var stages = m.Stages;
        for (int i = 0; i < stages.Count; i++)
        {
            var row = stages[i];
            float yy = LY + 20 + i * 30;
            var (g, gc) = row.State switch
            {
                OsmRunState.Done => (CIRCLE_CHECK, OK),
                OsmRunState.Running => (HOURGLASS, AMBER),
                OsmRunState.Failed => (XMARK, ERR),
                _ => (CIRCLE, DIS),
            };
            Glyph("stage:" + i, g, gc, W(LX + 2, yy + 3, s));
            TextAt(LX + 26, yy + 15, s, row.State == OsmRunState.Idle ? DIS : TXT, row.Label);
            var bMin = W(LX + 250, yy + 4, s); var bMax = W(LX + 550, yy + 16, s);
            dl.AddRectFilled(bMin, bMax, ImGui.GetColorU32(NEUTRAL), 2 * s);
            dl.AddRect(bMin, bMax, ImGui.GetColorU32(BORDER), 2 * s);
            float f = row.State == OsmRunState.Done ? 1f : (float)row.Fraction;
            if (f > 0) dl.AddRectFilled(bMin, new Vector2(bMin.X + (bMax.X - bMin.X) * f, bMax.Y), ImGui.GetColorU32(row.State == OsmRunState.Done ? OK : ACC), 2 * s);
            Rects["stage.bar:" + i] = (bMin, bMax);
            if (row.Produced.Length > 0) TextAt(LX + 562, yy + 15, s, NOTE, row.Produced);
        }
        string time = "Elapsed " + Clock(m.Elapsed) + (m.Remaining is { } r ? " · about " + Minutes(r) + " left" : "");
        TextAt(LX, LY + 270, s, SUB, time);
        if (m.ImportState == OsmRunState.Failed && m.Failure is { } why) TextAt(LX, LY + 292, s, ERR, why);

        // the finished box (right) — or, without an importer, the input-set line
        const float DX = 780, DY = 76;
        var dMin = W(DX, DY, s); var dMax = W(DX + 400, DY + 236, s);
        dl.AddRectFilled(dMin, dMax, ImGui.GetColorU32(POPUP), 4 * s);
        dl.AddRect(dMin, dMax, ImGui.GetColorU32(BORDER), 4 * s);
        Rects["done"] = (dMin, dMax);
        if (m.ImportState != OsmRunState.Done)
        {
            TextAt(DX + 12, DY + 20, s, NOTE, "When it finishes");
            TextAt(DX + 12, DY + 42, s, DIS, "the terrain opens in New Scenario from here");
            return;
        }
        if (m.TerrainFolder is { } folder)
        {
            Glyph("done", CIRCLE_CHECK, OK, W(DX + 14, DY + 36, s));
            TextAt(DX + 42, DY + 52, s, TXT, $"Terrain \"{m.Name}\" is ready", bold: true);
            var p = m.Preview;
            string facts = p == null ? "" : Num(p.Counts.Buildings) + " buildings · " + p.Counts.RoadKm.ToString("0", CultureInfo.InvariantCulture) + " km of roads · " + WebMercator.SizeText(m.BoxWidthM, m.BoxHeightM);
            TextAt(DX + 42, DY + 72, s, SUB, facts);
            TextAt(DX + 42, DY + 86, s, SUB, p?.Attribution ?? "");
            if (IconButton("open", DX + 42, DY + 112, 190, "Open in New Scenario", FILE, BLUE, BTNSEL, m.Services.OpenNewScenario == null, s))
            { m.Services.OpenNewScenario!(folder); ImGui.CloseCurrentPopup(); IsOpen = false; }
            if (IconButton("folder", DX + 240, DY + 112, 110, "Show folder", FOLDER_OPEN, AMBER, null, m.Services.ShowFolder == null, s)) m.Services.ShowFolder!(folder);
        }
        else
        {
            TextAt(DX + 12, DY + 20, s, NOTE, "Until the terrain importer is built:");
            Glyph("inputset", INFO_G, INFO, W(DX + 14, DY + 30, s));
            TextAt(DX + 34, DY + 42, s, SUB, "Input set written to " + (m.InputSetFolder ?? ""));
            TextAt(DX + 34, DY + 56, s, SUB, "— the terrain importer is not built yet.");
            if (IconButton("folder", DX + 34, DY + 84, 110, "Show folder", FOLDER_OPEN, AMBER, null, m.Services.ShowFolder == null || m.InputSetFolder == null, s)) m.Services.ShowFolder!(m.InputSetFolder!);
        }
    }

    private static string Clock(TimeSpan t) => ((int)t.TotalMinutes).ToString(CultureInfo.InvariantCulture) + ":" + t.Seconds.ToString("00", CultureInfo.InvariantCulture);
    private static string Minutes(TimeSpan t) => t.TotalMinutes < 1.5 ? "a minute" : ((int)Math.Round(t.TotalMinutes)).ToString(CultureInfo.InvariantCulture) + " minutes";

    public void Dispose() => _tiles?.Dispose();
}
