using System.Numerics;
using CarKinem.Commands;
using CarKinem.Core;
using CarKinem.Formation;
using CarKinem.Systems;
using Fdp.Kernel;
using Fdp.ModuleHost_Core.Abstractions;
using Xunit;

namespace CarKinem.Tests.Commands
{
    public class VehicleCommandSystemTests
    {
        [Fact]
        public void JoinFormation_SetsFormationMemberAndMode()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<NavState>();
            repo.RegisterComponent<FormationMember>();
            repo.RegisterComponent<FormationRoster>();
            repo.RegisterEvent<CmdJoinFormation>();
            
            var system = new VehicleCommandSystem();
            system.Create(repo);
            
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NavState());
            // FormationMember added dynamically by system if missing
            
            var leader = repo.CreateEntity();
            repo.AddComponent(leader, new FormationRoster { Count = 1 });
            
            var api = new VehicleAPI(repo);
            api.JoinFormation(entity, leaderEntity: leader, slotIndex: 2);
            
            // Playback and Swap
            var cb = ((ISimulationView)repo).GetCommandBuffer();
            ((EntityCommandBuffer)cb).Playback(repo);
            repo.Bus.SwapBuffers();
            
            system.Run();
            
            var nav = repo.GetComponent<NavState>(entity);
            Assert.Equal(KinematicsMode.Formation, nav.Mode);
            
            Assert.True(repo.HasComponent<FormationMember>(entity));
            var member = repo.GetComponent<FormationMember>(entity);
            Assert.Equal(leader.Index, member.LeaderEntityId);
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
            system.Create(repo);
            
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NavState { Mode = KinematicsMode.Formation });
            
            var api = new VehicleAPI(repo);
            api.LeaveFormation(entity);
            
            // Playback and Swap
            var cb = ((ISimulationView)repo).GetCommandBuffer();
            ((EntityCommandBuffer)cb).Playback(repo);
            repo.Bus.SwapBuffers();
            
            system.Run();
            
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
            system.Create(repo);
            
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
            
            system.Run();
            
            // Ideally should not crash and do nothing.
            // Functionally hard to verify "nothing happened" to a dead entity.
            // But we can verify no exception was thrown.
            
            repo.Dispose();
        }
    }
}
