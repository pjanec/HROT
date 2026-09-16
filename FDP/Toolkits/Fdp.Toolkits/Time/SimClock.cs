using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Time
{
    /// <summary>
    /// Entry point for reading simulation time: <c>SimClock.Of(view).IsAdvancing</c>.
    ///
    /// <para>Takes the world (or a view of it) rather than a time controller, deliberately. A
    /// controller's <c>GetCurrentState()</c> builds a state with a ZERO delta, so a caller that asked
    /// the controller would be told "halted" on every frame including the running ones. The delta is
    /// only meaningful on the instance the kernel pushed into the world this frame, which is what
    /// this reads.</para>
    /// </summary>
    public static class SimClock
    {
        /// <summary>
        /// Reads the clock of the world behind <paramref name="view"/>.
        ///
        /// <para>The cast to <see cref="EntityRepository"/> is the engine's own convention for
        /// escalating from a read-only view — <c>CarKinematicsSystem</c>,
        /// <c>InteractionDispatcherSystem</c> and <c>MissionAdapterSystem</c> all do exactly this.
        /// The alternative was widening <see cref="ISimulationView"/>, which carries no singleton
        /// accessor at all and has over a thousand references.</para>
        ///
        /// <para>A view with no repository behind it reports halted rather than throwing: a clock
        /// nobody is driving is not advancing, and that is the honest answer.</para>
        /// </summary>
        public static ISimClock Of(ISimulationView? view) => new WorldSimClock(view as EntityRepository);

        /// <summary>Reads the clock of <paramref name="world"/> directly.</summary>
        public static ISimClock Of(EntityRepository? world) => new WorldSimClock(world);
    }
}
