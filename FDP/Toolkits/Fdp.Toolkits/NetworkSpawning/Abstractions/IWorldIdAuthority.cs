namespace Fdp.Toolkit.NetworkSpawning
{
    /// <summary>
    /// ⭐⭐⭐ <b>The ONE id authority a world has, and the only thing anyone may say to it at a world
    /// boundary.</b> 📄 <c>docs/DESIGN_Deterministic_Network_Ids.md</c> §11 *(<c>HN-037</c>)*.
    ///
    /// <para>🔒 <b>User, `2026-08-24`:</b> <i>"there should be one single allocation path in both [edit and
    /// live] cases. Editor is no exception… both should use same allocator that resets to initial value
    /// (1000 for the first entity allocated) whenever whole 'world' resets."</i></para>
    ///
    /// <para>⭐⭐ <b>Why a capability interface and not a member on <see cref="INetworkIdAllocator"/>.</b> Same
    /// reasoning as <see cref="IRestorableIdAllocator"/>, and one more that is specific to this: the
    /// authority in <c>--mode all</c> is <b>not an allocator at all</b> — it is the
    /// <c>DdsIdAllocatorServer</c> the orchestrator hosts, which no node holds an
    /// <see cref="INetworkIdAllocator"/> for. ⇒ ⛔ a member on the allocator interface could not express the
    /// cluster case, which is the case the whole design exists for.</para>
    ///
    /// <para>⛔⛔ <b>THE GUARD, and it is the entire safety argument</b> *(§11b)*: this is reachable ONLY from
    /// the world-reset / scenario-load path, where the world has just been cleared. ⚠ Fired mid-exercise it
    /// is <b>catastrophic</b> — it fights <c>mgmt-1</c> §5.7's forward high-water mark and clobbers live
    /// pools. ⇒ ⭐ resetting BACKWARD is safe here and nowhere else, because nothing survives the boundary
    /// to collide with. 📌 Preview is the opposite case and deliberately does NOT use this — it does not
    /// clear the world, so it restores its own pool locally *(<see cref="IRestorableIdAllocator"/>, §4d)*.
    /// </para>
    /// </summary>
    public interface IWorldIdAuthority
    {
        /// <summary>
        /// ⭐⭐⭐ After this returns, <b>the next id the authority issues is <paramref name="firstId"/></b>.
        ///
        /// <para>⚠⚠ <b>That sentence is the contract, and it was NOT true of every implementation before
        /// <c>HN-037</c>.</b> 📐 Measured `2026-08-24`, <c>Reset(1000)</c> across the five production
        /// allocators gave <b>three</b> different answers: <c>1000</c> *(the editor's nested allocator,
        /// <c>DdsIdAllocator</c>)*, <c>1001</c> *(<c>Hrot.Core.SequentialIdAllocator</c>,
        /// <c>IgSequentialIdAllocator</c> — both pre-increment)*, and <c>throw</c> *(<c>BlockIdManager</c>,
        /// which clears its pool and ignores the argument)*. ⇒ ⛔ <i>"one single allocation path"</i> cannot
        /// be true while the reset means three things, so the contract is stated here in terms of the
        /// OBSERVABLE — the next id — rather than in terms of a counter no two implementations agree on.
        /// </para>
        /// </summary>
        void ResetToBase(long firstId);
    }

    /// <summary>
    /// ⭐ The world boundary's constants and the adapter from a plain
    /// <see cref="INetworkIdAllocator"/> to <see cref="IWorldIdAuthority"/>.
    /// </summary>
    public static class WorldIdAuthority
    {
        /// <summary>
        /// ⭐⭐⭐ <b>The first id every world hands out, on every host.</b> 🔒 User, `2026-08-24`:
        /// <i>"1000 for the first entity allocated"</i>.
        ///
        /// <para>⭐ It is a constant and not a per-host setting on purpose: the reproducible 1000-block and
        /// cross-host id parity are the SAME property, and they fall out of one number *(§11b)*.</para>
        /// </summary>
        public const long WorldBase = 1000;

        /// <summary>
        /// ⭐ Adapts an allocator that IS its world's authority — the editor's offline one-node case.
        /// <para>⛔ Do NOT wrap a pooled CLIENT of a central authority with this *(a
        /// <c>DdsIdAllocator</c>)*: its <c>Reset</c> is a cluster-wide <c>Req_Reset</c> broadcast, so the
        /// node holding the client would be silently speaking for the whole cluster. ⭐ In the cluster the
        /// authority is the server, and it implements this interface itself.</para>
        /// </summary>
        public static IWorldIdAuthority FromAllocator(INetworkIdAllocator allocator) =>
            new AllocatorAuthority(allocator);

        private sealed class AllocatorAuthority : IWorldIdAuthority
        {
            private readonly INetworkIdAllocator _allocator;
            public AllocatorAuthority(INetworkIdAllocator allocator) => _allocator = allocator;

            // ⭐ Relies on the UNIFORM Reset contract (see ResetToBase's remarks). The two pre-increment
            //   allocators were corrected to match it rather than being special-cased here — a per-caller
            //   adjustment is exactly the silent divergence HN-037 already was.
            public void ResetToBase(long firstId) => _allocator.Reset(firstId);
        }
    }
}
