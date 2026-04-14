using Fdp.Kernel;
using FDP.Toolkit.Behavior.Systems;

namespace FDP.Toolkit.Behavior.Modules
{
    /// <summary>
    /// Grouping for top-down command processing systems: doctrine assignment and
    /// multi-phase mission plan advancement.
    ///
    /// <para><b>Systems registered (in order):</b></para>
    /// <list type="number">
    ///   <item><see cref="DoctrineIngressSystem"/> — applies AssignDoctrineEvents to entities</item>
    ///   <item><see cref="MissionDirectorSystem"/> — advances MissionPlanQueue phases</item>
    /// </list>
    ///
    /// <para>Belongs to the <em>Brain</em> tier of the Brain/Muscle decomposition (MOD1 §3.2.2).</para>
    /// </summary>
    public sealed class MissionControlModule
    {
        private readonly DoctrineRegistry _registry;

        public MissionControlModule(DoctrineRegistry registry)
        {
            _registry = registry;
        }

        /// <summary>
        /// Registers the doctrine ingress and mission director systems into the provided group.
        /// </summary>
        public void RegisterSystems(SystemGroup group)
        {
            group.AddSystem(new DoctrineIngressSystem(_registry));
            group.AddSystem(new MissionDirectorSystem());
        }
    }
}
