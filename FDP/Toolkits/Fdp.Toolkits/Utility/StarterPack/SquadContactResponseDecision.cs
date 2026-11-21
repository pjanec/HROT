namespace Fdp.Toolkit.Utility
{
    /// <summary>⭐ <c>CE-2123</c> S2 — what a squad does about a contact, once it has returned fire (option ids start at 1).</summary>
    public enum SquadContactResponse : byte
    {
        /// <summary>Fix the enemy with fire and flank it (the S1 <c>SuppressAndManeuver</c> drill).</summary>
        Assault      = 1,
        /// <summary>Get out of the enemy's sight, element covering element (<c>BreakContact</c>).</summary>
        BreakContact = 2,
        /// <summary>Stay where each member is and fight (<c>SquadHold</c>).</summary>
        Hold         = 3,
        /// <summary>⭐ <c>CE-2152</c> C-c (R-2013) — a man is down: cover and bring him in (<c>CasualtyCare</c>).</summary>
        Rescue       = 4,
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2123</c> S2 — the squad leader's choice after contact: ASSAULT when the squad is healthy, has ammunition and the
    /// enemy's share of the strength in play is low; BREAK CONTACT when the enemy's share is high; otherwise HOLD (a floor).
    /// ctx.Self is the COMMANDER (squad inputs). Decided ONCE per reaction (<c>UtilityNodes.ChooseOptionOnce</c>), so no
    /// hysteresis. 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §4.5 S2-D.
    /// </summary>
    [UtilityDecision(
        assetId:         "5a2123c0-0000-4000-a000-squadcontact1",
        displayName:     "Squad contact response",
        kind:            DecisionKind.PostureSelect,   // ⚠ the OPTION-decision kind: UtilityScorer.ChooseOption scores only PostureSelect (ManeuverSelect returns 0)
        category:        "Tactical/Squad",
        hysteresisBonus: 0f,
        OptionNames = typeof(SquadContactResponse))]
    public sealed partial class SquadContactResponseDecision : IUtilityDecisionDefinition
    {
        /// <summary>The decision's asset id, for a C# tree that names it (<c>ReactToContactBehavior</c>).</summary>
        public const string DecisionAssetId = "5a2123c0-0000-4000-a000-squadcontact1";

        /// <summary>Builds the decision definition via the fluent builder.</summary>
        public static void Build(IUtilityDecisionBuilder b) => b
            .Option((ushort)SquadContactResponse.Assault, ScoringMode.WeightedProduct, o => o
                .Consider(In.SquadStrengthRatio(),      0.8f, Curve.Linear)
                .Consider(In.SquadAmmoRollup(),         0.7f, Curve.Threshold)
                .Consider(In.SquadEnemyStrengthRatio(), 1.0f, Curve.InverseLinear))
            .Option((ushort)SquadContactResponse.BreakContact, ScoringMode.WeightedProduct, o => o
                .Consider(In.SquadEnemyStrengthRatio(), 1.0f, Curve.Logistic))
            .Option((ushort)SquadContactResponse.Hold, ScoringMode.WeightedSum, o => o
                .Consider(In.Constant(0.25f),           1.0f, Curve.Linear))
            // ⭐ CE-2152 C-c (R-2013, §7.5.4) — any factor near zero kills it: no casualty, aimed fire on him, the squad under fire.
            //   Weights are exponents: urgency and fire superiority at 0.5 so a quiet squad's fresh casualty beats both Hold and a
            //   strong squad's Assault (calibration in §7.5.4 as built).
            .Option((ushort)SquadContactResponse.Rescue, ScoringMode.WeightedProduct, o => o
                .Consider(In.CasualtyUrgency(),         0.5f, Curve.Linear)
                .Consider(In.CasualtyExposure(),        1.0f, Curve.InverseLinear)
                .Consider(In.SquadQuietSeconds(),       1.0f, Curve.Linear)
                .Consider(In.SquadEnemyStrengthRatio(), 0.5f, Curve.InverseLinear));
    }
}
