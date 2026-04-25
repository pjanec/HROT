using System.Collections.Generic;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Systems;

namespace Fdp.Toolkit.Combat.Modules
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
    /// <c>DamageAssessedEgressTranslator</c>) live in <c>Hrot.SimHost/Network</c> and are
    /// registered separately in <c>SimHostApp</c> / <c>NodeBootstrapper</c>.
    /// </para>
    /// </summary>
    public sealed class DamageAssessmentModule
    {
        /// <summary>Systems that run in the Simulation phase.</summary>
        public IReadOnlyList<IEcsModuleSystem> SimulationSystems { get; } =
            new IEcsModuleSystem[] { new DamageCalculationSystem() };
    }
}
