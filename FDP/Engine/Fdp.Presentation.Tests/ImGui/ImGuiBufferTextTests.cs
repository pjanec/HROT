using System.Text;
using Fdp.Presentation.Utils;

namespace Fdp.Presentation.Tests.ImGui;

/// <summary>
/// BP-86 — <see cref="ImGuiBufferText"/> must stop at the FIRST NUL.
///
/// <para>
/// The shipped bug was <c>Encoding.UTF8.GetString(buf).TrimEnd('\0')</c> at seven sites.
/// <c>TrimEnd</c> strips only <b>trailing</b> NULs, so a rename to a shorter string left the
/// tail of the previous value alive behind the terminator and produced an identifier with an
/// interior NUL (<c>"P1\0am0"</c>), which was then persisted to <c>.bp.json</c>.
/// </para>
///
/// <para>
/// These are one test per <b>behaviour</b>, not one per call site — the seven sites all route
/// through this helper now.
/// </para>
/// </summary>
public sealed class ImGuiBufferTextTests
{
    /// <summary>
    /// Reproduces exactly what ImGui leaves in the buffer: the buffer is seeded with
    /// <paramref name="seed"/>, then <paramref name="typed"/> plus one terminator is written
    /// over the front, leaving the seed's tail untouched.
    /// </summary>
    private static byte[] SimulateInputText(string seed, string typed, int capacity = 256)
    {
        var buf = new byte[capacity];
        Encoding.UTF8.GetBytes(seed).CopyTo(buf, 0);
        buf[Encoding.UTF8.GetByteCount(seed)] = 0;          // seed's own terminator

        var typedBytes = Encoding.UTF8.GetBytes(typed);
        typedBytes.CopyTo(buf, 0);
        buf[typedBytes.Length] = 0;                          // new terminator, tail left stale
        return buf;
    }

    // ── the reported defect ──────────────────────────────────────────────────

    [Fact]
    public void Decode_ShorterThanPrevious_DropsStaleTail()
    {
        // "Param0" overwritten with "P1" -> bytes 3..5 still hold "am0"
        var buf = SimulateInputText("Param0", "P1");

        var actual = ImGuiBufferText.Decode(buf);

        Assert.Equal("P1", actual);
        Assert.DoesNotContain('\0', actual);
    }

    [Fact]
    public void Decode_ShorterThanPrevious_OldTrimEndIdiomWouldHaveFailed()
    {
        // Locks in WHY the helper exists: the previous idiom is still wrong on this input,
        // so a future "simplification" back to TrimEnd cannot pass this suite.
        var buf = SimulateInputText("Param0", "P1");

        var oldIdiom = Encoding.UTF8.GetString(buf).TrimEnd('\0');

        Assert.Equal("P1\0am0", oldIdiom);
        Assert.NotEqual(oldIdiom, ImGuiBufferText.Decode(buf));
    }

    [Fact]
    public void Decode_TrimDoesNotRescueTheOldIdiom()
    {
        // Sites 4-6 chained .Trim(); this proves that never helped.
        var buf = SimulateInputText("Param0", "P1");

        var oldIdiomTrimmed = Encoding.UTF8.GetString(buf).TrimEnd('\0').Trim();

        Assert.Contains('\0', oldIdiomTrimmed);
    }

    // ── no regression on the cases that already worked ───────────────────────

    [Fact]
    public void Decode_LongerThanPrevious_IsExact()
    {
        var buf = SimulateInputText("P1", "LongerName");

        Assert.Equal("LongerName", ImGuiBufferText.Decode(buf));
    }

    [Fact]
    public void Decode_SameLengthAsPrevious_IsExact()
    {
        var buf = SimulateInputText("Param0", "Param9");

        Assert.Equal("Param9", ImGuiBufferText.Decode(buf));
    }

    // ── edges ────────────────────────────────────────────────────────────────

    [Fact]
    public void Decode_EmptyBuffer_ReturnsEmpty_DoesNotThrow()
    {
        Assert.Equal(string.Empty, ImGuiBufferText.Decode(new byte[0]));
    }

    [Fact]
    public void Decode_NullBuffer_ReturnsEmpty_DoesNotThrow()
    {
        Assert.Equal(string.Empty, ImGuiBufferText.Decode((byte[]?)null));
    }

    [Fact]
    public void Decode_AllZeroBuffer_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, ImGuiBufferText.Decode(new byte[64]));
    }

    [Fact]
    public void Decode_ClearedThenRetyped_ReturnsOnlyTheNewText()
    {
        // User selects-all and types a single character over a long previous value.
        var buf = SimulateInputText("AVeryLongParameterName", "x");

        Assert.Equal("x", ImGuiBufferText.Decode(buf));
    }

    [Fact]
    public void Decode_UnterminatedFullBuffer_ReturnsWholeBuffer()
    {
        var buf = Encoding.UTF8.GetBytes("abcd");   // no terminator at all

        Assert.Equal("abcd", ImGuiBufferText.Decode(buf));
    }

    [Fact]
    public void Decode_MultiByteUtf8_IsNotSplit()
    {
        var buf = SimulateInputText("Ærøskøbing", "Æro");

        Assert.Equal("Æro", ImGuiBufferText.Decode(buf));
    }

    // ── DecodeTrimmed ────────────────────────────────────────────────────────

    [Fact]
    public void DecodeTrimmed_StripsWhitespaceButStillStopsAtFirstNul()
    {
        var buf = SimulateInputText("Param0", "  P1  ");

        Assert.Equal("P1", ImGuiBufferText.DecodeTrimmed(buf));
    }

    [Fact]
    public void DecodeTrimmed_NullBuffer_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, ImGuiBufferText.DecodeTrimmed(null));
    }
}
