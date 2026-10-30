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

    public StartSurfaceWindow(GlobalMenuRegistry menu, Func<StartVariant> variant)
        : base(WindowId, "Start", WindowManager.StartPerspectiveName, WindowScope.PerspectiveBound)
    {
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

    /// <summary>⭐ A dark band behind the menu block: the wallpaper's centre is bright (the arrow), so the white title and the buttons need it. One alpha.</summary>
    public const float MenuBandAlpha = 0.55f;

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

    protected override void DrawClientArea()
    {
        DrawWallpaper();   // ⭐ CE-4112: under everything the surface draws
        var model = Model();
        var avail = Gui.GetContentRegionAvail();
        const float buttonWidth = 320f;
        float blockHeight = Gui.GetTextLineHeightWithSpacing() * 2 + model.Buttons.Count * (Gui.GetFrameHeightWithSpacing() + 4f);
        Gui.SetCursorPosY(Gui.GetCursorPosY() + Math.Max(0f, (avail.Y - blockHeight) * 0.4f));

        {
            // the dark band behind the menu block (readability over the bright wallpaper)
            var wpos = Gui.GetWindowPos();
            float top = Gui.GetCursorScreenPos().Y - 12f;
            float bandW = Math.Max(buttonWidth, Gui.CalcTextSize(model.Text).X) + 56f;
            Gui.GetWindowDrawList().AddRectFilled(new Vector2(wpos.X + (avail.X - bandW) / 2f, top), new Vector2(wpos.X + (avail.X + bandW) / 2f, top + blockHeight + 24f),
                (uint)Math.Round(MenuBandAlpha * 255f) << 24, 8f);
        }
        CenteredText("HROT");
        CenteredText(model.Text);
        Gui.Spacing();
        foreach (var b in model.Buttons)
        {
            Gui.SetCursorPosX(Math.Max(0f, (avail.X - buttonWidth) * 0.5f));
            Gui.BeginDisabled(!b.Enabled);
            if (Gui.Button(b.Label, new Vector2(buttonWidth, 0f))) b.Invoke();
            Gui.EndDisabled();
            Gui.Spacing();
        }
    }

    private static void CenteredText(string text)
    {
        float w = Gui.CalcTextSize(text).X;
        Gui.SetCursorPosX(Math.Max(0f, (Gui.GetContentRegionAvail().X - w) * 0.5f));
        Gui.TextUnformatted(text);
    }
}
