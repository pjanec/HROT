using CarKinem.Commands;
using CarKinem.Core;
using CarKinem.Formation;
using CarKinem.Systems;
using Fdp.Core;
using Xunit;

namespace CarKinem.Tests.Commands
{
    public class FormationCreationTests
    {
        [Fact]
        public void CreateFormation_AddsControllerToLeader()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<FormationController>();
            repo.RegisterEvent<CmdCreateFormation>();
            
            var system = new VehicleCommandSystem();
            
            var leaderEntity = repo.CreateEntity();
            
            // Create formation
            repo.Bus.Publish(new CmdCreateFormation
            {
                LeaderEntity = leaderEntity,
                Type = FormationType.Column,
                Params = new FormationParams
                {
                    Spacing = 5.0f,
                    MaxCatchUpFactor = 1.2f,
                    BreakDistance = 50.0f,
                    ArrivalThreshold = 2.0f
                }
            });
            
            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.016f);
            
            // Verify controller
            Assert.True(repo.HasComponent<FormationController>(leaderEntity));
            var controller = repo.GetComponent<FormationController>(leaderEntity);
            Assert.Equal(FormationType.Column, controller.Type);
            Assert.Equal(5.0f, controller.Params.Spacing);
            
            repo.Dispose();
        }
        
        [Fact]
        public void JoinFormation_AddsFollowerComponent()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<VehicleState>();
            repo.RegisterComponent<NavState>();
            repo.RegisterComponent<FormationFollower>();
            repo.RegisterComponent<FormationController>();
            repo.RegisterEvent<CmdCreateFormation>();
            repo.RegisterEvent<CmdJoinFormation>();
            
            var system = new VehicleCommandSystem();
            
            var leaderEntity = repo.CreateEntity();
            repo.AddComponent(leaderEntity, new VehicleState());
            repo.AddComponent(leaderEntity, new NavState());
            
            var followerEntity = repo.CreateEntity();
            repo.AddComponent(followerEntity, new VehicleState());
            repo.AddComponent(followerEntity, new NavState());
            
            // Create formation
            repo.Bus.Publish(new CmdCreateFormation
            {
                LeaderEntity = leaderEntity,
                Type = FormationType.Column,
                Params = new FormationParams { Spacing = 5.0f }
            });
            
            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.016f);
            
            // Join formation
            repo.Bus.Publish(new CmdJoinFormation
            {
                Entity = followerEntity,
                LeaderEntity = leaderEntity,
                SlotIndex = 1
            });
            
            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.016f);
            
            // Verify follower
            Assert.True(repo.HasComponent<FormationFollower>(followerEntity));
            var member = repo.GetComponent<FormationFollower>(followerEntity);
            Assert.Equal(leaderEntity, member.LeaderEntity);
            Assert.Equal(1, member.SlotIndex);
            
            repo.Dispose();
        }
    }
}
