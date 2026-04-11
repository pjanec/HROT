using System.Collections.Generic;
using CarKinem.Core;
using Fdp.Kernel;

namespace FDP.Toolkit.Navigation.Systems
{
    /// <summary>
    /// Bridges <see cref="NavigationIntent"/> (CQRS command written by the Brain tier via
    /// <see cref="Executors.MoveToExecutor"/>) into <see cref="NavState"/> (physics input
    /// consumed by <see cref="CarKinem.Systems.CarKinematicsSystem"/>).
    ///
    /// <para>
    /// This system is the "nervous system" adapter for the CQRS navigation contract
    /// (MOD1-P1T1/P1T2). It must run <b>after</b> <c>LocomotionDispatcherSystem</c>
    /// (so executors have already written <see cref="NavigationIntent"/>) and
    /// <b>before</b> <c>CarKinematicsSystem</c> (so the updated <see cref="NavState"/>
    /// is visible to the physics layer in the same tick).
    /// </para>
    ///
    /// <para>
    /// <b>Mapping rules:</b>
    /// <list type="bullet">
    ///   <item>If <see cref="NavigationIntent.Mode"/> is <see cref="NavigationMode.None"/> →
    ///     skip (no active intent; physics layer retains its current <see cref="NavState"/>).</item>
    ///   <item><see cref="NavigationMode.DirectPoint"/> → <c>KinematicsMode.Direct</c>:
    ///     copy <c>FinalDestination</c>, <c>TargetSpeed</c>, <c>ArrivalRadius</c>.</item>
    ///   <item><see cref="NavigationMode.RoadGraph"/> → <c>KinematicsMode.RoadGraph</c>:
    ///     copy <c>TargetNodeId</c> to <see cref="NavState.CurrentSegmentId"/>.</item>
    ///   <item><see cref="NavigationMode.FollowRoute"/> → <c>KinematicsMode.CustomTrajectory</c>:
    ///     copy <c>TrajectoryId</c>.  When <c>IntentId</c> changes, <c>NavState.ProgressS</c>
    ///     is reset to 0 so the vehicle restarts the route from the beginning.</item>
    /// </list>
    /// </para>
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public class NavigationIntentBridgeSystem : ComponentSystem
    {
        // Tracks the last IntentId applied per entity index (used for FollowRoute loop-reset).
        // Key: entity index (int). Stale entries for destroyed entities are harmless — a new
        // entity at the same slot will have IntentId=0 (default) which never matches the stored
        // value, so ProgressS will be reset on first activation (correct behaviour).
        private readonly Dictionary<int, uint> _lastAppliedIntentId = new();

        protected override void OnUpdate()
        {
            var query = World.Query()
                .With<NavigationIntent>()
                .With<NavState>()
                .Build();

            foreach (var entity in query)
            {
                var intent = World.GetComponent<NavigationIntent>(entity);

                // Skip inactive intents — let NavState retain its current value.
                if (intent.Mode == NavigationMode.None)
                    continue;

                var nav = World.GetComponent<NavState>(entity);

                switch (intent.Mode)
                {
                    case NavigationMode.DirectPoint:
                        nav.Mode             = KinematicsMode.Direct;
                        nav.FinalDestination = intent.FinalDestination;
                        nav.TargetSpeed      = intent.TargetSpeed;
                        nav.ArrivalRadius    = intent.ArrivalRadius;
                        nav.HasArrived       = 0;
                        break;

                    case NavigationMode.RoadGraph:
                        nav.Mode             = KinematicsMode.RoadGraph;
                        nav.RoadPhase        = RoadGraphPhase.Approaching;
                        nav.CurrentSegmentId = intent.TargetNodeId;
                        nav.TargetSpeed      = intent.TargetSpeed;
                        nav.HasArrived       = 0;
                        break;

                    case NavigationMode.FollowRoute:
                    {
                        bool isNewIntent = !_lastAppliedIntentId.TryGetValue(entity.Index, out uint lastId)
                                           || lastId != intent.IntentId;

                        nav.Mode         = KinematicsMode.CustomTrajectory;
                        nav.TrajectoryId = intent.TrajectoryId;
                        nav.HasArrived   = 0;
                        if (isNewIntent)
                            // ProgressS is reset ONLY when the intent id changes (i.e. a new or
                            // looped command), NOT every tick.  Resetting unconditionally would
                            // restart the route from the beginning on every frame while the
                            // vehicle is driving, making forward progress impossible.
                            nav.ProgressS = 0f;  // restart route from beginning on new intent

                        _lastAppliedIntentId[entity.Index] = intent.IntentId;
                        break;
                    }

                    default:
                        // Unsupported modes fall through as Direct (best-effort).
                        nav.Mode             = KinematicsMode.Direct;
                        nav.FinalDestination = intent.FinalDestination;
                        nav.TargetSpeed      = intent.TargetSpeed;
                        nav.ArrivalRadius    = intent.ArrivalRadius;
                        nav.HasArrived       = 0;
                        break;
                }

                World.SetComponent(entity, nav);
            }
        }
    }
}
