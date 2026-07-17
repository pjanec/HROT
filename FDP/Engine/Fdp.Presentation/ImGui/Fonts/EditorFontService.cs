using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using ImGuiNET;
using rlImGui_cs;

namespace Fdp.Presentation.Fonts;

/// <summary>
/// Owns the editor's ImGui font pipeline: bakes a scalable <em>Roboto</em> UI ("chrome")
/// face plus a ladder of canvas faces, merges Font Awesome icons, applies DPI / user
/// UI-scaling, and re-uploads the atlas. Publishes the result to
/// <see cref="EditorFontRegistry"/> for the NodeEdit canvas theme.
///
/// <para><b>Threading / frame timing:</b> a (re)bake calls <c>io.Fonts.Clear()/Build()</c>
/// and <c>rlImGui.ReloadFonts()</c>, which must run OUTSIDE an ImGui frame (not between
/// <c>rlImGui.Begin()</c> and <c>rlImGui.End()</c>). Startup baking runs during setup;
/// runtime scale changes set a pending flag that the render loop drains via
/// <see cref="ApplyPendingRebuild"/> before beginning the next frame.</para>
/// </summary>
public sealed class EditorFontService
{
    // ── Tunables ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Base pixel size of the UI chrome face at scale 1.0. Kept close to ImGui's built-in
    /// 13 px default so the font fits the default control padding / frame heights comfortably
    /// (16 px was ~23% too tall for edit boxes and tab headers). At this size 100% UI-scale
    /// reads as "normal", and the slider scales up from there for hi-DPI.
    /// </summary>
    public const float BaseUiPx = 14f;

    /// <summary>Canvas ladder base sizes (pre-scale). Chosen to cover the ~0.3×–3× zoom
    /// range crisply once multiplied by the effective scale.</summary>
    private static readonly float[] LadderBaseSizes = { 10f, 13f, 16f, 20f, 26f, 32f };

    /// <summary>Clamp range for the effective scale (DPI × user).</summary>
    public const float MinScale = 0.5f;
    public const float MaxScale = 3.0f;

    // ── Glyph ranges (Level-1 Unicode) ────────────────────────────────────────
    // Pinned for process lifetime — ImGui reads the range arrays during Build().
    // UI face: Latin + Latin Extended + Greek + Cyrillic + Vietnamese + punctuation/currency.
    private static readonly ushort[] UiRanges =
    {
        0x0020, 0x00FF, // Basic Latin + Latin-1 Supplement
        0x0100, 0x024F, // Latin Extended-A/B
        0x0370, 0x03FF, // Greek
        0x0400, 0x052F, // Cyrillic + Cyrillic Supplement
        0x1E00, 0x1EFF, // Latin Extended Additional (Vietnamese)
        0x2000, 0x206F, // General Punctuation (dashes, quotes, ellipsis)
        0x20A0, 0x20BF, // Currency symbols (€ etc.)
        0x2190, 0x21FF, // Arrows (used by some UI labels)
        0,
    };
    // Canvas ladder: lighter — Latin + Cyrillic (node titles / pins are almost always
    // ASCII identifiers; Cyrillic kept because Roboto ships it and this project uses it).
    private static readonly ushort[] CanvasRanges =
    {
        0x0020, 0x00FF,
        0x0100, 0x024F,
        0x0400, 0x052F,
        0,
    };
    private static readonly ushort[] FaRanges =
    {
        IconsFontAwesome6.IconMin, IconsFontAwesome6.IconMax, 0,
    };

    private static GCHandle _uiRangesHandle;
    private static GCHandle _canvasRangesHandle;
    private static GCHandle _faRangesHandle;
    private static bool _rangesPinned;

    private static void EnsureRangesPinned()
    {
        if (_rangesPinned) return;
        _uiRangesHandle     = GCHandle.Alloc(UiRanges, GCHandleType.Pinned);
        _canvasRangesHandle = GCHandle.Alloc(CanvasRanges, GCHandleType.Pinned);
        _faRangesHandle     = GCHandle.Alloc(FaRanges, GCHandleType.Pinned);
        _rangesPinned = true;
    }

    // ── State ─────────────────────────────────────────────────────────────────

    private float _appliedStyleScale = 1f; // scale currently baked into ImGuiStyle sizes
    private bool  _pendingRebuild;
    private string? _robotoPath;
    private string? _faPath;

    /// <summary>Monitor content scale reported by the platform (autodetected DPI).</summary>
    public float DpiScale { get; private set; } = 1f;

    /// <summary>User-chosen UI-scale multiplier (from the Settings slider). Default 1.0.</summary>
    public float UserScale { get; private set; } = 1f;

    /// <summary>Effective scale actually baked = clamp(DpiScale × UserScale).</summary>
    public float EffectiveScale => Math.Clamp(DpiScale * UserScale, MinScale, MaxScale);

    /// <summary>True after <see cref="Initialize"/> has baked the atlas at least once.</summary>
    public bool IsInitialized { get; private set; }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Bake the initial atlas. Call once during ImGui setup (an ImGui context must exist
    /// and no frame may be in progress). <paramref name="dpiScale"/> is the autodetected
    /// monitor content scale; <paramref name="userScale"/> is the persisted UI-scale
    /// multiplier (1.0 if none).
    /// </summary>
    public void Initialize(float dpiScale, float userScale)
    {
        DpiScale  = SanitizeScale(dpiScale, 1f);
        UserScale = SanitizeScale(userScale, 1f);
        Rebuild(EffectiveScale);
        IsInitialized = true;
        _pendingRebuild = false;
    }

    /// <summary>Update the autodetected DPI scale (e.g. window moved to another monitor);
    /// schedules a rebuild on the next <see cref="ApplyPendingRebuild"/>.</summary>
    public void SetDpiScale(float dpiScale)
    {
        var v = SanitizeScale(dpiScale, DpiScale);
        if (v == DpiScale) return;
        DpiScale = v;
        _pendingRebuild = true;
    }

    /// <summary>Set the user UI-scale multiplier (Settings slider); schedules a rebuild.</summary>
    public void SetUserScale(float userScale)
    {
        var v = SanitizeScale(userScale, UserScale);
        if (v == UserScale) return;
        UserScale = v;
        _pendingRebuild = true;
    }

    /// <summary>True when a scale change is queued and waiting for a frame boundary.</summary>
    public bool HasPendingRebuild => _pendingRebuild;

    /// <summary>
    /// If a rebuild is queued, perform it now. MUST be called at a frame boundary — after
    /// <c>rlImGui.End()</c> / before the next <c>rlImGui.Begin()</c>. Returns true if a
    /// rebuild ran.
    /// </summary>
    public bool ApplyPendingRebuild()
    {
        if (!_pendingRebuild) return false;
        _pendingRebuild = false;
        Rebuild(EffectiveScale);
        return true;
    }

    // ── Core bake ─────────────────────────────────────────────────────────────

    private unsafe void Rebuild(float scale)
    {
        if (ImGui.GetCurrentContext() == IntPtr.Zero)
            return; // no context — nothing to bake (defensive; should not happen in the shell)

        EnsureRangesPinned();

        string robotoPath = EnsureExtracted("Roboto-Regular.ttf", ref _robotoPath,
            EmbeddedFontResources.GetRobotoRegularTtfBytes);
        string faPath = EnsureExtracted("fa-solid-900.ttf", ref _faPath,
            EmbeddedFontResources.GetFontAwesomeSolidTtfBytes);

        var io = ImGui.GetIO();
        io.Fonts.Clear();

        float uiPx = MathF.Round(BaseUiPx * scale);

        // 1) UI chrome face → becomes Fonts[0], the implicit ImGui default.
        nint defaultFont = (nint)io.Fonts
            .AddFontFromFileTTF(robotoPath, uiPx, default, _uiRangesHandle.AddrOfPinnedObject())
            .NativePtr;

        // 2) Merge Font Awesome solid icons onto the UI face (same logical font).
        MergeFontAwesome(io, faPath, uiPx);

        // 3) Canvas ladder — one Roboto face per (scaled) ladder size, keyed by baked px.
        var ladder = new Dictionary<float, nint>(LadderBaseSizes.Length);
        foreach (float baseSize in LadderBaseSizes)
        {
            float px = MathF.Round(baseSize * scale);
            if (px < 1f) px = 1f;
            if (ladder.ContainsKey(px)) continue; // rounding collision — one face is enough
            nint ptr = (nint)io.Fonts
                .AddFontFromFileTTF(robotoPath, px, default, _canvasRangesHandle.AddrOfPinnedObject())
                .NativePtr;
            ladder[px] = ptr;
        }

        // 4) Build + upload to GPU (rlImGui rebuilds the backing texture).
        io.Fonts.Build();
        rlImGui.ReloadFonts();

        // Bilinear-filter the ImGui font atlas. Canvas node text is drawn with
        // dl.AddText(font, size, …) at arbitrary zoom-derived sizes (not the baked size); with
        // the default point sampling those downscaled glyphs alias into unreadable mush at small
        // zoom. Bilinear resamples them smoothly. At the baked size it is a no-op (1:1).
        nint fontTexId = io.Fonts.TexID;
        if (fontTexId != IntPtr.Zero)
        {
            var fontTex = new Raylib_cs.Texture2D { Id = (uint)fontTexId };
            Raylib_cs.Raylib.SetTextureFilter(fontTex, Raylib_cs.TextureFilter.Bilinear);
        }

        // 5) Scale widget metrics (padding/rounding/etc.) by the delta from the last bake.
        //    ScaleAllSizes multiplies current values, so apply the ratio, not the absolute.
        float ratio = scale / _appliedStyleScale;
        if (MathF.Abs(ratio - 1f) > 1e-4f)
            ImGui.GetStyle().ScaleAllSizes(ratio);
        _appliedStyleScale = scale;

        // 6) Publish for the canvas theme.
        EditorFontRegistry.Publish(defaultFont, ladder, scale);
    }

    private static unsafe void MergeFontAwesome(ImGuiIOPtr io, string faPath, float uiPx)
    {
        var cfgNative = ImGuiNative.ImFontConfig_ImFontConfig();
        var cfg = new ImFontConfigPtr(cfgNative);
        try
        {
            cfg.MergeMode        = true;   // fold glyphs into the preceding (UI) font
            cfg.PixelSnapH       = true;
            cfg.GlyphMinAdvanceX = uiPx;   // monospace the icons so they align in menus/toolbars
            // Icons render a touch smaller than text baseline reads best; keep at uiPx for crispness.
            io.Fonts.AddFontFromFileTTF(faPath, uiPx, cfg, _faRangesHandle.AddrOfPinnedObject());
        }
        finally
        {
            ImGuiNative.ImFontConfig_destroy(cfgNative);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static float SanitizeScale(float v, float fallback)
        => (float.IsNaN(v) || float.IsInfinity(v) || v <= 0f) ? fallback : v;

    /// <summary>Extract an embedded TTF to a stable temp path once, so ImGui can read it via
    /// AddFontFromFileTTF (which owns its own buffer — avoids native/managed alloc mismatch).</summary>
    private static string EnsureExtracted(string fileName, ref string? cached, Func<byte[]> loader)
    {
        if (cached != null && File.Exists(cached)) return cached;
        var dir = Path.Combine(Path.GetTempPath(), "HROT-fonts");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        byte[] bytes = loader();
        if (!File.Exists(path) || new FileInfo(path).Length != bytes.Length)
            File.WriteAllBytes(path, bytes);
        cached = path;
        return path;
    }
}
