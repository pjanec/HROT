using System;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Mappers;
using Fdp.Toolkit.Squad.Systems;
using Fdp.Toolkit.Utility;
using Xunit;

namespace Fdp.Toolkit.Squad.Tests.Mappers
{
    /// <summary>
    /// Tests for <see cref="ForceManeuverMapper"/> and <see cref="ClearForceManeuverMapper"/>.
    /// Success criteria: SC-P3-03-1 through SC-P3-03-3.
    /// </summary>
    public unsafe class ForceManeuverMapperTests : IDisposable
    {
        private EntityRepository _repo;

        // Stub reader for the clear test's subsequent scorer invocation.
        private static float StubConst09(in UtilityInputCtx ctx) => 0.9f;
        private const ushort IdStub = 0xF003;

        public ForceManeuverMapperTests()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<Blackboard1024>();
            _repo.RegisterComponent<SquadStateMarker>();
            _repo.RegisterComponent<UtilityResultBuffer>();

            SquadInputs.RegisterAll();
            UtilityInputReaderStore.Register(IdStub, &StubConst09);
        }

        public void Dispose()
        {
            UtilityInputReaderStore.Clear();
            _repo.Dispose();
        }

        private Entity CreateCommanderWithBlackboard()
        {
            var e = _repo.CreateEntity();
            _repo.AddComponent(e, new Blackboard1024());
            _repo.AddComponent(e, new SquadStateMarker());
            _repo.AddComponent(e, new UtilityResultBuffer());
            return e;
        }

        // ── SC-P3-03-1: ForceManeuverMapper sets ManeuverKind and flag ───────────

        [Fact]
        public void ForceManeuverMapper_SetsManeuverKindAndFlag()
        {
            var mapper = new ForceManeuverMapper();
            var commander = CreateCommanderWithBlackboard();

            bool ok = mapper.TryMap(commander, _repo,
                "{\"maneuverKind\":1}", out AssignBehaviorEvent assignment);

            Assert.True(ok);
            Assert.NotNull(assignment);

            ref var state = ref SquadCognitiveState.Project(
                ref _repo.GetComponentRW<Blackboard1024>(commander));
            Assert.Equal(1, state.ManeuverKind);
            Assert.NotEqual(0u, state.Flags & 1u);
        }

        // ── SC-P3-03-2: ClearForceManeuverMapper clears flag, scorer then runs ───

        [Fact]
        public void ClearForceManeuverMapper_ClearsFlagAndScorerResumes()
        {
            var forceMapper = new ForceManeuverMapper();
            var clearMapper = new ClearForceManeuverMapper();
            var commander = CreateCommanderWithBlackboard();

            // Force ManeuverKind = 1 with override.
            forceMapper.TryMap(commander, _repo, "{\"maneuverKind\":1}", out _);

            ref var state = ref SquadCognitiveState.Project(
                ref _repo.GetComponentRW<Blackboard1024>(commander));
            Assert.NotEqual(0u, state.Flags & 1u);

            // Clear override.
            bool ok = clearMapper.TryMap(commander, _repo, string.Empty, out AssignBehaviorEvent assignment);
            Assert.True(ok);
            Assert.Equal(0u, state.Flags & 1u);

            // Subsequent Run should not be blocked by override flag.
            var def = new UtilityDecisionDef
            {
                DebugName = "ClearTest",
                Kind      = DecisionKind.ManeuverSelect,
                Options   = new[]
                {
                    new UtilityOption
                    {
                        OptionId = 0,
                        Mode     = ScoringMode.WeightedProduct,
                        Considerations = new[]
                        {
                            new UtilityConsideration(IdStub, InputContext.Self, weight: 1f,
                                curve: new ResponseCurve(CurveKind.Linear, slope: 1f))
                        }
                    }
                }
            };
            CommanderUtilityTickSystem.Run(_repo, commander, in def, currentTick: 1);
            // ManeuverKind may change to 0 (the only option), confirming the scorer ran.
            Assert.Equal(0, state.ManeuverKind);
        }

        // ── SC-P3-03-3: No Blackboard1024 -> TryMap returns false ────────────────

        [Fact]
        public void ForceManeuverMapper_NoBlackboard_ReturnsFalse()
        {
            var mapper = new ForceManeuverMapper();
            var entity = _repo.CreateEntity();  // no Blackboard1024

            bool ok = mapper.TryMap(entity, _repo, "{\"maneuverKind\":2}", out _);
            Assert.False(ok);
        }
    }
}
