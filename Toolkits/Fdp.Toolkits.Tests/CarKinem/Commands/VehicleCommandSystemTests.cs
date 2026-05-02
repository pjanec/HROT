using System.Numerics;
using CarKinem.Commands;
using CarKinem.Core;
using CarKinem.Formation;
using CarKinem.Systems;
using Fbt;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Xunit;

namespace CarKinem.Tests.Commands
{
    public class VehicleCommandSystemTests
    {
        [Fact]
        public void JoinFormation_PublishesCmdAssignSubordinate_NotFormationFollower()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<NavState>();
            repo.RegisterComponent<FormationFollower>();
            repo.RegisterComponent<FormationController>();
            repo.RegisterEvent<CmdJoinFormation>();
            repo.RegisterEvent<CmdAssignSubordinate>();
            repo.RegisterEvent<CmdRemoveSubordinate>();
            repo.RegisterEvent<CmdAssignSubordinateRejected>();

            var system = new VehicleCommandSystem();

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NavState());

            var leader = repo.CreateEntity();
            repo.AddComponent(leader, new FormationController());

            var api = new VehicleAPI(repo);
            api.JoinFormation(entity, leaderEntity: leader, slotIndex: 2);

            // Playback and Swap
            var cb = ((ISimulationView)repo).GetCommandBuffer();
            ((EntityCommandBuffer)cb).Playback(repo);
            repo.Bus.SwapBuffers();

            system.Execute(repo, 0.016f);

            // NavState.Mode should be set to Formation by VehicleCommandSystem
            var nav = repo.GetComponent<NavState>(entity);
            Assert.Equal(KinematicsMode.Formation, nav.Mode);

            // FormationFollower must NOT be added directly (UnitHierarchySystem handles it)
            Assert.False(repo.HasComponent<FormationFollower>(entity));

            // CmdAssignSubordinate must be published with correct fields
            repo.Bus.SwapBuffers();
            var assigns = repo.Bus.Read<CmdAssignSubordinate>();
            Assert.Equal(1, assigns.Length);
            var assign = assigns[0];
            Assert.Equal(entity, assign.Subordinate);
            Assert.Equal(leader, assign.Commander);
            Assert.Equal((byte)1, assign.HasFormationSlot);
            Assert.Equal((ushort)2, assign.SlotIndex);

            repo.Dispose();
        }

        [Fact]
        public void LeaveFormation_PublishesCmdRemoveSubordinate_SetsModeToNone()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<NavState>();
            repo.RegisterComponent<FormationFollower>();
            repo.RegisterComponent<UnitSubordinate>();
            repo.RegisterEvent<CmdLeaveFormation>();
            repo.RegisterEvent<CmdAssignSubordinate>();
            repo.RegisterEvent<CmdRemoveSubordinate>();
            repo.RegisterEvent<CmdAssignSubordinateRejected>();

            var system = new VehicleCommandSystem();

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NavState { Mode = KinematicsMode.Formation });

            // Add FormationFollower and UnitSubordinate to simulate an assigned subordinate
            repo.AddComponent(entity, new FormationFollower { IsInFormation = 1 });
            var commander = repo.CreateEntity();
            repo.AddComponent(entity, new UnitSubordinate { Commander = commander });

            var api = new VehicleAPI(repo);
            api.LeaveFormation(entity);

            // Playback and Swap
            var cb = ((ISimulationView)repo).GetCommandBuffer();
            ((EntityCommandBuffer)cb).Playback(repo);
            repo.Bus.SwapBuffers();

            system.Execute(repo, 0.016f);

            // NavState.Mode should be reset to None
            var nav = repo.GetComponent<NavState>(entity);
            Assert.Equal(KinematicsMode.None, nav.Mode);

            // FormationFollower still present (UnitHierarchySystem removes it, not VehicleCommandSystem)
            Assert.True(repo.HasComponent<FormationFollower>(entity));

            // CmdRemoveSubordinate must be published
            repo.Bus.SwapBuffers();
            var removes = repo.Bus.Read<CmdRemoveSubordinate>();
            Assert.Equal(1, removes.Length);
            Assert.Equal(entity, removes[0].Subordinate);

            repo.Dispose();
        }

        [Fact]
        public void RejectedSubordinate_SetsLocomotionFailure()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<NavState>();
            repo.RegisterComponent<LocomotionChannel>();
            repo.RegisterEvent<CmdAssignSubordinate>();
            repo.RegisterEvent<CmdRemoveSubordinate>();
            repo.RegisterEvent<CmdAssignSubordinateRejected>();

            var system = new VehicleCommandSystem();

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NavState());
            repo.AddComponent(entity, new LocomotionChannel { Status = NodeStatus.Running });

            // Publish rejection directly (as if UnitHierarchySystem published it)
            repo.Bus.Publish(new CmdAssignSubordinateRejected { Subordinate = entity });
            repo.Bus.SwapBuffers();

            system.Execute(repo, 0.016f);

            var channel = repo.GetComponent<LocomotionChannel>(entity);
            Assert.Equal(NodeStatus.Failure, channel.Status);

            repo.Dispose();
        }

        [Fact]
        public void LeaveFormation_SetsModeToNone()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<NavState>();
            repo.RegisterEvent<CmdLeaveFormation>();
            repo.RegisterEvent<CmdAssignSubordinate>();
            repo.RegisterEvent<CmdRemoveSubordinate>();
            repo.RegisterEvent<CmdAssignSubordinateRejected>();

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
            repo.RegisterEvent<CmdAssignSubordinate>();
            repo.RegisterEvent<CmdRemoveSubordinate>();
            repo.RegisterEvent<CmdAssignSubordinateRejected>();

            var system = new VehicleCommandSystem();

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NavState { Mode = KinematicsMode.Formation });

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
