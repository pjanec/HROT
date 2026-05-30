using System;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.DangerArea;
using Fdp.Toolkit.Squad.StarterPack;
using Fdp.Toolkit.Squad.Systems;
using Fdp.Toolkit.Utility;
using Xunit;

namespace Fdp.Toolkit.Squad.Tests
{
    /// <summary>
    /// End-to-end integration tests for Phase 3 ManeuverSelect pipeline.
    /// Success criteria: SC-P3-04-1 through SC-P3-04-4.
    /// </summary>
    public unsafe class Phase3IntegrationTests : IDisposable
    {
        private EntityRepository       _repo;
        private UtilityDecisionDef     _def;
        private Entity                 _commander;

        public Phase3IntegrationTests()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<UnitRoster>();
            _repo.RegisterComponent<UnitSubordinate>();
            _repo.RegisterComponent<Blackboard1024>();
            _repo.RegisterComponent<SquadStateMarker>();
            _repo.RegisterComponent<Health>();
            _repo.RegisterComponent<WeaponState>();
            _repo.RegisterComponent<DangerAreaCognitiveBuffer>();
            _repo.RegisterComponent<UtilityResultBuffer>();
            _repo.RegisterComponent<UtilityTraceWorkingMemory1024>();

            SquadInputs.RegisterAll();
            _def = ManeuverSelectStarterDecision.Build();

            // Build commander with everything needed.
            _commander = _repo.CreateEntity();
            _repo.AddComponent(_commander, new UnitRoster());
            _repo.AddComponent(_commander, new Blackboard1024());
            _repo.AddComponent(_commander, new SquadStateMarker());
            _repo.AddComponent(_commander, new UtilityResultBuffer());
            _repo.AddComponent(_commander, new UtilityTraceWorkingMemory1024());
            _repo.AddComponent(_commander, new DangerAreaCognitiveBuffer());

            // Add 2 members with full health and full ammo.
            for (int i = 0; i < 2; i++)
            {
                var m = _repo.CreateEntity();
                _repo.AddComponent(m, new Health { Current = 100f, Max = 100f });
                _repo.AddComponent(m, new WeaponState { Ammo = 100, MaxAmmo = 100 });
                ref var roster = ref _repo.GetComponentRW<UnitRoster>(_commander);
                UnitRoster.Add(ref roster, (long)m.PackedValue);
            }
        }

        public void Dispose()
        {
            UtilityInputReaderStore.Clear();
            _repo.Dispose();
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private void SetupFeature(uint featureId, DangerAreaKind kind, float threatRating = 0.8f)
        {
            ref var state = ref SquadCognitiveState.Project(
                ref _repo.GetComponentRW<Blackboard1024>(_commander));
            state.ActiveFeatureId = featureId;

            ref var buf = ref _repo.GetComponentRW<DangerAreaCognitiveBuffer>(_commander);
            buf.Count = 1;
            buf.GetSpanRW()[0] = new DangerAreaDescriptor
            {
                FeatureId    = featureId,
                Kind         = kind,
                ThreatRating = threatRating
            };
        }

        private ref SquadCognitiveState State() =>
            ref SquadCognitiveState.Project(ref _repo.GetComponentRW<Blackboard1024>(_commander));

        // ── SC-P3-04-1: StreetCrossing feature -> DangerAreaCross (option 0) ─────

        [Fact]
        public void Run_StreetCrossingFeature_SelectsDangerAreaCross()
        {
            SetupFeature(1u, DangerAreaKind.StreetCrossing, threatRating: 0.2f);

            ref var state = ref State();
            state.Contacts.LastManeuverSelectTick = 0;

            CommanderUtilityTickSystem.Run(_repo, _commander, in _def, currentTick: 1);

            Assert.Equal(ManeuverSelectStarterDecision.OptionIdDangerAreaCross, state.ManeuverKind);
        }

        // ── SC-P3-04-2: OpenGround feature -> BoundOverwatch (option 1) ──────────

        [Fact]
        public void Run_OpenGroundFeature_SelectsBoundOverwatch()
        {
            SetupFeature(2u, DangerAreaKind.OpenGround, threatRating: 0.7f);

            ref var state = ref State();
            state.Contacts.LastManeuverSelectTick = 0;

            CommanderUtilityTickSystem.Run(_repo, _commander, in _def, currentTick: 100);

            Assert.Equal(ManeuverSelectStarterDecision.OptionIdBoundOverwatch, state.ManeuverKind);
        }

        // ── SC-P3-04-3: Trace populated after run ─────────────────────────────────

        [Fact]
        public void Run_TraceEnabled_RecordCountNonZero()
        {
            SetupFeature(1u, DangerAreaKind.StreetCrossing, threatRating: 0.2f);

            ref var state = ref State();
            state.Contacts.LastManeuverSelectTick = 0;

            CommanderUtilityTickSystem.Run(_repo, _commander, in _def, currentTick: 1);

            ref var trace = ref _repo.GetComponentRW<UtilityTraceWorkingMemory1024>(_commander);
            Assert.True(trace.RecordCount > 0);
        }

        // ── SC-P3-04-4: MissionOverride blocks scorer ─────────────────────────────

        [Fact]
        public void Run_MissionOverrideSet_ManeuverKindUnchanged()
        {
            SetupFeature(1u, DangerAreaKind.StreetCrossing);

            ref var state = ref State();
            state.ManeuverKind = 2;
            state.Flags |= 1u;  // MissionOverrideBit

            CommanderUtilityTickSystem.Run(_repo, _commander, in _def, currentTick: 1000);

            Assert.Equal(2, state.ManeuverKind);
        }
    }
}
