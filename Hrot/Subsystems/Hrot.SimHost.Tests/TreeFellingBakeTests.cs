using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using CarKinem.Road;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.Replication.Egress;
using Hrot.Map.Common.Replication.Ingress;
using Hrot.Map.Common.Services;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4603</c> (B-6; docs/DESIGN_Vehicle_Breaching.md §7.7) — a felled lone trunk leaves the baked world (the navmesh and the drawing follow) on every node,
    /// its box is the dirty rectangle, a seek back brings it back; and the record crosses the wire (<c>dtTreeDamage</c>).
    /// </summary>
    public sealed class TreeFellingBakeTests : IDisposable
    {
        private const string Field = """
            {"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,60,60],"groundZ":0},"features":[
             {"type":"Feature","properties":{"kind":"tree","label":"oak"},"geometry":{"type":"Point","coordinates":[30,30]}}]}
            """;
        private readonly EntityRepository _world = new();
        private readonly string _staging = Path.Combine(Path.GetTempPath(), "b6-" + Guid.NewGuid().ToString("N"));
        private readonly TerrainWorld _field = TerrainWorldParser.Parse(Field, "range");
        private double _wall;

        public TreeFellingBakeTests() { SimHostComponentRegistry.RegisterAll(_world); Directory.CreateDirectory(_staging); }
        public void Dispose() { _world.Dispose(); try { Directory.Delete(_staging, true); } catch { } }

        private void Bake(StaticObstacleBakeSystem sys, int bakes)
        {
            sys.Execute(_world, 0.016f); _wall += 0.6;
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (sys.Bakes < bakes && DateTime.UtcNow < deadline) { sys.Execute(_world, 0.016f); if (sys.Bakes < bakes) Thread.Sleep(5); }
            Assert.Equal(bakes, sys.Bakes);
        }

        private int Trunks => _world.GetSingletonManaged<TerrainWorld>()!.Prisms.Count(p => p.Kind == TerrainPrismKind.Tree);

        /// <summary>🔴 Red-proof: drop the felled trunks from <c>TerrainObstacles.With</c> ⇒ the resident world keeps the trunk.</summary>
        [Fact]
        public void AFelledTrunk_LeavesTheBakedWorld_ItsBoxIsDirty_AndASeekBackRestoresIt()
        {
            var residency = new TerrainResidency(_staging, new RoadNetworkHolder());
            residency.Commit(_world, new TerrainResidency.Staged { Definition = new TerrainDefinition { Name = "range" }, World = _field, TerrainName = "range" });
            var sys = new StaticObstacleBakeSystem(() => _wall);
            var oak = TreeDamages.KeyOf(_field.Prisms.First(p => p.Kind == TerrainPrismKind.Tree));
            var first = new TreeDamage(); (first.CellX, first.CellY) = TreeDamages.CellOf(oak);
            var other = first; TreeDamages.Add(ref other, (100, 100), out _);   // a far, non-existent key: the baseline bake
            var e0 = _world.CreateEntity(); _world.AddComponent(e0, other);
            Bake(sys, 1);
            Assert.Equal(1, Trunks);

            var d = new TreeDamage(); (d.CellX, d.CellY) = TreeDamages.CellOf(oak); TreeDamages.Add(ref d, oak, out _);
            var e = _world.CreateEntity(); _world.AddComponent(e, d);
            Bake(sys, 2);
            Assert.Equal(0, Trunks);
            var dirty = Assert.Single(sys.LastDirty!);
            Assert.True(dirty.Min.X < 30f && dirty.Max.X > 30f && dirty.Min.Y < 30f && dirty.Max.Y > 30f);

            _world.DestroyEntity(e);
            Bake(sys, 3);
            Assert.Equal(1, Trunks);
        }

        [Fact]
        public void TheToolkitsOrdinal_IsTheDescriptorsOrdinal()
            => Assert.Equal((long)Hrot.NED.Descriptors.EDescriptorType.dtTreeDamage, TreeDamageSystem.DescriptorOrdinal);

        /// <summary>The wire form round-trips byte for byte and the replica's view has the trunk felled.</summary>
        [Fact]
        public void TheSample_RoundTrips_AndTheReplicasViewHasTheTrunkFelled()
        {
            using var replica = new EntityRepository();
            replica.RegisterComponent<NetworkIdentity>(); replica.RegisterComponent<NetworkAuthority>(); replica.RegisterComponent<GhostStateTracker>(); replica.RegisterComponent<TreeDamage>();
            replica.SetSingletonManaged(_field);
            var oak = TreeDamages.KeyOf(_field.Prisms.First(p => p.Kind == TerrainPrismKind.Tree));
            var d = new TreeDamage(); (d.CellX, d.CellY) = TreeDamages.CellOf(oak); TreeDamages.Add(ref d, oak, out _);
            var sample = EntityTreeDamageEgressTranslator.ToSample(6200, in d);
            Assert.True(EntityTreeDamageEgressTranslator.Matches(in sample, in d));
            var map = new NetworkEntityMap();
            var ingress = new EntityTreeDamageIngressTranslator(participant: null, map, new GhostCreationSystem(map), localNodeId: 2);
            using (var cmd = new EntityCommandBuffer()) { ingress.Receive(in sample, cmd, replica); cmd.Playback(replica); }
            Assert.True(map.TryGetEntity(6200, out var ghost));
            Assert.True(System.Runtime.InteropServices.MemoryMarshal.AsBytes(new ReadOnlySpan<TreeDamage>(in d))
                .SequenceEqual(System.Runtime.InteropServices.MemoryMarshal.AsBytes(new ReadOnlySpan<TreeDamage>(replica.GetComponentRO<TreeDamage>(ghost)))));
            Assert.False(_field.SegmentBlocked(new Vector3(20f, 30f, 1.5f), new Vector3(40f, 30f, 1.5f), DoorStates.Of(replica, _field)));
        }
    }
}
