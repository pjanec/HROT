using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Blueprints.Partitioning;

namespace Fdp.Toolkit.Behavior.Tests
{
    public static class TestWorldFactory
    {
        public static EntityRepository Create()
        {
            var world = new EntityRepository();
            world.RegisterComponent<BehaviorState>();
            world.RegisterComponent<LocomotionChannel>();
            world.RegisterComponent<WeaponChannel>();
            world.RegisterComponent<InteractionChannel>();
            world.RegisterComponent<ActorCapabilityState>();
            world.RegisterComponent<PreviousCapabilities>();
            world.RegisterComponent<BrainInterrupts>();   // O2 — the entity-fact tail
            world.RegisterComponent<SimTier>();
            world.RegisterComponent<BrainHsm128>();
            world.RegisterComponent<PassengerBuffer>();
            world.RegisterComponent<IsEmbarkedTag>();
            world.RegisterComponent<SimTransform>();
            world.RegisterComponent<Health>();

            // ⭐⭐⭐ O7c-② / CE-319 — THE TIER COMPONENTS ARE NOW A HARD DEPENDENCY OF BTREE EXECUTION.
            //   📐 Before this change a world could tick a BTree with NO occurrence store at all: the
            //   cursor was in BrainBTreeState, and registering that one component was enough. The cursor
            //   is now a SLOT, so an entity with no store has nowhere to keep it — and because discovery
            //   is the tier walk, such an entity is simply NOT ENUMERATED.
            //
            //   🔴🔴 THAT FAILS SILENTLY, AND IT IS THE CE-315 SHAPE AGAIN: a missing registration does
            //   not throw, it just means the BTree never ticks. ⚠ Eight tests in this project went red
            //   with "Expected 1, Actual 0" for exactly that reason — which is the cheap version of the
            //   lesson; the expensive version would have been a host shipping a brain that never runs.
            //
            //   ⭐ Production already satisfies this (HrotSharedComponentRegistry, CE-161); this factory
            //   simply has to mirror it. 📄 DESIGN_Occurrence_Scoped_Storage.md §31.
            BlueprintTierTable.RegisterAll(world);

            return world;
        }
    }
}
