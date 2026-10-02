using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Replication.Abstractions;
using Fdp.Toolkit.Replication.Services;
using Xunit;

namespace Fdp.Toolkit.Replication.Tests
{
    /// <summary>
    /// ⭐ The ownership-group MECHANISM over small synthetic groups — <see cref="OwnershipGroupTable"/> and
    /// <see cref="DescriptorOwnershipMap.BindGroups"/>. 📄 <c>docs/DESIGN_Ownership_Groups_And_Grants.md</c> §2, §5.5 D-4.
    /// The production table is railed in <c>HrotOwnershipGroupsTests</c>; the NED binding in
    /// <c>TheDescriptorMapIsWiredTests</c>.
    /// </summary>
    public class OwnershipGroupBindingTests
    {
        // Synthetic component ids and descriptor ordinals.
        private const int BrainA = 10, BrainB = 11, BrainLinked = 12, MuscleA = 20, MuscleLinked = 21, CreatorC = 30, LocalL = 40;
        private const long DBrain1 = 100, DBrain2 = 101, DMuscle = 200, DCreator = 300, DMixed = 400;

        private static BitMask512 Mask(params int[] ids)
        {
            var m = default(BitMask512);
            foreach (var id in ids) m.SetBit(id);
            return m;
        }

        private static OwnershipGroupTable Table() => new(new[]
        {
            new OwnershipGroup(NodeRole.Brain,        Mask(BrainA, BrainB, BrainLinked), _ => true),
            new OwnershipGroup(NodeRole.MuscleGround, Mask(MuscleA, MuscleLinked),       _ => true),
            new OwnershipGroup(NodeRole.Map2D,        default,                           _ => true),
        }, Mask(LocalL));

        private static readonly Dictionary<NodeRole, long> Anchors = new()
        {
            [NodeRole.Brain] = DBrain1,
            [NodeRole.MuscleGround] = DMuscle,
        };

        [Fact]
        public void TheTableRejectsAComponentInTwoGroups()
        {
            Assert.Throws<ArgumentException>(() => new OwnershipGroupTable(new[]
            {
                new OwnershipGroup(NodeRole.Brain,        Mask(1), _ => true),
                new OwnershipGroup(NodeRole.MuscleGround, Mask(1), _ => true),
            }, default));
        }

        [Fact]
        public void TheTableRejectsAGroupThatContainsALocalComponent()
        {
            Assert.Throws<ArgumentException>(() => new OwnershipGroupTable(
                new[] { new OwnershipGroup(NodeRole.Brain, Mask(1), _ => true) }, Mask(1)));
        }

        [Fact]
        public void GroupOfNamesTheRoleAndLocalAndCreatorComponentsAreInNone()
        {
            var t = Table();
            Assert.Equal(NodeRole.Brain,        t.GroupOf(BrainB));
            Assert.Equal(NodeRole.MuscleGround, t.GroupOf(MuscleA));
            Assert.Equal(NodeRole.None,         t.GroupOf(CreatorC));
            Assert.Equal(NodeRole.None,         t.GroupOf(LocalL));
            Assert.True(t.IsLocal(LocalL));
        }

        [Fact]
        public void AnEmptyGroupNeverApplies()
        {
            var t = Table();
            Assert.False(t.Groups[NodeRole.Map2D].AppliesTo(new TkbTemplate("x", 1)));
            Assert.True(t.Groups[NodeRole.Brain].AppliesTo(new TkbTemplate("x", 1)));
        }

        [Fact]
        public void DescriptorsBindToTheGroupOfTheirComponents_LocalComponentsIgnored()
        {
            var map = new DescriptorOwnershipMap();
            map.RegisterMapping(DBrain1, BrainA);
            map.RegisterMapping(DBrain2, BrainB);
            map.RegisterMapping(DMuscle, MuscleA, LocalL);
            map.RegisterMapping(DCreator, CreatorC);

            map.BindGroups(Table(), Anchors);

            Assert.Empty(map.GroupBindingViolations);
            Assert.Equal(new[] { DBrain1, DBrain2 }, map.DescriptorsOf(NodeRole.Brain).ToArray());
            Assert.Equal(new[] { DMuscle },          map.DescriptorsOf(NodeRole.MuscleGround).ToArray());
            Assert.Empty(map.DescriptorsOf(NodeRole.Map2D));
        }

        [Fact]
        public void NeverSentMembersAreLinkedToTheAnchorOnly()
        {
            var map = new DescriptorOwnershipMap();
            map.RegisterMapping(DBrain1, BrainA);
            map.RegisterMapping(DBrain2, BrainB);
            map.RegisterMapping(DMuscle, MuscleA);

            map.BindGroups(Table(), Anchors);

            // D-4: the anchor drags the linked member; the other brain descriptor moves only its own component.
            Assert.Equal(new[] { BrainA, BrainLinked }, map.GetComponentIdsForDescriptor(DBrain1).ToArray());
            Assert.Equal(new[] { BrainB },              map.GetComponentIdsForDescriptor(DBrain2).ToArray());
            Assert.Equal(new[] { MuscleA, MuscleLinked }, map.GetComponentIdsForDescriptor(DMuscle).ToArray());
        }

        [Fact]
        public void LinkedMembersDoNotEnterTheReverseIndex()
        {
            var map = new DescriptorOwnershipMap();
            map.RegisterFromTranslator(DBrain1, new[] { BrainA });
            map.BindGroups(Table(), Anchors);

            // Never sent ⇒ writing one must not republish the anchor.
            Assert.Empty(map.GetDescriptorsForComponentId(BrainLinked).ToArray());
            Assert.Equal(new[] { DBrain1 }, map.GetDescriptorsForComponentId(BrainA).ToArray());
        }

        [Fact]
        public void ADescriptorSpanningTwoGroupsOrAGroupAndTheCreatorIsAViolation()
        {
            var map = new DescriptorOwnershipMap();
            map.RegisterMapping(DBrain1, BrainA);
            map.RegisterMapping(DMuscle, MuscleA);
            map.RegisterMapping(DMixed, BrainB, MuscleLinked);
            map.RegisterMapping(DMixed + 1, BrainB, CreatorC);

            map.BindGroups(Table(), Anchors);

            Assert.Equal(2, map.GroupBindingViolations.Count);
            Assert.DoesNotContain(DMixed, map.DescriptorsOf(NodeRole.Brain));
        }

        [Fact]
        public void AGroupWithNeverSentMembersButNoAnchorDescriptorIsAViolation()
        {
            var map = new DescriptorOwnershipMap();
            map.RegisterMapping(DBrain2, BrainB);      // the anchor DBrain1 is not registered
            map.RegisterMapping(DMuscle, MuscleA);

            map.BindGroups(Table(), Anchors);

            Assert.Single(map.GroupBindingViolations);
        }
    }
}
