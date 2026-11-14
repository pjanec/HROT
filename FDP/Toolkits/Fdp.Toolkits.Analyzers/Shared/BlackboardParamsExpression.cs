namespace Fdp.Toolkit.Behavior.Shared
{
    /// <summary>
    /// THE one spelling of "the DTO field at a baked byte offset inside the entity's params region".
    ///
    /// <para><b>Why this file exists.</b> Four emitters produced this expression independently —
    /// <c>BTreeActionGenerator</c>, <c>HsmActionGenerator</c> (both in <c>Fdp.Toolkits.Analyzers</c>)
    /// and <c>BTreeBridgeEmitCore</c>, <c>HsmBridgeEmitCore</c> (in <c>Hrot.AiEditor.Persistence</c>) —
    /// in <b>three</b> different spellings, and one of them was wrong:
    /// <c>BTreeActionGenerator</c> emitted <c>ref bb.BehaviorParameters</c> without the <c>[0]</c>.
    /// <c>BehaviorParameters</c> is a <c>fixed byte[100]</c> buffer, so that is <c>CS1666</c> — the
    /// generated registrar did not compile the moment an assembly had both a <c>[BTreeAction]</c> and
    /// a <c>[SharedAiAction]</c> (<c>BP-306</c>).</para>
    ///
    /// <para><b>One home, not a mirror.</b> This file is compiled into
    /// <c>Fdp.Toolkits.Analyzers</c> and <b>linked</b> into <c>Hrot.AiEditor.Persistence</c> — both are
    /// <c>netstandard2.0</c>, and a linked source file crosses the wall that an assembly reference
    /// cannot (the analyzer must not appear in a shipped emitter's dependency graph). It is
    /// <c>internal</c> on both sides, so a project referencing both assemblies sees neither copy and
    /// no <c>CS0436</c> arises.</para>
    ///
    /// <para>⛔ Do not re-inline the text. The duplication is the defect this file removes; the same
    /// shape produced <c>E6</c>'s compound key and <c>HsmActionKey</c>'s two spellings before it.</para>
    /// </summary>
    internal static class BlackboardParamsExpression
    {
        /// <summary>
        /// ⭐⭐⭐ <b>The base of the params region: a <c>ref byte</c> at the start of the entity's ROOT
        /// PARAMS OCCURRENCE SLOT.</b>
        ///
        /// <para>🔴 <b><c>P3-C</c> (<c>2026-09-21</c>) moved this.</b> It used to be
        /// <c>ref {bb}.BehaviorParameters[0]</c> — offset 0 of a per-entity <c>BrainBlackboard</c>
        /// component, whose <c>[0]</c> was load-bearing only because indexing takes a <c>fixed</c>
        /// buffer out of "unfixed expression" territory. ⇒ the component is retired and the region
        /// now lives in the occurrence store, keyed by the behaviour.</para>
        ///
        /// <para>⭐⭐ <b>The OFFSET ARITHMETIC IS UNCHANGED, deliberately</b> (§29.6). Every baked
        /// <c>byteOffset</c> indexes INTO the same packed variable table it always did — only the
        /// anchor moved. ⛔ That is why this is a one-line change per emitter and not a rewrite of
        /// <c>E3b-0</c>'s seed offsets.</para>
        ///
        /// <para>⚠ <b>The parameters changed shape, and they had to.</b> The old form took the name of
        /// a blackboard local the thunk already held; the new anchor is resolved from
        /// <c>(world, entity)</c>, which every emitted thunk has in a DIFFERENT spelling
        /// (<c>ctx.World</c>/<c>ctx.Self</c>, <c>repo</c>/<c>bridge-&gt;Self</c>, …). ⇒ the caller
        /// supplies both expressions; the projection text still lives only here.</para>
        /// </summary>
        internal static string Base(string worldExpr, string selfExpr) =>
            "ref global::Fdp.Toolkit.Behavior.RootParamsAccess.RootRef(" + worldExpr + ", " + selfExpr + ")";

        /// <summary>
        /// A <c>ref byte</c> at <paramref name="byteOffset"/> inside the params region, ready to be
        /// wrapped in <c>Unsafe.As&lt;byte, TDto&gt;(…)</c> by the caller.
        /// </summary>
        internal static string At(string worldExpr, string selfExpr, int byteOffset) =>
            "ref Unsafe.AddByteOffset(" + Base(worldExpr, selfExpr) + ", (nint)" + byteOffset + ")";

        /// <summary>
        /// ⭐⭐⭐ <c>CE-431</c> — <b>the field at <paramref name="byteOffset"/> inside the RUNNING
        /// BEHAVIOUR's block</b>, projected from the thunk's own <c>ref byte</c> blackboard argument.
        ///
        /// <para>🔴 <see cref="At"/> projects from the ENTITY's root block, which is right for a root
        /// behaviour and WRONG for a hosted subtree: the child read its host's bytes whatever it was
        /// ticked with. ⭐ The interpreter threads <c>bb</c> by <c>ref</c>, the root tick passes the root
        /// block as <c>bb</c> and a hosted child is ticked with its OWN block ⇒ this is the same bytes
        /// for a root and the right bytes for a child.</para>
        ///
        /// <para>⛔ <c>BehaviorBlock.Require</c> throws when <c>bb</c> is the "no block" sentinel, so a
        /// projection on a params-less behaviour fails loudly — as <c>RootRef</c> did — instead of
        /// reading the stack.</para>
        ///
        /// <para>⚠ <b>Only a <c>byte</c> blackboard is a block.</b> A hand-written tree with a TYPED
        /// blackboard struct is not ticked through the brain system, so its <c>bb</c> is not the block;
        /// there the root projection is kept, unchanged.</para>
        /// </summary>
        internal static string AtBlock(string bbExpr, string bbType, string worldExpr, string selfExpr, int byteOffset) =>
            IsByteBlackboard(bbType)
                ? "ref Unsafe.AddByteOffset(ref global::Fdp.Toolkit.Behavior.BehaviorBlock.Require(ref " + bbExpr + "), (nint)" + byteOffset + ")"
                : At(worldExpr, selfExpr, byteOffset);

        /// <summary>⭐ The block base itself — <c>ref byte</c> — for a whole-block struct projection.</summary>
        internal static string BlockBase(string bbExpr) =>
            "ref global::Fdp.Toolkit.Behavior.BehaviorBlock.Require(ref " + bbExpr + ")";

        private static bool IsByteBlackboard(string bbType) =>
            bbType == "byte" || bbType == "global::System.Byte" || bbType == "System.Byte";

        /// <summary>
        /// ⭐ The same, with the offset given as an EXPRESSION rather than a constant — the seed paths,
        /// where <c>E3b-0</c>'s per-state offset is only known at dispatch.
        /// </summary>
        internal static string AtExpr(string worldExpr, string selfExpr, string offsetExpr) =>
            "ref Unsafe.AddByteOffset(" + Base(worldExpr, selfExpr) + ", (nint)(" + offsetExpr + "))";
    }
}
