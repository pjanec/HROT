using System;
using System.Text;

namespace Fdp.Presentation.Utils;

/// <summary>
/// Decoding of the fixed-size, NUL-terminated <c>byte[]</c> buffers that
/// <c>ImGui.InputText</c> writes into.
///
/// <para>
/// ImGui writes the new text plus a single NUL terminator and <b>leaves the remainder of
/// the buffer untouched</b>.  Whenever the new value is shorter than the previous one, the
/// tail of the previous value survives past the terminator:
/// </para>
///
/// <code>
/// offset:  0    1    2    3    4    5    6
/// before: 'P'  'a'  'r'  'a'  'm'  '0'  \0      "Param0"
/// after:  'P'  '1'  \0   'a'  'm'  '0'  \0      wrote "P1\0"; bytes 3-5 are stale
///                   ^ terminator
/// </code>
///
/// <para>
/// Decoding the whole buffer and calling <c>TrimEnd('\0')</c> therefore yields
/// <c>"P1\0am0"</c> -- an identifier containing an interior NUL, which then gets persisted
/// and reaches the compiler.  <c>Trim()</c> does not help either: it strips only leading and
/// trailing whitespace, never an interior NUL.  The only correct read is to stop at the
/// <b>first</b> terminator.
/// </para>
///
/// <para>See BP-86.  Route every ImGui text-buffer decode through this helper.</para>
/// </summary>
public static class ImGuiBufferText
{
    /// <summary>
    /// Decodes a NUL-terminated ImGui text buffer, stopping at the first terminator.
    /// </summary>
    /// <param name="buffer">The buffer handed to <c>ImGui.InputText</c>. May be null.</param>
    /// <returns>
    /// The text up to (excluding) the first NUL, or the whole buffer when it has no
    /// terminator. Returns <see cref="string.Empty"/> for a null or empty buffer, and for a
    /// buffer whose first byte is already the terminator.
    /// </returns>
    public static string Decode(byte[]? buffer)
        => buffer is null || buffer.Length == 0
            ? string.Empty
            : Decode(new ReadOnlySpan<byte>(buffer));

    /// <summary>
    /// Span overload of <see cref="Decode(byte[])"/>.
    /// </summary>
    public static string Decode(ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty) return string.Empty;

        int end = buffer.IndexOf((byte)0);
        if (end < 0) end = buffer.Length;   // unterminated: the whole buffer is the value

        return end == 0 ? string.Empty : Encoding.UTF8.GetString(buffer.Slice(0, end));
    }

    /// <summary>
    /// <see cref="Decode(byte[])"/> followed by <see cref="string.Trim()"/>, for the call
    /// sites that additionally want surrounding whitespace removed.  Kept as a distinct
    /// method so the NUL handling cannot be accidentally dropped when the trim is copied.
    /// </summary>
    public static string DecodeTrimmed(byte[]? buffer) => Decode(buffer).Trim();
}
