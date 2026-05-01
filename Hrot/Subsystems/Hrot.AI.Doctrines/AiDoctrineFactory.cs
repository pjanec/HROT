using System;
using System.Runtime.InteropServices;
using Fbt.Runtime;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Services;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using Hrot.AI.Doctrines.Brains;
using Hrot.AI.Doctrines.Generated;

namespace Hrot.AI.Doctrines
{
    /// <summary>
    /// Single source of truth for CGF doctrine registrations.
    ///
    /// <para>
    /// Called at startup via reflection (from <c>CgfDoctrineSetup.LoadFromAiAssembly</c>
    /// or <c>FbtAssemblyHotReloader</c>'s callback in <c>EditorSubsystem</c>).
    /// All expensive BTree compilation and action-registry wiring occurs inside
    /// <see cref="BuildRegistrationAction"/> on whatever thread the caller chooses
    /// (typically a background thread); the returned delegate performs only lightweight
    /// <c>DoctrineRegistry.Register</c> calls and is safe to invoke on the main thread.
    /// </para>
    /// </summary>
    public static class AiDoctrineFactory
    {
        // Doctrine integer IDs.  Mirror of CgfDoctrineIds in Hrot.CGF.
        // Values are stable and must never change once published.
        private const int MoveTo_BT         = 3001;
        private const int FollowRoute_BT    = 3002;
        private const int JoinFormation_BT  = 3003;
        private const int Idle_HSM          = 3010;
        private const int WanderMilitary_BT = 3011;
        private const int FireAtTarget_BT   = 3012;

        /// <summary>
        /// Compiles all BTree interpreters and wires all action delegates for this
        /// assembly version, then returns a lightweight <see cref="Action{T}"/> that
        /// applies the resulting <see cref="DoctrineDefinition"/>s into the caller's
        /// <see cref="DoctrineRegistry"/> on the main thread.
        ///
        /// <para>
        /// Designed to be called on a background thread (inside the ALC hot-reload worker)
        /// so the CPU-intensive BTree compilation does not stall the 60 Hz UI loop.
        /// The returned action is then staged and invoked from the main thread via
        /// <c>FbtAssemblyHotReloader.DrainPendingCallbacks()</c>.
        /// </para>
        /// </summary>
        /// <param name="geoTransform">
        /// Geographic coordinate transform used by MoveToLocation. May be <c>null</c>
        /// in offline / Cartesian-only contexts.
        /// </param>
        /// <param name="entityMap">Network-entity map used by FireAtTarget.</param>
        public static unsafe Action<DoctrineRegistry> BuildRegistrationAction(
            IGeographicTransform? geoTransform,
            NetworkEntityMap entityMap)
        {
            // Action delegates for all BTree nodes in this assembly version.
            var actionRegistry = new ActionRegistry<BrainBlackboard, BTreeContext>();
            FbtActionRegistrar.RegisterAll(actionRegistry);

            // Pre-compile BTree blobs on the calling thread (CPU-bound work).
            var moveToBlob        = FbtTreeCatalog.GetMoveToLocation();
            var followRouteBlob   = FbtTreeCatalog.GetFollowRoute();
            var joinFormationBlob = FbtTreeCatalog.GetJoinFormation();
            var wanderBlob        = FbtTreeCatalog.GetWanderMilitary();
            var fireAtTargetBlob  = FbtTreeCatalog.GetFireAtTarget();

            // Pre-compile HSM blob for Idle_HSM: a single "Idle" state with no transitions.
            var idleHsmBuilder = new HsmBuilder("Idle_HSM");
            idleHsmBuilder.State("Idle").Initial();
            var idleGraph    = idleHsmBuilder.Build();
            HsmNormalizer.Normalize(idleGraph);
            var idleFlat     = HsmFlattener.Flatten(idleGraph);
            HsmDefinitionBlob idleHsmBlob = HsmEmitter.Emit(idleFlat);

            return (DoctrineRegistry registry) =>
            {
                // unsafe lambdas assigned to ParseParamsDelegate require this block
                registry.Register(MoveTo_BT, "MoveToLocation",
                    new DoctrineDefinition
                    {
                        Name             = "MoveToLocation",
                        BrainTier        = BehaviorConstants.BrainTierBTree,
                        ParseParams      = (json, ptr) => CgfNodes.ParseMoveToParams(json, ptr, geoTransform),
                        ParamsDtoType    = typeof(CgfNodes.MoveToLocationParams),
                        BTreeInterpreter = new Interpreter<BrainBlackboard, BTreeContext>(
                            moveToBlob, actionRegistry),
                    });

                registry.Register(FollowRoute_BT, "FollowRoute",
                    new DoctrineDefinition
                    {
                        Name             = "FollowRoute",
                        BrainTier        = BehaviorConstants.BrainTierBTree,
                        ParseParams      = (json, ptr) => CgfNodes.ParseFollowRouteParams(json, ptr),
                        ParamsDtoType    = typeof(CgfNodes.FollowRouteParams),
                        BTreeInterpreter = new Interpreter<BrainBlackboard, BTreeContext>(
                            followRouteBlob, actionRegistry),
                    });

                registry.Register(JoinFormation_BT, "JoinFormation",
                    new DoctrineDefinition
                    {
                        Name             = "JoinFormation",
                        BrainTier        = BehaviorConstants.BrainTierBTree,
                        ParamsDtoType    = typeof(CgfNodes.JoinFormationParams),
                        BTreeInterpreter = new Interpreter<BrainBlackboard, BTreeContext>(
                            joinFormationBlob, actionRegistry),
                    });

                registry.Register(Idle_HSM, "Idle",
                    new DoctrineDefinition
                    {
                        Name          = "Idle",
                        BrainTier     = BehaviorConstants.BrainTierHsm,
                        HsmDefinition = idleHsmBlob,
                    });

                registry.Register(WanderMilitary_BT, "WanderMilitary",
                    new DoctrineDefinition
                    {
                        Name             = "WanderMilitary",
                        BrainTier        = BehaviorConstants.BrainTierBTree,
                        BTreeInterpreter = new Interpreter<BrainBlackboard, BTreeContext>(
                            wanderBlob, actionRegistry),
                    });

                registry.Register(FireAtTarget_BT, "FireAtTarget",
                    new DoctrineDefinition
                    {
                        Name             = "FireAtTarget",
                        BrainTier        = BehaviorConstants.BrainTierBTree,
                        ParseParams      = (json, ptr) => CgfNodes.ParseFireAtTargetParams(json, ptr, entityMap),
                        ParamsDtoType    = typeof(CgfNodes.FireAtTargetParams),
                        BTreeInterpreter = new Interpreter<BrainBlackboard, BTreeContext>(
                            fireAtTargetBlob, actionRegistry),
                    });
            };
        }
    }
}
