using Fdp.Modules.Geographic;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Params;
using Fdp.Toolkit.Replication.Services;
using Hrot.CGF.Brains;
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

            unsafe
            {
                registry.Register(CgfDoctrineIds.MoveTo_BT, "MoveToLocation",
                    new DoctrineDefinition
                    {
                        Name = "MoveToLocation",
                        BrainTier = BehaviorConstants.BrainTierBTree,
                        ParseParams = (json, ptr) => CgfNodes.ParseMoveToParams(json, ptr, geoTransform),
                        BTreeInterpreter = CgfNodes.BuildMoveToLocationInterpreter()
                    });

                registry.Register(CgfDoctrineIds.FollowRoute_BT, "FollowRoute",
                    new DoctrineDefinition
                    {
                        Name = "FollowRoute",
                        BrainTier = BehaviorConstants.BrainTierBTree,
                        ParseParams = (json, ptr) => CgfNodes.ParseFollowRouteParams(json, ptr),
                        BTreeInterpreter = CgfNodes.BuildFollowRouteInterpreter()
                    });

                registry.Register(CgfDoctrineIds.FireAtTarget_BT, "FireAtTarget",
                    new DoctrineDefinition
                    {
                        Name = "FireAtTarget",
                        BrainTier = BehaviorConstants.BrainTierBTree,
                        ParseParams = (json, ptr) => CgfNodes.ParseFireAtTargetParams(json, ptr, entityMap),
                        BTreeInterpreter = CgfNodes.BuildFireAtTargetInterpreter()
                    });
            }

            registry.Register(CgfDoctrineIds.JoinFormation_BT, "JoinFormation",
                new DoctrineDefinition
                {
                    Name = "JoinFormation",
                    BrainTier = BehaviorConstants.BrainTierBTree,
                    BTreeInterpreter = CgfNodes.BuildJoinFormationInterpreter()
                });

            registry.Register(CgfDoctrineIds.Idle_HSM, "Idle",
                new DoctrineDefinition
                {
                    Name = "Idle",
                    BrainTier = BehaviorConstants.BrainTierHsm
                });

            registry.Register(CgfDoctrineIds.WanderMilitary_BT, "WanderMilitary",
                new DoctrineDefinition
                {
                    Name = "WanderMilitary",
                    BrainTier = BehaviorConstants.BrainTierBTree,
                    BTreeInterpreter = CgfNodes.BuildWanderMilitaryInterpreter()
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
            remapper.Register<FireAtTargetParamsJsonDto>("FireAtTarget");
            remapper.Register<FollowRouteParamsJsonDto>("FollowRoute");
            return remapper;
        }

        /// <summary>
        /// Creates a <see cref="BehaviorUiRegistry"/> pre-registered with all CGF
        /// behavior param DTO types so the mission editor panel can render each
        /// behavior's parameters generically.
        /// </summary>
        public static BehaviorUiRegistry CreateBehaviorUiRegistry()
        {
            var registry = new BehaviorUiRegistry();
            registry.Register<FireAtTargetParamsJsonDto>("FireAtTarget");
            registry.Register<FollowRouteParamsJsonDto>("FollowRoute");
            registry.Register<MoveToLocationParamsJsonDto>("MoveToLocation");
            return registry;
        }
    }
}
