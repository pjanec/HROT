using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐ Buildings Stage 5d-2 (📄 docs/DESIGN_Building_Interiors.md §3j "5d-2 as built") — <c>DoorLocksmith</c>, a curated C# tree
    /// (R-223: test behaviours are C#; this one drives the <c>bt-doors</c> scenario): walk to the door, unlock it, open it, walk
    /// through to the objective. Built only from the shared door nodes (<see cref="DoorNodes"/>) and the MoveTo channel node.
    /// </summary>
    public static class DoorLocksmithBehavior
    {
        /// <summary>The blackboard: one params block per node (the verbs differ, the door is the same).</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct DoorLocksmithBlackboard
        {
            public MoveToDoorParams Approach;
            public OperateDoorParams Unlock;
            public OperateDoorParams Open;
            public CgfNodes.MoveToLocationParams Enter;
        }

        /// <summary>The authored contract in, the blackboard out: one door key fills the three door nodes.</summary>
        [BehaviorResolver(BehaviorNames.DoorLocksmith)]
        public static void Resolve(in DoorLocksmithParamsJsonDto authored, ref DoorLocksmithBlackboard block, EntityRepository world, Entity self)
        {
            block.Approach = new MoveToDoorParams { Door = authored.Door, Speed = authored.Speed };
            block.Unlock = new OperateDoorParams { Door = authored.Door, Verb = DoorVerb.Unlock };
            block.Open = new OperateDoorParams { Door = authored.Door, Verb = DoorVerb.Open };
            block.Enter = new CgfNodes.MoveToLocationParams
            {
                X = authored.X, Y = authored.Y, ArrivalRadius = 1f,
                Speed = authored.Speed > 0f ? authored.Speed : DoorNodes.DefaultSpeed,
            };
        }

        [BTreeDefinition(BehaviorNames.DoorLocksmith, Curated = true, ParamsType = typeof(DoorLocksmithBlackboard))]
        public static BTreeBuilder<DoorLocksmithBlackboard, BTreeContext> BuildDoorLocksmithTree()
        {
            return new BTreeBuilder<DoorLocksmithBlackboard, BTreeContext>()
                .Sequence(seq => seq
                    .Action(bb => bb.Approach, DoorNodes.MoveToDoor)
                    .Action(bb => bb.Unlock, DoorNodes.OperateDoor)
                    .Action(bb => bb.Open, DoorNodes.OperateDoor)
                    .Action(bb => bb.Enter, CgfNodes.Action_WriteMoveToChannel));
        }
    }
}
