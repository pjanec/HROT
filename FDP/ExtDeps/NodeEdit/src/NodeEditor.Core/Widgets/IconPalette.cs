using System.Numerics;

namespace NodeEditor.Core.Widgets;

/// <summary>
/// ⭐ Q-C1 / U-2 — the ONE colour list for icons (docs/UX/DESIGN_Colour_Icons.md §2 · DESIGN_Details_Forms.md §6.3): the status tokens and the hues. A call site never picks
/// its own colour (R-174); it names a token. The hues are the mode indicator's (DAR §9a, R-3060) so the tokens ARE what <c>ModeGlyph</c> drew; the status values are the forms' palette.
/// </summary>
public static class IconPalette
{
    // ── status tokens (forms §6.3) ───────────────────────────────────────────────────────────────────────────────────
    public static readonly Vector4 Error   = new(0.90f, 0.33f, 0.33f, 1f);
    public static readonly Vector4 Pending = new(0.95f, 0.75f, 0.20f, 1f);
    public static readonly Vector4 Info    = new(0.42f, 0.69f, 1.00f, 1f);
    public static readonly Vector4 Muted   = new(0.50f, 0.50f, 0.50f, 1f);

    // ── hues (colour icons §3) ───────────────────────────────────────────────────────────────────────────────────────
    public static readonly Vector4 Grey   = new(0.60f, 0.60f, 0.60f, 1f);
    public static readonly Vector4 Violet = new(0.66f, 0.45f, 0.92f, 1f);
    public static readonly Vector4 Green  = new(0.25f, 0.80f, 0.30f, 1f);
    public static readonly Vector4 Amber  = new(1.00f, 0.70f, 0.20f, 1f);
    public static readonly Vector4 Blue   = new(0.30f, 0.60f, 1.00f, 1f);
    public static readonly Vector4 Teal   = new(0.25f, 0.78f, 0.75f, 1f);
    public static readonly Vector4 Orange = new(1.00f, 0.55f, 0.20f, 1f);
    public static readonly Vector4 Pink   = new(0.95f, 0.45f, 0.70f, 1f);
    // ⚠ static initialisers run in textual order: an alias must come AFTER the field it copies
    public static readonly Vector4 Ok     = Green;
    public static readonly Vector4 Red    = Error;

    // ── one hue per view family ──────────────────────────────────────────────────────────────────────────────────────
    public static readonly Vector4 Hierarchy = Teal;
    public static readonly Vector4 Type      = Orange;
    public static readonly Vector4 Macro     = Violet;
    public static readonly Vector4 Layout    = Violet;
    public static readonly Vector4 Window    = Blue;
}
