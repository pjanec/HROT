using Fdp.Kernel;
using FDP.Toolkit.Combat.Systems;
using FDP.Toolkit.Replication.Services;

namespace FDP.Toolkit.Combat.Modules
{
    /// <summary>
    /// Registers the systems required for damage assessment on the authority node.
    ///
    /// <para><b>Systems registered (in execution order):</b></para>
    /// <list type="number">
    ///   <item>
    ///     <b>Simulation phase</b> — <see cref="DamageCalculationSystem"/>: consumes
    ///     <c>DetonationNotification</c> events, computes flat HP loss (POC), and publishes
    ///     <c>DamageAssessedEvent</c> for the egress translator.
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// The companion translator pair (<c>MunitionDetonationIngressTranslator</c> and
    /// <c>DamageAssessedEgressTranslator</c>) live in <c>Bagira.SimHost/Network</c> and are
    /// registered separately in <c>SimHostApp</c> / <c>NodeBootstrapper</c>.
    /// </para>
    /// </summary>
    public sealed class DamageAssessmentModule
    {
        /// <summary>
        /// Registers damage-assessment systems into the provided system groups.
        /// </summary>
        /// <param name="simGroup">Simulation-phase group — receives <see cref="DamageCalculationSystem"/>.</param>
        /// <param name="entityMap">
        /// Shared <see cref="NetworkEntityMap"/> injected into <see cref="DamageCalculationSystem"/>
        /// for resolving network entity IDs to local ECS handles.
        /// </param>
        public void RegisterSystems(SystemGroup simGroup, NetworkEntityMap entityMap)
        {
            simGroup.AddSystem(new DamageCalculationSystem(entityMap));
        }
    }
}
