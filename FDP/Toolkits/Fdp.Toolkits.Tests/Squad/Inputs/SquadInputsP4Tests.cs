using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Components;
using Fdp.Toolkit.Utility;
using Xunit;

namespace Fdp.Toolkit.Squad.Tests.Inputs
{
    /// <summary>
    /// Tests for the Phase 4 member-side Utility input readers: AssignedRole, AssignedSlot.
    /// Success criteria: SC-P4-01-1 through SC-P4-01-3.
    /// </summary>
    public unsafe class SquadInputsP4Tests : IDisposable
    {
        private EntityRepository _repo;

        public SquadInputsP4Tests()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<UnitRoster>();
            _repo.RegisterComponent<UnitSubordinate>();
            _repo.RegisterComponent<Blackboard1024>();
            _repo.RegisterComponent<SquadStateMarker>();
            _repo.RegisterComponent<BehaviorState>();
            _repo.RegisterComponent<MovementModeIntent>();

            SquadInputs.RegisterAll();
        }

        public void Dispose()
        {
            UtilityInputReaderStore.Clear();
            _repo.Dispose();
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private Entity CreateCommander()
        {
            var e = _repo.CreateEntity();
            _repo.AddComponent(e, new UnitRoster());
            _repo.AddComponent(e, new Blackboard1024());
            _repo.AddComponent(e, new SquadStateMarker());
            return e;
        }

        private Entity AddMember(Entity commander)
        {
            var m = _repo.CreateEntity();
            _repo.AddComponent(m, new UnitSubordinate { Commander = commander });
            ref var roster = ref _repo.GetComponentRW<UnitRoster>(commander);
            UnitRoster.Add(ref roster, (long)m.PackedValue);
            return m;
        }

        private UtilityInputCtx MakeCtx(Entity self, InputParams @params = default) =>
            new UtilityInputCtx { Repo = _repo, Self = self, Context = Entity.Null, Params = @params };

        // ── SC-P4-01-1: AssignedRole match and non-match ─────────────────────────

        [Fact]
        public void AssignedRole_Match_Returns1()
        {
            var cmd = CreateCommander();
            var member = AddMember(cmd);

            // Set member 0 role to 2 (Suppressor).
            ref var state = ref SquadCognitiveState.Project(
                ref _repo.GetComponentRW<Blackboard1024>(cmd));
            var roleSpan = MemoryMarshal.CreateSpan(
                ref Unsafe.As<RoleAssignmentArray, RoleSlot>(
                    ref Unsafe.AsRef(in state.Roles)), 16);
            roleSpan[0].RoleId = 2;

            float result = SquadInputs.AssignedRole(MakeCtx(member,
                new InputParams { BlueprintId = 2 }));
            Assert.Equal(1f, result);
        }

        [Fact]
        public void AssignedRole_Mismatch_Returns0()
        {
            var cmd = CreateCommander();
            var member = AddMember(cmd);

            // Set member 0 role to 2 (Suppressor).
            ref var state = ref SquadCognitiveState.Project(
                ref _repo.GetComponentRW<Blackboard1024>(cmd));
            var roleSpan = MemoryMarshal.CreateSpan(
                ref Unsafe.As<RoleAssignmentArray, RoleSlot>(
                    ref Unsafe.AsRef(in state.Roles)), 16);
            roleSpan[0].RoleId = 2;

            // Query for RoleId=3 (Flanker) — should not match.
            float result = SquadInputs.AssignedRole(MakeCtx(member,
                new InputParams { BlueprintId = 3 }));
            Assert.Equal(0f, result);
        }

        // ── SC-P4-01-2: Member with no UnitSubordinate returns 0f ────────────────

        [Fact]
        public void AssignedRole_NoUnitSubordinate_Returns0()
        {
            // Entity not in any squad.
            var loner = _repo.CreateEntity();

            float result = SquadInputs.AssignedRole(MakeCtx(loner,
                new InputParams { BlueprintId = 1 }));
            Assert.Equal(0f, result);
        }

        // ── SC-P4-01-3: AssignedSlot match and non-match ─────────────────────────

        [Fact]
        public void AssignedSlot_Match_Returns1()
        {
            var cmd = CreateCommander();
            var member = AddMember(cmd);

            ref var state = ref SquadCognitiveState.Project(
                ref _repo.GetComponentRW<Blackboard1024>(cmd));

            // Set member 0 element index to 1.
            var elemSpan = MemoryMarshal.CreateSpan(
                ref Unsafe.As<MemberElementIndexArray, byte>(
                    ref Unsafe.AsRef(in state.Elements.MemberElements)), 16);
            elemSpan[0] = 1;

            // Set slot 0: ElementIndex=1, SlotKind=2.
            var slotSpan = MemoryMarshal.CreateSpan(
                ref Unsafe.As<SlotAssignmentArray, SlotState>(
                    ref Unsafe.AsRef(in state.Slots)), 12);
            slotSpan[0] = new SlotState { ElementIndex = 1, SlotKind = 2 };

            float result = SquadInputs.AssignedSlot(MakeCtx(member,
                new InputParams { BlueprintId = 2 }));
            Assert.Equal(1f, result);
        }

        [Fact]
        public void AssignedSlot_Mismatch_Returns0()
        {
            var cmd = CreateCommander();
            var member = AddMember(cmd);

            ref var state = ref SquadCognitiveState.Project(
                ref _repo.GetComponentRW<Blackboard1024>(cmd));

            // Set member 0 element index to 1.
            var elemSpan = MemoryMarshal.CreateSpan(
                ref Unsafe.As<MemberElementIndexArray, byte>(
                    ref Unsafe.AsRef(in state.Elements.MemberElements)), 16);
            elemSpan[0] = 1;

            // Set slot 0: ElementIndex=1, SlotKind=2.
            var slotSpan = MemoryMarshal.CreateSpan(
                ref Unsafe.As<SlotAssignmentArray, SlotState>(
                    ref Unsafe.AsRef(in state.Slots)), 12);
            slotSpan[0] = new SlotState { ElementIndex = 1, SlotKind = 2 };

            // Query for SlotKind=3 — should not match.
            float result = SquadInputs.AssignedSlot(MakeCtx(member,
                new InputParams { BlueprintId = 3 }));
            Assert.Equal(0f, result);
        }
    }
}
