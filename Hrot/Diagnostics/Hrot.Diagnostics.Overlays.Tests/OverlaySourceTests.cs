using System.Numerics;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Utility;
using Hrot.Diagnostics.Overlays;
using Xunit;
using FixedString32 = Fdp.Toolkit.Diagnostics.Gizmos.FixedString32;

namespace Hrot.Diagnostics.Overlays.Tests
{
    // Stub IGizmoDrawBuilder that counts all draw-primitive calls.
    // Default-interface-body methods (DrawBox2D, EndFrame, DrawMainMenuBinding, EmitRaw)
    // are intentionally left to their no-op defaults.
    internal sealed class CountingDrawBuilder : IGizmoDrawBuilder
    {
        public int EmitCount;

        public void DrawLine(
            Vector3 start, Vector3 end, Rgba32 color,
            float thickness = 1f,
            SizeMode sizeMode = SizeMode.ScreenPixels,
            PipelineTarget target = PipelineTarget.All,
            byte layer = 0,
            LineStyle style = LineStyle.Solid) => EmitCount++;

        public void DrawLineGradient(
            Vector3 start, Vector3 end, Rgba32 startColor, Rgba32 endColor,
            float thickness = 1f,
            SizeMode sizeMode = SizeMode.ScreenPixels,
            PipelineTarget target = PipelineTarget.All,
            byte layer = 0,
            LineStyle style = LineStyle.Solid) => EmitCount++;

        public void DrawSphere(
            Vector3 center, float radius, Rgba32 color,
            float thickness = 0f,
            SizeMode sizeMode = SizeMode.WorldMeters,
            PipelineTarget target = PipelineTarget.All,
            byte layer = 0,
            Rgba32 fillColor = default,
            LineStyle style = LineStyle.Solid) => EmitCount++;

        public void DrawArrow(
            Vector3 from, Vector3 to, Rgba32 color,
            float headSize = 1f,
            byte layer = 0) => EmitCount++;

        public void DrawText(
            float x, float y, FixedString32 text, Rgba32 color,
            CoordinateSpace space = CoordinateSpace.World,
            byte layer = 0) => EmitCount++;

        public void DrawTextLong(
            float x, float y, string text, Rgba32 color,
            CoordinateSpace space = CoordinateSpace.World,
            byte layer = 0) => EmitCount++;
    }

    public sealed class OverlaySourceTests
    {
        // Creates and returns a disposed-on-call EntityRepository with all overlay-relevant
        // component types registered. Caller owns the returned instance and must dispose it.
        private static EntityRepository CreateTestRepo()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<DebugState>();
            repo.RegisterComponent<SensorContactList>();
            repo.RegisterComponent<TargetMemory>();
            repo.RegisterComponent<EqsCognitiveBuffer>();
            repo.RegisterComponent<UtilityTraceWorkingMemory1024>();
            repo.RegisterComponent<UnitRoster>();
            return repo;
        }

        // ── SC-P4-01-1: UtilityDecisionOverlaySource ─────────────────────────────

        [Fact]
        public void UtilityDecision_NoDebugState_EmitsZero()
        {
            using var repo = CreateTestRepo();
            var arbiter = new OverlayBudgetArbiter(float.MaxValue);
            var source  = new UtilityDecisionOverlaySource(repo, arbiter);

            // Entity exists but has no DebugState — source must skip it cleanly.
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new UtilityTraceWorkingMemory1024());

            var draw = new CountingDrawBuilder();
            arbiter.BeginFrame();
            source.Emit(0.016f, draw);

            Assert.Equal(0, draw.EmitCount);
        }

        // ── SC-P4-01-2 ────────────────────────────────────────────────────────────

        [Fact]
        public void UtilityDecision_FlagSet_TracePresent_EmitsAtLeastOne()
        {
            using var repo = CreateTestRepo();
            var arbiter = new OverlayBudgetArbiter(float.MaxValue);
            var source  = new UtilityDecisionOverlaySource(repo, arbiter);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new DebugState { Ai = AiOverlayFlags.UtilityDecision });
            repo.AddComponent(entity, new UtilityTraceWorkingMemory1024());

            ref var mem = ref repo.GetComponentRW<UtilityTraceWorkingMemory1024>(entity);
            mem.WriteWinnerRecord(
                tick: 1, winnerOptionId: 0, winnerDefinitionIdx: 0,
                winnerScore: 0.9f, runnerUpMargin: 0.1f);

            var draw = new CountingDrawBuilder();
            arbiter.BeginFrame();
            source.Emit(0.016f, draw);

            Assert.True(draw.EmitCount >= 1);
        }

        [Fact]
        public void UtilityDecision_FlagAbsent_EmitsZero()
        {
            using var repo = CreateTestRepo();
            var arbiter = new OverlayBudgetArbiter(float.MaxValue);
            var source  = new UtilityDecisionOverlaySource(repo, arbiter);

            var entity = repo.CreateEntity();
            // Ai field has no UtilityDecision bit set
            repo.AddComponent(entity, new DebugState { Ai = AiOverlayFlags.Perception });
            repo.AddComponent(entity, new UtilityTraceWorkingMemory1024());

            ref var mem = ref repo.GetComponentRW<UtilityTraceWorkingMemory1024>(entity);
            mem.WriteWinnerRecord(tick: 1, winnerOptionId: 0, winnerDefinitionIdx: 0,
                winnerScore: 0.8f, runnerUpMargin: 0.2f);

            var draw = new CountingDrawBuilder();
            arbiter.BeginFrame();
            source.Emit(0.016f, draw);

            Assert.Equal(0, draw.EmitCount);
        }

        [Fact]
        public void UtilityDecision_FlagSet_ComponentAbsent_EmitsZero_NoThrow()
        {
            using var repo = CreateTestRepo();
            var arbiter = new OverlayBudgetArbiter(float.MaxValue);
            var source  = new UtilityDecisionOverlaySource(repo, arbiter);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new DebugState { Ai = AiOverlayFlags.UtilityDecision });
            // UtilityTraceWorkingMemory1024 intentionally NOT added

            var draw = new CountingDrawBuilder();
            arbiter.BeginFrame();
            source.Emit(0.016f, draw); // must not throw

            Assert.Equal(0, draw.EmitCount);
        }

        // ── PerceptionOverlaySource ───────────────────────────────────────────────

        [Fact]
        public void Perception_FlagAbsent_EmitsZero()
        {
            using var repo = CreateTestRepo();
            var arbiter = new OverlayBudgetArbiter(float.MaxValue);
            var source  = new PerceptionOverlaySource(repo, arbiter);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new DebugState { Ai = AiOverlayFlags.TargetMemory });
            repo.AddComponent(entity, new SensorContactList());

            var draw = new CountingDrawBuilder();
            arbiter.BeginFrame();
            source.Emit(0.016f, draw);

            Assert.Equal(0, draw.EmitCount);
        }

        [Fact]
        public void Perception_FlagSet_ComponentAbsent_EmitsZero_NoThrow()
        {
            using var repo = CreateTestRepo();
            var arbiter = new OverlayBudgetArbiter(float.MaxValue);
            var source  = new PerceptionOverlaySource(repo, arbiter);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new DebugState { Ai = AiOverlayFlags.Perception });
            // SensorContactList intentionally NOT added

            var draw = new CountingDrawBuilder();
            arbiter.BeginFrame();
            source.Emit(0.016f, draw); // must not throw

            Assert.Equal(0, draw.EmitCount);
        }

        [Fact]
        public void Perception_FlagAndComponentPresent_EmitsAtLeastOne()
        {
            using var repo = CreateTestRepo();
            var arbiter = new OverlayBudgetArbiter(float.MaxValue);
            var source  = new PerceptionOverlaySource(repo, arbiter);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new DebugState { Ai = AiOverlayFlags.Perception });
            repo.AddComponent(entity, new SensorContactList());

            var draw = new CountingDrawBuilder();
            arbiter.BeginFrame();
            source.Emit(0.016f, draw);

            Assert.True(draw.EmitCount >= 1);
        }

        // ── TargetMemoryOverlaySource ─────────────────────────────────────────────

        [Fact]
        public void TargetMemory_FlagAbsent_EmitsZero()
        {
            using var repo = CreateTestRepo();
            var arbiter = new OverlayBudgetArbiter(float.MaxValue);
            var source  = new TargetMemoryOverlaySource(repo, arbiter);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new DebugState { Ai = AiOverlayFlags.Perception });
            repo.AddComponent(entity, new TargetMemory());

            var draw = new CountingDrawBuilder();
            arbiter.BeginFrame();
            source.Emit(0.016f, draw);

            Assert.Equal(0, draw.EmitCount);
        }

        [Fact]
        public void TargetMemory_FlagSet_ComponentAbsent_EmitsZero_NoThrow()
        {
            using var repo = CreateTestRepo();
            var arbiter = new OverlayBudgetArbiter(float.MaxValue);
            var source  = new TargetMemoryOverlaySource(repo, arbiter);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new DebugState { Ai = AiOverlayFlags.TargetMemory });
            // TargetMemory intentionally NOT added

            var draw = new CountingDrawBuilder();
            arbiter.BeginFrame();
            source.Emit(0.016f, draw); // must not throw

            Assert.Equal(0, draw.EmitCount);
        }

        [Fact]
        public void TargetMemory_FlagAndComponentPresent_EmitsAtLeastOne()
        {
            using var repo = CreateTestRepo();
            var arbiter = new OverlayBudgetArbiter(float.MaxValue);
            var source  = new TargetMemoryOverlaySource(repo, arbiter);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new DebugState { Ai = AiOverlayFlags.TargetMemory });
            repo.AddComponent(entity, new TargetMemory());

            // Seed one target so Count > 0 and at least one sphere is emitted.
            ref var tm = ref repo.GetComponentRW<TargetMemory>(entity);
            TargetMemory.AddOrUpdateTarget(ref tm, entityId: 1L, posX: 10f, posY: 20f, scoreBoost: 5f, tick: 1u);

            var draw = new CountingDrawBuilder();
            arbiter.BeginFrame();
            source.Emit(0.016f, draw);

            Assert.True(draw.EmitCount >= 1);
        }

        // ── EqsOverlaySource ──────────────────────────────────────────────────────

        [Fact]
        public void Eqs_FlagAbsent_EmitsZero()
        {
            using var repo = CreateTestRepo();
            var arbiter = new OverlayBudgetArbiter(float.MaxValue);
            var source  = new EqsOverlaySource(repo, arbiter);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new DebugState { Ai = AiOverlayFlags.Perception });
            repo.AddComponent(entity, new EqsCognitiveBuffer());

            var draw = new CountingDrawBuilder();
            arbiter.BeginFrame();
            source.Emit(0.016f, draw);

            Assert.Equal(0, draw.EmitCount);
        }

        [Fact]
        public void Eqs_FlagSet_ComponentAbsent_EmitsZero_NoThrow()
        {
            using var repo = CreateTestRepo();
            var arbiter = new OverlayBudgetArbiter(float.MaxValue);
            var source  = new EqsOverlaySource(repo, arbiter);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new DebugState { Ai = AiOverlayFlags.Eqs });
            // EqsCognitiveBuffer intentionally NOT added

            var draw = new CountingDrawBuilder();
            arbiter.BeginFrame();
            source.Emit(0.016f, draw); // must not throw

            Assert.Equal(0, draw.EmitCount);
        }

        [Fact]
        public void Eqs_FlagAndComponentPresent_EmitsAtLeastOne()
        {
            using var repo = CreateTestRepo();
            var arbiter = new OverlayBudgetArbiter(float.MaxValue);
            var source  = new EqsOverlaySource(repo, arbiter);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new DebugState { Ai = AiOverlayFlags.Eqs });
            repo.AddComponent(entity, new EqsCognitiveBuffer());

            var draw = new CountingDrawBuilder();
            arbiter.BeginFrame();
            source.Emit(0.016f, draw);

            Assert.True(draw.EmitCount >= 1);
        }

        // ── SquadAssignmentOverlaySource ──────────────────────────────────────────

        [Fact]
        public void SquadAssignment_FlagAbsent_EmitsZero()
        {
            using var repo = CreateTestRepo();
            var arbiter = new OverlayBudgetArbiter(float.MaxValue);
            var source  = new SquadAssignmentOverlaySource(repo, arbiter);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new DebugState { Ai = AiOverlayFlags.Perception });
            repo.AddComponent(entity, new UnitRoster());

            var draw = new CountingDrawBuilder();
            arbiter.BeginFrame();
            source.Emit(0.016f, draw);

            Assert.Equal(0, draw.EmitCount);
        }

        [Fact]
        public void SquadAssignment_FlagSet_ComponentAbsent_EmitsZero_NoThrow()
        {
            using var repo = CreateTestRepo();
            var arbiter = new OverlayBudgetArbiter(float.MaxValue);
            var source  = new SquadAssignmentOverlaySource(repo, arbiter);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new DebugState { Ai = AiOverlayFlags.SquadAssignment });
            // UnitRoster intentionally NOT added

            var draw = new CountingDrawBuilder();
            arbiter.BeginFrame();
            source.Emit(0.016f, draw); // must not throw

            Assert.Equal(0, draw.EmitCount);
        }

        // ── SC-P4-02-1: OverlayBudgetArbiter ─────────────────────────────────────

        [Fact]
        public void BudgetArbiter_ShedsChannels_KeepsUtilityDecision()
        {
            // Budget 1 ms; recording 2 ms for Channels exceeds the budget.
            // Shed order starts with Channels (lowest priority), so Channels is shed first.
            // UtilityDecision is highest priority and must remain permitted.
            var arbiter = new OverlayBudgetArbiter(1f);
            arbiter.BeginFrame();

            bool channelsAllowed = arbiter.RecordAndCheck(AiOverlayFlags.Channels, 2f);

            Assert.False(channelsAllowed);
            Assert.True(arbiter.IsPermitted(AiOverlayFlags.UtilityDecision));
        }
    }
}
