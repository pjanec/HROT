using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Utility;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <c>CE-3160</c> (R-266, R-223) — <c>CombatPosture</c>, the library's combat posture, as ONE C#-built tree: node for node what
    /// the retired <c>CombatPosture.btree.json</c> was (📄 docs/DESIGN_Decision_Layer.md §3.3b, §3.3e, §3.3f, §3.3h). Every leaf is a
    /// shared node (R-174) — the port changes the FORM, not what the soldier does. Its JSON, HSM and blueprint copies are gone.
    /// </summary>
    public static class CombatPostureBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct CombatPostureBlackboard
        {
            public ChooseOptionParams Choose;
            public UtilityChoice Choice;
            public IsOptionParams IsAdvance, IsCover, IsSuppress, IsFallBack, IsHoldProne;
            public PostureSensorsParams Sensors;
            public AdvanceParams Advance;
            public PeekAndFireParams Peek;
            public EngageParams Engage;
            public EqsTacticsParams Fallback;
            public ChooseOptionParams Approach;
            public UtilityChoice ApproachChoice;
            public IsOptionParams IsFlank, IsFiringPos, IsCloseToThrow;
            public PostureSensorsParams ApproachSensors;
            public EqsTacticsParams Flank, Firing, Throw;
            public WillingToCloseParams Willing;   // ⭐ AQ89 C/D — the Suppress branch's closing gate
            public DesignateParams Designate;      // ⭐ CE-3170 — an attack order's target (C-5)
            public LookParams Look;                // ⭐ CE-2297 LD-3 — where his eyes go (threat → sector → the order's bearing)
            public StayParams Stay;                // ⭐ CE-2297 slice 2a (R-2055) — a finished branch does not end a staying posture

            // ⭐ one working memory per node, as the JSON tree's auto slots were
            public PostureSensorsState SensorsState;
            public ApproachSensorsState ApproachSensorsState;
            public EqsTacticsState FlankState, FiringState, FallBackState, ThrowState;
            public AdvanceState AdvanceState;
            public PeekAndFireState PeekState;
            public EngageState SuppressState, HoldProneState;
            public EqsTacticsState SuppressThrowState;   // ⭐ AQ89 C — the close the Suppress branch runs (its own, not the approach's)
            public DesignateState DesignateState;        // ⭐ CE-3170
            public LookNodeState LookState;              // ⭐ CE-2297
            public OccupyState OccupyState;              // ⭐ CE-2275 D-2e — a posted man occupies his post first
            public FrameWatchState FrameWatchState;      // ⭐ CE-2275 D-2l — a frame post's watch, leaning out
        }

        /// <summary>Every value the JSON tree's blackboard carried as a default — the posture an unconfigured soldier fights with.</summary>
        public static CombatPostureBlackboard Defaults() => new CombatPostureBlackboard
        {
            Choose      = new ChooseOptionParams { Decision = UtilityDecisionRef.FromAssetId(CombatPostureDecision.DecisionAssetId) },
            IsAdvance   = new IsOptionParams { Option = (byte)Posture.AdvanceAndAttack },
            IsCover     = new IsOptionParams { Option = (byte)Posture.TakeCover },
            IsSuppress  = new IsOptionParams { Option = (byte)Posture.Suppress },
            IsFallBack  = new IsOptionParams { Option = (byte)Posture.Flee },   // the retreat option runs FallBack (R-259)
            IsHoldProne = new IsOptionParams { Option = (byte)Posture.HoldProne },
            Sensors     = new PostureSensorsParams { SearchRadius = 60f, ScoreDeltaThreshold = 0.05f },
            Advance     = new AdvanceParams { Speed = 3f, ArrivalRadius = 5f, CooldownSeconds = 1f },
            Engage      = new EngageParams { CooldownSeconds = 1f },
            Fallback    = new EqsTacticsParams { SearchRadius = 60f, MinRepositionMetres = 5f, Speed = 2.5f, ArrivalRadius = 1f, ScoreDeltaThreshold = 0.05f },
            Approach    = new ChooseOptionParams { Decision = UtilityDecisionRef.FromAssetId(AttackApproachDecision.DecisionAssetId) },
            IsFlank     = new IsOptionParams { Option = (byte)Approach.Flank },
            IsFiringPos = new IsOptionParams { Option = (byte)Approach.FiringPosition },
            IsCloseToThrow = new IsOptionParams { Option = (byte)Approach.CloseToThrow },   // ⭐ WL-4 W-12f
            ApproachSensors = new PostureSensorsParams { SearchRadius = 40f, ScoreDeltaThreshold = 0.05f },
            Flank       = Approaching(),
            Firing      = Approaching(),
            Throw       = Approaching() with { SearchRadius = EqsTacticsNodes.ThrowSearchRadius },   // ⭐ WL-4 W-12f
        };

        private static EqsTacticsParams Approaching() => new EqsTacticsParams
        {
            SearchRadius = 40f, MinRepositionMetres = 5f, Speed = 3f, ArrivalRadius = 1.5f, ScoreDeltaThreshold = 0.05f,
            FireCooldownSeconds = 1f, FireWhileMoving = 1,
        };

        /// <summary>⭐ The authored contract (<see cref="CombatPostureParamsJsonDto"/>, the shape every scenario already writes): the
        /// defaults, then the advance — a zero field keeps its default.</summary>
        [BehaviorResolver(BehaviorNames.CombatPosture)]
        public static void Resolve(in CombatPostureParamsJsonDto authored, ref CombatPostureBlackboard block, EntityRepository world, Entity self)
        {
            block = Defaults();
            var a = authored.Advance;
            block.Advance.Objective = a.Objective;
            if (a.Speed > 0f) block.Advance.Speed = a.Speed;
            if (a.ArrivalRadius > 0f) block.Advance.ArrivalRadius = a.ArrivalRadius;
            if (a.CooldownSeconds > 0f) block.Advance.CooldownSeconds = a.CooldownSeconds;
            block.Willing.Assault = authored.Assault ? (byte)1 : (byte)0;   // ⭐ AQ89 D
            block.Stay.Stay = block.Advance.Stay = authored.Stay ? (byte)1 : (byte)0;   // ⭐ CE-2297 slice 2a (R-2055)
            Post(in authored.Post, ref block);   // ⭐ CE-2275 D-2a / D-2d (PC-14) — his hide points and his approach moves stay on his post

            // ⭐ CE-2297 LD-3e — the order carries the facing: a point he watches toward when no threat and no sector point his eyes,
            //   scanned ±30° (📄 Look Direction §3.3a)
            if (authored.WatchTowards != Vector3.Zero && world.HasComponent<SimTransform>(self))
            {
                block.Look.Bearing = LookAt.BearingTo(world.GetComponentRO<SimTransform>(self).Position, authored.WatchTowards);
                block.Look.BearingSet = 1;
                block.Look.HalfWidth = LookAt.WatchHalfWidth;
            }

            // ⭐ CE-3170 (docs/DESIGN_Entity_Control.md C-5) — an ATTACK: the named target, and with no objective authored the advance
            //   goes to where the target is now (the unit closes in; the decision picks the way).
            if (authored.Target.IsNone) return;
            var map = world.HasSingletonManaged<NetworkEntityMap>() ? world.GetSingletonManaged<NetworkEntityMap>() : null;
            if (map == null || !map.TryGetEntity(authored.Target.NetworkId, out var target) || !world.IsAlive(target))
            {
                Hrot.AI.Behaviors.Logging.BehaviorLog.ParseWarn("CombatPosture target=" + authored.Target.NetworkId + " not found in entity map; the posture runs without one.");
                return;
            }
            block.Designate.TargetPacked = (long)target.PackedValue;
            if (a.Objective == Vector3.Zero && world.HasComponent<SimTransform>(target))
                block.Advance.Objective = world.GetComponentRO<SimTransform>(target).Position;
        }

        /// <summary>⭐ <c>CE-2275</c> D-2a (PC-14, DA-40) — a posted man's take-cover branch is anchored to his post: a window post flies the
        /// window column (<see cref="WindowDuelBehavior.Shape"/> A — prone under the sill, up at it), a cover post the defaults; either way a
        /// hide point off the post is never taken (<see cref="PeekAndFireNodes.OnPost"/>). No post ⇒ the defaults, unanchored.</summary>
        internal static void Post(in CombatPosturePostJsonDto post, ref PeekAndFireParams peek)
        {
            if (post.Radius <= 0f) return;
            peek = post.Window ? WindowDuelBehavior.Shape(0) : default;
            if (post.Window && post.SetBack) peek.HideTemplate = Fdp.Toolkit.Spatial.Eqs.FindWindowSetBackPosition.BlueprintId;   // ⭐ D-2b (DA-6)
            if (!post.Window && post.Roof)   // ⭐ D-2g (DA-22) — the window's stance peek over the parapet: prone behind it, up at the point's stance
            {
                peek = WindowDuelBehavior.Shape(0);
                peek.HideTemplate = Fdp.Toolkit.Spatial.Eqs.FindRoofPosts.BlueprintId;
            }
            peek.Anchor = post.Point;
            peek.AnchorRadius = post.Radius;
            peek.AnchorAlternate = post.Alternate;
            peek.AnchorHasAlternate = post.HasAlternate ? (byte)1 : (byte)0;
            if (post.Window && post.Frame)   // ⭐ D-2l (DA-80, R-2059) — beside the frame, leaning out; the sill shape above is his fallback (DA-84)
            {
                peek.Frame = 1;
                peek.FrameA = post.FrameA;
                peek.FrameB = post.FrameB;
                peek.Opening = post.Opening;
            }
        }

        /// <summary>⭐ <c>CE-2275</c> D-2d (PC-14) — a posted man's approach moves (flank, firing position, close to throw) stay on his post
        /// too: measured live (<c>sq-defend-house</c>), his posture chose AdvanceAndAttack and its firing-position move took him 5–7 m off it.
        /// ⛔ Not the retreat (Flee): breaking is morale, not repositioning.</summary>
        internal static void Post(in CombatPosturePostJsonDto post, ref CombatPostureBlackboard block)
        {
            Post(in post, ref block.Peek);
            if (post.Radius <= 0f) return;
            block.Flank.Anchor = block.Firing.Anchor = block.Throw.Anchor = post.Point;
            block.Flank.AnchorRadius = block.Firing.AnchorRadius = block.Throw.AnchorRadius = post.Radius;
        }

        /// <summary>📄 §3.3b: decide, sense and act in one Parallel (RequireOne — the posture ends when its winner ends); the winner runs
        /// under an ObserverSelector so a changed decision switches branch at once (the old branch's deactivators run). ⭐ CE-2297 slice
        /// 2a (R-2055): the winner and the attack's target each sit in a Sequence with <see cref="PostureNodes.Stay"/> after them — a
        /// staying posture's finished branch reads Running there, and under the Parallel the Sequence starts again from the branch
        /// every tick, so the decision still switches; a failed branch ends the posture as before.</summary>
        [Behavior(BehaviorNames.CombatPosture, Params = typeof(global::Hrot.Map.Definitions.Behavior.CombatPostureParamsJsonDto), Categories = BehaviorCategory.Infantry | BehaviorCategory.Insurgent)]   // ⭐ CE-2134
        [BTreeDefinition(BehaviorNames.CombatPosture, Curated = true, Group = "Library/Infantry", ParamsType = typeof(CombatPostureBlackboard))]
        public static BTreeBuilder<CombatPostureBlackboard, BTreeContext> BuildCombatPostureTree()
        {
            return new BTreeBuilder<CombatPostureBlackboard, BTreeContext>()
                .Parallel(1, decide => decide
                    .Sequence(attack => attack
                        .StatefulAction(bb => bb.Designate, bb => bb.DesignateState, PostureNodes.UntilTargetDown)   // ⭐ CE-3170
                        .Action(bb => bb.Stay, PostureNodes.Stay))
                    .StatefulAction(bb => bb.Choose, bb => bb.Choice, UtilityNodes.ChooseOption)
                    .StatefulAction(bb => bb.Sensors, bb => bb.SensorsState, PostureNodes.PostureSensors)
                    .StatefulAction(bb => bb.Approach, bb => bb.ApproachChoice, UtilityNodes.ChooseOption)
                    .StatefulAction(bb => bb.ApproachSensors, bb => bb.ApproachSensorsState, PostureNodes.ApproachSensors)
                    .StatefulAction(bb => bb.Look, bb => bb.LookState, LookNodes.Look)   // ⭐ CE-2297 LD-3 — the eyes, beside the work
                    .Sequence(work => work
                    .ObserverSelector(winner => winner
                        // ⭐ CE-2275 D-2e (PC-14, DA-45) — the retreat first (options are exclusive, so the order is neutral), then a POSTED
                        //   man not yet on his post goes there, firing on the way, whatever else the decision chose
                        .Sequence(fallBack => fallBack
                            .StatefulCondition(bb => bb.IsFallBack, bb => bb.Choice, UtilityNodes.IsOption)
                            .StatefulAction(bb => bb.Fallback, bb => bb.FallBackState, EqsTacticsNodes.FallBack))
                        .Sequence(occupy => occupy
                            .StatefulCondition(bb => bb.Peek, bb => bb.OccupyState, PostureNodes.OffPost)
                            .StatefulAction(bb => bb.Advance, bb => bb.AdvanceState, PostureNodes.AdvanceAndAttack))
                        // ⭐⭐ CE-2366 A (approved 2026-10-25) — a man POSTED at a window / frame / roof with something to fight runs ONE peek
                        //   cycle whatever the decision says: peek, fire if he sees him, back behind the wall, another side or window when the
                        //   spot is used up or hot. The decision switching between its options no longer tears the cycle down.
                        //   ⛔ SUPERSEDED: AQ102 B (CE-2356) flew him through PeekAndFire from the Suppress branch only, and the TakeCover
                        //   branch ran it with the same state — a switch between them, or to Advance, reset it (📐 live bt-window-duel).
                        .Sequence(posted => posted
                            .Condition(bb => bb.Peek, PostureNodes.PostedWithThreat)
                            .StatefulAction(bb => bb.Peek, bb => bb.PeekState, PeekAndFireNodes.PeekAndFire))
                        .Sequence(advance => advance
                            .StatefulCondition(bb => bb.IsAdvance, bb => bb.Choice, UtilityNodes.IsOption)
                            .ObserverSelector(approach => approach
                                .Sequence(flank => flank
                                    .StatefulCondition(bb => bb.IsFlank, bb => bb.ApproachChoice, UtilityNodes.IsOption)
                                    .StatefulAction(bb => bb.Flank, bb => bb.FlankState, EqsTacticsNodes.Flank))
                                .Sequence(firing => firing
                                    .StatefulCondition(bb => bb.IsFiringPos, bb => bb.ApproachChoice, UtilityNodes.IsOption)
                                    .StatefulAction(bb => bb.Firing, bb => bb.FiringState, EqsTacticsNodes.FiringPosition))
                                // ⭐ WL-4 W-12f (Weapon_Layers §5f) — a grenadier closes to throw at the hidden target
                                .Sequence(close => close
                                    .StatefulCondition(bb => bb.IsCloseToThrow, bb => bb.ApproachChoice, UtilityNodes.IsOption)
                                    .StatefulAction(bb => bb.Throw, bb => bb.ThrowState, EqsTacticsNodes.CloseToThrow))
                                .StatefulAction(bb => bb.Advance, bb => bb.AdvanceState, PostureNodes.AdvanceAndAttack)))
                        .Sequence(cover => cover
                            .StatefulCondition(bb => bb.IsCover, bb => bb.Choice, UtilityNodes.IsOption)
                            .StatefulAction(bb => bb.Peek, bb => bb.PeekState, PeekAndFireNodes.PeekAndFire))
                        // ⭐⭐ AQ89 A/C (R-269) — at a hidden target a WILLING unit carrying a throwable closes to throw under its own
                        //   blind fire; anyone else suppresses blind at the last-known spot instead of idling. Outer scores untouched.
                        .Sequence(suppress => suppress
                            .StatefulCondition(bb => bb.IsSuppress, bb => bb.Choice, UtilityNodes.IsOption)
                            .ObserverSelector(how => how
                                // (a man posted at an opening never gets here with something to fight — the posted branch above, CE-2366 A)
                                .Sequence(close => close
                                    .StatefulCondition(bb => bb.Willing, bb => bb.SuppressThrowState, PostureNodes.WillingToClose)
                                    .StatefulAction(bb => bb.Throw, bb => bb.SuppressThrowState, EqsTacticsNodes.CloseToThrow))
                                .StatefulAction(bb => bb.Engage, bb => bb.SuppressState, PostureNodes.Suppress)))
                        .Sequence(prone => prone
                            .StatefulCondition(bb => bb.IsHoldProne, bb => bb.Choice, UtilityNodes.IsOption)
                            .StatefulAction(bb => bb.Engage, bb => bb.HoldProneState, PostureNodes.HoldProne))
                        // Hold (option 5) and anything unscored — ⭐ CE-2275 D-2l: at a frame post he leans out to watch
                        .StatefulAction(bb => bb.Peek, bb => bb.FrameWatchState, PostureNodes.HoldWatch))
                    .Action(bb => bb.Stay, PostureNodes.Stay)));   // ⭐ CE-2297 slice 2a
        }
    }
}
