using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Hrot.Common.Systems;
using Hrot.SimHost.Tests;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ <c>CE-4153</c> S2 (docs/DESIGN_Scenario_Entity_Keys.md) — <c>CreateEntityRequestSystem</c> is the ONE place an entity's stable
    /// <see cref="EntityGuid"/> is decided: a request that brings its file key keeps it, a non-transient authored request is minted ONCE,
    /// a transient (sketch) request gets none, and a composite template's children / seat presets DERIVE theirs from the parent's
    /// (<see cref="EntityGuidDerivation"/>) so two loads of the same parent give the same children.
    /// </summary>
    public sealed class EntityGuidMintTests
    {
        private const long ParentType = 42L, ChildType = 43L;
        private const int Node = 7;

        private static EntityRepository World()
        {
            var r = new EntityRepository();
            r.RegisterComponent<NetworkIdentity>();
            r.RegisterComponent<NetworkAuthority>();
            r.RegisterComponent<TkbIdentity>();
            r.RegisterComponent<GhostStateTracker>();
            r.RegisterEvent<Fdp.Toolkit.Lifecycle.Events.ConstructionOrder>();
            r.RegisterEvent<Fdp.Toolkit.Lifecycle.Events.DestructionOrder>();
            return r;
        }

        private static TkbDatabase Tkb(bool withChild)
        {
            var db = new TkbDatabase();
            var parent = new TkbTemplate("P", ParentType);
            if (withChild)
            {
                parent.ChildBlueprints.Add(new ChildBlueprintDefinition(2, (Fdp.Core.TkbTypeId)(ChildType)));
                parent.ChildBlueprints.Add(new ChildBlueprintDefinition(5, (Fdp.Core.TkbTypeId)(ChildType)));
            }
            db.Register(parent);
            db.Register(new TkbTemplate("C", ChildType));
            return db;
        }

        private static List<SpawnEntityCommand> Spawn(bool withChild, bool transient, Guid? bringsKey = null)
        {
            using var repo = World();
            var source = new StubRequestSource();
            var comps = new List<object> { new EntityInfo { Name = "X", ForceId = ForceId.Friend } };
            if (bringsKey.HasValue) comps.Add(new EntityGuid(bringsKey.Value));
            source.Enqueue(new EntityCreationRequest
            {
                RequestId = Guid.NewGuid(), OwnerAppInstanceId = Node, TkbType = (Fdp.Core.TkbTypeId)(ParentType), IsTransient = transient,
                InitialComponents = comps,
            });
            var system = new CreateEntityRequestSystem(source, new StubAckSink(), Tkb(withChild), new StubIdAllocator(100), Node);
            system.Execute(repo, 0f);
            repo.Bus.SwapBuffers();
            return ((ISimulationView)repo).ReadManagedEvents<SpawnEntityCommand>().ToList();
        }

        private static Guid? KeyOf(SpawnEntityCommand c) => c.InitialComponents?.OfType<EntityGuid>().Select(g => (Guid?)g.Value).FirstOrDefault();

        [Fact]
        public void ANonTransientAuthoredRequest_IsMintedOnce_ATransientOneGetsNone()
        {
            var one = Assert.Single(Spawn(false, transient: false));
            Assert.NotNull(KeyOf(one));
            Assert.Null(KeyOf(Assert.Single(Spawn(false, transient: true))));
        }

        [Fact]
        public void ARequestThatBringsItsFileKey_KeepsIt()
        {
            var g = Guid.NewGuid();
            Assert.Equal(g, KeyOf(Assert.Single(Spawn(false, transient: false, bringsKey: g))));
        }

        [Fact]
        public void CompositeChildren_DeriveTheirGuidFromTheParent_IdenticalAcrossTwoSpawns()
        {
            var g = Guid.NewGuid();
            var a = Spawn(true, false, g);
            var b = Spawn(true, false, g);   // a second load of the same parent

            var kids = a.Where(c => c.TkbType == ChildType).Select(KeyOf).ToArray();
            Assert.Equal(2, kids.Length);
            Assert.Equal(new Guid?[] { EntityGuidDerivation.Derive(g, "child:2"), EntityGuidDerivation.Derive(g, "child:5") }, kids.OrderBy(x => x == EntityGuidDerivation.Derive(g, "child:2") ? 0 : 1).ToArray());
            Assert.Equal(kids.OrderBy(x => x), b.Where(c => c.TkbType == ChildType).Select(KeyOf).OrderBy(x => x));
            Assert.NotEqual(kids[0], kids[1]);   // distinct per slot
        }

        [Fact]
        public void ATransientParent_HasNoDerivedChildren()
        {
            Assert.All(Spawn(true, transient: true), c => Assert.Null(KeyOf(c)));
        }
    }

    public sealed class EntityGuidDerivationTests
    {
        [Fact]
        public void MatchesTheRfc4122_V5_KnownVector()
        {
            // RFC 4122 appendix B style: uuid5(NAMESPACE_DNS, "python.org")
            var dns = Guid.Parse("6ba7b810-9dad-11d1-80b4-00c04fd430c8");
            Assert.Equal(Guid.Parse("886313e1-3b8a-5372-9b90-0c9aee199e5d"), EntityGuidDerivation.Derive(dns, "python.org"));
        }

        [Fact]
        public void IsDeterministic_AndDiffersByNameAndParent()
        {
            var p = Guid.NewGuid();
            Assert.Equal(EntityGuidDerivation.Derive(p, "a"), EntityGuidDerivation.Derive(p, "a"));
            Assert.NotEqual(EntityGuidDerivation.Derive(p, "a"), EntityGuidDerivation.Derive(p, "b"));
            Assert.NotEqual(EntityGuidDerivation.Derive(p, "a"), EntityGuidDerivation.Derive(Guid.NewGuid(), "a"));
        }
    }
}
