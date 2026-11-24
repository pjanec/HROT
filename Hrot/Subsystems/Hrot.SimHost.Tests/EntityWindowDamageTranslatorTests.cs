using System.Linq;
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
    /// ⭐⭐ <c>CE-5033</c> (glass windows GW-2, docs/DESIGN_Glass_Windows.md D-3) — a building's damaged window panes cross the wire: the deciding node's
    /// <see cref="EntityWindowDamageEgressTranslator"/> builds the sample (a window's panel as its rank WITHIN the building), the receiver's
    /// <see cref="EntityWindowDamageIngressTranslator"/> lands it on ITS house by key, and that node's own traces then pass the broken pane. The
    /// <see cref="EntityBuildingDamageTranslatorTests"/> shape.
    /// </summary>
    public sealed class EntityWindowDamageTranslatorTests
    {
        private const string House = """
            { "type": "Feature", "properties": { "kind": "building", "label": "H",
                "building": { "footprint": [[0,0],[10,0],[10,8],[0,8]],
                  "storeys": [ { "height": 3, "walls": [
                    { "from": [0,0], "to": [10,0], "thickness": 0.3, "openings": [ { "kind": "window", "at": 2, "width": 1.2 }, { "kind": "window", "at": 6, "width": 1.2 } ] },
                    { "from": [10,0], "to": [10,8], "thickness": 0.3 },
                    { "from": [10,8], "to": [0,8], "thickness": 0.3 }, { "from": [0,8], "to": [0,0], "thickness": 0.3 } ] } ] } },
              "geometry": { "type": "Point", "coordinates": [20, 20] } }
            """;
        private const string Garage = """
            { "type": "Feature", "properties": { "kind": "building", "label": "G",
                "building": { "footprint": [[0,0],[6,0],[6,6],[0,6]],
                  "storeys": [ { "height": 3, "walls": [
                    { "from": [0,0], "to": [6,0], "thickness": 0.3 }, { "from": [6,0], "to": [6,6], "thickness": 0.3 },
                    { "from": [6,6], "to": [0,6], "thickness": 0.3 }, { "from": [0,6], "to": [0,0], "thickness": 0.3 } ] } ] } },
              "geometry": { "type": "Point", "coordinates": [-40, -40] } }
            """;

        private static TerrainWorld Parse(params string[] features) => TerrainWorldParser.Parse(
            "{ \"type\": \"FeatureCollection\", \"features\": [ " + string.Join(", ", features) + " ] }", "range");

        private static EntityRepository Replica(TerrainWorld terrain)
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<NetworkAuthority>();
            repo.RegisterComponent<GhostStateTracker>();
            repo.RegisterComponent<BuildingDamage>();
            repo.RegisterComponent<WindowDamage>();
            repo.RegisterManagedComponent<TerrainObjectKey>();
            repo.SetSingletonManaged(terrain);
            return repo;
        }

        private static int SouthWall(TerrainWorld t) => System.Array.FindIndex(System.Linq.Enumerable.ToArray(t.Panels),
            p => System.MathF.Abs(p.A.Y - 20f) < 1e-3f && System.MathF.Abs(p.B.Y - 20f) < 1e-3f && p.B.X > p.A.X);

        private static readonly Vector3 Outside = new(22.6f, 15f, 1.5f), Inside = new(22.6f, 25f, 1.5f);

        /// <summary>The toolkit states the wire ordinal (it cannot reference the NED assembly); this pins the two equal.</summary>
        [Fact]
        public void TheToolkitsOrdinal_IsTheDescriptorsOrdinal()
            => Assert.Equal((long)Hrot.NED.Descriptors.EDescriptorType.dtWindowDamage, BuildingDamageSystem.WindowDescriptorOrdinal);

        /// <summary>
        /// The sender's house "H" is building 1 (a garage stands before it); the receiver's is building 0. The sample names the house by KEY and the window's wall by its
        /// rank within the house, so the receiver breaks ITS "H"'s first south window and cracks the second, and its trace passes the broken one; the recorded owner never
        /// takes its own sample back. 🔴 Mutation: send the GLOBAL panel ⇒ the receiver maps a panel the house does not have ⇒ red.
        /// </summary>
        [Fact]
        public void TheWindows_LandOnTheSameHouseAndPane_WhenTheTwoWorldsNumberThemDifferently()
        {
            var sender = Parse(Garage, House);
            Assert.Equal(1, BuildingDamages.IndexOfKey(sender, "range/H"));
            var decided = new WindowDamage { Building = 1 };
            WindowDamages.Break(ref decided, SouthWall(sender), 0, 61d);                    // ⭐ CE-11051 — stamped
            WindowDamages.Hit(ref decided, SouthWall(sender), 1, 0.25f, 0.75f, 62d);
            var sample = EntityWindowDamageEgressTranslator.ToSample(5300, decided, "range/H", sender);
            Assert.True(EntityWindowDamageEgressTranslator.Matches(in sample, in decided, "range/H", sender));
            var restamped = decided; restamped.SetHitAt(1, 63d);
            Assert.False(EntityWindowDamageEgressTranslator.Matches(in sample, in restamped, "range/H", sender));   // a new time alone is a change to send

            var terrain = Parse(House);
            using var replica = Replica(terrain);
            Assert.True(terrain.QueryFire(Outside, Inside, DoorStates.Of(replica, terrain)).Any(c => c.Kind == TerrainWorld.PaneKind));
            var map = new NetworkEntityMap();
            var ingress = new EntityWindowDamageIngressTranslator(participant: null, map, new GhostCreationSystem(map), localNodeId: 2);
            using (var cmd = new EntityCommandBuffer()) { ingress.Receive(in sample, cmd, replica); cmd.Playback(replica); }
            Assert.True(map.TryGetEntity(5300, out var ghost));
            Assert.Equal("range/H", replica.GetComponent<TerrainObjectKey>(ghost).Key);
            var got = replica.GetComponentRO<WindowDamage>(ghost);
            Assert.Equal(0, got.Building);
            Assert.Equal(2, got.Count);
            Assert.Equal(new WindowEntry(SouthWall(terrain), 0, WindowDamages.BrokenHits, 0, 0, 0, 0), got.Get(0));
            Assert.Equal(new WindowEntry(SouthWall(terrain), 1, 1, 64, 191, 0, 0), got.Get(1));
            Assert.Equal((61d, 62d), (got.HitAtOf(0).Seconds, got.HitAtOf(1).Seconds));   // ⭐ CE-11051 — the times cross the wire
            Assert.False(terrain.QueryFire(Outside, Inside, DoorStates.Of(replica, terrain)).Any(c => c.Kind == TerrainWorld.PaneKind));   // the owner's broken pane is gone here too

            // the recorded OWNER ignores a sample of its own entity
            replica.AddComponent(ghost, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 2));
            replica.SetComponent(ghost, new WindowDamage { Building = 0 });
            using (var cmd = new EntityCommandBuffer()) { ingress.Receive(in sample, cmd, replica); cmd.Playback(replica); }
            Assert.Equal(0, replica.GetComponentRO<WindowDamage>(ghost).Count);
        }

        /// <summary>A window the receiver's world has no glass for is dropped, never put on another pane.</summary>
        [Fact]
        public void AWindowTheReceiverHasNoGlassFor_IsDropped()
        {
            var sender = Parse(House);
            var decided = new WindowDamage { Building = 0 };
            WindowDamages.Break(ref decided, SouthWall(sender), 1);
            var sample = EntityWindowDamageEgressTranslator.ToSample(5301, decided, "range/H", sender);
            var terrain = Parse(House.Replace("{ \"kind\": \"window\", \"at\": 6, \"width\": 1.2 }", "{ \"kind\": \"window\", \"at\": 6, \"width\": 1.2, \"glazed\": false }"));
            Assert.True(EntityWindowDamageIngressTranslator.TryFromSample(in sample, terrain, out var c));
            Assert.Equal(0, c.Count);
        }
    }
}
