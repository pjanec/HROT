using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;

namespace Fdp.Toolkit.Navigation.Systems
{
    /// <summary>
    /// Materializes <see cref="PathfindingResultEvent"/>s published by
    /// <see cref="PathfindingSolverSystem"/> into the <see cref="PathfindingBatchData"/> ring buffer
    /// so the Brain BTree can read results without any locking.
    ///
    /// <para><b>Execution phase:</b> <see cref="SystemPhase.Input"/>, so results are visible to
    /// BTree nodes in the same frame's Simulation phase.</para>
    ///
    /// <para><b>Thread safety:</b> runs on the main thread only.  Safe to mutate struct fields
    /// via <see cref="EntityRepository"/>.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    public sealed class PathfindingResultMaterializationSystem : IEcsModuleSystem
    {
        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            var events = view.ReadEvents<PathfindingResultEvent>();
            if (events.IsEmpty) return;

            if (view is not EntityRepository repo) return;
            if (!repo.HasSingleton<PathfindingBatchData>()) return;

            ref var batch = ref repo.GetSingleton<PathfindingBatchData>();

            for (int i = 0; i < events.Length; i++)
            {
                ref readonly var evt = ref events[i];
                int slot = (int)((uint)evt.RequestId % (uint)PathfindingBatchData.DefaultCapacity);

                batch.Results[slot] = new PathResult
                {
                    RequestId           = evt.RequestId,
                    IsReachable         = evt.IsReachable,
                    TotalDistanceMeters = evt.TotalDistanceMeters,
                    RouteHandle         = evt.RouteHandle,
                    SourceNodeId        = evt.SourceNodeId,
                };

                // Entity-specific writes: look up originator, branch on action.
                // entityIndex is packed in the high 32 bits of RequestId.
                int entityIndex = (int)((ulong)evt.RequestId >> 32);
                var entity = repo.GetEntityByIndex(entityIndex);
                if (!repo.IsAlive(entity)) continue;

                // ⭐ CE-3026 — a MoveTo is a PathToPoint INTENT, which exists on the vehicle side on every host; it used to be
                //   keyed on the Brain's LocomotionChannel, which only the editor's fused world has. PlanRoute (dormant,
                //   design §7) still arrives on the channel.
                bool pathToPoint = repo.HasComponent<NavigationIntent>(entity)
                    && repo.GetComponent<NavigationIntent>(entity).Mode == NavigationMode.PathToPoint;
                ushort action = repo.HasComponent<LocomotionChannel>(entity)
                    ? repo.GetComponent<LocomotionChannel>(entity).ActiveAction
                    : (ushort)0;

                if (pathToPoint)
                {
                    if (evt.IsReachable)
                    {
                        // Commit corridor state for this entity.
                        repo.AddComponent(entity, new NavigationCorridorMuscle
                        {
                            RouteHandle         = evt.RouteHandle,
                            NavmeshVersion      = (uint)evt.NavmeshVersionAtPlan,
                            CurrentSegmentIndex = 0,
                            TotalSegmentCount   = 1,
                            TotalDistance       = evt.TotalDistanceMeters,
                            PrimaryBackend      = (byte)evt.PrimaryBackend,
                        });

                        if (repo.HasComponent<NavigationStatus>(entity))
                        {
                            ref var status = ref repo.GetComponentRW<NavigationStatus>(entity);
                            status.Phase  = NavigationPhase.Following;
                            status.Result = NavigationResult.InProgress;
                        }

                        repo.Bus.Publish(new MoveStartedEvent
                        {
                            RequestId    = evt.RequestId,
                            RouteHandle  = evt.RouteHandle,
                            SourceNodeId = evt.SourceNodeId,
                        });
                    }
                    else
                    {
                        // ⭐ CE-3026 — no path ⇒ FAIL, never a straight-line fallback: stop the vehicle and report it for
                        //   THIS intent (the status writer resets a status whose IntentId does not match).
                        if (repo.HasComponent<global::CarKinem.Core.NavState>(entity))
                        {
                            ref var nav = ref repo.GetComponentRW<global::CarKinem.Core.NavState>(entity);
                            nav.Mode        = global::CarKinem.Core.KinematicsMode.None;
                            nav.TargetSpeed = 0f;
                        }
                        if (repo.HasComponent<NavigationStatus>(entity))
                        {
                            ref var status = ref repo.GetComponentRW<NavigationStatus>(entity);
                            status.IntentId = repo.GetComponent<NavigationIntent>(entity).IntentId;
                            status.Result   = NavigationResult.FailedUnreachable;
                        }
                    }
                }
                else if (action == NavigationConstants.ActionIdPlanRoute)
                {
                    if (repo.HasComponent<NavigationStatus>(entity))
                    {
                        ref var status = ref repo.GetComponentRW<NavigationStatus>(entity);
                        if (evt.IsReachable)
                        {
                            status.Phase       = NavigationPhase.Idle;
                            status.Result      = NavigationResult.PathFound;
                            status.RouteHandle = evt.RouteHandle;
                        }
                        else
                        {
                            status.Result = NavigationResult.NoPath;
                        }
                    }
                }
            }
        }
    }
}
