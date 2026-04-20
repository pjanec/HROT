using Fdp.Core;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;

namespace Fdp.Toolkit.Perception.Tests
{
    /// <summary>
    /// Creates a fully-registered <see cref="EntityRepository"/> for perception unit tests.
    /// Registers all components and events consumed by the Perception toolkit systems.
    /// </summary>
    public static class PerceptionTestWorldFactory
    {
        public static EntityRepository Create()
        {
            var world = new EntityRepository();

            // Core kernel components used by all perception systems.
            world.RegisterComponent<SimTransform>();
            world.RegisterComponent<SimVelocity>();

            // Perception-specific components.
            world.RegisterComponent<EntityInfo>();
            world.RegisterComponent<PerceptionReceptor>();
            world.RegisterComponent<TargetMemory>();
            world.RegisterComponent<SensorContactList>();
            world.RegisterComponent<ActiveSensorTracks>();

            // Events exchanged within the Perception pipeline.
            world.RegisterEvent<AudioStimulusEvent>();
            world.RegisterEvent<LosCheckRequestEvent>();
            world.RegisterEvent<TargetVisibleEvent>();
            world.RegisterEvent<TargetHeardEvent>();

            return world;
        }
    }
}
