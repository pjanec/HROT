using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Terrain;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐ CE-4624 MS-T2 (docs/designs/minefields/DESIGN_Minefields.md MF-13…MF-17, MR-2, MR-13, KR-5, KR-6, KR-8) — the place-knowledge core: the keyed writer, the per-side
    /// union table on <c>DoorStates</c>, the sharing rules.
    /// </summary>
    public sealed class PlaceKnowledgeTests
    {
        private const int Blue = 1, Red = 2;
        private static readonly Guid FieldGuid = Guid.Parse("a7000000-0000-4000-8000-0000000000f1");

        private sealed class Rig
        {
            public readonly EntityRepository W = new();
            public readonly PlaceKnowledgeSystem Knowledge = new();
            public readonly MinefieldStrikesSystem Strikes = new();
            public Entity Field;

            public Rig(ushort knownAtStart = 0)
            {
                W.RegisterComponent<SimTransform>(); W.RegisterComponent<Health>(); W.RegisterComponent<EntityInfo>(); W.RegisterComponent<EntityGuid>();
                W.RegisterComponent<Minefield>(); W.RegisterComponent<MinefieldKnowledge>(); W.RegisterComponent<MinefieldStrikes>(); W.RegisterComponent<NetworkAuthority>();
                W.RegisterManagedComponent<TestAreaOutline>();
                W.RegisterEvent<MineStruck>(); W.RegisterEvent<MinefieldCellsSwept>(); W.RegisterEvent<MinefieldMarked>(); W.RegisterEvent<LaneCleared>();
                Field = W.CreateEntity();
                W.AddComponent(Field, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
                W.AddComponent(Field, new Minefield { Type = MineType.AntiPersonnel, SpacingM = 5f, KnownAtStartSides = knownAtStart });
                W.AddComponent(Field, new EntityGuid(FieldGuid));
                W.SetManagedComponent(Field, new TestAreaOutline { Points = new[] { new Vector2(-100, -100), new Vector2(100, -100), new Vector2(100, 100), new Vector2(-100, 100) } });
            }

            public Entity Man(int side, Vector2 at, float health = 100f)
            {
                var e = W.CreateEntity();
                W.AddComponent(e, new SimTransform { Position = new Vector3(at, 0f), Rotation = Quaternion.Identity });
                W.AddComponent(e, new EntityInfo { ForceId = (ForceId)side });
                W.AddComponent(e, new Health { Current = health, Max = 100f });
                return e;
            }

            /// <summary>One frame of the writers (two passes: the second lands items that waited on a creation); each pass's creation requests are materialised as this node's entities.</summary>
            public void Frame(PlaceKnowledgeSystem? writer = null)
            {
                W.Bus.SwapBuffers();
                for (int pass = 0; pass < 2; pass++)
                {
                    (writer ?? Knowledge).Execute(W, 0.016f);
                    Strikes.Execute(W, 0.016f);
                    W.Bus.SwapBuffers();
                    foreach (var r in W.Bus.ReadManaged<EntityCreationRequest>().ToList())
                    {
                        var e = W.CreateEntity();
                        foreach (var c in r.InitialComponents!)
                            switch (c)
                            {
                                case SimTransform t: W.AddComponent(e, t); break;
                                case MinefieldKnowledge k: W.AddComponent(e, k); break;
                                case MinefieldStrikes st: W.AddComponent(e, st); break;
                            }
                    }
                }
                W.Tick();   // the frame ends (a view table's stamp holds across frames)
            }

            public MinefieldKnowledgeOps.KnowledgeGrid Grid()
            {
                var o = new List<Vector2>(); MinefieldKnowledgeOps.TryOutline(W, Field, o, out var a);
                return MinefieldKnowledgeOps.Grid(W.GetComponentRO<Minefield>(Field), a, o);
            }

            public MineCellKnowledge At(int side, Vector2 p)
            {
                var g = Grid(); MinefieldKnowledgeOps.CellOf(g, p, out int c, out int r);
                return PlaceKnowledgeTable.Of(W).Knows(side, Field, c, r);
            }
        }

        /// <summary>
        /// ⭐ MF-13 ①, MR-13 — a strike teaches the VICTIM's side and every side with a LIVING member within 300 m: the cells within 50 m become SUSPECTED; a side whose only member
        /// there is dead, or far, learns nothing; the struck mine is in the field's strikes record. 🔴 Red-proof: drop the witness test ⇒ Red (far) learns too.
        /// </summary>
        [Fact]
        public void AStrike_TeachesTheVictimAndLivingWitnesses_SuspectedWithin50m_AndIsRecordedAsGone()
        {
            var rig = new Rig();
            var victim = rig.Man(Blue, new Vector2(0, 0));
            rig.Man(Red, new Vector2(900, 0));             // far: no witness
            rig.Man(3, new Vector2(150, 0), health: 0f);   // dead: no witness
            rig.Man(4, new Vector2(250, 0));               // living, within 300 m
            rig.W.Bus.Publish(new MineStruck { Field = rig.Field, Row = 0, Col = 0, By = victim, X = 0, Y = 0, At = (SimInstant)5.0 });
            rig.Frame();
            Assert.Equal(MineCellKnowledge.Suspected, rig.At(Blue, new Vector2(0, 0)));
            Assert.Equal(MineCellKnowledge.Suspected, rig.At(Blue, new Vector2(40, 0)));
            Assert.Equal(MineCellKnowledge.Unknown, rig.At(Blue, new Vector2(90, 0)));     // beyond 50 m
            Assert.Equal(MineCellKnowledge.Suspected, rig.At(4, new Vector2(0, 0)));
            Assert.Equal(MineCellKnowledge.Unknown, rig.At(Red, new Vector2(0, 0)));
            Assert.Equal(MineCellKnowledge.Unknown, rig.At(3, new Vector2(0, 0)));
            Assert.True(MinefieldStrikesSystem.IsGone(rig.W, FieldGuid, 0, 0));
            Assert.False(MinefieldStrikesSystem.IsGone(rig.W, FieldGuid, 0, 1));
        }

        /// <summary>⭐ MF-13 ②, MR-2 — a recon sweep makes cells known-mined / known-clear for its side, and the lattice never LOWERS a cell (a later "suspected" leaves "known-mined").</summary>
        [Fact]
        public unsafe void ASweep_MakesCellsKnown_AndTheLatticeNeverLowers()
        {
            var rig = new Rig();
            var s = new MinefieldCellsSwept { Field = rig.Field, SidePlus1 = Blue + 1, Count = 2 };
            s.Col[0] = 3; s.Row[0] = 3; s.Found[0] = (byte)MineCellKnowledge.KnownMined;
            s.Col[1] = 4; s.Row[1] = 3; s.Found[1] = (byte)MineCellKnowledge.KnownClear;
            rig.W.Bus.Publish(s);
            rig.Frame();
            var t = PlaceKnowledgeTable.Of(rig.W);
            Assert.Equal(MineCellKnowledge.KnownMined, t.Knows(Blue, rig.Field, 3, 3));
            Assert.Equal(MineCellKnowledge.KnownClear, t.Knows(Blue, rig.Field, 4, 3));
            var victim = rig.Man(Blue, MinefieldKnowledgeOps.CentreOf(rig.Grid(), 3, 3));
            var p = MinefieldKnowledgeOps.CentreOf(rig.Grid(), 3, 3);
            rig.W.Bus.Publish(new MineStruck { Field = rig.Field, By = victim, X = p.X, Y = p.Y });
            rig.Frame();
            Assert.Equal(MineCellKnowledge.KnownMined, PlaceKnowledgeTable.Of(rig.W).Knows(Blue, rig.Field, 3, 3));   // not lowered to suspected
        }

        /// <summary>
        /// ⭐ KR-5 — two deciding nodes each write their OWN record of one (field, side): every reader sees the UNION (per cell the max, Marked OR-ed), whatever the order.
        /// 🔴 Red-proof: take the first record only ⇒ the second node's cell is lost.
        /// </summary>
        [Fact]
        public void TwoNodesRecords_OfOneKey_AreReadAsTheirUnion()
        {
            var rig = new Rig();
            MinefieldKnowledge K(byte col, MineCellKnowledge v, bool marked)
            {
                var k = new MinefieldKnowledge();
                k.Header.Subject = FieldGuid; k.Header.SidePlus1 = Blue + 1; k.Header.Kind = PlaceKnowledgeKind.Minefield; k.Header.Marked = (byte)(marked ? 1 : 0);
                k.Set(col, 0, v);
                return k;
            }
            var a = rig.W.CreateEntity(); rig.W.AddComponent(a, K(1, MineCellKnowledge.KnownClear, false));
            var b = rig.W.CreateEntity(); rig.W.AddComponent(b, K(2, MineCellKnowledge.KnownMined, false));
            var c = rig.W.CreateEntity(); rig.W.AddComponent(c, K(1, MineCellKnowledge.Suspected, false));
            var t = PlaceKnowledgeTable.Of(rig.W);
            Assert.Equal(MineCellKnowledge.KnownClear, t.Knows(Blue, rig.Field, 1, 0));   // max(known-clear, suspected)
            Assert.Equal(MineCellKnowledge.KnownMined, t.Knows(Blue, rig.Field, 2, 0));
            Assert.False(t.Marked(Blue, rig.Field));
            var d = rig.W.CreateEntity(); rig.W.AddComponent(d, K(5, MineCellKnowledge.Unknown, true));
            var t2 = PlaceKnowledgeTable.Of(rig.W);
            Assert.True(t2.Marked(Blue, rig.Field));
            Assert.Equal(MineCellKnowledge.KnownMined, t2.Knows(Blue, rig.Field, 30, 30));   // marked: known in full
            Assert.Equal(1, t2.RecordCount);
        }

        /// <summary>⭐ MF-15, R-5046 — an own field is known in full to the sides that know it at start, with NO record; another side knows nothing until it learns.</summary>
        [Fact]
        public void KnownAtStart_IsKnownInFull_WithNoRecord()
        {
            var rig = new Rig(knownAtStart: 1 << Blue);
            var t = PlaceKnowledgeTable.Of(rig.W);
            Assert.True(t.Marked(Blue, rig.Field));
            Assert.Equal(MineCellKnowledge.KnownMined, t.Knows(Blue, rig.Field, 0, 0));
            Assert.Equal(MineCellKnowledge.Unknown, t.Knows(Red, rig.Field, 0, 0));
            Assert.Equal(0, t.RecordCount);
        }

        /// <summary>⭐ MF-13 ③, MR-13 — a side MARKS a field; a living unit of ANOTHER side within 30 m of its edge learns it marked (marking is physical); one further off does not.
        /// 🔴 Red-proof: skip the edge check ⇒ Red never learns.</summary>
        [Fact]
        public void AMarkedField_IsLearnedByAnotherSide_SeeingItsEdge()
        {
            var rig = new Rig();
            rig.W.Bus.Publish(new MinefieldMarked { Field = rig.Field, SidePlus1 = Blue + 1 });
            rig.Frame();
            Assert.True(PlaceKnowledgeTable.Of(rig.W).Marked(Blue, rig.Field));
            rig.Man(Red, new Vector2(120, 0));     // 20 m from the east edge
            rig.Man(5, new Vector2(200, 0));       // 100 m off
            for (int i = 0; i < PlaceKnowledgeSystem.EdgeCheckTicks; i++) rig.Frame();
            var t = PlaceKnowledgeTable.Of(rig.W);
            Assert.True(t.Marked(Red, rig.Field));
            Assert.False(t.Marked(5, rig.Field));
        }

        /// <summary>⭐ KR-8 / R-220 — the table rides on <c>DoorStates</c>; a steady frame hands back the SAME instance and allocates nothing; a new record rebuilds it.</summary>
        [Fact]
        public void TheTable_RidesOnDoorStates_ASteadyFrameIsTheSameInstanceAndAllocatesNothing()
        {
            var rig = new Rig(knownAtStart: 1 << Blue);
            var terrain = new TerrainWorld { Name = "t", BoundsMin = new Vector2(-200), BoundsMax = new Vector2(200) };
            rig.W.Tick();   // a frame boundary: what was written while building the rig is behind the stamp
            var first = DoorStates.Of(rig.W, terrain);
            Assert.Same(PlaceKnowledgeTable.Of(rig.W), first.Knowledge);
            Assert.True(first.Knowledge.Marked(Blue, rig.Field));
            for (int i = 0; i < 3; i++) PlaceKnowledgeTable.Of(rig.W);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++) PlaceKnowledgeTable.Of(rig.W);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            var t0 = PlaceKnowledgeTable.Of(rig.W);
            rig.W.Bus.Publish(new MinefieldMarked { Field = rig.Field, SidePlus1 = Red + 1 });
            rig.Frame();
            Assert.NotSame(t0, PlaceKnowledgeTable.Of(rig.W));
            Assert.True(PlaceKnowledgeTable.Of(rig.W).Marked(Red, rig.Field));
        }

        /// <summary>⭐ a host that composes both packs registers the writer twice in one world: the first instance claims it, the second writes nothing (no second record).</summary>
        [Fact]
        public void TwoWritersInOneWorld_OnlyTheFirstWrites()
        {
            var rig = new Rig();
            var second = new PlaceKnowledgeSystem();
            rig.W.Bus.Publish(new MinefieldMarked { Field = rig.Field, SidePlus1 = Blue + 1 });
            rig.Frame();                     // the rig's writer claims the world
            rig.W.Bus.Publish(new MinefieldMarked { Field = rig.Field, SidePlus1 = Red + 1 });
            rig.Frame(second);               // the second instance: idle
            Assert.False(PlaceKnowledgeTable.Of(rig.W).Marked(Red, rig.Field));
            Assert.Equal(0, second.Requested);
        }
    
        /// <summary>⭐ MS-T4 (MR-11, MR-18, R-5048) — a cleared lane is a RECORD of the deciding node (never a write to the authored field): a <see cref="LaneCleared"/> with its segment
        /// becomes one lane of the field's <see cref="MinefieldLanes"/>; the same lane twice is one. 🔴 Red-proof: ignore the width test ⇒ an authored-lane event (width 0) writes too.</summary>
        [Fact]
        public void ALaneCleared_IsARecordOfTheDecidingNode_OnceEach()
        {
            var rig = new Rig();
            rig.W.RegisterComponent<MinefieldLanes>();
            var lanes = new MinefieldLanesSystem();
            void Frame()
            {
                rig.W.Bus.SwapBuffers();
                for (int pass = 0; pass < 2; pass++)
                {
                    lanes.Execute(rig.W, 0.016f);
                    rig.W.Bus.SwapBuffers();
                    foreach (var r in rig.W.Bus.ReadManaged<EntityCreationRequest>().ToList())
                    {
                        var e = rig.W.CreateEntity();
                        foreach (var c in r.InitialComponents!) if (c is MinefieldLanes l) rig.W.AddComponent(e, l); else if (c is SimTransform t) rig.W.AddComponent(e, t);
                    }
                }
                rig.W.Tick();
            }
            var lane = new LaneCleared { Field = rig.Field, Lane = 255, SidePlus1 = Blue + 1, Ax = -100, Ay = 0, Bx = 100, By = 0, Width = 4.5f };
            rig.W.Bus.Publish(lane); Frame();
            rig.W.Bus.Publish(lane); Frame();
            rig.W.Bus.Publish(new LaneCleared { Field = rig.Field, Lane = 0, SidePlus1 = Blue + 1 }); Frame();   // an authored lane: no geometry, no record
            var got = new List<LaneItem>();
            MinefieldLanesSystem.LanesOf(rig.W, FieldGuid, got);
            Assert.Single(got);
            Assert.Equal(new Vector2(-100, 0), got[0].A);
            Assert.Equal(4.5f, got[0].Width);
            Assert.Equal(Blue + 1, got[0].SidePlus1);
        }
}
}
