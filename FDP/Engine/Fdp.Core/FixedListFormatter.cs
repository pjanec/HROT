using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Fdp.Core;

/// <summary>
/// FC-3c (Q#21-D3/D-p) — THE single definition of the fixed-list SUMMARY string:
/// <c>List&lt;Elem&gt;[N] Count=k {e0, e1, …}</c>, rendered from a wrapper's raw byte image.
/// <para>
/// Two hosts share it: the Blueprints debugger watch (transient bytes→string — the shape its
/// collectible-ALC constraint demands: nothing from the generated assembly is retained) and
/// the StructEdit <c>FixedListBufferViewProvider</c>'s collapsed row. The rendered element
/// window is F2-clamped to <c>min(max(Count,0), N)</c>, so a stale/garbage Count can never
/// drive an out-of-range read of the payload bytes.
/// </para>
/// </summary>
public static class FixedListFormatter
{
    /// <summary>
    /// Renders <paramref name="bytes"/> (the wrapper's byte image, starting at the wrapper's
    /// first byte) as the list summary string. False when <paramref name="wrapperType"/> is
    /// not a fixed-list wrapper (<see cref="FixedListShape"/>) or the image is too short.
    /// </summary>
    public static bool TryFormat(ReadOnlySpan<byte> bytes, Type wrapperType, out string formatted)
    {
        formatted = "";
        if (!FixedListShape.TryGet(wrapperType, out var elemType, out var bufType,
                out int capacity, out var countField, out var bufferField))
            return false;

        int countOffset, itemsOffset;
        try
        {
            countOffset = (int)Marshal.OffsetOf(wrapperType, countField.Name);
            itemsOffset = (int)Marshal.OffsetOf(wrapperType, bufferField.Name);
        }
        catch (ArgumentException) { return false; }        // non-blittable wrapper — cannot map bytes

        int elemSize = (int)typeof(Unsafe)
            .GetMethod(nameof(Unsafe.SizeOf))!
            .MakeGenericMethod(elemType).Invoke(null, null)!;
        if (elemSize <= 0 || bytes.Length < countOffset + 4) return false;

        int count = MemoryMarshal.Read<int>(bytes.Slice(countOffset, 4));
        int shown = Math.Min(Math.Max(count, 0), capacity);

        var sb = new StringBuilder();
        sb.Append("List<").Append(elemType.Name).Append(">[").Append(capacity)
          .Append("] Count=").Append(count).Append(" {");
        for (int i = 0; i < shown; i++)
        {
            int start = itemsOffset + i * elemSize;
            if (start + elemSize > bytes.Length) break;
            if (i > 0) sb.Append(", ");
            sb.Append(FormatElement(bytes.Slice(start, elemSize), elemType));
        }
        sb.Append('}');
        formatted = sb.ToString();
        return true;
    }

    private static string FormatElement(ReadOnlySpan<byte> bytes, Type t)
    {
        if (t == typeof(int))     return MemoryMarshal.Read<int>(bytes).ToString();
        if (t == typeof(float))   return MemoryMarshal.Read<float>(bytes).ToString();
        if (t == typeof(bool))    return (bytes[0] != 0).ToString();
        if (t == typeof(uint))    return MemoryMarshal.Read<uint>(bytes).ToString();
        if (t == typeof(long))    return MemoryMarshal.Read<long>(bytes).ToString();
        if (t == typeof(ulong))   return MemoryMarshal.Read<ulong>(bytes).ToString();
        if (t == typeof(double))  return MemoryMarshal.Read<double>(bytes).ToString();
        if (t == typeof(byte))    return bytes[0].ToString();
        if (t == typeof(sbyte))   return unchecked((sbyte)bytes[0]).ToString();
        if (t == typeof(short))   return MemoryMarshal.Read<short>(bytes).ToString();
        if (t == typeof(ushort))  return MemoryMarshal.Read<ushort>(bytes).ToString();
        if (t == typeof(Vector2)) return MemoryMarshal.Read<Vector2>(bytes).ToString();
        if (t == typeof(Vector3)) return MemoryMarshal.Read<Vector3>(bytes).ToString();
        if (t == typeof(Vector4)) return MemoryMarshal.Read<Vector4>(bytes).ToString();
        if (t.IsEnum)
        {
            var underlying = Enum.GetUnderlyingType(t);
            if (underlying == typeof(int))
                return Enum.ToObject(t, MemoryMarshal.Read<int>(bytes)).ToString() ?? "?";
        }
        return "…";                                        // composite struct — summarized
    }
}
