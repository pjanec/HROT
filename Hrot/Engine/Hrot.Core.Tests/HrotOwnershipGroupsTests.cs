using System.Linq;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Hrot.Map.Common.Tests
{
    /// <summary>
    /// ⭐⭐ <b>The PRODUCTION ownership groups</b> — <see cref="HrotOwnershipGroups.Table"/>.
    /// 📄 <c>docs/DESIGN_Ownership_Groups_And_Grants.md</c> §1–§2. Each fact asserted here is a user ruling or a measured
    /// classification row; a change to the table that breaks one is a design change, not a test fix.
    /// </summary>
    public class HrotOwnershipGroupsTests
    {
        private static readonly Fdp.Toolkit.Replication.Abstractions.OwnershipGroupTable T = HrotOwnershipGroups.Table;

        [Fact]
        public void ThereIsExactlyOneGroupPerNodeRole_R172()
        {
            var roles = new[] { NodeRole.Brain, NodeRole.MuscleGround, NodeRole.Perception, NodeRole.NavigationSolver, NodeRole.Map2D };
            Assert.Equal(roles.OrderBy(r => r), T.Groups.Keys.OrderBy(r => r));
        }

        [Fact]
        public void DtWorldPosMovesWholeAsTheMuscleGroundGroup_R170()
        {
            foreach (int id in new[] { GlobalComponentIds.SimTransform, GlobalComponentIds.SimVelocity,
                                       GlobalComponentIds.VehicleState, GlobalComponentIds.VehicleParams, GlobalComponentIds.NavState })
                Assert.Equal(NodeRole.MuscleGround, T.GroupOf(id));
        }

        [Fact]
        public void DamageAndPerceptionIntentsStayWithTheBrain_R171()
        {
            Assert.Equal(NodeRole.Brain, T.GroupOf(GlobalComponentIds.CombatHealth));
            Assert.Equal(NodeRole.Brain, T.GroupOf(ComponentType<Fdp.Toolkit.Perception.Components.PerceptionReceptor>.ID));
            Assert.Equal(NodeRole.Brain, T.GroupOf(GlobalComponentIds.EqsSensor));
            Assert.Equal(NodeRole.Brain, T.GroupOf(NavigationContractsComponentIds.NavigationIntent));
            Assert.Equal(NodeRole.Brain, T.GroupOf(ComponentType<BehaviorState>.ID));
        }

        [Fact]
        public void PerceptionExecutionIsItsOwnGroupNotMuscleGround_R171()
        {
            Assert.Equal(NodeRole.Perception, T.GroupOf(GlobalComponentIds.EqsCognitiveBuffer));
            Assert.Equal(NodeRole.Perception, T.GroupOf(GlobalComponentIds.SensorEvalState));
            Assert.Equal(NodeRole.Perception, T.GroupOf(Fdp.Toolkit.Perception.PerceptionApplicationComponentIds.SensorContactList));
        }

        [Fact]
        public void NavigationSolverAndMap2DGroupsAreEmpty()
        {
            Assert.True(T.Groups[NodeRole.NavigationSolver].Members.IsEmpty());
            Assert.True(T.Groups[NodeRole.Map2D].Members.IsEmpty());     // R-161
        }

        [Fact]
        public void IdentityAndMapGeometryStayWithTheCreator_AndShadowsAreLocal()
        {
            Assert.Equal(NodeRole.None, T.GroupOf(GlobalComponentIds.NetworkIdentity));
            Assert.Equal(NodeRole.None, T.GroupOf(GlobalComponentIds.EntityInfo));
            Assert.True(T.IsLocal(GlobalComponentIds.NetworkTransform));
            Assert.True(T.IsLocal(GlobalComponentIds.NetworkVelocity));
        }

        [Fact]
        public void AGroupAppliesOnlyWhenTheTemplateProvisionsIt_G4()
        {
            var bare = new TkbTemplate("bare", 1);
            Assert.False(T.Groups[NodeRole.Brain].AppliesTo(bare));
            Assert.False(T.Groups[NodeRole.MuscleGround].AppliesTo(bare));
            Assert.False(T.Groups[NodeRole.Perception].AppliesTo(bare));

            var noBrain = new TkbTemplate("noBrain", 2);
            noBrain.AddDescriptor(new BehaviorProfileDto { BrainTier = 0 });
            noBrain.AddDescriptor(new SensorCapabilitiesDto { VisionRange = 0f });
            Assert.False(T.Groups[NodeRole.Brain].AppliesTo(noBrain));
            Assert.False(T.Groups[NodeRole.Perception].AppliesTo(noBrain));

            var tank = new TkbTemplate("tank", 3);
            tank.AddDescriptor(new BehaviorProfileDto { BrainTier = 1 });
            tank.AddDescriptor(new SensorCapabilitiesDto { VisionRange = 1000f });
            tank.AddDescriptor(new VehicleParametersDto());
            Assert.True(T.Groups[NodeRole.Brain].AppliesTo(tank));
            Assert.True(T.Groups[NodeRole.MuscleGround].AppliesTo(tank));
            Assert.True(T.Groups[NodeRole.Perception].AppliesTo(tank));
        }

        /// <summary>
        /// ⭐ <c>CE-523</c> (S8) rests on this: Health is in the BRAIN group, and only the Brain node runs
        /// <c>HealthApplicationSystem</c> (gated on the Health claim). A template that provisions Health
        /// (<see cref="CombatPlatformDefDto"/>) but no brain would keep Health with its creator — a SimHost or IG that
        /// applies no damage — so every production combat template must carry a brain. A new brainless combat template
        /// breaks this, and with it the damage path: run <c>HealthApplicationSystem</c> on every host before adding one.
        /// 📄 <c>docs/DESIGN_Ownership_Groups_And_Grants.md</c> §5.6 S8.
        /// </summary>
        [Fact]
        public void EveryProductionTemplateWithHealth_HasABrain_SoTheBrainNodeAppliesItsDamage_CE523()
        {
            var combat = HrotEnvironment.CreateTkb().GetAll().Where(t => t.GetDescriptor<CombatPlatformDefDto>() != null).ToList();
            Assert.NotEmpty(combat);
            Assert.All(combat, t => Assert.True(T.Groups[NodeRole.Brain].AppliesTo(t), $"{t.Name} has Health but no brain"));
        }
    }
}
