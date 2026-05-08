using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Raylib_cs;

namespace Fdp.Toolkit.Vis2D.Gizmos
{
    public static class RichTextRenderer
    {
        public static void DrawRichTextBadge(ref Fdp.Toolkit.Diagnostics.Gizmos.FixedString32 text, int screenX, int screenY, int fontSize = 12)
        {
            GizmoMap.Presentation.RichTextRenderer.DrawRichTextBadge(ref text, screenX, screenY, fontSize);
        }

        public static void DrawRichTextBadge(ref Fdp.Core.FixedString32 text, int screenX, int screenY, int fontSize = 12)
        {
            ref var converted = ref Unsafe.As<Fdp.Core.FixedString32, Fdp.Toolkit.Diagnostics.Gizmos.FixedString32>(ref text);
            GizmoMap.Presentation.RichTextRenderer.DrawRichTextBadge(ref converted, screenX, screenY, fontSize);
        }

        public static List<(string Text, Color Color)> ParseChunks(ref Fdp.Toolkit.Diagnostics.Gizmos.FixedString32 text)
        {
            return ParseChunksImpl(ref Unsafe.As<Fdp.Toolkit.Diagnostics.Gizmos.FixedString32, Fdp.Core.FixedString32>(ref text));
        }

        public static List<(string Text, Color Color)> ParseChunks(ref Fdp.Core.FixedString32 text)
        {
            return ParseChunksImpl(ref text);
        }

        private static List<(string Text, Color Color)> ParseChunksImpl(ref Fdp.Core.FixedString32 text)
        {
            var chunks = new List<(string Text, Color Color)>(4);

            ref byte dataStart = ref Unsafe.As<Fdp.Core.FixedString32, byte>(ref text);
            ReadOnlySpan<byte> span = MemoryMarshal.CreateReadOnlySpan(ref dataStart, 32);

            Color currentColor = Color.White;
            int runStart = 0;

            for (int i = 0; i <= 31; i++)
            {
                byte b = i < 32 ? span[i] : (byte)0x00;

                if (b == 0x00 || b < 0x20)
                {
                    if (i > runStart)
                    {
                        string s = Encoding.ASCII.GetString(span.Slice(runStart, i - runStart));
                        chunks.Add((s, currentColor));
                    }

                    if (b == 0x00) break;
                    currentColor = ColorForControlByte(b);
                    runStart = i + 1;
                }
            }

            return chunks;
        }

        private static Color ColorForControlByte(byte b)
        {
            return b switch
            {
                0x01 => Color.Red,
                0x02 => Color.Green,
                0x03 => Color.Yellow,
                _ => Color.White
            };
        }
    }
}
