using System;

namespace Fdp.Toolkit.Tkb.Attributes
{
    /// <summary>
    /// Marks a class or struct as a TKB descriptor DTO, binding it to a hierarchical name
    /// that matches the JSON property key used in entity files.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct,
                    Inherited = false, AllowMultiple = false)]
    public sealed class TkbDescriptorAttribute : Attribute
    {
        /// <summary>
        /// The hierarchical descriptor name as it appears in JSON entity files
        /// (e.g. "TkbMaster", "Gen.VehicleParameters").
        /// Must not be null, empty, or whitespace.
        /// Must not contain '#' — the '#PartId' postfix is a runtime instance delimiter
        /// and must not appear in schema-level names.
        /// </summary>
        public string HierarchicalName { get; }

        /// <summary>
        /// Initializes a new <see cref="TkbDescriptorAttribute"/>.
        /// </summary>
        /// <param name="hierarchicalName">The descriptor name as it appears in JSON.</param>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="hierarchicalName"/> is null, empty, whitespace,
        /// or contains a '#' character.
        /// </exception>
        public TkbDescriptorAttribute(string hierarchicalName)
        {
            if (string.IsNullOrWhiteSpace(hierarchicalName))
                throw new ArgumentException(
                    "TkbDescriptor hierarchicalName must not be null or whitespace.",
                    nameof(hierarchicalName));

            if (hierarchicalName.Contains('#'))
                throw new ArgumentException(
                    "'#PartId' is a runtime instance delimiter and must not appear in " +
                    "schema-level descriptor names. Remove the '#' from the name.",
                    nameof(hierarchicalName));

            HierarchicalName = hierarchicalName;
        }
    }
}
