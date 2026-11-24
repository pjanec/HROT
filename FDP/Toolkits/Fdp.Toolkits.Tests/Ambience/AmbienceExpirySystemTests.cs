using System;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.Ambient;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.World;
using Xunit;

namespace Fdp.Toolkit.Ambient.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-11034</c> (R-11014; 📄 docs/designs/audio/DESIGN_Ambience.md §1, §5 slice 1) — the ambience layer's OWNER side: <see cref="AmbienceExpirySystem"/> ends a layer at its sim
    /// time (never before; 0 = never), on SIM time alone, only on the node that owns it, through the one network destroy when it is replicated — and costs 0 bytes in a steady frame.
    /// </summary>
    public sealed class AmbienceExpirySystemTests : IDisposable
    {
        private readonly EntityRepository _w = new();
        private readonly AmbienceExpirySystem _sys = new();

        public AmbienceExpirySystemTests()
        {
            _w.RegisterComponent<SimTransform>();
            _w.RegisterComponent<Ambience>();
            Clock(0);
        }

        public void Dispose() => _w.Dispose();

        private void Clock(double t) => _w.SetSingletonUnmanaged(new GlobalTime { TotalTime = t });

        private Entity Layer(string kind, double until, Vector3? at = null)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new Ambience { Kind = new FixedString32(kind), Intensity = 1f, Radius = 800f, Until = (SimInstant)until });
            if (at is { } p) _w.AddComponent(e, new SimTransform { Position = p, Rotation = Quaternion.Identity });
            return e;
        }

        private void Frame(double t) { Clock(t); _sys.Execute(_w, 0.016f); _w.Bus.SwapBuffers(); }   // the commands it published are readable after the swap

        /// <summary>The component is a recorded one: unmanaged (no reference inside), the catalogue's id, saved and recorded by default.</summary>
        [Fact]
        public void TheComponent_IsUnmanaged_AndOnItsCatalogueId()
        {
            Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<Ambience>());
            Assert.Equal(GlobalComponentIds.Ambience, ComponentType<Ambience>.ID);
            Assert.Equal(DataPolicy.Default, System.Reflection.CustomAttributeExtensions.GetCustomAttribute<DataPolicyAttribute>(typeof(Ambience))!.Policy);
        }

        /// <summary>A layer ends AT its sim time and not a frame before; the layer with no end (0) and a layer ending later stay. 🔴 Mutation: expire at <c>now &gt;= Until - 1</c> ⇒ the 59.9 s frame removes it ⇒ red.</summary>
        [Fact]
        public void ALayer_EndsAtItsSimTime_NeverBefore_AndUntilZeroNeverEnds()
        {
            var timed = Layer("alarm.airraid", until: 60);
            var forever = Layer("battle.distant", until: 0);
            var later = Layer("alarm.bell", until: 90);

            Frame(59.9);
            Assert.True(_w.IsAlive(timed));
            Frame(60.0);
            Assert.False(_w.IsAlive(timed));
            Assert.True(_w.IsAlive(forever));
            Assert.True(_w.IsAlive(later));
            Assert.Equal(1, _sys.Expired);

            Frame(100000.0);
            Assert.True(_w.IsAlive(forever));                  // 0 = until removed
            Assert.False(_w.IsAlive(later));
        }

        /// <summary>The system reads the SIM clock only: with the clock standing still no wall time ends a layer, and a layer ends the moment the clock — not the wall — passes its time.</summary>
        [Fact]
        public void TheLayer_FollowsTheSimClock_NotTheWallClock()
        {
            var layer = Layer("alarm.bell", until: 5);
            var wall = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 3; i++) Frame(0.0);            // the sim clock stood still across several frames
            Assert.True(wall.ElapsedTicks > 0 && _w.IsAlive(layer));
            Frame(5.0);
            Assert.False(_w.IsAlive(layer));
        }

        /// <summary>A POSITIONED layer ends the same way (its position is irrelevant to its life).</summary>
        [Fact]
        public void APositionedLayer_EndsToo()
        {
            var siren = Layer("alarm.airraid", until: 10, at: new Vector3(100, 200, 0));
            Frame(10.0);
            Assert.False(_w.IsAlive(siren));
        }

        /// <summary>A layer this node does not own is not its to end — its owner's destroy reaches it. 🔴 Mutation: the authority test removed ⇒ the replica is destroyed here ⇒ red.</summary>
        [Fact]
        public void ALayerThisNodeDoesNotOwn_IsNotEnded()
        {
            _w.RegisterComponent<NetworkAuthority>();
            var replica = Layer("alarm.airraid", until: 10);
            _w.AddComponent(replica, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));
            Frame(20.0);
            Assert.True(_w.IsAlive(replica));
            Assert.Equal(0, _sys.Expired);
        }

        /// <summary>A REPLICATED layer ends through the one teardown command, published ONCE (the layer is marked TearDown so the next frame does not ask again) — a direct destroy would leave every replica sounding.</summary>
        [Fact]
        public void AReplicatedLayer_EndsThroughTheTeardownCommand_Once()
        {
            _w.RegisterComponent<NetworkIdentity>();
            _w.RegisterManagedEvent<DestroyEntityCommand>();
            var layer = Layer("alarm.airraid", until: 10);
            _w.AddComponent(layer, new NetworkIdentity { Value = 4242 });

            Frame(10.0);
            var asked = _w.Bus.ReadManaged<DestroyEntityCommand>().ToArray();
            Assert.True(_w.IsAlive(layer));                    // the teardown removes it, not this system
            Assert.Equal(4242, Assert.Single(asked).NetworkId);
            Assert.Equal(EntityLifecycle.TearDown, _w.GetLifecycleState(layer));

            Frame(10.5);
            Assert.Empty(_w.Bus.ReadManaged<DestroyEntityCommand>());   // asked once
        }

        /// <summary>⭐ HOT PATH (CLAUDE.md): a steady frame — layers alive, none due — allocates 0 bytes. 🔴 Mutation: a <c>new List&lt;int&gt;()</c> in <c>Execute</c> ⇒ bytes &gt; 0 ⇒ red.</summary>
        [Fact]
        public void ASteadyFrame_Allocates_ZeroBytes()
        {
            for (int i = 0; i < 6; i++) Layer(i % 2 == 0 ? "battle.distant" : "alarm.bell", until: i % 3 == 0 ? 0 : 1e9, at: i < 3 ? null : new Vector3(i, i, 0));
            for (int i = 0; i < 50; i++) Frame(1.0 + i);        // warm: JIT, the query's first build
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) _sys.Execute(_w, 0.016f);
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0L, bytes);
        }
    }
}
