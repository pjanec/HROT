using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Replication.Components;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-3110</c> I-2b (R-224, Q-1) — the passenger side DERIVED from the vehicle's list on a node that did not decide the boarding:
    /// the list arrives (here: written directly, as the ingress does), <see cref="EmbarkedStateSystem"/> tags and strips; the list empties,
    /// it untags, gives the capabilities back and places the body beside the hull — but only where the passenger's POSITION is owned.
    /// 📄 docs/DESIGN_Entity_Interactions.md §7.1c.
    /// </summary>
    public sealed class EmbarkedStateSystemTests : IDisposable
    {
        private const int Here = 1, Elsewhere = 2;
        private readonly EntityRepository _world = new();
        private readonly EmbarkedStateSystem _system = new();

        public EmbarkedStateSystemTests()
        {
            _world.RegisterComponent<SimTransform>();
            _world.RegisterComponent<ActorCapabilityState>();
            _world.RegisterComponent<PassengerBuffer>();
            _world.RegisterComponent<IsEmbarkedTag>();
            _world.RegisterComponent<NetworkAuthority>();
            _world.RegisterManagedComponent<DescriptorOwnership>();
        }

        public void Dispose() => _world.Dispose();

        private Entity Vehicle(Vector3 at)
        {
            var v = _world.CreateEntity();
            _world.AddComponent(v, new SimTransform { Position = at, Rotation = Quaternion.Identity });   // facing east (+X)
            _world.AddComponent(v, new PassengerBuffer());
            return v;
        }

        /// <summary>A soldier whose position is owned by <paramref name="positionOwner"/> (this node is <see cref="Here"/>).</summary>
        private Entity Soldier(Vector3 at, int positionOwner = Here)
        {
            var s = _world.CreateEntity();
            _world.AddComponent(s, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _world.AddComponent(s, new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot });
            _world.AddComponent(s, new NetworkAuthority(primaryOwnerId: Elsewhere, localNodeId: Here));
            var ownership = new DescriptorOwnership();
            ownership.Map[Embarkation.PositionKey] = positionOwner;
            _world.SetManagedComponent(s, ownership);
            return s;
        }

        /// <summary>The list as the ingress writes it — no tag, no capabilities touched.</summary>
        private void ListArrives(Entity vehicle, params Entity[] passengers)
        {
            var b = new PassengerBuffer();
            foreach (var p in passengers) b.Passengers[b.Count++] = p;
            _world.SetComponent(vehicle, b);
        }

        private ActorCapabilities Caps(Entity e) => _world.GetComponent<ActorCapabilityState>(e).Capabilities;

        [Fact]
        public void CE3110_I2b_AListThatArrives_TagsAndStripsThePassenger()
        {
            var apc = Vehicle(new Vector3(100, 50, 0));
            var a = Soldier(new Vector3(90, 50, 0));
            var b = Soldier(new Vector3(91, 50, 0));

            ListArrives(apc, a, b);
            _system.Execute(_world, 0f);

            foreach (var s in new[] { a, b })
            {
                Assert.Equal(apc, _world.GetComponentRO<IsEmbarkedTag>(s).VehicleEntity);
                Assert.Equal(ActorCapabilities.None, Caps(s) & Embarkation.StrippedAboard);
            }
            Assert.Equal(2, _system.Boarded);

            _system.Execute(_world, 0f);   // nothing new: idempotent
            Assert.Equal(2, _system.Boarded);
        }

        [Fact]
        public void CE3110_I2b_AnEmptiedList_Untags_GivesCapsBack_AndPlacesBesideTheHull_OnlyWhereThePositionIsOwned()
        {
            var apc = Vehicle(new Vector3(100, 50, 0));
            var ours = Soldier(new Vector3(90, 50, 0), positionOwner: Here);
            var theirs = Soldier(new Vector3(91, 50, 0), positionOwner: Elsewhere);
            ListArrives(apc, ours, theirs);
            _system.Execute(_world, 0f);

            ListArrives(apc);
            _system.Execute(_world, 0f);

            foreach (var s in new[] { ours, theirs })
            {
                Assert.False(_world.HasComponent<IsEmbarkedTag>(s));
                Assert.Equal(Embarkation.StrippedAboard, Caps(s) & Embarkation.StrippedAboard);
            }
            // the column on the right (south of an east-facing hull): beside it, not where it boarded
            var placed = _world.GetComponentRO<SimTransform>(ours).Position;
            Assert.Equal(Embarkation.DismountPoint(_world, apc, 0, 2), placed);
            Assert.True(placed.Y < 50f);
            // the other node owns that body's position — a write here would be overwritten next frame, so none is made
            Assert.Equal(new Vector3(91, 50, 0), _world.GetComponentRO<SimTransform>(theirs).Position);
            Assert.Equal(2, _system.Left);
        }

        [Fact]
        public void CE3110_I2b_TheDecidingNode_IsANoOp_ItsTagAndListAgreeAlready()
        {
            var apc = Vehicle(new Vector3(100, 50, 0));
            var a = Soldier(new Vector3(90, 50, 0));
            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(_world, a, apc));

            _system.Execute(_world, 0f);
            Assert.Equal(0, _system.Boarded);
            Assert.Equal(0, _system.Left);
            Assert.Equal(apc, _world.GetComponentRO<IsEmbarkedTag>(a).VehicleEntity);
        }

        [Fact]
        public void CE3110_I2b_AMoveToAnotherVehicle_RetagsWithoutLeaving()
        {
            var first = Vehicle(new Vector3(100, 50, 0));
            var second = Vehicle(new Vector3(200, 50, 0));
            var a = Soldier(new Vector3(90, 50, 0));
            ListArrives(first, a);
            _system.Execute(_world, 0f);

            ListArrives(first);
            ListArrives(second, a);
            _system.Execute(_world, 0f);

            Assert.Equal(second, _world.GetComponentRO<IsEmbarkedTag>(a).VehicleEntity);
            Assert.Equal(ActorCapabilities.None, Caps(a) & Embarkation.StrippedAboard);
            Assert.Equal(0, _system.Left);
        }

        [Fact]
        public void CE3110_I2b_ADeadPassenger_LeavesWithoutItsCapabilitiesBack()
        {
            _world.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
            var apc = Vehicle(new Vector3(100, 50, 0));
            var a = Soldier(new Vector3(90, 50, 0));
            _world.AddComponent(a, new Fdp.Toolkit.Combat.Components.Health { Current = 0f, Max = 100f });
            ListArrives(apc, a);
            _system.Execute(_world, 0f);

            ListArrives(apc);
            _system.Execute(_world, 0f);

            Assert.False(_world.HasComponent<IsEmbarkedTag>(a));
            Assert.Equal(ActorCapabilities.None, Caps(a) & Embarkation.StrippedAboard);
        }
    }
}
