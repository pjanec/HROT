using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.DangerArea;
using Fdp.Toolkit.Utility;
using Xunit;

namespace Fdp.Toolkit.Squad.Tests.Inputs
{
    /// <summary>
    /// Tests for the Phase 3 squad-commander Utility input readers.
    /// Success criteria: SC-P3-02-1 through SC-P3-02-5.
    /// </summary>
    public unsafe class SquadInputsP3Tests : IDisposable
    {
        private EntityRepository _repo;

        public SquadInputsP3Tests()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<UnitRoster>();
            _repo.RegisterComponent<UnitSubordinate>();
            _repo.RegisterComponent<Blackboard1024>();
            _repo.RegisterComponent<SquadStateMarker>();
            _repo.RegisterComponent<Health>();
            _repo.RegisterComponent<WeaponState>();
            _repo.RegisterComponent<DangerAreaCognitiveBuffer>();

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

        private Entity AddMember(Entity commander, Health? health = null, WeaponState? weapon = null)
        {
            var m = _repo.CreateEntity();
            if (health.HasValue)    _repo.AddComponent(m, health.Value);
            if (weapon.HasValue)    _repo.AddComponent(m, weapon.Value);
            ref var roster = ref _repo.GetComponentRW<UnitRoster>(commander);
            UnitRoster.Add(ref roster, (long)m.PackedValue);
            return m;
        }

        private UtilityInputCtx MakeCtx(Entity self, InputParams @params = default) =>
            new UtilityInputCtx { Repo = _repo, Self = self, Context = Entity.Null, Params = @params };

        // ── SC-P3-02-1: SquadStrengthRatio ───────────────────────────────────────

        [Fact]
        public void SquadStrengthRatio_AllFullHealth_Returns1()
        {
            var cmd = CreateCommander();
            AddMember(cmd, health: new Health { Current = 100f, Max = 100f });
            AddMember(cmd, health: new Health { Current = 100f, Max = 100f });

            float ratio = SquadInputs.SquadStrengthRatio(MakeCtx(cmd));
            Assert.Equal(1.0f, ratio, precision: 4);
        }

        [Fact]
        public void SquadStrengthRatio_OneMemberDead_Returns0Point5()
        {
            var cmd = CreateCommander();
            AddMember(cmd, health: new Health { Current = 0f,   Max = 100f });
            AddMember(cmd, health: new Health { Current = 100f, Max = 100f });

            float ratio = SquadInputs.SquadStrengthRatio(MakeCtx(cmd));
            Assert.Equal(0.5f, ratio, precision: 4);
        }

        [Fact]
        public void SquadStrengthRatio_NoUnitRoster_Returns1()
        {
            // Commander without UnitRoster.
            var e = _repo.CreateEntity();
            _repo.AddComponent(e, new Blackboard1024());

            float ratio = SquadInputs.SquadStrengthRatio(MakeCtx(e));
            Assert.Equal(1.0f, ratio, precision: 4);
        }

        // ── SC-P3-02-2: ActiveFeatureThreatRating with no active feature ─────────

        [Fact]
        public void ActiveFeatureThreatRating_NoActiveFeature_Returns0()
        {
            var cmd = CreateCommander();
            _repo.AddComponent(cmd, new DangerAreaCognitiveBuffer());

            // ActiveFeatureId == 0 by default.
            float result = SquadInputs.ActiveFeatureThreatRating(MakeCtx(cmd));
            Assert.Equal(0f, result);
        }

        // ── SC-P3-02-3: ActiveFeatureKindIs flip ─────────────────────────────────

        [Fact]
        public void ActiveFeatureKindIs_MatchAndNonMatch_Flip()
        {
            var cmd = CreateCommander();
            _repo.AddComponent(cmd, new DangerAreaCognitiveBuffer());

            // Set active feature.
            ref var state = ref SquadCognitiveState.Project(
                ref _repo.GetComponentRW<Blackboard1024>(cmd));
            state.ActiveFeatureId = 42u;

            // Write descriptor into buffer.
            ref var buf = ref _repo.GetComponentRW<DangerAreaCognitiveBuffer>(cmd);
            buf.Count = 1;
            buf.GetSpanRW()[0] = new DangerAreaDescriptor
            {
                FeatureId   = 42u,
                Kind        = DangerAreaKind.StreetCrossing,
                ThreatRating = 0.8f
            };

            // Matching kind returns 1f.
            float matchResult = SquadInputs.ActiveFeatureKindIs(MakeCtx(cmd,
                new InputParams { BlueprintId = (uint)DangerAreaKind.StreetCrossing }));
            Assert.Equal(1f, matchResult);

            // Non-matching kind returns 0f.
            float noMatchResult = SquadInputs.ActiveFeatureKindIs(MakeCtx(cmd,
                new InputParams { BlueprintId = (uint)DangerAreaKind.OpenGround }));
            Assert.Equal(0f, noMatchResult);

            // Feature not in buffer returns 0f.
            state.ActiveFeatureId = 99u;
            float notFoundResult = SquadInputs.ActiveFeatureKindIs(MakeCtx(cmd,
                new InputParams { BlueprintId = (uint)DangerAreaKind.StreetCrossing }));
            Assert.Equal(0f, notFoundResult);
        }

        // ── SC-P3-02-4: SquadAmmoRollup ──────────────────────────────────────────

        [Fact]
        public void SquadAmmoRollup_AllFull_Returns1()
        {
            var cmd = CreateCommander();
            AddMember(cmd, weapon: new WeaponState { Ammo = 100, MaxAmmo = 100 });
            AddMember(cmd, weapon: new WeaponState { Ammo = 100, MaxAmmo = 100 });

            float ratio = SquadInputs.SquadAmmoRollup(MakeCtx(cmd));
            Assert.Equal(1.0f, ratio, precision: 4);
        }

        [Fact]
        public void SquadAmmoRollup_OneMemberEmpty_Returns0Point5()
        {
            var cmd = CreateCommander();
            AddMember(cmd, weapon: new WeaponState { Ammo = 0,   MaxAmmo = 100 });
            AddMember(cmd, weapon: new WeaponState { Ammo = 100, MaxAmmo = 100 });

            float ratio = SquadInputs.SquadAmmoRollup(MakeCtx(cmd));
            Assert.Equal(0.5f, ratio, precision: 4);
        }

        [Fact]
        public void SquadAmmoRollup_NoWeaponState_Returns1()
        {
            var cmd = CreateCommander();
            // Members with no WeaponState (only Health).
            AddMember(cmd, health: new Health { Current = 100f, Max = 100f });
            AddMember(cmd, health: new Health { Current = 100f, Max = 100f });

            float ratio = SquadInputs.SquadAmmoRollup(MakeCtx(cmd));
            Assert.Equal(1.0f, ratio, precision: 4);
        }

        // ── SC-P3-02-5: Zero-alloc ────────────────────────────────────────────────

        [Fact]
        public void AllReaders_ZeroAlloc_After1MillionCalls()
        {
            const int Iterations = 1_000_000;

            var cmd = CreateCommander();
            _repo.AddComponent(cmd, new DangerAreaCognitiveBuffer());
            AddMember(cmd, health: new Health { Current = 80f, Max = 100f },
                          weapon: new WeaponState { Ammo = 80, MaxAmmo = 100 });

            // Set up a descriptor so readers have something to find.
            ref var state = ref SquadCognitiveState.Project(
                ref _repo.GetComponentRW<Blackboard1024>(cmd));
            state.ActiveFeatureId = 1u;
            ref var buf = ref _repo.GetComponentRW<DangerAreaCognitiveBuffer>(cmd);
            buf.Count = 1;
            buf.GetSpanRW()[0] = new DangerAreaDescriptor
            {
                FeatureId    = 1u,
                Kind         = DangerAreaKind.StreetCrossing,
                ThreatRating = 0.5f
            };

            var ctx = MakeCtx(cmd, new InputParams { BlueprintId = (uint)DangerAreaKind.StreetCrossing });

            // Warm-up: JIT compile all readers before measuring.
            for (int i = 0; i < 10; i++)
            {
                SquadInputs.SquadStrengthRatio(ctx);
                SquadInputs.SquadAmmoRollup(ctx);
                SquadInputs.ActiveFeatureThreatRating(ctx);
                SquadInputs.ActiveFeatureKindIs(ctx);
                SquadInputs.SquadPoolThreatAggregate(ctx);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();

            for (int i = 0; i < Iterations; i++)
            {
                SquadInputs.SquadStrengthRatio(ctx);
                SquadInputs.SquadAmmoRollup(ctx);
                SquadInputs.ActiveFeatureThreatRating(ctx);
                SquadInputs.ActiveFeatureKindIs(ctx);
                SquadInputs.SquadPoolThreatAggregate(ctx);
            }

            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.True(after - before <= 64,
                $"Allocated {after - before} bytes over {Iterations} iterations (expected 0, tolerance 64).");
        }
    }
}
