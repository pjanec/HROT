using System;

namespace Fdp.Toolkit.Utility
{
    /// <summary>
    /// Marks a static method as a named Utility AI input reader.
    /// The <see cref="Name"/> is used by the Phase 2 source generator to derive
    /// the FNV-1a-16 identifier registered in <see cref="UtilityInputReaderStore"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class UtilityInputAttribute : Attribute
    {
        /// <summary>The canonical input name; must match the corresponding <c>StandardInputIds</c> constant.</summary>
        public string Name { get; }

        public UtilityInputAttribute(string name) { Name = name; }
    }
}
