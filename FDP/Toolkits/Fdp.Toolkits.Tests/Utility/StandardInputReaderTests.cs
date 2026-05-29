using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Modules.Geographic.Components;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Utility;
using Xunit;

namespace Fdp.Toolkit.Tests.Utility
{
    /// <summary>
    /// Tests for all 17 Phase 1 standard input readers and the StandardInputIds hash constants.
    /// Success criteria: SC-P1-06-1 through SC-P1-06-5 and additional coverage.
    /// </summary>
    public class StandardInputReaderTests : IDisposable
    {
        private readonly UtilityTestWorld _world;

        public StandardInputReaderTests()
        {
            _world = new UtilityTestWorld();
            StandardInputs.RegisterAll();
        }

        public void Dispose()
        {
            UtilityInputReaderStore.Clear();
            _world.Dispose();
        }

        // ── helpers ─────────────────────────────────────────────────────────────

        private UtilityInputCtx MakeCtx(Entity self, Entity context = default, InputParams parms = default)
            => new UtilityInputCtx { Repo = _world.Repo, Self = self, Context = context, Params = parms };

        private static uint Fnv1a32(string name)
        {
            uint hash = 2166136261u;
            foreach (char c in name) { hash ^= (byte)c; hash *= 16777619u; }
            return hash;
        }

        // ── SC-P1-06-1: AmmoFraction ─────────────────────────────────────────────

        [Fact]
        public void AmmoFraction_ReturnsZero_WhenMaxAmmoIsZero()
        {
            var agent = _world.Repo.CreateEntity();
            _world.Repo.AddComponent(agent, new WeaponState { Ammo = 0, MaxAmmo = 0 });
            float result = StandardInputs.AmmoFraction(MakeCtx(agent));
            Assert.Equal(0f, result);
        }

        [Fact]
        public void AmmoFraction_ReturnsHalf_For15of30()
        {
            var agent = _world.Repo.CreateEntity();
            _world.Repo.AddComponent(agent, new WeaponState { Ammo = 15, MaxAmmo = 30 });
            float result = StandardInputs.AmmoFraction(MakeCtx(agent));
            Assert.Equal(0.5f, result, precision: 5);
        }

        [Fact]
        public void AmmoFraction_ClampsToOne_WhenAmmoExceedsMax()
        {
            var agent = _world.Repo.CreateEntity();
            _world.Repo.AddComponent(agent, new WeaponState { Ammo = 40, MaxAmmo = 30 });
            float result = StandardInputs.AmmoFraction(MakeCtx(agent));
            Assert.Equal(1.0f, result);
        }

        [Fact]
        public void AmmoFraction_ReturnsZero_WhenNoWeaponState()
        {
            var agent = _world.Repo.CreateEntity();
            float result = StandardInputs.AmmoFraction(MakeCtx(agent));
            Assert.Equal(0f, result);
        }

        // ── SC-P1-06-1: WeaponHasAmmo ────────────────────────────────────────────

        [Fact]
        public void WeaponHasAmmo_ReturnsOne_WhenAmmoPositive()
        {
            var agent = _world.Repo.CreateEntity();
            _world.Repo.AddComponent(agent, new WeaponState { Ammo = 1, MaxAmmo = 30 });
            Assert.Equal(1f, StandardInputs.WeaponHasAmmo(MakeCtx(agent)));
        }

        [Fact]
        public void WeaponHasAmmo_ReturnsZero_WhenAmmoIsZero()
        {
            var agent = _world.Repo.CreateEntity();
            _world.Repo.AddComponent(agent, new WeaponState { Ammo = 0, MaxAmmo = 30 });
            Assert.Equal(0f, StandardInputs.WeaponHasAmmo(MakeCtx(agent)));
        }

        // ── HealthFraction ────────────────────────────────────────────────────────

        [Fact]
        public void HealthFraction_ReturnsZero_WhenMaxIsZero()
        {
            var agent = _world.Repo.CreateEntity();
            _world.Repo.AddComponent(agent, new Health { Current = 0f, Max = 0f });
            Assert.Equal(0f, StandardInputs.HealthFraction(MakeCtx(agent)));
        }

        [Fact]
        public void HealthFraction_ReturnsHalf_WhenCurrentIsHalfMax()
        {
            var agent = _world.Repo.CreateEntity();
            _world.Repo.AddComponent(agent, new Health { Current = 50f, Max = 100f });
            Assert.Equal(0.5f, StandardInputs.HealthFraction(MakeCtx(agent)), precision: 5);
        }

        // ── SC-P1-06-4: DistanceToContext ────────────────────────────────────────

        [Fact]
        public void DistanceToContext_ReturnsOne_AtDistanceZero()
        {
            var self    = _world.SpawnAgent(1f, 1f);
            var contact = _world.Repo.CreateEntity();
            _world.Repo.AddComponent(contact, new Position { Value = Vector3.Zero });
            var parms   = new InputParams { MaxRange = 500f };
            float result = StandardInputs.DistanceToContext(MakeCtx(self, contact, parms));
            Assert.Equal(1f, result, precision: 5);
        }

        [Fact]
        public void DistanceToContext_ReturnsZero_AtMaxRange()
        {
            var self    = _world.SpawnAgent(1f, 1f);
            var contact = _world.Repo.CreateEntity();
            _world.Repo.AddComponent(contact, new Position { Value = new Vector3(500f, 0f, 0f) });
            var parms   = new InputParams { MaxRange = 500f };
            float result = StandardInputs.DistanceToContext(MakeCtx(self, contact, parms));
            Assert.Equal(0f, result, precision: 5);
        }

        [Fact]
        public void DistanceToContext_ReturnsHalf_AtHalfMaxRange()
        {
            var self    = _world.SpawnAgent(1f, 1f);
            var contact = _world.Repo.CreateEntity();
            _world.Repo.AddComponent(contact, new Position { Value = new Vector3(250f, 0f, 0f) });
            var parms   = new InputParams { MaxRange = 500f };
            float result = StandardInputs.DistanceToContext(MakeCtx(self, contact, parms));
            Assert.Equal(0.5f, result, precision: 4);
        }

        [Fact]
        public void DistanceToContext_ClampsToZero_BeyondMaxRange()
        {
            var self    = _world.SpawnAgent(1f, 1f);
            var contact = _world.Repo.CreateEntity();
            _world.Repo.AddComponent(contact, new Position { Value = new Vector3(1000f, 0f, 0f) });
            var parms   = new InputParams { MaxRange = 500f };
            float result = StandardInputs.DistanceToContext(MakeCtx(self, contact, parms));
            Assert.Equal(0f, result);
        }

        // ── Step-1.5: 3D reconciliation — multi-level fixture ─────────────────────

        [Fact]
        public void DistanceToContext_MultiLevel_PureAltitude_CountsInDistance()
        {
            // Contact directly above self (same X/Y, Z = 100 m).
            // With 2D distance the XY offset is 0 -> old score would be 1.0 (wrong).
            // With 3D distance the altitude gap = 100 m -> correct score = 1 - 100/200 = 0.5.
            var self    = _world.SpawnAgent(1f, 1f);
            var contact = _world.Repo.CreateEntity();
            _world.Repo.AddComponent(contact, new Position { Value = new Vector3(0f, 0f, 100f) });
            var parms   = new InputParams { MaxRange = 200f };
            float result = StandardInputs.DistanceToContext(MakeCtx(self, contact, parms));
            Assert.Equal(0.5f, result, precision: 4);
        }

        [Fact]
        public void DistanceToContext_MultiLevel_StreetVsBridge_SameXY_DifferentScores()
        {
            // Street contact: (100, 0, 0)  — 3D distance = 100 m       -> score = 0.8
            // Bridge contact: (100, 0, 40) — 3D distance ~= 107.703 m  -> score ~= 0.7846
            // On flat terrain (2D) both would have XY distance = 100 m -> identical score = 0.8.
            // With 3D distance the altitude-separated bridge contact is farther, so it scores lower.
            var self          = _world.SpawnAgent(1f, 1f);
            var streetContact = _world.Repo.CreateEntity();
            var bridgeContact = _world.Repo.CreateEntity();
            _world.Repo.AddComponent(streetContact, new Position { Value = new Vector3(100f, 0f,  0f) });
            _world.Repo.AddComponent(bridgeContact, new Position { Value = new Vector3(100f, 0f, 40f) });
            var parms         = new InputParams { MaxRange = 500f };
            float streetScore = StandardInputs.DistanceToContext(MakeCtx(self, streetContact, parms));
            float bridgeScore = StandardInputs.DistanceToContext(MakeCtx(self, bridgeContact, parms));
            // Street: exact 0.8 (100/500)
            Assert.Equal(0.8f, streetScore, precision: 4);
            // Bridge: 3D distance computed via Vector3.Distance matches the reader's own formula
            float expected3D = 1f - Vector3.Distance(Vector3.Zero, new Vector3(100f, 0f, 40f)) / 500f;
            Assert.Equal(expected3D, bridgeScore, precision: 4);
            // Altitude-separated contact must score strictly lower than its street-level counterpart.
            Assert.True(bridgeScore < streetScore);
        }

        // ── SC-P1-06-2: HasLineOfSight ────────────────────────────────────────────

        [Fact]
        public unsafe void HasLineOfSight_ReturnsOne_WhenVisualBitSet()
        {
            var self    = _world.SpawnAgent(1f, 1f);
            var contact = _world.Repo.CreateEntity();
            _world.SeedContact(self, contact, distanceM: 50f, threatBoost: 1f, contactHealth01: -1f, hasLos: true);
            float result = StandardInputs.HasLineOfSight(MakeCtx(self, contact));
            Assert.Equal(1f, result);
        }

        [Fact]
        public unsafe void HasLineOfSight_ReturnsZero_WhenOnlyAcousticBitSet()
        {
            var self    = _world.SpawnAgent(1f, 1f);
            var contact = _world.Repo.CreateEntity();
            _world.SeedContact(self, contact, distanceM: 50f, threatBoost: 1f, contactHealth01: -1f, hasLos: false);
            float result = StandardInputs.HasLineOfSight(MakeCtx(self, contact));
            Assert.Equal(0f, result);
        }

        [Fact]
        public void HasLineOfSight_ReturnsZero_WhenContactNotInMemory()
        {
            var self    = _world.SpawnAgent(1f, 1f);
            var contact = _world.Repo.CreateEntity();
            // contact never seeded into self's TargetMemory
            float result = StandardInputs.HasLineOfSight(MakeCtx(self, contact));
            Assert.Equal(0f, result);
        }

        [Fact]
        public unsafe void HasLineOfSight_ReturnsZero_VisualAndAcousticBothUnset()
        {
            // Seed with acoustic only (hasLos=false), then verify Visual bit is 0
            var self    = _world.SpawnAgent(1f, 1f);
            var contact = _world.Repo.CreateEntity();
            _world.SeedContact(self, contact, distanceM: 10f, threatBoost: 1f, contactHealth01: -1f, hasLos: false);
            ref readonly var mem = ref _world.Repo.GetComponentRO<TargetMemory>(self);
            // Acoustic bit should be set, Visual should not
            Assert.Equal(0, mem.Modalities[0] & (byte)Fdp.Toolkit.Perception.Components.SensorModality.Visual);
            Assert.NotEqual(0, mem.Modalities[0] & (byte)Fdp.Toolkit.Perception.Components.SensorModality.Acoustic);
        }

        // ── HaveLiveTarget ────────────────────────────────────────────────────────

        [Fact]
        public void HaveLiveTarget_ReturnsOne_WhenContactExists()
        {
            var self    = _world.SpawnAgent(1f, 1f);
            var contact = _world.Repo.CreateEntity();
            _world.SeedContact(self, contact, 50f, 1f, -1f, true);
            Assert.Equal(1f, StandardInputs.HaveLiveTarget(MakeCtx(self)));
        }

        [Fact]
        public void HaveLiveTarget_ReturnsZero_WhenNoContacts()
        {
            var self = _world.SpawnAgent(1f, 1f);
            Assert.Equal(0f, StandardInputs.HaveLiveTarget(MakeCtx(self)));
        }

        // ── ContactThreatLevel ────────────────────────────────────────────────────

        [Fact]
        public void ContactThreatLevel_ReturnsScore_WhenContactFound()
        {
            var self    = _world.SpawnAgent(1f, 1f);
            var contact = _world.Repo.CreateEntity();
            _world.SeedContact(self, contact, 50f, 0.75f, -1f, true);
            float result = StandardInputs.ContactThreatLevel(MakeCtx(self, contact));
            Assert.Equal(0.75f, result, precision: 4);
        }

        [Fact]
        public void ContactThreatLevel_ReturnsZero_WhenContactNotFound()
        {
            var self    = _world.SpawnAgent(1f, 1f);
            var contact = _world.Repo.CreateEntity();
            float result = StandardInputs.ContactThreatLevel(MakeCtx(self, contact));
            Assert.Equal(0f, result);
        }

        // ── SC-P1-06-3: EqsTopScore ──────────────────────────────────────────────

        [Fact]
        public void EqsTopScore_ReturnsTopScore_WhenReady()
        {
            var agent     = _world.SpawnAgent(1f, 1f);
            uint bpId     = UtilityTestWorld.Fnv1a32("CoverQuery");
            _world.SpawnEqsSensor(agent, bpId, topScore: 0.8f, count: 2, instanceId: 0);
            var parms     = new InputParams { BlueprintId = bpId };
            float result  = StandardInputs.EqsTopScore(MakeCtx(agent, default, parms));
            Assert.Equal(0.8f, result, precision: 4);
        }

        [Fact]
        public void EqsTopScore_ReturnsZero_WhenNoBlueprintIdMatch()
        {
            var agent    = _world.SpawnAgent(1f, 1f);
            uint bpId    = UtilityTestWorld.Fnv1a32("CoverQuery");
            _world.SpawnEqsSensor(agent, bpId, topScore: 0.8f, count: 2, instanceId: 0);
            var parms    = new InputParams { BlueprintId = 0xDEADBEEFu };
            float result = StandardInputs.EqsTopScore(MakeCtx(agent, default, parms));
            Assert.Equal(0f, result);
        }

        [Fact]
        public void EqsTopScore_ReturnsZero_WhenNotReady()
        {
            var agent     = _world.SpawnAgent(1f, 1f);
            uint bpId     = UtilityTestWorld.Fnv1a32("FlankQuery");
            var sensor    = _world.Repo.CreateEntity();
            // Add sensor with LastUpdateTick == 0 (not ready)
            _world.Repo.AddComponent(sensor, new EqsSensor { BlueprintId = bpId });
            _world.Repo.AddComponent(sensor, new EqsCognitiveBuffer { Count = 1, LastUpdateTick = 0 });
            _world.Repo.AddComponent(sensor, new Fdp.Toolkit.Replication.Components.PartMetadata
            {
                ParentEntity = agent, InstanceId = 0
            });
            var parms    = new InputParams { BlueprintId = bpId };
            float result = StandardInputs.EqsTopScore(MakeCtx(agent, default, parms));
            Assert.Equal(0f, result);
        }

        // ── SC-P1-06-5: IsAssignedTarget ─────────────────────────────────────────

        [Fact]
        public unsafe void IsAssignedTarget_ReturnsOne_WhenAssignmentMatches()
        {
            var leader = _world.SpawnLeader();
            var member = _world.SpawnSquadMember(leader, 1f, 1f);
            var target = _world.Repo.CreateEntity();

            // Write assignment into leader's blackboard
            ref var bb    = ref _world.Repo.GetComponentRW<Blackboard1024>(leader);
            ref var state = ref ThreatMatrixAssignmentState.Project(ref bb);
            ref var roster = ref _world.Repo.GetComponentRW<UnitRoster>(leader);
            int idx = UnitRoster.IndexOf(ref roster, (long)member.PackedValue);
            state.GetSlot(idx).AssignedTargetHandle = (long)target.PackedValue;

            float result = StandardInputs.IsAssignedTarget(MakeCtx(member, target));
            Assert.Equal(1f, result);
        }

        [Fact]
        public unsafe void IsAssignedTarget_ReturnsZero_WhenDifferentTarget()
        {
            var leader  = _world.SpawnLeader();
            var member  = _world.SpawnSquadMember(leader, 1f, 1f);
            var target1 = _world.Repo.CreateEntity();
            var target2 = _world.Repo.CreateEntity();

            // Assign target1, query for target2
            ref var bb    = ref _world.Repo.GetComponentRW<Blackboard1024>(leader);
            ref var state = ref ThreatMatrixAssignmentState.Project(ref bb);
            ref var roster = ref _world.Repo.GetComponentRW<UnitRoster>(leader);
            int idx = UnitRoster.IndexOf(ref roster, (long)member.PackedValue);
            state.GetSlot(idx).AssignedTargetHandle = (long)target1.PackedValue;

            float result = StandardInputs.IsAssignedTarget(MakeCtx(member, target2));
            Assert.Equal(0f, result);
        }

        [Fact]
        public void IsAssignedTarget_ReturnsOne_WhenNoSubordinate()
        {
            // Units with no UnitSubordinate are not managed by the assignment system;
            // the input is a neutral pass (1f) so it does not veto WeightedProduct scores.
            var agent  = _world.SpawnAgent(1f, 1f);
            var target = _world.Repo.CreateEntity();
            Assert.Equal(1f, StandardInputs.IsAssignedTarget(MakeCtx(agent, target)));
        }

        // ── Constant ─────────────────────────────────────────────────────────────

        [Fact]
        public void Constant_ReturnsParamsMaxRange_Clamped()
        {
            var agent   = _world.SpawnAgent(1f, 1f);
            var parms   = new InputParams { MaxRange = 0.7f };
            float result = StandardInputs.Constant(MakeCtx(agent, default, parms));
            Assert.Equal(0.7f, result, precision: 5);
        }

        // ── AssignmentFor helper (via UtilityTestWorld.AssignmentFor) ─────────────

        [Fact]
        public unsafe void AssignmentFor_ReturnsAssignedTarget_WhenSet()
        {
            var leader = _world.SpawnLeader();
            var member = _world.SpawnSquadMember(leader, 1f, 1f);
            var target = _world.Repo.CreateEntity();

            ref var bb    = ref _world.Repo.GetComponentRW<Blackboard1024>(leader);
            ref var state = ref ThreatMatrixAssignmentState.Project(ref bb);
            ref var roster = ref _world.Repo.GetComponentRW<UnitRoster>(leader);
            int idx = UnitRoster.IndexOf(ref roster, (long)member.PackedValue);
            state.GetSlot(idx).AssignedTargetHandle = (long)target.PackedValue;

            long result = _world.AssignmentFor(leader, member);
            Assert.Equal((long)target.PackedValue, result);
        }

        [Fact]
        public void AssignmentFor_ReturnsNegativeOne_WhenMemberNotInRoster()
        {
            var leader = _world.SpawnLeader();
            var stranger = _world.SpawnAgent(1f, 1f); // not added to roster
            long result = _world.AssignmentFor(leader, stranger);
            Assert.Equal(-1L, result);
        }

        // ── SC hash values pin test ───────────────────────────────────────────────

        [Fact]
        public void StandardInputIds_HashValues_MatchFnv1a32()
        {
            Assert.Equal((ushort)(Fnv1a32("AmmoFraction")          & 0xFFFF), StandardInputIds.AmmoFraction);
            Assert.Equal((ushort)(Fnv1a32("WeaponHasAmmo")         & 0xFFFF), StandardInputIds.WeaponHasAmmo);
            Assert.Equal((ushort)(Fnv1a32("WeaponReadiness")       & 0xFFFF), StandardInputIds.WeaponReadiness);
            Assert.Equal((ushort)(Fnv1a32("HealthFraction")        & 0xFFFF), StandardInputIds.HealthFraction);
            Assert.Equal((ushort)(Fnv1a32("ContactHealthFraction") & 0xFFFF), StandardInputIds.ContactHealthFraction);
            Assert.Equal((ushort)(Fnv1a32("DistanceToContext")     & 0xFFFF), StandardInputIds.DistanceToContext);
            Assert.Equal((ushort)(Fnv1a32("ContactThreatLevel")    & 0xFFFF), StandardInputIds.ContactThreatLevel);
            Assert.Equal((ushort)(Fnv1a32("HasLineOfSight")        & 0xFFFF), StandardInputIds.HasLineOfSight);
            Assert.Equal((ushort)(Fnv1a32("HaveLiveTarget")        & 0xFFFF), StandardInputIds.HaveLiveTarget);
            Assert.Equal((ushort)(Fnv1a32("EnemyStrengthRatio")    & 0xFFFF), StandardInputIds.EnemyStrengthRatio);
            Assert.Equal((ushort)(Fnv1a32("EqsTopScore")           & 0xFFFF), StandardInputIds.EqsTopScore);
            Assert.Equal((ushort)(Fnv1a32("EqsResultCount")        & 0xFFFF), StandardInputIds.EqsResultCount);
            Assert.Equal((ushort)(Fnv1a32("IsAssignedTarget")      & 0xFFFF), StandardInputIds.IsAssignedTarget);
            Assert.Equal((ushort)(Fnv1a32("AllyAdvancingNearby")   & 0xFFFF), StandardInputIds.AllyAdvancingNearby);
            Assert.Equal((ushort)(Fnv1a32("Constant")              & 0xFFFF), StandardInputIds.Constant);
            Assert.Equal((ushort)(Fnv1a32("WeaponRangeBandFit")    & 0xFFFF), StandardInputIds.WeaponRangeBandFit);
            Assert.Equal((ushort)(Fnv1a32("WeaponEffectivenessVsTarget") & 0xFFFF), StandardInputIds.WeaponEffectivenessVsTarget);
        }
    }
}
