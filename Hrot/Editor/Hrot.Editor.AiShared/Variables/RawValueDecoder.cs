using System;
using System.Runtime.InteropServices;

namespace Hrot.Editor.AiShared.Variables;

/// <summary>
/// ⭐⭐ <b>The production <see cref="DecodeRawValue"/>.</b> Turns a variable's raw bytes into a CLR
/// value so <see cref="VariableValueFormatter"/> has something to render.
///
/// <para><b>Why this exists.</b> 📐 Measured at Batch 79: every <c>VariableValueFormatter</c> in the
/// repo was in a TEST, each with its own inline lambda. ⛔ Wiring the table with a
/// <c>(_, _) =&gt; null</c> default would have rendered every cell <c>&lt;unreadable&gt;</c> — a silent
/// default that reads as a working panel, which is the exact shape this programme keeps finding.</para>
///
/// <para><b>Blittable only, and that is the honest boundary.</b> A variable's bytes are a struct
/// image: primitives, enums and <c>[StructLayout]</c> structs all decode; anything with references
/// does not, and returns <c>null</c> rather than a guess. ⭐ The formatter maps both <c>null</c> and
/// "the bytes came back unchanged" to <c>&lt;unreadable&gt;</c>, so a failure is visible rather than
/// rendered as hex.</para>
///
/// <para>⚠ <b>Undersized input is a failure, not a partial read.</b> Decoding fewer bytes than the
/// type needs would produce a plausible-looking wrong value — worse than <c>&lt;unreadable&gt;</c>.
/// Oversized input is fine: a row reads a slice of a wider buffer.</para>
/// </summary>
public static class RawValueDecoder
{
    /// <summary>The delegate to hand to <see cref="VariableValueFormatter"/>.</summary>
    public static DecodeRawValue Instance { get; } = Decode;

    /// <summary>
    /// Decodes <paramref name="bytes"/> as <paramref name="type"/>, or returns <c>null</c> when it
    /// cannot. ⛔ Never throws — a monitor must not take the window down.
    /// </summary>
    public static object? Decode(byte[] bytes, Type type)
    {
        if (bytes is null || type is null || bytes.Length == 0) return null;

        // ⭐ bool first: Marshal.SizeOf(bool) is 4 by default while the blackboard packs it as 1
        //   (the [MarshalAs(I1)] the DTO emitter injects), so the generic path would read too much.
        if (type == typeof(bool)) return bytes[0] != 0;

        var target = type.IsEnum ? Enum.GetUnderlyingType(type) : type;

        int size;
        try { size = Marshal.SizeOf(target); }
        catch { return null; }                       // not a blittable layout

        if (size <= 0 || bytes.Length < size) return null;

        object? value;
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { value = Marshal.PtrToStructure(handle.AddrOfPinnedObject(), target); }
        catch { return null; }
        finally { handle.Free(); }

        if (value is null) return null;

        // ⭐ Re-wrap an enum so the formatter prints the NAME, not the ordinal — a designer reading
        //   a posture or a lane wants the member, and that is the whole reason enums are offered.
        return type.IsEnum ? Enum.ToObject(type, value) : value;
    }
}
