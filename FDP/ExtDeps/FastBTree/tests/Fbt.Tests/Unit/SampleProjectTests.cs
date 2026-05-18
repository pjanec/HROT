using System.Runtime.InteropServices;
using Fbt;
using Fbt.Examples.FluentBTree;

namespace Fbt.Tests.Unit
{
    public class SampleProjectTests
    {
        // FBT-040 SC1
        [Fact]
        public void CombatBlackboard_AmmoCount_IsAtOffset0()
        {
            Assert.Equal(0, (int)Marshal.OffsetOf<CombatBlackboard>("AmmoCount"));
        }

        // FBT-040 SC2
        [Fact]
        public void CombatBlackboard_EngagementRange_IsAtOffset8()
        {
            Assert.Equal(8, (int)Marshal.OffsetOf<CombatBlackboard>("EngagementRange"));
        }

        // FBT-041 SC1
        [Fact]
        public void CheckAmmo_ZeroAmmo_ReturnsFailure()
        {
            int ammo = 0;
            var state = new BehaviorTreeState();
            var ctx = new CombatContext();
            Assert.Equal(NodeStatus.Failure, CombatActions.CheckAmmo(ref ammo, ref state, ref ctx));
        }

        // FBT-041 SC1 (positive case)
        [Fact]
        public void CheckAmmo_NonZeroAmmo_ReturnsSuccess()
        {
            int ammo = 3;
            var state = new BehaviorTreeState();
            var ctx = new CombatContext();
            Assert.Equal(NodeStatus.Success, CombatActions.CheckAmmo(ref ammo, ref state, ref ctx));
        }

        // FBT-041 SC2
        [Fact]
        public void AimAndFire_DecrementsAmmo()
        {
            int ammo = 5;
            var state = new BehaviorTreeState();
            var ctx = new CombatContext();
            CombatActions.AimAndFire(ref ammo, ref state, ref ctx);
            Assert.Equal(4, ammo);
        }

        // FBT-041 SC3
        [Fact]
        public void HoldPosition_ReturnsRunningThenSuccess()
        {
            var bb = new CombatBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new CombatContext();

            var r1 = CombatActions.HoldPosition(ref bb, ref state, ref ctx, 0);
            var r2 = CombatActions.HoldPosition(ref bb, ref state, ref ctx, 0);

            Assert.Equal(NodeStatus.Running, r1);
            Assert.Equal(NodeStatus.Success, r2);
        }

        // FBT-042 SC1
        [Fact]
        public void BuildAmbushTree_HasCorrectTreeName()
        {
            var blob = AmbushTree.BuildAmbushTree();
            Assert.Equal("Ambush_BT", blob.TreeName);
        }

        // FBT-042 SC2
        [Fact]
        public void BuildAmbushTree_HasCorrectNodeStructure()
        {
            var blob = AmbushTree.BuildAmbushTree();
            Assert.Equal(6, blob.Nodes.Length);
            Assert.Equal(NodeType.Selector,  blob.Nodes[0].Type);
            Assert.Equal(NodeType.Sequence,  blob.Nodes[1].Type);
            Assert.Equal(NodeType.Condition, blob.Nodes[2].Type);
            Assert.Equal(NodeType.Condition, blob.Nodes[3].Type);
            Assert.Equal(NodeType.Action,    blob.Nodes[4].Type);
            Assert.Equal(NodeType.Action,    blob.Nodes[5].Type);
        }

        // FBT-044 integration: threat visible + ammo available -> AimAndFire runs
        [Fact]
        public void AmbushTree_ThreatVisibleWithAmmo_ExecutesAimAndFire()
        {
            var interpreter = AmbushTree.CreateInterpreter();
            var bb = new CombatBlackboard { ThreatVisible = true, AmmoCount = 3 };
            var state = new BehaviorTreeState();
            var ctx = new CombatContext();

            interpreter.Tick(ref bb, ref state, ref ctx);

            // AimAndFire should have decremented ammo
            Assert.Equal(2, bb.AmmoCount);
        }

        // FBT-044 integration: no threat -> selector falls back to HoldPosition
        [Fact]
        public void AmbushTree_NoThreat_FallsBackToHoldPosition()
        {
            var interpreter = AmbushTree.CreateInterpreter();
            var bb = new CombatBlackboard { ThreatVisible = false, AmmoCount = 10 };
            var state = new BehaviorTreeState();
            var ctx = new CombatContext();

            // Tick 1: HoldPosition returns Running
            var r1 = interpreter.Tick(ref bb, ref state, ref ctx);
            // Tick 2: HoldPosition returns Success
            var r2 = interpreter.Tick(ref bb, ref state, ref ctx);

            Assert.Equal(NodeStatus.Running, r1);
            Assert.Equal(NodeStatus.Success, r2);
            // Ammo must be unchanged (sequence never reached AimAndFire)
            Assert.Equal(10, bb.AmmoCount);
        }

        // FBT-044 integration: threat visible + no ammo -> selector falls back to HoldPosition
        [Fact]
        public void AmbushTree_ThreatVisibleNoAmmo_FallsBackToHoldPosition()
        {
            var interpreter = AmbushTree.CreateInterpreter();
            var bb = new CombatBlackboard { ThreatVisible = true, AmmoCount = 0 };
            var state = new BehaviorTreeState();
            var ctx = new CombatContext();

            var r1 = interpreter.Tick(ref bb, ref state, ref ctx);
            var r2 = interpreter.Tick(ref bb, ref state, ref ctx);

            Assert.Equal(NodeStatus.Running, r1);
            Assert.Equal(NodeStatus.Success, r2);
        }
    }
}
