using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GizmoMap.Contracts.Symbology;
using Xunit;

namespace GizmoMap.Contracts.Tests
{
    /// <summary>
    /// ⭐ CE-8608 (docs/designs/aggregate-units/DESIGN_Stochastic_Unit_UI.md §6; docs/UX/DESIGN_Unit_Symbols.md) — the land-unit symbol as data and vectors: the wire
    /// form is a 2525C SIDC (+ HROT's mobility and secondary mark) that round-trips and fits the 31-character primitive text; a plain SIDC parses; the geometry
    /// draws the frame of each affiliation, the role's icon, the echelon marks (dots / bars / crosses as SHAPES), the mobility under the frame and one secondary
    /// mark box; 0 B. 🔴 Red-proof (compiles): the friend frame emitted as the hostile diamond ⇒ the friend polygon's corners are wrong ⇒ red.
    /// </summary>
    public class SymbologyTests
    {
        private sealed class Rec : ISymbolSink
        {
            public readonly List<Vector2[]> Polygons = new(); public readonly List<(Vector2 A, Vector2 B)> Lines = new();
            public readonly List<(Vector2 C, float Rx, uint Fill)> Ellipses = new(); public readonly List<string> Texts = new();
            public bool Record = true;
            public void Polygon(ReadOnlySpan<Vector2> points, uint fill, uint outline, float outlinePx) { if (Record) Polygons.Add(points.ToArray()); }
            public void Line(Vector2 a, Vector2 b, uint colour, float px) { if (Record) Lines.Add((a, b)); }
            public void Ellipse(Vector2 centre, float rx, float ry, uint fill, uint outline, float outlinePx) { if (Record) Ellipses.Add((centre, rx, fill)); }
            public void Text(Vector2 centre, string text, float sizePx, uint colour) { if (Record) Texts.Add(text); }
        }

        [Fact]
        public void CE8608_TheCode_RoundTrips_FitsThePrimitive_AndAPlainSidcParses()
        {
            var s = new SymbolCode(SymbolAffiliation.Friend, SymbolRole.Mechanized, SymbolEchelon.Platoon, SymbolMobility.Tracked, SymbolRole.AntiArmour);
            Span<char> buf = stackalloc char[SymbolCode.MaxLength];
            int n = s.Format(buf);
            Assert.Equal("SFGPUCIZ---D---|T|AT", buf.Slice(0, n).ToString());
            Assert.True(n <= 31);
            Assert.True(SymbolCode.TryParse(buf.Slice(0, n), out var back));
            Assert.Equal(s, back);
            Assert.True(SymbolCode.TryParse("SHGPUCA----E---".AsSpan(), out var tk));   // a plain 2525C hostile armour company
            Assert.Equal(new SymbolCode(SymbolAffiliation.Hostile, SymbolRole.Armour, SymbolEchelon.Company), tk);
            Assert.False(SymbolCode.TryParse("GFGPGLB--------".AsSpan(), out _));        // a control measure: not a land unit
        }

        [Fact]
        public void CE8608_TheGeometry_FrameIconEchelonMobilitySecondary_AllAsShapes()
        {
            var r = new Rec();
            SymbolGeometry.Build(new SymbolCode(SymbolAffiliation.Friend, SymbolRole.Mechanized, SymbolEchelon.Platoon, SymbolMobility.Tracked, SymbolRole.AntiArmour), new Vector2(100, 100), 20f, r);
            var frame = r.Polygons[0];
            Assert.Equal(new[] { new Vector2(85, 90), new Vector2(115, 90), new Vector2(115, 110), new Vector2(85, 110) }, frame);   // friend: 1.5 h × h rectangle
            Assert.Equal(2, r.Lines.Count(l => (l.A - l.B).Length() > 30f));                      // the infantry cross, corner to corner
            Assert.Contains(r.Ellipses, e => e.Fill == 0 && e.Rx > 5f);                             // + the armour oval = mechanised
            Assert.Equal(3, r.Ellipses.Count(e => e.Fill == SymbolGeometry.Ink && e.C.Y < 90f));    // platoon: three dots above the frame
            Assert.Contains(r.Polygons, p => p.All(v => v.Y > 110f));                               // the track band under the frame
            Assert.Contains("AT", r.Texts);                                                         // the secondary mark

            r = new Rec();
            SymbolGeometry.Build(new SymbolCode(SymbolAffiliation.Hostile, SymbolRole.Infantry, SymbolEchelon.Company), Vector2.Zero, 20f, r);
            Assert.Equal(new[] { new Vector2(0, -16), new Vector2(16, 0), new Vector2(0, 16), new Vector2(-16, 0) }, r.Polygons[0]);   // the hostile diamond
            Assert.Contains(r.Lines, l => l.A.X == l.B.X && l.A.Y < -16f);                          // company: one bar above

            r = new Rec();
            SymbolGeometry.Build(new SymbolCode(SymbolAffiliation.Unknown, SymbolRole.Infantry, SymbolEchelon.Squad), Vector2.Zero, 12f, r);
            Assert.True(r.Ellipses.Count >= 5);                                                     // the quatrefoil
            Assert.DoesNotContain(r.Ellipses, e => e.Fill == SymbolGeometry.Ink);                    // a row-sized symbol (< 15 px): no echelon marks
        }

        [Fact]
        public void CE8608_ParsingAndDrawing_AllocateNothing()
        {
            var r = new Rec { Record = false };
            var code = "SFGPUCIZ---D---|T|AT";
            SymbolCode.TryParse(code.AsSpan(), out var s);
            SymbolGeometry.Build(s, Vector2.Zero, 24f, r);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) { SymbolCode.TryParse(code.AsSpan(), out s); SymbolGeometry.Build(s, new Vector2(i, i), 24f, r); }
            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }
}
