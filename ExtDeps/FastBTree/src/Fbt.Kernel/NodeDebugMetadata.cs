namespace Fbt
{
    /// <summary>
    /// Per-node debug metadata captured at tree-build time (managed, not serialized).
    /// Placed in Fbt.Kernel (not Fbt.Compiler) to allow BehaviorTreeBlob to reference it
    /// without creating a circular dependency between Fbt.Kernel and Fbt.Compiler.
    /// </summary>
    public class NodeDebugMetadata
    {
        public string Label = string.Empty;
        public string SourceFile = string.Empty;
        public int LineNumber;
        public string CustomComment = string.Empty;
        /// <summary>Stable UUID correlating this node with the visual authoring tool.</summary>
        public string VisualId = string.Empty;
    }
}
