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
    /// ⭐ <c>CE-2257</c> BC-1 (CB-53, CB-11; 📄 docs/designs/group-maneuvers/DESIGN_Building_Clearing.md §7.1) — <c>BreachDoor</c>, a curated C#
    /// tree from the shared door nodes (<see cref="DoorNodes"/>): walk to the door, OPEN it, and when the owner refuses (locked) BREACH it
    /// (3 s). Success once it stands open (or broken); Failure when neither works — the squad reports the room blocked. The stack's #1 runs
    /// it (the squad play's <c>OperateDoor</c> member order).
    /// </summary>
    public static class BreachDoorBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct BreachDoorBlackboard
        {
            public MoveToDoorParams Approach;
            public OperateDoorParams Open;
            public OperateDoorParams Breach;
        }

        [BehaviorResolver(BehaviorNames.BreachDoor)]
        public static void Resolve(in BreachDoorParamsJsonDto authored, ref BreachDoorBlackboard block, EntityRepository world, Entity self)
        {
            block.Approach = new MoveToDoorParams { Door = authored.Door, Speed = authored.Speed };
            block.Open = new OperateDoorParams { Door = authored.Door, Verb = DoorVerb.Open };
            block.Breach = new OperateDoorParams { Door = authored.Door, Verb = DoorVerb.Breach };
        }

        [Behavior(BehaviorNames.BreachDoor, Params = typeof(global::Hrot.Map.Definitions.Behavior.BreachDoorParamsJsonDto), Categories = BehaviorCategory.AllMilitary)]
        [BTreeDefinition(BehaviorNames.BreachDoor, Curated = true, Group = "Library/Basic", ParamsType = typeof(BreachDoorBlackboard))]
        public static BTreeBuilder<BreachDoorBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<BreachDoorBlackboard, BTreeContext>()
                .Sequence(seq => seq
                    .Action(bb => bb.Approach, DoorNodes.MoveToDoor)
                    .Selector(how => how
                        .Action(bb => bb.Open, DoorNodes.OperateDoor)
                        .Action(bb => bb.Breach, DoorNodes.OperateDoor)));
    }
}
