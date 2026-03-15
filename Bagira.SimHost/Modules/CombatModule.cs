using Bagira.SimHost.Systems;
using Fdp.Kernel;
using FDP.Toolkit.Behavior.Systems;
using FDP.Toolkit.Combat.Systems;
using FDP.Toolkit.Physics.Systems;

namespace Bagira.SimHost.Modules
{
    /// <summary>
    /// Grouping for combat, perception, and physics systems that are present on all node roles.
    ///
    /// <para><b>Systems registered (in execution order):</b></para>
    /// <list type="number">
    ///   <item><b>Input phase</b> — <see cref="FireProcessingSystem"/>, <see cref="RaycastSolverSystem"/>, <see cref="HitResolutionSystem"/></item>
    ///   <item><b>Simulation phase</b> — <see cref="PerceptionBroadphaseSystem"/>, <see cref="LosRequestBatchingSystem"/>,
    ///     <see cref="ThreatEvaluationAdapterSystem"/>, <see cref="DamageSystem"/>, <see cref="HsmDamageBridgeSystem"/></item>
    ///   <item><b>Post-sim phase</b> — <see cref="BallisticsSystem"/></item>
    /// </list>
    ///
    /// <para>
    /// Currently lives in <c>Bagira.SimHost</c> (rather than an FDP toolkit) because
    /// <see cref="PerceptionBroadphaseSystem"/> and <see cref="ThreatEvaluationAdapterSystem"/>
    /// carry Bagira-domain dependencies.
    /// </para>
    /// </summary>
    public sealed class CombatModule
    {
        /// <summary>
        /// Registers combat, perception, and physics systems into the provided groups.
        /// </summary>
        /// <param name="inputGroup">Input-phase group — receives fire processing and raycast resolution.</param>
        /// <param name="simGroup">Simulation-phase group — receives perception broadphase, damage, and HSM bridge.</param>
        /// <param name="postSimGroup">Post-simulation group — receives ballistics integration.</param>
        public void RegisterSystems(SystemGroup inputGroup, SystemGroup simGroup, SystemGroup postSimGroup)
        {
            // ── Input phase ───────────────────────────────────────────────────
            inputGroup.AddSystem(new FireProcessingSystem());
            inputGroup.AddSystem(new RaycastSolverSystem());
            inputGroup.AddSystem(new HitResolutionSystem());

            // ── Simulation phase ──────────────────────────────────────────────
            simGroup.AddSystem(new PerceptionBroadphaseSystem());
            // LosRequestBatchingSystem removed: it is now IModuleSystem-only and
            // runs exclusively inside AutonomousPerceptionModule on the background thread.
            simGroup.AddSystem(new ThreatEvaluationAdapterSystem());
            simGroup.AddSystem(new DamageSystem());
            simGroup.AddSystem(new HsmDamageBridgeSystem());

            // ── Post-simulation ───────────────────────────────────────────────
            postSimGroup.AddSystem(new BallisticsSystem());
        }
    }
}
