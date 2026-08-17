namespace Fdp.Toolkit.Behavior.Analyzers
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>E6</c>/<c>W9</c> — the ONE place an HSM action or guard id is computed.</b>
    ///
    /// <para>
    /// 📄 Plan §4d: the id is <i>"reconciled in lockstep via a shared resolver"</i>. ⛔ Before this,
    /// <see cref="HsmActionGenerator"/> computed it at <b>seven</b> call sites — the dispatcher's
    /// <c>ActionTable</c>, the dispatcher's <c>GuardTable</c>, and five in the registrar's
    /// <c>RegisterAll</c> — each spelling out the same FNV-1a. ⭐ <b>Two call sites each computing
    /// "the same" key is the duplication that produces a disagreement</b>, and this file is the fix
    /// for the mechanism regardless of which string the programme ultimately keys on.
    /// </para>
    ///
    /// <para>
    /// ⚠⚠ <b>The ALGORITHM is shared here; the KEY STRING is not yet, and that is a live question —
    /// see <see cref="ForActionName"/>.</b>
    /// </para>
    /// </summary>
    internal static class HsmActionKey
    {
        /// <summary>
        /// FNV-1a over UTF-16 code units, truncated to 16 bits.
        ///
        /// <para>
        /// ⚠⚠ <b>This is a MIRROR of <c>Fhsm.Compiler.HsmFlattener.ComputeHash</c>, and the mirror is
        /// forced:</b> the analyzer targets <c>netstandard2.0</c> and cannot reference the compiler
        /// assembly. ⇒ <b>the drift is the defect</b>, which is why
        /// <c>HsmActionIdAgreementTests</c> recomputes the flattener's answer independently and
        /// compares. Same shape as <c>BehaviorParameterSizeAnalyzer</c>'s inlined
        /// <c>MaxBehaviorParamByteSize</c>.
        /// </para>
        /// </summary>
        public static ushort Compute(string key)
        {
            uint hash = 2166136261;
            foreach (char c in key) { hash ^= c; hash *= 16777619; }
            return (ushort)(hash & 0xFFFF);
        }

        /// <summary>
        /// ⭐ The key string for a plain <c>[HsmAction]</c> / <c>[HsmGuard]</c> method.
        ///
        /// <para>
        /// 🔴🔴 <b>It is the SIMPLE name today, and that is measured to be wrong for one of the two
        /// shipped consumers.</b> An asset addresses an action by whatever string it stores, and the
        /// two shipped consumers store different forms:
        /// <list type="bullet">
        ///   <item><c>FDP/Examples</c> build their machines by hand and store the <b>simple name</b>
        ///   (<c>.Activity("Activity_Cruise")</c>) ⇒ agrees with this key.</item>
        ///   <item>🔴 <c>Hrot.AI.Behaviors</c>' <c>.hsm.json</c> assets store the <b>FQN</b>
        ///   (<c>Hrot.AI.Behaviors.CgfHsmNodes.StubIdle</c>, emitted verbatim by <c>HsmEmitCore</c>)
        ///   ⇒ the blob addresses <c>Compute(fqn)</c> while this registers <c>Compute(name)</c>, and
        ///   the two differ.</item>
        /// </list>
        /// </para>
        ///
        /// <para>
        /// ⛔ <b>Switching this to the FULL name is NOT a local fix</b> — it would invert the breakage
        /// onto the hand-built consumers. ⇒ the string is a <b>plan-level decision</b>, escalated
        /// rather than taken here; <c>HsmActionIdAgreementTests</c> pins the current answer so the
        /// decision is made against a measurement instead of a memory.
        /// </para>
        /// </summary>
        public static ushort ForActionName(string registeredName) => Compute(registeredName);

        /// <summary>The key for a <c>[SharedAiAction]</c>/<c>[SharedAiCondition]</c> entry.</summary>
        public static ushort ForCompoundKey(string compoundKey) => Compute(compoundKey);

        /// <summary>The key for the generated exit-cleanup peer of a channel-writing action.</summary>
        public static ushort ForExitCleanup(string registeredName) => Compute(ExitCleanupName(registeredName));

        /// <summary>
        /// ⭐ The exit-cleanup action's registered NAME. ⛔ It was spelled out as
        /// <c>"ExitCleanup_" + name</c> at four sites — two computing the id, two emitting the thunk —
        /// so the prefix now has one home too.
        /// </summary>
        public static string ExitCleanupName(string registeredName) => "ExitCleanup_" + registeredName;
    }
}
