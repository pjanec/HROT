namespace Fdp.Toolkit.Behavior.Components
{
    /// <summary>
    /// ⭐⭐ <c>CE-3034</c> (R-188, R-193) — WHO started a behaviour. Carried by every assign / clear event and copied by
    /// <see cref="Systems.BehaviorIngressSystem"/> onto <see cref="BehaviorState.Origin"/>.
    /// 📄 <c>docs/DESIGN_Sensors_And_Doctrine.md</c> §6 · <c>docs/DESIGN_Decision_Layer.md</c> §4.
    /// <para>⭐ THE ONE GATE: an assignment cannot replace a running behaviour of HIGHER rank —
    /// <c>Operator &gt; Superior &gt; Sop</c>. <see cref="Self"/> (a behaviour replacing or ending ITSELF) keeps the
    /// running origin. <see cref="Unmarked"/> reads as <see cref="Operator"/>, so an old or unmarked path is human-rank
    /// and the AI can never override it by accident.</para>
    /// </summary>
    public enum BehaviorOrigin : byte
    {
        /// <summary>No origin given. ⭐ On an EVENT it ranks as <see cref="Operator"/> (and is STORED as Operator). On a
        /// RUNNING behaviour it means nobody assigned it through the gate — an empty slot, or a behaviour stamped directly
        /// (a TKB default, a fixture) — and anything may replace it.</summary>
        Unmarked = 0,
        /// <summary>The unit's own SOP (R-198; called "doctrine" in older documents) — the lowest rank.</summary>
        Sop = 1,
        /// <summary>A superior: a mission plan, a commander node, a tactical intent from DDS, an editor-authored assignment.</summary>
        Superior = 2,
        /// <summary>A human operator: operator UI, mission-control abort, the debug API.</summary>
        Operator = 3,
        /// <summary>⭐ <c>CE-2078</c> (R-199) — a REACTION the unit's SOP starts to answer an event: it pauses the task (unless
        /// the ROE says <c>StayOnTask</c>), the task restarts when it ends; it yields only to a more urgent reaction or a new
        /// order. Not a rank — <see cref="BehaviorOriginRank"/> and the ingress apply the reaction rules.</summary>
        Reaction = 4,
        /// <summary>A behaviour replacing or ending ITSELF (hot-reload restart, a chained re-assign) — keeps the running origin.</summary>
        Self = 255,
    }

    /// <summary>⭐ <c>CE-2078</c> (R-199) — how urgent a reaction is: a running reaction yields only to a MORE urgent one.
    /// ⚠ Members are prefixed-free but unique in the module (the DDS IDL puts every enum member of a module in one scope).</summary>
    public enum ReactionUrgency : byte
    {
        /// <summary>Not a reaction.</summary>
        NotAReaction = 0,
        /// <summary>Something worth attention (a sound, a distant contact).</summary>
        Alert = 1,
        /// <summary>A contact.</summary>
        Contact = 2,
        /// <summary>Being shot at.</summary>
        UnderFire = 3,
        /// <summary>Being hit.</summary>
        Hit = 4,
    }

    /// <summary>⭐ <c>CE-3034</c> — the rank rule, in one place.</summary>
    public static class BehaviorOriginRank
    {
        /// <summary>The comparable rank of an origin: <see cref="BehaviorOrigin.Unmarked"/> counts as <see cref="BehaviorOrigin.Operator"/>.</summary>
        public static int Of(BehaviorOrigin origin) => origin switch
        {
            BehaviorOrigin.Sop      => 1,
            BehaviorOrigin.Superior => 2,
            _                       => 3,   // Operator, Unmarked (and Self, which never reaches a comparison)
        };

        /// <summary>
        /// May an assignment / clear of <paramref name="incoming"/> origin replace the running behaviour?
        /// An empty slot — or one whose behaviour was stamped directly, never assigned through the gate — admits anything;
        /// <see cref="BehaviorOrigin.Self"/> is always admitted; otherwise the incoming rank must be at least the running one.
        /// </summary>
        public static bool Admits(BehaviorOrigin incoming, in BehaviorState running)
            => Admits(incoming, running.ActiveBehaviorHash, running.Origin);

        /// <summary>The same rule for any slot (⭐ <c>CE-3035</c>: the SOP slot uses it too).</summary>
        public static bool Admits(BehaviorOrigin incoming, int runningHash, BehaviorOrigin runningOrigin)
        {
            if (runningHash == BehaviorIds.None) return true;
            if (runningOrigin == BehaviorOrigin.Unmarked) return true;   // stamped directly, never through the gate
            if (incoming == BehaviorOrigin.Self) return true;
            return Of(incoming) >= Of(runningOrigin);
        }

        /// <summary>The origin the running behaviour carries after an admitted assignment: <see cref="BehaviorOrigin.Self"/>
        /// keeps the running origin (an ordered behaviour that chains stays ordered).</summary>
        public static BehaviorOrigin AfterAssign(BehaviorOrigin incoming, in BehaviorState running)
            => AfterAssign(incoming, running.Origin);

        /// <summary>The same rule for any slot.</summary>
        public static BehaviorOrigin AfterAssign(BehaviorOrigin incoming, BehaviorOrigin runningOrigin)
            => incoming switch
            {
                BehaviorOrigin.Self     => runningOrigin,
                BehaviorOrigin.Unmarked => BehaviorOrigin.Operator,   // an unmarked order is human-rank — store it so
                _                       => incoming,
            };
    }
}
