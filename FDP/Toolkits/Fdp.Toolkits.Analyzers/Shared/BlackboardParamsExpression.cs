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
        /// The base of the params region: a <c>ref byte</c> at offset 0 of the blackboard's fixed
        /// buffer. The <c>[0]</c> is load-bearing — indexing is what takes the fixed buffer out of
        /// "unfixed expression" territory.
        /// </summary>
        internal static string Base(string blackboardExpr) => "ref " + blackboardExpr + ".BehaviorParameters[0]";

        /// <summary>
        /// A <c>ref byte</c> at <paramref name="byteOffset"/> inside the params region, ready to be
        /// wrapped in <c>Unsafe.As&lt;byte, TDto&gt;(…)</c> by the caller.
        /// </summary>
        internal static string At(string blackboardExpr, int byteOffset) =>
            "ref Unsafe.AddByteOffset(" + Base(blackboardExpr) + ", (nint)" + byteOffset + ")";
    }
}
