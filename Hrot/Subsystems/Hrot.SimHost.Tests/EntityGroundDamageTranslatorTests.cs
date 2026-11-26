using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.Replication.Egress;
using Hrot.Map.Common.Replication.Ingress;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4591</c> (P-4; docs/DESIGN_Ground_Patches_And_Road_Blockage.md G-7, D-1) — a ground cell's craters cross the wire: the deciding node's
    /// <see cref="EntityGroundDamageEgressTranslator"/> form, a receiver's <see cref="EntityGroundDamageIngressTranslator"/> builds a ghost carrying the craters, and
    /// that node's own view answers the cratered ground (<see cref="DoorStates.Craters"/>) — a standalone brain sees the Muscle's crater. The breach pair's shape.
    /// </summary>
    public sealed class EntityGroundDamageTranslatorTests
    {
        private static EntityRepository World(out TerrainWorld terrain)
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<NetworkAuthority>();
            repo.RegisterComponent<GhostStateTracker>();
            repo.RegisterComponent<GroundDamage>();
            int n = 101;
            terrain = new TerrainWorld { Name = "range", Height = new TerrainHeightGrid(Vector2.Zero, 1f, n, n, new float[n * n]) };
            repo.SetSingletonManaged(terrain);
            return repo;
        }

        private static GroundDamage Two()
        {
            var d = new GroundDamage { CellX = 0, CellY = 0 };
            GroundDamages.Add(ref d, new Crater(new Vector2(30f, 30f), 3.6f, 1.6f, 0.3f, (SimInstant)12.5, CraterKind.Large), out _);
            GroundDamages.Add(ref d, new Crater(new Vector2(50f, 20f), 1f, 0.4f, 0.1f, (SimInstant)13.0, CraterKind.Small), out _);
            return d;
        }

        /// <summary>The toolkit states the wire ordinal (it cannot reference the NED assembly); this pins the two equal.</summary>
        [Fact]
        public void TheToolkitsOrdinal_IsTheDescriptorsOrdinal()
            => Assert.Equal((long)Hrot.NED.Descriptors.EDescriptorType.dtGroundDamage, GroundDamageSystem.DescriptorOrdinal);

        /// <summary>⭐ CE-4624 — the minefield's three ordinals (the toolkit cannot reference NED) equal the wire's (NOTE_Terrain_To_All_Minefield_Contract.md).</summary>
        [Fact]
        public void CE4624_TheMinefieldOrdinals_MatchTheWire()
        {
            Assert.Equal((long)Hrot.NED.Descriptors.EDescriptorType.dtMinefield, Fdp.Toolkit.Terrain.Minefields.MinefieldDescriptorOrdinal);
            Assert.Equal((long)Hrot.NED.Descriptors.EDescriptorType.dtMinefieldStrikes, Fdp.Toolkit.Terrain.Minefields.StrikesDescriptorOrdinal);
            Assert.Equal((long)Hrot.NED.Descriptors.EDescriptorType.dtMinefieldKnowledge, Fdp.Toolkit.Terrain.Minefields.KnowledgeDescriptorOrdinal);
            Assert.Equal((long)Hrot.NED.Descriptors.EDescriptorType.dtMinefieldLanes, Fdp.Toolkit.Terrain.Minefields.LanesDescriptorOrdinal);
            Assert.Equal((long)Hrot.NED.Descriptors.EDescriptorType.dtMineClearing, Fdp.Toolkit.Terrain.Minefields.ClearingDescriptorOrdinal);
        }

        /// <summary>
        /// The wire form round-trips BYTE FOR BYTE (the same craters, the same birth — V-10: no node recomputes a profile from the charge) and reaches a ghost whose
        /// view reads the crater at once; the recorded owner never takes its own sample back; an unchanged component matches its last sample (no resend).
        /// 🔴 Red-proof: drop the <c>BornAt</c> copy in <c>FromSample</c> ⇒ the bytes differ.
        /// </summary>
        [Fact]
        public void TheSample_RoundTripsEveryCrater_TheReplicasViewIsCratered_AndTheOwnerNeverTakesItsOwnBack()
        {
            using var replica = World(out var terrain);
            var original = Two();
            var sample = EntityGroundDamageEgressTranslator.ToSample(6100, in original);
            Assert.True(EntityGroundDamageEgressTranslator.Matches(in sample, in original));
            var map = new NetworkEntityMap();
            var ingress = new EntityGroundDamageIngressTranslator(participant: null, map, new GhostCreationSystem(map), localNodeId: 2);

            using (var cmd = new EntityCommandBuffer()) { ingress.Receive(in sample, cmd, replica); cmd.Playback(replica); }
            Assert.True(map.TryGetEntity(6100, out var ghost));
            Assert.True(System.Runtime.InteropServices.MemoryMarshal.AsBytes(new System.ReadOnlySpan<GroundDamage>(in original))
                .SequenceEqual(System.Runtime.InteropServices.MemoryMarshal.AsBytes(new System.ReadOnlySpan<GroundDamage>(replica.GetComponentRO<GroundDamage>(ghost)))));
            Assert.Equal(-1.6f, TerrainWorldQuery.ForView(replica, terrain).GroundHeightAt(30f, 30f), 4);

            replica.AddComponent(ghost, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 2));
            var newer = original;
            GroundDamages.Add(ref newer, new Crater(new Vector2(10f, 10f), 1f, 0.4f, 0.1f, (SimInstant)14.0, CraterKind.Small), out _);
            replica.SetComponent(ghost, newer);
            using (var cmd = new EntityCommandBuffer()) { ingress.Receive(in sample, cmd, replica); cmd.Playback(replica); }
            Assert.Equal(3, replica.GetComponentRO<GroundDamage>(ghost).Count);
            Assert.False(EntityGroundDamageEgressTranslator.Matches(in sample, in newer));
        }
        /// <summary>⭐ <c>CE-4664</c> — the heat marks' ordinal (the toolkit cannot reference NED) equals the wire's: 242, fixed at dispatch.</summary>
        [Fact]
        public void CE4664_TheHeatMarksOrdinal_IsTheDescriptorsOrdinal()
            => Assert.Equal((long)Hrot.NED.Descriptors.EDescriptorType.dtGroundHeatMarks, GroundHeatMarkSystem.DescriptorOrdinal);

        /// <summary>
        /// ⭐⭐ <c>CE-4664</c> (thermal drivers TD-14) — a mark written on one node is on EVERY node, a late joiner included: the cell's record crosses the wire BYTE FOR BYTE
        /// (the footprint, the hot end, the stand's times, the engine time, the power class) and reaches a GHOST — a node that never saw the entity made, as a late joiner reads
        /// the TransientLocal sample; an unchanged record matches its last sample (no resend); the recorded owner never takes its own sample back.
        /// 🔴 Red-proof: drop the <c>EngineOnSeconds</c> copy in <c>FromSample</c> ⇒ the bytes differ.
        /// </summary>
        [Fact]
        public void CE4664_AHeatMark_IsOnEveryNode_ALateJoinerIncluded_ByteForByte()
        {
            using var lateJoiner = new EntityRepository();
            lateJoiner.RegisterComponent<NetworkIdentity>(); lateJoiner.RegisterComponent<NetworkAuthority>(); lateJoiner.RegisterComponent<GhostStateTracker>();
            lateJoiner.RegisterComponent<GroundHeatMarks>();
            var original = new GroundHeatMarks { CellX = 1, CellY = -2 };
            GroundHeatMarkRules.Add(ref original, new HeatMark(HeatMarkKind.VehicleStand, new Vector2(80f, -100f), 1.2f, 6.9f, 3.6f, HeatMarkEnd.Rear,
                                                               (SimInstant)100.0, (SimInstant)1000.0, 900f, ThermalPowerClass.Tank), out _);
            GroundHeatMarkRules.Add(ref original, new HeatMark(HeatMarkKind.VehicleStand, new Vector2(100f, -90f), -0.4f, 4.5f, 1.8f, HeatMarkEnd.Front,
                                                               (SimInstant)1100.0, (SimInstant)1300.0, 120f, ThermalPowerClass.Car), out _);
            var sample = EntityGroundHeatMarksEgressTranslator.ToSample(6200, in original);
            Assert.True(EntityGroundHeatMarksEgressTranslator.Matches(in sample, in original));

            var map = new NetworkEntityMap();
            var ingress = new EntityGroundHeatMarksIngressTranslator(participant: null, map, new GhostCreationSystem(map), localNodeId: 3);
            using (var cmd = new EntityCommandBuffer()) { ingress.Receive(in sample, cmd, lateJoiner); cmd.Playback(lateJoiner); }
            Assert.True(map.TryGetEntity(6200, out var ghost));
            Assert.True(SameBytes(in original, lateJoiner.GetComponentRO<GroundHeatMarks>(ghost)));

            lateJoiner.AddComponent(ghost, new NetworkAuthority(primaryOwnerId: 3, localNodeId: 3));
            var newer = original;
            GroundHeatMarkRules.Add(ref newer, new HeatMark(HeatMarkKind.VehicleStand, new Vector2(70f, -70f), 0f, 5f, 2.5f, HeatMarkEnd.Rear,
                                                            (SimInstant)1400.0, (SimInstant)1500.0, 100f, ThermalPowerClass.Truck), out _);
            lateJoiner.SetComponent(ghost, newer);
            using (var cmd = new EntityCommandBuffer()) { ingress.Receive(in sample, cmd, lateJoiner); cmd.Playback(lateJoiner); }
            Assert.Equal(3, lateJoiner.GetComponentRO<GroundHeatMarks>(ghost).Count);
            Assert.False(EntityGroundHeatMarksEgressTranslator.Matches(in sample, in newer));
        }

        private static EntityRepository MineWorld()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<NetworkIdentity>(); repo.RegisterComponent<NetworkAuthority>(); repo.RegisterComponent<GhostStateTracker>();
            repo.RegisterComponent<Minefield>(); repo.RegisterComponent<MinefieldStrikes>(); repo.RegisterComponent<MinefieldKnowledge>();
            return repo;
        }

        private static bool SameBytes<T>(in T a, in T b) where T : unmanaged
            => System.Runtime.InteropServices.MemoryMarshal.AsBytes(new System.ReadOnlySpan<T>(in a)).SequenceEqual(System.Runtime.InteropServices.MemoryMarshal.AsBytes(new System.ReadOnlySpan<T>(in b)));

        /// <summary>
        /// ⭐⭐ <c>CE-4624</c> MS-T2 — the three minefield records cross the wire BYTE FOR BYTE: the authored field (type, spacing, orientation, seed, known-at-start sides, two lanes), a
        /// strikes chunk (the field's Guid halves, struck and cleared mines) and a side's knowledge (header, marked, learned-at, the 960-byte grid); each reaches a ghost on another node, an
        /// unchanged component matches its last sample (no resend), and the recorded owner never takes its own sample back. 🔴 Red-proof: drop the lane copy / the Guid high half / the
        /// cell packing in <c>FromSample</c> ⇒ the bytes differ.
        /// </summary>
        [Fact]
        public void CE4624_TheMinefieldRecords_RoundTripByteForByte_ToAGhost_AndTheOwnerNeverTakesItsOwnBack()
        {
            using var replica = MineWorld();
            var map = new NetworkEntityMap();
            var ghosts = new GhostCreationSystem(map);

            var field = new Minefield { Type = MineType.Mixed, AntiHandling = 1, SpacingM = 4.5f, OrientationDeg = 33f, Seed = 0xDEADBEEFu, KnownAtStartSides = 0b101, LaneCount = 2 };
            field.SetLane(0, new MinefieldLane(new Vector2(1, 2), new Vector2(30, 2), 4.5f, 2, 0b10));
            field.SetLane(1, new MinefieldLane(new Vector2(5, 9), new Vector2(5, 40), 8f, 0, 0));
            var fs = EntityMinefieldEgressTranslator.ToSample(7101, in field);
            Assert.True(EntityMinefieldEgressTranslator.Matches(in fs, in field));
            var fi = new EntityMinefieldIngressTranslator(participant: null, map, ghosts, localNodeId: 2);
            using (var cmd = new EntityCommandBuffer()) { fi.Receive(in fs, cmd, replica); cmd.Playback(replica); }
            Assert.True(map.TryGetEntity(7101, out var fg));
            Assert.True(SameBytes(in field, in replica.GetComponentRO<Minefield>(fg)));

            var guid = new System.Guid("0a1b2c3d-1111-2222-3333-444455556666");
            var strikes = new MinefieldStrikes { Field = guid, Chunk = 1, Count = 2 };
            strikes.Set(0, 3, -4, MineGone.Struck); strikes.Set(1, -1, 12, MineGone.Cleared);
            var ss = EntityMinefieldStrikesEgressTranslator.ToSample(7102, in strikes);
            Assert.True(EntityMinefieldStrikesEgressTranslator.Matches(in ss, in strikes));
            var si = new EntityMinefieldStrikesIngressTranslator(participant: null, map, ghosts, localNodeId: 2);
            using (var cmd = new EntityCommandBuffer()) { si.Receive(in ss, cmd, replica); cmd.Playback(replica); }
            Assert.True(map.TryGetEntity(7102, out var sg));
            Assert.True(SameBytes(in strikes, in replica.GetComponentRO<MinefieldStrikes>(sg)));

            var know = new MinefieldKnowledge();
            ((IKeyedRecord<KnowledgeKey, KnowledgeItem>)know).RecordKey = new KnowledgeKey(guid, 2);
            know.Header.Marked = 1; know.Header.Source = KnowledgeSource.Strike; know.Header.LearnedAt = (SimInstant)41.5; know.Header.Chunk = 0;
            know.Set(0, 0, MineCellKnowledge.Suspected); know.Set(59, 63, MineCellKnowledge.KnownMined); know.Set(17, 30, MineCellKnowledge.KnownClear);
            var ks = EntityMinefieldKnowledgeEgressTranslator.ToSample(7103, in know);
            Assert.True(EntityMinefieldKnowledgeEgressTranslator.Matches(in ks, in know));
            var ki = new EntityMinefieldKnowledgeIngressTranslator(participant: null, map, ghosts, localNodeId: 2);
            using (var cmd = new EntityCommandBuffer()) { ki.Receive(in ks, cmd, replica); cmd.Playback(replica); }
            Assert.True(map.TryGetEntity(7103, out var kg));
            Assert.True(SameBytes(in know, in replica.GetComponentRO<MinefieldKnowledge>(kg)));
            Assert.Equal(MineCellKnowledge.KnownMined, replica.GetComponentRO<MinefieldKnowledge>(kg).Get(59, 63));

            // a change is a new sample; the recorded owner never takes its own sample back
            var more = know; more.Set(1, 1, MineCellKnowledge.KnownMined);
            Assert.False(EntityMinefieldKnowledgeEgressTranslator.Matches(in ks, in more));
            replica.AddComponent(kg, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 2));
            replica.SetComponent(kg, more);
            using (var cmd = new EntityCommandBuffer()) { ki.Receive(in ks, cmd, replica); cmd.Playback(replica); }
            Assert.Equal(MineCellKnowledge.KnownMined, replica.GetComponentRO<MinefieldKnowledge>(kg).Get(1, 1));
        }

        /// <summary>⭐ CE-2468 (UF-2) — the toolkit states the under-fire ordinal (it cannot reference NED); this pins it equal to the wire's.</summary>
        [Fact]
        public void CE2468_TheUnderFireOrdinal_MatchesTheWire()
            => Assert.Equal((long)Hrot.NED.Descriptors.EDescriptorType.dtUnderFireKnowledge, Fdp.Toolkit.Terrain.UnderFireWriter.DescriptorOrdinal);

        /// <summary>
        /// ⭐ <c>CE-2468</c> UF-2 — a side's under-fire record (every field distinctive) crosses to a ghost BYTE FOR BYTE, an unchanged record matches its last sample (no resend), a change
        /// (State 0 → 1) does not, and the recorded owner never takes its own sample back. A ghost's removal when the owner retires the record is the generic entity-destroy path
        /// (DeleteEntityRequest), pinned by <c>DestroyEntityCommandEgressTranslatorTests</c> and <c>EntityDestroyIntegrationTests</c> — the descriptor ingress only applies Alive samples.
        /// 🔴 Red-proof: drop the Point.Z copy in <c>FromSample</c> ⇒ the bytes differ; drop the State test in <c>Matches</c> ⇒ the change is not re-published; drop the owner guard ⇒ the owner takes its own back.
        /// </summary>
        [Fact]
        public void CE2468_AnUnderFireRecord_RoundTripsByteForByte_ToAGhost_AndTheOwnerNeverTakesItsOwnBack()
        {
            using var replica = MineWorld();
            replica.RegisterComponent<UnderFireKnowledge>();
            var map = new NetworkEntityMap();
            var ghosts = new GhostCreationSystem(map);

            var know = new UnderFireKnowledge
            {
                Cause = UnderFireCause.Direct, Point = new Vector3(123.25f, 7.5f, -45.75f), RangeM = 61.5f, EnemyStrength = 3.25f,
                Header = new PlaceKnowledgeHeader { SubjectKind = SubjectKind.Place, SidePlus1 = 3, Kind = PlaceKnowledgeKind.UnderFire, State = 0, Source = KnowledgeSource.Contact,
                                                    Subject = new System.Guid("0a1b2c3d-1111-2222-3333-444455556666"), ObjectIndex = -1, LearnedAt = (SimInstant)1234.5 },
            };
            var ks = EntityUnderFireKnowledgeEgressTranslator.ToSample(7201, in know);
            Assert.True(EntityUnderFireKnowledgeEgressTranslator.Matches(in ks, in know));
            var ki = new EntityUnderFireKnowledgeIngressTranslator(participant: null, map, ghosts, localNodeId: 2);
            using (var cmd = new EntityCommandBuffer()) { ki.Receive(in ks, cmd, replica); cmd.Playback(replica); }
            Assert.True(map.TryGetEntity(7201, out var kg));
            Assert.True(SameBytes(in know, in replica.GetComponentRO<UnderFireKnowledge>(kg)));

            // a change is a new sample; the recorded owner never takes its own sample back
            var cooled = know; cooled.Header.State = 1;
            Assert.False(EntityUnderFireKnowledgeEgressTranslator.Matches(in ks, in cooled));
            replica.AddComponent(kg, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 2));
            replica.SetComponent(kg, cooled);
            using (var cmd = new EntityCommandBuffer()) { ki.Receive(in ks, cmd, replica); cmd.Playback(replica); }
            Assert.Equal(1, replica.GetComponentRO<UnderFireKnowledge>(kg).Header.State);
        }

        /// <summary>
        /// ⭐ <c>CE-4624</c> MS-T4 — the runtime LANES record (a field's Guid, two cleared lanes with segment, width and side) and the clearer's tool marker (<c>MineClearing</c>: tool, width, the
        /// line charge's landing point) cross the wire byte for byte. 🔴 Red-proof: drop the <c>LineTo</c> copy / a lane's <c>SidePlus1</c> in <c>FromSample</c> ⇒ the bytes differ.
        /// </summary>
        [Fact]
        public void CE4624_TheLanesRecord_AndTheClearersMarker_RoundTripByteForByte()
        {
            using var replica = MineWorld();
            replica.RegisterComponent<MinefieldLanes>(); replica.RegisterComponent<MineClearing>();
            var map = new NetworkEntityMap();
            var ghosts = new GhostCreationSystem(map);
            var lanes = new MinefieldLanes { Field = new System.Guid("0a1b2c3d-1111-2222-3333-444455556666"), Chunk = 0 };
            ((IKeyedRecord<System.Guid, LaneItem>)lanes).TryAdd(new LaneItem(lanes.Field, new Vector2(1, 2), new Vector2(30, 2), 4.5f, 2));
            ((IKeyedRecord<System.Guid, LaneItem>)lanes).TryAdd(new LaneItem(lanes.Field, new Vector2(5, 9), new Vector2(5, 80), 8f, 1));
            var ls = EntityMinefieldLanesEgressTranslator.ToSample(7104, in lanes);
            Assert.True(EntityMinefieldLanesEgressTranslator.Matches(in ls, in lanes));
            var li = new EntityMinefieldLanesIngressTranslator(participant: null, map, ghosts, localNodeId: 2);
            using (var cmd = new EntityCommandBuffer()) { li.Receive(in ls, cmd, replica); cmd.Playback(replica); }
            Assert.True(map.TryGetEntity(7104, out var lg));
            Assert.True(SameBytes(in lanes, in replica.GetComponentRO<MinefieldLanes>(lg)));

            var mc = new MineClearing { Active = 1, Tool = MineClearingTool.LineCharge, WidthM = 8f, LineTo = new System.Numerics.Vector3(90, 4, 0) };
            var cs = EntityMineClearingEgressTranslator.ToSample(7105, in mc);
            Assert.True(EntityMineClearingEgressTranslator.Same(in cs, in mc));
            Assert.True(SameBytes(in mc, EntityMineClearingIngressTranslator.FromSample(in cs)));
        }

        /// <summary>
        /// ⭐ <c>CE-4622</c> K-2 (Checkpoint RM-2, §5a) — a hull's <c>RamOrder</c> (on, the target's network id, the crowd flag) crosses the wire byte for byte on <c>dtRamOrder</c>
        /// = 196 (fixed by the coordinator); an unchanged order matches its last sample (no resend); switched off, it differs (the off is sent).
        /// 🔴 Red-proof: drop the <c>Crowd</c> copy in <c>FromSample</c> ⇒ the bytes differ.
        /// </summary>
        [Fact]
        public void CE4622_TheRamOrder_RoundTripsByteForByte_OnOrdinal196()
        {
            Assert.Equal(196L, (long)Hrot.NED.Descriptors.EDescriptorType.dtRamOrder);
            Assert.Equal(126, GlobalComponentIds.RamOrder);
            var ro = new CarKinem.Core.RamOrder { Active = 1, Crowd = 1, Target = new Fdp.Toolkit.Replication.EntityRef(4622) };
            var s = EntityRamOrderEgressTranslator.ToSample(7106, in ro);
            Assert.True(EntityRamOrderEgressTranslator.Same(in s, in ro));
            Assert.True(SameBytes(in ro, EntityRamOrderIngressTranslator.FromSample(in s)));
            var off = ro; off.Active = 0;
            Assert.False(EntityRamOrderEgressTranslator.Same(in s, in off));
        }

        /// <summary>
        /// ⭐ <c>CE-4693</c> (bridge laying BL-S3) — a bridge engineer's <c>BridgeCarried</c> (count, order, the span type, both banks, the target) crosses the wire byte for byte
        /// on <c>dtBridgeCarried</c> = 247, component 292; an unchanged one matches its last sample (no resend); a changed order differs (it is sent).
        /// 🔴 Red-proof: drop the far bank's copy in <c>FromSample</c> ⇒ the bytes differ.
        /// </summary>
        [Fact]
        public void CE4693_TheBridgeCarried_RoundTripsByteForByte_OnOrdinal247()
        {
            Assert.Equal(247L, (long)Hrot.NED.Descriptors.EDescriptorType.dtBridgeCarried);
            Assert.Equal(292, GlobalComponentIds.BridgeCarried);
            var bc = new Fdp.Toolkit.Terrain.BridgeCarried
            {
                Count = 1, Order = Fdp.Toolkit.Terrain.BridgeOrder.Lay, BridgeType = 8825,
                NearBank = new Fdp.Core.WorldPoint(10.5f, -3f, 1.25f), FarBank = new Fdp.Core.WorldPoint(34.25f, -2f, 1.5f), Target = new Fdp.Toolkit.Replication.EntityRef(4693),
                Refusal = Fdp.Toolkit.Terrain.BridgeRefusal.GapTooWide, GapM = 31.5f,
            };
            var s = EntityBridgeCarriedEgressTranslator.ToSample(7107, in bc);
            Assert.True(EntityBridgeCarriedEgressTranslator.Same(in s, in bc));
            Assert.True(SameBytes(in bc, EntityBridgeCarriedIngressTranslator.FromSample(in s)));
            var recover = bc; recover.Order = Fdp.Toolkit.Terrain.BridgeOrder.Recover;
            Assert.False(EntityBridgeCarriedEgressTranslator.Same(in s, in recover));
            var cleared = bc; cleared.Refusal = Fdp.Toolkit.Terrain.BridgeRefusal.NotRefused;
            Assert.False(EntityBridgeCarriedEgressTranslator.Same(in s, in cleared));
        }
    }
}
