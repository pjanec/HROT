using System.Globalization;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.Host;

/// <summary>
/// Converts between a <see cref="LiteralNode.ValueJson"/> (a raw C# literal — e.g. <c>5f</c>,
/// <c>"hi"</c>, <c>true</c>) and the plain, designer-friendly value shown in the inline editor.
/// This is where the C# literal syntax (float <c>f</c> suffix, string quotes) is added/stripped, so
/// the designer only ever types a bare value.
/// <para>
/// Only the common inline-editable types are handled (<see cref="HasInlineEditor"/>); rarer types
/// keep their value in the node title and are edited in the Details panel.
/// </para>
/// </summary>
internal static class LiteralValueJson
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Types that get an inline body editor (and a clean, value-free node title).</summary>
    public static bool HasInlineEditor(string? typeId) => typeId switch
    {
        BlueprintTypeSystem.Int32   => true,
        BlueprintTypeSystem.Single  => true,
        BlueprintTypeSystem.Float64 => true,
        BlueprintTypeSystem.Bool    => true,
        BlueprintTypeSystem.String  => true,
        BlueprintTypeSystem.Byte    => true,
        _                           => false,
    };

    /// <summary>
    /// ValueJson (C# literal) → the plain string the inline pin editor expects (the form
    /// <see cref="BlueprintPinDefaultValue.ParseValue"/> can turn back into a boxed value).
    /// </summary>
    public static string ToEditString(string? typeId, string? valueJson)
    {
        var raw = valueJson ?? string.Empty;
        return typeId switch
        {
            BlueprintTypeSystem.Single  => raw.TrimEnd('f', 'F'),
            BlueprintTypeSystem.String  => raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"' ? raw[1..^1] : raw,
            _                           => raw,
        };
    }

    /// <summary>
    /// Boxed editor value → the ValueJson C# literal (adds the float <c>f</c> suffix / string quotes
    /// the Roslyn emitter needs). Matches <see cref="LiteralNodeDrawer"/>'s per-type formatting.
    /// </summary>
    public static string ToValueJson(string? typeId, object? value)
    {
        switch (typeId)
        {
            case BlueprintTypeSystem.Int32:
                return Convert.ToInt32(value ?? 0, Inv).ToString(Inv);
            case BlueprintTypeSystem.Single:
                return Convert.ToSingle(value ?? 0f, Inv).ToString(Inv) + "f";
            case BlueprintTypeSystem.Float64:
                return Convert.ToDouble(value ?? 0.0, Inv).ToString(Inv);
            case BlueprintTypeSystem.Bool:
                return (value is bool b && b) ? "true" : "false";
            case BlueprintTypeSystem.String:
                return "\"" + (value?.ToString() ?? string.Empty) + "\"";
            case BlueprintTypeSystem.Byte:
                return Convert.ToByte(Math.Clamp(Convert.ToInt32(value ?? 0, Inv), byte.MinValue, byte.MaxValue)).ToString(Inv);
            default:
                return value?.ToString() ?? string.Empty;
        }
    }
}
