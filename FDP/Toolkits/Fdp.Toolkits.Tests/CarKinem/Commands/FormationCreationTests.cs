using CarKinem.Commands;
using CarKinem.Core;
using CarKinem.Formation;
using CarKinem.Systems;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Xunit;

namespace CarKinem.Tests.Commands
{
    public class FormationCreationTests
    {
        /// <summary>⭐ CE-1034 H3 (TH-F) — a 2-D spawn command puts the vehicle on the ground THERE, never at Z = 0 (R-248).</summary>
        [Fact]
        public void H3_ASpawnCommandOnASlope_PutsTheVehicleOnTheGround()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<VehicleState>();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<SimVelocity>();
            repo.RegisterComponent<VehicleParams>();
            repo.RegisterComponent<NavState>();
            repo.RegisterEvent<CmdSpawnVehicle>();
            repo.RegisterManagedComponent<Fdp.Toolkit.Terrain.TerrainWorld>();
            var world = Fdp.Toolkit.Terrain.Tests.SlopeFixture.World();
            repo.SetSingletonManaged(world);
            var e = repo.CreateEntity();
            repo.Bus.Publish(new CmdSpawnVehicle { Entity = e, Position = new System.Numerics.Vector2(50f, 150f), Heading = new System.Numerics.Vector2(1, 0), Class = VehicleClass.PersonalCar });
            repo.Bus.SwapBuffers();
            new VehicleCommandSystem().Execute(repo, 0.016f);
            Assert.Equal(world.GroundHeightAt(50f, 150f), repo.GetComponent<SimTransform>(e).Position.Z, 3);
            Assert.Equal(world.GroundHeightAt(50f, 150f), repo.GetComponent<NavState>(e).FinalDestination.Z, 3);
            repo.Dispose();
        }

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
            repo.RegisterEvent<CmdAssignSubordinate>();
            repo.RegisterEvent<CmdRemoveSubordinate>();
            repo.RegisterEvent<CmdAssignSubordinateRejected>();
            
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
            
            // Verify follower: VehicleCommandSystem now publishes CmdAssignSubordinate
            // instead of writing FormationFollower directly (UnitHierarchySystem handles that).
            // Check that the subordinate intent was published with correct slot index.
            repo.Bus.SwapBuffers();
            var assigns = repo.Bus.Read<CmdAssignSubordinate>();
            Assert.Equal(1, assigns.Length);
            Assert.Equal(followerEntity, assigns[0].Subordinate);
            Assert.Equal(leaderEntity, assigns[0].Commander);
            Assert.Equal((ushort)1, assigns[0].SlotIndex);
            
            repo.Dispose();
        }
    }
}
