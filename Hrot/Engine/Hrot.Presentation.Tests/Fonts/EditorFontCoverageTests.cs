using Fdp.Testing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Fdp.Presentation.Fonts;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Tests.Map3D;
using ImGuiNET;
using Xunit;

namespace Hrot.Presentation.Tests.Fonts;

/// <summary>
/// ⭐ <c>CE-1063</c> — a glyph the editor font does not carry draws as "?". 📐 Measured: Roboto's cmap has NO arrow, geometric shape, ⚠ or ✓
/// (only • and ◊), so "● EDIT", "▶ Enter Preview", "⚠ … not in the catalog" and every "A → B" diagnostic drew "?". The fix merges a DejaVu
/// Sans symbol subset under Roboto (<see cref="EditorFontService"/>); these rails hold it: every non-ASCII character in a UI string literal
/// must be baked into the UI face, and a real atlas must hold the glyphs.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class EditorFontCoverageTests
{
    /// <summary>Characters that NO baked font carries, allowed only because every use is a log line, an exception or generated-code
    /// comment text — never drawn. A new one fails the rail: draw it from Font Awesome (<see cref="IconsFontAwesome6"/>) or the fallback.</summary>
    private static readonly Dictionary<int, string> NeverDrawn = new()
    {
        [0x2B50] = "⭐ — log/exception/generated-comment text only",
        [0x26D4] = "⛔ — log/exception/generated-comment text only",
        [0x1F4C4] = "📄 — generated-comment text only",
    };

    [Fact]
    public void CE1063_TheFallback_CarriesWhatRobotoLacks()
    {
        var roboto = Cmap(EmbeddedFontResources.GetRobotoRegularTtfBytes());
        var symbols = Cmap(EmbeddedFontResources.GetSymbolFallbackTtfBytes());
        foreach (char c in "●■▶◀→⇒↑↓↻↔▾▸⚠✓✗✕⋯⅓₀")
        {
            Assert.False(roboto.Contains(c), $"Roboto now has U+{(int)c:X4} '{c}' — the fallback is no longer the reason it draws");
            Assert.True(symbols.Contains(c), $"the symbol fallback lacks U+{(int)c:X4} '{c}'");
            Assert.True(In(EditorFontService.UiSymbolFaceRanges, c), $"U+{(int)c:X4} '{c}' is not in the UI face's symbol ranges");
        }
        foreach (char c in "↑↓✕✓▾⚠")
            Assert.True(In(EditorFontService.CanvasSymbolFaceRanges, c), $"U+{(int)c:X4} '{c}' is not in the canvas faces' symbol ranges (node drawers use it)");
    }

    [Fact]
    public void CE1063_EveryUiStringLiteral_DrawsWithBakedGlyphs()
    {
        var roboto = Cmap(EmbeddedFontResources.GetRobotoRegularTtfBytes());
        var symbols = Cmap(EmbeddedFontResources.GetSymbolFallbackTtfBytes());
        bool Baked(int cp) =>
            (roboto.Contains(cp) && In(EditorFontService.UiFaceRanges, cp))
            || (symbols.Contains(cp) && In(EditorFontService.UiSymbolFaceRanges, cp))
            || (cp >= IconsFontAwesome6.IconMin && cp <= IconsFontAwesome6.IconMax);

        string root = TestRepo.Root;
        var literal = new Regex("\"(?:[^\"\\\\\\n]|\\\\.)*\"", RegexOptions.Compiled);
        var missing = new List<string>();
        int files = 0;
        foreach (var dir in new[] { "Hrot", "FDP/Engine/Fdp.Presentation", "FDP/Toolkits", "FDP/ExtDeps/NodeEdit/src/NodeEditor.UI", "FDP/ExtDeps/NodeEdit/src/NodeEditor.Core" })
        {
            foreach (var path in Directory.EnumerateFiles(Path.Combine(root, dir), "*.cs", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (rel.Contains("/bin/") || rel.Contains("/obj/") || rel.Contains("Tests")) continue;
                files++;
                int lineNo = 0;
                foreach (var line in File.ReadLines(path))
                {
                    lineNo++;
                    var trimmed = line.TrimStart();
                    if (trimmed.StartsWith("//") || trimmed.StartsWith("*")) continue;
                    foreach (Match m in literal.Matches(line))
                        foreach (var rune in m.Value.EnumerateRunes())
                            if (rune.Value > 0x7E && !Baked(rune.Value) && !NeverDrawn.ContainsKey(rune.Value))
                                missing.Add($"{rel}:{lineNo} U+{rune.Value:X4} '{rune}' in {m.Value[..Math.Min(60, m.Value.Length)]}");
                }
            }
        }
        Assert.True(files > 1000, $"the scan found only {files} files — wrong repo root?");
        Assert.True(missing.Count == 0, "glyphs no baked font carries (they draw \"?\"):\n" + string.Join("\n", missing.Distinct().Take(40)));
    }

    /// <summary>A node BADGE (an attachment pill's <c>Glyph</c>, a header glyph) draws with the canvas faces, which carry less than the UI
    /// face: Roboto's Latin/Cyrillic, the canvas symbol ranges and the NAMED Font Awesome icons only.</summary>
    [Fact]
    public void CE1063_EveryNodeBadgeGlyph_DrawsWithTheCanvasFaces()
    {
        var roboto = Cmap(EmbeddedFontResources.GetRobotoRegularTtfBytes());
        var symbols = Cmap(EmbeddedFontResources.GetSymbolFallbackTtfBytes());
        bool Canvas(int cp) =>
            cp <= 0x7E
            || (roboto.Contains(cp) && In(EditorFontService.CanvasFaceRanges, cp))
            || (symbols.Contains(cp) && In(EditorFontService.CanvasSymbolFaceRanges, cp))
            || In(EditorFontService.CanvasIconFaceRanges, cp);
        var named = typeof(IconsFontAwesome6).GetFields().Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!);

        string root = TestRepo.Root;
        var glyph = new Regex(@"\b(?:Header)?Glyph\s*(?:=>|=)\s*(?:[^;""\n]*\?\s*)?(?<v>""(?:[^""\\]|\\.)*""|(?:Fdp\.Presentation\.Fonts\.)?IconsFontAwesome6\.(?<n>\w+))", RegexOptions.Compiled);
        var missing = new List<string>();
        int badges = 0;
        foreach (var dir in new[] { "Hrot", "FDP/ExtDeps/NodeEdit/src/NodeEditor.UI" })
            foreach (var path in Directory.EnumerateFiles(Path.Combine(root, dir), "*.cs", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (rel.Contains("/bin/") || rel.Contains("/obj/") || rel.Contains("Tests")) continue;
                foreach (Match m in glyph.Matches(File.ReadAllText(path)))
                {
                    badges++;
                    string text = m.Groups["n"].Success
                        ? (named.TryGetValue(m.Groups["n"].Value, out var v) ? v : throw new Xunit.Sdk.XunitException($"{rel}: no IconsFontAwesome6.{m.Groups["n"].Value}"))
                        : m.Groups["v"].Value.Trim('"');
                    foreach (var rune in text.EnumerateRunes())
                        if (!Canvas(rune.Value)) missing.Add($"{rel} U+{rune.Value:X4} '{rune}' in {m.Value}");
                }
            }
        Assert.True(badges >= 5, $"found only {badges} badge glyphs — the scan is not reaching the attachments");
        Assert.True(missing.Count == 0, "node badge glyphs the canvas faces lack (they draw \"?\"):\n" + string.Join("\n", missing));
    }

    /// <summary>The bake itself: <see cref="EditorFontService"/> on a real ImGui context, then the UI face and a canvas face hold the glyphs.</summary>
    [SkippableFact]
    public void CE1063_TheBakedAtlas_HoldsTheSymbols()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin();
        new EditorFontService().Initialize(1f, 1f);
        // ⛔ Assert AFTER the frame: an exception between ImGui.Begin and End trips ImGui's native assert and kills the test host.
        var missing = new List<string>();
        try
        {
            frame.Step(() =>
            {
                ImGui.Begin("CE-1063");
                ImGui.Text("● EDIT  ▶ Enter  ⚠ warning  A → B  ✓ ok");
                unsafe
                {
                    var ui = ImGui.GetFont();
                    foreach (char c in "●■▶→⚠✓✗⋯")
                        if (ui.FindGlyphNoFallback(c).NativePtr == null) missing.Add($"UI face: U+{(int)c:X4} '{c}'");
                    if (ui.FindGlyphNoFallback(IconsFontAwesome6.Stop[0]).NativePtr == null) missing.Add("UI face: Font Awesome no longer merged");
                    var canvas = new ImFontPtr((ImFont*)EditorFontRegistry.ResolveCanvasFont(16f));
                    foreach (char c in "↑↓✕✓▾⚠")
                        if (canvas.FindGlyphNoFallback(c).NativePtr == null) missing.Add($"canvas face: U+{(int)c:X4} '{c}'");
                    foreach (var icon in new[] { IconsFontAwesome6.Bolt, IconsFontAwesome6.Link, IconsFontAwesome6.SatelliteDish, IconsFontAwesome6.ChartBar })
                        if (canvas.FindGlyphNoFallback(icon[0]).NativePtr == null) missing.Add($"canvas face: icon U+{(int)icon[0]:X4}");
                    if (canvas.FindGlyphNoFallback('\uf1b9').NativePtr != null) missing.Add("canvas face: an UNNAMED icon (car) — the whole PUA was merged");
                }
                ImGui.End();
            });
        }
        finally { EditorFontRegistry.Reset(); }
        Assert.True(missing.Count == 0, string.Join("\n", missing));
    }

    private static bool In(IReadOnlyList<(int First, int Last)> ranges, int cp) => ranges.Any(r => cp >= r.First && cp <= r.Last);

    /// <summary>The codepoints a TrueType font maps to a glyph (cmap formats 4 and 12).</summary>
    internal static HashSet<int> Cmap(byte[] d)
    {
        static int U16(byte[] b, int o) => (b[o] << 8) | b[o + 1];
        static short S16(byte[] b, int o) => (short)((b[o] << 8) | b[o + 1]);
        static long U32(byte[] b, int o) => ((long)b[o] << 24) | ((long)b[o + 1] << 16) | ((long)b[o + 2] << 8) | b[o + 3];

        int tables = U16(d, 4), cmap = -1;
        for (int i = 0; i < tables; i++)
            if (Encoding.ASCII.GetString(d, 12 + 16 * i, 4) == "cmap") cmap = (int)U32(d, 12 + 16 * i + 8);
        Assert.True(cmap > 0, "no cmap table");
        var set = new HashSet<int>();
        int subtables = U16(d, cmap + 2);
        for (int i = 0; i < subtables; i++)
        {
            int o = cmap + (int)U32(d, cmap + 4 + 8 * i + 4);
            int format = U16(d, o);
            if (format == 4)
            {
                int seg = U16(d, o + 6) / 2;
                int ends = o + 14, starts = ends + 2 * seg + 2, deltas = starts + 2 * seg, offsets = deltas + 2 * seg;
                for (int k = 0; k < seg; k++)
                {
                    int s = U16(d, starts + 2 * k), e = U16(d, ends + 2 * k), delta = S16(d, deltas + 2 * k), ro = U16(d, offsets + 2 * k);
                    for (int c = s; c <= e && c != 0xFFFF; c++)
                    {
                        int g = ro == 0 ? (c + delta) & 0xFFFF : U16(d, offsets + 2 * k + ro + 2 * (c - s));
                        if (ro != 0 && g != 0) g = (g + delta) & 0xFFFF;
                        if (g != 0) set.Add(c);
                    }
                }
            }
            else if (format == 12)
            {
                long groups = U32(d, o + 12);
                for (long k = 0; k < groups; k++)
                {
                    int p = o + 16 + (int)(12 * k);
                    for (long c = U32(d, p); c <= U32(d, p + 4); c++) set.Add((int)c);
                }
            }
        }
        return set;
    }

}
