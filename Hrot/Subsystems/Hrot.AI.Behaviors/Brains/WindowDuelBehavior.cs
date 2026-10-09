using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <c>CE-3136</c> P-8 (D10, R-223: test behaviours are C#; 📄 docs/DESIGN_Peek_And_Fire.md §2, §8.4) — <c>WindowDuel</c>, the curated
    /// tree that flies BOTH soldiers of <c>bt-window-duel</c>: one <see cref="PeekAndFireNodes.PeekAndFire"/> node, its parameters the
    /// design's A (window) or B (street) column. The node does everything; the tree only picks the shape.
    /// </summary>
    public static class WindowDuelBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct WindowDuelBlackboard
        {
            public PeekAndFireParams Peek;
            public PeekAndFireState PeekState;
        }

        /// <summary>§8.4's columns. ⭐ A zero field means the node's default (<see cref="PeekAndFireNodes.Effective"/>), so only what
        /// differs from it is set here.</summary>
        public static PeekAndFireParams Shape(int shape) => shape == 1
            ? new PeekAndFireParams   // B — in the street: behind a cover, steps out, suppresses and bounds
            {
                HideTemplate = FindCoverFromTarget.BlueprintId, Mode = PeekMode.Step, SearchRadius = 25f, PeekSearchRadius = 3f,
                HideStanceOverride = (byte)(StanceId.Crouched + 1),   // ⚠ crouched behind the van: a window above sees a man standing
                HideSecondsMin = 2f, HideSecondsMax = 4f, ExposeSeconds = 2.5f, BlindRounds = 4, FireCooldownSeconds = 0.25f,
                ExposuresPerPosition = 2, RelocateSpeed = 5f, MinRelocateMetres = 6f, SuppressBeforeRelocate = 1,
            }
            : new PeekAndFireParams   // A — at a window: prone under the sill, up at it, another window after a few exposures
            {
                HideTemplate = FindWindowFiringPosition.BlueprintId, Mode = PeekMode.Stance, SearchRadius = 12f,
                HideStanceOverride = (byte)(StanceId.Prone + 1),
                HideSecondsMin = 2f, HideSecondsMax = 5f, BlindRounds = 2, ExposuresPerPosition = 3,
                RelocateSpeed = 2f, MinRelocateMetres = 2f,
            };

        [BehaviorResolver(BehaviorNames.WindowDuel)]
        public static void Resolve(in WindowDuelParamsJsonDto authored, ref WindowDuelBlackboard block, EntityRepository world, Entity self)
        {
            var p = Shape(authored.Shape);
            if (authored.SearchRadius > 0f) p.SearchRadius = authored.SearchRadius;
            if (authored.ExposuresPerPosition > 0) p.ExposuresPerPosition = authored.ExposuresPerPosition;
            if (authored.RoundsPerExposure > 0) p.RoundsPerExposure = authored.RoundsPerExposure;
            if (authored.BlindRounds > 0) p.BlindRounds = authored.BlindRounds;
            block.Peek = p;
        }

        [BTreeDefinition(BehaviorNames.WindowDuel, Curated = true, ParamsType = typeof(WindowDuelBlackboard))]
        public static BTreeBuilder<WindowDuelBlackboard, BTreeContext> BuildWindowDuelTree()
        {
            return new BTreeBuilder<WindowDuelBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Peek, bb => bb.PeekState, PeekAndFireNodes.PeekAndFire);
        }
    }
}
