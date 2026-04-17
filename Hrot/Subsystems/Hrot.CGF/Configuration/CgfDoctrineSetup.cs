using Fdp.Modules.Geographic;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Replication.Services;
using Hrot.CGF.Brains;

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
    }
}
