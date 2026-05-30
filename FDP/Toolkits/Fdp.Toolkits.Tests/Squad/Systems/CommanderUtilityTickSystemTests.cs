using System;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Systems;
using Fdp.Toolkit.Utility;
using Xunit;

namespace Fdp.Toolkit.Squad.Tests.Systems
{
    /// <summary>
    /// Tests for <see cref="CommanderUtilityTickSystem"/>.
    /// Success criteria: SC-P3-01-1 through SC-P3-01-4.
    /// </summary>
    public unsafe class CommanderUtilityTickSystemTests : IDisposable
    {
        private EntityRepository _repo;

        // Mutable stub scores for cadence-swap test.
        private static float s_stubHighScore  = 0.9f;
        private static float s_stubLowScore   = 0.1f;

        private static float StubHigh(in UtilityInputCtx ctx) => s_stubHighScore;
        private static float StubLow(in UtilityInputCtx ctx)  => s_stubLowScore;

        // Fresh IDs that don't collide with any existing registered reader.
        private const ushort IdHigh = 0xF001;
        private const ushort IdLow  = 0xF002;

        public CommanderUtilityTickSystemTests()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<UnitRoster>();
            _repo.RegisterComponent<Blackboard1024>();
            _repo.RegisterComponent<SquadStateMarker>();
            _repo.RegisterComponent<UtilityResultBuffer>();
            _repo.RegisterComponent<UtilityTraceWorkingMemory1024>();

            SquadInputs.RegisterAll();
            UtilityInputReaderStore.Register(IdHigh, &StubHigh);
            UtilityInputReaderStore.Register(IdLow,  &StubLow);
        }

        public void Dispose()
        {
            UtilityInputReaderStore.Clear();
            _repo.Dispose();
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private Entity CreateCommander(bool withTrace = false)
        {
            var e = _repo.CreateEntity();
            _repo.AddComponent(e, new Blackboard1024());
            _repo.AddComponent(e, new SquadStateMarker());
            _repo.AddComponent(e, new UtilityResultBuffer());
            if (withTrace)
                _repo.AddComponent(e, new UtilityTraceWorkingMemory1024());
            return e;
        }

        /// <summary>
        /// Two-option stub def: option 0 uses IdHigh, option 1 uses IdLow.
        /// </summary>
        private static UtilityDecisionDef BuildStubDef() => new UtilityDecisionDef
        {
            DebugName = "StubManeuverSelect",
            Kind      = DecisionKind.ManeuverSelect,
            Options   = new[]
            {
                new UtilityOption
                {
                    OptionId       = 0,
                    Mode           = ScoringMode.WeightedProduct,
                    Considerations = new[]
                    {
                        new UtilityConsideration(IdHigh, InputContext.Self, weight: 1f,
                            curve: new ResponseCurve(CurveKind.Linear, slope: 1f))
                    }
                },
                new UtilityOption
                {
                    OptionId       = 1,
                    Mode           = ScoringMode.WeightedProduct,
                    Considerations = new[]
                    {
                        new UtilityConsideration(IdLow, InputContext.Self, weight: 1f,
                            curve: new ResponseCurve(CurveKind.Linear, slope: 1f))
                    }
                },
            }
        };

        // ── SC-P3-01-1: Scorer selects option 0 (highest score) ──────────────────

        [Fact]
        public void Run_TwoOptions_WinnerIsHighestScore_ManeuverKindIs0()
        {
            s_stubHighScore = 0.9f;
            s_stubLowScore  = 0.1f;

            var commander = CreateCommander();
            var def = BuildStubDef();

            CommanderUtilityTickSystem.Run(_repo, commander, in def, currentTick: 1);

            ref var state = ref SquadCognitiveState.Project(
                ref _repo.GetComponentRW<Blackboard1024>(commander));
            Assert.Equal(0, state.ManeuverKind);
        }

        // ── SC-P3-01-2: MissionOverride blocks scorer ─────────────────────────────

        [Fact]
        public void Run_MissionOverrideSet_ScorerSkipped_ManeuverKindUnchanged()
        {
            s_stubHighScore = 0.9f;
            s_stubLowScore  = 0.1f;

            var commander = CreateCommander();
            ref var state = ref SquadCognitiveState.Project(
                ref _repo.GetComponentRW<Blackboard1024>(commander));
            state.ManeuverKind = 99;
            state.Flags |= 1u;  // MissionOverrideBit

            var def = BuildStubDef();
            CommanderUtilityTickSystem.Run(_repo, commander, in def, currentTick: 1);

            Assert.Equal(99, state.ManeuverKind);
        }

        // ── SC-P3-01-3: Cadence gate blocks mid-interval re-score ────────────────

        [Fact]
        public void Run_CadenceGate_BlocksMidIntervalRescore()
        {
            s_stubHighScore = 0.9f;
            s_stubLowScore  = 0.1f;

            var commander = CreateCommander();
            var def = BuildStubDef();

            // Tick 1: first run — option 0 wins.
            CommanderUtilityTickSystem.Run(_repo, commander, in def, currentTick: 1, tickInterval: 6);

            ref var state = ref SquadCognitiveState.Project(
                ref _repo.GetComponentRW<Blackboard1024>(commander));
            Assert.Equal(0, state.ManeuverKind);

            // Swap: option 1 now has a higher raw score.
            s_stubHighScore = 0.1f;
            s_stubLowScore  = 0.9f;

            // Tick 3: within interval — cadence gate should block re-score.
            CommanderUtilityTickSystem.Run(_repo, commander, in def, currentTick: 3, tickInterval: 6);
            Assert.Equal(0, state.ManeuverKind);  // still 0

            // Tick 7: interval elapsed (7 - 1 = 6 >= 6) — re-score runs, option 1 wins.
            CommanderUtilityTickSystem.Run(_repo, commander, in def, currentTick: 7, tickInterval: 6);
            Assert.Equal(1, state.ManeuverKind);
        }

        // ── SC-P3-01-4: TraceWorkingMemory gets populated ─────────────────────────

        [Fact]
        public void Run_WithTraceBuffer_RecordCountIsNonZero()
        {
            s_stubHighScore = 0.9f;
            s_stubLowScore  = 0.1f;

            var commander = CreateCommander(withTrace: true);
            var def = BuildStubDef();

            CommanderUtilityTickSystem.Run(_repo, commander, in def, currentTick: 1);

            ref var trace = ref _repo.GetComponentRW<UtilityTraceWorkingMemory1024>(commander);
            Assert.True(trace.RecordCount > 0);
        }
    }
}
