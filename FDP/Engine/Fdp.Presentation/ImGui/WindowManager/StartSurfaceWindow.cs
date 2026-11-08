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
    /// <param name="CommandId">⭐ U-1 — the declared command the menu item runs (K5); the look of an ACTION (icon, short words) is keyed on it, never on the label. Null for a Recent row's button or a closure-only item.</param>
    /// <param name="IconKey">⭐ U-1 — a contributed item's own icon key (the Help ▸ About DTO's <c>status/info</c>), the look's key when there is no command.</param>
    public sealed record StartButton(string Label, bool Enabled, Action Invoke, string? Why = null, NodeEditor.Core.Widgets.IconGlyph? Icon = null, string? CommandId = null, string? IconKey = null);

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
                _menu.AllowedInIdleItems().Select(ToButton).ToList(), _recent?.Invoke());
        return new StartSurfaceModel(variant, variant == StartVariant.ServerLost ? ServerLostText : WaitingText, Array.Empty<StartButton>());
    }

    /// <summary>
    /// ⭐ U-1 — one menu item as a button. ⛔ The disabled reason is NOT part of the label: a host words a greyed item <i>Name (unavailable — why)</i> / <i>Name (why)</i> (<c>ScenarioMenuCommands.RegisterCommand</c>); the button
    /// keeps the plain <see cref="GlobalMenuRegistry.IdleAction.Name"/> and the reason goes to the tooltip (<see cref="StartButton.Why"/>).
    /// </summary>
    private static StartButton ToButton(GlobalMenuRegistry.IdleAction a)
    {
        bool on = a.Enabled();
        string full = a.Label();
        string name = a.Name.Length > 0 ? a.Name : full;
        return new StartButton(on ? full : name, on, a.Invoke, on ? null : ReasonOf(name, full), null, a.CommandId, a.IconKey);
    }

    /// <summary>⭐ U-1 — the reason a host put after the plain <paramref name="name"/> in a greyed item's <paramref name="full"/> label (<c>"Name (unavailable — why)"</c>, <c>"Name (why)"</c>, or a label that is only the reason), or null when the label is the plain name.</summary>
    public static string? ReasonOf(string name, string full)
    {
        if (string.IsNullOrEmpty(full) || full == name) return null;
        string rest = full.StartsWith(name, StringComparison.Ordinal) ? full.Substring(name.Length).Trim() : full;
        if (rest.Length > 1 && rest[0] == '(' && rest[^1] == ')') rest = rest.Substring(1, rest.Length - 2);
        const string unavailable = "unavailable — ";
        if (rest.StartsWith(unavailable, StringComparison.Ordinal)) rest = rest.Substring(unavailable.Length);
        return rest.Length > 0 ? rest : null;
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
        Measure(model);
        float buttonWidth = _actionW;
        // ⭐ the panel is sized by what it shows and centred on the WHOLE surface (⛔ was: the content width without the left padding, and 40 % down)
        var wpos = Gui.GetWindowPos();
        var wsize = Gui.GetWindowSize();
        float visibleH = wsize.Y - Math.Max(0f, _bottomInset?.Invoke() ?? 0f);   // ⭐ centred on what is VISIBLE (the status bar covers the bottom)
        var recent = model.RecentRows;
        bool twoColumns = recent.Count > 0;
        float leftW = buttonWidth, gap = twoColumns ? ColumnGap : 0f, rightW = twoColumns ? _recentW : 0f;
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
        _actionRects.Clear(); _rowRects.Clear(); _actionProbes.Clear(); _rowButtonProbes.Clear();
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
            RecentColumnRight = wpos.X + rx + rightW;
            Gui.SetCursorPosY(colsTop);
            Gui.SetCursorPosX(rx);
            // ⭐ U-1 — the heading WRAPS inside the column (⛔ was one long line cut at the column's edge: "AAR of the lates…")
            Gui.PushTextWrapPos(Gui.GetCursorPosX() + rightW);
            Gui.TextDisabled(RecentHeading);
            Gui.PopTextWrapPos();
            string? hint = null;
            for (int i = 0; i < recent.Count; i++) { DrawRecentRow(i, recent[i], rx, rightW, wpos); if (recent[i].JustRun && recent[i].Hint is { } h) hint = h; }
            if (hint != null) { Gui.SetCursorPosX(rx); Gui.TextDisabled(hint); }
            if (Gui.GetCursorPosY() < leftBottom)
            {
                // ⛔ a SetCursorPos that ends the window must be followed by an item, or ImGui asserts "Code uses SetCursorPos()/SetCursorScreenPos()
                //   to extend window/parent boundaries" (seen on Windows at start-up, 2026-11-08). The empty Dummy validates the extent; placed one
                //   ItemSpacing higher so the cursor lands exactly on leftBottom (the block height below is unchanged).
                Gui.SetCursorPosY(leftBottom - style.ItemSpacing.Y);
                Gui.Dummy(Vector2.Zero);
            }
        }
        Gui.PopStyleColor(3);
        _blockHeight = Gui.GetCursorPosY() - startY - style.ItemSpacing.Y;
    }

    private const float ColumnGap = 24f, TitleScale = 1.7f;

    // ⭐ U-1 — sizes are MEASURED from the text each frame (font, UI scale, glyph), never constants: a fixed 220 / 66 / 470 is what cut the labels at the user's font.
    private const float ActionMinW = 160f, RecentMinW = 470f, RowMinH = 46f, RowButtonMinW = 54f, BtnPadX = 8f, BtnGap = 6f;
    private float _actionW = ActionMinW, _recentW = RecentMinW, _rowH = RowMinH, _rowBtnW = RowButtonMinW;
    private static readonly Dictionary<string, string> Words = new();

    /// <summary>The word as the UI font can draw it: the font has no ellipsis, so "…" is drawn as three dots (cached — nothing allocates once a label was seen).</summary>
    private static string Drawn(string label)
    {
        if (label.IndexOf('…') < 0) return label;
        if (!Words.TryGetValue(label, out var w)) Words[label] = w = label.Replace("…", "...");
        return w;
    }

    /// <summary>⭐ U-1 — measures the columns from this frame's text: the action column fits its widest glyph + words, a Recent row button fits the widest glyph + word of any row, the Recent column fits the names, the sub lines and the three buttons.</summary>
    private void Measure(StartSurfaceModel model)
    {
        float line = Gui.GetTextLineHeight();
        float aw = ActionMinW;
        for (int i = 0; i < model.Buttons.Count; i++)
        {
            var (text, glyph) = ActionLook(model.Buttons[i]);
            aw = Math.Max(aw, BtnPadX + (glyph != null ? Gui.CalcTextSize(glyph).X + BtnGap : 0f) + Gui.CalcTextSize(text).X + BtnPadX);
        }
        _actionW = MathF.Ceiling(aw);
        var recent = model.RecentRows;
        float bw = RowButtonMinW, nameW = 0f;
        for (int i = 0; i < recent.Count; i++)
        {
            var r = recent[i];
            bw = Math.Max(bw, RowButtonWidth(r.Live)); bw = Math.Max(bw, RowButtonWidth(r.Edit)); bw = Math.Max(bw, RowButtonWidth(r.Aar));
            nameW = Math.Max(nameW, Math.Max(Gui.CalcTextSize(r.Name).X, Gui.CalcTextSize(r.Sub).X));
        }
        _rowBtnW = MathF.Ceiling(bw);
        _rowH = Math.Max(RowMinH, MathF.Ceiling(2f * line + 14f));
        _recentW = MathF.Ceiling(Math.Max(RecentMinW, 12f + nameW + 16f + 3f * _rowBtnW + 8f));
    }

    private static float RowButtonWidth(StartButton b)
        => BtnPadX + (b.Icon is { } ic ? Gui.CalcTextSize(ic.Glyph).X + BtnGap : 0f) + Gui.CalcTextSize(b.Label).X + BtnPadX;

    private readonly List<(Vector2 Min, Vector2 Max)> _actionRects = new(), _rowRects = new();
    private readonly List<ButtonProbe> _actionProbes = new(), _rowButtonProbes = new();
    /// <summary>⭐ Rail probes (last drawn frame, screen px): the title, each action button, each Recent row\'s box.</summary>
    public (Vector2 Min, Vector2 Max) TitleRect { get; private set; }
    public IReadOnlyList<(Vector2 Min, Vector2 Max)> ActionRects => _actionRects;
    public IReadOnlyList<(Vector2 Min, Vector2 Max)> RecentRowRects => _rowRects;

    /// <summary>⭐ U-1 rail probe: where one button and its drawn parts landed (screen px) — the box, the glyph's right edge (NaN: no glyph), the words' left and right edges.</summary>
    public readonly record struct ButtonProbe(string Label, Vector2 Min, Vector2 Max, float GlyphRight, float TextLeft, float TextRight, bool HasGlyph);
    /// <summary>⭐ U-1 — each action button as last drawn, in order.</summary>
    public IReadOnlyList<ButtonProbe> ActionProbes => _actionProbes;
    /// <summary>⭐ U-1 — each Recent row button as last drawn (Live, Edit, AAR per row, in order).</summary>
    public IReadOnlyList<ButtonProbe> RowButtonProbes => _rowButtonProbes;
    /// <summary>⭐ U-1 — the Recent heading's wrapped height as last drawn is checked through the column: its right edge (screen px).</summary>
    public float RecentColumnRight { get; private set; }

    /// <summary>
    /// ⭐ Storyboard ①: how an Idle action is DRAWN — its icon and its words (<i>New scenario...</i> opens a dialog, so the ellipsis — three dots: the UI font has no …). ⭐⭐ U-1: keyed on the item's COMMAND
    /// (<see cref="StartButton.CommandId"/>; K5 — every app menu item runs a declared command), ⛔ never on its label: the label carries the host's dynamic text and a rename must not strip the icon. A contributed item
    /// with no command (Help ▸ About) is keyed on its own icon key. An action the picture does not name keeps its (drawn) label and has no icon.
    /// </summary>
    internal static (string Text, string? Glyph) ActionLook(StartButton b) => b.CommandId switch
    {
        "scenario.new" => ("New scenario...", Fonts.IconsFontAwesome6.Plus),
        "scenario.load" => ("Open scenario...", Fonts.IconsFontAwesome6.FolderOpen),
        "scenario.openRecording" => ("Open recording...", Fonts.IconsFontAwesome6.ClockRotateLeft),
        "scenario.loadCheckpoint" => ("Load checkpoint...", Fonts.IconsFontAwesome6.Flag),
        "scenario.close" => ("Close", Fonts.IconsFontAwesome6.RightFromBracket),
        "shell.exit" => ("Exit", Fonts.IconsFontAwesome6.Xmark),
        _ => b.IconKey switch
        {
            "status/info" => (Drawn(b.Label), Fonts.IconsFontAwesome6.CircleInfo),
            _ => (Drawn(b.Label), null),
        },
    };

    /// <summary>One left-column action as the storyboard draws it: a box, its grey icon at the left, the words after it (left-aligned), greyed when disabled with the reason as the tooltip.</summary>
    private void DrawActionButton(StartButton b, Vector2 size)
    {
        var (text, glyph) = ActionLook(b);
        Gui.BeginDisabled(!b.Enabled);
        Gui.PushID(b.Label);
        bool clicked = Gui.Button("##action", size);
        Gui.PopID();
        var min = Gui.GetItemRectMin();
        float y = min.Y + (Gui.GetItemRectSize().Y - Gui.GetTextLineHeight()) / 2f;
        var dl = Gui.GetWindowDrawList();
        float textX = min.X + BtnPadX;
        float glyphRight = float.NaN;
        if (glyph != null)
        {
            dl.AddText(new Vector2(min.X + BtnPadX, y), Gui.GetColorU32(NodeEditor.Core.Widgets.IconPalette.Grey), glyph);
            glyphRight = min.X + BtnPadX + Gui.CalcTextSize(glyph).X;
            textX = glyphRight + BtnGap;
        }
        dl.AddText(new Vector2(textX, y), Gui.GetColorU32(ImGuiCol.Text), text);
        _actionProbes.Add(new ButtonProbe(b.Label, min, Gui.GetItemRectMax(), glyphRight, textX, textX + Gui.CalcTextSize(text).X, glyph != null));
        if (clicked) b.Invoke();
        Gui.EndDisabled();
        if (!b.Enabled && b.Why is { } why && Gui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) Gui.SetTooltip(why);
    }

    /// <summary>One Recent-row button: opaque, disabled-greyed, the glyph at its left edge then the words at the glyph's width + a gap (⛔ was: the words pushed right with a text-align fraction, which overprinted the glyph when the font was wide), the reason as the tooltip while it is disabled.</summary>
    private void DrawStartButton(StartButton b, Vector2 size)
    {
        Gui.BeginDisabled(!b.Enabled);
        Gui.PushID(b.Label);
        bool clicked = Gui.Button("##row", size);
        Gui.PopID();
        var min = Gui.GetItemRectMin();
        float y = min.Y + (Gui.GetItemRectSize().Y - Gui.GetTextLineHeight()) / 2f;
        var dl = Gui.GetWindowDrawList();
        float textX = min.X + BtnPadX;
        float glyphRight = float.NaN;
        if (b.Icon is { } icon)
        {
            dl.AddText(new Vector2(min.X + BtnPadX, y), Gui.GetColorU32(b.Enabled ? icon.Color : icon.Color with { W = 0.4f }), icon.Glyph);
            glyphRight = min.X + BtnPadX + Gui.CalcTextSize(icon.Glyph).X;
            textX = glyphRight + BtnGap;
        }
        dl.AddText(new Vector2(textX, y), Gui.GetColorU32(ImGuiCol.Text), b.Label);
        _rowButtonProbes.Add(new ButtonProbe(b.Label, min, Gui.GetItemRectMax(), glyphRight, textX, textX + Gui.CalcTextSize(b.Label).X, b.Icon != null));
        if (clicked) b.Invoke();
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
        dl.AddRectFilled(top, new Vector2(top.X + width, top.Y + _rowH), Gui.GetColorU32(row.JustRun ? new Vector4(0.27f, 0.29f, 0.35f, 1f) : new Vector4(0.16f, 0.17f, 0.20f, 1f)), 4f);
        dl.AddRect(top, new Vector2(top.X + width, top.Y + _rowH), Gui.GetColorU32(row.JustRun ? new Vector4(0.78f, 0.78f, 0.83f, 1f) : new Vector4(0.30f, 0.31f, 0.36f, 1f)), 4f);
        _rowRects.Add((top, new Vector2(top.X + width, top.Y + _rowH)));
        float textTop = top.Y + (_rowH - (2f * Gui.GetTextLineHeight() + 2f)) / 2f;
        dl.AddText(new Vector2(top.X + 12f, textTop), Gui.GetColorU32(ImGuiCol.Text), row.Name);
        dl.AddText(new Vector2(top.X + 12f, textTop + Gui.GetTextLineHeight() + 2f), Gui.GetColorU32(ImGuiCol.TextDisabled), row.Sub);
        float bw = _rowBtnW;
        float bx = top.X + width - 3 * bw - 8f;
        float by = top.Y + (_rowH - Gui.GetFrameHeight()) / 2f;
        DrawRowButton(0, row.Live, bx, by, top);
        DrawRowButton(1, row.Edit, bx + bw, by, top);
        DrawRowButton(2, row.Aar, bx + 2 * bw, by, top);
        Gui.SetCursorScreenPos(new Vector2(top.X, top.Y + _rowH));
        Gui.Dummy(new Vector2(width, 6f));   // the gap between rows (also extends the window's content, so the cursor ends below the row)
        Gui.PopID();
    }

    private void DrawRowButton(int slot, StartButton b, float x, float y, Vector2 rowTop)
    {
        Gui.SetCursorScreenPos(new Vector2(x, y));
        Gui.PushID(slot);
        DrawStartButton(b, new Vector2(_rowBtnW - 4f, 0f));
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
