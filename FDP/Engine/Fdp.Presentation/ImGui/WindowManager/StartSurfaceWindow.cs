using System;
using System.Numerics;
using ImGuiNET;

namespace Fdp.Presentation.WindowManager;

/// <summary>
/// ⭐⭐ <c>CE-4096</c> (AQ108 §2.6 / D16) — <b>the ONE start surface</b>: the only window that claims the <c>Start</c> perspective, so the
/// A0 rule holds and no other panel shows. ⛔ <b>It owns NO action</b>: in the Actions variant it draws the menu items marked allowed-in-Idle
/// (<see cref="GlobalMenuRegistry.AllowedInIdleItems"/>) as buttons and invokes the SAME bindings, so the surface and the menu cannot
/// disagree; otherwise it draws one waiting sentence.
/// </summary>
public sealed class StartSurfaceWindow : ManagedWindow
{
    /// <summary>The window id (a rail asserts exactly one window claims Start).</summary>
    public const string WindowId = "fdp_start_surface";

    public const string WaitingText = "Waiting for the server to load a scenario";
    public const string ServerLostText = "Server lost — waiting";
    public const string ActionsText = "Open a scenario or a recording to begin";

    /// <summary>One button: a menu leaf's label and the menu's own binding.</summary>
    /// <param name="Why">⭐ CE-3414 — the tooltip a DISABLED button gives (the reason); null = none.</param>
    /// <param name="Icon">⭐ CE-3414 — a coloured glyph drawn at the button's left edge (the Recent rows' ● Live · pen Edit · ▶ AAR); null = text only.</param>
    public sealed record StartButton(string Label, bool Enabled, Action Invoke, string? Why = null, NodeEditor.Core.Widgets.IconGlyph? Icon = null);

    /// <summary>⭐⭐ CE-3414 (AQ108 §0 D31) — one row of the <i>Recent</i> list: the scenario's name, a sub line (<i>Live · 10 min ago</i> · <i>never run</i>) and its three buttons. Pure data, built by the host (the surface owns no action).</summary>
    /// <param name="JustRun">⭐ CE-3414 follow-up (storyboard ⑧) — this scenario\'s run has JUST ended: its box is highlighted.</param>
    /// <param name="Hint">⭐ the line under the list for a just-run row (<i>Night attack ▸ AAR = the replay of the latest run, at its start</i>); null = none.</param>
    public sealed record StartRecentRow(string Name, string Sub, StartButton Live, StartButton Edit, StartButton Aar, bool JustRun = false, string? Hint = null);

    /// <summary>⭐ What the window shows now — pure data, so a rail can read and "click" it without ImGui.</summary>
    public sealed record StartSurfaceModel(StartVariant Variant, string Text, IReadOnlyList<StartButton> Buttons, IReadOnlyList<StartRecentRow>? Recent = null)
    {
        /// <summary>The Recent rows (empty when the host offers none).</summary>
        public IReadOnlyList<StartRecentRow> RecentRows => Recent ?? Array.Empty<StartRecentRow>();
    }

    /// <summary>The column heading of the Recent list (the storyboard's panel ①).</summary>
    public const string RecentHeading = "Recent  -  Live · Edit (author seat only) · AAR of the latest run";

    private readonly GlobalMenuRegistry _menu;
    private readonly Func<StartVariant> _variant;
    private readonly Func<IReadOnlyList<StartRecentRow>>? _recent;

    /// <param name="bottomInset">⭐ What covers the surface's bottom edge (the status bar's height, <c>wm.StatusBar.Height</c>): the menu panel is centred on
    /// the VISIBLE area above it. Null = nothing covers it.</param>
    /// <param name="recent">⭐ CE-3414 — the Recent rows, from the host (read every frame in Idle: the provider caches). Null = the single-column surface.</param>
    public StartSurfaceWindow(GlobalMenuRegistry menu, Func<StartVariant> variant, Func<float>? bottomInset = null, Func<IReadOnlyList<StartRecentRow>>? recent = null)
        : base(WindowId, "Start", WindowManager.StartPerspectiveName, WindowScope.PerspectiveBound)
    {
        _bottomInset = bottomInset;
        _recent = recent;
        _menu = menu ?? throw new ArgumentNullException(nameof(menu));
        _variant = variant ?? throw new ArgumentNullException(nameof(variant));
        IsOpen = true;
        ShowInMenu = false;   // never a toggle in the Windows menu — it is the perspective, not a panel
    }

    /// <summary>The model: Actions = the allowed-in-Idle menu items (the shell's Help is always one), else one waiting sentence.</summary>
    public StartSurfaceModel Model()
    {
        var variant = _variant();
        if (variant == StartVariant.Actions)
            return new StartSurfaceModel(StartVariant.Actions, ActionsText,
                _menu.AllowedInIdleItems().Select(a => new StartButton(a.Label(), a.Enabled(), a.Invoke)).ToList(), _recent?.Invoke());
        return new StartSurfaceModel(variant, variant == StartVariant.ServerLost ? ServerLostText : WaitingText, Array.Empty<StartButton>());
    }

    protected override ImGuiWindowFlags ExtraFlags =>
        ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse
        | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoScrollbar;

    /// <summary>Full-screen: the whole work area under the menu bar.</summary>
    protected override void BeforeBegin()
    {
        var vp = Gui.GetMainViewport();
        Gui.SetNextWindowPos(vp.WorkPos);
        Gui.SetNextWindowSize(vp.WorkSize);
    }

    // ── CE-4112: the wallpaper ────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ <c>CE-4112</c> — the wallpaper scaled to COVER a <paramref name="winW"/> × <paramref name="winH"/> window with its aspect kept (centre-crop): <c>scale = max(W/w, H/h)</c>, the image centred.
    /// <c>X</c>/<c>Y</c> are the image rect's offset from the window's corner (≤ 0: the overflow is cropped), <c>W</c>/<c>H</c> its size; <c>U0..V1</c> the visible part in texture space.
    /// </summary>
    public readonly record struct CoverRect(float Scale, float X, float Y, float W, float H, float U0, float V0, float U1, float V1);

    /// <summary>The pure cover math (a rail pins it; the draw uses its UVs).</summary>
    public static CoverRect Cover(float winW, float winH, float imgW, float imgH)
    {
        float scale = Math.Max(winW / imgW, winH / imgH);
        float w = imgW * scale, h = imgH * scale;
        float x = (winW - w) / 2f, y = (winH - h) / 2f;
        return new CoverRect(scale, x, y, w, h, -x / w, -y / h, (winW - x) / w, (winH - y) / h);
    }

    /// <summary>⭐ A dark panel behind the menu block: the wallpaper's centre is bright (the arrow), so the white title and the buttons need it. One alpha.
    /// 🔒 User, <c>2026-10-30</c>: <i>"the transparency need to be much less, too much of the background blends in the text and buttons"</i> — ⛔ was 0.55.</summary>
    public const float MenuBandAlpha = 0.9f;

    private static nint _wallpaperTexture;
    private static int _wallpaperW, _wallpaperH;
    private static bool _wallpaperTried;

    /// <summary>The wallpaper's texture (id, size), loaded ONCE per process on the first drawn frame (a GL context is current) from the embedded resource through
    /// <see cref="Icons.EmbeddedAtlasResources"/> — ⛔ no file on disk. <see langword="null"/> when the GPU upload failed (the surface then draws without it).</summary>
    public static (nint Id, int W, int H)? Wallpaper()
    {
        if (!_wallpaperTried)
        {
            // ⭐ No Raylib window ⇒ no GL context: the upload crashed the PROCESS natively (an ImGui-only test fixture — StartGateTests aborted the whole
            //   Fdp.Presentation.Tests run there, silently leaving every later test unrun). Skip it and try again once a window exists (as MapCamera / Map3DBuild do).
            bool ready;
            try { ready = Raylib_cs.Raylib.IsWindowReady(); } catch (Exception) { ready = false; }
            if (!ready) return null;
            _wallpaperTried = true;
            var img = Raylib_cs.Raylib.LoadImageFromMemory(".png", Icons.EmbeddedAtlasResources.GetWallpaperPngBytes());
            var tex = Raylib_cs.Raylib.LoadTextureFromImage(img);
            Raylib_cs.Raylib.UnloadImage(img);
            if (tex.Id != 0)
            {
                Raylib_cs.Raylib.SetTextureFilter(tex, Raylib_cs.TextureFilter.Bilinear);
                _wallpaperTexture = (nint)tex.Id; _wallpaperW = tex.Width; _wallpaperH = tex.Height;
            }
        }
        return _wallpaperTexture != 0 ? (_wallpaperTexture, _wallpaperW, _wallpaperH) : null;
    }

    private void DrawWallpaper()
    {
        if (Wallpaper() is not { } wp) return;
        var pos = Gui.GetWindowPos();
        var size = Gui.GetWindowSize();
        var c = Cover(size.X, size.Y, wp.W, wp.H);
        Gui.GetWindowDrawList().AddImage(wp.Id, pos, pos + size, new Vector2(c.U0, c.V0), new Vector2(c.U1, c.V1));
    }

    /// <summary>The menu panel's padding around its items (px).</summary>
    private const float PanelPadX = 28f, PanelPadY = 14f;

    /// <summary>The block's height as last DRAWN (the title, the line, the buttons) — measured, so the centring is exact from the second frame on.</summary>
    private float _blockHeight;
    private readonly Func<float>? _bottomInset;

    /// <summary>⭐ The menu panel's last drawn rect, in screen pixels (a rail checks it is centred on the surface).</summary>
    public (Vector2 Min, Vector2 Max) PanelRect { get; private set; }

    protected override void DrawClientArea()
    {
        DrawWallpaper();   // ⭐ CE-4112: under everything the surface draws
        var model = Model();
        const float buttonWidth = ActionWidth;
        // ⭐ the panel is sized by what it shows and centred on the WHOLE surface (⛔ was: the content width without the left padding, and 40 % down)
        var wpos = Gui.GetWindowPos();
        var wsize = Gui.GetWindowSize();
        float visibleH = wsize.Y - Math.Max(0f, _bottomInset?.Invoke() ?? 0f);   // ⭐ centred on what is VISIBLE (the status bar covers the bottom)
        var recent = model.RecentRows;
        bool twoColumns = recent.Count > 0;
        float leftW = buttonWidth, gap = twoColumns ? ColumnGap : 0f, rightW = twoColumns ? RecentColumnWidth : 0f;
        Gui.SetWindowFontScale(TitleScale);
        float titleW = Gui.CalcTextSize("HROT").X;
        Gui.SetWindowFontScale(1f);
        bool showText = model.Variant != StartVariant.Actions;   // ⭐ the storyboard has no sentence under the title (the actions speak for themselves)
        float contentW = Math.Max(leftW + gap + rightW, Math.Max(titleW, showText ? Gui.CalcTextSize(model.Text).X : 0f));
        float blockH = _blockHeight > 0f ? _blockHeight
            : Gui.GetTextLineHeightWithSpacing() * 2 + model.Buttons.Count * (Gui.GetFrameHeightWithSpacing() + Gui.GetStyle().ItemSpacing.Y);
        var min = new Vector2(MathF.Round(wpos.X + (wsize.X - contentW) / 2f - PanelPadX), MathF.Round(wpos.Y + (visibleH - blockH) / 2f - PanelPadY));
        var max = new Vector2(min.X + contentW + 2 * PanelPadX, min.Y + blockH + 2 * PanelPadY);
        PanelRect = (min, max);
        Gui.GetWindowDrawList().AddRectFilled(min, max, (uint)Math.Round(MenuBandAlpha * 255f) << 24, 8f);

        float left = min.X + PanelPadX - wpos.X;   // window-local x of the panel's content
        Gui.SetCursorPosY(min.Y + PanelPadY - wpos.Y);
        float startY = Gui.GetCursorPosY();
        // ⭐ the large HROT title (storyboard ①: 22 px beside the 13 px body)
        Gui.SetWindowFontScale(TitleScale);
        var titleMin = Gui.GetCursorScreenPos();
        CenteredText("HROT", left, contentW);
        TitleRect = (titleMin, new Vector2(titleMin.X + contentW, Gui.GetCursorScreenPos().Y));
        Gui.SetWindowFontScale(1f);
        if (showText) CenteredText(model.Text, left, contentW);
        Gui.Spacing();
        // ⭐ opaque buttons: ImGui's default button colours are translucent, so the wallpaper showed through them
        var style = Gui.GetStyle();
        Gui.PushStyleColor(ImGuiCol.Button, Opaque(style.Colors[(int)ImGuiCol.Button]));
        Gui.PushStyleColor(ImGuiCol.ButtonHovered, Opaque(style.Colors[(int)ImGuiCol.ButtonHovered]));
        Gui.PushStyleColor(ImGuiCol.ButtonActive, Opaque(style.Colors[(int)ImGuiCol.ButtonActive]));
        float colsTop = Gui.GetCursorPosY();
        float leftX = twoColumns ? left : left + (contentW - buttonWidth) / 2f;   // one column: centred, as before
        _actionRects.Clear(); _rowRects.Clear();
        foreach (var b in model.Buttons)
        {
            Gui.SetCursorPosX(leftX);
            DrawActionButton(b, new Vector2(buttonWidth, 0f));
            _actionRects.Add((Gui.GetItemRectMin(), Gui.GetItemRectMax()));
            Gui.Spacing();
        }
        float leftBottom = Gui.GetCursorPosY();
        if (twoColumns)
        {
            // ⭐ CE-3414 — the right column: the heading, then one box per Recent scenario (name · sub line · ● Live · pen Edit · ▶ AAR)
            float rx = left + leftW + gap;
            Gui.SetCursorPosY(colsTop);
            Gui.SetCursorPosX(rx);
            Gui.TextDisabled(RecentHeading);
            string? hint = null;
            for (int i = 0; i < recent.Count; i++) { DrawRecentRow(i, recent[i], rx, rightW, wpos); if (recent[i].JustRun && recent[i].Hint is { } h) hint = h; }
            if (hint != null) { Gui.SetCursorPosX(rx); Gui.TextDisabled(hint); }
            if (Gui.GetCursorPosY() < leftBottom) Gui.SetCursorPosY(leftBottom);
        }
        Gui.PopStyleColor(3);
        _blockHeight = Gui.GetCursorPosY() - startY - style.ItemSpacing.Y;
    }

    private const float ColumnGap = 24f, RecentColumnWidth = 470f, RowHeight = 46f, RowButtonW = 66f, ActionWidth = 220f, TitleScale = 1.7f;

    private readonly List<(Vector2 Min, Vector2 Max)> _actionRects = new(), _rowRects = new();
    /// <summary>⭐ Rail probes (last drawn frame, screen px): the title, each action button, each Recent row\'s box.</summary>
    public (Vector2 Min, Vector2 Max) TitleRect { get; private set; }
    public IReadOnlyList<(Vector2 Min, Vector2 Max)> ActionRects => _actionRects;
    public IReadOnlyList<(Vector2 Min, Vector2 Max)> RecentRowRects => _rowRects;

    /// <summary>⭐ Storyboard ①: how an Idle action is DRAWN — its icon and its words (<i>New scenario...</i> opens a dialog, so the ellipsis — three dots: the UI font has no …); the menu\'s own label (and every binding) is untouched. An action the picture does not name keeps its label and has no icon.</summary>
    private static (string Text, string? Glyph) ActionLook(string label) => label switch
    {
        "New Scenario" => ("New scenario...", Fonts.IconsFontAwesome6.Plus),
        "Open Scenario" => ("Open scenario...", Fonts.IconsFontAwesome6.FolderOpen),
        "Open Recording" => ("Open recording...", Fonts.IconsFontAwesome6.ClockRotateLeft),
        "About" => ("About", Fonts.IconsFontAwesome6.CircleInfo),
        _ => (label, null),
    };

    /// <summary>One left-column action as the storyboard draws it: a box, its grey icon at the left, the words after it (left-aligned), greyed when disabled with the reason as the tooltip.</summary>
    private static void DrawActionButton(StartButton b, Vector2 size)
    {
        var (text, glyph) = ActionLook(b.Label);
        Gui.BeginDisabled(!b.Enabled);
        Gui.PushID(b.Label);
        bool clicked = Gui.Button("##action", size);
        Gui.PopID();
        var min = Gui.GetItemRectMin();
        float y = min.Y + (Gui.GetItemRectSize().Y - Gui.GetTextLineHeight()) / 2f;
        var dl = Gui.GetWindowDrawList();
        if (glyph != null) dl.AddText(new Vector2(min.X + 8f, y), Gui.GetColorU32(NodeEditor.Core.Widgets.IconPalette.Grey), glyph);
        dl.AddText(new Vector2(min.X + (glyph != null ? 30f : 8f), y), Gui.GetColorU32(ImGuiCol.Text), text);
        if (clicked) b.Invoke();
        Gui.EndDisabled();
        if (!b.Enabled && b.Why is { } why && Gui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) Gui.SetTooltip(why);
    }

    /// <summary>One start button: opaque, disabled-greyed, the glyph at its left edge, the reason as the tooltip while it is disabled.</summary>
    private static void DrawStartButton(StartButton b, Vector2 size)
    {
        Gui.BeginDisabled(!b.Enabled);
        if (b.Icon is { } icon)
        {
            // the label is pushed right of the glyph (the text is aligned left of the remaining width)
            Gui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.72f, 0.5f));
            bool clicked = Gui.Button(b.Label, size);
            Gui.PopStyleVar();
            var min = Gui.GetItemRectMin();
            Gui.GetWindowDrawList().AddText(new Vector2(min.X + 7f, min.Y + (Gui.GetItemRectSize().Y - Gui.GetTextLineHeight()) / 2f),
                Gui.GetColorU32(b.Enabled ? icon.Color : icon.Color with { W = 0.4f }), icon.Glyph);
            if (clicked) b.Invoke();
        }
        else if (Gui.Button(b.Label, size)) b.Invoke();
        Gui.EndDisabled();
        if (!b.Enabled && b.Why is { } why && Gui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) Gui.SetTooltip(why);
    }

    private void DrawRecentRow(int i, StartRecentRow row, float x, float width, Vector2 wpos)
    {
        Gui.PushID(i);
        Gui.SetCursorPosX(x);
        var top = Gui.GetCursorScreenPos();
        var dl = Gui.GetWindowDrawList();
        // ⭐ storyboard ⑧: the just-run row is the selected one — a lighter box and a pale frame
        dl.AddRectFilled(top, new Vector2(top.X + width, top.Y + RowHeight), Gui.GetColorU32(row.JustRun ? new Vector4(0.27f, 0.29f, 0.35f, 1f) : new Vector4(0.16f, 0.17f, 0.20f, 1f)), 4f);
        dl.AddRect(top, new Vector2(top.X + width, top.Y + RowHeight), Gui.GetColorU32(row.JustRun ? new Vector4(0.78f, 0.78f, 0.83f, 1f) : new Vector4(0.30f, 0.31f, 0.36f, 1f)), 4f);
        _rowRects.Add((top, new Vector2(top.X + width, top.Y + RowHeight)));
        dl.AddText(new Vector2(top.X + 12f, top.Y + 6f), Gui.GetColorU32(ImGuiCol.Text), row.Name);
        dl.AddText(new Vector2(top.X + 12f, top.Y + 6f + Gui.GetTextLineHeight() + 2f), Gui.GetColorU32(ImGuiCol.TextDisabled), row.Sub);
        float bx = top.X + width - 3 * RowButtonW - 8f;
        float by = top.Y + (RowHeight - Gui.GetFrameHeight()) / 2f;
        DrawRowButton(0, row.Live, bx, by, top);
        DrawRowButton(1, row.Edit, bx + RowButtonW, by, top);
        DrawRowButton(2, row.Aar, bx + 2 * RowButtonW, by, top);
        Gui.SetCursorScreenPos(new Vector2(top.X, top.Y + RowHeight));
        Gui.Dummy(new Vector2(width, 6f));   // the gap between rows (also extends the window's content, so the cursor ends below the row)
        Gui.PopID();
    }

    private static void DrawRowButton(int slot, StartButton b, float x, float y, Vector2 rowTop)
    {
        Gui.SetCursorScreenPos(new Vector2(x, y));
        Gui.PushID(slot);
        DrawStartButton(b, new Vector2(RowButtonW - 4f, 0f));
        Gui.PopID();
    }

    /// <summary>The colour made opaque: blended over the panel's own near-black (what the eye saw through it), so the hue stays the style's.</summary>
    private static Vector4 Opaque(Vector4 c)
    {
        const float under = 0.06f;
        float a = c.W;
        return new Vector4(c.X * a + under * (1f - a), c.Y * a + under * (1f - a), c.Z * a + under * (1f - a), 1f);
    }

    private static void CenteredText(string text, float left, float width)
    {
        Gui.SetCursorPosX(left + (width - Gui.CalcTextSize(text).X) / 2f);
        Gui.TextUnformatted(text);
    }
}
