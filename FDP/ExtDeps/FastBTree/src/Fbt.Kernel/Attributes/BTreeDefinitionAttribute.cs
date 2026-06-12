using System;

namespace Fbt
{
    /// <summary>
    /// Marks a static method returning a BTreeBuilder or BehaviorTreeBlob as a named tree
    /// to be auto-catalogued by the Fbt.SourceGen source generator.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class BTreeDefinitionAttribute : Attribute
    {
        /// <summary>The logical name of the behavior tree (used as the catalog key).</summary>
        public string TreeName { get; }

        /// <summary>Stable editor asset GUID (8-4-4-4-12). Set by the editor codegen; null for hand-authored.</summary>
        public string? AssetId { get; set; }

        /// <summary>
        /// When true, signals that this asset uses an editor-managed companion blackboard file
        /// (e.g. {AssetName}.Blackboard.cs). The runtime ignores this flag; it is read by the
        /// HROT BTree editor. Default is false -- all existing assets are unaffected.
        /// </summary>
        public bool BlackboardManaged { get; set; }

        /// <summary>
        /// When set, the source generator wires BehaviorIngressSystem to provision a
        /// Blackboard1024 component for this behavior. Null means no heavy component is attached.
        /// Default is null -- existing behavior is preserved.
        /// </summary>
        public Type? HeavyDtoType { get; set; }

        public BTreeDefinitionAttribute(string treeName)
        {
            TreeName = treeName;
        }
    }
}
