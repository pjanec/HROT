using Fdp.Core;
using System.Text.Json.Serialization;

namespace Hrot.IG.Components;

/// <summary>
/// Rendering style parameters for a map visual overlay entity.
///
/// Populated by <c>MapVisualOverlayIngressTranslator</c> from the
/// <c>MapVisualOverlay.StyleOverrideJson</c> DDS field (if present), or
/// from <see cref="Default"/> when no override is provided.
///
/// <para>Stored as a blittable unmanaged struct so that it can live in
/// contiguous ECS archetype storage.  Colour channels are stored in compact
/// <c>Color32</c> fields to avoid a dependency on <c>Raylib_cs</c> in the
/// <c>Hrot.Map.Common</c> shared project; callers in <c>Hrot.IG</c>
/// convert to <c>Raylib_cs.Color</c> when needed.</para>
///
/// <para>Defined in <c>Hrot.Map.Common</c> so that both the IG and
/// SimHost/Runner projects can reference it without circular dependencies.</para>
/// </summary>
[ComponentId(GlobalComponentIds.MapOverlayStyle)]
public struct MapOverlayStyle
{
    // ── Fill colour ───────────────────────────────────────────────────────────

    /// <summary>Fill colour as RGBA bytes.</summary>
    public Color32 FillColor;
    [JsonIgnore]
    public byte FillR
    {
        readonly get => FillColor.R;
        set => FillColor.R = value;
    }
    /// <summary>Green channel of the polygon fill colour (0–255).</summary>
    [JsonIgnore]
    public byte FillG
    {
        readonly get => FillColor.G;
        set => FillColor.G = value;
    }
    /// <summary>Blue channel of the polygon fill colour (0–255).</summary>
    [JsonIgnore]
    public byte FillB
    {
        readonly get => FillColor.B;
        set => FillColor.B = value;
    }
    /// <summary>Alpha channel of the polygon fill colour (0–255).</summary>
    [JsonIgnore]
    public byte FillA
    {
        readonly get => FillColor.A;
        set => FillColor.A = value;
    }

    // ── Border colour ─────────────────────────────────────────────────────────

    /// <summary>Border colour as RGBA bytes.</summary>
    public Color32 BorderColor;
    [JsonIgnore]
    public byte BorderR
    {
        readonly get => BorderColor.R;
        set => BorderColor.R = value;
    }
    /// <summary>Green channel of the polygon border colour (0–255).</summary>
    [JsonIgnore]
    public byte BorderG
    {
        readonly get => BorderColor.G;
        set => BorderColor.G = value;
    }
    /// <summary>Blue channel of the polygon border colour (0–255).</summary>
    [JsonIgnore]
    public byte BorderB
    {
        readonly get => BorderColor.B;
        set => BorderColor.B = value;
    }
    /// <summary>Alpha channel of the polygon border colour (0–255).</summary>
    [JsonIgnore]
    public byte BorderA
    {
        readonly get => BorderColor.A;
        set => BorderColor.A = value;
    }

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
            FillColor     = new Color32(255, 0, 0, 80),
            BorderColor   = new Color32(255, 255, 255, 255),
            LineThickness = 2.0f,
            IsClosed      = true,
        };

    // ── JSON serialization ────────────────────────────────────────────────────

    /// <summary>
    /// Serialises this style to the JSON format consumed by <see cref="FromJson"/>.
    /// </summary>
    public string ToJson()
    {
        static string Hex(byte b) => b.ToString("X2");
        string fill   = $"#{Hex(FillR)}{Hex(FillG)}{Hex(FillB)}{Hex(FillA)}";
        string border = $"#{Hex(BorderR)}{Hex(BorderG)}{Hex(BorderB)}{Hex(BorderA)}";
        string thickness = LineThickness.ToString("G9", System.Globalization.CultureInfo.InvariantCulture);
        return $"{{\"FillColor\":\"{fill}\",\"BorderColor\":\"{border}\",\"LineThickness\":{thickness}}}";
    }

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
