using System;
using System.Numerics;
using CarKinem.Commands;
using CarKinem.Core;
using CarKinem.Formation;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace CarKinem.Systems
{
    /// <summary>
    /// Processes vehicle command events.
    /// Runs early to update NavState before physics.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public class VehicleCommandSystem : IEcsModuleSystem
    {
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(VehicleCommandSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            ProcessSpawnCommands(repo);
            ProcessCreateFormationCommands(repo);
            ProcessJoinFormationCommands(repo);
            ProcessLeaveFormationCommands(repo);
        }
        
        private void ProcessSpawnCommands(EntityRepository repo)
        {
            var events = repo.Bus.Read<CmdSpawnVehicle>();
            
            foreach (var cmd in events)
            {
                var entity = cmd.Entity;
                
                // Verify entity was pre-allocated and is alive
                if (!repo.IsAlive(entity))
                {
                    // Console.WriteLine($"WARNING: CmdSpawnVehicle references dead entity {entity}");
                    continue;
                }
                
                // Add VehicleState (stripped)
                repo.AddComponent(entity, new VehicleState
                {
                    Speed = 0f,
                    SteerAngle = 0f,
                    Accel = 0f,
                    CurrentLaneIndex = -1
                });
                
                // Add SimTransform and SimVelocity
                // Note: Z=0 by default for 2D->3D bridge
                float yaw = MathF.Atan2(cmd.Heading.Y, cmd.Heading.X);
                repo.AddComponent(entity, new SimTransform
                {
                    Position = new Vector3(cmd.Position.X, cmd.Position.Y, 0),
                    Rotation = SimMath.FromYaw(yaw)
                });
                repo.AddComponent(entity, new SimVelocity
                {
                    Linear = Vector3.Zero,
                    Angular = Vector3.Zero
                });
                
                // Add VehicleParams component (use preset)
                var preset = VehiclePresets.GetPreset(cmd.Class);
                preset.Class = cmd.Class;  // Set class field
                repo.AddComponent(entity, preset);
                
                // Add NavState component (idle)
                repo.AddComponent(entity, new NavState
                {
                    Mode = KinematicsMode.None,
                    RoadPhase = RoadGraphPhase.Approaching,
                    TrajectoryId = -1,
                    CurrentSegmentId = -1,
                    ProgressS = 0f,
                    TargetSpeed = 0f,
                    FinalDestination = cmd.Position,
                    ArrivalRadius = 2.0f,
                    SpeedErrorInt = 0f,
                    LastSteerCmd = 0f,
                    ReverseAllowed = 0,
                    HasArrived = 0,
                    IsBlocked = 0
                });
            }
        }

        private void ProcessCreateFormationCommands(EntityRepository repo)
        {
            var events = repo.Bus.Read<CmdCreateFormation>();
            
            foreach (var cmd in events)
            {
                var leaderEntity = cmd.LeaderEntity;
                
                if (!repo.IsAlive(leaderEntity))
                {
                    // Console.WriteLine($"WARNING: CmdCreateFormation references dead leader {leaderEntity}");
                    continue;
                }
                
                // Create/update FormationController component on leader
                FormationController controller;
                
                if (repo.HasComponent<FormationController>(leaderEntity))
                {
                    // Update existing controller
                    controller = repo.GetComponent<FormationController>(leaderEntity);
                }
                else
                {
                    // Create new controller
                    controller = new FormationController();
                    repo.AddComponent(leaderEntity, controller);
                }
                
                // Configure controller
                controller.Type = cmd.Type;
                controller.Params = cmd.Params;
                
                repo.SetComponent(leaderEntity, controller);
            }
        }

        private void ProcessJoinFormationCommands(EntityRepository repo)
        {
            var events = repo.Bus.Read<CmdJoinFormation>();
            
            foreach (var cmd in events)
            {
                var followerEntity = cmd.Entity;
                var leaderEntity = cmd.LeaderEntity;
                
                if (!repo.IsAlive(followerEntity) || !repo.IsAlive(leaderEntity))
                    continue;
                
                // Verify leader has a formation controller
                if (!repo.HasComponent<FormationController>(leaderEntity))
                {
                    // Console.WriteLine($"WARNING: CmdJoinFormation: Leader {leaderEntity} has no FormationController");
                    continue;
                }
                
                // Add FormationFollower component if not exists
                if (!repo.HasComponent<FormationFollower>(followerEntity))
                {
                    repo.AddComponent(followerEntity, new FormationFollower());
                }
                
                var member = repo.GetComponent<FormationFollower>(followerEntity);
                member.LeaderEntity = leaderEntity;
                member.SlotIndex = (ushort)cmd.SlotIndex;
                member.State = FormationMemberState.Rejoining;
                member.IsInFormation = 1;
                repo.SetComponent(followerEntity, member);
                
                // Set follower navigation mode to Formation
                var nav = repo.GetComponent<NavState>(followerEntity);
                nav.Mode = KinematicsMode.Formation;
                nav.HasArrived = 0;
                repo.SetComponent(followerEntity, nav);
            }
        }
        
        private void ProcessLeaveFormationCommands(EntityRepository repo)
        {
            var events = repo.Bus.Read<CmdLeaveFormation>();
            
            foreach (var cmd in events)
            {
                var entity = cmd.Entity;
                
                if (!repo.IsAlive(entity))
                    continue;
                
                var nav = repo.GetComponent<NavState>(entity);
                nav.Mode = KinematicsMode.None;
                
                repo.SetComponent(entity, nav);
            }
        }
    }
}
