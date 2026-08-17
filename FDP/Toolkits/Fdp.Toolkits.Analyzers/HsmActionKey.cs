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
        /// ⭐⭐⭐ The key for a plain <c>[HsmAction]</c> / <c>[HsmGuard]</c> method: its
        /// <b>FULLY QUALIFIED name</b>, <c>{ContainingType.FullName}.{MethodName}</c>.
        ///
        /// <para>
        /// ⭐⭐ <b>COORDINATOR RULING <c>2026-08-17</c> — option (A), FQN everywhere</b> (plan §4A6).
        /// Batch 71 measured that the id disagreed across three sites: the analyzer hashed the SIMPLE
        /// name at both of its sites, while <c>Fhsm.Compiler.HsmFlattener</c> hashes whatever string
        /// the ASSET stored — and <c>HsmEmitCore</c> stores the FQN. 🔴 The shipped HSM entry actions
        /// therefore never dispatched: <c>ExecuteAction</c> was a <c>TryGetValue</c> miss, silently.
        /// </para>
        ///
        /// <para>
        /// ⛔ <b>(B) — making the asset store the simple name — was rejected</b>, because it leaves
        /// <c>W9</c>/<c>E6</c> unfixed <b>and</b> puts the collision into the FILE FORMAT. ⭐ (A)'s
        /// breakage is four call sites in example projects, and they fail at COMPILE time.
        /// </para>
        ///
        /// <para>
        /// ⚠ <b><c>[HsmAction(Name = "…")]</c> no longer changes the id.</b> The identity is the
        /// method, and the FQN names the method; a display-name override renaming an identity is the
        /// ambiguity this ruling removes. ⛔ Two methods with the same simple name in different types
        /// now get <b>distinct</b> ids, which is <c>E6</c>'s whole point.
        /// </para>
        /// </summary>
        public static ushort ForActionName(string fullyQualifiedName) => Compute(fullyQualifiedName);

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
