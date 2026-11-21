namespace Fbt.Runtime
{
    /// <summary>
    /// CE-365 -- how a <c>NodeType.Subtree</c> node reaches whatever runs the hosted child.
    /// See DESIGN_Occurrence_Scoped_Storage.md §33.
    ///
    /// <para>
    /// The kernel DECLARES this and a host assembly IMPLEMENTS it, so the kernel never learns what
    /// an occurrence slot is. Without a host, a Subtree node keeps returning Failure exactly as it
    /// did before -- the arm is opt-in.
    /// </para>
    ///
    /// <para>
    /// WHY THE KERNEL AND NOT A GENERATED THUNK. A thunk is something an EMITTER writes, and a
    /// hand-written C# tree has no emitter -- BTreeDefinitionGenerator produces only a
    /// Get&lt;Name&gt;() catalog. Dispatching in the kernel is the only route that serves
    /// editor-authored and hand-written trees identically, because both end up as a compiled
    /// BehaviorTreeBlob. It also keeps the node a Subtree in the blob, so the editor projector and
    /// every debug surface keep working.
    /// </para>
    ///
    /// <para>
    /// The SITE is identified by (blob, nodeIndex): the host resolves its own per-site key from
    /// those. NodeDefinition carries no visual id, and a Subtree node's PayloadIndex indexes
    /// SubtreeAssetIds rather than MethodNames, so the node index is the only identity the kernel
    /// can hand over.
    /// </para>
    /// </summary>
    public interface ISubtreeHost<TBlackboard, TContext>
        where TBlackboard : struct
        where TContext : struct, IAIContext, ITreeTracer
    {
        /// <summary>
        /// Ticks the child hosted at <paramref name="nodeIndex"/>. The returned status becomes the
        /// Subtree node's own status.
        /// </summary>
        NodeStatus Tick(
            ref TBlackboard blackboard,
            ref TContext context,
            BehaviorTreeBlob blob,
            int nodeIndex);

        /// <summary>
        /// CE-365 / F14 -- the host left the Subtree node while its child may still be Running.
        ///
        /// <para>
        /// This is NOT reachable through the ordinary deactivator path, and that is the subtle half
        /// of this arm: <c>SweepExitedNode</c> resolves a deactivator by
        /// <c>_blob.MethodNames[PayloadIndex]</c>, but a Subtree node's PayloadIndex indexes
        /// <c>SubtreeAssetIds</c> -- so that lookup reads the WRONG ARRAY for it. Without this hook
        /// the child's cursor survives the abandonment and the next entry resumes mid-tree.
        /// </para>
        /// </summary>
        void Reset(ref TContext context, BehaviorTreeBlob blob, int nodeIndex);
    }
}
