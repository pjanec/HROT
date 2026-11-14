namespace Fdp.Toolkit.Replication
{
    /// <summary>
    /// Host-capability tokens owned by the replication toolkit — the FEATURE tokens a host advertises on the
    /// durable <c>NodeCapabilities</c> descriptor beside its <c>fdp.role.*</c> tokens (AQ-70 §Q70-B).
    ///
    /// <para>⭐ The capability facility is <b>general</b> and models the OpenGL extension registry: a host
    /// advertises a set of namespaced tokens, a consumer tests membership, unknown tokens are ignored, absence =
    /// unsupported. <c>fdp.reliable-init</c> is its first feature token — a host that advertises it participates
    /// in the cross-node construction barrier; a host that does not is never waited for (graceful degradation,
    /// §3c ①). Role tokens (<c>fdp.role.*</c>) are the bit-backed subset and live in
    /// <see cref="Fdp.Core.NodeRoleTokens"/>.</para>
    /// </summary>
    public static class CapabilityTokens
    {
        /// <summary>A host advertising this supports the reliable-init construction barrier: it replies to a
        /// <c>WaitForAcks</c> <c>EntityMaster</c> (publishing <c>EntityLifecycleStatusDescriptor</c>). A creator
        /// includes only nodes advertising this token in its wait-set (§3b.1 / §3c ①).</summary>
        public const string ReliableInit = "fdp.reliable-init";
    }
}
