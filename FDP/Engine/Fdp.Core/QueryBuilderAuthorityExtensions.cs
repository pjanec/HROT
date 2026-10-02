namespace Fdp.Core
{
    /// <summary>
    /// ⭐⭐⭐ <b>Conditional authority filtering — <i>"require <typeparamref name="T"/>, and require that I
    /// OWN it, but only once this node has been told what it owns."</i></b>
    ///
    /// <para>📄 <c>docs/DESIGN_Role_Affinity_Ownership.md</c> §3.5, §6 step <c>3b</c>.</para>
    /// </summary>
    public static class QueryBuilderAuthorityExtensions
    {
        /// <summary>
        /// ⭐ <c>gate == false</c> ⇒ exactly <see cref="QueryBuilder.With{T}"/>;
        /// <c>gate == true</c> ⇒ <see cref="QueryBuilder.WithOwned{T}"/>.
        ///
        /// <para>⛔⛔ <b>WHY THIS EXISTS AT ALL, AND IT IS NOT STYLE.</b> Adding
        /// <c>WithOwned&lt;T&gt;()</c> unconditionally is <b>NOT</b> a no-op on a cluster that has not yet
        /// been given role policies. 📐 Measured: a promoted ghost owns <b>nothing</b> until something
        /// grants it authority, so an unconditional filter makes a node <b>stop processing every entity it
        /// did not create itself</b>. ⚠ That is the exact shape of the failure this design opens with
        /// (<c>CE-256</c>: <i>"owns nothing, so nothing it is responsible for ever moves"</i>) — so
        /// shipping the gate ahead of the policies would reproduce the bug while fixing it.</para>
        ///
        /// <para>⭐⭐ <b>The gate therefore follows the POLICY, not the calendar.</b> A host that has been
        /// handed an <c>IRoleAffinityPolicy</c> has, by that act, said <i>"I know which components are
        /// mine"</i> — and only then does <i>"do not touch what is not mine"</i> mean anything. ⇒ the same
        /// opt-in discipline that made the two ownership insertion points safe to ship early.</para>
        ///
        /// <para>⭐ <b>Gate on a component the caller ALREADY requires.</b> Because this is
        /// <c>With&lt;T&gt;</c> when the gate is off, passing a component the query did not previously
        /// demand would silently NARROW the matched set even with the gate off — a behaviour change
        /// wearing a feature flag. ⛔ Pick <typeparamref name="T"/> from the query's existing
        /// <c>With</c> clauses.</para>
        /// </summary>
        /// <param name="builder">The query being built.</param>
        /// <param name="gate">Whether this node is gating execution on authority.</param>
        public static QueryBuilder WithOwnedWhen<T>(this QueryBuilder builder, bool gate) where T : unmanaged
            => gate ? builder.WithOwned<T>() : builder.With<T>();
    }
}
