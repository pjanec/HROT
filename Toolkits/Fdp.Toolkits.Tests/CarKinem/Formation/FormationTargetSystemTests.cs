using CarKinem.Core;
using CarKinem.Formation;
using CarKinem.Systems;
using CarKinem.Trajectory;
using Fdp.Core;
using System;
using System.Numerics;
using Xunit;

namespace CarKinem.Tests.Formation
{
    public class FormationTargetSystemTests
    {
        [Fact]
        public void System_UpdatesFormationTargets()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<VehicleState>();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<SimVelocity>();
            repo.RegisterComponent<FormationRoster>();
            repo.RegisterComponent<FormationTarget>();
            repo.RegisterComponent<FormationMember>();

            var templateManager = new FormationTemplateManager();
            var trajectoryPool = new TrajectoryPoolManager();

			var system = new FormationTargetSystem(templateManager, trajectoryPool);

            // Create Leader at (100, 100), Forward East (1, 0) -> Yaw 0
            var leader = repo.CreateEntity();
            repo.AddComponent(leader, new VehicleState { Speed = 10f });
            repo.AddComponent(leader, new SimTransform { 
                Position = new Vector3(100, 100, 0), 
                Rotation = Quaternion.Identity // X-Forward = East
            });
            repo.AddComponent(leader, new SimVelocity { Linear = new Vector3(10, 0, 0) });

            // Create Follower at (0, 0), Forward East -> Yaw 0
            var follower = repo.CreateEntity();
            repo.AddComponent(follower, new VehicleState { Speed = 0f });
            repo.AddComponent(follower, new SimTransform { 
                Position = Vector3.Zero, 
                Rotation = Quaternion.Identity // X-Forward = East
            });
            repo.AddComponent(follower, new SimVelocity { Linear = Vector3.Zero });
            repo.AddComponent(follower, new FormationMember { State = FormationMemberState.Broken });

            // Create Formation Roster Entity
            var rosterEntity = repo.CreateEntity();
            var roster = new FormationRoster();
            
            // Fixed buffer assignment needs no 'new' allocation, they are inline.
            roster.SetMember(0, leader); // Leader at index 0
            roster.SetMember(1, follower);
            roster.SetSlotIndex(1, 0); // Use first slot
            roster.Count = 2;
            roster.Type = FormationType.Column;
            roster.Params = new FormationParams { ArrivalThreshold = 1f, BreakDistance = 20f, MaxCatchUpFactor = 1.2f };
            
            repo.AddComponent(rosterEntity, roster);

            system.Execute(repo, 0.016f);

            // Check follower target
            Assert.True(repo.HasComponent<FormationTarget>(follower));
            // Wait, does FormationTarget use SimTransform? No, it's a target component.
            // Check usage inside FormationTargetSystem.
            // Target is (95, 100).
            
            var target = repo.GetComponent<FormationTarget>(follower);
            
            // Expected slot pos: Leader (100,100) + Offset of slot 0 in Column (-5, 0 relative to leader) 
            // Relative to leader: Leader Forward is East (1,0). Right is South (0,-1).
            // Column usually places behind leader.
            // If offset is (-5, 0) in formation space (X=Right, Y=Forward?)
            // Or (X=Back, Y=Side)?
            // Standard column: Behind leader.
            // If FormationTemplate defines (0, -5)?
            // Assuming standard (0, -5) behind?
            // If result (95, 100) -> X changed by -5.
            // Leader at 100. New at 95. So 5m behind.
            // Since leader is facing East (X+), behind is West (X-).
            // 100 - 5 = 95. Y=100.
            // So (95, 100). Correct.

            Assert.Equal(95f, target.TargetPosition.X, 0.1f);
            Assert.Equal(100f, target.TargetPosition.Y, 0.1f);
            
            // Move follower closer to verify state change
            var closerPos = new Vector3(94.5f, 100f, 0f); // 0.5m dist from target
            // Update follower SimTransform
            var tfFollower = repo.GetComponent<SimTransform>(follower);
            tfFollower.Position = closerPos;
            repo.SetComponent(follower, tfFollower);
            
            system.Execute(repo, 0.016f);
            
            // Check state
            var member = repo.GetComponent<FormationMember>(follower);
            // Should be joined/InSlot
            Assert.Equal(FormationMemberState.InSlot, member.State);

            templateManager.Dispose();
            repo.Dispose();
        }

        [Fact]
        public void GetFormationTarget_FallbackHeading_MatchesXForwardConvention()
        {
            // Setup
            var repo = new EntityRepository();
            repo.RegisterComponent<VehicleState>();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<SimVelocity>();
            repo.RegisterComponent<FormationRoster>();
            repo.RegisterComponent<FormationTarget>();
            repo.RegisterComponent<FormationMember>();
            repo.RegisterComponent<NavState>();
            repo.RegisterComponent<GlobalTime>();

            var timeEntity = repo.CreateEntity();
            repo.AddComponent(timeEntity, new GlobalTime { DeltaTime = 0.1f });

            var templateManager = new FormationTemplateManager();
            var trajectoryPool = new TrajectoryPoolManager();

            var system = new FormationTargetSystem(templateManager, trajectoryPool);

            // 1. Create Leader at (0,0) with Identity rotation (Forward = X+)
            var leader = repo.CreateEntity();
            repo.AddComponent(leader, new VehicleState { Speed = 10f });
            repo.AddComponent(leader, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity // X-Forward
            });

            // 2. Create Follower
            var follower = repo.CreateEntity();
            repo.AddComponent(follower, new VehicleState { Speed = 10f });
            repo.AddComponent(follower, new SimTransform { Position = new Vector3(-10, 0, 0), Rotation = Quaternion.Identity });
            repo.AddComponent(follower, new FormationMember { State = FormationMemberState.InSlot });
            repo.AddComponent(follower, new FormationTarget());

            // 3. Create Roster on Leader
            var roster = new FormationRoster();
            roster.Type = FormationType.Column; 
            
            roster.SetMember(0, leader);
            roster.SetMember(1, follower);
            roster.Count = 2; // Important!

            repo.AddComponent(leader, roster);

            // 4. Run System
            system.Execute(repo, 0.016f);

            // 5. Verify FormationTarget on Follower
            // With Identity rotation, forward is X (1,0).
            var target = repo.GetComponent<FormationTarget>(follower);
            
            Assert.True(MathF.Abs(target.TargetHeading.X - 1f) < 0.001f, $"Expected X ~ 1, got {target.TargetHeading.X}");
            Assert.True(MathF.Abs(target.TargetHeading.Y - 0f) < 0.001f, $"Expected Y ~ 0, got {target.TargetHeading.Y}");
            
            templateManager.Dispose();
            repo.Dispose();
        }
    }
}
