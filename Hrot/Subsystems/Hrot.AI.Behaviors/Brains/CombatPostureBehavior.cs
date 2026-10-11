using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
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
            public IsOptionParams IsFlank, IsFiringPos;
            public PostureSensorsParams ApproachSensors;
            public EqsTacticsParams Flank, Firing;

            // ⭐ one working memory per node, as the JSON tree's auto slots were
            public PostureSensorsState SensorsState;
            public ApproachSensorsState ApproachSensorsState;
            public EqsTacticsState FlankState, FiringState, FallBackState;
            public AdvanceState AdvanceState;
            public PeekAndFireState PeekState;
            public EngageState SuppressState, HoldProneState;
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
            ApproachSensors = new PostureSensorsParams { SearchRadius = 40f, ScoreDeltaThreshold = 0.05f },
            Flank       = Approaching(),
            Firing      = Approaching(),
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
        }

        /// <summary>📄 §3.3b: decide, sense and act in one Parallel (RequireOne — the posture ends when its winner ends); the winner runs
        /// under an ObserverSelector so a changed decision switches branch at once (the old branch's deactivators run).</summary>
        [BTreeDefinition(BehaviorNames.CombatPosture, Curated = true, Group = "Library/Infantry", ParamsType = typeof(CombatPostureBlackboard))]
        public static BTreeBuilder<CombatPostureBlackboard, BTreeContext> BuildCombatPostureTree()
        {
            return new BTreeBuilder<CombatPostureBlackboard, BTreeContext>()
                .Parallel(1, decide => decide
                    .StatefulAction(bb => bb.Choose, bb => bb.Choice, UtilityNodes.ChooseOption)
                    .StatefulAction(bb => bb.Sensors, bb => bb.SensorsState, PostureNodes.PostureSensors)
                    .StatefulAction(bb => bb.Approach, bb => bb.ApproachChoice, UtilityNodes.ChooseOption)
                    .StatefulAction(bb => bb.ApproachSensors, bb => bb.ApproachSensorsState, PostureNodes.ApproachSensors)
                    .ObserverSelector(winner => winner
                        .Sequence(advance => advance
                            .StatefulCondition(bb => bb.IsAdvance, bb => bb.Choice, UtilityNodes.IsOption)
                            .ObserverSelector(approach => approach
                                .Sequence(flank => flank
                                    .StatefulCondition(bb => bb.IsFlank, bb => bb.ApproachChoice, UtilityNodes.IsOption)
                                    .StatefulAction(bb => bb.Flank, bb => bb.FlankState, EqsTacticsNodes.Flank))
                                .Sequence(firing => firing
                                    .StatefulCondition(bb => bb.IsFiringPos, bb => bb.ApproachChoice, UtilityNodes.IsOption)
                                    .StatefulAction(bb => bb.Firing, bb => bb.FiringState, EqsTacticsNodes.FiringPosition))
                                .StatefulAction(bb => bb.Advance, bb => bb.AdvanceState, PostureNodes.AdvanceAndAttack)))
                        .Sequence(cover => cover
                            .StatefulCondition(bb => bb.IsCover, bb => bb.Choice, UtilityNodes.IsOption)
                            .StatefulAction(bb => bb.Peek, bb => bb.PeekState, PeekAndFireNodes.PeekAndFire))
                        .Sequence(suppress => suppress
                            .StatefulCondition(bb => bb.IsSuppress, bb => bb.Choice, UtilityNodes.IsOption)
                            .StatefulAction(bb => bb.Engage, bb => bb.SuppressState, PostureNodes.Engage))
                        .Sequence(fallBack => fallBack
                            .StatefulCondition(bb => bb.IsFallBack, bb => bb.Choice, UtilityNodes.IsOption)
                            .StatefulAction(bb => bb.Fallback, bb => bb.FallBackState, EqsTacticsNodes.FallBack))
                        .Sequence(prone => prone
                            .StatefulCondition(bb => bb.IsHoldProne, bb => bb.Choice, UtilityNodes.IsOption)
                            .StatefulAction(bb => bb.Engage, bb => bb.HoldProneState, PostureNodes.HoldProne))
                        .Action(PostureNodes.Hold)));   // Hold (option 5) and anything unscored
        }
    }
}
