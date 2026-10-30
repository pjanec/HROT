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
    public sealed record StartButton(string Label, bool Enabled, Action Invoke);

    /// <summary>⭐ What the window shows now — pure data, so a rail can read and "click" it without ImGui.</summary>
    public sealed record StartSurfaceModel(StartVariant Variant, string Text, IReadOnlyList<StartButton> Buttons);

    private readonly GlobalMenuRegistry _menu;
    private readonly Func<StartVariant> _variant;

    /// <param name="bottomInset">⭐ What covers the surface's bottom edge (the status bar's height, <c>wm.StatusBar.Height</c>): the menu panel is centred on
    /// the VISIBLE area above it. Null = nothing covers it.</param>
    public StartSurfaceWindow(GlobalMenuRegistry menu, Func<StartVariant> variant, Func<float>? bottomInset = null)
        : base(WindowId, "Start", WindowManager.StartPerspectiveName, WindowScope.PerspectiveBound)
    {
        _bottomInset = bottomInset;
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
                _menu.AllowedInIdleItems().Select(a => new StartButton(a.Label(), a.Enabled(), a.Invoke)).ToList());
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
        const float buttonWidth = 320f;
        // ⭐ the panel is sized by what it shows and centred on the WHOLE surface (⛔ was: the content width without the left padding, and 40 % down)
        var wpos = Gui.GetWindowPos();
        var wsize = Gui.GetWindowSize();
        float visibleH = wsize.Y - Math.Max(0f, _bottomInset?.Invoke() ?? 0f);   // ⭐ centred on what is VISIBLE (the status bar covers the bottom)
        float contentW = Math.Max(buttonWidth, Math.Max(Gui.CalcTextSize("HROT").X, Gui.CalcTextSize(model.Text).X));
        float blockH = _blockHeight > 0f ? _blockHeight
            : Gui.GetTextLineHeightWithSpacing() * 2 + model.Buttons.Count * (Gui.GetFrameHeightWithSpacing() + Gui.GetStyle().ItemSpacing.Y);
        var min = new Vector2(MathF.Round(wpos.X + (wsize.X - contentW) / 2f - PanelPadX), MathF.Round(wpos.Y + (visibleH - blockH) / 2f - PanelPadY));
        var max = new Vector2(min.X + contentW + 2 * PanelPadX, min.Y + blockH + 2 * PanelPadY);
        PanelRect = (min, max);
        Gui.GetWindowDrawList().AddRectFilled(min, max, (uint)Math.Round(MenuBandAlpha * 255f) << 24, 8f);

        float left = min.X + PanelPadX - wpos.X;   // window-local x of the panel's content
        Gui.SetCursorPosY(min.Y + PanelPadY - wpos.Y);
        float startY = Gui.GetCursorPosY();
        CenteredText("HROT", left, contentW);
        CenteredText(model.Text, left, contentW);
        Gui.Spacing();
        // ⭐ opaque buttons: ImGui's default button colours are translucent, so the wallpaper showed through them
        var style = Gui.GetStyle();
        Gui.PushStyleColor(ImGuiCol.Button, Opaque(style.Colors[(int)ImGuiCol.Button]));
        Gui.PushStyleColor(ImGuiCol.ButtonHovered, Opaque(style.Colors[(int)ImGuiCol.ButtonHovered]));
        Gui.PushStyleColor(ImGuiCol.ButtonActive, Opaque(style.Colors[(int)ImGuiCol.ButtonActive]));
        foreach (var b in model.Buttons)
        {
            Gui.SetCursorPosX(left + (contentW - buttonWidth) / 2f);
            Gui.BeginDisabled(!b.Enabled);
            if (Gui.Button(b.Label, new Vector2(buttonWidth, 0f))) b.Invoke();
            Gui.EndDisabled();
            Gui.Spacing();
        }
        Gui.PopStyleColor(3);
        _blockHeight = Gui.GetCursorPosY() - startY - style.ItemSpacing.Y;
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
