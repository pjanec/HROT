using Fbt;
using Fbt.Runtime;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Services;
using Hrot.CGF.Brains;
using Hrot.CGF.Generated;
using Hrot.Map.Definitions.Doctrine;
using Hrot.Presentation.Behavior;

namespace Hrot.CGF.Configuration
{
    /// <summary>
    /// Single source of truth for CGF doctrine registrations.
    /// Relocated from Hrot.SimHost.Configuration as part of the Brain/Muscle
    /// architectural split (modular-2 feedback-1).
    /// </summary>
    public static class CgfDoctrineSetup
    {
        /// <param name="geoTransform">
        /// Geographic coordinate transform used by MoveToLocation. May be <c>null</c>
        /// in the offline editor where only Cartesian coordinates are used.
        /// </param>
        public static void RegisterAll(
            DoctrineRegistry registry,
            IGeographicTransform? geoTransform,
            NetworkEntityMap entityMap)
        {
            if (registry  == null) throw new System.ArgumentNullException(nameof(registry));
            if (entityMap == null) throw new System.ArgumentNullException(nameof(entityMap));

            // One shared registry covers all BTree doctrines for this assembly.
            // FbtActionRegistrar (generated) registers both 4-param and 3-param bridge closures.
            var actionRegistry = new ActionRegistry<BrainBlackboard, BTreeContext>();
            FbtActionRegistrar.RegisterAll(actionRegistry);

            unsafe
            {
                registry.Register(CgfDoctrineIds.MoveTo_BT, MoveToLocationParamsJsonDto.BehaviorId,
                    new DoctrineDefinition
                    {
                        Name = "MoveToLocation",
                        BrainTier = BehaviorConstants.BrainTierBTree,
                        ParseParams = (json, ptr) => CgfNodes.ParseMoveToParams(json, ptr, geoTransform),
                        ParamsDtoType = typeof(CgfNodes.MoveToLocationParams),
                        BTreeInterpreter = new Interpreter<BrainBlackboard, BTreeContext>(
                            FbtTreeCatalog.GetMoveToLocation(), actionRegistry)
                    });

                registry.Register(CgfDoctrineIds.FollowRoute_BT, FollowRouteParamsJsonDto.BehaviorId,
                    new DoctrineDefinition
                    {
                        Name = "FollowRoute",
                        BrainTier = BehaviorConstants.BrainTierBTree,
                        ParseParams = (json, ptr) => CgfNodes.ParseFollowRouteParams(json, ptr),
                        ParamsDtoType = typeof(CgfNodes.FollowRouteParams),
                        BTreeInterpreter = new Interpreter<BrainBlackboard, BTreeContext>(
                            FbtTreeCatalog.GetFollowRoute(), actionRegistry)
                    });

                registry.Register(CgfDoctrineIds.FireAtTarget_BT, FireAtTargetParamsJsonDto.BehaviorId,
                    new DoctrineDefinition
                    {
                        Name = "FireAtTarget",
                        BrainTier = BehaviorConstants.BrainTierBTree,
                        ParseParams = (json, ptr) => CgfNodes.ParseFireAtTargetParams(json, ptr, entityMap),
                        ParamsDtoType = typeof(CgfNodes.FireAtTargetParams),
                        BTreeInterpreter = new Interpreter<BrainBlackboard, BTreeContext>(
                            FbtTreeCatalog.GetFireAtTarget(), actionRegistry)
                    });
            }

            registry.Register(CgfDoctrineIds.JoinFormation_BT, JoinFormationParamsJsonDto.BehaviorId,
                new DoctrineDefinition
                {
                    Name = "JoinFormation",
                    BrainTier = BehaviorConstants.BrainTierBTree,
                    ParamsDtoType = typeof(CgfNodes.JoinFormationParams),
                    BTreeInterpreter = new Interpreter<BrainBlackboard, BTreeContext>(
                        FbtTreeCatalog.GetJoinFormation(), actionRegistry)
                });

            registry.Register(CgfDoctrineIds.Idle_HSM, IdleParamsJsonDto.BehaviorId,
                new DoctrineDefinition
                {
                    Name = "Idle",
                    BrainTier = BehaviorConstants.BrainTierHsm
                });

            registry.Register(CgfDoctrineIds.WanderMilitary_BT, WanderMilitaryParamsJsonDto.BehaviorId,
                new DoctrineDefinition
                {
                    Name = "WanderMilitary",
                    BrainTier = BehaviorConstants.BrainTierBTree,
                    BTreeInterpreter = new Interpreter<BrainBlackboard, BTreeContext>(
                        FbtTreeCatalog.GetWanderMilitary(), actionRegistry)
                });
        }

        /// <summary>
        /// Creates a <see cref="ScenarioBehaviorRemapper"/> pre-registered with all
        /// CGF behavior param DTO types that carry <c>[RemapNetworkId]</c> properties.
        /// Used by load handlers to rewrite network IDs after two-pass ID allocation.
        /// </summary>
        public static ScenarioBehaviorRemapper CreateBehaviorRemapper()
        {
            var remapper = new ScenarioBehaviorRemapper();
            DoctrineSchemaDiscovery.AutoRegister(new BehaviorUiRegistry(), remapper);
            return remapper;
        }

    }
}
