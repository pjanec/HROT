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
            return world;
        }
    }
}
