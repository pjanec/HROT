using System;
using System.Runtime.InteropServices;
using Fdp.Core;
using Hrot.Editor.AiShared.Variables;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Variables;

/// <summary>
/// ⭐⭐ <b><c>C-table</c> §4b/§9 — how a value is rendered in the cell and in its tooltip.</b>
///
/// <para>
/// ⛔⛔ <b>The rail that matters: an undecodable value renders <c>&lt;unreadable&gt;</c>, NEVER hex.</b>
/// 🔴 Raw hex in the watch panel was <c>BP-01</c>'s user-visible symptom, and it came from
/// <c>MarshalFromBytes</c> falling through to <c>return bytes</c>. ⇒ the decoder returning its input
/// unchanged is its <i>"I could not"</i> signal, and the formatter must translate that into words.
/// </para>
/// </summary>
public sealed class VariableValueFormatterTests
{
    private struct Vec3 { public float X, Y, Z; }

    private static Entity Ent(int i) => new Entity(i, 1);

    private static VariableRow Row(byte[] bytes, Type? clr, bool written = true, bool stale = false)
        => new(
            Origin:    new VariableRowOrigin(Guid.NewGuid(), Ent(1), "Variables", "V", "Asset"),
            ShortName: "V", TypeText: clr?.Name ?? "?", ClrType: clr,
            ReadValue: () => bytes,
            IsStale:   stale,
            HasEverBeenWritten: written);

    /// <summary>The real decoder's contract, in miniature: primitives and blittable structs decode;
    /// anything else comes back as the bytes it was given.</summary>
    private static readonly DecodeRawValue Decoder = (bytes, type) =>
    {
        if (type == typeof(int))   return MemoryMarshal.Read<int>(bytes);
        if (type == typeof(float)) return MemoryMarshal.Read<float>(bytes);
        if (type == typeof(bool))  return bytes[0] != 0;
        if (type == typeof(Vec3) && bytes.Length == 12) return MemoryMarshal.Read<Vec3>(bytes);
        return bytes;                                    // ⛔ the "I could not" signal
    };

    private static readonly VariableValueFormatter Fmt = new(Decoder);

    private static byte[] I32(int v) { var b = new byte[4]; MemoryMarshal.Write(b, in v); return b; }

    [Fact]
    public void Primitives_RenderInlineAndFormatted()
    {
        Assert.Equal("80", Fmt.Cell(Row(I32(80), typeof(int))));

        var f = new byte[4]; float val = 12.5f; MemoryMarshal.Write(f, in val);
        Assert.Equal("12.5", Fmt.Cell(Row(f, typeof(float))));

        Assert.Equal("true", Fmt.Cell(Row(new byte[] { 1 }, typeof(bool))));
    }

    /// <summary>⭐ A struct's cell is ONE LINE; its tooltip is multi-line, one field per line.
    /// ⛔ Same decode, same value — only the layout differs, because a second pretty-printer would be
    /// ruling 9 in miniature.</summary>
    [Fact]
    public void AStruct_IsOneLineInTheCell_AndMultiLineInTheTooltip()
    {
        var bytes = new byte[12];
        var v = new Vec3 { X = 1f, Y = 2f, Z = 3f };
        MemoryMarshal.Write(bytes, in v);
        var row = Row(bytes, typeof(Vec3));

        var cell = Fmt.Cell(row);
        Assert.DoesNotContain('\n', cell);
        Assert.StartsWith("{", cell);
        Assert.Contains("X=1", cell);

        var tip = Fmt.Tooltip(row);
        Assert.Contains('\n', tip);
        Assert.Contains("X = 1", tip);
        Assert.Contains("Z = 3", tip);
    }

    /// <summary>⭐ Elided to fit, with the ellipsis INSIDE the braces so the cell still reads as a
    /// struct rather than as truncated garbage.</summary>
    [Fact]
    public void ALongStruct_IsElidedButStillReadsAsAStruct()
    {
        string elided = VariableValueFormatterProbe.Elide("{Alpha=1, Bravo=2, Charlie=3, Delta=4}", 20);
        Assert.True(elided.Length <= 22);
        Assert.StartsWith("{", elided);
        Assert.EndsWith("…}", elided);
    }

    /// <summary>
    /// ⛔⛔ <b>The <c>BP-01</c> rail.</b> A type the decoder cannot handle must produce WORDS, and the
    /// cell must contain no hex whatsoever.
    /// </summary>
    [Fact]
    public void AnUndecodableValue_SaysSoInWords_AndNeverShowsHex()
    {
        var row = Row(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, typeof(DateTime));

        string cell = Fmt.Cell(row);
        Assert.Equal(VariableValueFormatter.Unreadable, cell);
        Assert.DoesNotContain("DE", cell, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BEEF", cell, StringComparison.OrdinalIgnoreCase);

        Assert.StartsWith(VariableValueFormatter.Unreadable, Fmt.Tooltip(row));
    }

    /// <summary>⚠ An unresolved CLR type is the other undecodable arm — same words, no throw.</summary>
    [Fact]
    public void ARowWithNoClrType_IsUnreadableRatherThanAThrow()
        => Assert.Equal(VariableValueFormatter.Unreadable, Fmt.Cell(Row(I32(1), clr: null)));

    /// <summary>⭐ Before the first write, Watch already had the right answer — <c>(pending)</c> via
    /// <c>!HasEverBeenWritten</c>. ✅ Reused, not reinvented.</summary>
    [Fact]
    public void BeforeTheFirstWrite_TheCellSaysPending()
        => Assert.Equal(VariableValueFormatter.PendingFirstWrite,
                        Fmt.Cell(Row(Array.Empty<byte>(), typeof(int), written: false)));

    /// <summary>⭐ A stale row shows its last known value; the tooltip says why it is greyed.
    /// ⛔ The greying itself is the renderer's job and is not headlessly checkable.</summary>
    [Fact]
    public void AStaleRow_KeepsItsValue_AndExplainsItselfInTheTooltip()
    {
        var row = Row(I32(7), typeof(int), stale: true);
        Assert.Equal("7", Fmt.Cell(row));
        Assert.Contains("no longer present", Fmt.Tooltip(row));
    }

    /// <summary>
    /// 🔴 <b>The formatter does NOT inherit the Watch buffer's 64-byte limit.</b>
    /// <c>Watch._valueBuffer</c> is <c>new byte[64]</c> and <c>WriteValue</c> THROWS above it, so
    /// <c>MemberSlotList</c> (96), <c>WaveState</c> (104) and <c>HillAttackSharedState</c> (136) cannot
    /// go through that carrier. ⇒ this asserts the limit is a property of that carrier, not of
    /// rendering — 136 bytes in, no throw.
    /// </summary>
    [Fact]
    public void A136ByteValue_FormatsWithoutInheritingTheSixtyFourByteLimit()
    {
        var big = new byte[136];
        var row = Row(big, typeof(DateTime));
        var ex  = Record.Exception(() => Fmt.Cell(row));
        Assert.Null(ex);
        Assert.Equal(VariableValueFormatter.Unreadable, Fmt.Cell(row));   // undecodable, but not fatal
    }
}

/// <summary>Test-only reach for the internal elision helper.</summary>
internal static class VariableValueFormatterProbe
{
    public static string Elide(string text, int width)
        => (string)typeof(VariableValueFormatter)
            .GetMethod("Elide", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object[] { text, width })!;
}
