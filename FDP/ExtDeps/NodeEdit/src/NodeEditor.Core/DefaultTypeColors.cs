using System.Numerics;
using NodeEditor.Primitives;

namespace NodeEditor.Core;

/// <summary>
/// Default type-key → color mapping used as fallback when the host's
/// type system doesn't specify one. Colors are RGBA in 0–1 range.
/// </summary>
public static class DefaultTypeColors
{
    private static readonly Dictionary<string, Vector4> _map = new(StringComparer.Ordinal)
    {
        // Booleans
        ["System.Boolean"] = ToRgba(0xE7, 0x4C, 0x3C, 0xFF),

        // Integers
        ["System.Byte"]    = ToRgba(0x5D, 0xAD, 0xE2, 0xFF),
        ["System.Int16"]   = ToRgba(0x5D, 0xAD, 0xE2, 0xFF),
        ["System.Int32"]   = ToRgba(0x5D, 0xAD, 0xE2, 0xFF),
        ["System.Int64"]   = ToRgba(0x5D, 0xAD, 0xE2, 0xFF),

        // Floats
        ["System.Single"]  = ToRgba(0xA6, 0xE2, 0x2E, 0xFF),
        ["System.Double"]  = ToRgba(0xA6, 0xE2, 0x2E, 0xFF),

        // Strings
        ["System.String"]  = ToRgba(0xE8, 0x4F, 0x8E, 0xFF),

        // Vectors / math
        ["System.Numerics.Vector2"]     = ToRgba(0xF1, 0xC4, 0x0F, 0xFF),
        ["System.Numerics.Vector3"]     = ToRgba(0xF1, 0xC4, 0x0F, 0xFF),
        ["System.Numerics.Vector4"]     = ToRgba(0xF1, 0xC4, 0x0F, 0xFF),
        ["System.Numerics.Quaternion"]  = ToRgba(0xF1, 0xC4, 0x0F, 0xFF),

        // Color
        ["NodeEditor.Color"]            = ToRgba(0xFF, 0x6B, 0x9D, 0xFF),

        // Guid
        ["System.Guid"]                 = ToRgba(0x2C, 0x3E, 0x50, 0xFF),
    };

    /// <summary>
    /// Get the default color for a type key. Returns mid-blue as fallback
    /// for unrecognized types (treated as generic struct).
    /// </summary>
    public static Vector4 GetColor(TypeKey key)
    {
        return _map.TryGetValue(key.Id, out var c)
            ? c
            : ToRgba(0x54, 0x99, 0xC7, 0xFF); // generic struct fallback
    }

    /// <summary>Exec pin/wire color (white).</summary>
    public static Vector4 ExecColor => new(1, 1, 1, 1);

    private static Vector4 ToRgba(byte r, byte g, byte b, byte a) =>
        new(r / 255f, g / 255f, b / 255f, a / 255f);
}
