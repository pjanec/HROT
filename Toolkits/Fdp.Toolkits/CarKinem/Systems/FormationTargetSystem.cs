using System;
using System.Numerics;
using CarKinem.Core;
using CarKinem.Formation;
using CarKinem.Trajectory;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.ModuleHost.Abstractions;

namespace CarKinem.Systems
{
    /// <summary>
    /// Calculates formation slot targets for members.
    /// Runs before CarKinematicsSystem.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public class FormationTargetSystem : IEcsModuleSystem
    {
        private readonly FormationTemplateManager _templateManager;
        private readonly TrajectoryPoolManager _trajectoryPool;
        
        public FormationTargetSystem(FormationTemplateManager templateManager, TrajectoryPoolManager trajectoryPool)
        {
            _templateManager = templateManager;
            _trajectoryPool = trajectoryPool;
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(FormationTargetSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            // Query all active formation followers
            var followerQuery = repo.Query().With<FormationFollower>().Build();
            
            foreach (var followerEntity in followerQuery)
            {
                var follower = repo.GetComponent<FormationFollower>(followerEntity);
                if (follower.IsInFormation == 0)
                    continue;

                // Get leader from UnitSubordinate component (leader reference removed from FormationFollower)
                if (!repo.HasComponent<UnitSubordinate>(followerEntity))
                    continue;
                var leaderEntity = repo.GetComponent<UnitSubordinate>(followerEntity).Commander;
                if (!repo.IsAlive(leaderEntity))
                    continue;
                if (!repo.HasComponent<FormationController>(leaderEntity))
                    continue;

                var controller = repo.GetComponent<FormationController>(leaderEntity);
                UpdateFollower(repo, followerEntity, ref follower, leaderEntity, ref controller);
            }
        }
        
        private void UpdateFollower(
            EntityRepository repo,
            Entity followerEntity,
            ref FormationFollower follower,
            Entity leaderEntity,
            ref FormationController controller)
        {
            var leaderState = repo.GetComponent<VehicleState>(leaderEntity);
            var leaderTf = repo.GetComponent<SimTransform>(leaderEntity);
            var template = _templateManager.GetTemplate(controller.Type);
            
            // Default Formation Orientation (Rigid fallback)
            Vector3 fwd3D = Vector3.Transform(Vector3.UnitX, leaderTf.Rotation);
            Vector2 formationHeading = new Vector2(fwd3D.X, fwd3D.Y);
            if (formationHeading == Vector2.Zero) formationHeading = Vector2.UnitX;
            else formationHeading = Vector2.Normalize(formationHeading);
            
            // Trajectory Following Logic ("Ghost Rails")
            bool hasTrajectory = false;
            CustomTrajectory trajectory = default;
            float leaderS = 0f;

            if (repo.HasComponent<NavState>(leaderEntity))
            {
                var nav = repo.GetComponent<NavState>(leaderEntity);
                if (nav.Mode == KinematicsMode.CustomTrajectory && nav.TrajectoryId > 0)
                {
                     if (_trajectoryPool.TryGetTrajectory(nav.TrajectoryId, out trajectory))
                     {
                         hasTrajectory = true;
                         leaderS = nav.ProgressS;
                         
                         // Update fallback heading to path tangent at leader position
                         // (Still useful if we fallback for some reason)
                         var (_, tangent, _) = _trajectoryPool.SampleTrajectory(trajectory.Id, leaderS);
                         if (tangent != Vector2.Zero) 
                             formationHeading = Vector2.Normalize(tangent);
                     }
                }
            }
            
            int slotIndex = follower.SlotIndex;
            Vector2 slotPos;
            Vector2 slotHeading;
            
            // Try to use Trajectory Following (Curved Formation)
            if (hasTrajectory && template.SlotOffsets != null && slotIndex < template.SlotOffsets.Length)
            {
                Vector2 offset = template.SlotOffsets[slotIndex];
                // offset.X = Longitudinal (Along track), offset.Y = Lateral (Right of track)
                
                float targetS = leaderS + offset.X;
                
                // Sample and Extrapolate if needed
                Vector2 pathPos;
                Vector2 pathTangent;
                
                if (trajectory.IsLooped == 0)
                {
                    // Linear Extrapolation for start/end
                    if (targetS < 0)
                    {
                        var (p0, t0, _) = _trajectoryPool.SampleTrajectory(trajectory.Id, 0);
                        pathPos = p0 + t0 * targetS; // targetS is negative distance
                        pathTangent = t0;
                    }
                    else if (targetS > trajectory.TotalLength)
                    {
                        var (pe, te, _) = _trajectoryPool.SampleTrajectory(trajectory.Id, trajectory.TotalLength);
                        pathPos = pe + te * (targetS - trajectory.TotalLength);
                        pathTangent = te;
                    }
                    else
                    {
                        // On path
                        (pathPos, pathTangent, _) = _trajectoryPool.SampleTrajectory(trajectory.Id, targetS);
                    }
                }
                else
                {
                    // Looped: SampleTrajectory handles wrapping
                    (pathPos, pathTangent, _) = _trajectoryPool.SampleTrajectory(trajectory.Id, targetS);
                }
                
                // Apply Lateral Offset
                Vector2 pathRight = new Vector2(pathTangent.Y, -pathTangent.X);
                slotPos = pathPos + pathRight * offset.Y;
                slotHeading = pathTangent;
            }
            else
            {
                // Fallback: Rigid Body formation relative to leader's current position/heading
                Vector2 leaderPos2D = new Vector2(leaderTf.Position.X, leaderTf.Position.Y);
                slotPos = template.GetSlotPosition(slotIndex, leaderPos2D, formationHeading);
                slotHeading = formationHeading;
            }
            
            // Get/create FormationTarget component
            if (!repo.HasComponent<FormationTarget>(followerEntity))
            {
                repo.AddComponent(followerEntity, new FormationTarget());
            }
            
            var target = repo.GetComponent<FormationTarget>(followerEntity);
            target.TargetPosition = slotPos;
            target.TargetHeading = slotHeading; 
            target.TargetSpeed = leaderState.Speed;
            repo.SetComponent(followerEntity, target);
            
            // Update follower state based on distance to slot
            var followerTf = repo.GetComponent<SimTransform>(followerEntity);
            
            float distToSlot = Vector2.Distance(new Vector2(followerTf.Position.X, followerTf.Position.Y), slotPos);
            
            if (distToSlot < controller.Params.ArrivalThreshold)
            {
                follower.State = FormationMemberState.InSlot;
            }
            else if (distToSlot < controller.Params.BreakDistance * 0.5f) // Heuristic for CatchUp
            {
                follower.State = FormationMemberState.CatchingUp;
            }
            else if (distToSlot < controller.Params.BreakDistance)
            {
                follower.State = FormationMemberState.Rejoining;
            }
            else
            {
                follower.State = FormationMemberState.Broken;
            }
            
            repo.SetComponent(followerEntity, follower);
        }
    }
}
