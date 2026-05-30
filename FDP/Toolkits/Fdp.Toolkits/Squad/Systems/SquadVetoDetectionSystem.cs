using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Primitives;

namespace Fdp.Toolkit.Squad.Systems
{
    /// <summary>
    /// Detects when a squad member's active behavior diverges from the squad leader's
    /// assignment for more than <see cref="_vetoConfirmTicks"/> consecutive ticks,
    /// then emits a <see cref="PhaseEvent"/> with kind <see cref="PhaseEventKind.VetoDetected"/>.
    /// </summary>
    /// <remarks>
    /// Veto is emitted into a caller-provided event list (zero allocation on the hot path).
    /// Hysteresis: a single-tick divergence does NOT trigger a veto; the divergence must
    /// persist for at least <see cref="_vetoConfirmTicks"/> consecutive ticks.
    /// </remarks>
    public unsafe sealed class SquadVetoDetectionSystem
    {
        private readonly uint _vetoConfirmTicks;
        // Per-member divergence counter (roster-slot indexed, max 16 members).
        private VetoCounterArray _vetoCounters;

        [InlineArray(16)]
        private struct VetoCounterArray
        {
#pragma warning disable CS0169
            private byte _element;
#pragma warning restore CS0169
        }

        public SquadVetoDetectionSystem(uint vetoConfirmTicks = 3)
        {
            _vetoConfirmTicks = vetoConfirmTicks;
        }

        /// <param name="repo">Active ECS repository.</param>
        /// <param name="commander">Commander entity (must have UnitRoster and Blackboard1024).</param>
        /// <param name="expectedHashByRole">
        /// Mapping from RoleId (byte, 0-based) to the expected BehaviorState.ActiveBehaviorHash.
        /// Index = RoleId; 0 = unassigned (always no-veto).
        /// The caller provides this at the maneuver level so the system stays generic.
        /// </param>
        /// <param name="vetoEvents">
        /// Output list; receives one <see cref="PhaseEvent"/> per vetoDetected member per call.
        /// Caller must pre-clear if desired.
        /// </param>
        public void Run(
            EntityRepository repo,
            Entity commander,
            ReadOnlySpan<int> expectedHashByRole,
            IList<(int memberSlot, PhaseEvent evt)> vetoEvents)
        {
            if (!repo.HasComponent<UnitRoster>(commander)) return;
            if (!repo.HasComponent<Blackboard1024>(commander)) return;

            ref readonly var state = ref SquadCognitiveState.Project(
                ref repo.GetComponentRW<Blackboard1024>(commander));

            var roleSpan = MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<RoleAssignmentArray, RoleSlot>(
                    ref Unsafe.AsRef(in state.Roles)), 16);

            var counterSpan = MemoryMarshal.CreateSpan(
                ref Unsafe.As<VetoCounterArray, byte>(ref _vetoCounters), 16);

            ref readonly var roster = ref repo.GetComponentRO<UnitRoster>(commander);
            for (int m = 0; m < roster.Count; m++)
            {
                byte roleId = roleSpan[m].RoleId;

                // Unassigned role: reset and skip.
                if (roleId == 0) { counterSpan[m] = 0; continue; }

                // Out-of-range role: reset and skip.
                if (roleId >= expectedHashByRole.Length) { counterSpan[m] = 0; continue; }

                int expectedHash = expectedHashByRole[roleId];

                var member = new Entity((ulong)roster.SubordinateEntities[m]);
                if (!repo.HasComponent<BehaviorState>(member)) { counterSpan[m] = 0; continue; }

                int actualHash = repo.GetComponentRO<BehaviorState>(member).ActiveBehaviorHash;

                if (actualHash != expectedHash)
                {
                    // Increment counter, cap at 255 to avoid overflow.
                    if (counterSpan[m] < 255) counterSpan[m]++;
                    if (counterSpan[m] >= _vetoConfirmTicks)
                        vetoEvents.Add((m, new PhaseEvent(PhaseEventKind.VetoDetected)));
                }
                else
                {
                    counterSpan[m] = 0;
                }
            }
        }
    }
}
