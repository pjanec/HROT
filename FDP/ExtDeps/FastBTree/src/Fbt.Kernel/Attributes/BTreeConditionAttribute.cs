using System;

namespace Fbt
{
    /// <summary>
    /// Marks a static method as an auto-registrable BTree condition delegate.
    /// Used by BTreeSchemaExporter and the Fbt.SourceGen source generator.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class BTreeConditionAttribute : Attribute { }
}
