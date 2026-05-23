using System;

namespace Fhsm.Kernel.Attributes
{
    // Marks a static method returning HsmDefinitionBlob as a named HSM asset
    // to be catalogued by the HSM asset contributor.
    // Method must be static, return HsmDefinitionBlob, and have zero parameters.
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class HsmDefinitionAttribute : Attribute
    {
        // The logical name of the state machine (used as the catalog key).
        public string MachineName { get; }

        // Optional stable asset GUID; used for identity across renames.
        // If null, the asset ID is derived from MachineName via FNV-1a-32.
        public string? AssetId { get; set; }

        public HsmDefinitionAttribute(string machineName)
        {
            MachineName = machineName;
        }
    }
}
