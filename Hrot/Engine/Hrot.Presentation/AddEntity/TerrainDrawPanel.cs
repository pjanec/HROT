using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common;
using ImGuiNET;
using NodeEditor.Core.Widgets;
using static Hrot.UI.Common.TerrainOsm.OsmWizardStyle;

namespace Hrot.UI.Common.AddEntity;

/// <summary>
/// ⭐⭐ Terrain authoring follow-up 2 (docs/DESIGN_Terrain_Authoring.md §15a; mockup 3 ①②③) — what the user chose for the terrain feature being DRAWN: a wall / fence / hedge's kind and height, a bridge's width, a scatter's recipe, density and seed. ONE object the drawing panel edits and the
/// commit reads (<see cref="Born"/>), so the panel and the entity can never disagree. Pure.
/// </summary>
public sealed class TerrainDrawOptions
{
    /// <summary>Wall, fence, hedge: the three kinds a LINE can be switched between (the mockup's Kind row).</summary>
    public static readonly (byte Kind, string Name)[] LineKinds = { (TerrainFeature.Hedge, "Hedge"), (TerrainFeature.Wall, "Wall"), (TerrainFeature.Fence, "Fence") };
    /// <summary>The density row (the mockup's Low · Normal · High) as multiples of the recipe's own items per 100 m².</summary>
    public static readonly (string Name, float Factor)[] Densities = { ("Low", 0.5f), ("Normal", 1f), ("High", 2f) };

    private readonly string _spec;

    private TerrainDrawOptions(string spec, byte kind, string template, TerrainShape shape, uint seed) { _spec = spec; Kind = kind; Template = template; Shape = shape; Seed = seed; }

    /// <summary>The options of a <c>feature:&lt;kind&gt;:&lt;template&gt;:&lt;Shape&gt;</c> spec of a LINE or an AREA; null for anything else (a point is one click and has nothing to set).</summary>
    public static TerrainDrawOptions? From(string? spec)
        => TerrainBirth.FeatureOf(spec) is { Shape: not TerrainShape.Point } f ? new TerrainDrawOptions(spec!, f.Kind, f.Template, f.Shape, TerrainBirth.NewSeed()) : null;

    public byte Kind { get; private set; }
    /// <summary>The material of a wall / fence / hedge (empty = the kind's), a scatter's recipe id.</summary>
    public string Template { get; private set; }
    public TerrainShape Shape { get; }
    /// <summary>Metres: a wall / fence / hedge's height, a bridge's deck width; 0 = the kind's default.</summary>
    public float Height { get; set; }
    public uint Seed { get; private set; }
    /// <summary>Scatter only: the Low · Normal · High choice (an index into <see cref="Densities"/>).</summary>
    public int Density { get; set; } = 1;
    /// <summary>Scatter only: the recipes that can be scattered, (id, name, items per 100 m²) — filled by the host from the recipes that carry a <c>scatter</c> section.</summary>
    public IReadOnlyList<(string Id, string Name, float Density)> ScatterRecipes { get; set; } = Array.Empty<(string, string, float)>();

    public bool IsLine => Shape == TerrainShape.Line && Kind is TerrainFeature.Wall or TerrainFeature.Fence or TerrainFeature.Hedge;
    public bool IsBridge => Kind == TerrainFeature.Bridge;
    public bool IsScatter => Kind == TerrainFeature.Scatter;
    /// <summary>Whether a panel has anything to ask: the line kinds, the bridge, the scatter. A wood, scrub or water area is just drawn.</summary>
    public bool HasPanel => IsLine || IsBridge || IsScatter;

    public string KindName { get { foreach (var (k, n) in LineKinds) if (k == Kind) return n; return IsBridge ? "Bridge" : IsScatter ? "Scatter" : "Terrain feature"; } }

    public void SetKind(byte kind) { if (kind != Kind) { Kind = kind; Height = 0f; } }
    public void SetTemplate(string template) => Template = template;
    public void Reroll() => Seed = TerrainBirth.NewSeed();

    /// <summary>The height the kind gives when none was typed (metres).</summary>
    public float DefaultHeight => Kind switch { TerrainFeature.Wall => FeatureExpander.DefaultWallHeight, TerrainFeature.Fence => FeatureExpander.DefaultFenceHeight, TerrainFeature.Hedge => FeatureExpander.DefaultHedgeHeight, TerrainFeature.Bridge => FeatureExpander.DefaultBridgeWidth, _ => 0f };
    public float HeightShown => Height > 0f ? Height : DefaultHeight;

    /// <summary>A scatter's items per 100 m²: the chosen recipe's own density × the density row's factor.</summary>
    public float ItemsPer100m2
    {
        get
        {
            foreach (var r in ScatterRecipes) if (r.Id == Template) return r.Density * Densities[Math.Clamp(Density, 0, Densities.Length - 1)].Factor;
            return 0f;
        }
    }

    /// <summary>The entity this drawing is born as: the area style dropped (a terrain feature is no map overlay), the <see cref="TerrainFeature"/> with the panel's kind, height / width, recipe, density and seed, and its name.</summary>
    public void Born(List<object> comps)
    {
        comps.RemoveAll(c => c is Hrot.Map.Common.Components.MapOverlayStyle);
        TerrainBirth.BornWith(TkbEntityTypes.TerrainFeature, _spec, Hrot.ScenarioEditor.Gizmos.EntityPlacementGizmo.FacingNorth, comps, Seed);
        int i = comps.FindIndex(c => c is TerrainFeature);
        if (i < 0) return;
        var f = (TerrainFeature)comps[i];
        f.Kind = Kind; f.Template = new FixedString32(Template); f.Seed = Seed;
        f.Pad = IsScatter ? ItemsPer100m2 : Height;
        comps[i] = f;
        int n = comps.FindIndex(c => c is EntityInfo);
        if (n >= 0) { var info = (EntityInfo)comps[n]; info.Name = new FixedString64(TerrainBirth.FeatureName((Kind, Template, Shape))); comps[n] = info; }
    }

    /// <summary>Metres along the placed points.</summary>
    public static float Length(IReadOnlyList<Vector2> points)
    {
        float len = 0f;
        for (int i = 1; i < points.Count; i++) len += Vector2.Distance(points[i - 1], points[i]);
        return len;
    }

    /// <summary>Square metres of the polygon the points make (0 for fewer than three).</summary>
    public static float Area(IReadOnlyList<Vector2> points)
    {
        if (points.Count < 3) return 0f;
        float a = 0f;
        for (int i = 0; i < points.Count; i++) { var p = points[i]; var q = points[(i + 1) % points.Count]; a += p.X * q.Y - q.X * p.Y; }
        return MathF.Abs(a) * 0.5f;
    }

    /// <summary>A scatter's expected item count: area / 100 × the density, capped at the engine's 2 000 (T-2).</summary>
    public float ItemsOf(IReadOnlyList<Vector2> points) => Math.Min(2000f, Area(points) / 100f * ItemsPer100m2);
}

/// <summary>What the drawing panel reads and drives: the options of the feature being drawn (null when no terrain feature is), the points placed so far, and the two ways out.</summary>
public interface ITerrainDrawSource
{
    TerrainDrawOptions? Options { get; }
    IReadOnlyList<Vector2> Points { get; }
    /// <summary>The panel's <i>Done</i> (Enter on the map).</summary>
    void Finish();
    /// <summary>The panel's <i>Cancel</i> (Esc on the map).</summary>
    void Cancel();
}

/// <summary>
/// ⭐⭐ Terrain authoring follow-up 2 (mockup 3 ① ② ③, R-4138) — the small panel that stands beside the map while a terrain feature is DRAWN: <b>① a wall / fence / hedge</b> (<i>Hedge — click to add a point</i>, the Kind row, the Height field, the length and <i>Enter ends</i>), <b>② a bridge</b>
/// (<i>Bridge — spans 46 m</i>, what it makes, what is later), <b>③ the area scatter</b> (<i>Scatter in this area</i>: What, Density, ≈ items · seed, the dice, Done / Cancel). It edits the <see cref="TerrainDrawOptions"/> the commit reads; it is a frame overlay the editor registers and it draws
/// nothing when no such tool is armed. ⛔ The scatter's <i>What</i> lists only recipes that carry a <c>scatter</c> section — none ships yet (backend's T-3), so the scatter pick is greyed and this panel is not reached in production; a rail drives it with a test recipe.
/// </summary>
public sealed class TerrainDrawPanel
{
    public const string WindowId = "##terrain_draw_panel";
    private readonly Func<ITerrainDrawSource?> _source;
    private string _lenText = "", _itemsText = ""; private int _lenKey = -1, _itemsKey = -1;

    public TerrainDrawPanel(Func<ITerrainDrawSource?> source) => _source = source ?? throw new ArgumentNullException(nameof(source));

    /// <summary>Where each element was drawn last frame (a rail's measure).</summary>
    public readonly Dictionary<string, (Vector2 Min, Vector2 Max)> Rects = new();
    public readonly Dictionary<string, (string Glyph, Vector4 Colour)> Glyphs = new();
    /// <summary>The panel's lines as last drawn (title, then the rows' texts): what a rail compares with the mockup's words.</summary>
    public readonly List<string> Lines = new();

    /// <summary>The frame overlay (<c>RegisterFrameOverlay</c>): draws the panel while a terrain feature with something to ask is being drawn.</summary>
    public void Draw()
    {
        var src = _source();
        var o = src?.Options;
        if (src is null || o is null || !o.HasPanel) { Rects.Clear(); return; }
        var vp = ImGui.GetMainViewport();
        float s = ImGui.GetFontSize() / 14f;
        ImGui.SetNextWindowPos(new Vector2(vp.WorkPos.X + 24f * s, vp.WorkPos.Y + vp.WorkSize.Y - 40f * s), ImGuiCond.Always, new Vector2(0f, 1f));
        ImGui.SetNextWindowSize(new Vector2(260f * s, 0f), ImGuiCond.Always);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, POPUP with { W = 0.96f }); ImGui.PushStyleColor(ImGuiCol.Border, o.IsScatter ? AMBER : ACC);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 4f * s); ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f); ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12f, 10f) * s);
        bool open = ImGui.Begin(WindowId, ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse);
        if (open)
        {
            Rects.Clear(); Glyphs.Clear(); Lines.Clear();
            Rects["panel"] = (ImGui.GetWindowPos(), ImGui.GetWindowPos() + ImGui.GetWindowSize());
            if (o.IsLine) Line(src, o, s); else if (o.IsBridge) Bridge(src, o, s); else Scatter(src, o, s);
        }
        ImGui.End();
        ImGui.PopStyleVar(3); ImGui.PopStyleColor(2);
    }

    private void Title(string glyphKey, string text)
    {
        var icon = Icons.Get(glyphKey);
        ImGui.TextColored(icon.Color, icon.Glyph); Glyphs["title"] = (icon.Glyph, icon.Color);
        ImGui.SameLine(); ImGui.TextUnformatted(text); Lines.Add(text);
    }

    private string Len(IReadOnlyList<Vector2> pts)
    {
        int key = (int)Math.Round(TerrainDrawOptions.Length(pts)) * 64 + pts.Count;
        if (key != _lenKey) { _lenKey = key; _lenText = $"{TerrainDrawOptions.Length(pts):0} m"; }
        return _lenText;
    }

    private void Line(ITerrainDrawSource src, TerrainDrawOptions o, float s)
    {
        var pts = src.Points;
        string title = $"{o.KindName} — " + (pts.Count == 0 ? "click to start" : "click to add a point");
        Title("terrain/walls", title);
        ImGui.Spacing();
        // Kind
        float rowY = ImGui.GetCursorPosY();
        ImGui.AlignTextToFramePadding(); ImGui.TextColored(SUB, "Kind");
        float x0 = 60f * s;
        for (int i = 0; i < TerrainDrawOptions.LineKinds.Length; i++)
        {
            ImGui.SameLine(x0 + i * 62f * s);
            var (k, name) = TerrainDrawOptions.LineKinds[i];
            bool sel = o.Kind == k;
            ImGui.PushStyleColor(ImGuiCol.Button, sel ? BTNSEL : BTN);
            if (sel) { ImGui.PushStyleColor(ImGuiCol.Border, ACC); ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f); }
            if (ImGui.Button(name + "##kind_" + k, new Vector2(58f * s, 22f * s))) o.SetKind(k);
            if (sel) { ImGui.PopStyleVar(); ImGui.PopStyleColor(); }
            ImGui.PopStyleColor();
            Rects["kind:" + name] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        }
        // Height
        ImGui.AlignTextToFramePadding(); ImGui.TextColored(SUB, "Height");
        ImGui.SameLine(x0); ImGui.SetNextItemWidth(60f * s);
        float h = o.HeightShown;
        if (ImGui.InputFloat("##height", ref h, 0f, 0f, "%.1f m") && h > 0f && h < 50f) o.Height = h;
        Rects["height"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        ImGui.SameLine(x0 + 70f * s); ImGui.AlignTextToFramePadding();
        string tail = (pts.Count > 1 ? Len(pts) + " · " : "") + "Enter ends";
        ImGui.TextColored(NOTE, tail); Lines.Add(tail);
    }

    private void Bridge(ITerrainDrawSource src, TerrainDrawOptions o, float s)
    {
        var pts = src.Points;
        string title = pts.Count < 2 ? "Bridge — click the two ends" : $"Bridge — spans {TerrainDrawOptions.Length(pts):0} m";
        Title("terrain/bridge", title);
        ImGui.Spacing();
        ImGui.TextColored(SUB, "deck + ramps, joined to both banks"); Lines.Add("deck + ramps, joined to both banks");
        // what the engine does not do yet is said, not drawn: backend's T-7 (CE-4423) makes the bridge a road edge at save
        ImGui.TextColored(NOTE, "road edge at save: later (T-7, CE-4423)"); Lines.Add("road edge at save: later (T-7, CE-4423)");
    }

    private void Scatter(ITerrainDrawSource src, TerrainDrawOptions o, float s)
    {
        var pts = src.Points;
        Title("terrain/clutter", "Scatter in this area");
        ImGui.Spacing();
        float x0 = 66f * s;
        ImGui.AlignTextToFramePadding(); ImGui.TextColored(SUB, "What");
        ImGui.SameLine(x0); ImGui.SetNextItemWidth(170f * s);
        string current = "";
        foreach (var r in o.ScatterRecipes) if (r.Id == o.Template) current = r.Name;
        if (ImGui.BeginCombo("##what", current))
        {
            foreach (var r in o.ScatterRecipes) if (ImGui.Selectable(r.Name, r.Id == o.Template)) o.SetTemplate(r.Id);
            ImGui.EndCombo();
        }
        Rects["what"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        ImGui.AlignTextToFramePadding(); ImGui.TextColored(SUB, "Density");
        for (int i = 0; i < TerrainDrawOptions.Densities.Length; i++)
        {
            ImGui.SameLine(x0 + i * 54f * s);
            bool sel = o.Density == i;
            ImGui.PushStyleColor(ImGuiCol.Button, sel ? BTNSEL : BTN);
            if (ImGui.Button(TerrainDrawOptions.Densities[i].Name + "##dens_" + i, new Vector2(50f * s, 22f * s))) o.Density = i;
            ImGui.PopStyleColor();
            Rects["density:" + TerrainDrawOptions.Densities[i].Name] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        }
        int key = (int)Math.Round(o.ItemsOf(pts)) * 8 + o.Density + (int)(o.Seed % 100000) * 100000;
        if (key != _itemsKey) { _itemsKey = key; _itemsText = $"≈ {o.ItemsOf(pts):0} items · seed {o.Seed}"; }
        ImGui.AlignTextToFramePadding(); ImGui.TextColored(SUB, _itemsText); Lines.Add(_itemsText);
        ImGui.SameLine(x0 + 150f * s);
        var dice = Icons.Get("terrain/reroll");
        ImGui.PushStyleColor(ImGuiCol.Text, dice.Color);
        if (ImGui.Button(dice.Glyph + "##dice", new Vector2(30f * s, 22f * s))) o.Reroll();
        ImGui.PopStyleColor();
        Rects["dice"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax()); Glyphs["dice"] = (dice.Glyph, dice.Color);
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Button, BTNSEL);
        if (ImGui.Button("Done##done", new Vector2(80f * s, 22f * s)) && pts.Count >= 3) src.Finish();
        ImGui.PopStyleColor();
        Rects["done"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        ImGui.SameLine(0, 6f * s);
        if (ImGui.Button("Cancel##cancel", new Vector2(84f * s, 22f * s))) src.Cancel();
        Rects["cancel"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 236f * s);
        ImGui.TextColored(NOTE, "one entity — an area + a recipe + a seed; Unpack makes it single items"); Lines.Add("one entity — an area + a recipe + a seed; Unpack makes it single items");
        ImGui.PopTextWrapPos();
    }
}
