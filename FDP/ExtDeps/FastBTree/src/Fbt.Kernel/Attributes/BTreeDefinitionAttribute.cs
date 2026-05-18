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

        public BTreeDefinitionAttribute(string treeName)
        {
            TreeName = treeName;
        }
    }
}
