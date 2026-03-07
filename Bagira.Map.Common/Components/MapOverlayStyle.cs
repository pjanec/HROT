using Fdp.Kernel;

namespace Bagira.IG.Components;

/// <summary>
/// Rendering style parameters for a map visual overlay entity.
///
/// Populated by <c>MapVisualOverlayIngressTranslator</c> from the
/// <c>MapVisualOverlay.StyleOverrideJson</c> DDS field (if present), or
/// from <see cref="Default"/> when no override is provided.
///
/// <para>Stored as a blittable unmanaged struct so that it can live in
/// contiguous ECS archetype storage.  Colour channels are stored as plain
/// <c>byte</c> fields to avoid a dependency on <c>Raylib_cs</c> in the
/// <c>Bagira.Map.Common</c> shared project; callers in <c>Bagira.IG</c>
/// convert to <c>Raylib_cs.Color</c> when needed.</para>
///
/// <para>Defined in <c>Bagira.Map.Common</c> so that both the IG and
/// SimHost/Runner projects can reference it without circular dependencies.</para>
/// </summary>
[ComponentId(GlobalComponentIds.MapOverlayStyle)]
public struct MapOverlayStyle
{
    // ── Fill colour ───────────────────────────────────────────────────────────

    /// <summary>Red channel of the polygon fill colour (0–255).</summary>
    public byte FillR;
    /// <summary>Green channel of the polygon fill colour (0–255).</summary>
    public byte FillG;
    /// <summary>Blue channel of the polygon fill colour (0–255).</summary>
    public byte FillB;
    /// <summary>Alpha channel of the polygon fill colour (0–255).</summary>
    public byte FillA;

    // ── Border colour ─────────────────────────────────────────────────────────

    /// <summary>Red channel of the polygon border colour (0–255).</summary>
    public byte BorderR;
    /// <summary>Green channel of the polygon border colour (0–255).</summary>
    public byte BorderG;
    /// <summary>Blue channel of the polygon border colour (0–255).</summary>
    public byte BorderB;
    /// <summary>Alpha channel of the polygon border colour (0–255).</summary>
    public byte BorderA;

    // ── Geometry parameters ───────────────────────────────────────────────────

    /// <summary>Border line thickness in pixels (world-space).</summary>
    public float LineThickness;

    /// <summary>
    /// When <c>true</c> the last vertex is connected back to the first,
    /// forming a closed polygon.  When <c>false</c> the shape is drawn as
    /// an open polyline.
    /// </summary>
    public bool IsClosed;

    // ── Factory helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns the default style: red semi-transparent fill, opaque white
    /// border, 2 px line thickness, closed polygon.
    /// </summary>
    public static MapOverlayStyle Default() =>
        new MapOverlayStyle
        {
            FillR         = 255, FillG   = 0,   FillB   = 0,   FillA   = 80,
            BorderR       = 255, BorderG = 255,  BorderB = 255, BorderA = 255,
            LineThickness = 2.0f,
            IsClosed      = true,
        };

    // ── JSON parsing ─────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a JSON style-override fragment and returns the resulting
    /// <see cref="MapOverlayStyle"/>.
    ///
    /// <para>Expected JSON shape (all fields optional; missing fields fall back
    /// to <see cref="Default"/>):</para>
    /// <code>
    /// {
    ///   "FillColor":     "#FF000050",   // RRGGBBAA hex
    ///   "BorderColor":   "#FFFFFFFF",
    ///   "LineThickness": 2.0
    /// }
    /// </code>
    /// </summary>
    /// <param name="json">
    /// Style-override JSON string.  May be <c>null</c> or empty, in which
    /// case <see cref="Default"/> is returned unchanged.
    /// </param>
    public static MapOverlayStyle FromJson(string? json)
    {
        var style = Default();

        if (string.IsNullOrWhiteSpace(json))
            return style;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("FillColor", out var fillEl) &&
                fillEl.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                if (TryParseColor(fillEl.GetString(),
                    out byte r, out byte g, out byte b, out byte a))
                {
                    style.FillR = r; style.FillG = g; style.FillB = b; style.FillA = a;
                }
            }

            if (root.TryGetProperty("BorderColor", out var borderEl) &&
                borderEl.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                if (TryParseColor(borderEl.GetString(),
                    out byte r, out byte g, out byte b, out byte a))
                {
                    style.BorderR = r; style.BorderG = g; style.BorderB = b; style.BorderA = a;
                }
            }

            if (root.TryGetProperty("LineThickness", out var thicknessEl) &&
                thicknessEl.TryGetSingle(out float t))
            {
                style.LineThickness = t;
            }
        }
        catch
        {
            // Malformed JSON — fall back to default (already set above).
        }

        return style;
    }

    // ── Internal helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Parses an <c>#RRGGBB</c> or <c>#RRGGBBAA</c> hex colour string.
    /// Returns <c>false</c> when the string is not in a recognised format.
    /// </summary>
    private static bool TryParseColor(
        string? hex,
        out byte r, out byte g, out byte b, out byte a)
    {
        r = g = b = 0; a = 255;

        if (string.IsNullOrEmpty(hex) || hex[0] != '#')
            return false;

        ReadOnlySpan<char> h = hex.AsSpan(1); // strip leading '#'

        if (h.Length == 6)
        {
            if (!byte.TryParse(h[0..2], System.Globalization.NumberStyles.HexNumber, null, out r)) return false;
            if (!byte.TryParse(h[2..4], System.Globalization.NumberStyles.HexNumber, null, out g)) return false;
            if (!byte.TryParse(h[4..6], System.Globalization.NumberStyles.HexNumber, null, out b)) return false;
            a = 255;
            return true;
        }

        if (h.Length == 8)
        {
            if (!byte.TryParse(h[0..2], System.Globalization.NumberStyles.HexNumber, null, out r)) return false;
            if (!byte.TryParse(h[2..4], System.Globalization.NumberStyles.HexNumber, null, out g)) return false;
            if (!byte.TryParse(h[4..6], System.Globalization.NumberStyles.HexNumber, null, out b)) return false;
            if (!byte.TryParse(h[6..8], System.Globalization.NumberStyles.HexNumber, null, out a)) return false;
            return true;
        }

        return false;
    }
}
