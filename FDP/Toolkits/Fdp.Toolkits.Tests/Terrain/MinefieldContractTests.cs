using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Toolkit.Terrain;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐ CE-4624 (docs/designs/minefields/DESIGN_Minefields.md MF-2, KR-2, §6.1; NOTE_Terrain_To_All_Minefield_Contract.md) — the contract every lane binds to:
    /// the derived pattern and the record sizes.
    /// </summary>
    public sealed class MinefieldContractTests
    {
        private static readonly Vector2[] Square = { new(-50, -50), new(50, -50), new(50, 50), new(-50, 50) };

        private static Minefield Field(float spacing = 5f, float orientation = 0f, MineType type = MineType.AntiPersonnel, uint seed = 0)
            => new() { Type = type, SpacingM = spacing, OrientationDeg = orientation, Seed = seed };

        /// <summary>⭐ KR-2 — the knowledge record fits the 1024 B component cap (header + 60 × 64 two-bit cells). 🔴 Red-proof: a 64 × 64 grid ⇒ over the cap.</summary>
        [Fact]
        public void TheKnowledgeRecord_FitsTheComponentCap_AndCellsRoundTrip()
        {
            Assert.True(Unsafe.SizeOf<MinefieldKnowledge>() <= 1024, $"MinefieldKnowledge is {Unsafe.SizeOf<MinefieldKnowledge>()} B");
            Assert.True(Unsafe.SizeOf<MinefieldStrikes>() <= 1024, $"MinefieldStrikes is {Unsafe.SizeOf<MinefieldStrikes>()} B");
            Assert.True(Unsafe.SizeOf<Minefield>() <= 1024);
            var k = new MinefieldKnowledge();
            k.Set(59, 63, MineCellKnowledge.KnownMined);
            k.Set(0, 0, MineCellKnowledge.Suspected);
            Assert.Equal(MineCellKnowledge.KnownMined, k.Get(59, 63));
            Assert.Equal(MineCellKnowledge.Suspected, k.Get(0, 0));
            Assert.Equal(MineCellKnowledge.Unknown, k.Get(1, 0));
            Assert.False(k.Raise(59, 63, MineCellKnowledge.Suspected));          // the lattice never lowers (MR-2)
            Assert.True(k.Raise(0, 0, MineCellKnowledge.KnownClear));
        }

        /// <summary>
        /// ⭐ MF-2 — staggered rows, equidistant, the same on every call, inside the outline: in a 100 m square at 5 m every mine has a neighbour exactly 5 m away in its
        /// row, odd rows sit half a spacing over, and no two mines are closer than the spacing × 0.86 (the staggered lattice's least distance). 🔴 Red-proof: drop the
        /// half-spacing offset ⇒ the odd rows line up with the even ones.
        /// </summary>
        [Fact]
        public void TheMines_AreStaggeredEquidistantDeterministic_AndInside()
        {
            var f = Field();
            var a = new List<Mine>(); var b = new List<Mine>();
            MinePattern.MinesIn(f, Vector2.Zero, Square, a);
            MinePattern.MinesIn(f, Vector2.Zero, Square, b);
            Assert.Equal(a, b);
            Assert.InRange(a.Count, 360, 440);                                     // ≈ (100 / 5)²
            Assert.All(a, m => Assert.True(MinePattern.Inside(Square, m.At)));
            var even = a.Where(m => m.Row == 0).OrderBy(m => m.Col).ToList();
            var odd = a.Where(m => m.Row == 1).OrderBy(m => m.Col).ToList();
            Assert.Equal(5f, even[1].At.X - even[0].At.X, 3);
            Assert.Equal(2.5f, ((odd[0].At.X - even[0].At.X) % 5f + 5f) % 5f, 3);   // staggered
            Assert.Equal(5f, odd[0].At.Y - even[0].At.Y, 3);
            float least = float.MaxValue;
            for (int i = 0; i < 60; i++) for (int j = i + 1; j < a.Count; j++) least = MathF.Min(least, Vector2.Distance(a[i].At, a[j].At));
            Assert.True(least >= 5f * 0.86f, $"least {least}");
        }

        /// <summary>⭐ MR-1 / R-5042 — the lattice is anchored at the FIELD's position: an outline edit keeps every remaining mine's (row, col) and place; rotating the rows turns them;
        /// a mixed field alternates AP / AT rows; a LANE is cut out. 🔴 Red-proof: anchor the lattice at the outline's bounds ⇒ the indices shift.</summary>
        [Fact]
        public void TheLattice_SurvivesAnOutlineEdit_TurnsWithTheRows_MixesByRow_AndALaneIsCutOut()
        {
            var f = Field();
            var whole = new List<Mine>(); MinePattern.MinesIn(f, Vector2.Zero, Square, whole);
            var trimmed = new List<Mine>(); MinePattern.MinesIn(f, Vector2.Zero, new[] { new Vector2(-50, -50), new Vector2(20, -50), new Vector2(20, 50), new Vector2(-50, 50) }, trimmed);
            var byIndex = whole.ToDictionary(m => (m.Row, m.Col));
            Assert.All(trimmed, m => Assert.Equal(byIndex[(m.Row, m.Col)].At, m.At));

            var turned = new List<Mine>(); MinePattern.MinesIn(Field(orientation: 30f), Vector2.Zero, Square, turned);
            var r0 = turned.Where(m => m.Row == 0).OrderBy(m => m.Col).Take(2).ToArray();
            Assert.Equal(30f, MathF.Atan2(r0[1].At.Y - r0[0].At.Y, r0[1].At.X - r0[0].At.X) * 180f / MathF.PI, 2);

            var mixed = new List<Mine>(); MinePattern.MinesIn(Field(type: MineType.Mixed), Vector2.Zero, Square, mixed);
            Assert.All(mixed, m => Assert.Equal((m.Row & 1) == 0 ? MineType.AntiPersonnel : MineType.AntiTank, m.Type));

            var laned = Field();
            laned.LaneCount = 1; laned.SetLane(0, new MinefieldLane(new Vector2(0, -60), new Vector2(0, 60), 6f, 1, 1));
            var cut = new List<Mine>(); MinePattern.MinesIn(laned, Vector2.Zero, Square, cut);
            Assert.True(cut.Count < whole.Count);
            Assert.DoesNotContain(cut, m => MathF.Abs(m.At.X) < 3f);
        }

        /// <summary>⭐ MF-14 — the knowledge grid covers the field in cells ≥ 10 m, coarsened to fit 60 × 64; a point maps to its cell and back.</summary>
        [Fact]
        public void TheKnowledgeGrid_CoversTheField_AndCoarsensALargeOne()
        {
            var g = MinefieldKnowledgeOps.Grid(Field(), Vector2.Zero, Square);
            Assert.Equal(10f, g.Cell);
            Assert.Equal(10, g.Cols); Assert.Equal(10, g.Rows);
            Assert.True(MinefieldKnowledgeOps.CellOf(g, new Vector2(-45, -45), out int c, out int r));
            Assert.Equal((0, 0), (c, r));
            Assert.True(Vector2.Distance(new Vector2(-45, -45), MinefieldKnowledgeOps.CentreOf(g, 0, 0)) < 0.01f);
            Assert.False(MinefieldKnowledgeOps.CellOf(g, new Vector2(60, 0), out _, out _));
            var big = MinefieldKnowledgeOps.Grid(Field(), Vector2.Zero, new[] { new Vector2(0, 0), new Vector2(2000, 0), new Vector2(2000, 300), new Vector2(0, 300) });
            Assert.True(big.Cols <= MinefieldKnowledge.MaxCols && big.Rows <= MinefieldKnowledge.MaxRows);
            Assert.True(big.Cell >= 2000f / MinefieldKnowledge.MaxCols);
        }
    }
}
