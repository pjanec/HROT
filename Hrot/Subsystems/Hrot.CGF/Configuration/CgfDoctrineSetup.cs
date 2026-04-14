using Fdp.Modules.Geographic;
using FDP.Toolkit.Behavior;
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
        public static void RegisterAll(DoctrineRegistry registry, IGeographicTransform geoTransform)
        {
            if (registry == null) throw new System.ArgumentNullException(nameof(registry));
            if (geoTransform == null) throw new System.ArgumentNullException(nameof(geoTransform));

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
