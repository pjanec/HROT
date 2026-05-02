using System.Numerics;
using CarKinem.Commands;
using CarKinem.Core;
using CarKinem.Formation;
using CarKinem.Systems;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Xunit;

namespace CarKinem.Tests.Commands
{
    public class VehicleCommandSystemTests
    {
        [Fact]
        public void JoinFormation_SetsFormationFollowerAndMode()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<NavState>();
            repo.RegisterComponent<FormationFollower>();
            repo.RegisterComponent<FormationController>();
            repo.RegisterEvent<CmdJoinFormation>();
            
            var system = new VehicleCommandSystem();
            
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NavState());
            // FormationFollower added dynamically by system if missing
            
            var leader = repo.CreateEntity();
            repo.AddComponent(leader, new FormationController());
            
            var api = new VehicleAPI(repo);
            api.JoinFormation(entity, leaderEntity: leader, slotIndex: 2);
            
            // Playback and Swap
            var cb = ((ISimulationView)repo).GetCommandBuffer();
            ((EntityCommandBuffer)cb).Playback(repo);
            repo.Bus.SwapBuffers();
            
            system.Execute(repo, 0.016f);
            
            var nav = repo.GetComponent<NavState>(entity);
            Assert.Equal(KinematicsMode.Formation, nav.Mode);
            
            Assert.True(repo.HasComponent<FormationFollower>(entity));
            var member = repo.GetComponent<FormationFollower>(entity);
            Assert.Equal(leader, member.LeaderEntity);
            Assert.Equal(2, member.SlotIndex);
            Assert.Equal(FormationMemberState.Rejoining, member.State);
            
            repo.Dispose();
        }

        [Fact]
        public void LeaveFormation_SetsModeToNone()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<NavState>();
            repo.RegisterEvent<CmdLeaveFormation>();
            
            var system = new VehicleCommandSystem();
            
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NavState { Mode = KinematicsMode.Formation });
            
            var api = new VehicleAPI(repo);
            api.LeaveFormation(entity);
            
            // Playback and Swap
            var cb = ((ISimulationView)repo).GetCommandBuffer();
            ((EntityCommandBuffer)cb).Playback(repo);
            repo.Bus.SwapBuffers();
            
            system.Execute(repo, 0.016f);
            
            var nav = repo.GetComponent<NavState>(entity);
            Assert.Equal(KinematicsMode.None, nav.Mode);
            
            repo.Dispose();
        }

        [Fact]
        public void Command_IgnoresDeadEntity()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<NavState>();
            repo.RegisterEvent<CmdLeaveFormation>();
            
            var system = new VehicleCommandSystem();
            
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NavState { Mode = KinematicsMode.Formation });
            var id = entity.Index;
            var gen = entity.Generation;
            
            repo.DestroyEntity(entity);
            
            // Reuse index with new generation (if any)
            // But here we just check checking old handle
            
            var api = new VehicleAPI(repo);
            // Command targeting the DEAD entity
            api.LeaveFormation(entity);
            
            // Playback and Swap
            var cb = ((ISimulationView)repo).GetCommandBuffer();
            ((EntityCommandBuffer)cb).Playback(repo);
            repo.Bus.SwapBuffers();
            
            system.Execute(repo, 0.016f);
            
            // Ideally should not crash and do nothing.
            // Functionally hard to verify "nothing happened" to a dead entity.
            // But we can verify no exception was thrown.
            
            repo.Dispose();
        }
    }
}
