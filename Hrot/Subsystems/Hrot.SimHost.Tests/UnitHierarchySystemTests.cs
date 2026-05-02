using CarKinem.Formation;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Lifecycle.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Utilities;
using Hrot.Common.Systems;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="UnitHierarchySystem"/> (CS016).
    ///
    /// <para>Naming convention: <c>Method_Scenario_ExpectedResult</c>.</para>
    ///
    /// <para>All tests that verify <see cref="SmartEgressUtil"/> dirty marking register
    /// <see cref="EgressPublicationState"/> and check that the ordinal <c>1L</c>
    /// (EntityInfo descriptor) is in <see cref="EgressPublicationState.DirtyDescriptors"/>
    /// after the system tick.</para>
    /// </summary>
    public class UnitHierarchySystemTests
    {
        // ── Shared helpers ────────────────────────────────────────────────────

        /// <summary>Creates a minimal repo with all components and events required by UnitHierarchySystem.</summary>
        private static EntityRepository CreateRepo()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<UnitRoster>();
            repo.RegisterComponent<UnitSubordinate>();
            repo.RegisterComponent<FormationFollower>();
            repo.RegisterEvent<CmdAssignSubordinate>();
            repo.RegisterEvent<CmdRemoveSubordinate>();
            repo.RegisterEvent<CmdAssignSubordinateRejected>();
            repo.RegisterEvent<DestructionOrder>();
            repo.RegisterManagedComponent<EgressPublicationState>();
            return repo;
        }

        private static void Tick(EntityRepository repo, float dt = 0.016f)
        {
            repo.Bus.SwapBuffers();
            new UnitHierarchySystem().Execute(repo, dt);
        }

        // ── CS016-T01 ─────────────────────────────────────────────────────────

        /// <summary>
        /// After assigning a subordinate, both UnitSubordinate and UnitRoster must be written
        /// atomically: UnitSubordinate.Commander is set, and UnitRoster.Count == 1 with the
        /// subordinate's packed value stored at index 0.
        /// </summary>
        [Fact]
        public void Assign_AtomicTwoWrite_UnitSubordinateAndRosterBothSet()
        {
            using var repo = CreateRepo();
            var cmd = repo.CreateEntity();
            var sub = repo.CreateEntity();

            repo.Bus.Publish(new CmdAssignSubordinate
            {
                Subordinate = sub,
                Commander   = cmd,
                Designation = TacticalDesignation.Wingman,
            });

            Tick(repo);

            Assert.True(repo.HasComponent<UnitSubordinate>(sub));
            var subComp = repo.GetComponent<UnitSubordinate>(sub);
            Assert.Equal(cmd, subComp.Commander);
            Assert.Equal(TacticalDesignation.Wingman, subComp.Designation);

            Assert.True(repo.HasComponent<UnitRoster>(cmd));
            unsafe
            {
                var roster = repo.GetComponent<UnitRoster>(cmd);
                Assert.Equal(1, roster.Count);
                Assert.Equal((long)sub.PackedValue, roster.SubordinateEntities[0]);
            }
        }

        // ── CS016-T02 ─────────────────────────────────────────────────────────

        /// <summary>
        /// Assigning three subordinates must preserve insertion order in the roster.
        /// </summary>
        [Fact]
        public void Assign_MultipleSubordinates_RosterOrderPreserved()
        {
            using var repo = CreateRepo();
            var cmd = repo.CreateEntity();
            var a   = repo.CreateEntity();
            var b   = repo.CreateEntity();
            var c   = repo.CreateEntity();

            repo.Bus.Publish(new CmdAssignSubordinate { Subordinate = a, Commander = cmd });
            repo.Bus.Publish(new CmdAssignSubordinate { Subordinate = b, Commander = cmd });
            repo.Bus.Publish(new CmdAssignSubordinate { Subordinate = c, Commander = cmd });

            Tick(repo);

            var roster = repo.GetComponent<UnitRoster>(cmd);
            Assert.Equal(3, roster.Count);
            unsafe
            {
                Assert.Equal((long)a.PackedValue, roster.SubordinateEntities[0]);
                Assert.Equal((long)b.PackedValue, roster.SubordinateEntities[1]);
                Assert.Equal((long)c.PackedValue, roster.SubordinateEntities[2]);
            }
        }

        // ── CS016-T03 ─────────────────────────────────────────────────────────

        /// <summary>
        /// Reassigning a subordinate to a different commander must remove it from the old
        /// roster and add it to the new one.
        /// </summary>
        [Fact]
        public void Assign_Reassign_MovesFromOldToNewCommander()
        {
            using var repo = CreateRepo();
            var cmd1 = repo.CreateEntity();
            var cmd2 = repo.CreateEntity();
            var sub  = repo.CreateEntity();

            repo.Bus.Publish(new CmdAssignSubordinate { Subordinate = sub, Commander = cmd1 });
            Tick(repo);

            repo.Bus.Publish(new CmdAssignSubordinate { Subordinate = sub, Commander = cmd2 });
            Tick(repo);

            // Old commander's roster must be empty
            var roster1 = repo.GetComponent<UnitRoster>(cmd1);
            Assert.Equal(0, roster1.Count);

            // New commander's roster must contain sub
            var roster2 = repo.GetComponent<UnitRoster>(cmd2);
            Assert.Equal(1, roster2.Count);
            unsafe
            {
                Assert.Equal((long)sub.PackedValue, roster2.SubordinateEntities[0]);
            }

            // UnitSubordinate must point at cmd2
            Assert.Equal(cmd2, repo.GetComponent<UnitSubordinate>(sub).Commander);
        }

        // ── CS016-T04 ─────────────────────────────────────────────────────────

        /// <summary>
        /// Removing the middle subordinate from [A, B, C] must produce [A, C] with the last
        /// slot zeroed and Count decremented.
        /// </summary>
        [Fact]
        public void Remove_MiddleEntry_OrderPreservingShift()
        {
            using var repo = CreateRepo();
            var cmd = repo.CreateEntity();
            var a   = repo.CreateEntity();
            var b   = repo.CreateEntity();
            var c   = repo.CreateEntity();

            repo.Bus.Publish(new CmdAssignSubordinate { Subordinate = a, Commander = cmd });
            repo.Bus.Publish(new CmdAssignSubordinate { Subordinate = b, Commander = cmd });
            repo.Bus.Publish(new CmdAssignSubordinate { Subordinate = c, Commander = cmd });
            Tick(repo);

            repo.Bus.Publish(new CmdRemoveSubordinate { Subordinate = b });
            Tick(repo);

            var roster = repo.GetComponent<UnitRoster>(cmd);
            Assert.Equal(2, roster.Count);
            unsafe
            {
                Assert.Equal((long)a.PackedValue, roster.SubordinateEntities[0]);
                Assert.Equal((long)c.PackedValue, roster.SubordinateEntities[1]);
                Assert.Equal(0L, roster.SubordinateEntities[2]); // last slot zeroed
            }

            // b must have lost its UnitSubordinate
            Assert.False(repo.HasComponent<UnitSubordinate>(b));
        }

        // ── CS016-T05 ─────────────────────────────────────────────────────────

        /// <summary>
        /// The 17th CmdAssignSubordinate (exceeding the 16-slot capacity) must not write
        /// UnitSubordinate or update the roster count.
        /// </summary>
        [Fact]
        public void Assign_CapacityExceeded_NoPartialWrite()
        {
            using var repo = CreateRepo();
            var cmd = repo.CreateEntity();

            // Fill roster to capacity
            for (int i = 0; i < UnitRoster.Capacity; i++)
            {
                var s = repo.CreateEntity();
                repo.Bus.Publish(new CmdAssignSubordinate { Subordinate = s, Commander = cmd });
            }
            Tick(repo);

            var rosterBefore = repo.GetComponent<UnitRoster>(cmd);
            Assert.Equal(UnitRoster.Capacity, rosterBefore.Count);

            // Try to assign 17th
            var overflow = repo.CreateEntity();
            repo.Bus.Publish(new CmdAssignSubordinate { Subordinate = overflow, Commander = cmd });
            Tick(repo);

            // Count must remain at capacity
            var rosterAfter = repo.GetComponent<UnitRoster>(cmd);
            Assert.Equal(UnitRoster.Capacity, rosterAfter.Count);

            // UnitSubordinate must NOT be added
            Assert.False(repo.HasComponent<UnitSubordinate>(overflow));
        }

        // ── CS016-T06 ─────────────────────────────────────────────────────────

        /// <summary>
        /// When a commander is destroyed, all subordinates must have UnitSubordinate removed
        /// and be marked dirty.
        /// </summary>
        [Fact]
        public void Destruction_Commander_ReleasesAllSubordinatesAndMarksDirty()
        {
            using var repo = CreateRepo();
            var cmd = repo.CreateEntity();
            var s1  = repo.CreateEntity();
            var s2  = repo.CreateEntity();
            var s3  = repo.CreateEntity();

            repo.Bus.Publish(new CmdAssignSubordinate { Subordinate = s1, Commander = cmd });
            repo.Bus.Publish(new CmdAssignSubordinate { Subordinate = s2, Commander = cmd });
            repo.Bus.Publish(new CmdAssignSubordinate { Subordinate = s3, Commander = cmd });
            Tick(repo);

            // Simulate commander destruction
            repo.Bus.Publish(new DestructionOrder { Entity = cmd });
            Tick(repo);

            foreach (var s in new[] { s1, s2, s3 })
            {
                Assert.False(repo.HasComponent<UnitSubordinate>(s), $"Subordinate {s.Index} must have no UnitSubordinate after commander destroyed");

                // Dirty mark: EgressPublicationState must contain ordinal 1L
                Assert.True(
                    ((ISimulationView)repo).HasManagedComponent<EgressPublicationState>(s)
                    && ((ISimulationView)repo).GetManagedComponentRO<EgressPublicationState>(s).DirtyDescriptors.Contains(1L),
                    $"Subordinate {s.Index} must be marked dirty (ordinal 1L) after commander destruction");
            }
        }

        // ── CS016-T07 ─────────────────────────────────────────────────────────

        /// <summary>
        /// When a subordinate is destroyed, it must be removed from the commander's roster.
        /// </summary>
        [Fact]
        public void Destruction_Subordinate_RemovedFromCommanderRoster()
        {
            using var repo = CreateRepo();
            var cmd = repo.CreateEntity();
            var s1  = repo.CreateEntity();
            var s2  = repo.CreateEntity();

            repo.Bus.Publish(new CmdAssignSubordinate { Subordinate = s1, Commander = cmd });
            repo.Bus.Publish(new CmdAssignSubordinate { Subordinate = s2, Commander = cmd });
            Tick(repo);

            // Simulate s1 destruction
            repo.Bus.Publish(new DestructionOrder { Entity = s1 });
            Tick(repo);

            var roster = repo.GetComponent<UnitRoster>(cmd);
            Assert.Equal(1, roster.Count);
            unsafe
            {
                Assert.Equal((long)s2.PackedValue, roster.SubordinateEntities[0]);
                Assert.Equal(0L, roster.SubordinateEntities[1]);
            }
        }

        // ── CS016-T08 ─────────────────────────────────────────────────────────

        /// <summary>
        /// Successful assignment must mark the subordinate dirty so its EntityInfo is re-broadcast.
        /// </summary>
        [Fact]
        public void Assign_Success_MarksSubordinateDirty()
        {
            using var repo = CreateRepo();
            var cmd = repo.CreateEntity();
            var sub = repo.CreateEntity();

            repo.Bus.Publish(new CmdAssignSubordinate { Subordinate = sub, Commander = cmd });
            Tick(repo);

            var view = (ISimulationView)repo;
            Assert.True(view.HasManagedComponent<EgressPublicationState>(sub));
            Assert.True(
                view.GetManagedComponentRO<EgressPublicationState>(sub).DirtyDescriptors.Contains(1L));
        }

        // ── CS016-T09 ─────────────────────────────────────────────────────────

        /// <summary>
        /// <see cref="FormationFollower"/> must be removed when a subordinate is removed
        /// from the hierarchy.
        /// </summary>
        [Fact]
        public void Remove_AlsoRemovesFormationFollower()
        {
            using var repo = CreateRepo();
            var cmd = repo.CreateEntity();
            var sub = repo.CreateEntity();

            repo.Bus.Publish(new CmdAssignSubordinate
            {
                Subordinate    = sub,
                Commander      = cmd,
                HasFormationSlot = 1,
                SlotIndex      = 1,
            });
            Tick(repo);

            Assert.True(repo.HasComponent<FormationFollower>(sub));

            repo.Bus.Publish(new CmdRemoveSubordinate { Subordinate = sub });
            Tick(repo);

            Assert.False(repo.HasComponent<FormationFollower>(sub));
        }

        // ── CS016-T10 ─────────────────────────────────────────────────────────

        /// <summary>
        /// When <c>HasFormationSlot == 1</c>, <see cref="FormationFollower"/> must be written
        /// atomically alongside <see cref="UnitSubordinate"/>. The SlotIndex must match.
        /// </summary>
        [Fact]
        public void Assign_WithFormationSlot_FormationFollowerWritten()
        {
            using var repo = CreateRepo();
            var cmd = repo.CreateEntity();
            var sub = repo.CreateEntity();

            repo.Bus.Publish(new CmdAssignSubordinate
            {
                Subordinate    = sub,
                Commander      = cmd,
                HasFormationSlot = 1,
                SlotIndex      = 3,
            });
            Tick(repo);

            Assert.True(repo.HasComponent<UnitSubordinate>(sub));
            Assert.True(repo.HasComponent<FormationFollower>(sub));
            var ff = repo.GetComponent<FormationFollower>(sub);
            Assert.Equal((ushort)3, ff.SlotIndex);
            Assert.Equal(FormationMemberState.Rejoining, ff.State);
        }

        // ── CS016-T11 ─────────────────────────────────────────────────────────

        /// <summary>
        /// When the roster is at capacity and <c>HasFormationSlot == 1</c>, neither
        /// <see cref="UnitSubordinate"/> nor <see cref="FormationFollower"/> must be written.
        /// </summary>
        [Fact]
        public void Assign_CapacityExceededWithFormationSlot_NeitherComponentWritten()
        {
            using var repo = CreateRepo();
            var cmd = repo.CreateEntity();

            for (int i = 0; i < UnitRoster.Capacity; i++)
            {
                var s = repo.CreateEntity();
                repo.Bus.Publish(new CmdAssignSubordinate { Subordinate = s, Commander = cmd });
            }
            Tick(repo);

            var overflow = repo.CreateEntity();
            repo.Bus.Publish(new CmdAssignSubordinate
            {
                Subordinate    = overflow,
                Commander      = cmd,
                HasFormationSlot = 1,
                SlotIndex      = 0,
            });
            Tick(repo);

            Assert.False(repo.HasComponent<UnitSubordinate>(overflow));
            Assert.False(repo.HasComponent<FormationFollower>(overflow));
        }

        // ── CS016-T12 ─────────────────────────────────────────────────────────

        /// <summary>
        /// When capacity is exceeded, <see cref="CmdAssignSubordinateRejected"/> must be
        /// published on the event bus.
        /// </summary>
        [Fact]
        public void Assign_CapacityExceeded_PublishesCmdAssignSubordinateRejected()
        {
            using var repo = CreateRepo();
            var cmd = repo.CreateEntity();

            for (int i = 0; i < UnitRoster.Capacity; i++)
            {
                var s = repo.CreateEntity();
                repo.Bus.Publish(new CmdAssignSubordinate { Subordinate = s, Commander = cmd });
            }
            Tick(repo);

            var overflow = repo.CreateEntity();
            repo.Bus.Publish(new CmdAssignSubordinate { Subordinate = overflow, Commander = cmd });
            Tick(repo);

            // The system writes CmdAssignSubordinateRejected into the write buffer.
            // SwapBuffers makes it readable.
            repo.Bus.SwapBuffers();
            var rejections = repo.Bus.Read<CmdAssignSubordinateRejected>();
            Assert.Equal(1, rejections.Length);
            Assert.Equal(overflow, rejections[0].Subordinate);
        }
    }
}
