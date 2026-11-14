using System;
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Xunit;

namespace Fdp.Toolkit.Scenario.Tests
{
    /// <summary>
    /// ⭐ <c>CE-4153</c> S7 (docs/DESIGN_Scenario_Entity_Keys.md) — a scenario file's entity key is the entity's STABLE
    /// <see cref="EntityGuid"/>: load → save → load keeps every key, two saves of an unchanged world are identical, and an entity that
    /// reaches save without a key is a loud defect (never a quiet mint).
    /// </summary>
    public sealed class EntityGuidStabilityTests : IDisposable
    {
        private readonly EntityRepository _repo;

        public EntityGuidStabilityTests()
        {
            ComponentTypeRegistry.Clear();
            _repo = Fresh();
        }

        public void Dispose() => _repo.Dispose();

        private static EntityRepository Fresh()
        {
            var r = new EntityRepository();
            r.RegisterComponent<DummyPosition>();
            r.RegisterComponent<ScenarioIgnoreTag>();
            r.RegisterComponent<EntityGuid>();
            return r;
        }

        private static ScenarioSerializer Serializer() => new ScenarioSerializerBuilder("TestSubsystem").Build();
        private static ScenarioHeader Header() => new("TestSubsystem");

        private static Entity Spawn(EntityRepository repo, Guid? guid, float x)
        {
            var e = repo.CreateEntity();
            repo.SetComponent(e, new DummyPosition { X = x });
            if (guid.HasValue) repo.AddComponent(e, new EntityGuid(guid.Value));
            return e;
        }

        [Fact]
        public void Save_WritesTheEntityGuidAsTheKey_AndNeverAsAComponent()
        {
            var g = Guid.NewGuid();
            Spawn(_repo, g, 1f);

            var dom = Serializer().Serialize(_repo, Header());
            var ents = dom["Entities"]!.AsObject();

            Assert.Equal(g.ToString(), Assert.Single(ents).Key);
            Assert.False(ents[g.ToString()]!.AsObject().ContainsKey(nameof(EntityGuid)));   // NoScenario: the key, not a field
        }

        [Fact]
        public void LoadSaveLoad_KeepsEveryKey()
        {
            var keys = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
            for (int i = 0; i < keys.Length; i++) Spawn(_repo, keys[i], i);
            var s = Serializer();
            var first = s.Serialize(_repo, Header());

            using var second = Fresh();
            s.Deserialize(second, first);
            var again = s.Serialize(second, Header());

            using var third = Fresh();
            s.Deserialize(third, again);
            var last = s.Serialize(third, Header());

            var expected = keys.Select(k => k.ToString()).OrderBy(k => k).ToArray();
            Assert.Equal(expected, again["Entities"]!.AsObject().Select(kv => kv.Key).OrderBy(k => k).ToArray());
            Assert.Equal(expected, last["Entities"]!.AsObject().Select(kv => kv.Key).OrderBy(k => k).ToArray());
        }

        [Fact]
        public void TwoSavesOfAnUnchangedWorld_AreIdentical()
        {
            Spawn(_repo, Guid.NewGuid(), 1f);
            Spawn(_repo, Guid.NewGuid(), 2f);
            var s = Serializer();

            var a = s.Serialize(_repo, Header()).ToJsonString();
            var b = s.Serialize(_repo, Header()).ToJsonString();

            Assert.Equal(a, b);   // before CE-4153 every save minted fresh keys
        }

        [Fact]
        public void AnEntityWithoutAGuid_OnATrackingRepo_FailsTheSaveLoudly()
        {
            Spawn(_repo, Guid.NewGuid(), 1f);
            Spawn(_repo, null, 2f);   // a creation path that stamped nothing

            var ex = Assert.Throws<InvalidOperationException>(() => Serializer().Serialize(_repo, Header()));
            Assert.Contains("no EntityGuid", ex.Message);
        }

        [Fact]
        public void TwoEntitiesWithTheSameGuid_FailTheSave_NamingBoth()
        {
            var g = Guid.NewGuid();
            Spawn(_repo, g, 1f);
            Spawn(_repo, g, 2f);

            var ex = Assert.Throws<InvalidOperationException>(() => Serializer().Serialize(_repo, Header()));
            Assert.Contains(g.ToString(), ex.Message);
        }

        [Fact]
        public void OwnershipMovedToAnotherNode_KeepsTheKey()
        {
            // Node A owns the entity and saves; the hand-over then makes node B (which learned the GUID from dtEntityGuid) the
            // primary owner — B's slice must carry the SAME key, or the merged file would hold the entity twice / re-key it.
            var g = Guid.NewGuid();
            var s = Serializer();

            using var nodeA = Fresh();
            nodeA.RegisterComponent<NetworkAuthority>();
            var ea = Spawn(nodeA, g, 1f);
            nodeA.AddComponent(ea, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
            var sliceA = s.Serialize(nodeA, Header());

            using var nodeB = Fresh();
            nodeB.RegisterComponent<NetworkAuthority>();
            var eb = Spawn(nodeB, g, 1f);   // the replica: same entity, GUID applied by dtEntityGuid ingress
            nodeB.AddComponent(eb, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 2));   // after the hand-over B is the primary
            var sliceB = s.Serialize(nodeB, Header());

            Assert.Equal(g.ToString(), Assert.Single(sliceA["Entities"]!.AsObject()).Key);
            Assert.Equal(g.ToString(), Assert.Single(sliceB["Entities"]!.AsObject()).Key);
        }

        [Fact]
        public void ABareToolkitRepo_ThatNeverRegisteredEntityGuid_KeepsEphemeralKeys()
        {
            ComponentTypeRegistry.Clear();
            using var bare = new EntityRepository();
            bare.RegisterComponent<DummyPosition>();
            var e = bare.CreateEntity();
            bare.SetComponent(e, new DummyPosition { X = 1f });

            var dom = Serializer().Serialize(bare, Header());   // not tracking identity ⇒ the generic toolkit behaviour, as before

            Assert.True(Guid.TryParse(Assert.Single(dom["Entities"]!.AsObject()).Key, out _));
        }
    }
}
