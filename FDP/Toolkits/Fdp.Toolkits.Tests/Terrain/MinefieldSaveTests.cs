using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Core.Serialization.Migrations;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Terrain;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐ CE-4624 MS-T2 (docs/designs/minefields/DESIGN_Minefields.md KR-15, MF-14, MR-2) — the scenario's <c>Knowledge</c> section: what the sides learned of places, plus each field's
    /// struck mines and lanes. Save → text → read, the two-slice merge by the kind's policy, a malformed entry failing loudly, and a replay seek restoring the recorded records.
    /// </summary>
    public sealed class MinefieldSaveTests
    {
        private const int Blue = 1, Red = 2;
        private static readonly Guid FieldA = Guid.Parse("a7000000-0000-4000-8000-0000000000f1");
        private static readonly Guid FieldB = Guid.Parse("a7000000-0000-4000-8000-0000000000f2");
        private const string OurType = "TestSubsystem";

        private static EntityRepository World()
        {
            var w = new EntityRepository();
            w.RegisterComponent<NetworkAuthority>();
            w.RegisterComponent<MinefieldKnowledge>(); w.RegisterComponent<MinefieldStrikes>(); w.RegisterComponent<MinefieldLanes>();
            return w;
        }

        private static MinefieldKnowledge Know(Guid field, int side, bool marked, double learnedAt, params (int Col, int Row, MineCellKnowledge Level)[] cells)
        {
            var k = new MinefieldKnowledge();
            k.Header.SubjectKind = SubjectKind.Entity; k.Header.Subject = field; k.Header.ObjectIndex = -1; k.Header.SidePlus1 = (byte)(side + 1);
            k.Header.Kind = PlaceKnowledgeKind.Minefield; k.Header.Marked = (byte)(marked ? 1 : 0); k.Header.LearnedAt = (SimInstant)learnedAt; k.Header.Source = KnowledgeSource.Recon;
            foreach (var (c, r, l) in cells) k.Set(c, r, l);
            return k;
        }

        private static Entity Add<T>(EntityRepository w, in T record, bool foreignOwner = false) where T : unmanaged
        {
            var e = w.CreateEntity();
            w.AddComponent(e, record);
            if (foreignOwner) w.AddComponent(e, new NetworkAuthority(primaryOwnerId: 7, localNodeId: 1));   // another node's entity
            return e;
        }

        private static MinefieldStrikes Strikes(Guid field, ushort chunk, params (short Row, short Col, MineGone How)[] mines)
        {
            var s = new MinefieldStrikes { Field = field, Chunk = chunk };
            foreach (var (r, c, h) in mines) s.TryAdd(new StrikeItem(field, r, c, h));
            return s;
        }

        private static MinefieldLanes Lanes(Guid field, params (Vector2 A, Vector2 B, float W, byte Side)[] lanes)
        {
            var l = new MinefieldLanes { Field = field };
            foreach (var (a, b, w, s) in lanes) l.TryAdd(new LaneItem(field, a, b, w, s));
            return l;
        }

        private static JsonObject Text(JsonObject section) => new() { [PlaceKnowledgeSection.Name] = JsonNode.Parse(section.ToJsonString()) };   // through TEXT: the file's own number formatting

        /// <summary>
        /// ⭐ KR-15 — every knowledge / strikes / lanes record this host OWNS is saved (knowledge as the non-unknown cells + Marked + LearnedAt; the strikes of two chunks as ONE entry; the lanes),
        /// read back through TEXT to the same content; a replica's record is not written (each slice once); a blank record says nothing; nothing at all ⇒ no section.
        /// 🔴 Red-proof: drop the <c>OwnsRecord</c> gate ⇒ the replica's cell is written too.
        /// </summary>
        [Fact]
        public void TheOwnedRecords_SaveAndReadBack_TheSameContent_AReplicaIsNotWritten()
        {
            var w = World();
            Assert.Null(PlaceKnowledgeSection.Write(w));
            Add(w, Know(FieldA, Blue, marked: false, 12.5, (3, 4, MineCellKnowledge.KnownMined), (59, 63, MineCellKnowledge.Suspected), (0, 0, MineCellKnowledge.KnownClear)));
            Add(w, Know(FieldA, Red, marked: true, 3.0));
            Add(w, Know(FieldB, Blue, marked: false, 1.0));                                                       // blank: nothing to say
            Add(w, Know(FieldB, Red, marked: false, 9.0, (7, 7, MineCellKnowledge.KnownMined)), foreignOwner: true);   // a replica
            var many = new (short, short, MineGone)[MinefieldStrikes.Capacity];
            for (int i = 0; i < many.Length; i++) many[i] = ((short)i, (short)(i + 1), MineGone.Struck);
            Add(w, Strikes(FieldA, 0, many));
            Add(w, Strikes(FieldA, 1, (-3, 200, MineGone.Cleared), (500, 6, MineGone.Struck)));
            Add(w, Lanes(FieldA, (new Vector2(-100, 0), new Vector2(100.5f, 2), 4.5f, Blue + 1), (new Vector2(0, -50), new Vector2(0, 50), 8f, Red + 1)));

            var section = PlaceKnowledgeSection.Write(w)!;
            Assert.NotNull(section);
            var saved = PlaceKnowledgeSection.Read(Text(section));

            Assert.Equal(2, saved.Knowledge.Count);            // FieldA/Blue and FieldA/Red; the blank and the replica are absent
            var blue = saved.Knowledge.Single(k => k.SidePlus1 == Blue + 1);
            Assert.Equal(FieldA, blue.Subject);
            Assert.False(blue.Marked);
            Assert.Equal(12.5, (double)blue.LearnedAt);
            Assert.Equal(KnowledgeSource.Recon, blue.Source);
            Assert.Equal(new[] { (((byte)0, (byte)0), (byte)2), (((byte)4, (byte)3), (byte)3), (((byte)63, (byte)59), (byte)1) },
                blue.Cells.Select(kv => (kv.Key, kv.Value)).ToArray());
            Assert.True(saved.Knowledge.Single(k => k.SidePlus1 == Red + 1).Marked);
            Assert.DoesNotContain(saved.Knowledge, k => k.Subject == FieldB);

            var mines = saved.Strikes[FieldA];
            Assert.Equal(MinefieldStrikes.Capacity + 2, mines.Count);   // both chunks in one entry
            Assert.Equal(MineGone.Cleared, mines[(-3, 200)]);
            Assert.Equal(MineGone.Struck, mines[(500, 6)]);
            var lanes = saved.Lanes[FieldA];
            Assert.Equal(2, lanes.Count);
            Assert.Contains(lanes, l => l.A == new Vector2(-100, 0) && l.B == new Vector2(100.5f, 2) && l.Width == 4.5f && l.SidePlus1 == Blue + 1);
            Assert.False(PlaceKnowledgeSection.Read(new JsonObject()).Knowledge.Any());
        }

        private static JsonObject Slice(JsonObject section)
        {
            var dom = new JsonObject { ["Entities"] = new JsonObject() };
            dom[PlaceKnowledgeSection.Name] = JsonNode.Parse(section.ToJsonString());
            JsonEnvelope.Write(dom, new DocumentMeta(OurType, 2));
            return dom;
        }

        private static JsonObject SaveOf(Action<EntityRepository> fill) { var w = World(); fill(w); return PlaceKnowledgeSection.Write(w)!; }

        /// <summary>
        /// ⭐⭐ KR-15, MR-2 — two deciding nodes each saved their OWN record of one (field, side): <c>ScenarioMergeCore</c> merges the slices by the kind's policy — per cell the lattice max, Marked
        /// OR-ed, LearnedAt max; strikes and lanes the union — and NEVER throws on the duplicate key (the terrain-object arm would). Order-free.
        /// 🔴 Red-proof: drop the knowledge arm in <c>ScenarioMergeCore</c> ⇒ the section vanishes; take the first entry only ⇒ the second node's cell is lost.
        /// </summary>
        [Fact]
        public void TwoSlices_CarryingOneKey_MergeByTheKindsPolicy_AndDoNotThrow()
        {
            var simHost = SaveOf(w =>
            {
                Add(w, Know(FieldA, Blue, marked: false, 5.0, (1, 1, MineCellKnowledge.Suspected), (2, 2, MineCellKnowledge.KnownClear), (3, 3, MineCellKnowledge.KnownMined)));
                Add(w, Strikes(FieldA, 0, (1, 1, MineGone.Struck), (2, 2, MineGone.Struck)));
                Add(w, Lanes(FieldA, (new Vector2(0, 0), new Vector2(10, 0), 4.5f, Blue + 1)));
            });
            var cgf = SaveOf(w =>
            {
                Add(w, Know(FieldA, Blue, marked: true, 9.0, (1, 1, MineCellKnowledge.KnownMined), (2, 2, MineCellKnowledge.Suspected), (4, 4, MineCellKnowledge.KnownClear)));
                Add(w, Strikes(FieldA, 0, (2, 2, MineGone.Cleared), (9, 9, MineGone.Struck)));
                Add(w, Lanes(FieldA, (new Vector2(0, 0), new Vector2(10, 0), 4.5f, Blue + 1), (new Vector2(0, 5), new Vector2(10, 5), 4.5f, Blue + 1)));
                Add(w, Know(FieldB, Red, marked: false, 2.0, (0, 0, MineCellKnowledge.Suspected)));
            });

            foreach (var order in new[] { new[] { simHost, cgf }, new[] { cgf, simHost } })
            {
                var slices = order.Select((s, i) => new ScenarioSlice((NodeId)(i + 1), OurType, Slice(s))).ToArray();
                var merged = (JsonObject)ScenarioMergeCore.Merge(slices, OurType).CanonicalDom![PlaceKnowledgeSection.Name]!;
                var saved = PlaceKnowledgeSection.Read(Text(merged));

                var a = saved.Knowledge.Single(k => k.Subject == FieldA);
                Assert.True(a.Marked);                                    // OR
                Assert.Equal(9.0, (double)a.LearnedAt);                           // max
                Assert.Equal((byte)3, a.Cells[((byte)1, (byte)1)]);       // max(suspected, known-mined)
                Assert.Equal((byte)2, a.Cells[((byte)2, (byte)2)]);       // max(known-clear, suspected)
                Assert.Equal((byte)3, a.Cells[((byte)3, (byte)3)]);
                Assert.Equal((byte)2, a.Cells[((byte)4, (byte)4)]);
                Assert.Equal(4, a.Cells.Count);
                Assert.Single(saved.Knowledge, k => k.Subject == FieldB);   // a key in ONE slice passes through
                Assert.Equal(3, saved.Strikes[FieldA].Count);               // (1,1) (2,2) (9,9) — the union
                Assert.Equal(MineGone.Cleared, saved.Strikes[FieldA][(2, 2)]);
                Assert.Equal(2, saved.Lanes[FieldA].Count);                 // the shared lane once
                Assert.Equal(merged.Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal), merged.Select(kv => kv.Key));   // the save's file order
            }
            Assert.Null(ScenarioMergeCore.Merge(new[] { new ScenarioSlice((NodeId)1, OurType, Slice(new JsonObject())) }, OurType).CanonicalDom![PlaceKnowledgeSection.Name]);
        }

        /// <summary>⭐ a malformed entry fails LOUDLY and names its key (a scenario that silently loses what a side learned reloads with the field unknown to the units that paid for it).</summary>
        [Theory]
        [InlineData("a7000000-0000-4000-8000-0000000000f1|2|1|0", "{ \"cells\": [[1, 2]] }")]                    // a cell row of two
        [InlineData("a7000000-0000-4000-8000-0000000000f1|2|1|0", "{ \"cells\": [[64, 0, 1]] }")]                // row outside the grid
        [InlineData("a7000000-0000-4000-8000-0000000000f1|2|1|0", "{ \"cells\": [[0, 0, 4]] }")]                 // level 4
        [InlineData("a7000000-0000-4000-8000-0000000000f1|0|1|0", "{ \"cells\": [] }")]                          // side + 1 = 0
        [InlineData("not-a-guid|2|1|0", "{ \"cells\": [] }")]
        [InlineData("a7000000-0000-4000-8000-0000000000f1|strikes", "{ \"mines\": [[1, 2, 7]] }")]               // how 7
        [InlineData("a7000000-0000-4000-8000-0000000000f1|lanes", "{ \"lanes\": [[0, 0, 1, 1, 0, 1]] }")]        // width 0
        public void AMalformedEntry_FailsLoudly(string key, string entry)
        {
            var dom = new JsonObject { [PlaceKnowledgeSection.Name] = new JsonObject { [key] = JsonNode.Parse(entry) } };
            var ex = Assert.Throws<InvalidOperationException>(() => PlaceKnowledgeSection.Read(dom));
            Assert.Contains(PlaceKnowledgeSection.Name, ex.Message);
        }

        /// <summary>⭐ an entry of a kind this build does not know is ignored (a newer scenario on an older build), and a camelCase section name reads.</summary>
        [Fact]
        public void AFutureKind_IsIgnored_AndTheSectionNameIsCaseTolerant()
        {
            var dom = new JsonObject { ["knowledge"] = new JsonObject { [$"{FieldA:D}|2|9|0"] = JsonNode.Parse("{ \"cells\": [[0, 0, 1]] }") } };
            Assert.Empty(PlaceKnowledgeSection.Read(dom).Knowledge);
            dom = new JsonObject { ["knowledge"] = new JsonObject { [$"{FieldA:D}|2|1|0"] = JsonNode.Parse("{ \"cells\": [[0, 0, 1]] }") } };
            Assert.Single(PlaceKnowledgeSection.Read(dom).Knowledge);
        }

        /// <summary>
        /// ⭐ MF-14 (recorded) — knowledge, strikes and lanes are unmanaged recorded components: a keyframe of world B restored over a live world that holds A (what an AAR seek does:
        /// <c>repo.Clear()</c> then a raw chunk blit — the <c>BuildingBreachTests</c> / GroundDamage precedent) serves B's records, back to A serves A's, forward to B again B's.
        /// 🔴 Red-proof: give <c>MinefieldKnowledge</c> a managed field / drop its <c>NoScenario</c>-recorded layout (a <c>string</c> member) ⇒ the blit cannot carry it and the seek loses the cells.
        /// </summary>
        [Fact]
        public void ASeek_RestoresTheRecordedKnowledgeStrikesAndLanes()
        {
            static EntityRepository Reg(EntityRepository r) { r.RegisterComponent<MinefieldKnowledge>(); r.RegisterComponent<MinefieldStrikes>(); r.RegisterComponent<MinefieldLanes>(); return r; }
            byte[] Keyframe(bool second, long tick)
            {
                using var src = Reg(new EntityRepository());
                var k = src.CreateEntity(); src.AddComponent(k, second
                    ? Know(FieldA, Blue, marked: true, 8.0, (9, 9, MineCellKnowledge.KnownMined))
                    : Know(FieldA, Blue, marked: false, 2.0, (1, 1, MineCellKnowledge.Suspected)));
                var s = src.CreateEntity(); src.AddComponent(s, second ? Strikes(FieldA, 0, (1, 1, MineGone.Struck), (4, 4, MineGone.Cleared)) : Strikes(FieldA, 0, (1, 1, MineGone.Struck)));
                if (second) { var l = src.CreateEntity(); src.AddComponent(l, Lanes(FieldA, (new Vector2(0, 0), new Vector2(40, 0), 4.5f, Blue + 1))); }
                using var ms = new MemoryStream();
                using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true)) new Fdp.Core.FlightRecorder.RecorderSystem().RecordKeyframe(src, bw, tick);
                return ms.ToArray();
            }
            byte[] a = Keyframe(false, 10), b = Keyframe(true, 20);

            using var live = Reg(new EntityRepository());
            var playback = new Fdp.Core.FlightRecorder.PlaybackSystem();
            void Seek(byte[] keyframe) { using var br = new BinaryReader(new MemoryStream(keyframe)); playback.ApplyFrame(live, br, processEvents: false); }
            void Expect(bool second, string at)
            {
                var saved = PlaceKnowledgeSection.Read(Text(PlaceKnowledgeSection.Write(live)!));
                var k = Assert.Single(saved.Knowledge);
                Assert.Equal(second, k.Marked);
                Assert.Equal(second ? (byte)3 : (byte)1, Assert.Single(k.Cells).Value);
                Assert.Equal(second ? 2 : 1, saved.Strikes[FieldA].Count);
                Assert.Equal(second, saved.Lanes.ContainsKey(FieldA));
                Assert.Equal(second, MinefieldStrikesSystem.IsGone(live, FieldA, 4, 4));
                int lanes = 0; foreach (var _ in live.Query().With<MinefieldLanes>().Build()) lanes++;
                Assert.Equal(second ? 1 : 0, lanes);
            }
            Seek(b); Expect(true, "B");
            Seek(a); Expect(false, "back to A");
            Seek(b); Expect(true, "forward to B again");
        }
    }
}
