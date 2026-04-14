using Fdp.Kernel;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;

namespace Fdp.Toolkit.Behavior.Tests
{
    public static class TestWorldFactory
    {
        public static EntityRepository Create()
        {
            var world = new EntityRepository();
            world.RegisterComponent<DoctrineState>();
            world.RegisterComponent<LocomotionChannel>();
            world.RegisterComponent<WeaponChannel>();
            world.RegisterComponent<InteractionChannel>();
            world.RegisterComponent<ActorCapabilityState>();
            world.RegisterComponent<PreviousCapabilities>();
            world.RegisterComponent<BrainBlackboard>();
            world.RegisterComponent<SimTier>();
            world.RegisterComponent<BrainBTreeState>();
            world.RegisterComponent<BrainHsm64>();
            world.RegisterComponent<BrainHsm128>();
            world.RegisterComponent<PassengerBuffer>();
            world.RegisterComponent<IsEmbarkedTag>();
            world.RegisterComponent<SimTransform>();
            world.RegisterComponent<Health>();
            return world;
        }
    }
}
