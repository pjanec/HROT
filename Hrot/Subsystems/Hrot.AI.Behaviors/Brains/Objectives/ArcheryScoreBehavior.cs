using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.ScenarioRules;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains.Objectives
{
    /// <summary>
    /// ⭐⭐ <c>CE-4649</c> (bows BW-11, R-3103; docs/designs/weapons/DESIGN_Bows_And_Crossbows.md) — <c>ArcheryScore</c>, a MEASURE (<see cref="ObjectiveRole.Measure"/>) of what the subject's ARROWS
    /// did to archery targets: <c>Value</c> = Σ <see cref="ArcheryScoring.RingAt(EntityRepository, Entity, Vector3, Vector3)"/> over the arrows (a round whose look <c>Sticks</c>) the subject shot that struck a target — a prop whose TKB type carries an
    /// <c>ArcheryTargetDto</c> — read from the frame's <see cref="DetonationNotification"/>s (the strike's point; the prop has no body parts). A thin behaviour like every measure (mirrors
    /// <see cref="CountBehavior"/>): ⭐ it READS other entities (the target's position and type), declared readable in the phase (<c>SimTransform</c>, <c>TkbIdentity</c> — <c>BrainReads</c>).
    /// ⚠ The evidence is the STRIKE on a prop (the round is torn down there); an arrow that missed and stuck in the ground scores nothing. No arrow yet ⇒ Value 0 and Score 0.
    /// </summary>
    public static class ArcheryScoreBehavior
    {
        public const string Kind = BehaviorNames.ArcheryScore;
        public const float DefaultTarget = 50f;

        [StructLayout(LayoutKind.Sequential)]
        public struct Block
        {
            public ObjectiveCommonBlock Common; public ObjectiveRun Run;
            public float Target; public float Rings; public int Arrows; public float ShownValue; public int ShownN;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ArcheryScoreBlackboard { public Block Block; }

        [BehaviorResolver(BehaviorNames.ArcheryScore)]
        public static void Resolve(in ArcheryScoreParamsJsonDto authored, ref ArcheryScoreBlackboard bb, EntityRepository world, Entity self)
            => bb.Block = new Block { Common = ObjectiveCommonBlock.From(in authored.Common), Target = authored.Target ?? DefaultTarget, ShownValue = float.NaN };

        [SharedAiAction]
        public static NodeStatus Tick(ref Block b, Entity self, EntityRepository world)
        {
            var f = ObjectiveHost.Enter(world, self, Kind, ObjectiveRole.Measure, in b.Common, ref b.Run);
            if (!f.Ready) return NodeStatus.Running;   // ⭐ CE-4568 — its components land at the merge; it begins next tick
            if (f.Reset) { b.Rings = 0f; b.Arrows = 0; b.ShownValue = float.NaN; }
            ref var s = ref world.GetComponentRW<ObjectiveStatus>(self);
            if (f.Evaluate)
            {
                Add(world, b.Common.Subject.Resolve(world), ref b.Rings, ref b.Arrows);
                s.Target = b.Target;
                s.Value = b.Rings;
                if (MeasureHost.Changed(ref b.ShownValue, ref b.ShownN, s.Value, b.Arrows, in s))
                    ObjectiveHost.Settle(ref s, Kind, ObjectiveState.Active, $"{b.Rings:0} rings with {b.Arrows} arrow(s)", f.Now);
            }
            ObjectiveRules.WriteScore(ref s);
            if (b.Arrows == 0) s.Score = 0f;   // nothing scored yet is not a pass
            return NodeStatus.Running;
        }

        /// <summary>⭐ The ONE reading: this frame's strikes by <paramref name="subject"/>'s arrows on an archery target, their rings added (<see cref="ArcheryScoring.RingAt(EntityRepository, Entity, Vector3, Vector3)"/>) and counted. ⛔ No allocation, no LINQ.</summary>
        public static void Add(EntityRepository world, Entity subject, ref float rings, ref int arrows)
        {
            if (subject.IsNull || !world.Bus.IsRegistered<DetonationNotification>()) return;
            var hits = world.ReadEvents<DetonationNotification>();
            for (int i = 0; i < hits.Length; i++)
            {
                ref readonly var h = ref hits[i];
                if (h.Shooter != subject || h.Target.IsNull || CombatTkb.VisualOf(world, h.Ammo) is not { Sticks: true }) continue;
                int ring = ArcheryScoring.RingAt(world, h.Target, new Vector3(h.HitX, h.HitY, h.HitZ), new Vector3(h.TravelX, h.TravelY, h.TravelZ));
                if (ring < 0) continue;   // not an archery target
                rings += ring; arrows++;
            }
        }

        [Behavior(BehaviorNames.ArcheryScore, Params = typeof(ArcheryScoreParamsJsonDto), Categories = BehaviorCategory.Objective)]
        [BTreeDefinition(BehaviorNames.ArcheryScore, Curated = true, Group = "Library/Objectives", ParamsType = typeof(ArcheryScoreBlackboard))]
        public static BTreeBuilder<ArcheryScoreBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<ArcheryScoreBlackboard, BTreeContext>().Action(bb => bb.Block, Tick);
    }
}
