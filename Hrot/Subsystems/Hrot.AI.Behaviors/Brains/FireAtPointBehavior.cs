using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
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
                FireAt = authored.FireAt,                                                  // ⭐ IF-2 (FM-4)
                Arc = authored.Arc is >= 0 and <= 2 ? (ArcPreference)authored.Arc : ArcPreference.Auto,   // ⭐ IF-2 (IF-C)
            };
        }
[Behavior(BehaviorNames.FireAtPoint, Params = typeof(global::Hrot.Map.Definitions.Behavior.FireAtPointParamsJsonDto), Categories = BehaviorCategory.AllMilitary)]   // ⭐ CE-2134

        [BTreeDefinition(BehaviorNames.FireAtPoint, Curated = true, Group = "Library/Basic", ParamsType = typeof(FireAtPointBlackboard))]
        public static BTreeBuilder<FireAtPointBlackboard, BTreeContext> BuildFireAtPointTree()
        {
            return new BTreeBuilder<FireAtPointBlackboard, BTreeContext>()
                .Sequence(seq => seq
                    .Action(bb => bb.Fire, CrouchBesideOwnMortar)   // ⭐ CE-1238
                    .Action(bb => bb.Fire, FireAtPointNodes.FireAtPoint));
        }

        /// <summary>How long getting down beside the mortar takes (s) — HoldStance's blend.</summary>
        public const float CrouchSeconds = HoldStanceBehavior.BlendSeconds;

        /// <summary>
        /// ⭐ CE-1238 (People Display U7a, R-1023; 🔒 User, `2026-10-21`: "Maybe the guy operating a mortar is crouching next to it usually
        /// (for smaller mortars)" — "Best if you do both changes yourself") — a man firing a mortar of his OWN (the MortarTeam 2005's
        /// 81 mm: <see cref="Hrot.Core.Tkb.MunitionTkbCatalog.IsMortar"/>) crouches beside it: the brain asks for the stance
        /// (<c>StanceRequest.Set</c>, idempotent), so the sim sees and hits him crouched and the figure draws him so. A crewed tube's gunner
        /// kneels in its seat already (the seat pose); any other weapon is left to whoever set his stance. Always Success.
        /// </summary>
        [SharedAiAction]
        public static NodeStatus CrouchBesideOwnMortar(ref FireAtPointNodeParams p, Entity self, EntityRepository world)
        {
            if (FiresOwnMortar(world, self, p.Mount))
                Hrot.MuscleCharacter.Animation.Stance.StanceRequest.Set(world, self, Fdp.Toolkit.Tkb.Domain.StanceId.Crouched, CrouchSeconds);
            return NodeStatus.Success;
        }

        /// <summary>⭐ CE-1238 — mount <paramref name="mount"/> of <paramref name="self"/> is a mortar of his own (not a controlled weapon system).</summary>
        public static bool FiresOwnMortar(EntityRepository world, Entity self, byte mount)
            => !WeaponSystems.IsControlledIndex(mount)
               && Hrot.Core.Tkb.MunitionTkbCatalog.IsMortar(
                   world.HasSingletonManaged<Fdp.Interfaces.ITkbDatabase>() ? world.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>() : null,
                   CombatTkb.MountOf(world, self, mount));
    }
}
