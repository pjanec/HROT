using Fdp.Modules.Geographic;
using FDP.Toolkit.Behavior;
using Hrot.SimHost.Brains;

namespace Hrot.SimHost.Configuration
{
    /// <summary>
    /// Single source of truth for SimHost doctrine registrations.
    /// Shared by SimHost and CGF composition roots.
    /// </summary>
    public static class SimHostDoctrineSetup
    {
        public static void RegisterAll(DoctrineRegistry registry, IGeographicTransform geoTransform)
        {
            if (registry == null) throw new System.ArgumentNullException(nameof(registry));
            if (geoTransform == null) throw new System.ArgumentNullException(nameof(geoTransform));

            unsafe
            {
                registry.Register(SimHostDoctrineIds.MoveTo_BT, "MoveToLocation",
                    new DoctrineDefinition
                    {
                        Name = "MoveToLocation",
                        BrainTier = BehaviorConstants.BrainTierBTree,
                        ParseParams = (json, ptr) => SimHostNodes.ParseMoveToParams(json, ptr, geoTransform),
                        BTreeInterpreter = SimHostNodes.BuildMoveToLocationInterpreter()
                    });

                registry.Register(SimHostDoctrineIds.FollowRoute_BT, "FollowRoute",
                    new DoctrineDefinition
                    {
                        Name = "FollowRoute",
                        BrainTier = BehaviorConstants.BrainTierBTree,
                        ParseParams = (json, ptr) => SimHostNodes.ParseFollowRouteParams(json, ptr),
                        BTreeInterpreter = SimHostNodes.BuildFollowRouteInterpreter()
                    });
            }

            registry.Register(SimHostDoctrineIds.JoinFormation_BT, "JoinFormation",
                new DoctrineDefinition
                {
                    Name = "JoinFormation",
                    BrainTier = BehaviorConstants.BrainTierBTree,
                    BTreeInterpreter = SimHostNodes.BuildJoinFormationInterpreter()
                });

            registry.Register(SimHostDoctrineIds.Idle_HSM, "Idle",
                new DoctrineDefinition
                {
                    Name = "Idle",
                    BrainTier = BehaviorConstants.BrainTierHsm
                });

            registry.Register(SimHostDoctrineIds.WanderMilitary_BT, "WanderMilitary",
                new DoctrineDefinition
                {
                    Name = "WanderMilitary",
                    BrainTier = BehaviorConstants.BrainTierBTree,
                    BTreeInterpreter = SimHostNodes.BuildWanderMilitaryInterpreter()
                });
        }
    }
}
