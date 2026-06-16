using System.Collections.Generic;
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
using Hrot.Diagnostics.Overlays;
using Xunit;
using FixedString32 = Fdp.Toolkit.Diagnostics.Gizmos.FixedString32;

namespace Hrot.Diagnostics.Overlays.Tests
{
    // Extended draw builder that records line styles and sphere positions for assertion.
    // Distinct from CountingDrawBuilder (defined in OverlaySourceTests.cs, same assembly).
    internal sealed class LineCapturingDrawBuilder : IGizmoDrawBuilder
    {
        public int EmitCount;
        public readonly List<(Vector3 start, Vector3 end, LineStyle style)> Lines
            = new List<(Vector3, Vector3, LineStyle)>();
        public readonly List<string> LongTexts = new List<string>();
        public readonly List<Vector3> SpherePositions = new List<Vector3>();

        public void DrawLine(Vector3 start, Vector3 end, Rgba32 color,
            float thickness = 1f, SizeMode sizeMode = SizeMode.ScreenPixels,
            PipelineTarget target = PipelineTarget.All, byte layer = 0,
            LineStyle style = LineStyle.Solid)
        { EmitCount++; Lines.Add((start, end, style)); }

        public void DrawLineGradient(Vector3 start, Vector3 end, Rgba32 sc, Rgba32 ec,
            float thickness = 1f, SizeMode sizeMode = SizeMode.ScreenPixels,
            PipelineTarget target = PipelineTarget.All, byte layer = 0,
            LineStyle style = LineStyle.Solid)
        { EmitCount++; }

        public void DrawSphere(Vector3 center, float radius, Rgba32 color,
            float thickness = 0f, SizeMode sizeMode = SizeMode.WorldMeters,
            PipelineTarget target = PipelineTarget.All, byte layer = 0,
            Rgba32 fillColor = default, LineStyle style = LineStyle.Solid)
        { EmitCount++; SpherePositions.Add(center); }

        public void DrawArrow(Vector3 from, Vector3 to, Rgba32 color,
            float headSize = 1f, byte layer = 0) => EmitCount++;

        public void DrawText(float x, float y, FixedString32 text, Rgba32 color,
            CoordinateSpace space = CoordinateSpace.World, byte layer = 0, float fontSizePx = 0f, float lineOffsetPx = 0f) => EmitCount++;

        public void DrawTextLong(float x, float y, string text, Rgba32 color,
            CoordinateSpace space = CoordinateSpace.World, byte layer = 0, float fontSizePx = 0f, float lineOffsetPx = 0f)
        { EmitCount++; LongTexts.Add(text); }
    }

    public sealed class SquadCoordinationOverlaySourceTests
    {
        // Creates an EntityRepository with all components needed by SquadCoordinationOverlaySource.
        private static EntityRepository CreateTestRepo()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<DebugState>();
            repo.RegisterComponent<UnitRoster>();
            repo.RegisterComponent<Blackboard1024>();
            repo.RegisterComponent<SquadStateMarker>();
            repo.RegisterComponent<DangerAreaCognitiveBuffer>();
            repo.RegisterComponent<BehaviorState>();
            repo.RegisterComponent<UtilityTraceWorkingMemory1024>();
            return repo;
        }

        // Creates a commander entity with DebugState (SquadAssignment flag), UnitRoster, and
        // Blackboard1024 pre-seeded with stateSnapshot. Caller owns the entity.
        private static unsafe Entity SetupSquadCommander(
            EntityRepository repo,
            SquadCognitiveState stateSnapshot)
        {
            var commander = repo.CreateEntity();
            repo.AddComponent(commander, new DebugState { Ai = AiOverlayFlags.SquadAssignment });
            repo.AddComponent(commander, new UnitRoster());
            repo.AddComponent(commander, new Blackboard1024());

            ref var bb    = ref repo.GetComponentRW<Blackboard1024>(commander);
            ref var state = ref SquadCognitiveState.Project(ref bb);
            state = stateSnapshot;

            return commander;
        }

        // ── SC-P7-01-1: toggle flag visibility ───────────────────────────────────

        [Fact]
        public void FlagSet_EmitsAtLeastOne()
        {
            using var repo = CreateTestRepo();
            var arbiter = new OverlayBudgetArbiter(float.MaxValue);
            var source  = new SquadCoordinationOverlaySource(repo, arbiter);

            var snap      = new SquadCognitiveState();
            var commander = SetupSquadCommander(repo, snap);

            var draw = new CountingDrawBuilder();
            arbiter.BeginFrame();
            source.Emit(0.016f, draw);

            Assert.True(draw.EmitCount >= 1);
        }

        [Fact]
        public void FlagAbsent_EmitsZero()
        {
            using var repo = CreateTestRepo();
            var arbiter = new OverlayBudgetArbiter(float.MaxValue);
            var source  = new SquadCoordinationOverlaySource(repo, arbiter);

            var commander = repo.CreateEntity();
            // AiOverlayFlags.Perception intentionally — NOT SquadAssignment
            repo.AddComponent(commander, new DebugState { Ai = AiOverlayFlags.Perception });
            repo.AddComponent(commander, new UnitRoster());
            repo.AddComponent(commander, new Blackboard1024());

            var draw = new CountingDrawBuilder();
            arbiter.BeginFrame();
            source.Emit(0.016f, draw);

            Assert.Equal(0, draw.EmitCount);
        }

        // ── SC-P7-01-2: element color persistence ────────────────────────────────

        [Fact]
        public unsafe void ElementColorPersistence_SameElementIndex_SameEmitCountAcrossTicks()
        {
            using var repo = CreateTestRepo();
            var arbiter = new OverlayBudgetArbiter(float.MaxValue);
            var source  = new SquadCoordinationOverlaySource(repo, arbiter);

            var snap      = new SquadCognitiveState();
            var commander = SetupSquadCommander(repo, snap);

            // Add 1 member (real entity, no utility trace so no veto line)
            var member = repo.CreateEntity();
            ref var roster = ref repo.GetComponentRW<UnitRoster>(commander);
            UnitRoster.Add(ref roster, (long)member.PackedValue);

            // Set member element index to 0
            ref var state = ref SquadCognitiveState.Project(
                ref repo.GetComponentRW<Blackboard1024>(commander));
            var memberElemSpan = MemoryMarshal.CreateSpan(
                ref Unsafe.As<MemberElementIndexArray, byte>(
                    ref state.Elements.MemberElements), 16);
            memberElemSpan[0] = 0; // element 0

            int count1;
            {
                var draw = new CountingDrawBuilder();
                arbiter.BeginFrame();
                source.Emit(0.016f, draw);
                count1 = draw.EmitCount;
            }

            int count2;
            {
                var draw = new CountingDrawBuilder();
                arbiter.BeginFrame();
                source.Emit(0.016f, draw);
                count2 = draw.EmitCount;
            }

            // Color is deterministic from element index — same count on every tick (no flicker)
            Assert.Equal(count1, count2);
            Assert.True(count1 >= 1);
        }

        // ── SC-P7-01-3: danger-area Z extent differs (ground vs bridge deck) ─────

        [Fact]
        public unsafe void DangerAreaObb_ZExtentDiffers_GroundVsBridgeDeck()
        {
            using var repo = CreateTestRepo();
            var arbiter = new OverlayBudgetArbiter(float.MaxValue);

            // --- Ground-level fixture ---
            {
                var source    = new SquadCoordinationOverlaySource(repo, arbiter);
                var snap      = new SquadCognitiveState { ActiveFeatureId = 1u };
                var commander = SetupSquadCommander(repo, snap);
                repo.AddComponent(commander, new DangerAreaCognitiveBuffer());

                ref var buf     = ref repo.GetComponentRW<DangerAreaCognitiveBuffer>(commander);
                var bufSpan = buf.GetSpanRW();
                bufSpan[0] = new DangerAreaDescriptor
                {
                    FeatureId = 1u,
                    Center    = new Vector3(0f, 0f, 0f),
                    ExtentsXY = new System.Numerics.Vector2(5f, 5f),
                    AngleRad  = 0f,
                    ZFloor    = 0f,
                    ZCeiling  = 2f,
                };
                buf.Count = 1;

                var draw = new LineCapturingDrawBuilder();
                arbiter.BeginFrame();
                source.Emit(0.016f, draw);

                float minY = float.MaxValue;
                float maxY = float.MinValue;
                foreach (var (start, end, _) in draw.Lines)
                {
                    if (start.Y < minY) minY = start.Y;
                    if (start.Y > maxY) maxY = start.Y;
                    if (end.Y < minY) minY = end.Y;
                    if (end.Y > maxY) maxY = end.Y;
                }
                Assert.True(draw.Lines.Count >= 12, "Expected at least 12 OBB lines");
                Assert.Equal(0f, minY, precision: 3);
                Assert.Equal(2f, maxY, precision: 3);
            }

            // --- Bridge-deck fixture ---
            using var repo2 = CreateTestRepo();
            {
                var source    = new SquadCoordinationOverlaySource(repo2, arbiter);
                var snap      = new SquadCognitiveState { ActiveFeatureId = 2u };
                var commander = SetupSquadCommander(repo2, snap);
                repo2.AddComponent(commander, new DangerAreaCognitiveBuffer());

                ref var buf     = ref repo2.GetComponentRW<DangerAreaCognitiveBuffer>(commander);
                var bufSpan = buf.GetSpanRW();
                bufSpan[0] = new DangerAreaDescriptor
                {
                    FeatureId = 2u,
                    Center    = new Vector3(0f, 0f, 0f),
                    ExtentsXY = new System.Numerics.Vector2(5f, 5f),
                    AngleRad  = 0f,
                    ZFloor    = 10f,
                    ZCeiling  = 12f,
                };
                buf.Count = 1;

                var draw = new LineCapturingDrawBuilder();
                arbiter.BeginFrame();
                source.Emit(0.016f, draw);

                float minY = float.MaxValue;
                float maxY = float.MinValue;
                foreach (var (start, end, _) in draw.Lines)
                {
                    if (start.Y < minY) minY = start.Y;
                    if (start.Y > maxY) maxY = start.Y;
                    if (end.Y < minY) minY = end.Y;
                    if (end.Y > maxY) maxY = end.Y;
                }
                Assert.True(draw.Lines.Count >= 12, "Expected at least 12 OBB lines");
                Assert.Equal(10f, minY, precision: 3);
                Assert.Equal(12f, maxY, precision: 3);
            }
        }

        // ── SC-P7-02-1: on-task member emits solid line only (no dashed/veto) ───

        [Fact]
        public unsafe void OnTaskMember_NoDivergence_EmitsSolidLineOnly()
        {
            using var repo = CreateTestRepo();
            var arbiter = new OverlayBudgetArbiter(float.MaxValue);
            var source  = new SquadCoordinationOverlaySource(repo, arbiter);

            var snap      = new SquadCognitiveState();
            var commander = SetupSquadCommander(repo, snap);

            // Member has BehaviorState but NO UtilityTraceWorkingMemory1024 (no veto trace)
            var member = repo.CreateEntity();
            repo.AddComponent(member, new BehaviorState { ActiveBehaviorHash = 0 });
            ref var roster = ref repo.GetComponentRW<UnitRoster>(commander);
            UnitRoster.Add(ref roster, (long)member.PackedValue);

            var draw = new LineCapturingDrawBuilder();
            arbiter.BeginFrame();
            source.Emit(0.016f, draw);

            // No dashed lines and no VETO: labels
            var dashedLines = draw.Lines.FindAll(l => l.style == LineStyle.Dashed);
            Assert.Empty(dashedLines);
            Assert.Empty(draw.LongTexts.FindAll(t => t.StartsWith("VETO:")));
        }

        // ── SC-P7-02-2: vetoing member emits dashed line + label ─────────────────

        [Fact]
        public unsafe void VetoingMember_EmitsDashedLineAndLabel()
        {
            using var repo = CreateTestRepo();
            var arbiter = new OverlayBudgetArbiter(float.MaxValue);
            var source  = new SquadCoordinationOverlaySource(repo, arbiter);

            var snap      = new SquadCognitiveState();
            var commander = SetupSquadCommander(repo, snap);

            // Member has a utility trace with a winner record (signals divergence in the overlay)
            var member = repo.CreateEntity();
            repo.AddComponent(member, new BehaviorState { ActiveBehaviorHash = 42 });
            repo.AddComponent(member, new UtilityTraceWorkingMemory1024());

            ref var mem = ref repo.GetComponentRW<UtilityTraceWorkingMemory1024>(member);
            mem.WriteWinnerRecord(tick: 1, winnerOptionId: 7, winnerDefinitionIdx: 0,
                winnerScore: 0.9f, runnerUpMargin: 0.1f);

            ref var roster = ref repo.GetComponentRW<UnitRoster>(commander);
            UnitRoster.Add(ref roster, (long)member.PackedValue);

            var draw = new LineCapturingDrawBuilder();
            arbiter.BeginFrame();
            source.Emit(0.016f, draw);

            // Must have at least one dashed line (the veto divergence indicator)
            var dashedLines = draw.Lines.FindAll(l => l.style == LineStyle.Dashed);
            Assert.True(dashedLines.Count >= 1);

            // Must have a VETO: label containing the winner option id (7)
            var vetoLabels = draw.LongTexts.FindAll(t => t.StartsWith("VETO:"));
            Assert.True(vetoLabels.Count >= 1);
            Assert.Contains("7", vetoLabels[0]);
        }

        // ── SC-P7-02-3: veto label updates as dominant consideration changes ─────

        [Fact]
        public unsafe void VetoLabel_UpdatesWhenOptionIdChanges()
        {
            using var repo = CreateTestRepo();
            var arbiter = new OverlayBudgetArbiter(float.MaxValue);
            var source  = new SquadCoordinationOverlaySource(repo, arbiter);

            var snap      = new SquadCognitiveState();
            var commander = SetupSquadCommander(repo, snap);

            var member = repo.CreateEntity();
            repo.AddComponent(member, new BehaviorState());
            repo.AddComponent(member, new UtilityTraceWorkingMemory1024());

            ref var roster = ref repo.GetComponentRW<UnitRoster>(commander);
            UnitRoster.Add(ref roster, (long)member.PackedValue);

            // Tick 1: option id = 3
            ref var mem = ref repo.GetComponentRW<UtilityTraceWorkingMemory1024>(member);
            mem.WriteWinnerRecord(tick: 1, winnerOptionId: 3, winnerDefinitionIdx: 0,
                winnerScore: 0.8f, runnerUpMargin: 0.2f);

            var draw1 = new LineCapturingDrawBuilder();
            arbiter.BeginFrame();
            source.Emit(0.016f, draw1);
            var label1 = draw1.LongTexts.Find(t => t.StartsWith("VETO:"));

            // Tick 2: option id = 5 (different consideration wins)
            mem.WriteWinnerRecord(tick: 2, winnerOptionId: 5, winnerDefinitionIdx: 0,
                winnerScore: 0.9f, runnerUpMargin: 0.1f);

            var draw2 = new LineCapturingDrawBuilder();
            arbiter.BeginFrame();
            source.Emit(0.016f, draw2);
            var label2 = draw2.LongTexts.Find(t => t.StartsWith("VETO:"));

            Assert.NotNull(label1);
            Assert.NotNull(label2);
            Assert.NotEqual(label1, label2); // label text changed between ticks
        }

        // ── SC-P7-03-1: phase label updates immediately on transition ─────────────

        [Fact]
        public unsafe void PhaseLabel_UpdatesOnTransition()
        {
            using var repo = CreateTestRepo();
            var arbiter = new OverlayBudgetArbiter(float.MaxValue);
            var source  = new SquadCoordinationOverlaySource(repo, arbiter);

            var snap1     = new SquadCognitiveState { PhaseId = 1, PhaseEnteredTick = 100u };
            var commander = SetupSquadCommander(repo, snap1);

            var draw1 = new LineCapturingDrawBuilder();
            arbiter.BeginFrame();
            source.Emit(0.016f, draw1);
            var phaseLabel1 = draw1.LongTexts.Find(t => t.StartsWith("Phase:"));
            Assert.NotNull(phaseLabel1);
            Assert.Contains("1", phaseLabel1);

            // Transition to phase 2
            ref var state = ref SquadCognitiveState.Project(
                ref repo.GetComponentRW<Blackboard1024>(commander));
            state.PhaseId        = 2;
            state.PhaseEnteredTick = 200u;

            var draw2 = new LineCapturingDrawBuilder();
            arbiter.BeginFrame();
            source.Emit(0.016f, draw2);
            var phaseLabel2 = draw2.LongTexts.Find(t => t.StartsWith("Phase:"));
            Assert.NotNull(phaseLabel2);
            Assert.Contains("2", phaseLabel2);
            Assert.NotEqual(phaseLabel1, phaseLabel2);
        }

        // ── SC-P7-03-2: dwell timer resets to 0 on phase transition ──────────────

        [Fact]
        public unsafe void PhaseEntryTick_ResetsOnTransition()
        {
            using var repo = CreateTestRepo();
            var arbiter = new OverlayBudgetArbiter(float.MaxValue);
            var source  = new SquadCoordinationOverlaySource(repo, arbiter);

            var snap      = new SquadCognitiveState { PhaseId = 0, PhaseEnteredTick = 50u };
            var commander = SetupSquadCommander(repo, snap);

            var draw1 = new LineCapturingDrawBuilder();
            arbiter.BeginFrame();
            source.Emit(0.016f, draw1);
            var label1 = draw1.LongTexts.Find(t => t.Contains("T0:"));
            Assert.NotNull(label1);
            Assert.Contains("50", label1); // T0:50

            // Phase transition resets PhaseEnteredTick to 150
            ref var state = ref SquadCognitiveState.Project(
                ref repo.GetComponentRW<Blackboard1024>(commander));
            state.PhaseId          = 1;
            state.PhaseEnteredTick = 150u;

            var draw2 = new LineCapturingDrawBuilder();
            arbiter.BeginFrame();
            source.Emit(0.016f, draw2);
            var label2 = draw2.LongTexts.Find(t => t.Contains("T0:"));
            Assert.NotNull(label2);
            Assert.Contains("150", label2); // T0:150 (reset to new entry tick)
            Assert.NotEqual(label1, label2);
        }

        // ── SC-P7-03-3: contact pool markers differ from per-member markers ───────

        [Fact]
        public unsafe void ContactPool_EmitsSpheres_WhenContactsPresent()
        {
            using var repo = CreateTestRepo();
            var arbiter = new OverlayBudgetArbiter(float.MaxValue);
            var source  = new SquadCoordinationOverlaySource(repo, arbiter);

            var snap      = new SquadCognitiveState();
            var commander = SetupSquadCommander(repo, snap);

            // Seed 2 contacts in the squad contact pool
            ref var state = ref SquadCognitiveState.Project(
                ref repo.GetComponentRW<Blackboard1024>(commander));
            state.Contacts.Count = 2;
            var contactSpan = MemoryMarshal.CreateSpan(
                ref Unsafe.As<SquadContactPoolSlots, SquadContact>(
                    ref state.Contacts.Contacts), 16);
            contactSpan[0] = new SquadContact
            {
                PositionX   = 10f, PositionY = 0f, PositionZ = 0f,
                ThreatScore = 0.8f
            };
            contactSpan[1] = new SquadContact
            {
                PositionX   = 20f, PositionY = 0f, PositionZ = 5f,
                ThreatScore = 0.5f
            };

            var draw = new LineCapturingDrawBuilder();
            arbiter.BeginFrame();
            source.Emit(0.016f, draw);

            // 2 contact pool spheres emitted (distinct from per-member TargetMemory spheres)
            Assert.Equal(2, draw.SpherePositions.Count);
            Assert.Contains(draw.SpherePositions, p => MathF.Abs(p.X - 10f) < 0.01f);
            Assert.Contains(draw.SpherePositions, p => MathF.Abs(p.X - 20f) < 0.01f);
        }

        // ── SC-P7-03-4: overlay budget shedding with 50 squads ───────────────────

        [Fact]
        public unsafe void BudgetShedding_50Squads_ChannelsShedFirst()
        {
            using var repo = CreateTestRepo();
            // Budget cap = 1 ms; Channels costs 2 ms so it is shed first.
            var arbiter = new OverlayBudgetArbiter(1f);
            var source  = new SquadCoordinationOverlaySource(repo, arbiter);

            // 50 squad commanders, each with the SquadAssignment flag
            for (int i = 0; i < 50; i++)
            {
                var snap = new SquadCognitiveState();
                SetupSquadCommander(repo, snap);
            }

            arbiter.BeginFrame();

            // Exhaust the budget with a Channels record (lowest priority, shed first)
            bool channelsAllowed = arbiter.RecordAndCheck(AiOverlayFlags.Channels, 2f);
            Assert.False(channelsAllowed); // Channels is over budget and shed

            // SquadAssignment is higher priority than Channels — must still be permitted
            Assert.True(arbiter.IsPermitted(AiOverlayFlags.SquadAssignment));

            var draw = new CountingDrawBuilder();
            source.Emit(0.016f, draw); // Should emit at least one call per squad
            Assert.True(draw.EmitCount >= 50);
        }
    }
}
