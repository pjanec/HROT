using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Toolkit.Replication.Services;
using Hrot.Map.Common.Replication.Egress;
using Hrot.Map.Common.Replication.Ingress;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ <c>CE-3271</c> (Q94 D, R-3018 — 📄 docs/blueprints/Architect_Question_94_Brain_Geometry_Sites.md §5.2a) — a spawn's birth level
    /// crosses from the creator to the position's owner and its "resolved" comes back, MONOTONICALLY: no owner check, a waiting request never
    /// undoes a resolved one.
    /// </summary>
    public sealed class SpawnLevelWireTests
    {
        private static (EntityRepository World, NetworkEntityMap Map, Entity Man) Node()
        {
            var w = new EntityRepository();
            w.RegisterComponent<SpawnLevelRequest>();
            var map = new NetworkEntityMap();
            var man = w.CreateEntity();
            map.Register(70L, man);
            return (w, map, man);
        }

        private static void Apply(EntityRepository w, NetworkEntityMap map, in SpawnLevelRequest r)
        {
            var sample = EntitySpawnLevelEgressTranslator.ToSample(70L, r);
            using var cmd = new EntityCommandBuffer();
            new EntitySpawnLevelIngressTranslator(participant: null, map).Apply(in sample, cmd, w);
            cmd.Playback(w);
        }

        /// <summary>The creator's request arrives with every field (a level below the ground too). ✅ Red-proof: the ingress dropping the level
        /// ⇒ red.</summary>
        [Fact]
        public void CE3271_TheCreatorsRequest_ReachesTheMuscle_WithItsModeAndLevel()
        {
            var (w, map, man) = Node();
            using (w)
            {
                Apply(w, map, new SpawnLevelRequest { Mode = SpawnHeightMode.AboveLevel, Level = -2 });
                var got = w.GetComponentRO<SpawnLevelRequest>(man);
                Assert.Equal((SpawnHeightMode.AboveLevel, (short)-2, (byte)0), (got.Mode, got.Level, got.Resolved));
            }
        }

        /// <summary>
        /// A resolved sample always applies; a waiting one never undoes it (a late or repeated creator sample cannot re-snap a man who moved).
        /// ✅ Red-proof: drop the "already here" guard in the ingress ⇒ the late waiting sample resets Resolved ⇒ red.
        /// </summary>
        [Fact]
        public void CE3271_AResolvedRequest_IsNeverUndoneByAWaitingOne()
        {
            var (w, map, man) = Node();
            using (w)
            {
                Apply(w, map, new SpawnLevelRequest { Mode = SpawnHeightMode.OnLevel, Level = 1 });
                Apply(w, map, new SpawnLevelRequest { Mode = SpawnHeightMode.OnLevel, Level = 1, Resolved = 1 });
                Assert.Equal(1, w.GetComponentRO<SpawnLevelRequest>(man).Resolved);
                Apply(w, map, new SpawnLevelRequest { Mode = SpawnHeightMode.OnLevel, Level = 1 });   // the creator's late sample
                Assert.Equal(1, w.GetComponentRO<SpawnLevelRequest>(man).Resolved);
            }
        }
    }
}
