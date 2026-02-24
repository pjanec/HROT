using Fdp.Kernel;
using FDP.Toolkit.Behavior.Components;

namespace FDP.Toolkit.Behavior.Tests
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
            world.RegisterComponent<BrainBlackboard>();
            world.RegisterComponent<SimTier>();
            world.RegisterComponent<BrainBTreeState>();
            world.RegisterComponent<BrainHsm64>();
            world.RegisterComponent<BrainHsm128>();
            return world;
        }
    }
}
