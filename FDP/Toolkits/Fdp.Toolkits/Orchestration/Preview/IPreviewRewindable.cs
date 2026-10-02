namespace Fdp.Toolkit.Orchestration.Preview
{
    /// <summary>
    /// ⭐⭐⭐ <b>One piece of state a preview must put back.</b>
    /// 📄 <c>docs/DESIGN_Deterministic_Network_Ids.md</c> §2b *(the enumeration)* · §4 ⑤ · §4c *(the chosen
    /// approach)*.
    ///
    /// <para>⛔⛔ <b>Why this exists at all.</b> 📐 A preview rewinds the world by
    /// <c>liveRepo.SyncFrom(snapshot)</c> — <b>and nothing else</b>. ⇒ every mutable thing that lives
    /// OUTSIDE the <c>EntityRepository</c> survives the rewind, and §2b enumerated <b>three</b> of them:
    /// the id allocator, <c>NetworkEntityMap</c> and <c>EntityLifecycleModule</c>'s pending queues.</para>
    ///
    /// <para>⭐⭐ <b>Three is why this is a LIST and not a hard-coded pair.</b> 📌 §4 ⑤ ruled: <i>"do not reach
    /// for a general vocabulary until ⓪ says how many participants there are — two justifies a small list;
    /// one does not."</i> ⇒ ⭐ the enumeration answered THREE, so the list is justified — ⛔ and it stays a
    /// small explicit list, not a discovery mechanism.</para>
    ///
    /// <para>⭐⭐⭐ <b>The capture is OPAQUE on purpose.</b> Each participant owns its own invariants, so it
    /// returns its own token and only it can interpret it. ⛔ A shared "preview state" DTO would put three
    /// subsystems' internals in one type and make the next participant a breaking change.</para>
    /// </summary>
    public interface IPreviewRewindable
    {
        /// <summary>⭐ For diagnostics and for the rail's per-participant assertion. Short and stable.</summary>
        string Name { get; }

        /// <summary>
        /// ⭐ Captures whatever this participant must have restored, or <see langword="null"/> when it
        /// cannot express a restorable position.
        /// <para>⚠ <b><c>null</c> is a real answer, not a failure</b> — §4c: a pooled allocator that has
        /// no snapshot to give must SAY SO, so the bracket can report that this node cannot guarantee
        /// reproducibility. ⛔ Returning a fake token would be the silent-default defect.</para>
        /// </summary>
        object? Capture();

        /// <summary>
        /// ⭐ Puts back what <see cref="Capture"/> returned. ⛔ Never called with a <see langword="null"/>
        /// capture — the bracket skips those and reports them instead.
        /// </summary>
        void Restore(object snapshot);
    }
}
