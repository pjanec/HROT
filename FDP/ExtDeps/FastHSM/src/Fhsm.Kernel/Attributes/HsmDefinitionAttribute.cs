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

        // When true, signals that this asset uses an editor-managed companion blackboard file
        // (e.g. {AssetName}.Blackboard.cs). The runtime ignores this flag; it is read by the
        // HROT HSM editor. Default is false -- all existing assets are unaffected.
        public bool BlackboardManaged { get; set; }

        // When set, the source generator wires BehaviorIngressSystem to provision a
        // Blackboard1024 component for this behavior. Null means no heavy component is attached.
        // Default is null -- existing behavior is preserved.
        public Type? HeavyDtoType { get; set; }

        public HsmDefinitionAttribute(string machineName)
        {
            MachineName = machineName;
        }
    }
}
