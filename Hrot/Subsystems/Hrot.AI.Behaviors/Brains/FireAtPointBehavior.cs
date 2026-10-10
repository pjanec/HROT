using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Combat;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐ Buildings Stage 6 (<c>CE-1032</c>, R-225 W-8; 📄 docs/DESIGN_Building_Interiors.md §3k) — <c>FireAtPoint</c>, a curated C# tree
    /// (R-223: test behaviours are C#; this one drives <c>bt-grenade-posture</c> and <c>bt-mortar-roof</c>): one shared
    /// <see cref="FireAtPointNodes.FireAtPoint"/> node. ⚠ It decides nothing — WHEN to throw or call fire is the behaviors lane's.
    /// </summary>
    public static class FireAtPointBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct FireAtPointBlackboard
        {
            public FireAtPointNodeParams Fire;
        }

        [BehaviorResolver(BehaviorNames.FireAtPoint)]
        public static void Resolve(in FireAtPointParamsJsonDto authored, ref FireAtPointBlackboard block, EntityRepository world, Entity self)
        {
            block.Fire = new FireAtPointNodeParams
            {
                Point = new Vector3(authored.X, authored.Y, authored.Z),
                CooldownSeconds = authored.CooldownSeconds,
                Rounds = authored.Rounds,
                Mount = (byte)authored.Mount,
            };
        }

        [BTreeDefinition(BehaviorNames.FireAtPoint, Curated = true, ParamsType = typeof(FireAtPointBlackboard))]
        public static BTreeBuilder<FireAtPointBlackboard, BTreeContext> BuildFireAtPointTree()
        {
            return new BTreeBuilder<FireAtPointBlackboard, BTreeContext>()
                .Action(bb => bb.Fire, FireAtPointNodes.FireAtPoint);
        }
    }
}
