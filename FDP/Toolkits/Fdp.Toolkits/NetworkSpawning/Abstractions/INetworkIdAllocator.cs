using System;

namespace Fdp.Toolkit.NetworkSpawning
{
    public interface INetworkIdAllocator : IDisposable
    {
        long AllocateId();

        /// <summary>
        /// ⭐⭐ <b>After this returns, the next id issued is <paramref name="startId"/>.</b>
        /// <para>📌 <c>HN-037</c> stated this contract in terms of the OBSERVABLE, because it was not true of
        /// every implementation: the two pre-increment allocators returned <c>startId + 1</c>. They were
        /// corrected; <see cref="IWorldIdAuthority.ResetToBase"/> carries the measurement.</para>
        /// <para>⚠ <c>BlockIdManager</c> is the one production allocator that still cannot honour it — it is
        /// a POOL with no authority to seed from, so it clears and waits for a block. ⛔ It is not on any
        /// authored-id path; see its own remarks.</para>
        /// </summary>
        void Reset( long startId = 0 );
    }

    /// <summary>
    /// ⭐⭐⭐ <b>An allocator that can put its own issuing position back — the preview dry-run capability.</b>
    /// 📄 <c>docs/DESIGN_Deterministic_Network_Ids.md</c> §4c *(the user's chosen approach)* · §4b *(why
    /// <c>Reset(Read())</c> could not do this)*.
    ///
    /// <para>🔒 <b>User, `2026-08-23`:</b> <i>"each node needs to remember the ids/chunks used during the run
    /// and on world reset to simply reset to their beginning while the central allocatore stays where it is
    /// for potential fresh allocations"</i>.</para>
    ///
    /// <para>⛔⛔ <b>WHY THIS IS A SEPARATE INTERFACE AND NOT A MEMBER ON <see cref="INetworkIdAllocator"/>.</b>
    /// 📐 Measured: <b>13</b> implementations exist — 5 production, 8 test doubles. ⭐ Adding a member to the
    /// hot interface means 13 edits, and a C# default implementation would be a <b>silent default</b> on
    /// every double *(the pattern <c>CLAUDE.md</c> forbids)*. ⇒ ⭐⭐ a capability interface the <b>five
    /// production allocators</b> implement, which the preview bracket type-tests and — crucially —
    /// <b>REPORTS</b> when absent.</para>
    ///
    /// <para>⛔⛔⛔ <b>AND WHY IT IS NOT <c>Reset(Capture())</c>.</b> §4b measured that identity to be
    /// impossible for the two pooled allocators: <c>BlockIdManager.Reset</c> clears the pool and <b>ignores
    /// its argument</b>, and <c>DdsIdAllocator.Reset</c> writes a <b>global <c>Req_Reset</c></b> that flushes
    /// every client's pool and moves the cluster's high-water mark <b>FORWARD</b>. ⇒ ⭐ using <c>Reset</c> to
    /// restore a preview would drag the whole cluster backward. 📌 <b>This capability never talks to a
    /// central authority.</b></para>
    ///
    /// <para>⭐⭐ <b>One concept, both shapes.</b> A scalar allocator's position is an integer; a pooled one's
    /// is the queue of ids it already holds. ⇒ <i>"restore my own issuing position"</i> covers all five, which
    /// is what <c>Reset(Read())</c> could not express.</para>
    /// </summary>
    public interface IRestorableIdAllocator
    {
        /// <summary>
        /// ⭐ Captures this allocator's issuing position, or <see langword="null"/> when it has none to give.
        /// <para>⚠ <b><c>null</c> is a legitimate answer</b> — a pooled allocator with an empty pool, or one
        /// whose state cannot be restored without involving the central authority, must say so. ⛔ Returning
        /// a fake token would make a non-reproducible preview look reproducible.</para>
        /// </summary>
        object? CaptureIssuingPosition();

        /// <summary>
        /// ⭐⭐ Restores the position captured earlier.
        /// <para>⛔⛔ <b>THE BOUNDARY, and it must not be crossed silently</b> *(§4c)*: an implementation
        /// restores <b>only what it held at capture</b>. ⚠ Ids obtained MID-preview are spent from the
        /// cluster's point of view — the central authority's high-water mark advanced past them and may
        /// have handed that range to another node ⇒ 🔴 <b>re-issuing them is a cross-node collision.</b>
        /// ⇒ ⭐ the guarantee is: <i>exact repetition while a preview stays within the ids already held;
        /// past that, the prefix repeats and the tail legitimately differs.</i></para>
        /// </summary>
        void RestoreIssuingPosition(object snapshot);
    }
}
