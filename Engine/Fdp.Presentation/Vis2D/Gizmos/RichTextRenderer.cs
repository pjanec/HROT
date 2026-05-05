using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Fdp.Core;
using Raylib_cs;

namespace Fdp.Toolkit.Vis2D.Gizmos
{
    /// <summary>
    /// Renders rich-text entity badges using inline control bytes for color switching.
    /// Control byte mapping: 0x01 = Red, 0x02 = Green, 0x03 = Yellow, 0x00 = end-of-string,
    /// any other value below 0x20 = White (default).
    /// </summary>
    public static class RichTextRenderer
    {
        private const int DefaultFontSize = 12;

        /// <summary>
        /// Draws a rich-text badge at the given screen coordinates.
        /// Parses control bytes inline without heap allocation per chunk (uses stackalloc).
        /// </summary>
        public static unsafe void DrawRichTextBadge(
            ref FixedString32 text,
            int screenX, int screenY,
            int fontSize = DefaultFontSize)
        {
            // Get raw byte span via Unsafe reinterpret.
            ref byte dataStart = ref Unsafe.As<FixedString32, byte>(ref text);
            ReadOnlySpan<byte> span = MemoryMarshal.CreateReadOnlySpan(ref dataStart, 32);

            Color currentColor = Color.White;
            byte* chunk        = stackalloc byte[33]; // 32 chars + null terminator
            int   chunkLen     = 0;
            int   cursorX      = screenX;

            for (int i = 0; i < 32; i++)
            {
                byte b = span[i];

                if (b == 0x00)
                {
                    // End of string: flush remaining chunk.
                    if (chunkLen > 0)
                    {
                        chunk[chunkLen] = 0;
                        string s = Encoding.ASCII.GetString(new ReadOnlySpan<byte>(chunk, chunkLen));
                        Raylib.DrawText(s, cursorX, screenY, fontSize, currentColor);
                        cursorX += Raylib.MeasureText(s, fontSize);
                    }
                    break;
                }

                if (b < 0x20)
                {
                    // Control byte: flush current chunk, switch color.
                    if (chunkLen > 0)
                    {
                        chunk[chunkLen] = 0;
                        string s = Encoding.ASCII.GetString(new ReadOnlySpan<byte>(chunk, chunkLen));
                        Raylib.DrawText(s, cursorX, screenY, fontSize, currentColor);
                        cursorX += Raylib.MeasureText(s, fontSize);
                        chunkLen = 0;
                    }
                    currentColor = ColorForControlByte(b);
                }
                else
                {
                    chunk[chunkLen++] = b;
                }
            }
        }

        /// <summary>
        /// Parses a <see cref="FixedString32"/> into color-annotated text chunks without
        /// issuing any draw calls. Intended for unit testing.
        /// </summary>
        internal static List<(string Text, Color Color)> ParseChunks(ref FixedString32 text)
        {
            var chunks = new List<(string Text, Color Color)>(4);

            ref byte dataStart = ref Unsafe.As<FixedString32, byte>(ref text);
            ReadOnlySpan<byte> span = MemoryMarshal.CreateReadOnlySpan(ref dataStart, 32);

            Color currentColor = Color.White;
            int   runStart     = 0;

            for (int i = 0; i <= 31; i++)
            {
                byte b = (i < 32) ? span[i] : (byte)0x00;

                if (b == 0x00 || b < 0x20)
                {
                    // Flush accumulated ASCII run.
                    if (i > runStart)
                    {
                        string s = Encoding.ASCII.GetString(span.Slice(runStart, i - runStart));
                        chunks.Add((s, currentColor));
                    }

                    if (b == 0x00) break; // End of string.

                    currentColor = ColorForControlByte(b);
                    runStart     = i + 1;
                }
            }

            return chunks;
        }

        private static Color ColorForControlByte(byte b) => b switch
        {
            0x01 => Color.Red,
            0x02 => Color.Green,
            0x03 => Color.Yellow,
            _    => Color.White,
        };
    }
}
