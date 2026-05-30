using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.DangerArea;
using Fdp.Toolkit.Utility;
using FixedString32 = Fdp.Toolkit.Diagnostics.Gizmos.FixedString32;

namespace Hrot.Diagnostics.Overlays
{
    /// <summary>
    /// Emits squad coordination overlay primitives for entities with
    /// <see cref="AiOverlayFlags.SquadAssignment"/> set in their <see cref="DebugState.Ai"/> field.
    /// Surfaces maneuver state: per-member element coloring + role labels (P7-01),
    /// assignment-vs-actual divergence lines + veto labels (P7-02),
    /// and phase label + dwell-entry tick + merged contact-pool markers (P7-03).
    /// Requires both <see cref="UnitRoster"/> and <see cref="Blackboard1024"/> on the commander.
    /// </summary>
    internal sealed unsafe class SquadCoordinationOverlaySource : IGizmoSource
    {
        private readonly EntityRepository _repo;
        private readonly OverlayBudgetArbiter _budget;

        // P7-01 element color palette indexed by MemberElementIndex (wraps at 4).
        private static readonly Rgba32[] s_elementColors = new Rgba32[]
        {
            new Rgba32(0x40, 0x80, 0xFF, 0xCC),  // 0: blue
            new Rgba32(0xFF, 0x40, 0x40, 0xCC),  // 1: red
            new Rgba32(0x40, 0xFF, 0x40, 0xCC),  // 2: green
            new Rgba32(0xFF, 0xFF, 0x00, 0xCC),  // 3: yellow
        };

        private static readonly Rgba32 s_assignColor = new Rgba32(0x00, 0xFF, 0x00, 0xCC);
        private static readonly Rgba32 s_vetoColor   = new Rgba32(0xFF, 0x80, 0x00, 0xCC);
        private static readonly Rgba32 s_vetoText    = new Rgba32(0xFF, 0xCC, 0x00, 0xCC);
        private static readonly Rgba32 s_obbColor    = new Rgba32(0xFF, 0x80, 0x00, 0xCC);
        private static readonly Rgba32 s_phaseColor  = new Rgba32(0xFF, 0xFF, 0xFF, 0xCC);
        private static readonly Rgba32 s_contactColor = new Rgba32(0xFF, 0x40, 0xFF, 0xCC);

        public SquadCoordinationOverlaySource(EntityRepository repo, OverlayBudgetArbiter budget)
        {
            _repo   = repo;
            _budget = budget;
        }

        public void Emit(float deltaTime, IGizmoDrawBuilder draw)
        {
            if (!_budget.IsPermitted(AiOverlayFlags.SquadAssignment)) return;

            var q = _repo.Query().With<DebugState>().Build();
            foreach (var entity in q)
            {
                ref readonly var ds = ref _repo.GetComponentRO<DebugState>(entity);
                if ((ds.Ai & AiOverlayFlags.SquadAssignment) == 0) continue;

                EmitForCommander(entity, draw);
            }
        }

        private void EmitForCommander(Entity commander, IGizmoDrawBuilder draw)
        {
            if (!_repo.HasComponent<UnitRoster>(commander)) return;
            if (!_repo.HasComponent<Blackboard1024>(commander)) return;

            ref var state  = ref SquadCognitiveState.Project(
                ref _repo.GetComponentRW<Blackboard1024>(commander));
            ref var roster = ref _repo.GetComponentRW<UnitRoster>(commander);

            EmitMemberOverlays(draw, ref state, ref roster);
            EmitDangerAreaObb(commander, draw, ref state);
            EmitPhaseAndContacts(draw, ref state);
        }

        // P7-01: per-member element color label + role label.
        // P7-02: solid assignment line + optional dashed veto line and label.
        private void EmitMemberOverlays(IGizmoDrawBuilder draw,
            ref SquadCognitiveState state, ref UnitRoster roster)
        {
            var memberElements = MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<MemberElementIndexArray, byte>(
                    ref Unsafe.AsRef(in state.Elements.MemberElements)), 16);
            var roles = MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<RoleAssignmentArray, RoleSlot>(
                    ref Unsafe.AsRef(in state.Roles)), 16);

            int count = roster.Count;
            for (int i = 0; i < count; i++)
            {
                byte elemIdx = memberElements[i];
                Rgba32 color = s_elementColors[elemIdx % 4];
                byte roleId = roles[i].RoleId;

                // P7-01: element-color label per member
                draw.DrawText(0f, 0f, new FixedString32($"E{elemIdx}R{roleId}"), color);

                // P7-02: solid assignment line (always)
                draw.DrawLine(Vector3.Zero, Vector3.Zero, s_assignColor, style: LineStyle.Solid);

                // Resolve the member entity from the roster fixed array
                Entity member;
                long packedHandle = roster.SubordinateEntities[i];
                member = new Entity((ulong)packedHandle);

                // P7-02: dashed veto line + label when member's utility trace has records
                if (_repo.HasComponent<UtilityTraceWorkingMemory1024>(member))
                {
                    ref readonly var mem = ref _repo.GetComponentRO<UtilityTraceWorkingMemory1024>(member);
                    if (mem.RecordCount > 0)
                    {
                        // Copy to mutable local (LatestSelected is non-readonly)
                        var memCopy = mem;
                        var result  = memCopy.LatestSelected();

                        draw.DrawLine(Vector3.Zero, Vector3.Zero, s_vetoColor, style: LineStyle.Dashed);
                        draw.DrawTextLong(0f, 0f, $"VETO:{result.OptionId}", s_vetoText);
                    }
                }
            }
        }

        // P7-01: danger-area OBB as 12 DrawLine calls (bottom + top + 4 verticals).
        private void EmitDangerAreaObb(Entity commander, IGizmoDrawBuilder draw,
            ref SquadCognitiveState state)
        {
            if (state.ActiveFeatureId == 0) return;
            if (!_repo.HasComponent<DangerAreaCognitiveBuffer>(commander)) return;

            ref readonly var buf = ref _repo.GetComponentRO<DangerAreaCognitiveBuffer>(commander);
            var span = buf.GetSpanRO();
            for (int d = 0; d < buf.Count; d++)
            {
                if (span[d].FeatureId != state.ActiveFeatureId) continue;
                DrawObbEdges(draw, in span[d]);
                break;
            }
        }

        // Emits 12 DrawLine calls representing the extruded OBB of a danger-area descriptor.
        // Uses the XZ plane for the footprint; ZFloor/ZCeiling become the Y offsets in world space.
        private static void DrawObbEdges(IGizmoDrawBuilder draw, in DangerAreaDescriptor desc)
        {
            float cos = MathF.Cos(desc.AngleRad);
            float sin = MathF.Sin(desc.AngleRad);

            float ex = desc.ExtentsXY.X;
            float ey = desc.ExtentsXY.Y;

            // 4 corners in the XZ plane rotated by AngleRad
            var c0 = new Vector3(desc.Center.X + cos * ex - sin * ey, 0f, desc.Center.Z + sin * ex + cos * ey);
            var c1 = new Vector3(desc.Center.X - cos * ex - sin * ey, 0f, desc.Center.Z - sin * ex + cos * ey);
            var c2 = new Vector3(desc.Center.X - cos * ex + sin * ey, 0f, desc.Center.Z - sin * ex - cos * ey);
            var c3 = new Vector3(desc.Center.X + cos * ex + sin * ey, 0f, desc.Center.Z + sin * ex - cos * ey);

            // ZFloor and ZCeiling applied as Y coordinate (world up = Y)
            var floorOffset = new Vector3(0f, desc.ZFloor,   0f);
            var ceilOffset  = new Vector3(0f, desc.ZCeiling, 0f);

            // Bottom face (4 edges at ZFloor)
            draw.DrawLine(c0 + floorOffset, c1 + floorOffset, s_obbColor);
            draw.DrawLine(c1 + floorOffset, c2 + floorOffset, s_obbColor);
            draw.DrawLine(c2 + floorOffset, c3 + floorOffset, s_obbColor);
            draw.DrawLine(c3 + floorOffset, c0 + floorOffset, s_obbColor);

            // Top face (4 edges at ZCeiling)
            draw.DrawLine(c0 + ceilOffset, c1 + ceilOffset, s_obbColor);
            draw.DrawLine(c1 + ceilOffset, c2 + ceilOffset, s_obbColor);
            draw.DrawLine(c2 + ceilOffset, c3 + ceilOffset, s_obbColor);
            draw.DrawLine(c3 + ceilOffset, c0 + ceilOffset, s_obbColor);

            // 4 vertical edges connecting floor to ceiling
            draw.DrawLine(c0 + floorOffset, c0 + ceilOffset, s_obbColor);
            draw.DrawLine(c1 + floorOffset, c1 + ceilOffset, s_obbColor);
            draw.DrawLine(c2 + floorOffset, c2 + ceilOffset, s_obbColor);
            draw.DrawLine(c3 + floorOffset, c3 + ceilOffset, s_obbColor);
        }

        // P7-03: phase id + dwell-entry tick label, and per-contact DrawSphere markers.
        private void EmitPhaseAndContacts(IGizmoDrawBuilder draw, ref SquadCognitiveState state)
        {
            draw.DrawTextLong(0f, 0f, $"Phase:{state.PhaseId} T0:{state.PhaseEnteredTick}",
                s_phaseColor);

            int contactCount = state.Contacts.Count;
            var contactSpan = MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<SquadContactPoolSlots, SquadContact>(
                    ref Unsafe.AsRef(in state.Contacts.Contacts)), 16);

            for (int c = 0; c < contactCount; c++)
            {
                var contact = contactSpan[c];
                draw.DrawSphere(
                    new Vector3(contact.PositionX, contact.PositionY, contact.PositionZ),
                    1.5f,
                    s_contactColor);
            }
        }
    }
}
