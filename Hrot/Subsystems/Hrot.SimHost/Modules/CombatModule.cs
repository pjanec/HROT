using Hrot.SimHost.Systems;
using Fdp.Core;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.Physics.Systems;
using Fdp.Toolkit.Replication.Services;

namespace Hrot.SimHost.Modules
{
    /// <summary>
    /// Grouping for combat, perception, and physics systems that are present on all node roles.
    ///
    /// <para><b>Systems registered (in execution order):</b></para>
    /// <list type="number">
    ///   <item><b>Input phase</b> — <see cref="FireProcessingSystem"/>, <see cref="RaycastSolverSystem"/>, <see cref="HitResolutionSystem"/></item>
    ///   <item><b>Simulation phase</b> — <see cref="PerceptionBroadphaseSystem"/>, <see cref="LosRequestBatchingSystem"/>,
    ///     <see cref="ThreatEvaluationAdapterSystem"/>, <see cref="DamageSystem"/></item>
    ///   <item><b>Post-sim phase</b> — <see cref="BallisticsSystem"/></item>
    /// </list>
    ///
    /// <para>
    /// <see cref="Fdp.Toolkit.Behavior.Systems.HsmDamageBridgeSystem"/> was relocated
    /// to <see cref="Fdp.Toolkit.Behavior.Modules.CognitiveRuntimeModule"/> (PACK-M001)
    /// so it runs on the Brain tier where <see cref="Fdp.Toolkit.Behavior.Components.BrainHsm128"/>
    /// components reside.
    /// </para>
    ///
    /// <para>
    /// Currently lives in <c>Hrot.SimHost</c> (rather than an FDP toolkit) because
    /// <see cref="PerceptionBroadphaseSystem"/> and <see cref="ThreatEvaluationAdapterSystem"/>
    /// carry Hrot-domain dependencies.
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
        /// <param name="entityMap">
        /// Shared <see cref="NetworkEntityMap"/> injected into <see cref="FireProcessingSystem"/>
        /// for resolving <c>WeaponFireIntent</c> network IDs to local ECS entities (BS1-T007).
        /// </param>
        public void RegisterSystems(
            SystemGroup inputGroup,
            SystemGroup simGroup,
            SystemGroup postSimGroup,
            NetworkEntityMap entityMap)
        {
            // ── Input phase ───────────────────────────────────────────────────
            inputGroup.AddSystem(new FireProcessingSystem());
            inputGroup.AddSystem(new RaycastSolverSystem());
            inputGroup.AddSystem(new HitResolutionSystem());

            // ── Simulation phase ──────────────────────────────────────────────
            simGroup.AddSystem(new PerceptionBroadphaseSystem());
            // LosRequestBatchingSystem removed: it is now IEcsModuleSystem-only and
            // runs exclusively inside AutonomousPerceptionModule on the background thread.
            simGroup.AddSystem(new ThreatEvaluationAdapterSystem());
            simGroup.AddSystem(new DamageSystem());
            // HsmDamageBridgeSystem relocated to CognitiveRuntimeModule (PACK-M001).

            // ── Post-simulation ───────────────────────────────────────────────
            postSimGroup.AddSystem(new BallisticsSystem());
        }
    }
}
