namespace Fdp.Toolkit.Blueprints.Partitioning
{
    /// <summary>
    /// S3-G (stage 2): scope of a code-authored stateful working-state variable. Mirrors the editor's
    /// <c>WorkingStateScope</c> byte values (0 = Node, 1 = Behavior, 2 = Entity) so a code-built slot key
    /// is byte-identical to the one the JSON emitter bakes for the same (asset, variable, scope). It governs
    /// how <c>BlueprintBlackboardPartitions</c> slots are keyed and provisioned, which is why it lives here
    /// beside the partition allocator rather than in the Behavior authoring namespace (whose IDL module would
    /// also collide with the <c>Behavior</c> enumerator under CycloneDDS codegen).
    /// </summary>
    public enum StatefulSlotScope : byte
    {
        /// <summary>Per-node local slot (key folds in the node's visual id). Default in the editor.</summary>
        Node     = 0,
        /// <summary>Shared by every node in one asset that binds the same variable.</summary>
        Behavior = 1,
        /// <summary>Shared across behaviours on an entity (variable id only; asset id excluded).</summary>
        Entity   = 2,
    }
}
