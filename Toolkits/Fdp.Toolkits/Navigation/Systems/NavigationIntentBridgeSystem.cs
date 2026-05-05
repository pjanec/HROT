using System;
using System.Collections.Generic;
using CarKinem.Core;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Navigation.Systems
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
    ///     halt navigation by setting <c>Mode=None</c> and <c>TargetSpeed=0</c> on <see cref="NavState"/>.</item>
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
    [UpdateInPhase(SystemPhase.Simulation)]
    public class NavigationIntentBridgeSystem : IEcsModuleSystem
    {
        // FIX: Cache by the full Entity struct (Index + Generation) to prevent
        // false-positives when entity indices are recycled by the free-list.
        private readonly Dictionary<Entity, uint> _lastAppliedIntentId = new();
    
        private uint _lastScanTick;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(NavigationIntentBridgeSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            var query = repo.Query()
                .With<NavigationIntent>()
                .With<NavState>()
                .Build();

            // 1. Coarse unmanaged filter
            foreach (var entity in repo.QueryDelta(query, _lastScanTick))
            {
                var intent = repo.GetComponent<NavigationIntent>(entity);

                // 2. Fine-grained filter using the full Entity struct
                if (_lastAppliedIntentId.TryGetValue(entity, out uint lastId) 
                    && lastId == intent.IntentId)
                {
                    continue;
                }

                var nav = repo.GetComponent<NavState>(entity);

                switch (intent.Mode)
                {
                    case NavigationMode.None:
                        // Explicit cancel/stop intent from cognitive tier.
                        nav.Mode        = KinematicsMode.None;
                        nav.TargetSpeed = 0f;
                        nav.HasArrived  = 0;
                        nav.ReverseAllowed = 0;
                        break;

                    case NavigationMode.DirectPoint:
                        nav.Mode             = KinematicsMode.Direct;
                        nav.FinalDestination = intent.FinalDestination;
                        nav.TargetSpeed      = intent.TargetSpeed;
                        nav.ArrivalRadius    = intent.ArrivalRadius;
                        nav.ReverseAllowed   = intent.ReverseAllowed;
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
                        nav.Mode         = KinematicsMode.CustomTrajectory;
                        nav.TrajectoryId = intent.TrajectoryId;
                        nav.HasArrived   = 0;
                        nav.ProgressS    = 0f; 
                        break;

                    default:
                        nav.Mode             = KinematicsMode.Direct;
                        nav.FinalDestination = intent.FinalDestination;
                        nav.TargetSpeed      = intent.TargetSpeed;
                        nav.ArrivalRadius    = intent.ArrivalRadius;
                        nav.HasArrived       = 0;
                        break;
                }

                repo.SetComponent(entity, nav);
            
                // Cache against the generation-safe handle
                _lastAppliedIntentId[entity] = intent.IntentId;
            }

            _lastScanTick = repo.GlobalVersion; 
        }
    }
}
