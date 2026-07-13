using System;
using System.Runtime.InteropServices;
using Fbt.Runtime;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Attributes;
using Fdp.Toolkit.Replication.Services;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using Hrot.AI.Behaviors.Brains;
using Hrot.AI.Behaviors.Generated;
using Hrot.AI.Behaviors.Trees;

namespace Hrot.AI.Behaviors
{
    /// <summary>
    /// Single source of truth for CGF behavior registrations.
    ///
    /// <para>
    /// Called at startup via reflection (from <c>CgfBehaviorSetup.LoadFromAiAssembly</c>
    /// or <c>FbtAssemblyHotReloader</c>'s callback in <c>EditorSubsystem</c>).
    /// All expensive BTree compilation and action-registry wiring occurs inside
    /// <see cref="BuildRegistrationAction"/> on whatever thread the caller chooses
    /// (typically a background thread); the returned delegate performs only lightweight
    /// <c>BehaviorRegistry.Register</c> calls and is safe to invoke on the main thread.
    /// </para>
    /// </summary>
    [BlueprintRegistrar]
    public static class AiBehaviorFactory
    {
        // Behavior integer IDs.  Mirror of CgfBehaviorIds in Hrot.CGF.
        // Values are stable and must never change once published.
        private const int MoveTo_BT         = 3001;
        private const int FollowRoute_BT    = 3002;
        private const int JoinFormation_BT  = 3003;
        private const int Idle_HSM          = 3010;
        private const int WanderMilitary_BT = 3011;
        private const int FireAtTarget_BT       = 3012;
        private const int HullDownAttackRun_BT   = 3013;
        private const int PlatoonHillAttack_BT   = 3014;

        /// <summary>
        /// Entry point used by <c>AiHotReloadCoordinator</c> attribute-driven discovery.
        /// Compiles all BTree/HSM interpreters and registers the resulting
        /// <see cref="BehaviorDefinition"/>s directly into <paramref name="registry"/>.
        /// </summary>
        /// <param name="registry">Target behavior registry (staging copy on hot-reload path).</param>
        /// <param name="geoTransform">Geographic transform; may be <c>null</c> in Cartesian contexts.</param>
        /// <param name="entityMap">Network-entity map; may be <c>null</c> in offline contexts.</param>
        public static unsafe void RegisterAll(
            BehaviorRegistry registry,
            IGeographicTransform? geoTransform,
            NetworkEntityMap? entityMap)
        {
            // Delegate to the existing two-phase implementation to avoid code duplication.
            BuildRegistrationAction(geoTransform, entityMap!)(registry);
        }

        /// <summary>
        /// Compiles all BTree interpreters and wires all action delegates for this
        /// assembly version, then returns a lightweight <see cref="Action{T}"/> that
        /// applies the resulting <see cref="BehaviorDefinition"/>s into the caller's
        /// <see cref="BehaviorRegistry"/> on the main thread.
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
        public static unsafe Action<BehaviorRegistry> BuildRegistrationAction(
            IGeographicTransform? geoTransform,
            NetworkEntityMap entityMap)
        {
            // Action delegates for all BTree nodes in this assembly version.
            var actionRegistry = new ActionRegistry<BrainBlackboard, BTreeContext>();
            FbtActionRegistrar.RegisterAll(actionRegistry);
            Func<string, bool> isResourceOwning = name => actionRegistry.TryGetDeactivator(name, out _);

            // Pre-compile BTree blobs on the calling thread (CPU-bound work).
            var moveToBlob        = FbtTreeCatalog.GetMoveToLocation(isResourceOwning);
            var followRouteBlob   = FbtTreeCatalog.GetFollowRoute(isResourceOwning);
            var joinFormationBlob = FbtTreeCatalog.GetJoinFormation(isResourceOwning);
            var wanderBlob        = FbtTreeCatalog.GetWanderMilitary(isResourceOwning);
            var fireAtTargetBlob  = FbtTreeCatalog.GetFireAtTarget(isResourceOwning);
            // HAJSON-A: Use JSON-generated blobs for Hill Attack trees.
            // Deactivator wiring (isResourceOwning) is HAJSON-B; not yet wired here.
            var hullDownBlob      = HullDownAttackRun.Build();
            // S3-G: PlatoonHillAttack is now stateful (Behavior-scoped shared working state). Its
            // interpreter + baked stateful thunks + working-slot manifest are produced by the generated
            // PlatoonHillAttackRegistrar (invoked in the registration below), not a hand-built def here.

            // Pre-compile HSM blob for Idle_HSM: a single "Idle" state with no transitions.
            var idleHsmBuilder = new HsmBuilder("Idle_HSM");
            idleHsmBuilder.State("Idle").Initial();
            var idleGraph    = idleHsmBuilder.Build();
            HsmNormalizer.Normalize(idleGraph);
            var idleFlat     = HsmFlattener.Flatten(idleGraph);
            HsmDefinitionBlob idleHsmBlob = HsmEmitter.Emit(idleFlat);
            // Sidecar metadata so AI diagnostic renderers/JSON dumps can symbolicate
            // raw trace records back to readable state/event/action names.
            MachineMetadata idleHsmMetadata = HsmEmitter.BuildMachineMetadata(idleGraph);

            return (BehaviorRegistry registry) =>
            {
                // unsafe lambdas assigned to ParseParamsDelegate require this block
                registry.Register(MoveTo_BT, "MoveToLocation",
                    new BehaviorDefinition
                    {
                        Name             = "MoveToLocation",
                        BrainTier        = BehaviorConstants.BrainTierBTree,
                        ParseParams      = (json, ptr) => CgfNodes.ParseMoveToParams(json, ptr, geoTransform!),
                        ParamsDtoType    = typeof(CgfNodes.MoveToLocationParams),
                        BTreeInterpreter = new Interpreter<BrainBlackboard, BTreeContext>(
                            moveToBlob, actionRegistry),
                    });

                registry.Register(FollowRoute_BT, "FollowRoute",
                    new BehaviorDefinition
                    {
                        Name             = "FollowRoute",
                        BrainTier        = BehaviorConstants.BrainTierBTree,
                        ParseParams      = (json, ptr) => CgfNodes.ParseFollowRouteParams(json, ptr),
                        ParamsDtoType    = typeof(CgfNodes.FollowRouteParams),
                        BTreeInterpreter = new Interpreter<BrainBlackboard, BTreeContext>(
                            followRouteBlob, actionRegistry),
                    });

                registry.Register(JoinFormation_BT, "JoinFormation",
                    new BehaviorDefinition
                    {
                        Name             = "JoinFormation",
                        BrainTier        = BehaviorConstants.BrainTierBTree,
                        ParamsDtoType    = typeof(CgfNodes.JoinFormationParams),
                        BTreeInterpreter = new Interpreter<BrainBlackboard, BTreeContext>(
                            joinFormationBlob, actionRegistry),
                    });

                registry.Register(Idle_HSM, "Idle",
                    new BehaviorDefinition
                    {
                        Name          = "Idle",
                        BrainTier     = BehaviorConstants.BrainTierHsm,
                        HsmDefinition = idleHsmBlob,
                        HsmMetadata   = idleHsmMetadata,
                    });

                registry.Register(WanderMilitary_BT, "WanderMilitary",
                    new BehaviorDefinition
                    {
                        Name             = "WanderMilitary",
                        BrainTier        = BehaviorConstants.BrainTierBTree,
                        BTreeInterpreter = new Interpreter<BrainBlackboard, BTreeContext>(
                            wanderBlob, actionRegistry),
                    });

                registry.Register(FireAtTarget_BT, "FireAtTarget",
                    new BehaviorDefinition
                    {
                        Name             = "FireAtTarget",
                        BrainTier        = BehaviorConstants.BrainTierBTree,
                        ParseParams      = (json, ptr) => CgfNodes.ParseFireAtTargetParams(json, ptr, entityMap),
                        ParamsDtoType    = typeof(CgfNodes.FireAtTargetParams),
                        BTreeInterpreter = new Interpreter<BrainBlackboard, BTreeContext>(
                            fireAtTargetBlob, actionRegistry),
                    });

                registry.Register(HullDownAttackRun_BT, "HullDownAttackRun",
                    new BehaviorDefinition
                    {
                        Name             = "HullDownAttackRun",
                        BrainTier        = BehaviorConstants.BrainTierBTree,
                        ParseParams      = (json, ptr) => HillAttackTankNodes.ParseHullDownAttackParams(json, ptr),
                        ParamsDtoType    = typeof(HullDownAttackParams),
                        BTreeInterpreter = new Interpreter<BrainBlackboard, BTreeContext>(
                            hullDownBlob, actionRegistry),
                    });

                // S3-G: PlatoonHillAttack runs on a Behavior-scoped shared working-state slot. Reuse the
                // generated PlatoonHillAttackRegistrar to register the six stateful thunks + the deactivator
                // into the shared actionRegistry and to build the interpreter + StatefulWorkingSlots manifest.
                // The registrar registers its def under its own asset-derived id into a THROWAWAY registry;
                // we then register the real def under the stable CGF id (PlatoonHillAttack_BT = 3014) with the
                // geo-aware ParseParams the generated registrar cannot supply. The params DTO is unchanged;
                // only the working-state hack was removed (HeavyDtoType → StatefulWorkingSlots manifest).
                var platoonHillStaging = new BehaviorRegistry();
                PlatoonHillAttackRegistrar.Register(
                    platoonHillStaging, new BlueprintRegistry().BeginStaging(), actionRegistry);
                if (platoonHillStaging.TryGetId("PlatoonHillAttack", out int genPlatoonHillId) &&
                    platoonHillStaging.TryGetDefinition(genPlatoonHillId, out var genPlatoonHillDef))
                {
                    registry.Register(PlatoonHillAttack_BT, "PlatoonHillAttack", new BehaviorDefinition
                    {
                        Name                       = genPlatoonHillDef.Name,
                        BrainTier                  = genPlatoonHillDef.BrainTier,
                        BTreeInterpreter           = genPlatoonHillDef.BTreeInterpreter,
                        ManagedBlackboardVariables = genPlatoonHillDef.ManagedBlackboardVariables,
                        StatefulWorkingSlots       = genPlatoonHillDef.StatefulWorkingSlots,
                        ParamsDtoType              = typeof(PlatoonHillAttackParams),
                        ParseParams                = (json, ptr) => HillAttackCommanderNodes.ParsePlatoonHillAttackParams(
                            json, ptr, geoTransform, entityMap),
                    });
                }
            };
        }
    }
}
