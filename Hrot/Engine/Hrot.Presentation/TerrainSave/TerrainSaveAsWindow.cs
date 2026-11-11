using System.Numerics;
using Fdp.Presentation.Fonts;
using ImGuiNET;
using static Hrot.UI.Common.TerrainOsm.OsmWizardStyle;

namespace Hrot.UI.Common.TerrainSave;

/// <summary>
/// ⭐⭐ Terrain authoring U-5 (docs/DESIGN_Terrain_Authoring.md §13 W-3/W-4, §14; the APPROVED mockup <c>docs/UX/terrain-authoring-4-save.svg</c>, R-4203) — <b>the modal "Save terrain as"</b> and its overwrite confirmation, laid out at the
/// mockup generator's coordinates (<c>scripts/gen-terrain-authoring-mockups.py</c> page 4) scaled by the font, in the colours of the OSM wizard's frame (<see cref="OsmWizardStyle"/>). It DRAWS <see cref="TerrainSaveAsModel"/> (the logic, pure).
/// ⭐ Every element is recorded in <see cref="Rects"/> / <see cref="Glyphs"/> — the conformance rail measures them (alignment, glyph AND colour — R-4138/R-4139). Hosted by <c>WindowManager.RegisterFrameOverlay(Draw)</c>, as the other modals.
/// ⚠ Not built here (premises that fail, see the status file): the terrain picker's SOURCE badge and <i>Edit terrain…</i> — opening a scenario from a terrain's own <c>source/</c> folder needs the scenario door to load by folder and the terrain catalogue to resolve <c>source/base</c> (backend's
/// note: "not built"); <i>Save terrain</i> (re-saving over the source's own terrain) joins when a source can be opened.
/// </summary>
public sealed class TerrainSaveAsWindow
{
    public const string Title = "Save terrain as";
    public const string PopupId = Title + "##terrain_save_as";

    private readonly Func<ITerrainSaveService?> _service;
    private bool _wantOpen;

    public TerrainSaveAsWindow(Func<ITerrainSaveService?> service) => _service = service ?? throw new ArgumentNullException(nameof(service));

    public TerrainSaveAsModel? Model { get; private set; }
    public bool IsOpen { get; private set; }

    /// <summary>Raised once after a successful save (the host loads the new scenario, toasts the result).</summary>
    public event Action<TerrainSaveOutcome>? Saved;

    /// <summary>Every element's rectangle last frame (screen px), by name — the conformance rail's measurement.</summary>
    public readonly Dictionary<string, (Vector2 Min, Vector2 Max)> Rects = new();
    /// <summary>Every named glyph drawn last frame with its colour (R-4139).</summary>
    public readonly Dictionary<string, (string Glyph, Vector4 Colour)> Glyphs = new();

    /// <summary>Why the menu item is greyed (null = it opens): no scenario with blocks loaded, or a host with no terrain service.</summary>
    public string? DisabledReason
    {
        get
        {
            var sv = _service();
            if (sv == null) return "this host cannot write terrains";
            return sv.SourceScenarioName == null ? "no scenario is loaded" : null;
        }
    }

    /// <summary>⭐ E-2 — why File ▸ Save Terrain is greyed (null = it opens): it bakes a terrain SOURCE back over its terrain, so a scenario that is not one cannot.</summary>
    public string? SaveDisabledReason
    {
        get
        {
            var sv = _service();
            if (sv == null) return "this host cannot write terrains";
            return TerrainSourceNames.TerrainOf(sv.SourceScenarioName) == null ? "this is not a terrain source (File ▸ Edit Terrain… opens one)" : null;
        }
    }

    /// <summary>File ▸ Save Terrain: the same dialog, on the terrain this source came from — its name is the terrain's, so saving asks before it replaces it (W-4).</summary>
    public void OpenSave() { if (SaveDisabledReason == null) Open(); }

    /// <summary>File ▸ Save Terrain As…: opens the dialog on a fresh model.</summary>
    public void Open()
    {
        var sv = _service();
        if (sv == null || sv.SourceScenarioName == null) return;
        Model = new TerrainSaveAsModel(sv);
        _wantOpen = true;
    }

    /// <summary>Opens on a model a rail built.</summary>
    public void OpenWith(TerrainSaveAsModel model) { Model = model; _wantOpen = true; }

    private static Vector2 W(float x, float y, float s) => ImGui.GetWindowPos() + new Vector2(x, y) * s;
    private static void At(float x, float y, float s) => ImGui.SetCursorScreenPos(W(x, y, s));
    private static float Ascent() { unsafe { return ImGui.GetFont().NativePtr->Ascent * ImGui.GetFontSize() / ImGui.GetFont().FontSize; } }
    private void TextAt(float x, float y, float s, Vector4 colour, string text) => ImGui.GetWindowDrawList().AddText(W(x, y, s) - new Vector2(0, Ascent()), ImGui.GetColorU32(colour), text);

    private void Glyph(string key, string glyph, Vector4 colour, float x, float y, float s)
    {
        var at = W(x, y, s);
        ImGui.GetWindowDrawList().AddText(at, ImGui.GetColorU32(colour), glyph);
        Glyphs[key] = (glyph, colour);
        Rects["glyph:" + key] = (at, at + ImGui.CalcTextSize(glyph));
    }

    private void Separator(float x, float y, float x1, string label, float s)
    {
        var p = W(x, y + 12, s) - new Vector2(0, Ascent());
        ImGui.GetWindowDrawList().AddText(p, ImGui.GetColorU32(SEPTEXT), label);
        var ts = ImGui.CalcTextSize(label);
        float ly = W(0, y + 8, s).Y;
        ImGui.GetWindowDrawList().AddLine(new Vector2(p.X + ts.X + 8 * s, ly), new Vector2(W(x1, 0, s).X, ly), ImGui.GetColorU32(SEP));
        Rects["sep:" + label] = (p, new Vector2(W(x1, 0, s).X, p.Y + ts.Y));
    }

    private bool Button(string key, float x, float y, float w, string label, string? glyph, Vector4? glyphColour, Vector4? fill, bool disabled, float s)
    {
        At(x, y, s);
        if (disabled) ImGui.BeginDisabled();
        var f = fill ?? BTN;   // the mockup's plain button is the dark blue, never the theme's bright one
        ImGui.PushStyleColor(ImGuiCol.Button, f); ImGui.PushStyleColor(ImGuiCol.ButtonHovered, (f * 1.12f) with { W = 1 });
        string text = glyph == null ? label : "    " + label;   // room for the glyph at the left of the label
        bool clicked = ImGui.Button(text + "##btn_" + key, new Vector2(w, 26) * s);
        ImGui.PopStyleColor(2);
        var min = ImGui.GetItemRectMin(); var max = ImGui.GetItemRectMax();
        if (disabled) ImGui.EndDisabled();
        if (glyph != null) Glyph("btn:" + key, glyph, disabled ? DIS : glyphColour ?? SUB, 0, 0, 1f, min.X + 10 * s, min.Y + (max.Y - min.Y - ImGui.GetFontSize()) * 0.5f);
        Rects["btn:" + key] = (min, max);
        return clicked && !disabled;
    }

    private void Glyph(string key, string glyph, Vector4 colour, float _, float __, float ___, float absX, float absY)
    {
        ImGui.GetWindowDrawList().AddText(new Vector2(absX, absY), ImGui.GetColorU32(colour), glyph);
        Glyphs[key] = (glyph, colour);
        Rects["glyph:" + key] = (new Vector2(absX, absY), new Vector2(absX, absY) + ImGui.CalcTextSize(glyph));
    }

    /// <summary>The frame overlay: draws the modal (and its overwrite confirmation) while open.</summary>
    public void Draw()
    {
        var m = Model;
        if (m == null) return;
        if (_wantOpen) { ImGui.OpenPopup(PopupId); _wantOpen = false; }
        float s = ImGui.GetFontSize() / 14f;
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowSize(new Vector2(560, 470) * s, ImGuiCond.Always);
        ImGui.SetNextWindowPos(vp.WorkPos + vp.WorkSize * 0.5f, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        bool open = true;
        IsOpen = ImGui.BeginPopupModal(PopupId, ref open, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoSavedSettings);
        if (!IsOpen) return;
        Rects.Clear(); Glyphs.Clear();
        Rects["window"] = (ImGui.GetWindowPos(), ImGui.GetWindowPos() + ImGui.GetWindowSize());
        m.Refresh();
        Form(m, s);
        bool cancel = Footer(m, s);
        if (m.Step == TerrainSaveStep.ConfirmOverwrite) Confirm(m, s);
        bool done = m.Step == TerrainSaveStep.Done && m.Outcome != null;
        if (done) Saved?.Invoke(m.Outcome!);
        if (done || cancel || !open) { ImGui.CloseCurrentPopup(); IsOpen = false; Model = null; }
        ImGui.EndPopup();
    }

    private void Form(TerrainSaveAsModel m, float s)
    {
        // Name — the mockup's form_row: label at x16, field at x136 (w 300), the valid / warning mark at x444
        At(16, 36, s); ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted("Name");
        At(136, 36, s);
        ImGui.SetNextItemWidth(300 * s);
        string name = m.Name;
        if (ImGui.InputText("##ts_name", ref name, 64)) m.Name = name;
        Rects["name"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        var fieldMax = ImGui.GetItemRectMax();
        if (m.NameOk && m.Error == null) Glyph("name.valid", IconsFontAwesome6.CircleCheck, OK, 444, 40, s);
        else Glyph("name.valid", IconsFontAwesome6.TriangleExclamation, m.NameOk ? ERR : PEND, 444, 40, s);
        TextAt(136, 72, s, NOTE, m.NameProblem is { } why ? why : m.PathLine);

        Separator(16, 92, 544, "Baked into the terrain", s);
        if (m.Preview is { } p)
        {
            Row("blocks", 0, IconsFontAwesome6.House, ORANGE, $"{p.Blocks} blocks → {p.Houses} houses", s);
            Row("woods", 1, IconsFontAwesome6.Mountain, GREEN, $"{p.Woods} woods, {p.Trees} trees", s);
            Row("walls", 2, IconsFontAwesome6.Route, GREY, $"{p.WallKm:0.#} km of walls, fences and hedges", s);
            Row("bridges", 3, IconsFontAwesome6.Route, BLUE, $"{p.Bridges} bridges", s);
        }
        else TextAt(42, 127, s, m.Error != null ? ERR : NOTE, m.Error ?? "-");

        Separator(16, 210, 544, "Stays in the source only", s);
        if (m.Preview is { } q)
        {
            Glyph("units", IconsFontAwesome6.Users, BLUE, 20, 234, s); TextAt(42, 245, s, SUB, $"{q.UnitsNotBaked} units and {q.AreasNotBaked} areas — not terrain (E-4)");
            Glyph("damage", IconsFontAwesome6.TriangleExclamation, RED, 20, 256, s); TextAt(42, 267, s, SUB, $"damage to {q.DamagedHousesNotBaked} houses from a test run — not baked (E-6)");
        }

        Separator(16, 296, 544, "Then", s);
        At(18, 318, s);
        bool mesh = m.PrebuildMesh;
        if (ImGui.Checkbox("##ts_mesh", ref mesh)) m.PrebuildMesh = mesh;
        Rects["mesh"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        TextAt(42, 332, s, SUB, "Pre-build the walkable mesh");
        TextAt(250, 332, s, NOTE, m.MeshEstimate);
        At(18, 346, s);
        bool scen = m.CreateScenario;
        if (ImGui.Checkbox("##ts_scen", ref scen)) m.CreateScenario = scen;
        Rects["scenario"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        TextAt(42, 360, s, SUB, "Create a scenario on it:");
        At(238, 346, s);
        ImGui.SetNextItemWidth(190 * s);
        string sn = m.ScenarioName;
        if (!m.CreateScenario) ImGui.BeginDisabled();
        if (ImGui.InputText("##ts_scen_name", ref sn, 64)) m.ScenarioName = sn;
        if (!m.CreateScenario) ImGui.EndDisabled();
        Rects["scenario.name"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        TextAt(44, 384, s, NOTE, "empty of the baked blocks; units are not copied (E-5)");
        _ = fieldMax;
    }

    private void Row(string key, int i, string glyph, Vector4 colour, string text, float s)
    {
        Glyph(key, glyph, colour, 20, 116 + i * 22, s);
        TextAt(42, 127 + i * 22, s, TXT, text);
    }

    private bool Footer(TerrainSaveAsModel m, float s)
    {
        if (Button("save", 560 - 250, 470 - 32, 130, "Save terrain", IconsFontAwesome6.Mountain, OK, BTNSEL, !m.CanSave, s)) m.Save();
        bool cancel = Button("cancel", 560 - 110, 470 - 32, 90, "Cancel", null, null, null, false, s);
        if (m.Error != null && m.Preview != null) TextAt(16, 470 - 12, s, ERR, m.Error);
        return cancel;
    }

    /// <summary>The overwrite confirmation (W-4): what it replaces and which scenarios will see the change.</summary>
    private void Confirm(TerrainSaveAsModel m, float s)
    {
        string id = $"Save terrain {m.Name}?##terrain_save_confirm";
        ImGui.OpenPopup(id);
        ImGui.SetNextWindowSize(new Vector2(280, 230) * s, ImGuiCond.Always);
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(vp.WorkPos + vp.WorkSize * 0.5f, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        bool open = true;
        if (!ImGui.BeginPopupModal(id, ref open, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoSavedSettings)) { if (!open) m.CancelReplace(); return; }
        Rects["confirm.window"] = (ImGui.GetWindowPos(), ImGui.GetWindowPos() + ImGui.GetWindowSize());
        Glyph("confirm.warn", IconsFontAwesome6.TriangleExclamation, PEND, 14, 34, s);
        TextAt(40, 46, s, TXT, "It replaces the terrain this");
        TextAt(40, 64, s, TXT, "source made earlier.");
        TextAt(40, 100, s, TXT, m.Users.Count == 1 ? "Used by 1 scenario:" : $"Used by {m.Users.Count} scenarios:");
        for (int i = 0; i < Math.Min(m.Users.Count, 4); i++) TextAt(52, 126 + i * 18, s, SUB, "• " + m.Users[i]);
        if (m.Users.Count > 4) TextAt(52, 126 + 4 * 18, s, NOTE, $"… and {m.Users.Count - 4} more");
        if (Button("replace", 70, 196, 110, "Replace", null, null, BTNSEL, false, s)) { m.ConfirmReplace(); ImGui.CloseCurrentPopup(); }
        if (Button("confirm-cancel", 186, 196, 80, "Cancel", null, null, null, false, s)) { m.CancelReplace(); ImGui.CloseCurrentPopup(); }
        ImGui.EndPopup();
    }
}
