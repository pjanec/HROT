using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Xunit;

namespace Fdp.Toolkit.Scenario.Tests
{
    /// <summary>
    /// ⭐ CE-4206 R1 (DESIGN_Map_Picking_Unification §6.2, F2) — <see cref="LiveRefScope"/>: the reference scope of a RUNNING world (names, GUIDs, keys and the
    /// reverse lookups the editor's link leaves need). There was no <c>ScenarioRefScope</c> test class to extend (its callers are the extractor rails), so this is the scope's first.
    /// Red-proof: a rebuild that never fires (<c>Refresh</c> answering from the first index) ⇒ <c>A_spawn_after_the_first_query_is_found</c> fails.
    /// </summary>
    public sealed class LiveRefScopeTests : IDisposable
    {
        private readonly EntityRepository _repo;
        private long _now;
        private int _nextId = 100;

        public LiveRefScopeTests()
        {
            ComponentTypeRegistry.Clear();
            _repo = new EntityRepository();
            _repo.RegisterComponent<NetworkIdentity>();
            _repo.RegisterComponent<EntityGuid>();
            _repo.RegisterComponent<RefKey>();
            _repo.RegisterComponent<EntityInfo>();
            _repo.RegisterComponent<Fdp.Core.SimTransform>();
        }

        public void Dispose() => _repo.Dispose();

        private LiveRefScope Scope() => new(_repo, () => _now);

        private (Entity E, EntityRef R) Spawn(string? name = null, string? key = null, Guid? guid = null, Vector3? at = null)
        {
            var e = _repo.CreateEntity();
            int id = _nextId++;
            _repo.AddComponent(e, new NetworkIdentity(id));
            if (name != null) _repo.AddComponent(e, new EntityInfo { Name = new FixedString64(name) });
            if (key != null) _repo.AddComponent(e, new RefKey(key));
            if (guid != null) _repo.AddComponent(e, new EntityGuid(guid.Value));
            if (at != null) _repo.AddComponent(e, new Fdp.Core.SimTransform { Position = at.Value, Rotation = Quaternion.Identity });
            return (e, new EntityRef(id));
        }

        [Fact]
        public void GuidAndKey_ResolveToTheEntity_AndTheReverseLookupsAnswer()
        {
            var g = Guid.NewGuid();
            var hq = Spawn("Blue HQ", key: "BlueHQ", guid: g, at: new Vector3(10, 20, 3));
            var bare = Spawn();
            var scope = Scope();

            Assert.True(scope.TryResolve(RefSpec.ByExact(g), out var byGuid));
            Assert.Equal(hq.R, byGuid);
            Assert.True(scope.TryResolve(RefSpec.ByKey("@BlueHQ"), out var byKey));
            Assert.Equal(hq.R, byKey);
            Assert.False(scope.TryResolve(RefSpec.ByKey("Nope"), out _));
            Assert.False(scope.TryResolve(RefSpec.None, out _));

            Assert.True(scope.TryGuidOf(hq.R, out var guid)); Assert.Equal(g, guid);
            Assert.True(scope.TryKeyOf(hq.R, out var key)); Assert.Equal("BlueHQ", key);
            Assert.False(scope.TryGuidOf(bare.R, out _));                      // a bullet has no stable id
            Assert.False(scope.TryKeyOf(bare.R, out _));
            Assert.True(scope.TryPositionOf(hq.R, out var p)); Assert.Equal(new WorldPoint(10, 20, 3), p);
            Assert.Equal("Blue HQ", scope.NameOf(hq.R));
            Assert.Equal("#" + bare.R.NetworkId, scope.NameOf(bare.R));
            Assert.Equal("(none)", scope.NameOf(EntityRef.None));
            Assert.Equal(new[] { "BlueHQ" }, scope.Keys());
        }

        [Fact]
        public void A_spawn_after_the_first_query_is_found()
        {
            var scope = Scope();
            Assert.False(scope.TryResolve(RefSpec.ByKey("Late"), out _));       // first query builds an empty index
            var late = Spawn(key: "Late");
            Assert.True(scope.TryResolve(RefSpec.ByKey("Late"), out var r));    // the world grew ⇒ the index rebuilt, no waiting
            Assert.Equal(late.R, r);
            Assert.Equal(2, scope.Rebuilds);
        }

        /// <summary>⭐ the ONE sim-side scope (<see cref="LiveRefScope.Of"/>): cached per world, a same-tick spawn is found (count moved), a key edited in place is found on the NEXT
        /// tick with NO wall-clock wait (sim must not read wall time), and a duplicate key shows in <see cref="LiveRefScope.Ambiguous"/> (SR-188; the first resolves).
        /// ✅ Red-proof: <c>Of</c> built with the default 250 ms window ⇒ the in-place edit is not found after the tick ⇒ red.</summary>
        [Fact]
        public void TheSimScope_IsOnePerWorld_FindsASameTickSpawn_AndAnInPlaceEditAfterATick_WithoutWallTime()
        {
            var scope = LiveRefScope.Of(_repo);
            Assert.Same(scope, LiveRefScope.Of(_repo));
            var unit = Spawn(name: "Unit");
            Assert.False(scope.TryResolve(RefSpec.ByKey("Late"), out _));
            var late = Spawn(key: "Late");
            Assert.True(scope.TryResolve(RefSpec.ByKey("Late"), out var r)); Assert.Equal(late.R, r);   // spawned the same tick ⇒ found
            _repo.AddComponent(unit.E, new RefKey("Edited"));
            _repo.Tick();                                                                                  // the frame clock moves; no wall time passes
            Assert.True(scope.TryResolve(RefSpec.ByKey("Edited"), out var e)); Assert.Equal(unit.R, e);
            var a = Spawn(key: "Ford"); Spawn(key: "Ford");
            Assert.True(scope.TryResolve(RefSpec.ByKey("Ford"), out var first)); Assert.Equal(a.R, first);
            Assert.Equal(2, scope.Ambiguous["Ford"]);
        }

        [Fact]
        public void A_key_edited_in_place_is_found_after_the_rebuild_interval_or_an_Invalidate()
        {
            var e = Spawn(name: "Unit");
            var scope = Scope();
            Assert.False(scope.TryResolve(RefSpec.ByKey("Edited"), out _));
            _repo.AddComponent(e.E, new RefKey("Edited"));
            scope.Invalidate();
            Assert.True(scope.TryResolve(RefSpec.ByKey("Edited"), out var r));
            Assert.Equal(e.R, r);
        }

        [Fact]
        public void A_duplicate_key_is_reported_not_thrown()
        {
            Spawn(key: "Twin"); Spawn(key: "Twin"); Spawn(key: "Solo");
            var scope = Scope();
            Assert.True(scope.HasDuplicate("Twin"));
            Assert.True(scope.HasDuplicate("@Twin"));
            Assert.False(scope.HasDuplicate("Solo"));
            Assert.True(scope.TryResolve(RefSpec.ByKey("Twin"), out _));        // still answers (the first found); the UI shows it red
            Assert.Equal(new[] { "Solo", "Twin" }, scope.Keys());
        }

        [Fact]
        public void A_world_without_the_components_is_an_empty_scope_not_a_crash()
        {
            ComponentTypeRegistry.Clear();
            using var bare = new EntityRepository();
            var scope = new LiveRefScope(bare);
            Assert.False(scope.TryResolve(RefSpec.ByKey("X"), out _));
            Assert.Empty(scope.Keys());
        }

        /// <summary>⭐ U-8 (DESIGN_Condition_Editor §8.2 ④) — the entity-link chip's text comes from the scope: <c>Kind: Name</c> with a kind, the name alone
        /// without one, <c>Kind #id</c> for a nameless thing; built at the rebuild, so asking again allocates nothing.</summary>
        [Fact]
        public void U8_TheChipLabel_IsKindThenName_FromTheScopesKinds_AndCostsNothingPerAsk()
        {
            var (obj, objRef) = Spawn(name: "Hold the bridge");
            var (_, tankRef) = Spawn(name: "Blue 1");
            var (anon, anonRef) = Spawn();
            var scope = new LiveRefScope(_repo, () => _now, kinds: (w, e) => e == obj ? new EntityKind("Objective", "thing/objective") : e == anon ? new EntityKind("Trigger", "thing/trigger") : null);

            Assert.Equal("Objective: Hold the bridge", scope.ChipLabelOf(objRef));
            Assert.Equal("Blue 1", scope.ChipLabelOf(tankRef));
            Assert.Equal($"Trigger #{anonRef.NetworkId}", scope.ChipLabelOf(anonRef));
            Assert.True(scope.TryKindOf(objRef, out var k) && k.IconKey == "thing/objective");
            Assert.False(scope.TryKindOf(tankRef, out _));
            Assert.Equal("Hold the bridge", Scope().ChipLabelOf(objRef));   // a scope with no kinds: the name alone
            Assert.Equal("(none)", scope.ChipLabelOf(EntityRef.None));

            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 50; i++) { scope.ChipLabelOf(objRef); scope.TryKindOf(objRef, out _); }
            Assert.Equal(before, System.GC.GetAllocatedBytesForCurrentThread());
        }

        /// <summary>⭐ N-5 (DESIGN_Struct_Editor §5, CE-3382) — what the one entity chip reads: a destroyed entity is GONE (not Exists), the side comes from
        /// <see cref="EntityInfo.ForceId"/>, the picker's rows are the named ids BY NAME, and <see cref="LiveRefScope.Generation"/> moves on a rebuild so a cached
        /// row list knows to rebuild. Red-proof: <c>_named</c> unsorted ⇒ the order assert fails.</summary>
        [Fact]
        public void N5_Exists_Side_AndTheNamedRows_ByName()
        {
            var (_, zulu) = Spawn("Zulu 9");
            var (alphaE, alpha) = Spawn("alpha 1");
            var (_, mike) = Spawn("Mike 4");
            Spawn();                                                             // nameless: not a row
            _repo.SetComponent(alphaE, new EntityInfo { Name = new FixedString64("alpha 1"), ForceId = ForceId.Hostile });
            var scope = Scope();

            Assert.Equal(new[] { alpha.NetworkId, mike.NetworkId, zulu.NetworkId }, scope.NamedIds().ToArray());
            Assert.True(scope.Exists(mike));
            Assert.False(scope.Exists(new EntityRef(9999)));
            Assert.False(scope.Exists(EntityRef.None));
            Assert.True(scope.TrySideOf(alpha, out var side) && side == ForceId.Hostile);
            Assert.True(scope.TrySideOf(zulu, out var z) && z == ForceId.Neutral);

            int gen = scope.Generation;
            Assert.Equal(gen, scope.Generation);                                // a steady ask: no rebuild
            _repo.DestroyEntity(alphaE);
            scope.Invalidate();
            Assert.NotEqual(gen, scope.Generation);
            Assert.False(scope.Exists(alpha));                                  // destroyed ⇒ the chip reads gone
            Assert.Equal(new[] { mike.NetworkId, zulu.NetworkId }, scope.NamedIds().ToArray());
        }
    }
}
