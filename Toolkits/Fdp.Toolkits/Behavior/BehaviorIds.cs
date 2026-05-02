namespace Fdp.Toolkit.Behavior
{
    /// <summary>
    /// Stable compile-time integer constants for all registered behaviors.
    ///
    /// These IDs are passed as the first argument to
    /// <see cref="BehaviorRegistry.Register(int, string, BehaviorDefinition)"/>
    /// and are stored in
    /// <see cref="Components.BehaviorState.ActiveBehaviorHash"/> and in
    /// <see cref="Components.MissionPhase.BehaviorId"/>.
    ///
    /// <b>Rules:</b>
    /// <list type="bullet">
    ///   <item>Each constant must be globally unique across the entire project.</item>
    ///   <item>Once assigned, a constant value must NEVER change (it may appear in
    ///         saved state, logs, and replicated data).</item>
    ///   <item>0 is reserved as "no behavior" (see <c>None</c>).</item>
    ///   <item>Civilian range: 1001–1999.  Military range: 2001–2999.</item>
    /// </list>
    /// </summary>
    public static class BehaviorIds
    {
        /// <summary>No behavior assigned.  Default / idle state.</summary>
        public const int None           = 0;

        // ── Civilian behaviors (1001–1999) ───────────────────────────────────
        /// <summary>Civilian wander behaviour.</summary>
        public const int WanderCivil    = 1001;

        /// <summary>Flee to a safe area in panic.</summary>
        public const int PanicFlee      = 1002;

        // ── Military behaviors (2001–2999) ───────────────────────────────────
        /// <summary>Escort a convoy along a waypoint route.</summary>
        public const int ConvoyEscort   = 2001;

        /// <summary>Standard infantry combat posture.</summary>
        public const int InfantryCombat = 2002;

        /// <summary>Set up an ambush and wait for trigger.</summary>
        public const int Ambush         = 2003;
    }
}
